using AElf.Standards.ACS0;
using Awaken.Contracts.Hooks;
using Points.Contracts.Point;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContractState
{
    internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
    {
        get;
        set;
    }
    internal PointsContractContainer.PointsContractReferenceState PointsContract { get; set; }
    internal AwakenHooksContractContainer.AwakenHooksContractReferenceState HooksContract { get; set; }
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
}