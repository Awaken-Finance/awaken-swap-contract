using System;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using Awaken.Contracts.Order;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContractTests
{
    [Fact]
    public async Task CreateLimitOrderTest()
    {
        await InitializeOrderContract();
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 1,
                MaxPricesEachPriceBook = 1
            }
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 500,
                Spender = OrderContractAddress
            });
        var result = await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 200,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        var logEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderCreated));
        var limitOrderCreated = LimitOrderCreated.Parser.ParseFrom(logEvent.NonIndexed);
        limitOrderCreated.SymbolIn.ShouldBe("ELF");
        limitOrderCreated.SymbolOut.ShouldBe("TEST");
        limitOrderCreated.AmountIn.ShouldBe(100);
        limitOrderCreated.AmountOut.ShouldBe(200);
        var userLimitOrder = await TomOrderStud.GetUserLimitOrder.CallAsync(new Int64Value
        {
            Value = limitOrderCreated.OrderId
        });
        userLimitOrder.AmountIn.ShouldBe(100);
        userLimitOrder.AmountOut.ShouldBe(200);
        userLimitOrder.Maker.ShouldBe(UserTomAddress);
        userLimitOrder.AmountInFilled.ShouldBe(0);
        userLimitOrder.OrderId.ShouldBe(limitOrderCreated.OrderId);
        var priceBook = await TomOrderStud.GetPriceBook.CallAsync(new Int64Value
        {
            Value = 1
        });
        priceBook.PriceList.Prices.Count.ShouldBe(1);
        priceBook.PriceList.Prices[0].ShouldBe(200000000);
        priceBook.PriceBookId.ShouldBe(1);
        priceBook.NextPagePriceBookId.ShouldBe(0);
        var orderBookId = await TomOrderStud.GetOrderBookIdByOrderId.CallAsync(new Int64Value()
        {
            Value = limitOrderCreated.OrderId
        });
        var orderBook = await TomOrderStud.GetOrderBook.CallAsync(orderBookId);
        orderBook.OrderBookId.ShouldBe(1);
        orderBook.NextPageOrderBookId.ShouldBe(0);
        orderBook.SymbolIn.ShouldBe("ELF");
        orderBook.SymbolOut.ShouldBe("TEST");
        orderBook.UserLimitOrders.Count.ShouldBe(1);
        orderBook.UserLimitOrders[0].OrderId.ShouldBe(1);

        await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 300,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        priceBook = await TomOrderStud.GetPriceBook.CallAsync(new Int64Value
        {
            Value = 1
        });
        priceBook.NextPagePriceBookId.ShouldBe(2);
        priceBook = await TomOrderStud.GetPriceBook.CallAsync(new Int64Value
        {
            Value = 2
        });
        priceBook.PriceList.Prices.Count.ShouldBe(1);
        priceBook.PriceList.Prices[0].ShouldBe(300000000);
        priceBook.PriceBookId.ShouldBe(2);
        priceBook.NextPagePriceBookId.ShouldBe(0);
        
        orderBookId = await TomOrderStud.GetOrderBookIdByOrderId.CallAsync(new Int64Value()
        {
            Value = 2
        });
        orderBook = await TomOrderStud.GetOrderBook.CallAsync(orderBookId);
        orderBook.OrderBookId.ShouldBe(2);
        orderBook.NextPageOrderBookId.ShouldBe(0);
        orderBook.SymbolIn.ShouldBe("ELF");
        orderBook.SymbolOut.ShouldBe("TEST");
        orderBook.UserLimitOrders.Count.ShouldBe(1);
        orderBook.UserLimitOrders[0].OrderId.ShouldBe(2);
    }

    [Fact]
    public async Task CreateLimitOrderTest_Failed()
    {
        await InitializeOrderContract();
        var result = await LilyOrderStud.CommitLimitOrder.SendWithExceptionAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "DAI",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 200,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        result.TransactionResult.Error.ShouldContain("Allowance not enough");
        await UserLilyTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "DAI",
                Amount = 100,
                Spender = OrderContractAddress
            });
        result = await LilyOrderStud.CommitLimitOrder.SendWithExceptionAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "DAI",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 200,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        result.TransactionResult.Error.ShouldContain("Balance not enough");
    }

    [Fact]
    public async Task MaxLimitOrdersTest()
    {
        await InitializeOrderContract();
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 50000,
                Spender = OrderContractAddress
            });
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 1,
                MaxPricesEachPriceBook = 1,
                MaxOrderBooksEachPrice = 5,
                MaxPriceBooksEachTradePair = 5,
            }
        });
        for (var i = 0; i <= 4; i++) {
            await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
            {
                SymbolIn = "ELF",
                SymbolOut = "TEST",
                AmountIn = 1,
                AmountOut = 1,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
            });
        }
        var result = await TomOrderStud.CommitLimitOrder.SendWithExceptionAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 1,
            AmountOut = 1,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        result.TransactionResult.Error.ShouldContain("Too many orders at this price.");
        
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "TEST",
                Amount = 50000,
                Spender = OrderContractAddress
            });
        for (var i = 0; i <= 5; i++) {
            await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
            {
                SymbolIn = "TEST",
                SymbolOut = "ELF",
                AmountIn = 1,
                AmountOut = i + 1,
                Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
            });
        }
        result = await TomOrderStud.CommitLimitOrder.SendWithExceptionAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "TEST",
            SymbolOut = "ELF",
            AmountIn = 1,
            AmountOut = 7,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        result.TransactionResult.Error.ShouldContain("Too many price in this tradePair.");
    }

    [Fact]
    public async Task CancelLimitOrderTest()
    {
        await InitializeOrderContract();
        var limitOrderId = await UserTomCommitLimitOrder();
        var result = await AdminOrderStud.CancelLimitOrder.SendWithExceptionAsync(new CancelLimitOrderInput
        {
            OrderId = limitOrderId
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        result = await AdminOrderStud.CancelLimitOrder.SendWithExceptionAsync(new CancelLimitOrderInput
        {
            OrderId = limitOrderId + 1
        });
        result.TransactionResult.Error.ShouldContain("OrderId not existed.");
        result = await TomOrderStud.CancelLimitOrder.SendAsync(new CancelLimitOrderInput
        {
            OrderId = limitOrderId
        });
        var logEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderCancelled));
        LimitOrderCancelled.Parser.ParseFrom(logEvent.NonIndexed).OrderId.ShouldBe(limitOrderId);
        var limitOrder = await TomOrderStud.GetUserLimitOrder.CallAsync(new Int64Value()
        {
            Value = limitOrderId
        });
        limitOrder.OrderId.ShouldBe(0);
        var orderBookId = await TomOrderStud.GetOrderBookIdByOrderId.CallAsync(new Int64Value()
        {
            Value = limitOrderId
        });
        orderBookId.Value.ShouldBe(0);
    }

    [Fact]
    public async Task GetFillResultTest()
    {
        await InitializeOrderContract();
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 1,
                MaxPricesEachPriceBook = 1,
                MaxFillOrderCount = 2
            }
        });
        var limitOrderId1 = await UserTomCommitLimitOrder();
        var limitOrderId2 = await UserTomCommitLimitOrder();
        var limitOrderId3 = await UserTomCommitLimitOrder();
        var price = await AdminOrderStud.CalculatePrice.CallAsync(new CalculatePriceInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 200
        });
        price.Value.ShouldBe(200000000);
        var getBestSellPriceOutput = await AdminOrderStud.GetBestSellPrice.CallAsync(new GetBestSellPriceInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MinOpenIntervalPrice = 0
        });
        getBestSellPriceOutput.Price.ShouldBe(200000000);
        getBestSellPriceOutput = await AdminOrderStud.GetBestSellPrice.CallAsync(new GetBestSellPriceInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MinOpenIntervalPrice = 200000000
        });
        getBestSellPriceOutput.Price.ShouldBe(0);
        var fillResult = await AdminOrderStud.GetFillResult.CallAsync(new GetFillResultInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 50,
            MinCloseIntervalPrice = 200000000,
            MaxOpenIntervalPrice = 300000000
        });
        fillResult.AmountInFilled.ShouldBe(50);
        fillResult.AmountOutFilled.ShouldBe(100);
        fillResult.MaxPriceFilled.ShouldBe(200000000);
        fillResult.OrderFilledCount.ShouldBe(1);
        
        fillResult = await AdminOrderStud.GetFillResult.CallAsync(new GetFillResultInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 150,
            MinCloseIntervalPrice = 200000000,
            MaxOpenIntervalPrice = 300000000
        });
        fillResult.AmountInFilled.ShouldBe(150);
        fillResult.AmountOutFilled.ShouldBe(300);
        fillResult.MaxPriceFilled.ShouldBe(200000000);
        fillResult.OrderFilledCount.ShouldBe(2);
        
        fillResult = await AdminOrderStud.GetFillResult.CallAsync(new GetFillResultInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 250,
            MinCloseIntervalPrice = 200000000,
            MaxOpenIntervalPrice = 300000000
        });
        fillResult.AmountInFilled.ShouldBe(200);
        fillResult.AmountOutFilled.ShouldBe(400);
        fillResult.MaxPriceFilled.ShouldBe(200000000);
        fillResult.OrderFilledCount.ShouldBe(2);
        
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxFillOrderCount = 5
            }
        });
        fillResult = await AdminOrderStud.GetFillResult.CallAsync(new GetFillResultInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 500,
            MinCloseIntervalPrice = 200000000,
            MaxOpenIntervalPrice = 300000000
        });
        fillResult.AmountInFilled.ShouldBe(300);
        fillResult.AmountOutFilled.ShouldBe(600);
        fillResult.MaxPriceFilled.ShouldBe(200000000);
        fillResult.OrderFilledCount.ShouldBe(3);
        
        await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 300,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        fillResult = await AdminOrderStud.GetFillResult.CallAsync(new GetFillResultInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 500,
            MinCloseIntervalPrice = 200000000,
            MaxOpenIntervalPrice = 300000001
        });
        fillResult.AmountInFilled.ShouldBe(400);
        fillResult.AmountOutFilled.ShouldBe(900);
        fillResult.MaxPriceFilled.ShouldBe(300000000);
        fillResult.OrderFilledCount.ShouldBe(4);
    }

    [Fact]
    public async Task FillLimitOrderTest()
    {
        await InitializeOrderContract();
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 1,
                MaxPricesEachPriceBook = 1,
                MaxFillOrderCount = 2
            }
        });
        var limitOrderId1 = await UserTomCommitLimitOrder();
        var limitOrderId2 = await UserTomCommitLimitOrder();
        var limitOrderId3 = await UserTomCommitLimitOrder();
        var fillResult = await AdminOrderStud.FillLimitOrder.SendWithExceptionAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 300000000,
            AmountIn = 50,
            To = AdminAddress
        });
        fillResult.TransactionResult.Error.ShouldContain("Sender is not in white list");
        await AdminOrderStud.AddFillOrderWhiteList.SendAsync(AdminAddress);
        await TokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Spender = OrderContractAddress,
                Symbol = "TEST",
                Amount = 10000
            });
        fillResult = await AdminOrderStud.FillLimitOrder.SendAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 300000000,
            AmountIn = 50,
            To = AdminAddress
        });
        var limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(fillResult.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId1);
        limitOrderFilled.AmountInFilled.ShouldBe(50);
        limitOrderFilled.AmountOutFilled.ShouldBe(100);
        limitOrderFilled.Taker.ShouldBe(AdminAddress);
        var limitOrder1 = await AdminOrderStud.GetUserLimitOrder.CallAsync(new Int64Value()
        {
            Value = limitOrderId1
        });
        limitOrder1.FillTime.ShouldBe(limitOrderFilled.FillTime);
        limitOrder1.AmountInFilled.ShouldBe(50);
        limitOrder1.AmountOutFilled.ShouldBe(100);
        
        fillResult = await AdminOrderStud.FillLimitOrder.SendAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 300000000,
            AmountIn = 200,
            To = AdminAddress
        }); 
        var limitOrderFilled1 = LimitOrderFilled.Parser.ParseFrom(fillResult.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        var limitOrderFilled2 = LimitOrderFilled.Parser.ParseFrom(fillResult.TransactionResult.Logs.Last(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled1.OrderId.ShouldBe(limitOrderId1);
        limitOrderFilled1.AmountInFilled.ShouldBe(50);
        limitOrderFilled1.AmountOutFilled.ShouldBe(100);
        limitOrderFilled1.Taker.ShouldBe(AdminAddress);
        limitOrderFilled2.OrderId.ShouldBe(limitOrderId2);
        limitOrderFilled2.AmountInFilled.ShouldBe(100);
        limitOrderFilled2.AmountOutFilled.ShouldBe(200);
        limitOrderFilled2.Taker.ShouldBe(AdminAddress);
        
        limitOrder1 = await AdminOrderStud.GetUserLimitOrder.CallAsync(new Int64Value()
        {
            Value = limitOrderId1
        });
        limitOrder1.OrderId.ShouldBe(0);
        var limitOrder2 = await AdminOrderStud.GetUserLimitOrder.CallAsync(new Int64Value()
        {
            Value = limitOrderId2
        });
        limitOrder2.OrderId.ShouldBe(0);
        
        await AdminOrderStud.FillLimitOrder.SendAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 300000000,
            AmountIn = 200,
            To = AdminAddress
        }); 
        var limitOrder3 = await AdminOrderStud.GetUserLimitOrder.CallAsync(new Int64Value()
        {
            Value = limitOrderId3
        });
        limitOrder3.OrderId.ShouldBe(0);
    }

    [Fact]
    public async Task FillLimitOrderTest_MultPrice()
    {
        await InitializeOrderContract();
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 1,
                MaxPricesEachPriceBook = 1,
                MaxFillOrderCount = 2
            }
        });
        await AdminOrderStud.AddFillOrderWhiteList.SendAsync(AdminAddress);
        
        var limitOrderId1 = await UserTomCommitLimitOrder();
        var limitOrderId2 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 300);
        var allowance = await TokenContractStub.GetAllowance.CallAsync(new GetAllowanceInput()
        {
            Spender = OrderContractAddress,
            Owner = UserTomAddress,
            Symbol = "ELF"
        });
        await TokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Spender = OrderContractAddress,
                Symbol = "TEST",
                Amount = 10000
            });
        await AdminOrderStud.FillLimitOrder.SendAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 400000000,
            AmountIn = 150,
            To = AdminAddress
        });
        var price = await AdminOrderStud.GetBestSellPrice.CallAsync(new GetBestSellPriceInput()
        {
            MinOpenIntervalPrice = 0,
            SymbolIn = "ELF",
            SymbolOut = "TEST"
        });
        price.Price.ShouldBe(300000000);
        
        await AdminOrderStud.FillLimitOrder.SendAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 400000000,
            AmountIn = 100,
            To = AdminAddress
        });
        price = await AdminOrderStud.GetBestSellPrice.CallAsync(new GetBestSellPriceInput()
        {
            MinOpenIntervalPrice = 0,
            SymbolIn = "ELF",
            SymbolOut = "TEST"
        });
        price.Price.ShouldBe(0);
        var result = await AdminOrderStud.FillLimitOrder.SendWithExceptionAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 400000000,
            AmountIn = 100,
            To = AdminAddress
        });
        result.TransactionResult.Error.ShouldContain("Price book id not existed");

        await AdminOrderStud.RemoveFillOrderWhiteList.SendAsync(AdminAddress);
        result = await AdminOrderStud.FillLimitOrder.SendWithExceptionAsync(new FillLimitOrderInput
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            MaxCloseIntervalPrice = 400000000,
            AmountIn = 100,
            To = AdminAddress
        });
        result.TransactionResult.Error.ShouldContain("Sender is not in white list");
    }

    [Fact]
    public async Task MaxFillOrderCountsTest()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 10
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await AdminOrderStud.SetOrderBookConfig.SendAsync(new SetOrderBookConfigInput
        {
            OrderBookConfig = new OrderBookConfig
            {
                MaxOrdersEachOrderBook = 50,
                MaxPricesEachPriceBook = 50,
                MaxFillOrderCount = 10
            }
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });

        // order + pool
        for (int i = 0; i < 20; i++)
        {
            await UserTomCommitLimitOrder("ELF", "TEST", 1, 1);
        }

        var result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 2000,
                        AmountOutMin = 900,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        var limitOrderFilled =  result.TransactionResult.Logs.Where(t => t.Name == nameof(LimitOrderFilled)).ToList();
        limitOrderFilled.Count.ShouldBe(10);
        var swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountIn.ShouldBe(1990);
    }

    [Fact]
    public async Task MixSwapExactTokensForTokensAndLimitOrderTest()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 50
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        // pool only
        var limitOrderId1 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 300);
        var result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 200,
                        AmountOutMin = 90,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        var logEvent = result.TransactionResult.Logs.FirstOrDefault(t => t.Name == nameof(LimitOrderFilled));
        logEvent.ShouldBeNull();
        
        // order only
        var limitOrderId2 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 150,
                        AmountOutMin = 70,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        var limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId2);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        
        // order + pool
        var limitOrderId3 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 200,
                        AmountOutMin = 90,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId3);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        var swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountIn.ShouldBe(50);
        
        // pool + order + pool
        var limitOrderId4 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 201);
        result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 400000,
                        AmountOutMin = 190000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(201);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId4);
        swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountIn.ShouldBe(399799);
        
        // order + pool + order
        var limitOrderId5 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        var limitOrderId6 = await UserTomCommitLimitOrder("ELF", "TEST", 200000, 403200);
        result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 400000,
                        AmountOutMin = 190000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.ShouldNotBeNull();
    }

    [Fact]
    public async Task MixSwapExactTokensForTokensAndLimitOrderTest_TwoPair()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 50
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        // (pool + order) + allPool
        var limitOrderId3 = await UserTomCommitLimitOrder("ELF", "TEST", 100000, 150000);
        var result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 200000,
                        AmountOutMin = 40000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF","DAI"},
                        FeeRates = { _feeRate,_feeRate }
                    },
                }
            });
        var limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId3);
        limitOrderFilled.AmountInFilled.ShouldBe(100000);
        limitOrderFilled.AmountOutFilled.ShouldBe(150000);
        var swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountIn.ShouldBe(50000);
        
        //  allPool + (pool + order)
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "TEST",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        var limitOrderId4 = await UserTomCommitLimitOrder("TEST", "ELF", 250000, 100000);
        result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 100000,
                        AmountOutMin = 360000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "DAI","ELF","TEST"},
                        FeeRates = { _feeRate,_feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId4);
        limitOrderFilled.AmountInFilled.ShouldBe(250000);
        limitOrderFilled.AmountOutFilled.ShouldBe(100000);
        var swapEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(Swap));
        swap = Swap.Swap.Parser.ParseFrom(swapEvent.NonIndexed);
        swap.SymbolIn.ShouldBe("DAI");
        swap.SymbolOut.ShouldBe("ELF");
        swap.AmountIn.ShouldBe(100000);
        var elfAmountOut = swap.AmountOut;
        result.TransactionResult.Logs.Remove(swapEvent);
        swapEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(Swap));
        swap = Swap.Swap.Parser.ParseFrom(swapEvent.NonIndexed);
        swap.SymbolIn.ShouldBe("ELF");
        swap.SymbolOut.ShouldBe("TEST");
        elfAmountOut.ShouldBe(100000 + swap.AmountIn);
    }

    [Fact]
    public async Task RepeatedSwapExactTokensForTokensAndLimitOrderTest()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 50
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });

        var limitOrderId1 = await UserTomCommitLimitOrder("ELF", "TEST", 200, 300);
        var limitOrderId2 = await UserTomCommitLimitOrder("ELF", "TEST", 200, 320);
        
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "TEST",
                Amount = 10000000,
                Spender = AwakenHooksContractAddress
            });
        var result = await TomHooksStud.SwapExactTokensForTokens.SendAsync(
            new SwapExactTokensForTokensInput
            {
                SwapTokens =
                {
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 150,
                        AmountOutMin = 70,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                    new SwapExactTokensForTokens
                    {
                        AmountIn = 170,
                        AmountOutMin = 70,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    }
                }
            });
        var limitOrderFilled = result.TransactionResult.Logs.Where(t => t.Name == nameof(LimitOrderFilled)).ToList();
        var orderFilled1 = LimitOrderFilled.Parser.ParseFrom(limitOrderFilled[0].NonIndexed);
        var orderFilled2 = LimitOrderFilled.Parser.ParseFrom(limitOrderFilled[1].NonIndexed);
        var orderFilled3 = LimitOrderFilled.Parser.ParseFrom(limitOrderFilled[2].NonIndexed);
        orderFilled1.AmountOutFilled.ShouldBe(150);
        orderFilled1.AmountInFilled.ShouldBe(100);
        orderFilled1.OrderId.ShouldBe(limitOrderId1);
        orderFilled2.AmountOutFilled.ShouldBe(150);
        orderFilled2.AmountInFilled.ShouldBe(100);
        orderFilled2.OrderId.ShouldBe(limitOrderId1);
        orderFilled3.AmountOutFilled.ShouldBe(20);
        orderFilled3.AmountInFilled.ShouldBe(12);
        orderFilled3.OrderId.ShouldBe(limitOrderId2);
    }

    [Fact]
    public async Task MixSwapTokensForExactTokensAndLimitOrderTest()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 50
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        // pool only
        var limitOrderId1 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 300);
        var result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens()
                    {
                        AmountInMax = 250,
                        AmountOut = 100,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        var logEvent = result.TransactionResult.Logs.FirstOrDefault(t => t.Name == nameof(LimitOrderFilled));
        logEvent.ShouldBeNull();
        
        // order only
        var limitOrderId2 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens()
                    {
                        AmountInMax = 250,
                        AmountOut = 100,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        var limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId2);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        
        // order + pool
        var limitOrderId3 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens()
                    {
                        AmountInMax = 400,
                        AmountOut = 150,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId3);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        var swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountOut.ShouldBe(50);
        
        // pool + order + pool
        var limitOrderId4 = await UserTomCommitLimitOrder("ELF", "TEST", 1000, 2008);
        result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens
                    {
                        AmountInMax = 400000,
                        AmountOut = 190000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.AmountInFilled.ShouldBe(1000);
        limitOrderFilled.AmountOutFilled.ShouldBe(2008);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId4);
        swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.AmountOut.ShouldBe(189000);
        
        // order + pool + order
        var limitOrderId5 = await UserTomCommitLimitOrder("ELF", "TEST", 100, 150);
        var limitOrderId6 = await UserTomCommitLimitOrder("ELF", "TEST", 200000, 403200);
        result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput()
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens
                    {
                        AmountInMax = 400000,
                        AmountOut = 190000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF" },
                        FeeRates = { _feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.AmountInFilled.ShouldBe(100);
        limitOrderFilled.AmountOutFilled.ShouldBe(150);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId5);
        swap = Swap.Swap.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(Swap)).NonIndexed);
        swap.ShouldNotBeNull();
        result.TransactionResult.Logs.Remove(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)));
        var limitOrderFilled2 = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled2.OrderId.ShouldBe(limitOrderId6);
        var allAmountOut = limitOrderFilled.AmountInFilled + limitOrderFilled2.AmountInFilled + swap.AmountOut;
        allAmountOut.ShouldBe(190000);
    }
    
    [Fact]
    public async Task MixSwapTokensForExactTokensAndLimitOrderTest_TwoPair()
    {
        await CreateAndAddLiquidity();
        await AdminHooksStud.SetLimitOrderConfig.SendAsync(new SetLimitOrderConfigInput()
        {
            MatchLimitOrderEnabled = true,
            MultiSwapMatchLimitOrderEnabled = true,
            MaxFillLimitOrderCount = 50
        });
        await AdminHooksStud.SetOrderContract.SendAsync(OrderContractAddress);
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        // (pool + order) + allPool
        var limitOrderId3 = await UserTomCommitLimitOrder("ELF", "TEST", 10000, 15000);
        var result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens
                    {
                        AmountInMax = 200000,
                        AmountOut = 40000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "TEST","ELF","DAI"},
                        FeeRates = { _feeRate,_feeRate }
                    },
                }
            });
        var limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId3);
        limitOrderFilled.AmountInFilled.ShouldBe(10000);
        limitOrderFilled.AmountOutFilled.ShouldBe(15000);
        var swaps = result.TransactionResult.Logs.Where(t => t.Name == nameof(Swap)).ToList();
        swaps.Count.ShouldBe(2);
        var firstSwap = Swap.Swap.Parser.ParseFrom(swaps[0].NonIndexed);
        firstSwap.SymbolIn.ShouldBe("TEST");
        firstSwap.SymbolOut.ShouldBe("ELF");
        var secondSwap = Swap.Swap.Parser.ParseFrom(swaps[1].NonIndexed);
        secondSwap.AmountIn.ShouldBe(10000 + firstSwap.AmountOut);
        
        //  allPool + (pool + order)
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "TEST",
                Amount = 10000000,
                Spender = OrderContractAddress
            });
        var limitOrderId4 = await UserTomCommitLimitOrder("TEST", "ELF", 250000, 100000);
        result = await TomHooksStud.SwapTokensForExactTokens.SendAsync(
            new SwapTokensForExactTokensInput
            {
                SwapTokens =
                {
                    new SwapTokensForExactTokens
                    {
                        AmountInMax = 100000,
                        AmountOut = 360000,
                        Channel = "",
                        Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 0, 3))),
                        To = UserTomAddress,
                        Path = { "DAI","ELF","TEST"},
                        FeeRates = { _feeRate,_feeRate }
                    },
                }
            });
        limitOrderFilled = LimitOrderFilled.Parser.ParseFrom(result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderFilled)).NonIndexed);
        limitOrderFilled.OrderId.ShouldBe(limitOrderId4);
        limitOrderFilled.AmountInFilled.ShouldBe(250000);
        limitOrderFilled.AmountOutFilled.ShouldBe(100000);
        swaps = result.TransactionResult.Logs.Where(t => t.Name == nameof(Swap)).ToList();
        swaps.Count.ShouldBe(2);
        firstSwap = Swap.Swap.Parser.ParseFrom(swaps[0].NonIndexed);
        firstSwap.SymbolIn.ShouldBe("DAI");
        firstSwap.SymbolOut.ShouldBe("ELF");
        secondSwap = Swap.Swap.Parser.ParseFrom(swaps[1].NonIndexed);
        firstSwap.AmountOut.ShouldBe(100000 + secondSwap.AmountIn);
    }

    private async Task<long> UserTomCommitLimitOrder()
    {
        await UserTomTokenContractStub.Approve.SendAsync(
            new ApproveInput
            {
                Symbol = "ELF",
                Amount = 10000,
                Spender = OrderContractAddress
            });
        var result = await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
        {
            SymbolIn = "ELF",
            SymbolOut = "TEST",
            AmountIn = 100,
            AmountOut = 200,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        var logEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderCreated));
        return LimitOrderCreated.Parser.ParseFrom(logEvent.NonIndexed).OrderId;
    }
    
    private async Task<long> UserTomCommitLimitOrder(string symbolIn, string symbolOut, long amountIn, long amountOut)
    {
        var result = await TomOrderStud.CommitLimitOrder.SendAsync(new CommitLimitOrderInput()
        {
            SymbolIn = symbolIn,
            SymbolOut = symbolOut,
            AmountIn = amountIn,
            AmountOut = amountOut,
            Deadline = Timestamp.FromDateTime(DateTime.UtcNow.Add(new TimeSpan(0, 1, 0)))
        });
        var logEvent = result.TransactionResult.Logs.First(t => t.Name == nameof(LimitOrderCreated));
        return LimitOrderCreated.Parser.ParseFrom(logEvent.NonIndexed).OrderId;
    }

    private async Task InitializeOrderContract()
    {
        await CreateAndGetToken();
        await AdminOrderStud.Initialize.SendAsync(new Order.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress
        });
    }
}