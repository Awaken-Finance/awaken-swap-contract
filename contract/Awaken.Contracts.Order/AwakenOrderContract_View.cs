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


    public override GetFillResultOutput GetFillResult(GetFillResultInput input)
    {
        var result = new GetFillResultOutput();
        var orderBookConfig = State.OrderBookConfig.Value;
        var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolOut][input.SymbolIn];
        var headerPriceBook = State.PriceBookMap[headerPriceBookId];
        if (headerPriceBookId <= 0 || headerPriceBook == null)
        {
            return result;
        }
        
        var priceBook = FindPriceBook(headerPriceBook, input.MinPrice);
        while (result.AmountInFilled < input.AmountIn)
        {
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (input.MaxPrice <= sellPrice)
                {
                    return result;
                }
                if (input.MinPrice > sellPrice)
                {
                    continue;
                }

                var headerOrderBookId = State.OrderBookIdMap[input.SymbolOut][input.SymbolIn][sellPrice];
                var headerOrderBook = State.OrderBookMap[headerOrderBookId];
                TryFillOrderBookList(headerOrderBook, input.AmountIn - result.AmountInFilled, 
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
        var priceBookId = State.HeaderPriceBookIdMap[input.SymbolOut][input.SymbolIn];
        if (priceBookId <= 0)
        {
            return result;
        }
        var priceBook = State.PriceBookMap[priceBookId];
        while (priceBook != null)
        {
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (input.MinPrice < sellPrice)
                {
                    result.Price = sellPrice;
                    return result;
                }
            }
            priceBook = State.PriceBookMap[priceBook.NextPagePriceBookId];
        }
        return result;
    }
}