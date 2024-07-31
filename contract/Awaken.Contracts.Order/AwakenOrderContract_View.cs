namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    public override GetFillResultOutput GetFillResult(GetFillResultInput input)
    {
        var result = new GetFillResultOutput();
        var headerPriceBookId = State.HeaderPriceBookIdMap[input.SymbolOut][input.SymbolIn];
        var headerPriceBook = State.PriceBookMap[headerPriceBookId];
        if (headerPriceBookId <= 0 || headerPriceBook == null)
        {
            return result;
        }

        CalculatePrice(input.SymbolOut, input.SymbolIn, input.AmountOutMin, 
            input.AmountIn, false, out var buyPrice);
        var orderCount = 0;
        var amountInUsed = 0L;
        var priceBook = headerPriceBook;
        
        while (amountInUsed < input.AmountIn)
        {
            foreach (var sellPrice in priceBook.PriceList.Prices)
            {
                if (buyPrice < sellPrice)
                {
                    return result;
                }
                var headerOrderBookId = State.OrderBookIdMap[input.SymbolOut][input.SymbolIn][sellPrice];
                var headerOrderBook = State.OrderBookMap[headerOrderBookId];
                FillOrderBookList(headerOrderBook, null,input.AmountIn - amountInUsed, 
                    MaxFillOrderCountEachSwap - orderCount,  out var amountInFilled, out var orderFilledCount);
                amountInUsed += amountInFilled;
                orderCount += orderFilledCount;

                if (amountInUsed >= input.AmountIn || orderCount >= MaxFillOrderCountEachSwap)
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
}