using System.Collections.Generic;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Awaken.Contracts.Hooks;
using Google.Protobuf.WellKnownTypes;
using Points.Contracts.Point;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract : AwakenPointsContractImplContainer.AwakenPointsContractImplBase
{
    public override Empty Join(JoinInput input)
    {
        Assert(input != null && IsStringValid(input.Domain), "Invalid input.");
        Assert(!State.JoinRecord[Context.Sender], "Already joined.");

        JoinPointsContract(input.Domain);

        return new Empty();
    }

    public override Empty BatchSettle(BatchSettleInput input)
    {
        CheckSettleAdminPermission();
        Assert(input.UserPointsList != null && input.UserPointsList.Count > 0, "Invalid input.");
        var userPointsList = new List<global::Points.Contracts.Point.UserPoints>();
        foreach (var userPoints in input.UserPointsList)
        {
            JoinPointsContract(null, userPoints.UserAddress);
            userPointsList.Add(new global::Points.Contracts.Point.UserPoints
            {
                UserAddress = userPoints.UserAddress,
                UserPointsValue = userPoints.UserPointsValue
            });
        }

        State.PointsContract.BatchSettle.Send(new global::Points.Contracts.Point.BatchSettleInput
        {
            ActionName = input.ActionName,
            DappId = State.PointsContractDAppId.Value,
            UserPointsList = { userPointsList }
        });
        return new Empty();
    }

    public override Empty AcceptReferral(AcceptReferralInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input.Referrer) && State.JoinRecord[input.Referrer], "Invalid referrer.");
        Assert(!State.JoinRecord[Context.Sender], "Already joined.");
        
        State.JoinRecord[Context.Sender] = true;
        
        State.PointsContract.AcceptReferral.Send(new global::Points.Contracts.Point.AcceptReferralInput()
        {
            DappId = State.PointsContractDAppId.Value,
            Referrer = input.Referrer,
            Invitee = Context.Sender
        });
        
        Context.Fire(new ReferralAccepted
        {
            Invitee = Context.Sender,
            Referrer = input.Referrer
        });
        
        return new Empty();
    }
    
    private void JoinPointsContract(string domain, Address registrant = null)
    {
        registrant ??= Context.Sender;
        if (!IsHashValid(State.PointsContractDAppId.Value) || State.PointsContract.Value == null)
        {
            return;
        }

        if (State.JoinRecord[registrant]) return;

        if (domain == null || domain == State.OfficialDomainAlias.Value)
        {
            domain = State.PointsContract.GetDappInformation.Call(new GetDappInformationInput
            {
                DappId = State.PointsContractDAppId.Value
            })?.DappInfo?.OfficialDomain;
        }

        State.JoinRecord[registrant] = true;

        State.PointsContract.Join.Send(new global::Points.Contracts.Point.JoinInput()
        {
            DappId = State.PointsContractDAppId.Value,
            Domain = domain,
            Registrant = registrant
        });

        Context.Fire(new Joined
        {
            Domain = domain,
            Registrant = registrant
        });
    }

    public override Empty FinishAction(FinishActionInput input)
    {
        Assert(input?.ActionDetail != null && IsAddressValid(input.ActionDetail.Address), "Invalid input.");
        Assert(IsStringValid(input.ActionDetail.SymbolA) && IsStringValid(input.ActionDetail.SymbolB), "Invalid input.");
        if (input.ActionType == ActionType.CommitLimitOrder || input.ActionType == ActionType.LimitOrderFilled)
        {
            Assert(Context.Sender == State.OrderContractAddress.Value, "No permission");
        }
        else if (input.ActionType == ActionType.Swap || input.ActionType == ActionType.AddLiquidity)
        {
            Assert(Context.Sender == State.HooksContract.Value, "No permission");
        }

        var configActionName = input.ActionType.ToString();
        var pointsRewardConfig = State.PointsRewardConfig[input.ActionType.ToString()];
        if (pointsRewardConfig == null || (pointsRewardConfig.FirstRewardAmount == 0 && pointsRewardConfig.Proportion == 0))
        {
            return new Empty();
        }

        if (pointsRewardConfig.Proportion > 0)
        {
            var value = CalculateValue(input.ActionDetail.SymbolA, input.ActionDetail.AmountA);
            if (input.ActionType == ActionType.AddLiquidity)
            {
                value += CalculateValue(input.ActionDetail.SymbolB, input.ActionDetail.AmountB);
            }
            else if (value == 0)
            {
                value = CalculateValue(input.ActionDetail.SymbolB, input.ActionDetail.AmountB);
            }
            if (value > 0)
            {
                var bigIntValue = new BigIntValue(value);
                var pointsAmountStr = bigIntValue.Mul(pointsRewardConfig.Proportion).Div(FeeRateMax).Value;
                if (!long.TryParse(pointsAmountStr, out var pointsAmount))
                {
                    throw new AssertionException($"Failed to parse {pointsAmountStr}");
                }
                State.PointsContract.Settle.Send(new SettleInput
                {
                    DappId = State.PointsContractDAppId.Value,
                    ActionName = configActionName,
                    UserAddress = input.ActionDetail.Address,
                    UserPoints = pointsRewardConfig.FirstRewardAmount
                });
            }
        }

        if (pointsRewardConfig.FirstRewardAmount > 0 && !State.DisposablePointsSettleRecord[input.ActionDetail.Address][configActionName])
        {
            State.PointsContract.Settle.Send(new SettleInput
            {
                DappId = State.PointsContractDAppId.Value,
                ActionName = configActionName,
                UserAddress = input.ActionDetail.Address,
                UserPoints = pointsRewardConfig.FirstRewardAmount
            });
            State.DisposablePointsSettleRecord[Context.Sender][configActionName] = true;
        }

        return new Empty();
    }

    private long CalculateValue(string symbol, long amount)
    {
        var symbolHash = HashHelper.ComputeFrom(symbol);
        var price = State.PriceMap[symbolHash];
        var tokenInfo = State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
        {
            Symbol = symbol
        });
        if (price <= 0)
        {
            var pricingToken = State.PricingTokenMap[symbol];
            if (pricingToken == null)
            {
                return 0;
            }

            var fromSymbolHash = HashHelper.ComputeFrom(pricingToken.FromSymbol);
            var fromSymbolPrice = State.PriceMap[fromSymbolHash];
            if (fromSymbolPrice <= 0)
            {
                return 0;
            }

            var relativePrice = State.HooksContract.Quote.Call(new QuoteInput
            {
                SymbolA = symbol,
                SymbolB = pricingToken.FromSymbol,
                AmountA = IntPow(10, tokenInfo.Decimals),
                FeeRate = pricingToken.FromFeeRate
            }).Value;
            var fromTokenInfo = State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
            {
                Symbol = pricingToken.FromSymbol
            });
            var priceStr = new BigIntValue(relativePrice).Mul(fromSymbolPrice).Div(IntPow(10, fromTokenInfo.Decimals)).Value;
            if (long.TryParse(priceStr, out price))
            {
                throw new AssertionException($"Failed to parse {priceStr}");
            }
        }

        if (price <= 0)
        {
            return 0;
        }
        var valueStr = new BigIntValue(amount).Mul(price).Div(IntPow(10, tokenInfo.Decimals)).Value;
        if (long.TryParse(valueStr, out var value))
        {
            throw new AssertionException($"Failed to parse {valueStr}");
        }
        return value;
    }
    
}