using AElf.Standards.ACS0;
using Awaken.Contracts.Hooks;
using Awaken.Contracts.Points;

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
    internal AwakenPointsContractContainer.AwakenPointsContractReferenceState AwakenPointsContract
    {
        get;
        set;
    }
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
}