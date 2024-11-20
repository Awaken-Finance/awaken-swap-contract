using AElf.Types;
using Awaken.Contracts.Token;
using Google.Protobuf.WellKnownTypes;
using AElf.Sdk.CSharp;
using Awaken.Contracts.Swap;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract
{
    public override GetSwapContractListOutput GetSwapContractList(Empty input)
    {
        return new GetSwapContractListOutput
        {
            SwapContractList = State.SwapContractInfoList.Value
        };
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

    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
    }
    
    public override GetLimitOrderConfigOutput GetLimitOrderConfig(Empty input)
    {
        return new GetLimitOrderConfigOutput()
        {
            MatchLimitOrderEnabled = State.MatchLimitOrderEnabled.Value,
            MultiSwapMatchLimitOrderEnabled = State.MultiSwapMatchLimitOrderEnabled.Value,
            MaxFillLimitOrderCount = State.MaxFillLimitOrderCount.Value
        };
    }

    public override GetAllReverseOutput GetAllReverse(GetAllReverseInput input)
    {
        var result = new GetAllReverseOutput();
        var getTokenInfoInput = new GetTokenInfoInput
        {
            Symbol = GetTokenPairSymbol(input.SymbolA, input.SymbolB)
        };
        var getReservesInput = new GetReservesInput
        {
            SymbolPair = { input.SymbolA + "-" + input.SymbolB}
        };
        foreach (var swapContractInfo in State.SwapContractInfoList.Value.SwapContracts)
        {
            var tokenInfo = Context.Call<TokenInfo>(swapContractInfo.LpTokenContractAddress, "GetTokenInfo", getTokenInfoInput);
            if (!string.IsNullOrWhiteSpace(tokenInfo.Symbol))
            {
                var reservesOutput = Context.Call<GetReservesOutput>(swapContractInfo.SwapContractAddress, "GetReserves", getReservesInput);
                var reservePairResult = reservesOutput.Results[0];
                result.Reverses.Add(new Reverse()
                {
                    FeeRate = swapContractInfo.FeeRate,
                    ReverseA = reservePairResult.ReserveA,
                    ReverseB = reservePairResult.ReserveB,
                    SymbolA = reservePairResult.SymbolA,
                    SymbolB = reservePairResult.SymbolB
                });
            }
        }
        return result;
    }

    public override Address GetOrderContract(Empty input)
    {
        return State.OrderContract.Value;
    }

    public override Address GetLabsFeeTo(Empty input)
    {
        return State.LabsFeeTo.Value;
    }

    public override Int64Value GetLabsFeeRate(Empty input)
    {
        return new Int64Value
        {
            Value = State.LabsFeeRate.Value
        };
    }
}