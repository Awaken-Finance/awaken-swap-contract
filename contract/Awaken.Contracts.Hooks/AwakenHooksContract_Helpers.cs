using System.Linq;
using Awaken.Contracts.Swap;
using AElf.Sdk.CSharp;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract
{
    private SwapContractInfo GetSwapContractInfo(long feeRate)
    {
        var swapContract =
            State.SwapContractInfoList.Value.SwapContracts.FirstOrDefault(t =>
                t.FeeRate == feeRate);
        Assert(swapContract != null, "feeRate not existed");
        return swapContract;
    }

    private RepeatedField<long> GetAmountsOut(long amountIn, RepeatedField<string> path, RepeatedField<long> feeRates)
    {
        Assert(path.Count >= 2, "Invalid path");
        Assert(path.Count == feeRates.Count + 1, "invalid feeRates");
        var amounts = new RepeatedField<long> {amountIn};
        for (var i = 0; i < path.Count - 1; i++)
        {
            var swapContract = GetSwapContractInfo(feeRates[i]);
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
    
    private RepeatedField<long> GetAmountsIn(long amountOut, RepeatedField<string> path, RepeatedField<long> feeRates)
    {
        Assert(path.Count >= 2, "Invalid path");
        Assert(path.Count == feeRates.Count + 1, "invalid feeRates");
        var amounts = new RepeatedField<long>() {amountOut};
        for (var i = path.Count - 1; i > 0; i--)
        {
            var swapContract = GetSwapContractInfo(feeRates[i - 1]);
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
    
    private long[] AddLiquidity(string tokenA, string tokenB, long amountADesired, long amountBDesired,
        long amountAMin, long amountBMin, long feeRate)
    {
        var swapContract = GetSwapContractInfo(feeRate);
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