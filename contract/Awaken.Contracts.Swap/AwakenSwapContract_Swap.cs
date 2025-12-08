using AElf.CSharp.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract
    {
        public override SwapOutput SwapExactTokensForTokens(SwapExactTokensForTokensInput input)
        {
            AssertContractInitialized();
            Assert(input.Deadline >= Context.CurrentBlockTime, "Expired");
            Assert(input.AmountIn > 0 && input.AmountOutMin >= 0, "Invalid Input");
            var amounts = GetAmountsOut(input.AmountIn, input.Path);
            Assert(amounts[amounts.Count - 1] >= input.AmountOutMin, "Insufficient Output amount");
            var pairAddress = GetPairAddress(input.Path[0], input.Path[1]);
            TransferIn(pairAddress, Context.Sender, input.Path[0], input.AmountIn);
            Swap(amounts, input.Path, input.To, input.Channel);
            return new SwapOutput
            {
                Amount = {amounts}
            };
        }

        public override SwapOutput SwapTokensForExactTokens(SwapTokensForExactTokensInput input)
        {
            AssertContractInitialized();
            Assert(input.Deadline.Seconds >= Context.CurrentBlockTime.Seconds, "Expired");
            Assert(input.AmountOut > 0 && input.AmountInMax > 0, "Invalid Input");
            var amounts = GetAmountsIn(input.AmountOut, input.Path);
            Assert(amounts[0] <= input.AmountInMax, "Excessive Input amount");
            var pairAddress = GetPairAddress(input.Path[0], input.Path[1]);
            TransferIn(pairAddress, Context.Sender, input.Path[0], amounts[0]);
            Swap(amounts, input.Path, input.To, input.Channel);
            return new SwapOutput
            {
                Amount = {amounts}
            };
        }

        public override Empty SwapExactTokensForTokensSupportingFeeOnTransferTokens(
            SwapExactTokensForTokensSupportingFeeOnTransferTokensInput input)
        {
            AssertContractInitialized();
            Assert(input.Deadline.Seconds >= Context.CurrentBlockTime.Seconds, "Expired");
            Assert(input.AmountIn > 0 && input.AmountOutMin >= 0, "Invalid Input");
            var pairAddress = GetPairAddress(input.Path[0], input.Path[1]);
            TransferIn(pairAddress, Context.Sender, input.Path[0], input.AmountIn);
            var balanceBefore = GetBalance(input.Path[input.Path.Count - 1], input.To);
            SwapSupportingFeeOnTransferTokens(input.Path, input.To, input.Channel);
            Context.SendInline(Context.Self, nameof(SwapExactTokensForTokensSupportingFeeOnTransferTokensVerify),
                new SwapExactTokensForTokensSupportingFeeOnTransferTokensVerifyInput
                {
                    AmountBefore = balanceBefore,
                    AmountOutMin = input.AmountOutMin,
                    Symbol = input.Path[input.Path.Count - 1],
                    To = input.To
                }.ToByteString());
            return new Empty();
        }

        public override Empty SwapExactTokensForTokensSupportingFeeOnTransferTokensVerify(
            SwapExactTokensForTokensSupportingFeeOnTransferTokensVerifyInput input)
        {
            AssertContractInitialized();
            Assert(Context.Sender == Context.Self, "No permission");
            var balanceAfter = GetBalance(input.Symbol, input.To);
            Assert(balanceAfter.Sub(input.AmountBefore) >= input.AmountOutMin, "Insufficient Output amount");
            return new Empty();
        }
    }
}