using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract
{
    public override Address GetAdmin(Empty input)
    {
        return base.GetAdmin(input);
    }

    public override Hash GetPointsContractDAppId(Empty input)
    {
        return base.GetPointsContractDAppId(input);
    }

    public override Address GetPointsContract(Empty input)
    {
        return base.GetPointsContract(input);
    }

    public override Address GetPointsSettleAdmin(Empty input)
    {
        return base.GetPointsSettleAdmin(input);
    }

    public override Int64Value GetPointsRewardConfig(StringValue input)
    {
        return base.GetPointsRewardConfig(input);
    }

    public override BoolValue GetJoinRecord(Address input)
    {
        return base.GetJoinRecord(input);
    }

    public override StringValue GetOfficialDomainAlias(Empty input)
    {
        return base.GetOfficialDomainAlias(input);
    }
    
    
}