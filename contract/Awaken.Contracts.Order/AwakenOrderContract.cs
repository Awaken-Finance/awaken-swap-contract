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
        Assert(input.AmountIn > 0 && input.AmountOut > 0, "Invalid input.");
        AssertContractInitialized();
        var orderBookConfig = State.OrderBookConfig.Value;
        // check price， tradePair existed
        AssertAllowanceAndBalance(input.SymbolIn, input.AmountIn);
        CalculatePrice(input.SymbolIn, input.SymbolOut, input.AmountIn, input.AmountOut, true, out var price);
        BigIntValue amountInBigValue = new BigIntValue(input.AmountIn);
        var realAmountOutStr = amountInBigValue.Mul(price).Div(orderBookConfig.PriceMultiple).Value;
        if (!long.TryParse(realAmountOutStr, out var realAmountOut))
        {
            throw new AssertionException($"Failed to parse {realAmountOutStr}");
        }
        
        var headOrderBookId = State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][price];
        if (headOrderBookId == 0)
        {
            headOrderBookId = State.LastOrderBookId.Value.Add(1);
            State.LastOrderBookId.Value = headOrderBookId;
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
            OrderBookId = headOrderBookId,
            SymbolIn = input.SymbolIn,
            SymbolOut = input.SymbolOut
        };
        for (var i = 1; i <= 30; i++)
        {
            if (orderBook.UserLimitOrders.Count < orderBookConfig.MaxOrdersEachOrderBook)
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
                    SymbolIn = input.SymbolIn,
                    SymbolOut = input.SymbolOut
                };
                break;
            }
            orderBook = State.OrderBookMap[orderBook.NextPageOrderBookId];
        }
        Assert(orderBook.UserLimitOrders.Count < orderBookConfig.MaxOrdersEachOrderBook, "Too many orders at this price.");
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
            Deadline = input.Deadline,
            OrderId = lastOrderId
        });
        return new Empty();
    }

    public override Empty CancelLimitOrder(CancelLimitOrderInput input)
    {
        AssertContractInitialized();
        var orderBookId = State.OrderIdToOrderBookIdMap[input.OrderId];
        Assert(orderBookId > 0, "OrderId not existed.");
        var orderBook = State.OrderBookMap[orderBookId];
        var userLimitOrder = orderBook.UserLimitOrders.FirstOrDefault(t => t.OrderId == input.OrderId);
        Assert(userLimitOrder != null, "Limit Order not existed.");
        Assert(userLimitOrder.Maker == Context.Sender, "No permission.");
        orderBook.UserLimitOrders.Remove(userLimitOrder);
        State.OrderIdToOrderBookIdMap.Remove(input.OrderId);
        Context.Fire(new LimitOrderCancelled
        {
            CancelTime = Context.CurrentBlockTime,
            OrderId = input.OrderId
        });
        return new Empty();
    }
    
    public override Empty FillLimitOrder(FillLimitOrderInput input)
    {
        AssertContractInitialized();
        if (State.FillOrderWhiteList.Value != null && State.FillOrderWhiteList.Value.Value.Count > 0)
        {
            Assert(State.FillOrderWhiteList.Value.Value.Contains(Context.Sender), "Sender is not in white list");
        }
        var orderBookConfig = State.OrderBookConfig.Value;
        var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolIn][input.SymbolOut];
        Assert(headerPriceBookId > 0, "Price book id not existed");
        var priceBook = State.PriceBookMap[headerPriceBookId];
        Assert(priceBook?.PriceList != null, "Price book not existed.");
        var orderCount = 0;
        var amountInUsed = 0L;
        
        var breakOuterLoop = false;
        while (!breakOuterLoop)
        {
            var removePricesCount = 0;
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (input.MaxOpenIntervalPrice < sellPrice)
                {
                    breakOuterLoop = true;
                    break;
                }
                var headerOrderBookId = State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][sellPrice];
                var headerOrderBook = State.OrderBookMap[headerOrderBookId];
                var curOrderBook = FillOrderBookList(headerOrderBook, input.To,input.AmountIn - amountInUsed, 
                    orderBookConfig.MaxFillOrderCount - orderCount,  out var amountInFilled, out var orderFilledCount);
                amountInUsed += amountInFilled;
                orderCount += orderFilledCount;
                if (curOrderBook == null)
                {
                    removePricesCount++;
                    State.OrderBookIdMap[input.SymbolIn][input.SymbolOut].Remove(sellPrice);
                }
                else if (headerOrderBook.OrderBookId != curOrderBook.OrderBookId)
                {
                    State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][sellPrice] = curOrderBook.OrderBookId;
                }
                
                if (amountInUsed >= input.AmountIn || orderCount >= orderBookConfig.MaxFillOrderCount)
                {
                    breakOuterLoop = true;
                    break;
                }
            }
            
            if (priceBook.PriceList.Prices.Count == removePricesCount)
            {
                State.PriceBookMap.Remove(priceBook.PriceBookId);
                if (priceBook.NextPagePriceBookId > 0)
                {
                    State.HeaderPriceBookIdMap[input.SymbolIn][input.SymbolOut] = priceBook.NextPagePriceBookId;
                    priceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
                    continue;
                }

                State.HeaderPriceBookIdMap[input.SymbolIn].Remove(input.SymbolOut);
                breakOuterLoop = true;
                continue;
            }
            if (removePricesCount == 0)
            {
                continue;
            }
            priceBook.PriceList = new PriceList
            {
                Prices = { priceBook.PriceList.Prices.Skip(removePricesCount) }
            };
        }
        return new Empty();
    }
    
    private OrderBook FillOrderBookList(OrderBook headerOrderBook, Address to, long maxAmountInFilled, int maxFillOrderCount, 
        out long amountInFilled, out int orderFilledCount)
    {
        amountInFilled = 0;
        orderFilledCount = 0;
        if (headerOrderBook == null)
        {
            return null;
        }
        var orderBook = headerOrderBook;

        while (amountInFilled < maxAmountInFilled && orderFilledCount < maxFillOrderCount)
        {
            FillOrderBook(orderBook, to, maxAmountInFilled - amountInFilled, maxFillOrderCount - orderFilledCount,
                out var amountInFilledThisBook, out var orderFilledCountThisBook);
            amountInFilled += amountInFilledThisBook;
            orderFilledCount += orderFilledCountThisBook;
            if (orderBook.UserLimitOrders.Count == 0)
            {
                State.OrderBookMap.Remove(orderBook.OrderBookId);
                if (orderBook.NextPageOrderBookId == 0)
                {
                    return null;
                }
                orderBook = State.OrderBookMap[orderBook.NextPageOrderBookId];
            }
        }

        return orderBook;
    }
    
    private void TryFillOrderBookList(OrderBook headerOrderBook, long maxAmountInFilled, int maxFillOrderCount, 
        out long amountInFilled, out long amountOutFilled, out int orderFilledCount)
    {
        amountInFilled = 0;
        amountOutFilled = 0;
        orderFilledCount = 0;
        if (headerOrderBook == null)
        {
            return;
        }
        var orderBook = headerOrderBook;
        while (amountInFilled < maxAmountInFilled && orderFilledCount < maxFillOrderCount)
        {
            TryFillOrderBook(orderBook, maxAmountInFilled - amountInFilled, maxFillOrderCount - orderFilledCount,
                out var amountInFilledThisBook, out var amountOutFilledThisBook, out var orderFilledCountThisBook);
            amountInFilled += amountInFilledThisBook;
            amountOutFilled += amountOutFilledThisBook;
            orderFilledCount += orderFilledCountThisBook;
            if (orderBook.NextPageOrderBookId == 0)
            {
                return;
            }
            orderBook = State.OrderBookMap[orderBook.NextPageOrderBookId];
        }
    }

    private void FillOrderBook(OrderBook orderBook, Address to, long maxAmountInFilled, int maxFillOrderCount, out long amountInFilled, 
        out int orderFilledCount)
    {
        amountInFilled = 0;
        orderFilledCount = 0;
        var orderNeedRemoveCount = 0;
        foreach (var userLimitOrder in orderBook.UserLimitOrders)
        {
            orderFilledCount++;
            var checkoutResult = CheckAllowanceAndBalance(userLimitOrder.Maker, orderBook.SymbolIn, userLimitOrder.AmountIn);
            if (!checkoutResult || userLimitOrder.Deadline < Context.CurrentBlockTime)
            {
                orderNeedRemoveCount++;
                State.OrderIdToOrderBookIdMap.Remove(userLimitOrder.OrderId);
                Context.Fire(new LimitOrderRemoved
                {
                    OrderId = userLimitOrder.OrderId,
                    RemoveTime = Context.CurrentBlockTime
                });
                continue;
            }
            var amountIn = Math.Min(maxAmountInFilled - amountInFilled, userLimitOrder.AmountIn - userLimitOrder.AmountInFilled);
            var amountOutStr = new BigIntValue(userLimitOrder.AmountOut).Mul(amountIn).Div(userLimitOrder.AmountIn).Value;
            if (!long.TryParse(amountOutStr, out var amountOut))
            {
                throw new AssertionException($"Failed to parse {amountOutStr}");
            }
            amountInFilled += amountIn;
            
            SwapInternal(userLimitOrder.Maker, to, orderBook.SymbolIn, orderBook.SymbolOut, amountIn, amountOut);
            userLimitOrder.AmountInFilled += amountIn;
            userLimitOrder.AmountOutFilled += amountOut;
            userLimitOrder.FillTime = Context.CurrentBlockTime;
            Context.Fire(new LimitOrderFilled
            {
                OrderId = userLimitOrder.OrderId,
                AmountInFilled = amountIn,
                AmountOutFilled = amountOut,
                FillTime = Context.CurrentBlockTime,
                Taker = to
            });
            if (userLimitOrder.AmountInFilled == userLimitOrder.AmountIn)
            {
                orderNeedRemoveCount++;
                State.OrderIdToOrderBookIdMap.Remove(userLimitOrder.OrderId);
            }

            if (amountInFilled >= maxAmountInFilled || orderFilledCount >= maxFillOrderCount)
            {
                break;
            }
        }
        if (orderBook.UserLimitOrders.Count == orderNeedRemoveCount)
        {
            orderBook.UserLimitOrders.Clear();
            return;
        }
        for (var i = 0; i < orderNeedRemoveCount; i++)
        {
            orderBook.UserLimitOrders.RemoveAt(0);
        }
    }
    
    private void TryFillOrderBook(OrderBook orderBook, long maxAmountInFilled, int maxFillOrderCount, out long amountInFilled, out long amountOutFilled,
        out int orderFilledCount)
    {
        amountInFilled = 0;
        amountOutFilled = 0;
        orderFilledCount = 0;
        foreach (var userLimitOrder in orderBook.UserLimitOrders)
        {
            orderFilledCount++;
            var checkoutResult = CheckAllowanceAndBalance(userLimitOrder.Maker, orderBook.SymbolIn, userLimitOrder.AmountIn - userLimitOrder.AmountInFilled);
            if (!checkoutResult || userLimitOrder.Deadline < Context.CurrentBlockTime)
            {
                continue;
            }
            var amountIn = Math.Min(maxAmountInFilled - amountInFilled, userLimitOrder.AmountIn - userLimitOrder.AmountInFilled);
            var amountOutStr = new BigIntValue(userLimitOrder.AmountOut).Mul(amountIn).Div(userLimitOrder.AmountIn).Value;
            if (!long.TryParse(amountOutStr, out var amountOut))
            {
                throw new AssertionException($"Failed to parse {amountOutStr}");
            }
            amountInFilled += amountIn;
            amountOutFilled += amountOut;
            if (amountInFilled >= maxAmountInFilled || orderFilledCount >= maxFillOrderCount)
            {
                break;
            }
        }
    }

    private void SwapInternal(Address maker, Address taker, string symbolIn, string symbolOut, long amountIn, long amountOut)
    {
        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = maker,
            To = Context.Self,
            Symbol = symbolIn,
            Amount = amountIn,
            Memo = "Fill Limit Order"
        });
        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = taker,
            Amount = amountIn,
            Symbol = symbolIn,
            Memo = "Fill Limit Order"
        });
        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            From = Context.Sender,
            To = Context.Self,
            Symbol = symbolOut,
            Amount = amountOut,
            Memo = "Fill Limit Order"
        });
        State.TokenContract.Transfer.Send(new TransferInput
        {
            To = maker,
            Amount = amountOut,
            Symbol = symbolOut,
            Memo = "Fill Limit Order"
        });
    }
}