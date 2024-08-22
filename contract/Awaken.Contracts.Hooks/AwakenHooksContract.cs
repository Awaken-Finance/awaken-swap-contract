using System.Collections.Generic;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using Awaken.Contracts.Order;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TransferFromInput = Awaken.Contracts.Token.TransferFromInput;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract : AwakenHooksContractContainer.AwakenHooksContractBase
{
    public override Empty Initialize(InitializeInput input)
    {
        Assert(!State.Initialized.Value, "Already initialized.");
        State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
        var author = State.GenesisContract.GetContractAuthor.Call(Context.Self);
        Assert(Context.Sender == author, "No permission.");
        State.TokenContract.Value =
            Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        if (input.SwapContractList?.SwapContracts.Count > 0)
        {
            FillSwapContractInfoList(input.SwapContractList);
        }
        State.Admin.Value = input.Admin ?? Context.Sender;
        State.Initialized.Value = true;
        return new Empty();
    }

    public override Empty SwapExactTokensForTokens(SwapExactTokensForTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        var limitOrderFillDetailMap = new Dictionary<string, FillDetail>();
        var maxFillCount = State.MaxFillLimitOrderCount.Value;
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsOut(swapInput.AmountIn, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[amounts.Count - 1] >= swapInput.AmountOutMin, "Insufficient Output amount");
            
            if (maxFillCount > 0 && State.MatchLimitOrderEnabled.Value &&
                (swapInput.FeeRates.Count == 1 || State.MultiSwapMatchLimitOrderEnabled.Value))
            {
                MixSwapExactTokensForTokensAndLimitOrder(swapInput, amounts, limitOrderFillDetailMap, maxFillCount, out var orderFilledCount);
                maxFillCount -= orderFilledCount;
                continue;
            }
            
            TransferFromSender(swapInput.Path[0], swapInput.AmountIn, "Hooks Swap");
            var beginIndex = 0;
            for (var pathCount = 0; pathCount < swapInput.FeeRates.Count; pathCount++)
            {
                if (pathCount < swapInput.FeeRates.Count - 1 && swapInput.FeeRates[pathCount] == swapInput.FeeRates[pathCount + 1])
                {
                    continue;
                }

                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[beginIndex],
                    Amount = amounts[beginIndex]
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amounts[beginIndex],
                    AmountOutMin = amounts[pathCount + 1],
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapExactTokensForTokensInput.Path.Add(swapInput.Path[index]);
                }

                Context.SendInline(swapContractAddress, nameof(SwapExactTokensForTokens), swapExactTokensForTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
        }
        FireHooksTransactionCreatedLogEvent(nameof(SwapExactTokensForTokens), input.ToByteString());
        return new Empty();
    }

    public override Empty SwapTokensForExactTokens(SwapTokensForExactTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        var limitOrderFillDetailMap = new Dictionary<string, FillDetail>();
        var maxFillCount = State.MaxFillLimitOrderCount.Value;
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsIn(swapInput.AmountOut, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[0] <= swapInput.AmountInMax, "Excessive Input amount");

            if (maxFillCount > 0 && State.MatchLimitOrderEnabled.Value &&
                (swapInput.FeeRates.Count == 1 || State.MultiSwapMatchLimitOrderEnabled.Value))
            {
                MixSwapTokensForExactTokensAndLimitOrder(swapInput, amounts, limitOrderFillDetailMap, maxFillCount, out int orderFilledCount);
                maxFillCount -= orderFilledCount;
                continue;
            }

            TransferFromSender(swapInput.Path[0], amounts[0], "Hooks Swap");
            var beginIndex = 0;
            for (var pathCount = 0; pathCount < swapInput.FeeRates.Count; pathCount++)
            {
                if (pathCount < swapInput.FeeRates.Count - 1 && swapInput.FeeRates[pathCount] == swapInput.FeeRates[pathCount + 1])
                {
                    continue;
                }
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[beginIndex],
                    Amount = amounts[beginIndex]
                });
                var swapTokensForExactTokensInput = new Swap.SwapTokensForExactTokensInput()
                {
                    AmountInMax = amounts[beginIndex],
                    AmountOut = amounts[pathCount + 1],
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapTokensForExactTokensInput.Path.Add(swapInput.Path[index]);
                }
                Context.SendInline(swapContractAddress, nameof(SwapTokensForExactTokens), swapTokensForExactTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
        }
        FireHooksTransactionCreatedLogEvent(nameof(SwapTokensForExactTokens), input.ToByteString());
        return new Empty();
    }

    public override Empty CreatePair(CreatePairInput input)
    {
        var swapContract = GetSwapContractInfo(input.FeeRate);
        Context.SendInline(swapContract.SwapContractAddress, nameof(CreatePair), new Swap.CreatePairInput
        {
            SymbolPair = input.SymbolPair
        });
        FireHooksTransactionCreatedLogEvent(nameof(CreatePair), input.ToByteString());
        return new Empty();
    }

    public override Empty AddLiquidity(AddLiquidityInput input)
    {
        var amounts = AddLiquidity(input.SymbolA, input.SymbolB, input.AmountADesired, input.AmountBDesired,
            input.AmountAMin, input.AmountBMin, input.FeeRate);
        var swapContractAddress = GetSwapContractInfo(input.FeeRate).SwapContractAddress;
        TransferFromSenderAndApprove(input.SymbolA, amounts[0], "Hooks AddLiquidity", swapContractAddress);
        TransferFromSenderAndApprove(input.SymbolB, amounts[1], "Hooks AddLiquidity", swapContractAddress);
        Context.SendInline(swapContractAddress, nameof(AddLiquidity), new AddLiquidityInput
        {
            AmountADesired = amounts[0],
            AmountAMin = amounts[0],
            SymbolA = input.SymbolA,
            AmountBDesired = amounts[1],
            AmountBMin = amounts[1],
            SymbolB = input.SymbolB,
            Channel = input.Channel,
            Deadline = input.Deadline,
            To = input.To
        });
        FireHooksTransactionCreatedLogEvent(nameof(AddLiquidity), input.ToByteString());
        return new Empty();
    }

    public override Empty RemoveLiquidity(RemoveLiquidityInput input)
    {
        var swapContract = GetSwapContractInfo(input.FeeRate);
        var lpTokenSymbol = GetTokenPairSymbol(input.SymbolA, input.SymbolB);
        Context.SendInline(swapContract.LpTokenContractAddress, "TransferFrom", new TransferFromInput
        {
            Symbol = lpTokenSymbol,
            Amount = input.LiquidityRemove,
            From = Context.Sender,
            To = Context.Self
        });
        Context.SendInline(swapContract.LpTokenContractAddress, "Approve", new Token.ApproveInput
        {
            Symbol = lpTokenSymbol,
            Amount = input.LiquidityRemove,
            Spender = swapContract.SwapContractAddress
        });
        Context.SendInline(swapContract.SwapContractAddress, nameof(RemoveLiquidity), new RemoveLiquidityInput
        {
            SymbolA = input.SymbolA,
            SymbolB = input.SymbolB,
            AmountAMin = input.AmountAMin,
            AmountBMin = input.AmountBMin,
            LiquidityRemove = input.LiquidityRemove,
            Deadline = input.Deadline,
            To = input.To
        });
        FireHooksTransactionCreatedLogEvent(nameof(RemoveLiquidity), input.ToByteString());
        return new Empty();
    }

    private void FireHooksTransactionCreatedLogEvent(string methodName, ByteString args)
    {
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = methodName,
            Args = args
        });
    }
}