using AElf;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract
{
    public override Empty Initialize(InitializeInput input)
    {
        Assert(!State.Initialized.Value, "Already initialized.");
        State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
        var author = State.GenesisContract.GetContractAuthor.Call(Context.Self);
        Assert(Context.Sender == author, "No permission.");
        State.TokenContract.Value =
            Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        Assert(!input.PointsContractAddress.Value.IsNullOrEmpty(), "Invalid points contract address.");
        Assert(!input.HooksContractAddress.Value.IsNullOrEmpty(), "Invalid hooks contract address.");
        Assert(!input.OrderContractAddress.Value.IsNullOrEmpty(), "Invalid order contract address.");
        State.PointsContract.Value = input.PointsContractAddress;
        State.HooksContract.Value = input.HooksContractAddress;
        State.OrderContractAddress.Value = input.OrderContractAddress;
        State.Admin.Value = input.Admin ?? Context.Sender;
        State.PointsSettleAdmin.Value = input.PointsSettleAdmin ?? Context.Sender;
        SetPointsRewardConfigList(input.PointsRewardConfigs);
        return new Empty();
    }

    public override Empty SetAdmin(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.Admin.Value = input;
        return new Empty();
    }
    
    public override Empty SetPointsRewardConfigList(SetPointsRewardConfigListInput input)
    {
        CheckAdminPermission();
        Assert(input.Data.Count > 0, "Invalid input list count.");
        SetPointsRewardConfigList(input.Data);
        return new Empty();
    }

    private void SetPointsRewardConfigList(RepeatedField<PointsRewardConfig> rewardConfigs)
    {
        foreach (var pointsRewardConfig in rewardConfigs)
        {
            Assert(pointsRewardConfig != null, "Invalid input.");
            var actionName = pointsRewardConfig.ActionName;
            Assert(IsStringValid(actionName) && pointsRewardConfig.FirstRewardAmount >= 0 
                                             && pointsRewardConfig.Proportion >= 0, "Invalid action name and amount.");
            State.PointsRewardConfig[actionName] = pointsRewardConfig;
        }
    }
    
    public override Empty SetPointsSettleAdmin(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.PointsSettleAdmin.Value = input;
        return new Empty();
    }
    
    public override Empty SetOfficialDomainAlias(SetOfficialDomainAliasInput input)
    {
        Assert(input != null && IsStringValid(input.Alias), "Invalid input.");
        CheckAdminPermission();

        if (State.OfficialDomainAlias.Value == input!.Alias)
        {
            return new Empty();
        }

        State.OfficialDomainAlias.Value = input.Alias;
        return new Empty();
    }

    public override Empty SetPointsContractDAppId(Hash input)
    {
        Assert(IsHashValid(input), "Invalid input.");
        CheckAdminPermission();
        State.PointsContractDAppId.Value = input;
        return new Empty();
    }

    public override Empty SetPointsContract(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.PointsContract.Value = input;
        return new Empty();
    }

    public override Empty SetPricingToken(SetPricingTokenInput input)
    {
        CheckAdminPermission();
        foreach (var pricingToken in input.PricingTokens)
        {
            Assert(IsStringValid(pricingToken.Symbol) && IsStringValid(pricingToken.FromSymbol), "Invalid symbol and fromSymbol.");
            State.PricingTokenMap[pricingToken.Symbol] = pricingToken;
        }
        return new Empty();;
    }

    private void CheckSettleAdminPermission()
    {
        Assert(State.PointsSettleAdmin.Value == Context.Sender, "No Permission.");
    }
    
    private void CheckAdminPermission()
    {
        Assert(State.Admin.Value == Context.Sender, "No Permission.");
    }
}