using System.Collections.Generic;
using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private long IntPow(int x, int y)
    {
        long result = 1;
        for (var i = 0; i < y; i++)
        {
            result *= x;
        }
        return result;
    }
    
    private void AssertAllowanceAndBalance(string symbol, long amount)
    {
        var allowance = State.TokenContract.GetAllowance.Call(new GetAllowanceInput
        {
            Spender = Context.Self,
            Owner = Context.Sender,
            Symbol = symbol
        });
        Assert(allowance.Allowance >= amount, "Allowance not enough");
        var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput()
        {
            Owner = Context.Sender,
            Symbol = symbol
        });
        Assert(balance.Balance >= amount, "Balance not enough");
    }
    
    private bool CheckAllowanceAndBalance(Address owner, string symbol, long amount, out ReasonType reasonType, Dictionary<Address, long> userBalanceUsedMap = null)
    {
        reasonType = ReasonType.Expired;
        var amountUsed = userBalanceUsedMap?.GetValueOrDefault(owner, 0) ?? 0;
        amount += amountUsed;
        var allowance = State.TokenContract.GetAllowance.Call(new GetAllowanceInput
        {
            Spender = Context.Self,
            Owner = owner,
            Symbol = symbol
        });
        if (allowance.Allowance < amount)
        {
            reasonType = ReasonType.AllowanceNotEnough;
            return false;
        }
        var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput()
        {
            Owner = owner,
            Symbol = symbol
        });
        if (balance.Balance < amount)
        {
            reasonType = ReasonType.BalanceNotEnough;
            return false;
        }
        return true;
    }
    
    private void UpdatePriceBook(string symbolIn, string symbolOut, long price)
    {
        var headerPriceBookId = State.HeaderPriceBookIdMap[symbolIn][symbolOut];
        if (headerPriceBookId == 0)
        {
            headerPriceBookId = State.LastPriceBookId.Value.Add(1);
            State.LastPriceBookId.Value = headerPriceBookId;
            State.HeaderPriceBookIdMap[symbolIn][symbolOut] = headerPriceBookId;
        }

        var headerPriceBook = State.PriceBookMap[headerPriceBookId];
        if (headerPriceBook == null)
        {
            headerPriceBook = new PriceBook
            {
                PriceBookId = headerPriceBookId,
                PriceList = new PriceList
                {
                    Prices = { price }
                }
            };
            State.PriceBookMap[headerPriceBookId] = headerPriceBook;
            return;
        }
        var priceBook = FindPriceBook(headerPriceBook, price);
        InsertIntoPriceBook(priceBook, price);
        if (priceBook.PriceList.Prices.Count > State.OrderBookConfig.Value.MaxPricesEachPriceBook)
        {
            ExpandPriceBook(priceBook);
        }
    }

    private void InsertIntoPriceBook(PriceBook priceBook, long price)
    {
        var prices = priceBook.PriceList.Prices;
        var left = 0;
        var right = prices.Count - 1;
        
        while (left <= right)
        {
            var mid = left + (right - left) / 2;
            if (prices[mid] == price)
            {
                return;
            }
            if (prices[mid] < price)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }
        prices.Insert(left, price);
    }

    private void ExpandPriceBook(PriceBook priceBook)
    {
        var spiltIndex = priceBook.PriceList.Prices.Count / 2;
        var newPriceBookId = State.LastPriceBookId.Value.Add(1);
        State.LastPriceBookId.Value = newPriceBookId;
        var newPriceBook = new PriceBook
        {
            PriceBookId = newPriceBookId,
            NextPagePriceBookId = priceBook.NextPagePriceBookId,
            PriceList = new PriceList
            {
                Prices = { priceBook.PriceList.Prices.Skip(spiltIndex) }
            }
        };
        State.PriceBookMap[newPriceBook.PriceBookId] = newPriceBook;
        priceBook.NextPagePriceBookId = newPriceBook.PriceBookId;
        priceBook.PriceList = new PriceList
        {
            Prices = {priceBook.PriceList.Prices.Take(spiltIndex)}
        };
    }

    private PriceBook FindPriceBook(PriceBook headerPriceBook, long price)
    {
        var priceBook = headerPriceBook;
        var nextPriceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
        for (var i = 0; i < State.OrderBookConfig.Value.MaxPriceBooksEachTradePair - 1; i++)
        {
            if (nextPriceBook == null || price < nextPriceBook.PriceList.Prices[0])
            {
                return priceBook;
            }
            priceBook = nextPriceBook;
            nextPriceBook = State.PriceBookMap[nextPriceBook.NextPagePriceBookId];
        }
        Assert(nextPriceBook == null || nextPriceBook.PriceList.Prices[0] > price, "Too many price in this tradePair.");
        return priceBook;
    }

    private int GetTokenDecimal(string symbol)
    {
        return State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
        {
            Symbol = symbol
        }).Decimals;
    }
    
    /*
     * price = 10^8 * (amountOut / 10^decimalOut ) / (amountIn/10^decimalIn)
     * equivalent to
     * price = 10^(8+decimalIn-decimalOut) * amountOut / amountIn
     */
    private void CalculatePrice(string symbolIn, string symbolOut, long amountIn, long amountOut, bool erasePlaceDecimal, out long price, out long realAmountOut)
    {
        var symbolInDecimal = GetTokenDecimal(symbolIn);
        var symbolOutDecimal = GetTokenDecimal(symbolOut);
        var amountOutBigIntValue = new BigIntValue(amountOut);
        var multiple = IntPow(10, 8 + symbolInDecimal - symbolOutDecimal);
        var weightPrice = amountOutBigIntValue.Mul(multiple)
            .Div(amountIn);
        if (!long.TryParse(weightPrice.Value, out price))
        {
            throw new AssertionException($"Failed to parse {weightPrice.Value}");
        }
        if (erasePlaceDecimal)
        {
            var erasePriceMultiple = State.OrderBookConfig.Value.ErasePriceMultiple;
            price = price / erasePriceMultiple * erasePriceMultiple;
        }

        var realAmountOutStr =
            new BigIntValue(amountIn).Mul(price).Div(multiple);
        if (!long.TryParse(realAmountOutStr.Value, out realAmountOut))
        {
            throw new AssertionException($"Failed to parse {realAmountOutStr.Value}");
        }
    }
}