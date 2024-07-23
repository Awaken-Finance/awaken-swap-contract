using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace Awaken.Contracts.Order;

public class AwakenOrderContractState : ContractState
{
    // key= tradePairHash, price, sideType; value = orderBookId
    public MappedState<Hash, long, SideType, long> OrderBookIdMap { get; set; }
    public SingletonState<long> LastOrderBookId { get; set; }
    public SingletonState<long> LastOrderId { get; set; }
    public MappedState<long, OrderBook> OrderBookMap { get; set; }
    public MappedState<long, long> OrderIdToOrderBookIdMap { get; set; }
    
    //  price book
    // key=tradePairHash, SideType; value = bestValue(buy=maxPrice, sell=minPrice)
    public MappedState<Hash, SideType, long> TradePairBestPriceMap {get; set;}
    // key=tradePairHash, SideType, firstPrice; value = PriceBook
    public MappedState<Hash, SideType, long, PriceBook> TradePairNextBuyPriceMap {get; set;}
}