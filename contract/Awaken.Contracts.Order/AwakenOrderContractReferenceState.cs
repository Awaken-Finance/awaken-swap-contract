using AElf.Standards.ACS0;
using Awaken.Contracts.Hooks;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContractState
{
    internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
    {
        get;
        set;
    }
    internal AwakenHooksContractContainer.AwakenHooksContractReferenceState HooksContract
    {
        get;
        set;
    }
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
}