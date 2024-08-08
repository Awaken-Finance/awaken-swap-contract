using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }

    public SingletonState<SwapContractInfoList> SwapContractInfoList { get; set; }

    public SingletonState<Address> Admin { get; set; }
    public SingletonState<bool> MatchLimitOrderEnabled { get; set; }
    public SingletonState<bool> MultiSwapMatchLimitOrderEnabled { get; set; }
    
    // key= symbolA,symbolB, value=priceA/priceB
    public MappedState<string, string, long> PriceMapper { get; set; }
}