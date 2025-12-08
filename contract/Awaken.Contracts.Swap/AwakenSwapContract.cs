using AElf;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract : AwakenSwapContractContainer.AwakenSwapContractBase
    {
        public override Empty Initialize(InitializeInput input)
        {
            Assert(!State.IsInitialized.Value, "Already initialized.");
            State.IsInitialized.Value = true;
            State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
            var author = State.GenesisContract.GetContractAuthor.Call(Context.Self);
            Assert(Context.Sender == author, "No permission.");
            State.TokenContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            State.LPTokenContract.Value = input.AwakenTokenContractAddress;
            State.Admin.Value = input.Admin ?? Context.Sender;
            return new Empty();
        }

        public override Address CreatePair(CreatePairInput input)
        {
            AssertContractInitialized();
            var tokenPair = ExtractTokenPair(input.SymbolPair);
            Assert(tokenPair[0] != tokenPair[1], "Identical Tokens.");
            Assert(State.PairVirtualAddressMap[tokenPair[0]][tokenPair[1]] == null, $"Pair {input.SymbolPair} Already Exist.");
            ValidTokenSymbol(tokenPair[0]);
            ValidTokenSymbol(tokenPair[1]);
            var pairString = GetTokenPair(tokenPair[0], tokenPair[1]);
            var pairHash = HashHelper.ComputeFrom(pairString);
            var pairVirtualAddress = Context.ConvertVirtualAddressToContractAddress(pairHash);
            State.PairVirtualAddressMap[tokenPair[0]][tokenPair[1]] = pairVirtualAddress;
            State.KValueMap[pairVirtualAddress] = new BigIntValue(0);
            // Add to PairList
            var pairList = State.PairList.Value ?? new StringList();
            if (!pairList.Value.Contains(pairString))
            {
                pairList.Value.Add(pairString);
                State.PairList.Value = pairList;
            }

            State.LPTokenContract.Create.Send(new Token.CreateInput
            {
                Symbol = GetTokenPairSymbol(tokenPair[0], tokenPair[1]),
                Decimals = 8,
                TokenName = $"Awaken {GetTokenPair(tokenPair[0], tokenPair[1])} LP Token",
                Issuer = Context.Self,
                IsBurnable = true,
                TotalSupply = long.MaxValue
            });

            Context.Fire(new PairCreated
            {
                SymbolA = tokenPair[0],
                SymbolB = tokenPair[1],
                Pair = pairVirtualAddress
            });

            return pairVirtualAddress;
        }
    }
}