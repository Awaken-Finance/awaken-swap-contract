using AElf.Standards.ACS0;
using Awaken.Contracts.Token;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContractState
    {
        internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractReferenceState TokenContract
        {
            get;
            set;
        }

        internal TokenContractContainer.TokenContractReferenceState LPTokenContract { get; set; }
        internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }

        
    }
}