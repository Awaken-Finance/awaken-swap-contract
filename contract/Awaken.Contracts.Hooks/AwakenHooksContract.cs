using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

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
    
    public override Empty AddSwapContractInfo(AddSwapContractInfoInput input)
    {
        Assert(Context.Sender == State.Admin.Value, "No permission.");
        Assert(input.SwapContractList != null && input.SwapContractList.SwapContracts.Count > 0, "Invalid input.");
        FillSwapContractInfoList(input.SwapContractList);
        return new Empty();
    }

    public override Empty RemoveSwapContractInfo(RemoveSwapContractInfoInput input)
    {
        Assert(Context.Sender == State.Admin.Value, "No permission.");
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
            State.TokenContract.TransferFrom.Send(new TransferFromInput
            {
                Symbol = swapInput.Path[0],
                Amount = swapInput.AmountIn,
                From = Context.Sender,
                To = Context.Self,
                Memo = "Hooks Swap"
            });
            for (var pathCount = 0; pathCount < swapInput.Path.Count - 1; pathCount++)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[pathCount],
                    Amount = amounts[pathCount]
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amounts[pathCount],
                    AmountOutMin = amounts[pathCount + 1],
                    Path = { swapInput.Path[pathCount], swapInput.Path[pathCount + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = "hooks",
                    To = pathCount == swapInput.Path.Count - 2 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, "SwapExactTokensForTokens", swapExactTokensForTokensInput.ToByteString());
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
            State.TokenContract.TransferFrom.Send(new TransferFromInput()
            {
                Symbol = swapInput.Path[0],
                Amount = amounts[0],
                From = Context.Sender,
                To = Context.Self,
                Memo = "Hooks Swap"
            });
            for (var pathCount = 0; pathCount < swapInput.Path.Count - 1; pathCount++)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[pathCount],
                    Amount = amounts[pathCount]
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amounts[pathCount],
                    AmountOutMin = amounts[pathCount + 1],
                    Path = { swapInput.Path[pathCount], swapInput.Path[pathCount + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = "hooks",
                    To = pathCount == swapInput.Path.Count - 2 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, "SwapExactTokensForTokens", swapExactTokensForTokensInput.ToByteString());
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
        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            Symbol = input.SymbolA,
            Amount = amounts[0],
            From = Context.Sender,
            To = Context.Self,
            Memo = "Hooks AddLiquidity"
        });
        State.TokenContract.Approve.Send(new ApproveInput
        {
            Symbol = input.SymbolA,
            Amount = amounts[0],
            Spender = swapContractAddress
        });
        State.TokenContract.TransferFrom.Send(new TransferFromInput
        {
            Symbol = input.SymbolB,
            Amount = amounts[1],
            From = Context.Sender,
            To = Context.Self,
            Memo = "Hooks AddLiquidity"
        });
        State.TokenContract.Approve.Send(new ApproveInput
        {
            Symbol = input.SymbolB,
            Amount = amounts[1],
            Spender = swapContractAddress
        });
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
        Context.SendInline(swapContract.LpTokenContractAddress, "TransferFrom", new Token.TransferFromInput
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