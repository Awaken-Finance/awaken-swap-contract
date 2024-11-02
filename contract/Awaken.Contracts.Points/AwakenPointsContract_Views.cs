using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract
{
    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
    }

    public override Hash GetPointsContractDAppId(Empty input)
    {
        return State.PointsContractDAppId.Value;
    }

    public override Address GetPointsContract(Empty input)
    {
        return State.PointsContract.Value;
    }

    public override Address GetHooksContract(Empty input)
    {
        return State.HooksContract.Value;
    }

    public override Address GetOracleContract(Empty input)
    {
        return State.OracleContract.Value;
    }

    public override Address GetOrderContract(Empty input)
    {
        return State.OrderContractAddress.Value;
    }

    public override Address GetPointsSettleAdmin(Empty input)
    {
        return State.PointsSettleAdmin.Value;
    }

    public override GetPointsRewardConfigListOutput GetPointsRewardConfig(StringValue input)
    {
        return new GetPointsRewardConfigListOutput
        {
            PointsRewardConfig = State.PointsRewardConfig[input.Value]
        };
    }

    public override Int64Value GetSubscriptId(Empty input)
    {
        return new Int64Value
        {
            Value = State.SubscriptionId.Value
        };
    }

    public override BoolValue GetJoinRecord(Address input)
    {
        return new BoolValue
        {
            Value = State.JoinRecord[input]
        };
    }

    public override StringValue GetOfficialDomainAlias(Empty input)
    {
        return new StringValue
        {
            Value = State.OfficialDomainAlias.Value ?? ""
        };
    }

    public override GetPricingTokenOutput GetPricingToken(StringValue input)
    {
        return new GetPricingTokenOutput
        {
            PricingToken = State.PricingTokenMap[input.Value]
        };
    }
}