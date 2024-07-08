using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Swap;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Hooks;

public class AwakenHooksContract : AwakenHooksContractContainer.AwakenHooksContractBase
{
    public override Empty SetSwapContractAddress(SetSwapContractAddressInput input)
    {
        Assert(Context.Sender == State.Admin.Value, "No permission.");
        FillSwapContractInfoList(input.SwapContractList);
        return new Empty();
    }

    public override GetSwapContractListOutput GetSwapContractList(Empty input)
    {
        return new GetSwapContractListOutput
        {
            SwapContractList = State.SwapContractInfoList.Value
        };
    }

    public override Empty Initialize(InitializeInput input)
    {
        Assert(!State.Initialized.Value, "Already initialized.");
        State.TokenContract.Value =
            Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        FillSwapContractInfoList(input.SwapContractList);
        State.Admin.Value = input.Admin ?? Context.Sender;
        State.Initialized.Value = true;
        return new Empty();
    }

    private void FillSwapContractInfoList(SwapContractInfoList inputSwapContractInfoList)
    {
        Assert(inputSwapContractInfoList != null && inputSwapContractInfoList.SwapContracts.Count > 1, "Invalid input.");
        var swapContractInfoList = State.SwapContractInfoList.Value ??= new SwapContractInfoList();
        foreach (var contractInfo in inputSwapContractInfoList.SwapContracts)
        {
            var swapContractInfo = State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t => t.FeeRate == contractInfo.FeeRate);
            if (swapContractInfo == null)
            {
                swapContractInfo = new SwapContractInfo
                {
                    FeeRate = contractInfo.FeeRate,
                    ContractAddress = contractInfo.ContractAddress
                };
                swapContractInfoList.SwapContracts.Add(swapContractInfo);
            }
            else
            {
                swapContractInfo.ContractAddress = contractInfo.ContractAddress;
            }
        }
        State.SwapContractInfoList.Value = swapContractInfoList;
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
                var swapContractAddress =
                    State.SwapContractInfoList.Value.SwapContracts.First(t =>
                        t.FeeRate == swapInput.FeeRates[pathCount]).ContractAddress;
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
                var swapContractAddress =
                    State.SwapContractInfoList.Value.SwapContracts.First(t =>
                        t.FeeRate == swapInput.FeeRates[pathCount]).ContractAddress;
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
                Context.SendInline(swapContractAddress, "SwapTokensForExactTokens", swapExactTokensForTokensInput.ToByteString());
            }
        }
        return new Empty();
    }

    public override GetAmountsOutOutput GetAmountsOut(GetAmountsOutInput input)
    {
        return new GetAmountsOutOutput()
        {
            Amount = { GetAmountsOut(input.AmountIn, input.Path, input.FeeRates) }
        };
    }

    public override GetAmountsInOutput GetAmountsIn(GetAmountsInInput input)
    {
        return new GetAmountsInOutput
        {
            Amount = { GetAmountsIn(input.AmountOut, input.Path, input.FeeRates) }
        };
    }
    
    private RepeatedField<long> GetAmountsIn(long amountOut, RepeatedField<string> path, RepeatedField<long> feeRates)
    {
        Assert(path.Count >= 2, "Invalid path");
        Assert(path.Count == feeRates.Count + 1, "invalid feeRates");
        var amounts = new RepeatedField<long>() {amountOut};
        for (var i = path.Count - 1; i > 0; i--)
        {
            var feeRate = feeRates[i - 1];
            var swapContract =
                State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                    t.FeeRate == feeRate);
            Assert(swapContract != null, "feeRate not existed");
            var amountIn = Context.Call<Int64Value>(swapContract.ContractAddress, "GetAmountIn", new GetAmountInInput()
            {
                AmountOut = amounts[0],
                SymbolIn = path[i],
                SymbolOut = path[i+1]
            }.ToByteString());
            amounts.Insert(0, amountIn.Value);
        }

        return amounts;
    }

    private RepeatedField<long> GetAmountsOut(long amountIn, RepeatedField<string> path, RepeatedField<long> feeRates)
    {
        Assert(path.Count >= 2, "Invalid path");
        Assert(path.Count == feeRates.Count + 1, "invalid feeRates");
        var amounts = new RepeatedField<long> {amountIn};
        for (var i = 0; i < path.Count - 1; i++)
        {
            var feeRate = feeRates[i];
            var swapContract =
                State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                    t.FeeRate == feeRate);
            Assert(swapContract != null, "feeRate not existed");
            var amountOut = Context.Call<Int64Value>(swapContract.ContractAddress, "GetAmountOut", new GetAmountOutInput
            {
                AmountIn = amounts[i],
                SymbolIn = path[i],
                SymbolOut = path[i+1]
            }.ToByteString());
            amounts.Add(amountOut.Value);
        }
        return amounts;
    }

    public override Empty CreatePair(CreatePairInput input)
    {
        var swapContract =
            State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                t.FeeRate == input.FeeRate);
        Assert(swapContract != null, "feeRate not existed");
        Context.SendInline(swapContract.ContractAddress, "CreatePair", new Swap.CreatePairInput
        {
            SymbolPair = input.SymbolPair
        });
        return base.CreatePair(input);
    }

    public override Empty AddLiquidity(AddLiquidityInput input)
    {
        var swapContract =
            State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                t.FeeRate == input.FeeRate);
        Assert(swapContract != null, "feeRate not existed");
        Context.SendInline(swapContract.ContractAddress, "AddLiquidity", new AddLiquidityInput
        {
            AmountADesired = input.AmountADesired,
            AmountAMin = input.AmountAMin,
            SymbolA = input.SymbolA,
            AmountBDesired = input.AmountBDesired,
            AmountBMin = input.AmountBMin,
            SymbolB = input.SymbolB,
            Channel = input.Channel,
            Deadline = input.Deadline,
            To = input.To
        });
        return base.AddLiquidity(input);
    }

    public override Empty RemoveLiquidity(RemoveLiquidityInput input)
    {
        return base.RemoveLiquidity(input);
    }
}