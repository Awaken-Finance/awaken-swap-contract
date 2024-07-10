using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
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
        Assert(input.SwapContractList != null && input.SwapContractList.SwapContracts.Count > 0, "Invalid input.");
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

    private void FillSwapContractInfoList(SwapContractInfoList inputSwapContractInfoList)
    {
        var swapContractInfoList = State.SwapContractInfoList.Value ??= new SwapContractInfoList();
        foreach (var contractInfo in inputSwapContractInfoList.SwapContracts)
        {
            var swapContractInfo = State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t => t.FeeRate == contractInfo.FeeRate);
            if (swapContractInfo == null)
            {
                swapContractInfo = new SwapContractInfo
                {
                    FeeRate = contractInfo.FeeRate,
                    SwapContractAddress = contractInfo.SwapContractAddress,
                    LpTokenContractAddress = contractInfo.LpTokenContractAddress
                };
                swapContractInfoList.SwapContracts.Add(swapContractInfo);
            }
            else
            {
                swapContractInfo.SwapContractAddress = contractInfo.SwapContractAddress;
                swapContractInfo.LpTokenContractAddress = contractInfo.LpTokenContractAddress;
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
                        t.FeeRate == swapInput.FeeRates[pathCount]).SwapContractAddress;
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
                        t.FeeRate == swapInput.FeeRates[pathCount]).SwapContractAddress;
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
            var amountIn = Context.Call<Int64Value>(swapContract.SwapContractAddress, "GetAmountIn", new GetAmountInInput()
            {
                AmountOut = amounts[0],
                SymbolIn = path[i - 1],
                SymbolOut = path[i]
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
            var amountOut = Context.Call<Int64Value>(swapContract.SwapContractAddress, "GetAmountOut", new GetAmountOutInput
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
        Context.SendInline(swapContract.SwapContractAddress, "CreatePair", new Swap.CreatePairInput
        {
            SymbolPair = input.SymbolPair
        });
        return new Empty();
    }

    public override Empty AddLiquidity(AddLiquidityInput input)
    {
        var amounts = AddLiquidity(input.SymbolA, input.SymbolB, input.AmountADesired, input.AmountBDesired,
            input.AmountAMin, input.AmountBMin, input.FeeRate);
        var swapContractAddress =
            State.SwapContractInfoList.Value.SwapContracts.First(t =>
                t.FeeRate == input.FeeRate).SwapContractAddress;
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
        return new Empty();
    }

    public override Empty RemoveLiquidity(RemoveLiquidityInput input)
    {
        var swapContract =
            State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                t.FeeRate == input.FeeRate);
        Assert(swapContract != null, "feeRate not existed");
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
        return new Empty();
    }
    
    private long[] AddLiquidity(string tokenA, string tokenB, long amountADesired, long amountBDesired,
        long amountAMin, long amountBMin, long feeRate)
    {
        var swapContract =
            State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                t.FeeRate == feeRate);
        Assert(swapContract != null, "feeRate not existed");

        long amountA;
        long amountB;
        var reservesOutput =
            Context.Call<GetReservesOutput>(swapContract.SwapContractAddress, "GetReserves", new GetReservesInput{
                SymbolPair = { tokenA + "-" + tokenB }
            });
        var reserves = reservesOutput.Results[0];
        if (reserves.ReserveA == 0 && reserves.ReserveB == 0)
        {
            // First time to add liquidity.
            amountA = amountADesired;
            amountB = amountBDesired;
        }
        else
        {
            // Not the first time, need to consider the changes of liquidity pool. 
            var amountBOptimal = Context.Call<Int64Value>(swapContract.SwapContractAddress, "Quote", new QuoteInput
            {
                SymbolA = tokenA,
                AmountA = amountADesired,
                SymbolB = tokenB,
            }).Value;
            if (amountBOptimal <= amountBDesired)
            {
                Assert(amountBOptimal >= amountBMin, $"Insufficient amount of token {tokenB}.");
                amountA = amountADesired;
                amountB = amountBOptimal;
            }
            else
            {
                var amountAOptimal = Context.Call<Int64Value>(swapContract.SwapContractAddress, "Quote", new QuoteInput
                {
                    SymbolA = tokenB,
                    SymbolB = tokenA,
                    AmountA = amountBDesired
                }).Value;
                Assert(amountAOptimal <= amountADesired);
                Assert(amountAOptimal >= amountAMin, $"Insufficient amount of token {tokenA}.");
                amountA = amountAOptimal;
                amountB = amountBDesired;
            }
        }

        return new[]
        {
            amountA, amountB
        };
    }
    
    private string GetTokenPairSymbol(string tokenA, string tokenB)
    {
        var symbols = SortSymbols(tokenA, tokenB);
        return $"ALP {symbols[0]}-{symbols[1]}";
    }

    private string[] SortSymbols(params string[] symbols)
    {
        Assert(
            symbols.Length == 2 && !symbols.First().All(IsValidItemIdChar) &&
            !symbols.Last().All(IsValidItemIdChar), "Invalid symbols for sorting.");
        return symbols.OrderBy(s => s).ToArray();
    }
    
    private bool IsValidItemIdChar(char character)
    {
        return character >= '0' && character <= '9';
    }
}