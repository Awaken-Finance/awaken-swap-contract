namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private const int MaxOrdersEachOrderBook = 20;
    private const int MaxPricesEachPriceBook = 63;
    private const int MaxFillOrderCount = 50;
    private const long PriceMultiple = 100000000;
    private const long ErasePriceMultiple = 10000;
    private const long MinOrderValueInUsdt = 1000000000;
}