namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private const int MaxOrdersEachOrderBook = 20;
    private const int MaxOrderBooksEachPrice = 50;
    private const int MaxPricesEachPriceBook = 63;
    private const int MaxPriceBooksEachTradePair = 50;
    private const int MaxFillOrderCount = 30;
    private const long PriceMultiple = 100000000;
    private const long ErasePriceMultiple = 1;
    private const int UserPendingOrdersLimit = 30;
    private const int IncreaseRateMax = 10000;
}