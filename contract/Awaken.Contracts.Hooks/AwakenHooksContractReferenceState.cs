using AElf.Standards.ACS0;
using Awaken.Contracts.Order;
using Awaken.Contracts.Points;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContractState
{
    internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
    {
        get;
        set;
    }
    internal AwakenOrderContractContainer.AwakenOrderContractReferenceState OrderContract
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