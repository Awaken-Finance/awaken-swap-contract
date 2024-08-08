using System;
using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Types;
using Awaken.Contracts.Order;
using Awaken.Contracts.Swap;
using Google.Protobuf.WellKnownTypes;
using AElf.Sdk.CSharp;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract
{
    private void MixSwapExactTokensForTokensAndLimitOrder(SwapExactTokensForTokens swapInput, RepeatedField<long> amounts)
    {
        var amountsOrder = new RepeatedField<long>();
        var maxOrderSellPrices = new RepeatedField<long>();
        var amountsPool = new RepeatedField<long>();
        var nextAmountIn = amounts.First();
        var limitOrderMatched = false; 
        for (var pathCount = 0; pathCount <= swapInput.FeeRates.Count - 1; pathCount++)
        {
            var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
            MatchLimitOrderByAmountIn(swapContractAddress, swapInput.Path[pathCount], swapInput.Path[pathCount + 1], 
                nextAmountIn, limitOrderMatched ? 0 : amounts[pathCount + 1],
                out var amountOutPoolFilled, out var amountInPoolFilled, out var amountOutOrderFilled, out var maxOrderSellPrice);
            if (pathCount == 0)
            {
                amountsPool.Add(amountInPoolFilled);
                amountsOrder.Add(nextAmountIn - amountInPoolFilled);
            }
            amountsPool.Add(amountOutPoolFilled);
            amountsOrder.Add(amountOutOrderFilled);
            nextAmountIn = amountOutPoolFilled + amountOutOrderFilled;
            if (amountOutOrderFilled > 0)
            {
                limitOrderMatched = true;
            }
            maxOrderSellPrices.Add(maxOrderSellPrice);
        }

        TransferFromSender(swapInput.Path[0], amountsOrder[0] + amountsPool[0], "Hooks Swap");
        for (var i = 0; i < amountsOrder.Count - 1; i++)
        {
            if (amountsPool[i] > 0)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[i]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[i],
                    Amount = amountsPool[i]
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amountsPool[i],
                    AmountOutMin = amountsPool[i + 1],
                    Path = { swapInput.Path[i], swapInput.Path[i + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, nameof(SwapExactTokensForTokens), swapExactTokensForTokensInput.ToByteString());
            }

            if (amountsOrder[i] > 0)
            {
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = State.OrderContract.Value,
                    Symbol = swapInput.Path[i],
                    Amount = amountsOrder[i]
                });
                State.OrderContract.FillLimitOrder.Send(new FillLimitOrderInput
                {
                    SymbolIn = swapInput.Path[i + 1],
                    SymbolOut = swapInput.Path[i],
                    AmountIn = amountsOrder[i + 1],
                    MaxOpenIntervalPrice = maxOrderSellPrices[i],
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                });
            }
        }
    }
    private void MatchLimitOrderByAmountIn(Address swapContractAddress, string symbolIn, string symbolOut, long amountIn, long amountOut,
        out long amountOutPoolFilled, out long amountInPoolFilled, out long amountOutOrderFilled, out long maxOrderSellPrice)
    {
        amountInPoolFilled = 0;
        amountOutPoolFilled = 0;
        amountOutOrderFilled = 0L;
        maxOrderSellPrice = 0L;
        var amountInOrderFilled = 0L;
        var limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
        {
            SymbolIn = symbolOut, // u
            SymbolOut = symbolIn, // elf
            MinOpenIntervalPrice = 0
        }).Price;
        if (amountOut == 0)
        {
            amountOut = Context.Call<Int64Value>(swapContractAddress, "GetAmountOut", new GetAmountOutInput()
            {
                SymbolIn = symbolIn,
                SymbolOut = symbolOut,
                AmountIn = amountIn
            }).Value;
        }
        if (limitOrderSellPrice == 0)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        var maxPollSellPrice = State.OrderContract.CalculatePrice.Call(new CalculatePriceInput
        {
            SymbolOut = symbolIn,
            SymbolIn = symbolOut,
            AmountOut = amountIn,
            AmountIn = amountOut
        }).Value;
        if (maxPollSellPrice < limitOrderSellPrice)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }

        var minPoolAmountIn = amountIn / 10;
        while (amountInPoolFilled + amountInOrderFilled < amountIn)
        {
            minPoolAmountIn = Math.Min(minPoolAmountIn, amountIn - amountInPoolFilled - amountInOrderFilled);
            var nextPoolAmountIn = amountInPoolFilled + minPoolAmountIn;
            var nextPoolAmountOut = Context.Call<Int64Value>(swapContractAddress, "GetAmountOut", new GetAmountOutInput()
            {
                SymbolIn = symbolIn,
                SymbolOut = symbolOut,
                AmountIn = nextPoolAmountIn
            }).Value;
            // cross order price
            var nextPoolSellPrice = State.OrderContract.CalculatePrice.Call(new CalculatePriceInput
            {
                SymbolOut = symbolIn,
                SymbolIn = symbolOut,
                AmountOut = minPoolAmountIn,
                AmountIn = nextPoolAmountOut - amountOutPoolFilled
            }).Value;
            if (nextPoolSellPrice < limitOrderSellPrice)
            {
                amountInPoolFilled = nextPoolAmountIn;
                amountOutPoolFilled = nextPoolAmountOut;
                continue;
            }
            var fillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                AmountOut = amountIn - amountInPoolFilled - amountInOrderFilled,
                MinCloseIntervalPrice = limitOrderSellPrice,
                MaxOpenIntervalPrice = nextPoolSellPrice
            });
            amountOutOrderFilled += fillResult.AmountInFilled;
            amountInOrderFilled += fillResult.AmountOutFilled;
            maxOrderSellPrice = fillResult.MaxPriceFilled;
            if (amountInOrderFilled + amountInPoolFilled >= amountIn)
            {
                break;
            }
            limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                MinOpenIntervalPrice = fillResult.MaxPriceFilled
            }).Price;
            if (limitOrderSellPrice == 0 || maxPollSellPrice < limitOrderSellPrice)
            {
                amountInPoolFilled = amountIn - amountInOrderFilled;
                amountOutPoolFilled = Context.Call<Int64Value>(swapContractAddress, "GetAmountOut", new GetAmountOutInput()
                {
                    SymbolIn = symbolIn,
                    SymbolOut = symbolOut,
                    AmountIn = amountInPoolFilled
                }).Value;
                break;
            }
        }
    }
    
    private void MixSwapTokensForExactTokensAndLimitOrder(SwapTokensForExactTokens swapInput, RepeatedField<long> amounts)
    {
        var amountsOrder = new RepeatedField<long>();
        var maxOrderSellPrices = new RepeatedField<long>();
        var amountsPool = new RepeatedField<long>();
        var nextAmountOut = amounts.Last();
        var limitOrderMatched = false; 
        for (var pathCount = swapInput.FeeRates.Count - 1; pathCount >= 0; pathCount--)
        {
            var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
            MatchLimitOrderByAmountOut(swapContractAddress, swapInput.Path[pathCount], swapInput.Path[pathCount + 1],
                limitOrderMatched ? 0 : amounts[pathCount],nextAmountOut,
                out var amountOutPoolFilled, out var amountInPoolFilled, out var amountInOrderFilled, out var maxOrderSellPrice);
            if (pathCount == swapInput.FeeRates.Count - 1)
            {
                amountsPool.Add(amountOutPoolFilled);
                amountsOrder.Add(nextAmountOut - amountOutPoolFilled);
            }
            amountsPool.Insert(0, amountInPoolFilled);
            amountsOrder.Insert(0, amountInOrderFilled);
            nextAmountOut = amountInPoolFilled + amountInOrderFilled;
            if (amountInOrderFilled > 0)
            {
                limitOrderMatched = true;
            }
            maxOrderSellPrices.Insert(0, maxOrderSellPrice);
        }

        TransferFromSender(swapInput.Path[0], amountsOrder[0] + amountsPool[0], "Hooks Swap");
        for (var i = 0; i < amountsOrder.Count - 1; i++)
        {
            if (amountsPool[i] > 0)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[i]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[i],
                    Amount = amountsPool[i]
                });
                var swapTokensForExactTokensInput = new Swap.SwapTokensForExactTokensInput()
                {
                    AmountInMax = amountsPool[i],
                    AmountOut = amountsPool[i + 1],
                    Path = { swapInput.Path[i], swapInput.Path[i + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, nameof(SwapTokensForExactTokens), swapTokensForExactTokensInput.ToByteString());
            }

            if (amountsOrder[i] > 0)
            {
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = State.OrderContract.Value,
                    Symbol = swapInput.Path[i],
                    Amount = amountsOrder[i]
                });
                State.OrderContract.FillLimitOrder.Send(new FillLimitOrderInput
                {
                    SymbolIn = swapInput.Path[i + 1],
                    SymbolOut = swapInput.Path[i],
                    AmountIn = amountsOrder[i + 1],
                    MaxOpenIntervalPrice = maxOrderSellPrices[i],
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                });
            }
        }
    }
    
    private void MatchLimitOrderByAmountOut(Address swapContractAddress, string symbolIn, string symbolOut, long amountIn, long amountOut, 
        out long amountOutPoolFilled, out long amountInPoolFilled, out long amountInOrderFilled, out long maxOrderSellPrice)
    {
        amountInPoolFilled = 0;
        amountOutPoolFilled = 0;
        amountInOrderFilled = 0L;
        maxOrderSellPrice = 0L;
        var amountOutOrderFilled = 0L;
        var limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
        {
            SymbolIn = symbolOut, // u
            SymbolOut = symbolIn, // elf
            MinOpenIntervalPrice = 0
        }).Price;
        if (amountIn == 0)
        {
            amountIn = Context.Call<Int64Value>(swapContractAddress, "GetAmountIn", new GetAmountInInput()
            {
                SymbolIn = symbolIn,
                SymbolOut = symbolOut,
                AmountOut = amountOut
            }).Value;
        }

        if (limitOrderSellPrice == 0)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        var maxPollSellPrice = State.OrderContract.CalculatePrice.Call(new CalculatePriceInput
        {
            SymbolOut = symbolIn,
            SymbolIn = symbolOut,
            AmountOut = amountIn,
            AmountIn = amountOut
        }).Value;
        if (maxPollSellPrice < limitOrderSellPrice)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        
        var minPoolAmountOut = amountOut / 10;
        while (amountOutPoolFilled + amountOutOrderFilled < amountOut)
        {
            minPoolAmountOut = Math.Min(minPoolAmountOut, amountOut - amountOutPoolFilled - amountOutOrderFilled);
            var nextPoolAmountOut = amountOutPoolFilled + minPoolAmountOut;
            var nextPoolAmountIn = Context.Call<Int64Value>(swapContractAddress, "GetAmountIn", new GetAmountInInput()
            {
                SymbolIn = symbolIn,
                SymbolOut = symbolOut,
                AmountOut = nextPoolAmountOut
            }).Value;
            // cross order price
            var nextPoolSellPrice = State.OrderContract.CalculatePrice.Call(new CalculatePriceInput
            {
                SymbolOut = symbolIn,
                SymbolIn = symbolOut,
                AmountOut = nextPoolAmountIn - amountInPoolFilled,
                AmountIn = minPoolAmountOut
            }).Value;
            if (nextPoolSellPrice < limitOrderSellPrice)
            {
                amountOutPoolFilled = nextPoolAmountOut;
                amountInPoolFilled = nextPoolAmountIn;
                continue;
            }
            var fillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                AmountIn = amountOut - amountOutPoolFilled - amountOutOrderFilled,
                MinCloseIntervalPrice = limitOrderSellPrice,
                MaxOpenIntervalPrice = nextPoolSellPrice
            });
            amountOutOrderFilled += fillResult.AmountInFilled;
            amountInOrderFilled += fillResult.AmountOutFilled;
            maxOrderSellPrice = fillResult.MaxPriceFilled;
            if (amountOutOrderFilled + amountOutPoolFilled >= amountOut)
            {
                break;
            }
            limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                MinOpenIntervalPrice = fillResult.MaxPriceFilled
            }).Price;
            if (limitOrderSellPrice == 0 || maxPollSellPrice < limitOrderSellPrice)
            {
                amountOutPoolFilled = amountOut - amountOutOrderFilled;
                amountInPoolFilled = Context.Call<Int64Value>(swapContractAddress, "GetAmountIn", new GetAmountInInput()
                {
                    SymbolIn = symbolIn,
                    SymbolOut = symbolOut,
                    AmountOut = amountOutPoolFilled
                }).Value;
                break;
            }
        }
    }
}