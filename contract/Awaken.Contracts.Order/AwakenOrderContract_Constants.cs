namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private const int MaxOrderCountEveryOrderBook = 50;
    private const int MaxPriceCountEveryPriceBook = 63;
    private const int MaxFillOrderCountEachSwap = 50;
}