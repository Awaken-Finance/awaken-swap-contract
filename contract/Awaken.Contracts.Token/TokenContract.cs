using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Token
{
    public partial class TokenContract : TokenContractContainer.TokenContractBase
    {
        public override Empty Initialize(InitializeInput input)
        {
            Assert(!State.IsInitialized.Value, "Already initialized.");
            State.IsInitialized.Value = true;
            State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
            var author = State.GenesisContract.GetContractAuthor.Call(Context.Self);
            Assert(Context.Sender == author, "No permission.");
            State.Owner.Value = input.Owner;
            State.MinterMap[input.Owner] = true;
            return new Empty();
        }
        
        
    }
}