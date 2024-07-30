using System;
using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract : AwakenOrderContractContainer.AwakenOrderContractBase
{
    public override Empty CommitLimitOrder(CommitLimitOrderInput input)
    {
        // check price， tradePair existed
        CheckAllowanceAndBalance(input.SymbolIn, input.AmountIn);
        var symbolInDecimal = GetTokenDecimal(input.SymbolIn);
        var symbolOutDecimal = GetTokenDecimal(input.SymbolOut);
        var amountOutBigIntValue = new BigIntValue(input.AmountOut);
        var weightPrice = amountOutBigIntValue.Mul(IntPow(10, 8 + symbolInDecimal - symbolOutDecimal))
            .Div(input.AmountIn);
        // four decimal places price
        var accuratePrice = weightPrice.Div(IntPow(10, 4));
        if (long.TryParse(accuratePrice.Value, out var price))
        {
            throw new AssertionException($"Failed to parse {accuratePrice.Value}");
        }
        var realAmountOutStr = accuratePrice.Mul(input.AmountIn).Div(IntPow(10, 4)).Value;
        if (long.TryParse(realAmountOutStr, out var realAmountOut))
        {
            throw new AssertionException($"Failed to parse {realAmountOutStr}");
        }
        
        var headOrderBookId = State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][price];
        if (headOrderBookId == 0)
        {
            State.LastOrderBookId.Value = State.LastOrderBookId.Value.Add(1);
            headOrderBookId = State.LastOrderBookId.Value;
            State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][price] = headOrderBookId;
            UpdatePriceBook(input.SymbolIn, input.SymbolOut, price);
        }

        var lastOrderId = State.LastOrderId.Value.Add(1);
        State.LastOrderId.Value = lastOrderId;
        var limitOrder = new UserLimitOrder
        {
            OrderId = lastOrderId,
            AmountIn = input.AmountIn,
            AmountOut = realAmountOut,
            Deadline = input.Deadline,
            Maker = Context.Sender
        };
        var orderBook = State.OrderBookMap[headOrderBookId] ?? new OrderBook
        {
            OrderBookId = headOrderBookId
        };
        for (var i = 1; i <= 30; i++)
        {
            if (orderBook.UserLimitOrders.Count < MaxOrderCountEveryOrderBook)
            {
                break;
            }
            if (orderBook.NextPageOrderBookId == 0)
            {
                var lastOrderBookId = State.LastOrderBookId.Value.Add(1);
                State.LastOrderBookId.Value = lastOrderBookId;
                orderBook.NextPageOrderBookId = lastOrderBookId;
                orderBook = new OrderBook
                {
                    OrderBookId = lastOrderBookId,
                };
                break;
            }
            orderBook = State.OrderBookMap[orderBook.NextPageOrderBookId];
        }
        Assert(orderBook.UserLimitOrders.Count < MaxOrderCountEveryOrderBook, "Too many orders at this price.");
        orderBook.UserLimitOrders.Add(limitOrder);
        State.OrderBookMap[orderBook.OrderBookId] = orderBook;
        State.OrderIdToOrderBookIdMap[limitOrder.OrderId] = orderBook.OrderBookId;
        Context.Fire(new LimitOrderCreated
        {
            SymbolIn = input.SymbolIn,
            SymbolOut = input.SymbolOut,
            AmountIn = input.AmountIn,
            AmountOut = realAmountOut,
            CommitTime = Context.CurrentBlockTime,
            Maker = Context.Sender,
            OrderId = lastOrderId
        });
        return new Empty();
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
        if (priceBook.PriceList.Prices.Count > MaxPriceCountEveryPriceBook)
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
        var newPriceBook = new PriceBook
        {
            PriceBookId = State.LastPriceBookId.Value.Add(1),
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
        for (var i = 0; i < 50; i++)
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

    private void CheckAllowanceAndBalance(string symbol, long amount)
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

    public override Empty CancelLimitOrder(CancelLimitOrderInput input)
    {
        var orderBookId = State.OrderIdToOrderBookIdMap[input.OrderId];
        Assert(orderBookId > 0, "OrderId not existed.");
        var orderBook = State.OrderBookMap[orderBookId];
        var userLimitOrder = orderBook.UserLimitOrders.FirstOrDefault(t => t.OrderId == input.OrderId);
        Assert(userLimitOrder != null, "Limit Order not existed.");
        Assert(userLimitOrder.Maker == Context.Sender, "No permission.");
        orderBook.UserLimitOrders.Remove(userLimitOrder);
        Context.Fire(new LimitOrderCancelled
        {
            CancelTime = Context.CurrentBlockTime,
            OrderId = input.OrderId
        });
        return base.CancelLimitOrder(input);
    }

    public override Empty FillLimitOrder(FillLimitOrderInput input)
    {
        var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolOut][input.SymbolIn];
        Assert(headerPriceBookId > 0, "No price book id.");
        var headerPriceBook = State.PriceBookMap[headerPriceBookId];
        Assert(headerPriceBook != null, "No price book.");
        return base.FillLimitOrder(input);
    }
}