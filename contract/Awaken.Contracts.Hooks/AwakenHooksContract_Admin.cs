using System.Linq;
using AElf;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContract
{
    public override Empty SetAdmin(Address input)
    {
        Assert(!input.Value.IsNullOrEmpty(), "Invalid input.");
        CheckAdminPermission();
        State.Admin.Value = input;
        return new Empty();
    }

    public override Empty AddSwapContractInfo(AddSwapContractInfoInput input)
    {
        CheckAdminPermission();
        Assert(input.SwapContractList != null && input.SwapContractList.SwapContracts.Count > 0, "Invalid input.");
        FillSwapContractInfoList(input.SwapContractList);
        return new Empty();
    }

    public override Empty RemoveSwapContractInfo(RemoveSwapContractInfoInput input)
    {
        CheckAdminPermission();
        Assert(input.FeeRates.Count > 0, "Invalid input.");
        var swapContractInfoList = State.SwapContractInfoList.Value ??= new SwapContractInfoList();
        foreach (var feeRate in input.FeeRates)
        {
            var swapContractInfo = swapContractInfoList.SwapContracts.FirstOrDefault(t => t.FeeRate == feeRate);
            if (swapContractInfo != null)
            {
                swapContractInfoList.SwapContracts.Remove(swapContractInfo);
            }
        }
        State.SwapContractInfoList.Value = swapContractInfoList;
        return new Empty();
    }

    public override Empty SetMatchLimitOrderEnabled(SetMatchLimitOrderEnabledInput input)
    {
        CheckAdminPermission();
        State.MatchLimitOrderEnabled.Value = input.MatchLimitOrderEnabled;
        State.MultiSwapMatchLimitOrderEnabled.Value = input.MultiSwapMatchLimitOrderEnabled;
        return new Empty();
    }

    public override Empty SetOrderContract(Address input)
    {
        CheckAdminPermission();
        Assert(!input.Value.IsNullOrEmpty(), "Invalid input.");
        State.OrderContract.Value = input;
        return new Empty();
    }
}