using System.Linq;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
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
                    Channel = "hooks",
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapExactTokensForTokensInput.Path.Add(swapInput.Path[index]);
                }

                Context.SendInline(swapContractAddress, "SwapExactTokensForTokens", swapExactTokensForTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
        }
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = "SwapExactTokensForTokens",
            Args = input.ToByteString()
        });
        return new Empty();
    }

    public override Empty SwapTokensForExactTokens(SwapTokensForExactTokensInput input)
    {
        Assert(input.SwapTokens.Count > 0, "Invalid input.");
        foreach (var swapInput in input.SwapTokens)
        {
            var amounts = GetAmountsIn(swapInput.AmountOut, swapInput.Path, swapInput.FeeRates);
            Assert(amounts[0] <= swapInput.AmountInMax, "Excessive Input amount");
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
                    Channel = "hooks",
                    To = pathCount == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                for (var index = beginIndex; index <= pathCount + 1; index++) {
                    swapTokensForExactTokensInput.Path.Add(swapInput.Path[index]);
                }
                Context.SendInline(swapContractAddress, "SwapTokensForExactTokens", swapTokensForExactTokensInput.ToByteString());
                beginIndex = pathCount + 1;
            }
        }
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = "SwapTokensForExactTokens",
            Args = input.ToByteString()
        });
        return new Empty();
    }
    
    public override Empty CreatePair(CreatePairInput input)
    {
        var swapContract = GetSwapContractInfo(input.FeeRate);
        Context.SendInline(swapContract.SwapContractAddress, "CreatePair", new Swap.CreatePairInput
        {
            SymbolPair = input.SymbolPair
        });
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = "CreatePair",
            Args = input.ToByteString()
        });
        return new Empty();
    }

    public override Empty AddLiquidity(AddLiquidityInput input)
    {
        var amounts = AddLiquidity(input.SymbolA, input.SymbolB, input.AmountADesired, input.AmountBDesired,
            input.AmountAMin, input.AmountBMin, input.FeeRate);
        var swapContractAddress = GetSwapContractInfo(input.FeeRate).SwapContractAddress;
        TransferFromSenderAndApprove(input.SymbolA, amounts[0], "Hooks AddLiquidity", swapContractAddress);
        TransferFromSenderAndApprove(input.SymbolB, amounts[1], "Hooks AddLiquidity", swapContractAddress);
        Context.SendInline(swapContractAddress, "AddLiquidity", new AddLiquidityInput
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
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = "AddLiquidity",
            Args = input.ToByteString()
        });
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
        Context.SendInline(swapContract.SwapContractAddress, "RemoveLiquidity", new RemoveLiquidityInput
        {
            SymbolA = input.SymbolA,
            SymbolB = input.SymbolB,
            AmountAMin = input.AmountAMin,
            AmountBMin = input.AmountBMin,
            LiquidityRemove = input.LiquidityRemove,
            Deadline = input.Deadline,
            To = input.To
        });
        Context.Fire(new HooksTransactionCreated()
        {
            Sender = Context.Sender,
            MethodName = "RemoveLiquidity",
            Args = input.ToByteString()
        });
        return new Empty();
    }
}