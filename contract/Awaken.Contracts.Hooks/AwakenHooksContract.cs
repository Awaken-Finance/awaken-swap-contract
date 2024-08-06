using System;
using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Order;
using Awaken.Contracts.Swap;
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

    public override Empty SetAdmin(Address input)
    {
        Assert(!input.Value.IsNullOrEmpty(), "Invalid input.");
        CheckAdminPermission();
        State.Admin.Value = input;
        return new Empty();
    }

    public override Empty AddSwapContractInfo(AddSwapContractInfoInput input)
    {
        CheckAdminPermission();
        Assert(input.SwapContractList != null && input.SwapContractList.SwapContracts.Count > 0, "Invalid input.");
        FillSwapContractInfoList(input.SwapContractList);
        return new Empty();
    }

    public override Empty RemoveSwapContractInfo(RemoveSwapContractInfoInput input)
    {
        CheckAdminPermission();
        Assert(input.FeeRates.Count > 0, "Invalid input.");
        var swapContractInfoList = State.SwapContractInfoList.Value ??= new SwapContractInfoList();
        foreach (var feeRate in input.FeeRates)
        {
            var swapContractInfo = swapContractInfoList.SwapContracts.FirstOrDefault(t => t.FeeRate == feeRate);
            if (swapContractInfo != null)
            {
                swapContractInfoList.SwapContracts.Remove(swapContractInfo);
            }
        }
        State.SwapContractInfoList.Value = swapContractInfoList;
        return new Empty();
    }

    public override Empty SwapExactTokensForTokens(SwapExactTokensForTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsOut(swapInput.AmountIn, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[amounts.Count - 1] >= swapInput.AmountOutMin, "Insufficient Output amount");
            TransferFromSender(swapInput.Path[0], swapInput.AmountIn, "Hooks Swap");

            for (var pathCount = 0; pathCount < swapInput.FeeRates.Count; pathCount++)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                var amountIn = amounts[pathCount];
                var amountOut = amounts[pathCount + 1];
                if (State.MatchLimitOrderEnabled.Value && (swapInput.FeeRates.Count == 1 || State.MultiSwapMatchLimitOrderEnabled.Value))
                {
                    MatchLimitOrder(swapContractAddress, pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self, 
                        swapInput.Path[pathCount], swapInput.Path[pathCount + 1], amounts[pathCount], amounts[pathCount + 1],
                        out var amountOutPoolFilled, out var amountInPoolFilled);
                    amountIn = amountInPoolFilled;
                    amountOut = amountOutPoolFilled;
                }
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[pathCount],
                    Amount = amountIn
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amountIn,
                    AmountOutMin = amountOut,
                    Path = { swapInput.Path[pathCount], swapInput.Path[pathCount + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };

                Context.SendInline(swapContractAddress, nameof(SwapExactTokensForTokens), swapExactTokensForTokensInput.ToByteString());
            }
        }
        FireHooksTransactionCreatedLogEvent(nameof(SwapExactTokensForTokens), input.ToByteString());
        return new Empty();
    }

    private void MatchLimitOrder(Address swapContractAddress, Address to, string symbolIn, string symbolOut, long amountIn, long amountOut,
        out long amountOutPoolFilled, out long amountInPoolFilled)
    {
        amountInPoolFilled = 0;
        amountOutPoolFilled = 0;
        var amountOutOrderFilled = 0L;
        var amountInOrderFilled = 0L;
        var limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
        {
            SymbolIn = symbolOut, // u
            SymbolOut = symbolIn, // elf
            MinOpenIntervalPrice = 0
        }).Price;
        if (limitOrderSellPrice == 0)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        var minPoolAmountOut = amountOut / 10;
        var maxOrderSellPrice = 0L;
        while (amountOutPoolFilled + amountOutOrderFilled < amountOut)
        {
            minPoolAmountOut = Math.Min(minPoolAmountOut, amountOut - amountOutOrderFilled - amountOutPoolFilled);
            amountOutPoolFilled += minPoolAmountOut;
            var nextPoolAmountIn = Context.Call<Int64Value>(swapContractAddress, "GetAmountIn", new GetAmountInInput()
            {
                SymbolIn = symbolIn,
                SymbolOut = symbolOut,
                AmountOut = amountOutPoolFilled
            }).Value;
            // cross order price
            var nextPoolSellPrice = (nextPoolAmountIn - amountInPoolFilled) / minPoolAmountOut;
            if (nextPoolSellPrice < limitOrderSellPrice)
            {
                continue;
            }
            amountInPoolFilled = nextPoolAmountIn;
            var fillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                AmountIn = amountOut - amountOutPoolFilled - amountOutOrderFilled,
                MinCloseIntervalPrice = limitOrderSellPrice,
                MaxOpenIntervalPrice = nextPoolSellPrice
            });
            amountOutOrderFilled += fillResult.AmountInFilled;
            amountInOrderFilled += fillResult.AmountOutFilled;
            maxOrderSellPrice = fillResult.MaxPriceFilled;
            if (amountOutOrderFilled + amountOutPoolFilled >= amountOut)
            {
                break;
            }
            limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                MinOpenIntervalPrice = fillResult.MaxPriceFilled
            }).Price;
            if (limitOrderSellPrice == 0)
            {
                break;
            }
        }

        if (amountOutOrderFilled > 0)
        {
            State.TokenContract.Approve.Send(new ApproveInput()
            {
                Spender = State.OrderContract.Value,
                Symbol = symbolIn,
                Amount = amountInOrderFilled
            });
            State.OrderContract.FillLimitOrder.Send(new FillLimitOrderInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                AmountIn = amountOutOrderFilled,
                MaxOpenIntervalPrice = maxOrderSellPrice,
                To = to
            });
        }
    }

    public override Empty SwapTokensForExactTokens(SwapTokensForExactTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsIn(swapInput.AmountOut, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[0] <= swapInput.AmountInMax, "Excessive Input amount");
            TransferFromSender(swapInput.Path[0], amounts[0], "Hooks Swap");
            for (var pathCount = 0; pathCount < swapInput.FeeRates.Count; pathCount++)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                var amountIn = amounts[pathCount];
                var amountOut = amounts[pathCount + 1];
                if (State.MatchLimitOrderEnabled.Value && (swapInput.FeeRates.Count == 1 || State.MultiSwapMatchLimitOrderEnabled.Value))
                {
                    MatchLimitOrder(swapContractAddress, pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self, 
                        swapInput.Path[pathCount], swapInput.Path[pathCount + 1], amounts[pathCount], amounts[pathCount + 1],
                        out var amountOutPoolFilled, out var amountInPoolFilled);
                    amountIn = amountInPoolFilled;
                    amountOut = amountOutPoolFilled;
                }
                
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[pathCount],
                    Amount = amountIn
                });
                var swapTokensForExactTokensInput = new Swap.SwapTokensForExactTokensInput()
                {
                    AmountInMax = amountIn,
                    AmountOut = amountOut,
                    Path = { swapInput.Path[pathCount], swapInput.Path[pathCount + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, nameof(SwapTokensForExactTokens), swapTokensForExactTokensInput.ToByteString());
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