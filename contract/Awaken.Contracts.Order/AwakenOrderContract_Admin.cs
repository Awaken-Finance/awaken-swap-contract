using AElf;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    public override Empty Initialize(InitializeInput input)
    {
        Assert(!State.Initialized.Value, "Already initialized.");
        State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
        var author = State.GenesisContract.GetContractAuthor.Call(Context.Self);
        Assert(Context.Sender == author, "No permission.");
        Assert(!input.HooksContractAddress.Value.IsNullOrEmpty(), "Invalid hooks contract address.");
        State.HooksContract.Value = input.HooksContractAddress;
        State.FillOrderWhiteList.Value = new WhiteList
        {
            Value = { input.HooksContractAddress }
        };
        State.TokenContract.Value =
            Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
        State.Admin.Value = input.Admin ?? Context.Sender;
        InitOrderBookConfig();
        State.CheckCommitPriceEnabled.Value = input.CheckCommitPriceEnabled;
        State.CommitPriceIncreaseRate.Value = input.CommitPriceIncreaseRate;
        State.Initialized.Value = true;
        return new Empty();
    }
    
    private void InitOrderBookConfig()
    {
        State.OrderBookConfig.Value = new OrderBookConfig
        {
            MaxOrdersEachOrderBook = MaxOrdersEachOrderBook,
            MaxPricesEachPriceBook = MaxPricesEachPriceBook,
            MaxFillOrderCount = MaxFillOrderCount,
            PriceMultiple = PriceMultiple,
            ErasePriceMultiple = ErasePriceMultiple,
            MaxOrderBooksEachPrice =MaxOrderBooksEachPrice,
            MaxPriceBooksEachTradePair = MaxPriceBooksEachTradePair,
            UserPendingOrdersLimit = UserPendingOrdersLimit
        };
    }
    
    public override Empty SetOrderBookConfig(SetOrderBookConfigInput input)
    {
        Assert(input.OrderBookConfig != null, "Invalid input.");
        AssertSenderIsAdmin();
        var orderBookConfig = State.OrderBookConfig.Value;
        if (input.OrderBookConfig.MaxOrdersEachOrderBook > 0)
        {
            orderBookConfig.MaxOrdersEachOrderBook = input.OrderBookConfig.MaxOrdersEachOrderBook;
        }
        if (input.OrderBookConfig.MaxOrderBooksEachPrice > 0)
        {
            orderBookConfig.MaxOrderBooksEachPrice = input.OrderBookConfig.MaxOrderBooksEachPrice;
        }
        if (input.OrderBookConfig.MaxPricesEachPriceBook > 0)
        {
            orderBookConfig.MaxPricesEachPriceBook = input.OrderBookConfig.MaxPricesEachPriceBook;
        }
        if (input.OrderBookConfig.MaxPriceBooksEachTradePair > 0)
        {
            orderBookConfig.MaxPriceBooksEachTradePair = input.OrderBookConfig.MaxPriceBooksEachTradePair;
        }
        if (input.OrderBookConfig.MaxFillOrderCount > 0)
        {
            orderBookConfig.MaxFillOrderCount = input.OrderBookConfig.MaxFillOrderCount;
        }
        if (input.OrderBookConfig.PriceMultiple > 0)
        {
            orderBookConfig.PriceMultiple = input.OrderBookConfig.PriceMultiple;
        }
        if (input.OrderBookConfig.ErasePriceMultiple > 0)
        {
            orderBookConfig.ErasePriceMultiple = input.OrderBookConfig.ErasePriceMultiple;
        }
        if (input.OrderBookConfig.UserPendingOrdersLimit > 0)
        {
            orderBookConfig.UserPendingOrdersLimit = input.OrderBookConfig.UserPendingOrdersLimit;
        }
        return new Empty();
    }

    public override Empty SetCommitPriceConfig(SetCommitPriceConfigInput input)
    {
        AssertSenderIsAdmin();
        State.CheckCommitPriceEnabled.Value = input.CheckCommitPriceEnabled;
        State.CommitPriceIncreaseRate.Value = input.CommitPriceIncreaseRate;
        return new Empty();
    }

    public override Empty SetAdmin(Address input)
    {
        Assert(!input.Value.IsNullOrEmpty(), "Invalid input.");
        AssertSenderIsAdmin();
        State.Admin.Value = input;
        return new Empty();
    }

    public override Empty SetHooksContract(Address input)
    {
        Assert(!input.Value.IsNullOrEmpty(), "Invalid input.");
        AssertSenderIsAdmin();
        State.HooksContract.Value = input;
        return new Empty();
    }

    public override Address GetAdmin(Empty input)
    {
        return State.Admin.Value;
    }

    public override Address GetHooksContract(Empty input)
    {
        return State.HooksContract.Value;
    }

    public override GetOrderBookConfigOutput GetOrderBookConfig(Empty input)
    {
        return new GetOrderBookConfigOutput
        {
            OrderBookConfig = State.OrderBookConfig.Value
        };
    }

    public override Empty AddFillOrderWhiteList(Address input)
    {
        Assert(input != null && !input.Value.IsNullOrEmpty(), "Invalid input.");
        AssertSenderIsAdmin();
        State.FillOrderWhiteList.Value ??= new WhiteList();
        Assert(!State.FillOrderWhiteList.Value.Value.Contains(input), "Address is exist");

        State.FillOrderWhiteList.Value.Value.Add(input);

        return new Empty();
    }

    public override Empty RemoveFillOrderWhiteList(Address input)
    {
        Assert(input != null && !input.Value.IsNullOrEmpty(), "Invalid input.");
        AssertSenderIsAdmin();
        Assert(State.FillOrderWhiteList.Value != null && State.FillOrderWhiteList.Value.Value.Contains(input), "Address not exist");
        State.FillOrderWhiteList.Value?.Value.Remove(input);
        return new Empty();
    }

    public override WhiteList GetFillOrderWhiteList(Empty input)
    {
        return State.FillOrderWhiteList.Value;
    }

    private void AssertContractInitialized()
    {
        Assert(State.Admin.Value != null, "Contract not initialized.");
    }

    private void AssertSenderIsAdmin()
    {
        AssertContractInitialized();
        Assert(Context.Sender == State.Admin.Value, "No permission.");
    }
}