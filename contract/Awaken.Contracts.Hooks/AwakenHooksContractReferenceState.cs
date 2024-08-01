using AElf.Standards.ACS0;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContractState
{
    internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
    {
        get;
        set;
    }
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
}