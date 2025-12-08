using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract
    {
        public override StringList GetPairs(Empty input)
        {
            return State.PairList.Value;
        }

        public override GetReservesOutput GetReserves(GetReservesInput input)
        {
            var length = input.SymbolPair.Count;
            var results = new GetReservesOutput();
            for (var i = 0; i < length; i++)
            {
                var tokens = ExtractTokenPair(input.SymbolPair[i]);
                ValidPairSymbol(tokens[0], tokens[1]);
                var pairAddress = State.PairVirtualAddressMap[tokens[0]][tokens[1]];
                var reserves = GetReserves(pairAddress, tokens[0], tokens[1]);
                results.Results.Add(new ReservePairResult()
                {
                    SymbolPair = input.SymbolPair[i],
                    SymbolA = tokens[0],
                    SymbolB = tokens[1],
                    ReserveA = reserves[0],
                    ReserveB = reserves[1],
                    BlockTimestampLast = State.LastBlockTimestampMap[pairAddress]
                });
            }

            return results;
        }
        

        public override GetTotalSupplyOutput GetTotalSupply(StringList input)
        {
            var length = input.Value.Count;
            var results = new GetTotalSupplyOutput();
            for (var i = 0; i < length; i++)
            {
                var tokens = ExtractTokenPair(input.Value[i]);
                ValidPairSymbol(tokens[0], tokens[1]);
                var lpTokenSymbol = GetTokenPairSymbol(tokens[0], tokens[1]);
                var lpTokenInfo = State.LPTokenContract.GetTokenInfo.Call(new Token.GetTokenInfoInput
                {
                    Symbol = lpTokenSymbol
                });
                results.Results.Add(new TotalSupplyResult()
                {
                    SymbolPair = input.Value[i],
                    TotalSupply = lpTokenInfo.Supply
                });
            }

            return results;
        }

        public override Int64Value Quote(QuoteInput input)
        {
            var symbols = SortSymbols(input.SymbolA, input.SymbolB);
            Assert(State.PairVirtualAddressMap[symbols[0]][symbols[1]] != null, "Pair not exists");
            var pairAddress = State.PairVirtualAddressMap[symbols[0]][symbols[1]];
            var reserves = GetReserves(pairAddress, input.SymbolA, input.SymbolB);
            var amountB = Quote(input.AmountA, reserves[0], reserves[1]);
            return new Int64Value
            {
                Value = amountB
            };
        }

        public override Int64Value GetAmountIn(GetAmountInInput input)
        {
            var symbols = SortSymbols(input.SymbolIn, input.SymbolOut);
            Assert(State.PairVirtualAddressMap[symbols[0]][symbols[1]] != null, "Pair not exists");
            var pairAddress = State.PairVirtualAddressMap[symbols[0]][symbols[1]];
            var reserves = GetReserves(pairAddress, input.SymbolIn, input.SymbolOut);
            var amountIn = GetAmountIn(input.AmountOut, reserves[0], reserves[1]);
            return new Int64Value
            {
                Value = amountIn
            };
        }

        public override Int64Value GetAmountOut(GetAmountOutInput input)
        {
            var symbols = SortSymbols(input.SymbolIn, input.SymbolOut);
            Assert(State.PairVirtualAddressMap[symbols[0]][symbols[1]] != null, "Pair not exists");
            var pairAddress = State.PairVirtualAddressMap[symbols[0]][symbols[1]];
            var reserves = GetReserves(pairAddress, input.SymbolIn, input.SymbolOut);
            var amountOut = GetAmountOut(input.AmountIn, reserves[0], reserves[1]);
            return new Int64Value()
            {
                Value = amountOut
            };
        }

        public override GetAmountsInOutput GetAmountsIn(GetAmountsInInput input)
        {
            return new GetAmountsInOutput()
            {
                Amount = {GetAmountsIn(input.AmountOut, input.Path)}
            };
        }

        public override GetAmountsOutOutput GetAmountsOut(GetAmountsOutInput input)
        {
            return new GetAmountsOutOutput()
            {
                Amount = {GetAmountsOut(input.AmountIn, input.Path)}
            };
        }

        public override BigIntValue GetKLast(Address pair)
        {
            return State.KValueMap[pair];
        }

        public override Address GetAdmin(Empty input)
        {
            return State.Admin.Value;
        }

        public override Address GetFeeTo(Empty input)
        {
            return State.FeeTo.Value;
        }

        public override Int64Value GetFeeRate(Empty input)
        {
            return new Int64Value()
            {
                Value = State.FeeRate.Value
            };
        }

        public override Address GetPairAddress(GetPairAddressInput input)
        {
            return GetPairAddress(input.SymbolA, input.SymbolB);
        }

    }
}