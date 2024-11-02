
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.MockPointsContract;

public class MockPointsContract : MockPointsContractContainer.MockPointsContractBase
{
    public override Empty Join(JoinInput input)
    {
        return new Empty();
    }

    public override Empty Settle(SettleInput input)
    {
        return new Empty();
    }

    public override Empty AcceptReferral(AcceptReferralInput input)
    {
        return new Empty();
    }
}