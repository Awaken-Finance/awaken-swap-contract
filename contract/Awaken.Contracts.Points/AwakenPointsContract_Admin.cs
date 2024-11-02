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
        Assert(IsAddressValid(input.PointsContractAddress), "Invalid points contract address.");
        Assert(IsAddressValid(input.HooksContractAddress), "Invalid hooks contract address.");
        Assert(IsAddressValid(input.OrderContractAddress), "Invalid order contract address.");
        Assert(IsAddressValid(input.OracleContractAddress), "Invalid oracle contract address.");
        State.PointsContract.Value = input.PointsContractAddress;
        State.HooksContract.Value = input.HooksContractAddress;
        State.OracleContract.Value = input.OracleContractAddress;
        State.OrderContractAddress.Value = input.OrderContractAddress;
        State.Admin.Value = input.Admin ?? Context.Sender;
        State.PointsSettleAdmin.Value = input.PointsSettleAdmin ?? Context.Sender;
        State.OfficialDomainAlias.Value = input.OfficialDomainAlias;
        State.SubscriptionId.Value = input.SubscriptionId;
        State.PointsContractDAppId.Value = input.PointsContractDAppId;
        SetPointsRewardConfigList(input.PointsRewardConfigs);
        SetPricingTokens(input.PricingTokens);
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

        if (State.OfficialDomainAlias.Value == input.Alias)
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

    public override Empty SetHooksContract(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.HooksContract.Value = input;
        return new Empty();
    }

    public override Empty SetOrderContract(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.OrderContractAddress.Value = input;
        return new Empty();
    }

    public override Empty SetOracleContract(Address input)
    {
        Assert(IsAddressValid(input), "Invalid input.");
        CheckAdminPermission();
        State.OracleContract.Value = input;
        return new Empty();
    }

    public override Empty SetPricingToken(SetPricingTokenInput input)
    {
        CheckAdminPermission();
        SetPricingTokens(input.PricingTokens);
        return new Empty();;
    }

    private void SetPricingTokens(RepeatedField<PricingToken> pricingTokens)
    {
        foreach (var pricingToken in pricingTokens)
        {
            Assert(IsStringValid(pricingToken.Symbol) && IsStringValid(pricingToken.FromSymbol), "Invalid symbol and fromSymbol.");
            State.PricingTokenMap[pricingToken.Symbol] = pricingToken;
        }
    }

    public override Empty SetSubscriptId(Int64Value input)
    {
        Assert(input.Value > 0, "Invalid input.");
        CheckAdminPermission();
        State.SubscriptionId.Value = input.Value;
        return new Empty();
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