using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using System;
using System.Linq;
using System.Threading;
using AElf.ContractTestBase.ContractTestKit;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Shouldly;
using CreateInput = AElf.Contracts.MultiToken.CreateInput;

namespace Awaken.Contracts.Swap.Tests
{
    public class AwakenSwapContractTests : AwakenSwapContractTestBase
    {
        [Fact]
        public async Task CompleteFlowTest()
        {
            await CreateAndGetToken();


            await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
            {
                Owner = AwakenSwapContractAddress
            });

            await AwakenSwapContractStub.Initialize.SendAsync(new InitializeInput()
            {
                Admin = AdminAddress,
                AwakenTokenContractAddress = LpTokentContractAddress
            });

            await AdminLpStub.SetAdmin.SendAsync(AdminAddress);
            await AdminLpStub.AddWhiteList.SendAsync(AwakenSwapContractAddress);

            var noPermissionException= await TomLpStub.SetAdmin.SendWithExceptionAsync(UserTomAddress);
            noPermissionException.TransactionResult.Error.ShouldContain("no permission");
            
            var existException = await AdminLpStub.AddWhiteList.SendWithExceptionAsync(AwakenSwapContractAddress);
            existException.TransactionResult.Error.ShouldContain("exist");

            await AwakenSwapContractStub.SetFeeRate.SendAsync(new Int64Value() { Value = 30 });
            var feeRate = await AwakenSwapContractStub.GetFeeRate.CallAsync(new Empty());
            feeRate.Value.ShouldBe((30));
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-TEST"
            });

            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-DAI"
            });
            var pairList = await UserTomStub.GetPairs.CallAsync(new Empty());
            pairList.Value.ShouldContain("ELF-TEST");
            pairList.Value.ShouldContain("DAI-ELF");

            #region AddLiquidity

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = 100000000,
                AmountAMin = 100000000,
                AmountBDesired = 200000000,
                AmountBMin = 200000000,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var reservesBegin1 = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-DAI" }
            });
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = 100000000,
                AmountAMin = 100000000,
                AmountBDesired = 200000000,
                AmountBMin = 200000000,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "DAI",
                To = UserTomAddress
            });


            var pair = SortSymbols("ELF", "DAI");


            var reserves = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST", "ELF-DAI" }
            });

            var reserveA = reserves.Results[0].ReserveA;
            var reserveB = reserves.Results[0].ReserveB;
            reserveA.ShouldBe(100000000);
            reserveB.ShouldBe(200000000);


            var balance = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            //token=math.sqrt(reserveA*reserveB)
            balance.Symbol.ShouldBe("ALP ELF-TEST");
            var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(reserveA * reserveB)).Value);
            balance.Amount.ShouldBe(balanceExpect);

            var totalSupply = UserTomStub.GetTotalSupply.CallAsync(new StringList()
            {
                Value = { "ELF-TEST" }
            });
            totalSupply.Result.Results[0].SymbolPair.ShouldBe("ELF-TEST");
            totalSupply.Result.Results[0].TotalSupply.ShouldBe(balanceExpect + 1);

            #endregion

            #region RemoveLiquidity

            await TomLpStub.Approve.SendAsync(new Token.ApproveInput()
            {
                Symbol = "ALP ELF-TEST",
                Spender = AwakenSwapContractAddress,
                Amount = int.MaxValue
            });
            var result = await UserTomStub.RemoveLiquidity.SendAsync(new RemoveLiquidityInput()
            {
                LiquidityRemove = balanceExpect,
                AmountAMin = Convert.ToInt64(100000000 * 0.995),
                AmountBMin = Convert.ToInt64(200000000 * 0.995),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
            });
            var amountA = balanceExpect.Mul(reserveA).Div(totalSupply.Result.Results[0].TotalSupply);
            var amountB = balanceExpect.Mul(reserveB).Div(totalSupply.Result.Results[0].TotalSupply);
            result.Output.SymbolA.ShouldBe("ELF");
            result.Output.SymbolB.ShouldBe("TEST");
            result.Output.AmountA.ShouldBe(amountA);
            result.Output.AmountB.ShouldBe(amountB);

            var balanceAfter = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            balanceAfter.Amount.ShouldBe(0);

            #endregion

            #region Swap

            var amountOut = 20000000;
            var amountIn = await UserTomStub.GetAmountIn.CallAsync(new GetAmountInInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "DAI",
                AmountOut = amountOut
            });
            var reserveElf = Convert.ToDecimal(reserves.Results[1].ReserveB);
            var reserveDai = reserves.Results[1].ReserveA;
            var numerator = reserveElf * amountOut * 10000;
            var denominator = (reserveDai - amountOut) * 9970;
            var amountInExpect = decimal.ToInt64(numerator / denominator) + 1;

            amountIn.Value.ShouldBe(amountInExpect);


            var balanceTomElfBefore = UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var balanceTomDaiBefore = UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "DAI"
                });

            var reservesBegin = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-DAI" }
            });

            var reserveA0 = reservesBegin.Results[0].ReserveA;
            var reserveB0 = reservesBegin.Results[0].ReserveB;
            await UserTomStub.SwapTokensForExactTokens.SendAsync(new SwapTokensForExactTokensInput()
            {
                AmountOut = amountOut,
                AmountInMax = amountInExpect,
                Path = { "ELF", "DAI" },
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
            });
            var balanceTomElfAfter = UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var balanceTomDaiAfter = UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "DAI"
                });
            balanceTomElfAfter.Result.Balance.ShouldBe(balanceTomElfBefore.Result.Balance.Sub(amountIn.Value));
            balanceTomDaiAfter.Result.Balance.ShouldBe(balanceTomDaiBefore.Result.Balance.Add(amountOut));

            #endregion
        }

        [Fact]
        public async Task AddLiquidityTest()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            const long errorInput = 0;

            #region Exceptions

            //Expired 
            var expiredException = await UserTomStub.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(-1, 0, -1))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            expiredException.TransactionResult.Error.ShouldContain("Expired");

            //Invalid Input
            var amountADesiredInputException = await UserTomStub.AddLiquidity.SendWithExceptionAsync(
                new AddLiquidityInput()
                {
                    AmountADesired = errorInput,
                    AmountAMin = amountADesired,
                    AmountBDesired = amountBDesired,
                    AmountBMin = amountBDesired,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST",
                    To = UserTomAddress
                });
            amountADesiredInputException.TransactionResult.Error.ShouldContain("Invalid Input");

            var amountBDesiredInputException = await UserTomStub.AddLiquidity.SendWithExceptionAsync(
                new AddLiquidityInput()
                {
                    AmountADesired = amountADesired,
                    AmountAMin = amountADesired,
                    AmountBDesired = errorInput,
                    AmountBMin = amountBDesired,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST",
                    To = UserTomAddress
                });
            amountBDesiredInputException.TransactionResult.Error.ShouldContain("Invalid Input");

            #endregion


            #region AddLiquidity at first time

            var liquidityBalanceBefore = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            var reservesBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyBefore = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST")
            });
            var elfBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountADesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });

            var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
            var reservesAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });

            var totalSupplyAfter = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST")
            });
            var elfBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            var testContractBalance = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = AwakenSwapContractAddress,
                    Symbol = "TEST"
                });


            var liquidityBalanceAfter = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });

            reservesAfter.Results[0].ReserveA.ShouldBe(reservesBefore.Results[0].ReserveA.Add(amountADesired));
            reservesAfter.Results[0].ReserveB.ShouldBe(reservesBefore.Results[0].ReserveB.Add(amountBDesired));

            liquidityBalanceAfter.Amount
                .ShouldBe(liquidityBalanceBefore.Amount.Add(balanceExpect));
            totalSupplyAfter.Supply.ShouldBe(totalSupplyBefore.Supply
                .Add(balanceExpect).Add(1));

            elfBalanceAfter.Balance.ShouldBe(elfBalanceBefore.Balance.Sub(amountADesired));
            testBalanceAfter.Balance.ShouldBe(testBalanceBefore.Balance.Sub(amountBDesired));
            testContractBalance.Balance.ShouldBe(amountBDesired);

            #endregion

            Thread.Sleep(3000);

            #region AddLiquidity at second time

            const long amountADesiredSecond = 100000000;
            const long amountBDesiredSecond = 200000000;
            const long floatAmount = 1000;

            var amountBOptimal = decimal.ToInt64(Convert.ToDecimal(amountADesiredSecond) *
                reservesAfter.Results[0].ReserveB / reservesAfter.Results[0].ReserveA);
            var amountAOptimal = decimal.ToInt64(Convert.ToDecimal(amountBDesiredSecond) *
                reservesAfter.Results[0].ReserveA / reservesAfter.Results[0].ReserveB);

            //Insufficient amount of tokenB
            var tokenBException = await UserTomStub.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond,
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBOptimal.Add(floatAmount),
                AmountBMin = amountBOptimal.Add(floatAmount),
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            tokenBException.TransactionResult.Error.ShouldContain("Insufficient amount of token TEST");

            //Insufficient amount of tokenA
            var tokenAException = await UserTomStub.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput()
            {
                AmountADesired = amountAOptimal.Add(floatAmount),
                AmountAMin = amountAOptimal.Add(floatAmount),
                AmountBDesired = amountBDesiredSecond,
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            tokenAException.TransactionResult.Error.ShouldContain("Insufficient amount of token ELF");

            //success
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond.Add(floatAmount),
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond,
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var amountASecond = amountADesiredSecond;
            var amountBSecond = amountBOptimal;
            var liquidityFromElf = Convert.ToInt64(new BigIntValue(amountASecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveA).Value);
            var liquidityFromTest = Convert.ToInt64(new BigIntValue(amountBSecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveB).Value);

            var liquidityMintSecond = Math.Min(liquidityFromElf, liquidityFromTest);

            var reservesAfterSecond = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyAfterSecond = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST")
            });
            var elfBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            var liquidityBalanceSecond = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });

            reservesAfterSecond.Results[0].ReserveA.ShouldBe(reservesAfter.Results[0].ReserveA.Add(amountASecond));
            reservesAfterSecond.Results[0].ReserveB.ShouldBe(reservesAfter.Results[0].ReserveB.Add(amountBSecond));

            liquidityBalanceSecond.Amount
                .ShouldBe(liquidityBalanceAfter.Amount.Add(liquidityMintSecond));
            totalSupplyAfterSecond.Supply.ShouldBe(totalSupplyAfter.Supply
                .Add(liquidityMintSecond));

            elfBalanceAfterSecond.Balance.ShouldBe(elfBalanceAfter.Balance.Sub(amountASecond));
            testBalanceAfterSecond.Balance.ShouldBe(testBalanceAfter.Balance.Sub(amountBSecond));
            //third time  to cover AddLiquidity 
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond,
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond.Add(floatAmount),
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var elfBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });

            elfBalanceAfterThird.Balance.ShouldBe(elfBalanceAfterSecond.Balance.Sub(amountADesiredSecond));
            testBalanceAfterThird.Balance.ShouldBe(testBalanceAfterSecond.Balance.Sub(amountBDesiredSecond));

            #endregion

            //fee on


            await AwakenSwapContractStub.SetFeeTo.SendAsync(UserTomAddress);
            var feeto = await AwakenSwapContractStub.GetFeeTo.CallAsync(new Empty());
            feeto.ShouldBe(UserTomAddress);
            var amountIn = amountADesiredSecond;
            var reserveBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var amountInWithFee = Convert.ToDecimal(amountIn) * 9970;
            var numerator = amountInWithFee * Convert.ToDecimal(reserveBefore.Results[0].ReserveB);
            var denominator = Convert.ToDecimal(reserveBefore.Results[0].ReserveA * 10000) + amountInWithFee;
            var amountOutExpect = decimal.ToInt64(numerator / denominator);
            await UserTomStub.SwapExactTokensForTokensSupportingFeeOnTransferTokens.SendAsync(
                new SwapExactTokensForTokensSupportingFeeOnTransferTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = amountIn,
                    AmountOutMin = amountOutExpect,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
                });

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond.Add(floatAmount),
                AmountAMin = 1,
                AmountBDesired = amountBDesiredSecond,
                AmountBMin = 1,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
        }

        [Fact]
        public async Task AddLiquidityTest_FT_NFT()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            const long errorInput = 0;

            #region AddLiquidity at first time

            var liquidityBalanceBefore = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1"),
                Owner = UserTomAddress
            });
            var reservesBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-ELF" }
            });
            var totalSupplyBefore = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1")
            });
            var elfBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountADesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "ELF",
                To = UserTomAddress
            });

            var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
            var reservesAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-ELF" }
            });

            var totalSupplyAfter = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1")
            });
            var elfBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });
            var testContractBalance = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = AwakenSwapContractAddress,
                    Symbol = "AAA-1"
                });


            var liquidityBalanceAfter = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1"),
                Owner = UserTomAddress
            });

            reservesAfter.Results[0].ReserveA.ShouldBe(reservesBefore.Results[0].ReserveA.Add(amountADesired));
            reservesAfter.Results[0].ReserveB.ShouldBe(reservesBefore.Results[0].ReserveB.Add(amountBDesired));

            liquidityBalanceAfter.Amount
                .ShouldBe(liquidityBalanceBefore.Amount.Add(balanceExpect));
            totalSupplyAfter.Supply.ShouldBe(totalSupplyBefore.Supply
                .Add(balanceExpect).Add(1));

            testBalanceAfter.Balance.ShouldBe(elfBalanceBefore.Balance.Sub(amountADesired));
            elfBalanceAfter.Balance.ShouldBe(testBalanceBefore.Balance.Sub(amountBDesired));
            testContractBalance.Balance.ShouldBe(amountADesired);

            #endregion

            Thread.Sleep(3000);

            #region AddLiquidity at second time

            const long amountADesiredSecond = 100000000;
            const long amountBDesiredSecond = 200000000;
            const long floatAmount = 1000;

            var amountBOptimal = decimal.ToInt64(Convert.ToDecimal(amountADesiredSecond) *
                reservesAfter.Results[0].ReserveB / reservesAfter.Results[0].ReserveA);
            var amountAOptimal = decimal.ToInt64(Convert.ToDecimal(amountBDesiredSecond) *
                reservesAfter.Results[0].ReserveA / reservesAfter.Results[0].ReserveB);

            //success
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond.Add(floatAmount),
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond,
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "ELF",
                To = UserTomAddress
            });
            var amountASecond = amountADesiredSecond;
            var amountBSecond = amountBOptimal;
            var liquidityFromElf = Convert.ToInt64(new BigIntValue(amountASecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveA).Value);
            var liquidityFromTest = Convert.ToInt64(new BigIntValue(amountBSecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveB).Value);

            var liquidityMintSecond = Math.Min(liquidityFromElf, liquidityFromTest);

            var reservesAfterSecond = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-ELF" }
            });
            var totalSupplyAfterSecond = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1")
            });
            var elfBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });
            var liquidityBalanceSecond = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "AAA-1"),
                Owner = UserTomAddress
            });

            reservesAfterSecond.Results[0].ReserveA.ShouldBe(reservesAfter.Results[0].ReserveA.Add(amountASecond));
            reservesAfterSecond.Results[0].ReserveB.ShouldBe(reservesAfter.Results[0].ReserveB.Add(amountBSecond));

            liquidityBalanceSecond.Amount
                .ShouldBe(liquidityBalanceAfter.Amount.Add(liquidityMintSecond));
            totalSupplyAfterSecond.Supply.ShouldBe(totalSupplyAfter.Supply
                .Add(liquidityMintSecond));

            testBalanceAfterSecond.Balance.ShouldBe(testBalanceAfter.Balance.Sub(amountASecond));
            elfBalanceAfterSecond.Balance.ShouldBe(elfBalanceAfter.Balance.Sub(amountBSecond));
            //third time  to cover AddLiquidity 
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond,
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond.Add(floatAmount),
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "ELF",
                To = UserTomAddress
            });
            var elfBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });

            testBalanceAfterThird.Balance.ShouldBe(testBalanceAfterSecond.Balance.Sub(amountADesiredSecond));
            elfBalanceAfterThird.Balance.ShouldBe(elfBalanceAfterSecond.Balance.Sub(amountBDesiredSecond));

            #endregion
        }

        [Fact]
        public async Task AddLiquidityTest_NFT_NFT()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            const long errorInput = 0;

            #region AddLiquidity at first time

            var liquidityBalanceBefore = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1"),
                Owner = UserTomAddress
            });
            var reservesBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-BBB-1" }
            });
            var totalSupplyBefore = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1")
            });
            var elfBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "BBB-1"
                });
            var testBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountADesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "BBB-1",
                To = UserTomAddress
            });

            var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
            var reservesAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-BBB-1" }
            });

            var totalSupplyAfter = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1")
            });
            var elfBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "BBB-1"
                });
            var testBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });
            var testContractBalance = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = AwakenSwapContractAddress,
                    Symbol = "AAA-1"
                });


            var liquidityBalanceAfter = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1"),
                Owner = UserTomAddress
            });

            reservesAfter.Results[0].ReserveA.ShouldBe(reservesBefore.Results[0].ReserveA.Add(amountADesired));
            reservesAfter.Results[0].ReserveB.ShouldBe(reservesBefore.Results[0].ReserveB.Add(amountBDesired));

            liquidityBalanceAfter.Amount
                .ShouldBe(liquidityBalanceBefore.Amount.Add(balanceExpect));
            totalSupplyAfter.Supply.ShouldBe(totalSupplyBefore.Supply
                .Add(balanceExpect).Add(1));

            testBalanceAfter.Balance.ShouldBe(elfBalanceBefore.Balance.Sub(amountADesired));
            elfBalanceAfter.Balance.ShouldBe(testBalanceBefore.Balance.Sub(amountBDesired));
            testContractBalance.Balance.ShouldBe(amountADesired);

            #endregion

            Thread.Sleep(3000);

            #region AddLiquidity at second time

            const long amountADesiredSecond = 100000000;
            const long amountBDesiredSecond = 200000000;
            const long floatAmount = 1000;

            var amountBOptimal = decimal.ToInt64(Convert.ToDecimal(amountADesiredSecond) *
                reservesAfter.Results[0].ReserveB / reservesAfter.Results[0].ReserveA);
            var amountAOptimal = decimal.ToInt64(Convert.ToDecimal(amountBDesiredSecond) *
                reservesAfter.Results[0].ReserveA / reservesAfter.Results[0].ReserveB);

            //success
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond.Add(floatAmount),
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond,
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "BBB-1",
                To = UserTomAddress
            });
            var amountASecond = amountADesiredSecond;
            var amountBSecond = amountBOptimal;
            var liquidityFromElf = Convert.ToInt64(new BigIntValue(amountASecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveA).Value);
            var liquidityFromTest = Convert.ToInt64(new BigIntValue(amountBSecond).Mul(totalSupplyAfter.Supply)
                .Div(reservesAfter.Results[0].ReserveB).Value);

            var liquidityMintSecond = Math.Min(liquidityFromElf, liquidityFromTest);

            var reservesAfterSecond = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "AAA-1-BBB-1" }
            });
            var totalSupplyAfterSecond = await AdminLpStub.GetTokenInfo.CallAsync(new Token.GetTokenInfoInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1")
            });
            var elfBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "BBB-1"
                });
            var testBalanceAfterSecond = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });
            var liquidityBalanceSecond = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("BBB-1", "AAA-1"),
                Owner = UserTomAddress
            });

            reservesAfterSecond.Results[0].ReserveA.ShouldBe(reservesAfter.Results[0].ReserveA.Add(amountASecond));
            reservesAfterSecond.Results[0].ReserveB.ShouldBe(reservesAfter.Results[0].ReserveB.Add(amountBSecond));

            liquidityBalanceSecond.Amount
                .ShouldBe(liquidityBalanceAfter.Amount.Add(liquidityMintSecond));
            totalSupplyAfterSecond.Supply.ShouldBe(totalSupplyAfter.Supply
                .Add(liquidityMintSecond));

            testBalanceAfterSecond.Balance.ShouldBe(testBalanceAfter.Balance.Sub(amountASecond));
            elfBalanceAfterSecond.Balance.ShouldBe(elfBalanceAfter.Balance.Sub(amountBSecond));
            //third time  to cover AddLiquidity 
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesiredSecond,
                AmountAMin = amountADesiredSecond,
                AmountBDesired = amountBDesiredSecond.Add(floatAmount),
                AmountBMin = amountBDesiredSecond,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "AAA-1",
                SymbolB = "BBB-1",
                To = UserTomAddress
            });
            var elfBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "BBB-1"
                });
            var testBalanceAfterThird = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "AAA-1"
                });

            testBalanceAfterThird.Balance.ShouldBe(testBalanceAfterSecond.Balance.Sub(amountADesiredSecond));
            elfBalanceAfterThird.Balance.ShouldBe(elfBalanceAfterSecond.Balance.Sub(amountBDesiredSecond));

            #endregion
        }

        [Fact]
        public async Task RemoveLiquidityTest()
        {
            await Initialize();

            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            const long liquidityRemove = 200000000;
            const long floatAmount = 10000;
            const long errorInput = 0;



            await TomLpStub.Approve.SendAsync(new Token.ApproveInput()
            {
                Symbol = "ALP ELF-TEST",
                Spender = AwakenSwapContractAddress,
                Amount = int.MaxValue
            });

            #region Exceptions

            //Expired 
            var expiredException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(new RemoveLiquidityInput()
            {
                AmountAMin = amountADesired,
                AmountBMin = amountBDesired,
                LiquidityRemove = liquidityRemove,
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(-1, 0, -1))),
                SymbolA = "ELF",
                SymbolB = "TEST"
            });
            expiredException.TransactionResult.Error.ShouldContain("Expired");

            //Invalid Input

            var liquidityRemoveInvalidException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(
                new RemoveLiquidityInput()
                {
                    AmountAMin = amountADesired,
                    AmountBMin = amountBDesired,
                    LiquidityRemove = errorInput,
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST"
                });
            liquidityRemoveInvalidException.TransactionResult.Error.ShouldContain("Invalid Input");

            // Pair not exists
            var pairException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(new RemoveLiquidityInput()
            {
                AmountAMin = amountADesired,
                AmountBMin = amountBDesired,
                LiquidityRemove = liquidityRemove,
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "INVALID"
            });
            pairException.TransactionResult.Error.ShouldContain("Pair ELF-INVALID does not exist");

            //Insufficient LiquidityToken
            var zeroLiquidityException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(
                new RemoveLiquidityInput()
                {
                    AmountAMin = amountADesired,
                    AmountBMin = amountBDesired,
                    LiquidityRemove = liquidityRemove,
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST"
                });
            zeroLiquidityException.TransactionResult.Error.ShouldContain("Insufficient LiquidityToken");

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var liquidityBalanceBefore = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            var insufficientLiquidityException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(
                new RemoveLiquidityInput()
                {
                    AmountAMin = amountADesired,
                    AmountBMin = amountBDesired,
                    To = UserTomAddress,
                    LiquidityRemove = liquidityBalanceBefore.Amount.Add(1),
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST"
                });
            insufficientLiquidityException.TransactionResult.Error.ShouldContain("Insufficient LiquidityToken");

            //Insufficient tokenA
            var insufficientTokenAException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(
                new RemoveLiquidityInput()
                {
                    AmountAMin = amountADesired.Add(floatAmount),
                    AmountBMin = amountBDesired,
                    LiquidityRemove = liquidityBalanceBefore.Amount,
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST"
                });
            insufficientTokenAException.TransactionResult.Error.ShouldContain("Insufficient token ELF");

            //Insufficient tokenB
            var insufficientTokenBException = await UserTomStub.RemoveLiquidity.SendWithExceptionAsync(
                new RemoveLiquidityInput()
                {
                    AmountAMin = amountADesired.Sub(floatAmount),
                    AmountBMin = amountBDesired.Add(floatAmount),
                    LiquidityRemove = liquidityBalanceBefore.Amount,
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                    SymbolA = "ELF",
                    SymbolB = "TEST"
                });
            insufficientTokenBException.TransactionResult.Error.ShouldContain("Insufficient token TEST");

            #endregion

            #region Success

            var reservesBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyBefore = await UserTomStub.GetTotalSupply.CallAsync(new StringList()
            {
                Value = { "ELF-TEST" }
            });
            var elfBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });

            await TomLpStub.Approve.SendAsync(new Token.ApproveInput()
            {
                Symbol = "ALP ELF-TEST",
                Spender = AwakenSwapContractAddress,
                Amount = liquidityBalanceBefore.Amount
            });

            await UserTomStub.RemoveLiquidity.SendAsync(new RemoveLiquidityInput()
            {
                AmountAMin = amountADesired.Sub(floatAmount),
                AmountBMin = amountBDesired.Sub(floatAmount),
                LiquidityRemove = liquidityBalanceBefore.Amount,
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST"
            });
            var liquidityRemoveAmountDecimal = Convert.ToDecimal(liquidityBalanceBefore.Amount);
            var amountAGet = decimal.ToInt64(liquidityRemoveAmountDecimal * reservesBefore.Results[0].ReserveA /
                                             totalSupplyBefore.Results[0].TotalSupply);
            var amountBGet = decimal.ToInt64(liquidityRemoveAmountDecimal * reservesBefore.Results[0].ReserveB /
                                             totalSupplyBefore.Results[0].TotalSupply);
            var reservesAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyAfter = await UserTomStub.GetTotalSupply.CallAsync(new StringList
            {
                Value = { "ELF-TEST" }
            });
            var elfBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            var liquidityBalance = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });

            reservesAfter.Results[0].ReserveA.ShouldBe(reservesBefore.Results[0].ReserveA.Sub(amountAGet));
            reservesAfter.Results[0].ReserveB.ShouldBe(reservesBefore.Results[0].ReserveB.Sub(amountBGet));

            liquidityBalance.Amount.ShouldBe(0);
            totalSupplyAfter.Results[0].TotalSupply.ShouldBe(totalSupplyBefore.Results[0].TotalSupply
                .Sub(liquidityBalanceBefore.Amount));

            elfBalanceAfter.Balance.ShouldBe(elfBalanceBefore.Balance.Add(amountAGet));
            testBalanceAfter.Balance.ShouldBe(testBalanceBefore.Balance.Add(amountBGet));

            #endregion
        }

        [Fact]
        public async Task Case0Test()
        {
            await Initialize();

            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            const long liquidityRemove = 200000000;
            const long floatAmount = 10000;
            const long errorInput = 0;

            await AdminLpStub.SetAdmin.SendAsync(AdminAddress);
            await AdminLpStub.AddWhiteList.SendAsync(AdminAddress);
            await AdminLpStub.AddWhiteList.SendAsync(UserTomAddress);
            await AdminLpStub.AddWhiteList.SendAsync(UserLilyAddress);
            await AdminLpStub.AddWhiteList.SendAsync(AwakenSwapContractAddress);

            await TomLpStub.Approve.SendAsync(new Token.ApproveInput()
            {
                Symbol = "ALP ELF-TEST",
                Spender = AwakenSwapContractAddress,
                Amount = int.MaxValue
            });
            await AwakenSwapContractStub.SetFeeTo.SendAsync(AdminAddress);
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var liquidityBalanceBefore = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });

            //swap 
            const long amountIn = 10000000;
            await UserTomStub.SwapExactTokensForTokens.SendAsync(new SwapExactTokensForTokensInput()
            {
                Path = { "ELF", "TEST" },
                To = UserTomAddress,
                AmountIn = amountIn,
                AmountOutMin = 0,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
            });

            var reservesBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyBefore = await UserTomStub.GetTotalSupply.CallAsync(new StringList()
            {
                Value = { "ELF-TEST" }
            });
            var elfBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });

            await TomLpStub.Approve.SendAsync(new Token.ApproveInput()
            {
                Symbol = "ALP ELF-TEST",
                Spender = AwakenSwapContractAddress,
                Amount = liquidityBalanceBefore.Amount
            });
            await UserTomStub.RemoveLiquidity.SendAsync(new RemoveLiquidityInput()
            {
                AmountAMin = 0,
                AmountBMin = 0,
                LiquidityRemove = liquidityBalanceBefore.Amount,
                To = UserTomAddress,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST"
            });
            var liquidityRemoveAmountDecimal = Convert.ToDecimal(liquidityBalanceBefore.Amount);
            liquidityRemoveAmountDecimal.ShouldBe(141421356m);

            var amountAGet = decimal.ToInt64(liquidityRemoveAmountDecimal * reservesBefore.Results[0].ReserveA /
                                             totalSupplyBefore.Results[0].TotalSupply);
            var amountBGet = decimal.ToInt64(liquidityRemoveAmountDecimal * reservesBefore.Results[0].ReserveB /
                                             totalSupplyBefore.Results[0].TotalSupply);

            var reservesAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var totalSupplyAfter = await UserTomStub.GetTotalSupply.CallAsync(new StringList
            {
                Value = { "ELF-TEST" }
            });
            var elfBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter = await TokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            var liquidityBalance = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });

            liquidityBalance.Amount.ShouldBe(0);

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = 0,
                AmountBDesired = amountBDesired,
                AmountBMin = 0,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var liquidityBalanceSecondAdd = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            liquidityBalanceBefore.Amount.ShouldBe(141421356L);
            liquidityBalanceSecondAdd.Amount.ShouldBe(128557147L);

            var liquidity0 = Convert.ToInt64(new BigIntValue(amountADesired)
                .Mul(totalSupplyAfter.Results[0].TotalSupply)
                .Div(reservesAfter.Results[0].ReserveA).Value);
            var liquidity1 = Convert.ToInt64(new BigIntValue(amountBDesired)
                .Mul(totalSupplyAfter.Results[0].TotalSupply)
                .Div(reservesAfter.Results[0].ReserveB).Value);
            var liquidityMin = Math.Min(liquidity0, liquidity1);
            liquidityMin.ShouldBe(128557147L);
            var reservesAfterAdd = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            reservesAfterAdd.Results[0].ReserveA.ShouldBe(100003001L);
            reservesAfterAdd.Results[0].ReserveB.ShouldBe(165349847L);
        }

        [Fact]
        public async Task SwapTest()
        {
            await Initialize();
            const long amountIn = 100000000;
            const long amountOut = 100000000;
            const long errorInput = -1;

            #region Exceptions

            //Expired
            var expiredException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = amountIn,
                    AmountOutMin = amountOut,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(-1, 0, -1))),
                });
            expiredException.TransactionResult.Error.ShouldContain("Expired");

            //Pair not Exists
            var pairException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    AmountIn = amountIn,
                    AmountOutMin = amountOut,
                    Path = { "ELF", "INVALID" },
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 60)))
                });
            pairException.TransactionResult.Error.ShouldContain("Pair ELF-INVALID does not exist");

            //Invalid Input
            var amountInException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = errorInput,
                    AmountOutMin = amountOut,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                });
            amountInException.TransactionResult.Error.ShouldContain("Invalid Input");
            var amountOutMinException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = amountIn,
                    AmountOutMin = errorInput,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                });
            amountOutMinException.TransactionResult.Error.ShouldContain("Invalid Input");

            //Insufficient reserves
            var reservesException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = amountIn,
                    AmountOutMin = amountOut,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
                });
            reservesException.TransactionResult.Error.ShouldContain("Insufficient reserves");

            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = 100000000,
                AmountAMin = 100000000,
                AmountBDesired = 200000000,
                AmountBMin = 200000000,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });

            var reserveBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var elfBalanceBefore = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });

            var amountInWithFee = Convert.ToDecimal(amountIn) * 9970;
            var numerator = amountInWithFee * Convert.ToDecimal(reserveBefore.Results[0].ReserveB);
            var denominator = Convert.ToDecimal(reserveBefore.Results[0].ReserveA * 10000) + amountInWithFee;
            var amountOutExpect = decimal.ToInt64(numerator / denominator);

            //Insufficient Output amount
            var outputException = await UserTomStub.SwapExactTokensForTokens.SendWithExceptionAsync(
                new SwapExactTokensForTokensInput()
                {
                    AmountIn = amountIn,
                    AmountOutMin = amountOutExpect.Sub(-1),
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
                });
            outputException.TransactionResult.Error.ShouldContain("Insufficient Output amount");

            #endregion

            #region SwapExactTokenForToken

            await UserTomStub.SwapExactTokensForTokens.SendAsync(new SwapExactTokensForTokensInput()
            {
                Path = { "ELF", "TEST" },
                To = UserTomAddress,
                AmountIn = amountIn,
                AmountOutMin = amountOutExpect,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
            });
            var reserveAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            reserveAfter.Results[0].ReserveA.ShouldBe(reserveBefore.Results[0].ReserveA.Add(amountIn));
            reserveAfter.Results[0].ReserveB.ShouldBe(reserveBefore.Results[0].ReserveB.Sub(amountOutExpect));
            var elfBalanceAfter = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            elfBalanceAfter.Balance.ShouldBe(elfBalanceBefore.Balance.Sub(amountIn));
            testBalanceAfter.Balance.ShouldBe(testBalanceBefore.Balance.Add(amountOutExpect));

            #endregion

            #region SwapTokenForExactToken

            const long amountOut1 = 10000000;
            var reserveInDecimal = Convert.ToDecimal(reserveAfter.Results[0].ReserveA);
            var reserveOutDecimal = Convert.ToDecimal(reserveAfter.Results[0].ReserveB);
            numerator = reserveInDecimal * amountOut1 * 10000;
            denominator = (reserveOutDecimal - amountOut1) * 9970;
            var amountIn1 = decimal.ToInt64(numerator / denominator) + 1;
            await UserTomStub.SwapTokensForExactTokens.SendAsync(new SwapTokensForExactTokensInput()
            {
                Path = { "ELF", "TEST" },
                To = UserTomAddress,
                AmountOut = amountOut1,
                AmountInMax = amountIn1,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
            });
            var reserveAfter1 = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            reserveAfter1.Results[0].ReserveA.ShouldBe(reserveAfter.Results[0].ReserveA.Add(amountIn1));
            reserveAfter1.Results[0].ReserveB.ShouldBe(reserveAfter.Results[0].ReserveB.Sub(amountOut1));
            var elfBalanceAfter1 = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceAfter1 = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            elfBalanceAfter1.Balance.ShouldBe(elfBalanceAfter.Balance.Sub(amountIn1));
            testBalanceAfter1.Balance.ShouldBe(testBalanceAfter.Balance.Add(amountOut1));

            #endregion

            #region SwapSupportingFeeOnTransferTokens

            reserveBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            amountInWithFee = Convert.ToDecimal(amountIn) * 9970;
            numerator = amountInWithFee * Convert.ToDecimal(reserveBefore.Results[0].ReserveB);
            denominator = Convert.ToDecimal(reserveBefore.Results[0].ReserveA * 10000) + amountInWithFee;
            amountOutExpect = decimal.ToInt64(numerator / denominator);
            await UserTomStub.SwapExactTokensForTokensSupportingFeeOnTransferTokens.SendAsync(
                new SwapExactTokensForTokensSupportingFeeOnTransferTokensInput()
                {
                    Path = { "ELF", "TEST" },
                    To = UserTomAddress,
                    AmountIn = amountIn,
                    AmountOutMin = amountOutExpect,
                    Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3)))
                });

            #endregion
        }

        [Fact]
        public async Task CreatePairTest()
        {
            await CreateAndGetToken();
            await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
            {
                Owner = AwakenSwapContractAddress
            });
            await AwakenSwapContractStub.Initialize.SendAsync(new InitializeInput()
            {
                Admin = AdminAddress,
                AwakenTokenContractAddress = LpTokentContractAddress
            });
            //Invalid TokenPair
            var tokenPairException = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
            {
                SymbolPair = "ELF"
            });
            tokenPairException.TransactionResult.Error.ShouldContain("Invalid TokenPair");

            //Identical Tokens
            var identicalException = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-ELF"
            });
            identicalException.TransactionResult.Error.ShouldContain("Identical Tokens");

            //Identical Tokens
            var exception = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-1"
            });
            exception.TransactionResult.Error.ShouldContain("Invalid symbols for sorting.");

            //  
            //Invalid Tokens
            var tokensException = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-INVALID"
            });
            tokensException.TransactionResult.Error.ShouldContain("Token INVALID not exists.");

            //success
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-TEST"
            });

            //Pair Existed
            var existsException = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-TEST"
            });
            existsException.TransactionResult.Error.ShouldContain("Pair ELF-TEST Already Exist");
            var pairList = await UserTomStub.GetPairs.CallAsync(new Empty());
            pairList.Value.ShouldContain("ELF-TEST");
        }

        /// <summary>
        /// Legal：
        /// AAA-BBB
        /// AAA-1-BBB
        /// AAA-BBB-1
        /// AAA-1-BBB-1
        /// sort：
        /// BBB-1-AAA -> AAA-BBB-1
        /// BBB-1-AAA-2 ->AAA-2-BBB-1
        /// 
        /// illegal：
        /// AAA-1
        /// AAA-BBB-C
        /// AAA-C-BBB
        /// AAA-1-BBB-C
        /// AAA-C-BBB-1
        /// 1-AAA
        /// 1-2-3-4
        /// 
        /// </summary>
        [Fact]
        public async Task CreatePair_NFT_Test()
        {
            await CreateAndGetToken();
            await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
            {
                Owner = AwakenSwapContractAddress
            });
            await AwakenSwapContractStub.Initialize.SendAsync(new InitializeInput()
            {
                Admin = AdminAddress,
                AwakenTokenContractAddress = LpTokentContractAddress
            });
            //illegal
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput
            {
                SymbolPair = "ELF-AAA-1"
            });
            var pairList1 = await UserTomStub.GetPairs.CallAsync(new Empty());
            pairList1.Value.ShouldContain("AAA-1-ELF");
            var result = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput
            {
                SymbolPair = "AAA-1-ELF"
            });
            result.TransactionResult.Error.ShouldContain("Pair AAA-1-ELF Already Exist");
            {
                await UserTomStub.CreatePair.SendAsync(new CreatePairInput
                {
                    SymbolPair = "AAA-1-BBB-1"
                });
                var pairList2 = await UserTomStub.GetPairs.CallAsync(new Empty());
                pairList2.Value.ShouldContain("AAA-1-BBB-1");
            }

            {
                await UserTomStub.CreatePair.SendAsync(new CreatePairInput
                {
                    SymbolPair = "BBB-1-DAI"
                });
                var pairList2 = await UserTomStub.GetPairs.CallAsync(new Empty());
                pairList2.Value.ShouldContain("BBB-1-DAI");
            }

            {
                //Invalid TokenPair
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "ELF-1"
                });
                executionResult.TransactionResult.Error.ShouldContain("Invalid symbols for sorting.");
            }
            {
                //Identical Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "AAA-BBB-CF"
                });
                executionResult.TransactionResult.Error.ShouldContain("Token AAA not exists.");
            }
            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "AAA-1-BBB-C"
                });
                executionResult.TransactionResult.Error.ShouldContain("Token BBB-C not exists.");
            }
            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "AAA-C-BBB-1"
                });
                executionResult.TransactionResult.Error.ShouldContain("Token AAA-C not exists.");
            }
            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "1-AAA"
                });
                executionResult.TransactionResult.Error.ShouldContain("Invalid symbols for sorting.");
            }

            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "1-2-3"
                });
                executionResult.TransactionResult.Error.ShouldContain("Invalid symbols for sorting.");
            }

            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "1-2-3-4"
                });
                executionResult.TransactionResult.Error.ShouldContain("Token 1-2 not exists.");
            }
            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "1-ELF-3"
                });
                executionResult.TransactionResult.Error.ShouldContain("Invalid symbols for sorting.");
            }
            {
                //Invalid Tokens
                var executionResult = await UserTomStub.CreatePair.SendWithExceptionAsync(new CreatePairInput()
                {
                    SymbolPair = "1-AAA-1-4"
                });
                executionResult.TransactionResult.Error.ShouldContain("Token 1-4 not exists.");
            }
        }

        [Fact]
        public async Task InitializeTest()
        {
            await CreateAndGetToken();
            await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
            {
                Owner = AwakenSwapContractAddress
            });
            await AwakenSwapContractStub.Initialize.SendAsync(new InitializeInput()
            {
                Admin = AdminAddress,
                AwakenTokenContractAddress = LpTokentContractAddress
            });
            //Already initialized

            var initializedException =
                await AwakenSwapContractStub.Initialize.SendWithExceptionAsync(new InitializeInput());
            initializedException.TransactionResult.Error.ShouldContain("Already initialized");
        }

        [Fact]
        public async Task GetReservesTest()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            //Pair not existed
            var pairException = await UserTomStub.GetReserves.CallWithExceptionAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-INVALID" }
            });
            pairException.Value.ShouldContain("Pair ELF-INVALID does not exist");
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var reserves = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            reserves.Results[0].ReserveA.ShouldBe(amountADesired);
            reserves.Results[0].ReserveB.ShouldBe(amountBDesired);
        }

        [Fact]
        public async Task GetTotalSupplyTest()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            //Pair not existed
            var pairException = await UserTomStub.GetTotalSupply.CallWithExceptionAsync(new StringList
            {
                Value = { "ELF-INVALID" }
            });
            pairException.Value.ShouldContain("Pair ELF-INVALID does not exist");
            //Success
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var totalSupply = await UserTomStub.GetTotalSupply.CallAsync(new StringList()
            {
                Value = { "ELF-TEST" }
            });
            var totalSupplyExpect =
                Convert.ToInt64(Sqrt(new BigIntValue(amountADesired).Mul(amountBDesired)).Add(1).Value);
            totalSupply.Results[0].TotalSupply.ShouldBe(totalSupplyExpect);
        }

        [Fact]
        public async Task GetLiquidityTokenBalanceTest()
        {
            await Initialize();
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            //Success  
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var liquidity = await AdminLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
            {
                Symbol = GetTokenPairSymbol("ELF", "TEST"),
                Owner = UserTomAddress
            });
            var liquidityExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
            liquidity.Amount.ShouldBe(liquidityExpect);
        }

        [Fact]
        public async Task QuoteTest()
        {
            const long amountA = 100000000;
            const long errorInput = 0;
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            await Initialize();
            var pairException = await UserTomStub.Quote.CallWithExceptionAsync(new QuoteInput()
            {
                SymbolA = "ELF",
                SymbolB = "INVALID",
                AmountA = amountA
            });
            pairException.Value.ShouldContain("Pair not exists");
            //Insufficient  amount
            var amountException = await UserTomStub.Quote.CallWithExceptionAsync(new QuoteInput()
            {
                SymbolA = "ELF",
                SymbolB = "TEST",
                AmountA = errorInput
            });
            amountException.Value.ShouldContain("Insufficient Amount");
            //Insufficient reserves
            var reservesException = await UserTomStub.Quote.CallWithExceptionAsync(new QuoteInput()
            {
                SymbolA = "ELF",
                SymbolB = "TEST",
                AmountA = amountA
            });
            reservesException.Value.ShouldContain("Insufficient reserves");
            //Success  
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var amountBExpect = decimal.ToInt64(Convert.ToDecimal(amountA) * amountBDesired / amountADesired);
            var amountB = await UserTomStub.Quote.CallAsync(new QuoteInput()
                {
                    SymbolA = "ELF",
                    SymbolB = "TEST",
                    AmountA = amountA
                }
            );
            amountB.Value.ShouldBe(amountBExpect);
        }

        [Fact]
        public async Task GetAmountInTest()
        {
            const long amountOut = 100000000;
            const long errorInput = 0;
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            await Initialize();
            var pairException = await UserTomStub.GetAmountIn.CallWithExceptionAsync(new GetAmountInInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "INVALID",
                AmountOut = amountOut
            });
            pairException.Value.ShouldContain("Pair not exists");
            //Insufficient Output amount
            var amountException = await UserTomStub.GetAmountIn.CallWithExceptionAsync(new GetAmountInInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountOut = errorInput
            });
            amountException.Value.ShouldContain("Insufficient Input Amount");
            //Insufficient reserves
            var reservesException = await UserTomStub.GetAmountIn.CallWithExceptionAsync(new GetAmountInInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountOut = amountOut
            });
            reservesException.Value.ShouldContain("Insufficient reserves");
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var numerator = Convert.ToDecimal(amountADesired) * amountOut * 10000;
            var denominator = (Convert.ToDecimal(amountBDesired) - amountOut) * 9970;
            var amountInExpect = decimal.ToInt64(numerator / denominator) + 1;
            var amountIn = await UserTomStub.GetAmountIn.CallAsync(new GetAmountInInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountOut = amountOut
            });
            amountIn.Value.ShouldBe(amountInExpect);
            var amountsIn = await UserTomStub.GetAmountsIn.CallAsync(new GetAmountsInInput()
            {
                Path = { "ELF", "TEST" },
                AmountOut = amountOut
            });
            amountsIn.Amount[0].ShouldBe(amountInExpect);
        }

        [Fact]
        public async Task GetAmountOutTest()
        {
            const long amountIn = 100000000;
            const long errorInput = 0;
            const long amountADesired = 100000000;
            const long amountBDesired = 200000000;
            await Initialize();
            var pairException = await UserTomStub.GetAmountOut.CallWithExceptionAsync(new GetAmountOutInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "INVALID",
                AmountIn = amountIn
            });
            pairException.Value.ShouldContain("Pair not exists");
            //Insufficient Output amount
            var amountException = await UserTomStub.GetAmountOut.CallWithExceptionAsync(new GetAmountOutInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountIn = errorInput
            });
            amountException.Value.ShouldContain("Insufficient Output Amount");
            //Insufficient reserves
            var reservesException = await UserTomStub.GetAmountOut.CallWithExceptionAsync(new GetAmountOutInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountIn = amountIn
            });
            reservesException.Value.ShouldContain("Insufficient reserves");
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = amountADesired,
                AmountAMin = amountADesired,
                AmountBDesired = amountBDesired,
                AmountBMin = amountBDesired,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });
            var amountInWithFee = amountIn.Mul(997);
            var numerator = amountInWithFee * Convert.ToDecimal(amountBDesired);
            var denominator = (Convert.ToDecimal(amountADesired) * 1000) + amountInWithFee;
            var amountOutExpect = decimal.ToInt64(numerator / denominator);
            var amountOut = await UserTomStub.GetAmountOut.CallAsync(new GetAmountOutInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountIn = amountIn
            });
            amountOut.Value.ShouldBe(amountOutExpect);

            var amountsOut = await UserTomStub.GetAmountsOut.CallAsync(new GetAmountsOutInput()
            {
                Path = { "ELF", "TEST" },
                AmountIn = amountIn
            });
            amountsOut.Amount[1].ShouldBe(amountOutExpect);
        }


        [Fact]
        public async Task FeeTest()
        {
            const long amountIn = 100000000;
            await Initialize();
            await UserTomStub.AddLiquidity.SendAsync(new AddLiquidityInput()
            {
                AmountADesired = 100000000,
                AmountAMin = 100000000,
                AmountBDesired = 200000000,
                AmountBMin = 200000000,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                SymbolA = "ELF",
                SymbolB = "TEST",
                To = UserTomAddress
            });

            var reserveBefore = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
            var elfBalanceBefore = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "ELF"
                });
            var testBalanceBefore = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = UserTomAddress,
                    Symbol = "TEST"
                });
            var elfContractBalanceBefore = await UserTomTokenContractStub.GetBalance.CallAsync(
                new AElf.Contracts.MultiToken.GetBalanceInput()
                {
                    Owner = AwakenSwapContractAddress,
                    Symbol = "ELF"
                });

            var amountInWithFee = new BigIntValue(amountIn * 9970);
            var numerator = amountInWithFee.Mul(reserveBefore.Results[0].ReserveB);
            var denominator = new BigIntValue(reserveBefore.Results[0].ReserveA * 10000).Add(amountInWithFee);
            var amountOutExpect = Convert.ToInt64(numerator.Div(denominator).Value);
            var blockTimeProvider = GetRequiredService<IBlockTimeProvider>();
            blockTimeProvider.SetBlockTime(Timestamp.FromDateTime(DateTime.UtcNow).AddDays(365));

            await UserTomStub.SwapExactTokensForTokens.SendAsync(new SwapExactTokensForTokensInput()
            {
                Path = { "ELF", "TEST" },
                To = UserTomAddress,
                AmountIn = amountIn,
                AmountOutMin = amountOutExpect,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(366)),
                Channel = ""
            });
            var reserveAfter = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
            {
                SymbolPair = { "ELF-TEST" }
            });
        }

        [Fact]
        public void SqrtTest()
        {
            var zero = new BigIntValue(0);
            var number = new BigIntValue(Decimal.MaxValue.ToString());
            Sqrt(zero).ShouldBe(zero);
            Convert.ToInt64(Math.Sqrt(Convert.ToDouble(number.Value)))
                .ShouldBe(Convert.ToInt64(Sqrt(number).Add(1).Value));
        }

        [Fact]
        public async Task FeeToTest()
        {
            await Initialize();
            await AwakenSwapContractStub.SetFeeTo.SendAsync(UserTomAddress);
            var feeto = await AwakenSwapContractStub.GetFeeTo.CallAsync(new Empty());
            feeto.ShouldBe(UserTomAddress);
        }

        [Fact]
        public async Task ChangeOwnerTest()
        {
            await Initialize();
            await AwakenSwapContractStub.ChangeOwner.SendAsync(UserLilyAddress);
            var admin = await AwakenSwapContractStub.GetAdmin.CallAsync(new Empty());
            admin.ShouldBe(UserLilyAddress);
        }

        private async Task CreateAndGetToken()
        {
            var res = await CreateMutiTokenAsync(TokenContractImplStub, new CreateInput
            {
                Symbol = "TEST",
                TokenName = "TEST",
                TotalSupply = 100000000000000,
                Decimals = 8,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var issueResult = await TokenContractStub.Issue.SendAsync(new AElf.Contracts.MultiToken.IssueInput
            {
                Amount = 100000000000000,
                Symbol = "TEST",
                To = AdminAddress
            });
            issueResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var balance = await TokenContractStub.GetBalance.SendAsync(new AElf.Contracts.MultiToken.GetBalanceInput()
            {
                Owner = AdminAddress,
                Symbol = "TEST"
            });
            balance.Output.Balance.ShouldBe(100000000000000);
            //DAI

            var res1 = await CreateMutiTokenAsync(TokenContractImplStub, new CreateInput
            {
                Symbol = "DAI",
                TokenName = "DAI",
                TotalSupply = 100000000000000,
                Decimals = 10,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res1.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var issueResult2 = await TokenContractStub.Issue.SendAsync(new AElf.Contracts.MultiToken.IssueInput
            {
                Amount = 100000000000000,
                Symbol = "DAI",
                To = AdminAddress
            });
            issueResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var balance2 = await TokenContractStub.GetBalance.SendAsync(new AElf.Contracts.MultiToken.GetBalanceInput()
            {
                Owner = AdminAddress,
                Symbol = "DAI"
            });
            balance2.Output.Balance.ShouldBe(100000000000000);
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "ELF",
                Memo = "Recharge",
                To = UserTomAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "ELF",
                Memo = "Recharge",
                To = UserLilyAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "TEST",
                Memo = "Recharge",
                To = UserTomAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "TEST",
                Memo = "Recharge",
                To = UserLilyAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "DAI",
                Memo = "Recharge",
                To = UserTomAddress
            });


            var res4 = await CreateMutiTokenAsync(TokenContractImplStub, new CreateInput
            {
                Symbol = "AAA-0",
                TokenName = "AAA-0",
                TotalSupply = 100000000000000,
                Decimals = 0,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res4.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var res3 = await TokenContractStub.Create.SendAsync(new CreateInput
            {
                Symbol = "AAA-1",
                TokenName = "AAA-1",
                TotalSupply = 100000000000000,
                Decimals = 0,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res3.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var issueResult3 = await TokenContractStub.Issue.SendAsync(new AElf.Contracts.MultiToken.IssueInput
            {
                Amount = 100000000000000,
                Symbol = "AAA-1",
                To = AdminAddress
            });
            issueResult3.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var balance3 = await TokenContractStub.GetBalance.SendAsync(new AElf.Contracts.MultiToken.GetBalanceInput()
            {
                Owner = AdminAddress,
                Symbol = "AAA-1"
            });
            balance3.Output.Balance.ShouldBe(100000000000000);
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "AAA-1",
                Memo = "Recharge",
                To = UserTomAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "AAA-1",
                Memo = "Recharge",
                To = UserLilyAddress
            });

            var res5 = await CreateMutiTokenAsync(TokenContractImplStub, new CreateInput
            {
                Symbol = "BBB-0",
                TokenName = "BBB-0",
                TotalSupply = 100000000000000,
                Decimals = 0,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res5.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var res6 = await TokenContractStub.Create.SendAsync(new CreateInput
            {
                Symbol = "BBB-1",
                TokenName = "BBB-1",
                TotalSupply = 100000000000000,
                Decimals = 0,
                Issuer = AdminAddress,
                Owner = AdminAddress,
            });
            res6.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var issueResult4 = await TokenContractStub.Issue.SendAsync(new AElf.Contracts.MultiToken.IssueInput
            {
                Amount = 100000000000000,
                Symbol = "BBB-1",
                To = AdminAddress
            });
            issueResult4.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var balance4 = await TokenContractStub.GetBalance.SendAsync(new AElf.Contracts.MultiToken.GetBalanceInput()
            {
                Owner = AdminAddress,
                Symbol = "BBB-1"
            });
            balance4.Output.Balance.ShouldBe(100000000000000);
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "BBB-1",
                Memo = "Recharge",
                To = UserTomAddress
            });
            await TokenContractStub.Transfer.SendAsync(new AElf.Contracts.MultiToken.TransferInput()
            {
                Amount = 100000000000,
                Symbol = "BBB-1",
                Memo = "Recharge",
                To = UserLilyAddress
            });

            //authorize  Tom and Lily and admin to transfer ELF and TEST and DAI to FinanceContract
            await UserTomTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "ELF"
            });
            await UserTomTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "DAI"
            });
            await TokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "ELF"
            });
            await UserLilyTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "ELF"
            });
            await UserTomTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "TEST"
            });
            await TokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "TEST"
            });
            await UserLilyTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "TEST"
            });
            await TokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "AAA-1"
            });
            await UserTomTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "AAA-1"
            });
            await UserLilyTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "AAA-1"
            });
            await TokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "BBB-1"
            });
            await UserTomTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "BBB-1"
            });
            await UserLilyTokenContractStub.Approve.SendAsync(new AElf.Contracts.MultiToken.ApproveInput()
            {
                Amount = 100000000000,
                Spender = AwakenSwapContractAddress,
                Symbol = "BBB-1"
            });
        }

        private async Task Initialize()
        {
            await CreateAndGetToken();
            await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
            {
                Owner = AwakenSwapContractAddress
            });
            await AwakenSwapContractStub.Initialize.SendAsync(new InitializeInput()
            {
                Admin = AdminAddress,
                AwakenTokenContractAddress = LpTokentContractAddress
            });
            await AwakenSwapContractStub.SetFeeRate.SendAsync(new Int64Value() { Value = 30 });
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-TEST"
            });

            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-DAI"
            });

            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "ELF-AAA-1"
            });
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "BBB-1-ELF"
            });
            await UserTomStub.CreatePair.SendAsync(new CreatePairInput()
            {
                SymbolPair = "AAA-1-BBB-1"
            });
        }

        private static BigIntValue Sqrt(BigIntValue n)
        {
            if (n.Value == "0")
                return n;
            var left = new BigIntValue(1);
            var right = n;
            var mid = left.Add(right).Div(2);
            while (!left.Equals(right) && !mid.Equals(left))
            {
                if (mid.Equals(n.Div(mid)))
                    return mid;
                if (mid < n.Div(mid))
                {
                    left = mid;
                    mid = left.Add(right).Div(2);
                }
                else
                {
                    right = mid;
                    mid = left.Add(right).Div(2);
                }
            }

            return left;
        }

        private string GetTokenPairSymbol(string tokenA, string tokenB)
        {
            var symbols = SortSymbols(tokenA, tokenB);
            return $"ALP {symbols[0]}-{symbols[1]}";
        }

        private string[] SortSymbols(params string[] symbols)
        {
            return symbols.OrderBy(s => s).ToArray();
        }

        [Fact]
        public async Task RemoveWhiteList()
        {
            await Initialize();
            await AdminLpStub.SetAdmin.SendAsync(DefaultAddress);
            var executionResult1 = await AdminLpStub.RemoveWhiteList.SendWithExceptionAsync(UserTomAddress);
            executionResult1.TransactionResult.Error.ShouldContain("Address not exist");
            await AdminLpStub.AddWhiteList.SendAsync(AwakenSwapContractAddress);
            await AdminLpStub.AddWhiteList.SendAsync(UserTomAddress);
            {
                var whiteList = await AdminLpStub.GetWhiteList.CallAsync(new Empty());
                whiteList.Value.Count.ShouldBe(2);
                whiteList.Value[0].ShouldBe(AwakenSwapContractAddress);
                whiteList.Value[1].ShouldBe(UserTomAddress);
            }
            await AdminLpStub.RemoveWhiteList.SendAsync(UserTomAddress);
            {
                var whiteList = await AdminLpStub.GetWhiteList.CallAsync(new Empty());
                whiteList.Value.Count.ShouldBe(1);
                whiteList.Value[0].ShouldBe(AwakenSwapContractAddress);
            }
            var executionResult = await AdminLpStub.RemoveWhiteList.SendWithExceptionAsync(UserTomAddress);
            executionResult.TransactionResult.Error.ShouldContain("Address not exist");
        }
    }
}