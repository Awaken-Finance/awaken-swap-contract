using AElf.Sdk.CSharp.State;
using AElf.Types;
using Google.Protobuf.Collections;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }
    public SingletonState<Address> Admin { get; set; }
    public SingletonState<OrderBookConfig> OrderBookConfig { get; set; }
    public SingletonState<bool> CheckCommitPriceEnabled { get; set; }
    public SingletonState<int> CommitPriceIncreaseRate { get; set; }
    public SingletonState<WhiteList> FillOrderWhiteList { get; set; }

    // key = symbolIn, symbolOut, price(amountOut/amountIn); value = orderBookId
    public MappedState<string, string, long, long> OrderBookIdMap { get; set; }
    public SingletonState<long> LastOrderBookId { get; set; }
    public SingletonState<long> LastOrderId { get; set; }
    public MappedState<long, OrderBook> OrderBookMap { get; set; }
    public MappedState<long, long> OrderIdToOrderBookIdMap { get; set; }
    
    //  price book
    // key= symbolIn, symbolOut; value = headerPriceBookId 
    public MappedState<string, string, long> HeaderPriceBookIdMap {get; set;}
    public SingletonState<long> LastPriceBookId { get; set; }
    // key= priceBookId; value = PriceBook
    public MappedState<long, PriceBook> PriceBookMap {get; set;}
    
    public MappedState<Address, LimitOrderIdList> UserLimitOrderIdsMap { get; set; }
}