using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.MockOracleContract;

public class MockOracleContract: MockOracleContractContainer.MockOracleContractBase
{
    public override Empty SendRequest(SendRequestInput input)
    {
        return new Empty();
    }
}