using System;
using System.Collections.Generic;
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
    private void MixSwapExactTokensForTokensAndLimitOrder(SwapExactTokensForTokens swapInput, RepeatedField<long> amounts, 
        Dictionary<string, FillDetail> fillDetailMap, int maxOrderFillCount, out int orderFilledCount)
    {
        orderFilledCount = 0;
        var amountsOrder = new RepeatedField<long>();
        var amountsPool = new RepeatedField<long>();
        var nextAmountIn = amounts.First();
        var limitOrderMatched = false; 
        for (var pathCount = 0; pathCount <= swapInput.FeeRates.Count - 1; pathCount++)
        {
            var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
            MatchLimitOrderByAmountIn(swapContractAddress, swapInput.Path[pathCount], swapInput.Path[pathCount + 1], 
                nextAmountIn, limitOrderMatched ? 0 : amounts[pathCount + 1], fillDetailMap, maxOrderFillCount,
                out var amountOutPoolFilled, out var amountInPoolFilled, out var amountOutOrderFilled,
                out var pairOrderFilledCount);
            
            amountsPool.Add(amountInPoolFilled);
            amountsPool.Add(amountOutPoolFilled);
            amountsOrder.Add(nextAmountIn - amountInPoolFilled);
            amountsOrder.Add(amountOutOrderFilled);
            nextAmountIn = amountOutPoolFilled + amountOutOrderFilled;
            if (amountOutOrderFilled > 0)
            {
                limitOrderMatched = true;
            }
            orderFilledCount += pairOrderFilledCount;
        }

        TransferFromSender(swapInput.Path[0], amountsOrder[0] + amountsPool[0], "Hooks Swap");
        for (var i = 0; i < swapInput.FeeRates.Count; i++)
        {
            if (amountsPool[2 * i] > 0 && amountsPool[2 * i + 1] > 0)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[i]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[i],
                    Amount = amountsPool[2 * i]
                });
                var swapExactTokensForTokensInput = new Swap.SwapExactTokensForTokensInput()
                {
                    AmountIn = amountsPool[2 * i],
                    AmountOutMin = amountsPool[2 * i + 1],
                    Path = { swapInput.Path[i], swapInput.Path[i + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, nameof(SwapExactTokensForTokens), swapExactTokensForTokensInput.ToByteString());
            }

            if (amountsOrder[2 * i] > 0 && amountsOrder[2 * i + 1] > 0)
            {
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = State.OrderContract.Value,
                    Symbol = swapInput.Path[i],
                    Amount = amountsOrder[2 * i]
                });
                State.OrderContract.FillLimitOrder.Send(new FillLimitOrderInput
                {
                    SymbolIn = swapInput.Path[i + 1],
                    SymbolOut = swapInput.Path[i],
                    AmountOut = amountsOrder[2 * i],
                    MaxCloseIntervalPrice = fillDetailMap[swapInput.Path[i] + "-" + swapInput.Path[i + 1]].Price,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                });
            }
        }
    }
    private void MatchLimitOrderByAmountIn(Address swapContractAddress, string symbolIn, string symbolOut, long amountIn, long amountOut,
        Dictionary<string, FillDetail> fillDetailMap, int maxOrderFillCount, 
        out long amountOutPoolFilled, out long amountInPoolFilled, 
        out long amountOutOrderFilled, out int orderFilledCount)
    {
        amountInPoolFilled = 0;
        amountOutPoolFilled = 0;
        amountOutOrderFilled = 0L;
        orderFilledCount = 0;
        var amountInOrderFilled = 0L;
        if (amountOut == 0)
        {
            amountOut = GetAmountOutFromPool(swapContractAddress, symbolIn, symbolOut, amountIn);
        }
        if (maxOrderFillCount <= 0)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        fillDetailMap.TryGetValue(symbolIn + "-" + symbolOut, out var fillDetail);
        var limitOrderSellPrice = fillDetail?.Price ?? State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
        {
            SymbolIn = symbolOut,
            SymbolOut = symbolIn,
            MinOpenIntervalPrice = 0
        }).Price;
        // no limit order
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
        // pool is better
        if (maxPollSellPrice < limitOrderSellPrice)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }

        var remainOrderMatched = false;
        var minPoolAmountIn = amountIn / 10;
        while (amountInPoolFilled + amountInOrderFilled < amountIn)
        {
            minPoolAmountIn = Math.Min(minPoolAmountIn, amountIn - amountInPoolFilled - amountInOrderFilled);
            var nextPoolAmountIn = amountInPoolFilled + minPoolAmountIn;
            var nextPoolAmountOut = GetAmountOutFromPool(swapContractAddress, symbolIn, symbolOut, nextPoolAmountIn);
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
                // pool is better
                amountInPoolFilled = nextPoolAmountIn;
                amountOutPoolFilled = nextPoolAmountOut;
                continue;
            }

            if (!remainOrderMatched && fillDetail?.Price > 0)
            {
                //try match remain order at this price
                remainOrderMatched = true;
                var singlePriceFillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
                {
                    SymbolIn = symbolOut,
                    SymbolOut = symbolIn,
                    AmountOut = amountIn - amountInPoolFilled - amountInOrderFilled + fillDetail.AmountOut,
                    MinCloseIntervalPrice = fillDetail.Price,
                    MaxOpenIntervalPrice = fillDetail.Price + 1,
                    MaxFillOrderCount = maxOrderFillCount - orderFilledCount
                });
                if (singlePriceFillResult.AmountOutFilled > fillDetail.AmountOut)
                {
                    amountInOrderFilled += singlePriceFillResult.AmountOutFilled - fillDetail.AmountOut;
                    amountOutOrderFilled += singlePriceFillResult.AmountInFilled - fillDetail.AmountIn;
                    orderFilledCount += singlePriceFillResult.OrderFilledCount - fillDetail.OrderFilledCount;
                    fillDetail.AmountOut = singlePriceFillResult.AmountOutFilled;
                    fillDetail.AmountIn = singlePriceFillResult.AmountInFilled;
                    if (amountInOrderFilled + amountInPoolFilled >= amountIn || orderFilledCount >= maxOrderFillCount)
                    {
                        break;
                    }
                }
                limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
                {
                    SymbolIn = symbolOut,
                    SymbolOut = symbolIn,
                    MinOpenIntervalPrice = limitOrderSellPrice
                }).Price;
                if (limitOrderSellPrice == 0 || maxPollSellPrice < limitOrderSellPrice)
                {
                    break;
                }
                continue;
            }
            remainOrderMatched = true;
            var fillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
            {
                SymbolIn = symbolOut,
                SymbolOut = symbolIn,
                AmountOut = amountIn - amountInPoolFilled - amountInOrderFilled,
                MinCloseIntervalPrice = limitOrderSellPrice,
                MaxOpenIntervalPrice = nextPoolSellPrice,
                MaxFillOrderCount = maxOrderFillCount - orderFilledCount
            });
            amountOutOrderFilled += fillResult.AmountInFilled;
            amountInOrderFilled += fillResult.AmountOutFilled;
            orderFilledCount += fillResult.OrderFilledCount;
            if (fillDetail == null)
            {
                fillDetail = new FillDetail();
                fillDetailMap[symbolIn + "-" + symbolOut] = fillDetail;
            }
            fillDetail.Price = fillResult.MaxPriceFilled;
            fillDetail.AmountOut = fillResult.FillDetails.Last().AmountOut;
            fillDetail.AmountIn = fillResult.FillDetails.Last().AmountIn;
            
            if (amountInOrderFilled + amountInPoolFilled >= amountIn || orderFilledCount >= maxOrderFillCount)
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
                break;
            }
        }
        if (amountInOrderFilled + amountInPoolFilled != amountIn)
        {
            amountInPoolFilled = amountIn - amountInOrderFilled;
            amountOutPoolFilled =
                GetAmountOutFromPool(swapContractAddress, symbolIn, symbolOut, amountInPoolFilled);
        }
    }
    
    private void MixSwapTokensForExactTokensAndLimitOrder(SwapTokensForExactTokens swapInput, RepeatedField<long> amounts,
        Dictionary<string, FillDetail> fillDetailMap, int maxOrderFillCount, out int orderFilledCount)
    {
        orderFilledCount = 0;
        var amountsOrder = new RepeatedField<long>();
        var amountsPool = new RepeatedField<long>();
        var nextAmountOut = amounts.Last();
        var limitOrderMatched = false; 
        for (var pathCount = swapInput.FeeRates.Count - 1; pathCount >= 0; pathCount--)
        {
            var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[pathCount]).SwapContractAddress;
            MatchLimitOrderByAmountOut(swapContractAddress, swapInput.Path[pathCount], swapInput.Path[pathCount + 1],
                limitOrderMatched ? 0 : amounts[pathCount],nextAmountOut, fillDetailMap, maxOrderFillCount,
                out var amountOutPoolFilled, out var amountInPoolFilled, out var amountInOrderFilled, out var pairOrderFilledCount);
            
            amountsPool.Insert(0, amountOutPoolFilled);
            amountsPool.Insert(0, amountInPoolFilled);
            amountsOrder.Insert(0, nextAmountOut - amountOutPoolFilled);
            amountsOrder.Insert(0, amountInOrderFilled);
            nextAmountOut = amountInPoolFilled + amountInOrderFilled;
            if (amountInOrderFilled > 0)
            {
                limitOrderMatched = true;
            }
            orderFilledCount += pairOrderFilledCount;
        }

        TransferFromSender(swapInput.Path[0], amountsOrder[0] + amountsPool[0], "Hooks Swap");
        for (var i = 0; i < swapInput.FeeRates.Count; i++)
        {
            if (amountsPool[2 * i] > 0 && amountsPool[2 * i + 1] > 0)
            {
                var swapContractAddress = GetSwapContractInfo(swapInput.FeeRates[i]).SwapContractAddress;
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = swapContractAddress,
                    Symbol = swapInput.Path[i],
                    Amount = amountsPool[2 * i]
                });
                var swapTokensForExactTokensInput = new Swap.SwapTokensForExactTokensInput()
                {
                    AmountInMax = amountsPool[2 * i],
                    AmountOut = amountsPool[2 * i + 1],
                    Path = { swapInput.Path[i], swapInput.Path[i + 1] },
                    Deadline = swapInput.Deadline,
                    Channel = swapInput.Channel,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                };
                Context.SendInline(swapContractAddress, nameof(SwapTokensForExactTokens), swapTokensForExactTokensInput.ToByteString());
            }

            if (amountsOrder[2 * i] > 0 && amountsOrder[2 * i] > 0)
            {
                State.TokenContract.Approve.Send(new ApproveInput()
                {
                    Spender = State.OrderContract.Value,
                    Symbol = swapInput.Path[i],
                    Amount = amountsOrder[2 * i]
                });
                State.OrderContract.FillLimitOrder.Send(new FillLimitOrderInput
                {
                    SymbolIn = swapInput.Path[i + 1],
                    SymbolOut = swapInput.Path[i],
                    AmountIn = amountsOrder[2 * i + 1],
                    MaxCloseIntervalPrice = fillDetailMap[swapInput.Path[i] + "-" + swapInput.Path[i + 1]].Price,
                    To = i == swapInput.FeeRates.Count - 1 ? swapInput.To : Context.Self
                });
            }
        }
    }
    
    private void MatchLimitOrderByAmountOut(Address swapContractAddress, string symbolIn, string symbolOut, long amountIn, long amountOut, 
        Dictionary<string, FillDetail> fillDetailMap, int maxOrderFillCount,
        out long amountOutPoolFilled, out long amountInPoolFilled, out long amountInOrderFilled, out int orderFilledCount)
    {
        orderFilledCount = 0;
        amountInPoolFilled = 0;
        amountOutPoolFilled = 0;
        amountInOrderFilled = 0L;
        var amountOutOrderFilled = 0L;
        if (amountIn == 0)
        {
            amountIn = GetAmountInFromPool(swapContractAddress, symbolIn, symbolOut, amountOut);
        }

        if (maxOrderFillCount <= 0)
        {
            amountInPoolFilled = amountIn;
            amountOutPoolFilled = amountOut;
            return;
        }
        fillDetailMap.TryGetValue(symbolIn + "-" + symbolOut, out var fillDetail);
        var limitOrderSellPrice = fillDetail?.Price ?? State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
        {
            SymbolIn = symbolOut,
            SymbolOut = symbolIn,
            MinOpenIntervalPrice = 0
        }).Price;
        // no limit order
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
        
        var remainOrderMatched = false;
        var minPoolAmountOut = amountOut / 10;
        while (amountOutPoolFilled + amountOutOrderFilled < amountOut)
        {
            minPoolAmountOut = Math.Min(minPoolAmountOut, amountOut - amountOutPoolFilled - amountOutOrderFilled);
            var nextPoolAmountOut = amountOutPoolFilled + minPoolAmountOut;
            var nextPoolAmountIn = GetAmountInFromPool(swapContractAddress, symbolIn, symbolOut, nextPoolAmountOut);
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
            if (!remainOrderMatched && fillDetail?.Price > 0)
            {
                //try match remain order at this price
                remainOrderMatched = true;
                var singlePriceFillResult = State.OrderContract.GetFillResult.Call(new GetFillResultInput
                {
                    SymbolIn = symbolOut,
                    SymbolOut = symbolIn,
                    AmountIn = amountOut - amountOutPoolFilled - amountOutOrderFilled + fillDetail.AmountIn,
                    MinCloseIntervalPrice = fillDetail.Price,
                    MaxOpenIntervalPrice = fillDetail.Price + 1,
                    MaxFillOrderCount = maxOrderFillCount - orderFilledCount
                });
                if (singlePriceFillResult.AmountInFilled > fillDetail.AmountIn)
                {
                    amountInOrderFilled += singlePriceFillResult.AmountOutFilled - fillDetail.AmountOut;
                    amountOutOrderFilled += singlePriceFillResult.AmountInFilled - fillDetail.AmountIn;
                    orderFilledCount += singlePriceFillResult.OrderFilledCount - fillDetail.OrderFilledCount;
                    fillDetail.AmountIn = singlePriceFillResult.AmountInFilled;
                    if (amountOutOrderFilled + amountOutPoolFilled >= amountOut || orderFilledCount >= maxOrderFillCount)
                    {
                        break;
                    }
                }
                limitOrderSellPrice = State.OrderContract.GetBestSellPrice.Call(new GetBestSellPriceInput
                {
                    SymbolIn = symbolOut,
                    SymbolOut = symbolIn,
                    MinOpenIntervalPrice = limitOrderSellPrice
                }).Price;
                if (limitOrderSellPrice == 0 || maxPollSellPrice < limitOrderSellPrice)
                {
                    break;
                }
                continue;
            }
            remainOrderMatched = true;
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
            orderFilledCount += fillResult.OrderFilledCount;
            if (fillDetail == null)
            {
                fillDetail = new FillDetail();
                fillDetailMap[symbolIn + "-" + symbolOut] = fillDetail;
            }
            fillDetail.Price = fillResult.MaxPriceFilled;
            fillDetail.AmountIn = fillResult.FillDetails.Last().AmountIn;
            if (amountOutOrderFilled + amountOutPoolFilled >= amountOut || orderFilledCount >= maxOrderFillCount)
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
                break;
            }
        }

        if (amountOutOrderFilled + amountOutPoolFilled != amountOut)
        {
            amountOutPoolFilled = amountOut - amountOutOrderFilled;
            amountInPoolFilled = GetAmountInFromPool(swapContractAddress, symbolIn, symbolOut, amountOutPoolFilled);
        }
    }

    private long GetAmountOutFromPool(Address swapContractAddress, string symbolIn, string symbolOut, long amountIn)
    {
        return Context.Call<Int64Value>(swapContractAddress, "GetAmountOut", new GetAmountOutInput
        {
            SymbolIn = symbolIn,
            SymbolOut = symbolOut,
            AmountIn = amountIn
        }).Value;
    }
    
    private long GetAmountInFromPool(Address swapContractAddress, string symbolIn, string symbolOut, long amountOut)
    {
        return Context.Call<Int64Value>(swapContractAddress, "GetAmountIn", new GetAmountInInput()
        {
            SymbolIn = symbolIn,
            SymbolOut = symbolOut,
            AmountOut = amountOut
        }).Value;
    }
}