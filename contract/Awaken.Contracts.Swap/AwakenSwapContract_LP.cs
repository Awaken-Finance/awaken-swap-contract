using AElf.CSharp.Core;
using Awaken.Contracts.Token;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract
    {
        public override AddLiquidityOutput AddLiquidity(AddLiquidityInput input)
        {
            AssertContractInitialized();
            Assert(input.Deadline.Seconds >= Context.CurrentBlockTime.Seconds, "Expired.");
            Assert(input.AmountAMin >= 0 && input.AmountBMin >= 0 && input.AmountADesired > 0 && input.AmountBDesired > 0,
                "Invalid input amount.");
            var amounts = AddLiquidity(input.SymbolA, input.SymbolB, input.AmountADesired, input.AmountBDesired,
                input.AmountAMin, input.AmountBMin);
            var sortedSymbol = SortSymbols(input.SymbolA, input.SymbolB);
            var pairVirtualAddress = State.PairVirtualAddressMap[sortedSymbol[0]][sortedSymbol[1]];
            TransferIn(pairVirtualAddress, Context.Sender, input.SymbolA, amounts[0]);
            TransferIn(pairVirtualAddress, Context.Sender, input.SymbolB, amounts[1]);
            var lpTokenAmount = MintLPToken(input.SymbolA, input.SymbolB, amounts[0], amounts[1], input.To,
                input.Channel);
            return new AddLiquidityOutput
            {
                AmountA = amounts[0],
                AmountB = amounts[1],
                LiquidityToken = lpTokenAmount,
                SymbolA = input.SymbolA,
                SymbolB = input.SymbolB
            };
        }
        

        public override RemoveLiquidityOutput RemoveLiquidity(RemoveLiquidityInput input)
        {
            AssertContractInitialized();
            Assert(input.Deadline.Seconds >= Context.CurrentBlockTime.Seconds, "Expired");
            Assert(input.AmountAMin >= 0 && input.AmountBMin >= 0 && input.LiquidityRemove > 0, "Invalid Input");
            var amount = RemoveLiquidity(input.SymbolA, input.SymbolB, input.LiquidityRemove, input.AmountAMin,
                input.AmountBMin, input.To);
            return new RemoveLiquidityOutput
            {
                AmountA = amount[0],
                AmountB = amount[1],
                SymbolA = input.SymbolA,
                SymbolB = input.SymbolB
            };
        }
    }
}