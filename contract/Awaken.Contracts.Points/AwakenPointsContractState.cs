using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }
    public SingletonState<Address> Admin { get; set; }
    public SingletonState<Address> OrderContractAddress { get; set; }

    // points
    public SingletonState<Hash> PointsContractDAppId { get; set; }
    public SingletonState<Address> PointsSettleAdmin { get; set; }
    public SingletonState<string> OfficialDomainAlias { get; set; }
    public MappedState<Address, bool> JoinRecord { get; set; }

    // userAddress, type
    public MappedState<Address, string, bool> DisposablePointsSettleRecord { get; set; }
    // action name -> point amount
    public MappedState<string, PointsRewardConfig> PointsRewardConfig { get; set; }
    // Hash(symbol) -> price
    public MappedState<Hash, long> PriceMap { get; set; }

    public MappedState<string, PricingToken> PricingTokenMap { get; set; }
    public SingletonState<long> SubscriptionId { get; set; }
}