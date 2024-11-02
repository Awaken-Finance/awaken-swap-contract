using System.Collections.Generic;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Order;
using Awaken.Contracts.Points;
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
        Assert(input.LabsFeeRate >= 0 && input.LabsFeeRate <= State.LabsFeeRate.Value, "Invalid labsFeeRate.");
        var limitOrderFillDetailMap = new Dictionary<string, FillDetail>();
        var maxFillCount = State.MaxFillLimitOrderCount.Value;
        var totalAmountOutMap = new Dictionary<Address, Dictionary<string, long>>();
        var curAmountIn = 0L;
        var curSymbolIn = "";
        var curAmountOut = 0L;
        var curSymbolOut = "";
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsOut(swapInput.AmountIn, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[amounts.Count - 1] >= swapInput.AmountOutMin, "Insufficient Output amount");
            
            if (maxFillCount > 0 && State.MatchLimitOrderEnabled.Value &&
                (swapInput.FeeRates.Count == 1 || State.MultiSwapMatchLimitOrderEnabled.Value))
            {
                MixSwapExactTokensForTokensAndLimitOrder(swapInput, amounts, limitOrderFillDetailMap, maxFillCount, 
                    out var orderFilledCount, out var amountOut);
                maxFillCount -= orderFilledCount;
                RecordTotalAmountOut(totalAmountOutMap, swapInput.To, swapInput.Path[swapInput.FeeRates.Count], amountOut);
                ProcessSwapAction(ref curSymbolIn, ref curAmountIn, ref curSymbolOut, ref curAmountOut, swapInput, amountOut);
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
                    To = Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapExactTokensForTokensInput.Path.Add(swapInput.Path[index]);
                }
                Context.SendInline(swapContractAddress, nameof(SwapExactTokensForTokens), swapExactTokensForTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
            RecordTotalAmountOut(totalAmountOutMap, swapInput.To, swapInput.Path[swapInput.FeeRates.Count], amounts[swapInput.FeeRates.Count]);
            ProcessSwapAction(ref curSymbolIn, ref curAmountIn, ref curSymbolOut, ref curAmountOut, swapInput, amounts[swapInput.FeeRates.Count]);
        }
        ChargeLabsFee(totalAmountOutMap, input.LabsFeeRate);
        FireHooksTransactionCreatedLogEvent(nameof(SwapExactTokensForTokens), input.ToByteString());
        FinishSwapAction(curSymbolIn, curAmountIn, curSymbolOut, curAmountOut);
        return new Empty();
    }
    
    private void ProcessSwapAction(ref string curSymbolIn, ref long curAmountIn, 
        ref string curSymbolOut, ref long curAmountOut, 
        SwapExactTokensForTokens swapInput, long amountOut)
    {
        if (!string.IsNullOrWhiteSpace(curSymbolIn) &&  curSymbolIn != swapInput.Path[0] || 
            !string.IsNullOrWhiteSpace(curSymbolOut) && curSymbolOut != swapInput.Path[swapInput.FeeRates.Count])
        {
            FinishSwapAction(curSymbolIn, curAmountIn, curSymbolOut, curAmountOut);
            curSymbolIn = swapInput.Path[0];
            curSymbolOut = swapInput.Path[swapInput.FeeRates.Count];
            curAmountIn = swapInput.AmountIn;
            curAmountOut = amountOut;
        }
        else
        {
            curSymbolIn = swapInput.Path[0];
            curSymbolOut = swapInput.Path[swapInput.FeeRates.Count];
            curAmountIn += swapInput.AmountIn;
            curAmountOut += amountOut;
        }
    }

    private void FinishSwapAction(string symbolIn, long amountIn, string symbolOut, long amountOut)
    {
        if (CheckPointsContract())
        {
            return;
        }

        State.AwakenPointsContract.FinishAction.Send(new FinishActionInput
        {
            ActionType = ActionType.Swap,
            ActionDetail = new ActionDetail
            {
                Address = Context.Sender,
                AmountA = amountIn,
                AmountB = amountOut,
                SymbolA = symbolIn,
                SymbolB = symbolOut
            }
        });
    }

    private void ChargeLabsFee(Dictionary<Address, Dictionary<string, long>> totalAmountOutMap, long labsFeeRate)
    {
        foreach (var totalAmountOutPair in totalAmountOutMap)
        {
            foreach (var symbolToAmountPair in totalAmountOutPair.Value)
            {
                var amountOut = symbolToAmountPair.Value;
                if (labsFeeRate > 0)
                {
                    var labsFee = labsFeeRate * amountOut / FeeRateMax;
                    if (labsFee > 0)
                    {
                        State.TokenContract.Transfer.Send(new TransferInput
                        {
                            Symbol = symbolToAmountPair.Key,
                            Amount = labsFee,
                            To = State.LabsFeeTo.Value,
                            Memo = "Hooks Labs Fee"
                        });
                        Context.Fire(new LabsFeeCharged
                        {
                            Address = totalAmountOutPair.Key,
                            Symbol = symbolToAmountPair.Key,
                            Amount = labsFee,
                            FeeTo = State.LabsFeeTo.Value
                        });
                        amountOut -= labsFee;
                    }
                }
                State.TokenContract.Transfer.Send(new TransferInput
                {
                    Symbol = symbolToAmountPair.Key,
                    Amount = amountOut,
                    To = totalAmountOutPair.Key,
                    Memo = "Hooks Swap"
                });
            }
        }
    }

    private void RecordTotalAmountOut(Dictionary<Address, Dictionary<string, long>> totalAmountOutMap, Address to, string symbol, long amount)
    {
        var symbolToAmount = totalAmountOutMap.GetValueOrDefault(to, null);
        if (symbolToAmount == null)
        {
            symbolToAmount = new Dictionary<string, long>();
            totalAmountOutMap[to] = symbolToAmount;
        }
        symbolToAmount[symbol] =
            symbolToAmount.GetValueOrDefault(symbol, 0) + amount;
    }

    public override Empty SwapTokensForExactTokens(SwapTokensForExactTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        Assert(input.LabsFeeRate >= 0 && input.LabsFeeRate <= State.LabsFeeRate.Value, "Invalid labsFeeRate.");
        var limitOrderFillDetailMap = new Dictionary<string, FillDetail>();
        var maxFillCount = State.MaxFillLimitOrderCount.Value;
        var totalAmountOutMap = new Dictionary<Address, Dictionary<string, long>>();
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsIn(swapInput.AmountOut, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[0] <= swapInput.AmountInMax, "Excessive Input amount");
            RecordTotalAmountOut(totalAmountOutMap, swapInput.To, swapInput.Path[swapInput.FeeRates.Count], swapInput.AmountOut);
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
                    To = Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapTokensForExactTokensInput.Path.Add(swapInput.Path[index]);
                }
                Context.SendInline(swapContractAddress, nameof(SwapTokensForExactTokens), swapTokensForExactTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
        }
        ChargeLabsFee(totalAmountOutMap, input.LabsFeeRate);
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
        if (CheckPointsContract())
        {
            State.AwakenPointsContract.FinishAction.Send(new FinishActionInput
            {
                ActionType = ActionType.AddLiquidity,
                ActionDetail = new ActionDetail
                {
                    Address = input.To,
                    SymbolA = input.SymbolA,
                    AmountA = amounts[0],
                    SymbolB = input.SymbolB,
                    AmountB = amounts[1]
                }
            });
        }
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

    private bool CheckPointsContract()
    {
        return State.AwakenPointsContract.Value != null && !State.AwakenPointsContract.Value.Value.IsNullOrEmpty();
    }
}