namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private const int MaxOrderCountEveryOrderBook = 50;
    private const int MaxPriceCountEveryPriceBook = 63;
    private const int MaxFillOrderCountEachSwap = 50;
    private const long PriceMultiple = 100000000;
    private const long ErasePriceMultiple = 10000;
}