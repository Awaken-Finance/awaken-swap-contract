using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract
{
    public override GetSwapContractListOutput GetSwapContractList(Empty input)
    {
        return new GetSwapContractListOutput
        {
            SwapContractList = State.SwapContractInfoList.Value
        };
    }
    
    public override GetAmountsOutOutput GetAmountsOut(GetAmountsOutInput input)
    {
        return new GetAmountsOutOutput()
        {
            Amount = { GetAmountsOut(input.AmountIn, input.Path, input.FeeRates) }
        };
    }

    public override GetAmountsInOutput GetAmountsIn(GetAmountsInInput input)
    {
        return new GetAmountsInOutput
        {
            Amount = { GetAmountsIn(input.AmountOut, input.Path, input.FeeRates) }
        };
    }

    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
    }
}