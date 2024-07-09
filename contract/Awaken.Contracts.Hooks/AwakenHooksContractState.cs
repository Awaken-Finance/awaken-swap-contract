using AElf.Sdk.CSharp.State;
using AElf.Types;
using Awaken.Contracts.Token;

namespace Awaken.Contracts.Hooks;

public class AwakenHooksContractState : ContractState
{
    public SingletonState<bool> Initialized { get; set; }

    public SingletonState<SwapContractInfoList> SwapContractInfoList { get; set; }

    public SingletonState<Address> Admin { get; set; }
    internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
    {
        get;
        set;
    }

}