using System.Linq;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{

    // public override GetFillLimitOrderOutput GetFillLimitOrder(GetFillLimitOrderInput input)
    // {
    //     var result = new GetFillLimitOrderOutput();
    //     var orderBookConfig = State.OrderBookConfig.Value;
    //     var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolOut][input.SymbolIn];
    //     var headerPriceBook = State.PriceBookMap[headerPriceBookId];
    //     if (headerPriceBookId <= 0 || headerPriceBook == null)
    //     {
    //         return result;
    //     }
    //
    //     CalculatePrice(input.SymbolOut, input.SymbolIn, input.AmountIn, 
    //         input.AmountIn, false, out var buyPrice);
    //     var orderCount = 0;
    //     var amountInUsed = 0L;
    //     var priceBook = headerPriceBook;
    //     
    //     while (amountInUsed < input.AmountIn)
    //     {
    //         foreach (var sellPrice in priceBook.PriceList.Prices)
    //         {
    //             if (buyPrice < sellPrice)
    //             {
    //                 return result;
    //             }
    //             var headerOrderBookId = State.OrderBookIdMap[input.SymbolOut][input.SymbolIn][sellPrice];
    //             var headerOrderBook = State.OrderBookMap[headerOrderBookId];
    //             TryFillOrderBookList(headerOrderBook, result.FillOrders,input.AmountIn - amountInUsed, 
    //                 orderBookConfig.MaxFillOrderCount - orderCount,  out var amountInFilled, out var orderFilledCount);
    //             amountInUsed += amountInFilled;
    //             orderCount += orderFilledCount;
    //
    //             if (amountInUsed >= input.AmountIn || orderCount >= orderBookConfig.MaxFillOrderCount)
    //             {
    //                 return result;
    //             }
    //         }
    //         
    //         if (priceBook.NextPagePriceBookId > 0)
    //         {
    //             priceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
    //             continue;
    //         }
    //         return result;
    //     }
    //     return result;
    // }

    public override Int64Value CalculatePrice(CalculatePriceInput input)
    {
        CalculatePrice(input.SymbolIn, input.SymbolOut, input.AmountIn, input.AmountOut, false, out var price, out var realAmountOut);
        return new Int64Value
        {
            Value = price
        };
    }


    public override GetFillResultOutput GetFillResult(GetFillResultInput input)
    {
        var result = new GetFillResultOutput();
        if (input.AmountIn == 0 && input.AmountOut == 0)
        {
            return result;
        }
        if (input.AmountIn == 0)
        {
            input.AmountIn = long.MaxValue;
        }
        if (input.AmountOut == 0)
        {
            input.AmountOut = long.MaxValue;
        }

        var orderBookConfig = State.OrderBookConfig.Value;
        var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolIn][input.SymbolOut];
        var headerPriceBook = State.PriceBookMap[headerPriceBookId];
        if (headerPriceBookId <= 0 || headerPriceBook == null)
        {
            return result;
        }
        
        var priceBook = FindPriceBook(headerPriceBook, input.MinCloseIntervalPrice);
        while (result.AmountInFilled < input.AmountIn && result.AmountOutFilled < input.AmountOut)
        {
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (input.MaxOpenIntervalPrice <= sellPrice)
                {
                    return result;
                }
                if (input.MinCloseIntervalPrice > sellPrice)
                {
                    continue;
                }

                var headerOrderBookId = State.OrderBookIdMap[input.SymbolIn][input.SymbolOut][sellPrice];
                var headerOrderBook = State.OrderBookMap[headerOrderBookId];
                TryFillOrderBookList(headerOrderBook, input.AmountIn - result.AmountInFilled, input.AmountOut - result.AmountOutFilled,
                    orderBookConfig.MaxFillOrderCount - result.OrderFilledCount,  out var amountInFilled, 
                    out var amountOutFilled, out var orderFilledCount);
                result.AmountInFilled += amountInFilled;
                result.AmountOutFilled += amountOutFilled;
                result.OrderFilledCount += orderFilledCount;
                result.MaxPriceFilled = sellPrice;
                
                if (result.AmountInFilled >= input.AmountIn || result.OrderFilledCount >= orderBookConfig.MaxFillOrderCount)
                {
                    return result;
                }
            }
            
            if (priceBook.NextPagePriceBookId > 0)
            {
                priceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
                continue;
            }
            return result;
        }
        return result;
    }

    public override GetBestSellPriceOutput GetBestSellPrice(GetBestSellPriceInput input)
    {
        var result = new GetBestSellPriceOutput();
        var priceBookId = State.HeaderPriceBookIdMap[input.SymbolIn][input.SymbolOut];
        if (priceBookId <= 0)
        {
            return result;
        }
        var priceBook = State.PriceBookMap[priceBookId];
        while (priceBook != null)
        {
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (input.MinOpenIntervalPrice < sellPrice)
                {
                    result.Price = sellPrice;
                    return result;
                }
            }
            priceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
        }
        return result;
    }

    public override PriceBook GetPriceBook(Int64Value input)
    {
        return State.PriceBookMap[input.Value];
    }

    public override OrderBook GetOrderBook(Int64Value input)
    {
        return State.OrderBookMap[input.Value];
    }

    public override UserLimitOrder GetUserLimitOrder(Int64Value input)
    {
        var orderBookId = State.OrderIdToOrderBookIdMap[input.Value];
        if (orderBookId <= 0)
        {
            return null;
        }
        var orderBook = State.OrderBookMap[orderBookId];
        return orderBook.UserLimitOrders.FirstOrDefault(t => t.OrderId == input.Value);
    }

    public override Int64Value GetOrderBookIdByOrderId(Int64Value input)
    {
        return new Int64Value
        {
            Value = State.OrderIdToOrderBookIdMap[input.Value]
        };
    }
}