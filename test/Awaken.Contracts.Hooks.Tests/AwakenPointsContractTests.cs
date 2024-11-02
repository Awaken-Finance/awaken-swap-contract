using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Types;
using Awaken.Contracts.Points;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Awaken.Contracts.Hooks;

public partial class AwakenHooksContractTests
{
    private Hash _pointsContractDAppId = HashHelper.ComputeFrom("AwakenPoints");
    
    [Fact]
    public async Task InitializePointsTest()
    {
        var result = await TomPointsStud.Initialize.SendWithExceptionAsync(new Points.InitializeInput());
        result.TransactionResult.Error.ShouldContain("No permission");
        result = await AdminPointsStud.Initialize.SendWithExceptionAsync(new Points.InitializeInput());
        result.TransactionResult.Error.ShouldContain("Invalid points contract address");
        await AdminPointsStud.Initialize.SendAsync(new Points.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress,
            OrderContractAddress = OrderContractAddress,
            OracleContractAddress = MockOracleContractAddress,
            PointsContractAddress = MockPointsContractAddress,
            OfficialDomainAlias = "AWK",
            PointsContractDAppId = _pointsContractDAppId,
            SubscriptionId = 1,
            PointsRewardConfigs = { new PointsRewardConfig
            {
                ActionName = ActionType.Swap.ToString(),
                FirstRewardAmount = 500,
                Proportion = 10000
            } },
            PricingTokens = { new PricingToken
            {
                Symbol = "TEST",
                FromSymbol = "ELF",
                FromFeeRate = _feeRate
            } }
        });
        
        var admin = await AdminPointsStud.GetAdmin.CallAsync(new Empty());
        var settleAdmin = await AdminPointsStud.GetPointsSettleAdmin.CallAsync(new Empty());
        admin.ShouldBe(AdminAddress);
        settleAdmin.ShouldBe(AdminAddress);
        
        var pointsContractAddress = await AdminPointsStud.GetPointsContract.CallAsync(new Empty());
        var hooksContractAddress = await AdminPointsStud.GetHooksContract.CallAsync(new Empty());
        var orderContractAddress = await AdminPointsStud.GetOrderContract.CallAsync(new Empty());
        var oracleContractAddress = await AdminPointsStud.GetOracleContract.CallAsync(new Empty());
        pointsContractAddress.ShouldBe(MockPointsContractAddress);
        hooksContractAddress.ShouldBe(AwakenHooksContractAddress);
        orderContractAddress.ShouldBe(OrderContractAddress);
        oracleContractAddress.ShouldBe(MockOracleContractAddress);
        
        var officialDomain = await AdminPointsStud.GetOfficialDomainAlias.CallAsync(new Empty());
        officialDomain.Value.ShouldBe("AWK");
        var dAppId = await AdminPointsStud.GetPointsContractDAppId.CallAsync(new Empty());
        dAppId.Value.ShouldBe(_pointsContractDAppId);
        var subscriptId = await AdminPointsStud.GetSubscriptId.CallAsync(new Empty());
        subscriptId.Value.ShouldBe(1);

        var pointsRewardConfig = await AdminPointsStud.GetPointsRewardConfig.CallAsync(new StringValue()
        {
            Value = "Swap"
        });
        pointsRewardConfig.PointsRewardConfig.ActionName.ShouldBe("Swap");
        pointsRewardConfig.PointsRewardConfig.FirstRewardAmount.ShouldBe(500);
        pointsRewardConfig.PointsRewardConfig.Proportion.ShouldBe(10000);
        var pricingTokens = await AdminPointsStud.GetPricingToken.CallAsync(new StringValue()
        {
            Value = "TEST"
        });
        pricingTokens.PricingToken.Symbol.ShouldBe("TEST");
        pricingTokens.PricingToken.FromSymbol.ShouldBe("ELF");
        pricingTokens.PricingToken.FromFeeRate.ShouldBe(_feeRate);
    }

    [Fact]
    public async Task PointContractAdminCheckTest()
    {
        await InitializePointContract();
        var result = await TomPointsStud.SetPointsContract.SendWithExceptionAsync(AdminAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetPointsContract.SendAsync(AdminAddress);
        var pointContractAddress = await AdminPointsStud.GetPointsContract.CallAsync(new Empty());
        pointContractAddress.ShouldBe(AdminAddress);
        
        result = await TomPointsStud.SetHooksContract.SendWithExceptionAsync(AdminAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetHooksContract.SendAsync(AdminAddress);
        var hooksContractAddress = await AdminPointsStud.GetHooksContract.CallAsync(new Empty());
        hooksContractAddress.ShouldBe(AdminAddress);
        
        result = await TomPointsStud.SetOrderContract.SendWithExceptionAsync(AdminAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetOrderContract.SendAsync(AdminAddress);
        var orderContractAddress = await AdminPointsStud.GetOrderContract.CallAsync(new Empty());
        orderContractAddress.ShouldBe(AdminAddress);
        
        result = await TomPointsStud.SetOracleContract.SendWithExceptionAsync(AdminAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetOracleContract.SendAsync(AdminAddress);
        var oracleContractAddress = await AdminPointsStud.GetOracleContract.CallAsync(new Empty());
        oracleContractAddress.ShouldBe(AdminAddress);
        
        result = await TomPointsStud.SetOfficialDomainAlias.SendWithExceptionAsync(new SetOfficialDomainAliasInput()
        {
            Alias = "XX"
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetOfficialDomainAlias.SendAsync(new SetOfficialDomainAliasInput()
        {
            Alias = "XX"
        });
        var domain = await AdminPointsStud.GetOfficialDomainAlias.CallAsync(new Empty());
        domain.Value.ShouldBe("XX");
        
        result = await TomPointsStud.SetPointsSettleAdmin.SendWithExceptionAsync(UserTomAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetPointsSettleAdmin.SendAsync(UserTomAddress);
        var settleAdmin = await AdminPointsStud.GetPointsSettleAdmin.CallAsync(new Empty());
        settleAdmin.ShouldBe(UserTomAddress);
        
        result = await TomPointsStud.SetSubscriptId.SendWithExceptionAsync(new Int64Value
        {
            Value = 111
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetSubscriptId.SendAsync(new Int64Value
        {
            Value = 111
        });
        var subscriptId = await AdminPointsStud.GetSubscriptId.CallAsync(new Empty());
        subscriptId.Value.ShouldBe(111);
        
        result = await TomPointsStud.SetPointsContractDAppId.SendWithExceptionAsync(_pointsContractDAppId);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetPointsContractDAppId.SendAsync(_pointsContractDAppId);
        var dAppId = await AdminPointsStud.GetPointsContractDAppId.CallAsync(new Empty());
        dAppId.ShouldBe(_pointsContractDAppId);
        
        result = await TomPointsStud.SetPricingToken.SendWithExceptionAsync(new SetPricingTokenInput());
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetPricingToken.SendAsync(new SetPricingTokenInput()
        {
            PricingTokens = { new PricingToken
            {
                Symbol = "TEST",
                FromSymbol = "ELF",
                FromFeeRate = _feeRate
            } }
        });
        var pricingToken = await AdminPointsStud.GetPricingToken.CallAsync(new StringValue()
        {
            Value = "TEST"
        });
        pricingToken.PricingToken.FromSymbol.ShouldBe("ELF");
        pricingToken.PricingToken.Symbol.ShouldBe("TEST");
        pricingToken.PricingToken.FromFeeRate.ShouldBe(_feeRate);
        
        result = await TomPointsStud.SetPointsRewardConfigList.SendWithExceptionAsync(new SetPointsRewardConfigListInput());
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetPointsRewardConfigList.SendAsync(new SetPointsRewardConfigListInput()
        {
            Data = { new PointsRewardConfig
            {
                ActionName = ActionType.Swap.ToString(),
                FirstRewardAmount = 500,
                Proportion = 10000
            } }
        });
        var rewardConfig = await AdminPointsStud.GetPointsRewardConfig.CallAsync(new StringValue()
        {
            Value = ActionType.Swap.ToString()
        });
        rewardConfig.PointsRewardConfig.ActionName.ShouldBe(ActionType.Swap.ToString());
        rewardConfig.PointsRewardConfig.FirstRewardAmount.ShouldBe(500);
        rewardConfig.PointsRewardConfig.Proportion.ShouldBe(10000);
        
        result = await TomPointsStud.SetAdmin.SendWithExceptionAsync(AdminAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
        await AdminPointsStud.SetAdmin.SendAsync(UserTomAddress);
        var admin = await AdminPointsStud.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(UserTomAddress);
    }

    [Fact]
    public async Task JoinTest()
    {
        await InitializePointContract();
        var result = await AdminPointsStud.Join.SendWithExceptionAsync(new JoinInput
        {
            Domain = "",
        });
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        result = await AdminPointsStud.Join.SendWithExceptionAsync(new JoinInput
        {
            Domain = "AAA",
        });
        result.TransactionResult.Error.ShouldContain("Points contract is not configured.");
        
        await AdminPointsStud.SetPointsContractDAppId.SendAsync(_pointsContractDAppId);
        result = await AdminPointsStud.Join.SendAsync(new JoinInput
        {
            Domain = "AAA"
        });
        var joinedEvent = result.TransactionResult.Logs.First(o => o.Name == nameof(Joined));
        var joined = Joined.Parser.ParseFrom(joinedEvent.NonIndexed);
        joined.Domain.ShouldBe("AAA");
        joined.Registrant.ShouldBe(AdminAddress);
        
        result = await AdminPointsStud.Join.SendWithExceptionAsync(new JoinInput
        {
            Domain = "AAA"
        });
        result.TransactionResult.Error.ShouldContain("Already joined.");
    }
    
    [Fact]
    public async Task AcceptRefererTest()
    {
        await InitializePointContract();
        await AdminPointsStud.SetPointsContractDAppId.SendAsync(_pointsContractDAppId);
        var result = await AdminPointsStud.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput()
        {
            Referrer = null
        });
        result.TransactionResult.Error.ShouldContain("Invalid referrer.");
        result = await AdminPointsStud.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput()
        {
            Referrer = UserTomAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid referrer.");
        await TomPointsStud.Join.SendAsync(new JoinInput
        {
            Domain = "AAA"
        });
        
        result = await AdminPointsStud.AcceptReferral.SendAsync(new AcceptReferralInput()
        {
            Referrer = UserTomAddress
        });
       
        var acceptedEvent = result.TransactionResult.Logs.First(o => o.Name == nameof(ReferralAccepted));
        var accepted = ReferralAccepted.Parser.ParseFrom(acceptedEvent.NonIndexed);
        accepted.Referrer.ShouldBe(UserTomAddress);
        accepted.Invitee.ShouldBe(AdminAddress);
        
        result = await AdminPointsStud.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput()
        {
            Referrer = UserTomAddress
        });
        result.TransactionResult.Error.ShouldContain("Already joined.");
    }

    [Fact]
    public async Task BatchSettleTest()
    {
        await InitializePointContract();
        await AdminPointsStud.SetPointsContractDAppId.SendAsync(_pointsContractDAppId);
        var result = await AdminPointsStud.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            UserPointsList = {  }
        });
        result.TransactionResult.Error.ShouldContain("Invalid input.");
        result = await TomPointsStud.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            ActionName = "Swap",
            UserPointsList = { new UserPoints()}
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        result = await AdminPointsStud.BatchSettle.SendAsync(new BatchSettleInput
        {
            ActionName = "Swap",
            UserPointsList = { new UserPoints
            {
                UserAddress = UserTomAddress,
                UserPoints_ = 100
            }}
        });
        var joinedEvent = result.TransactionResult.Logs.First(o => o.Name == nameof(Joined));
        var joined = Joined.Parser.ParseFrom(joinedEvent.NonIndexed);
        joined.Domain.ShouldBe("XXX");
        joined.Registrant.ShouldBe(UserTomAddress);
    }
    private async Task InitializePointContract()
    {
        await AdminPointsStud.Initialize.SendAsync(new Points.InitializeInput
        {
            HooksContractAddress = AwakenHooksContractAddress,
            OrderContractAddress = OrderContractAddress,
            OracleContractAddress = MockOracleContractAddress,
            PointsContractAddress = MockPointsContractAddress,
        });
    }
}