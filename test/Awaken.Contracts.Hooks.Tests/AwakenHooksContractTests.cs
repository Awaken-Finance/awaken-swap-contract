
using System;
using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Types;
using Awaken.Contracts.Swap;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Awaken.Contracts.Hooks;

public class AwakenHooksContractTests : AwakenHooksContractTestBase
{
    [Fact]
    public async Task InitializeTest()
    {
        var result = await TomHooksStud.Initialize.SendWithExceptionAsync(new InitializeInput());
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminHooksStud.Initialize.SendAsync(new InitializeInput
        {
            SwapContractList = new SwapContractInfoList()
            {
                SwapContracts = { new SwapContractInfo
                {
                    FeeRate = 10,
                    LpTokenContractAddress = LpTokenContractAddress,
                    SwapContractAddress = AwakenSwapContractAddress
                } }
            }
        });
        var swapContractList = await AdminHooksStud.GetSwapContractList.CallAsync(new Empty());
        swapContractList.SwapContractList.SwapContracts.Count.ShouldBe(1);
        swapContractList.SwapContractList.SwapContracts[0].SwapContractAddress.ShouldBe(AwakenSwapContractAddress);
        swapContractList.SwapContractList.SwapContracts[0].LpTokenContractAddress.ShouldBe(LpTokenContractAddress);
        swapContractList.SwapContractList.SwapContracts[0].FeeRate.ShouldBe(10);
        
        result = await AdminHooksStud.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            SwapContractList = new SwapContractInfoList()
        });
        result.TransactionResult.Error.ShouldContain("Already initialized.");
    }

    [Fact]
    public async Task AddSwapContractInfoTest()
    {
        await Initialize();
        var result = await TomHooksStud.AddSwapContractInfo.SendWithExceptionAsync(new AddSwapContractInfoInput());
        result.TransactionResult.Error.ShouldContain("No permission.");
        result = await AdminHooksStud.AddSwapContractInfo.SendWithExceptionAsync(new AddSwapContractInfoInput());
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        result = await AdminHooksStud.AddSwapContractInfo.SendWithExceptionAsync(new AddSwapContractInfoInput
        {
            SwapContractList = new SwapContractInfoList()
        });
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        await AdminHooksStud.AddSwapContractInfo.SendAsync(new AddSwapContractInfoInput()
        {
            SwapContractList = new SwapContractInfoList()
            {
                SwapContracts = { new SwapContractInfo()
                {
                    FeeRate = 10,
                    LpTokenContractAddress = UserTomAddress,
                    SwapContractAddress = UserLilyAddress
                } }
            }
        });
        var swapContractListOutput = await AdminHooksStud.GetSwapContractList.CallAsync(new Empty());
        swapContractListOutput.SwapContractList.SwapContracts.Count.ShouldBe(2);
        swapContractListOutput.SwapContractList.SwapContracts[1].FeeRate.ShouldBe(10);
        swapContractListOutput.SwapContractList.SwapContracts[1].SwapContractAddress.ShouldBe(UserLilyAddress);
        swapContractListOutput.SwapContractList.SwapContracts[1].LpTokenContractAddress.ShouldBe(UserTomAddress);
        
        await AdminHooksStud.AddSwapContractInfo.SendAsync(new AddSwapContractInfoInput()
        {
            SwapContractList = new SwapContractInfoList()
            {
                SwapContracts = { new SwapContractInfo()
                {
                    FeeRate = 10,
                    LpTokenContractAddress = UserLilyAddress,
                    SwapContractAddress = UserTomAddress
                } }
            }
        });
        swapContractListOutput = await AdminHooksStud.GetSwapContractList.CallAsync(new Empty());
        swapContractListOutput.SwapContractList.SwapContracts.Count.ShouldBe(2);
        swapContractListOutput.SwapContractList.SwapContracts[1].FeeRate.ShouldBe(10);
        swapContractListOutput.SwapContractList.SwapContracts[1].SwapContractAddress.ShouldBe(UserTomAddress);
        swapContractListOutput.SwapContractList.SwapContracts[1].LpTokenContractAddress.ShouldBe(UserLilyAddress);
        
        result = await TomHooksStud.RemoveSwapContractInfo.SendWithExceptionAsync(new RemoveSwapContractInfoInput());
        result.TransactionResult.Error.ShouldContain("No permission.");
        result = await AdminHooksStud.RemoveSwapContractInfo.SendWithExceptionAsync(new RemoveSwapContractInfoInput());
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        await AdminHooksStud.RemoveSwapContractInfo.SendAsync(new RemoveSwapContractInfoInput()
        {
            FeeRates = { 10 }
        });
        swapContractListOutput = await AdminHooksStud.GetSwapContractList.CallAsync(new Empty());
        swapContractListOutput.SwapContractList.SwapContracts.Count.ShouldBe(1);
        swapContractListOutput.SwapContractList.SwapContracts[0].FeeRate.ShouldBe(_feeRate);
    }

    [Fact]
    public async Task CreatePairTest()
    {
        await Initialize();
        var result = await TomHooksStud.CreatePair.SendWithExceptionAsync(new CreatePairInput()
        {
            SymbolPair = "ELF-TEST",
            FeeRate = 10
        });
        result.TransactionResult.Error.ShouldContain("feeRate not existed");
        //success
        await TomHooksStud.CreatePair.SendAsync(new CreatePairInput()
        {
            SymbolPair = "ELF-TEST",
            FeeRate = _feeRate
        });
    }

    [Fact]
    public async Task AddLiquidityTest()
    {
        const long amountADesired = 100000000;
        const long amountBDesired = 200000000;
        await Initialize();
        await TomHooksStud.CreatePair.SendAsync(new CreatePairInput()
        {
            SymbolPair = "ELF-TEST",
            FeeRate = _feeRate
        });
        var result = await TomHooksStud.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = 100
        });
        result.TransactionResult.Error.ShouldContain("feeRate not existed");
        
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        var reserves1 = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
        {
            SymbolPair = { "ELF-TEST" }
        });
        reserves1.Results[0].ReserveA.ShouldBe(amountADesired);
        reserves1.Results[0].ReserveB.ShouldBe(amountBDesired);
        
        var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
        var totalSupply = UserTomStub.GetTotalSupply.CallAsync(new Swap.StringList()
        {
            Value = { "ELF-TEST" }
        });
        totalSupply.Result.Results[0].SymbolPair.ShouldBe("ELF-TEST");
        totalSupply.Result.Results[0].TotalSupply.ShouldBe(balanceExpect + 1);

        var tomBalance = await TomLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
        {
            Owner = UserTomAddress,
            Symbol = GetTokenPairSymbol("ELF", "TEST")
        });
        tomBalance.Amount.ShouldBe(balanceExpect);
        
        result = await TomHooksStud.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired + 1,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        result.TransactionResult.Error.ShouldContain("Insufficient amount of token TEST.");
        result = await TomHooksStud.AddLiquidity.SendWithExceptionAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired + 1,
            AmountBDesired = amountBDesired / 2,
            AmountBMin = amountBDesired / 2,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        result.TransactionResult.Error.ShouldContain("Insufficient amount of token ELF.");
        
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        var reserves2 = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
        {
            SymbolPair = { "ELF-TEST" }
        });
        reserves2.Results[0].ReserveA.ShouldBe(2 * amountADesired);
        reserves2.Results[0].ReserveB.ShouldBe(2 * amountBDesired);
        
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired / 2,
            AmountBDesired = amountBDesired / 2,
            AmountBMin = amountBDesired / 2,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        var reserves3 = await UserTomStub.GetReserves.CallAsync(new GetReservesInput()
        {
            SymbolPair = { "ELF-TEST" }
        });
        reserves3.Results[0].ReserveA.ShouldBe(amountADesired * 5 / 2);
        reserves3.Results[0].ReserveB.ShouldBe(amountBDesired * 5 / 2);
    }

    [Fact]
    public async Task RemoveLiquidityTest()
    {
        const long amountADesired = 100000000;
        const long amountBDesired = 200000000;
        await Initialize();
        await TomHooksStud.CreatePair.SendAsync(new CreatePairInput()
        {
            SymbolPair = "ELF-TEST",
            FeeRate = _feeRate
        });
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });

        var result = await TomHooksStud.RemoveLiquidity.SendWithExceptionAsync(new RemoveLiquidityInput()
        {
            SymbolA = "ELF",
            AmountAMin = 0,
            SymbolB = "TEST",
            AmountBMin = 0,
            LiquidityRemove = 1,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            To = UserTomAddress,
            FeeRate = 10
        });
        result.TransactionResult.Error.ShouldContain("feeRate not existed");
        await TomLpStub.Approve.SendAsync(new Token.ApproveInput
        {
            Symbol = GetTokenPairSymbol("ELF", "TEST"),
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress
        });
        result = await TomHooksStud.RemoveLiquidity.SendAsync(new RemoveLiquidityInput()
        {
            SymbolA = "ELF",
            AmountAMin = 0,
            SymbolB = "TEST",
            AmountBMin = 0,
            LiquidityRemove = 10000,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        
        var balanceExpect = Convert.ToInt64(Sqrt(new BigIntValue(amountADesired * amountBDesired)).Value);
        var totalSupply = UserTomStub.GetTotalSupply.CallAsync(new Swap.StringList()
        {
            Value = { "ELF-TEST" }
        });
        totalSupply.Result.Results[0].SymbolPair.ShouldBe("ELF-TEST");
        totalSupply.Result.Results[0].TotalSupply.ShouldBe(balanceExpect + 1 - 10000);

        var tomBalance = await TomLpStub.GetBalance.CallAsync(new Token.GetBalanceInput()
        {
            Owner = UserTomAddress,
            Symbol = GetTokenPairSymbol("ELF", "TEST")
        });
        tomBalance.Amount.ShouldBe(balanceExpect - 10000);
    }

    [Fact]
    public async Task SwapExactTokensForTokensTest()
    {
        await CreateAndAddLiquidity();
        var result = await TomHooksStud.SwapExactTokensForTokens.SendWithExceptionAsync(
            new SwapExactTokensForTokensInput());
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        var balanceELFBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "ELF"
        });
        var balanceTESTBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "TEST"
        });
        var balanceDAIBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "DAI"
        });
        var amountIn = 1000000;
        var amountOuts = await TomHooksStud.GetAmountsOut.CallAsync(new GetAmountsOutInput()
        {
            AmountIn = amountIn,
            Path = { "TEST","ELF","DAI" },
            FeeRates = { _feeRate, _feeRate }
        });
        await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = amountIn,
                        AmountOutMin = 0,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF","DAI" },
                        FeeRates = { _feeRate, _feeRate }
                    },
                }
            });
        var balanceELFAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "ELF"
        });
        var balanceTESTAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "TEST"
        });
        var balanceDAIAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "DAI"
        });
        balanceELFAfter.Balance.ShouldBe(balanceELFBefore.Balance);
        balanceTESTAfter.Balance.ShouldBe(balanceTESTBefore.Balance - amountIn);
        balanceDAIAfter.Balance.ShouldBe(balanceDAIBefore.Balance + amountOuts.Amount[2]);
        
        
        balanceELFBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "ELF"
        });
        balanceTESTBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "TEST"
        });
        balanceDAIBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "DAI"
        });
        var amountOuts1 = await TomHooksStud.GetAmountsOut.CallAsync(new GetAmountsOutInput()
        {
            AmountIn = amountIn,
            Path = { "TEST","ELF"},
            FeeRates = { _feeRate }
        });
        var amountOuts2 = await TomHooksStud.GetAmountsOut.CallAsync(new GetAmountsOutInput()
        {
            AmountIn = amountIn,
            Path = { "ELF","DAI"},
            FeeRates = { _feeRate }
        });
        await LilyHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = amountIn,
                        AmountOutMin = 0,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserLilyAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                    new SwapExactTokensForTokens
                    {
                        AmountIn = amountIn,
                        AmountOutMin = 0,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserLilyAddress,
                        Path = { "ELF","DAI" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        balanceELFAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "ELF"
        });
        balanceTESTAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "TEST"
        });
        balanceDAIAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "DAI"
        });
        balanceELFAfter.Balance.ShouldBe(balanceELFBefore.Balance + amountOuts1.Amount[1] - amountIn);
        balanceTESTAfter.Balance.ShouldBe(balanceTESTBefore.Balance - amountIn);
        balanceDAIAfter.Balance.ShouldBe(balanceDAIBefore.Balance + amountOuts2.Amount[1]);
    }
    
    [Fact]
    public async Task SwapTokensForExactTokensTest()
    {
        await CreateAndAddLiquidity();
        var result = await TomHooksStud.SwapTokensForExactTokens.SendWithExceptionAsync(
            new SwapTokensForExactTokensInput());
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        var balanceELFBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "ELF"
        });
        var balanceTESTBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "TEST"
        });
        var balanceDAIBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "DAI"
        });
        var amountOut = 1000000;
        var amountsIn = await TomHooksStud.GetAmountsIn.CallAsync(new GetAmountsInInput()
        {
            AmountOut = amountOut,
            Path = { "TEST","ELF","DAI" },
            FeeRates = { _feeRate, _feeRate }
        });
        await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens()
                    {
                        AmountOut = amountOut,
                        AmountInMax = 5 * amountOut,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF","DAI" },
                        FeeRates = { _feeRate, _feeRate }
                    },
                }
            });
        var balanceELFAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "ELF"
        });
        var balanceTESTAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "TEST"
        });
        var balanceDAIAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserTomAddress,
            Symbol = "DAI"
        });
        balanceELFAfter.Balance.ShouldBe(balanceELFBefore.Balance);
        balanceTESTAfter.Balance.ShouldBe(balanceTESTBefore.Balance - amountsIn.Amount[0]);
        balanceDAIAfter.Balance.ShouldBe(balanceDAIBefore.Balance + amountOut);
        
        
        balanceELFBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "ELF"
        });
        balanceTESTBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "TEST"
        });
        balanceDAIBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "DAI"
        });
        var amountsIn1 = await TomHooksStud.GetAmountsIn.CallAsync(new GetAmountsInInput()
        {
            AmountOut = amountOut,
            Path = { "TEST","ELF"},
            FeeRates = { _feeRate }
        });
        var amountsIn2 = await TomHooksStud.GetAmountsIn.CallAsync(new GetAmountsInInput()
        {
            AmountOut = amountOut,
            Path = { "ELF","DAI"},
            FeeRates = { _feeRate }
        });
        await LilyHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens()
                    {
                        AmountOut = amountOut,
                        AmountInMax = 5 * amountOut,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserLilyAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                    new SwapTokensForExactTokens
                    {
                        AmountOut = amountOut,
                        AmountInMax = 5 * amountOut,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserLilyAddress,
                        Path = { "ELF","DAI" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        balanceELFAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "ELF"
        });
        balanceTESTAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "TEST"
        });
        balanceDAIAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
        {
            Owner = UserLilyAddress,
            Symbol = "DAI"
        });
        balanceELFAfter.Balance.ShouldBe(balanceELFBefore.Balance + amountOut - amountsIn2.Amount[0]);
        balanceTESTAfter.Balance.ShouldBe(balanceTESTBefore.Balance - amountsIn1.Amount[0]);
        balanceDAIAfter.Balance.ShouldBe(balanceDAIBefore.Balance + amountOut);
    }

    private async Task CreateAndAddLiquidity()
    {
        const long amountADesired = 100000000;
        const long amountBDesired = 200000000;
        await Initialize();
        await TomHooksStud.CreatePair.SendAsync(new CreatePairInput()
        {
            SymbolPair = "ELF-TEST",
            FeeRate = _feeRate
        });
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "ELF",
            SymbolB = "TEST",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
        
        await TomHooksStud.CreatePair.SendAsync(new CreatePairInput()
        {
            SymbolPair = "DAI-ELF",
            FeeRate = _feeRate
        });
        await TomHooksStud.AddLiquidity.SendAsync(new AddLiquidityInput
        {
            AmountADesired = amountADesired,
            AmountAMin = amountADesired,
            AmountBDesired = amountBDesired,
            AmountBMin = amountBDesired,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
            SymbolA = "DAI",
            SymbolB = "ELF",
            To = UserTomAddress,
            FeeRate = _feeRate
        });
    }

    [Fact]
    public async Task GetAmountInTest()
    {
        const long amountOut = 100000000;
        const long errorInput = 0;
        const long amountADesired = 100000000;
        const long amountBDesired = 200000000;
        await Initialize();
    }

    [Fact]
    public async Task GetAmountOutTest()
    {
        const long amountIn = 100000000;
        const long errorInput = 0;
        const long amountADesired = 100000000;
        const long amountBDesired = 200000000;
        await Initialize();
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

        var issueResult = await TokenContractStub.Issue.SendAsync(new IssueInput
        {
            Amount = 100000000000000,
            Symbol = "TEST",
            To = AdminAddress
        });
        issueResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        var balance = await TokenContractStub.GetBalance.SendAsync(new GetBalanceInput()
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

        var issueResult2 = await TokenContractStub.Issue.SendAsync(new IssueInput
        {
            Amount = 100000000000000,
            Symbol = "DAI",
            To = AdminAddress
        });
        issueResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        var balance2 = await TokenContractStub.GetBalance.SendAsync(new GetBalanceInput()
        {
            Owner = AdminAddress,
            Symbol = "DAI"
        });
        balance2.Output.Balance.ShouldBe(100000000000000);
        await TokenContractStub.Transfer.SendAsync(new TransferInput()
        {
            Amount = 100000000000,
            Symbol = "ELF",
            Memo = "Recharge",
            To = UserTomAddress
        });
        await TokenContractStub.Transfer.SendAsync(new TransferInput()
        {
            Amount = 100000000000,
            Symbol = "ELF",
            Memo = "Recharge",
            To = UserLilyAddress
        });
        await TokenContractStub.Transfer.SendAsync(new TransferInput()
        {
            Amount = 100000000000,
            Symbol = "TEST",
            Memo = "Recharge",
            To = UserTomAddress
        });
        await TokenContractStub.Transfer.SendAsync(new TransferInput()
        {
            Amount = 100000000000,
            Symbol = "TEST",
            Memo = "Recharge",
            To = UserLilyAddress
        });
        await TokenContractStub.Transfer.SendAsync(new TransferInput()
        {
            Amount = 100000000000,
            Symbol = "DAI",
            Memo = "Recharge",
            To = UserTomAddress
        });

        //authorize  Tom and Lily and admin to transfer ELF and TEST and DAI to FinanceContract
        await UserTomTokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "ELF"
        });
        await UserTomTokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "DAI"
        });
        await UserTomTokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "TEST"
        });
        
        await TokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "TEST"
        });
        await TokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "ELF"
        });
        
        await UserLilyTokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "ELF"
        });
        await UserLilyTokenContractStub.Approve.SendAsync(new ApproveInput()
        {
            Amount = 100000000000,
            Spender = AwakenHooksContractAddress,
            Symbol = "TEST"
        });
    }

    private readonly long _feeRate = 30;
    private readonly long _feeRate2 = 50;
    private async Task Initialize()
    {
        await CreateAndGetToken();
        await AdminLpStub.Initialize.SendAsync(new Token.InitializeInput()
        {
            Owner = AwakenSwapContractAddress
        });
        await AdminLpStub.AddWhiteList.SendAsync(AwakenHooksContractAddress);
        await AdminLpStub.AddWhiteList.SendAsync(AwakenSwapContractAddress);
        await AwakenSwapContractStub.Initialize.SendAsync(new Swap.InitializeInput()
        {
            Admin = AdminAddress,
            AwakenTokenContractAddress = LpTokenContractAddress
        });
        await AwakenSwapContractStub.SetFeeRate.SendAsync(new Int64Value() {Value = _feeRate});
        await AdminHooksStud.Initialize.SendAsync(new InitializeInput
        {
            Admin = AdminAddress,
            SwapContractList = new SwapContractInfoList()
            {
                SwapContracts = { new SwapContractInfo
                {
                    FeeRate = _feeRate,
                    SwapContractAddress = AwakenSwapContractAddress,
                    LpTokenContractAddress = LpTokenContractAddress
                } }
            }
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
}