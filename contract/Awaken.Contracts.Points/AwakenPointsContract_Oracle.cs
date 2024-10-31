using System.Linq;
using AElf;
using AElf.Sdk.CSharp;
using AetherLink.Contracts.Consumer;
using AetherLink.Contracts.Oracle;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract
{
    public override Empty StartOracleRequest(StartOracleRequestInput input)
    {
        CheckAdminPermission();
        State.OracleContract.SendRequest.Send(new SendRequestInput
        {
            SubscriptionId = State.SubscriptionId.Value,
            RequestTypeIndex = PriceDataFeedsRequestTypeIndex,
            SpecificData = input.SpecificData,
            TraceId = input.TraceId
        });
        return new Empty();
    }

    public override Empty HandleOracleFulfillment(HandleOracleFulfillmentInput input)
    {
        Assert(Context.Sender == State.OracleContract.Value, "No permission.");
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input.RequestId), "Invalid input request id.");
        Assert(input.RequestTypeIndex > 0, "Invalid request type index.");
        Assert(!input.Response.IsNullOrEmpty() || !input.Err.IsNullOrEmpty(), "Invalid input response or err.");

        if (input.RequestTypeIndex == PriceDataFeedsRequestTypeIndex)
        {
            FulfillDataFeedsRequest(input);
        }
        else
        {
            Assert(false, "Invalid request type index.");
        }
        return new Empty();
}

    private void FulfillDataFeedsRequest(HandleOracleFulfillmentInput input)
    {
        if (input.Response.IsNullOrEmpty()) return;

        var priceList = LongList.Parser.ParseFrom(input.Response);
        if (priceList.Data.Count < 1)
        {
            return;
        }
        var longList = new LongList
        {
            Data = {priceList.Data}
        };

        var sortedList = longList.Data.ToList().OrderBy(l => l).ToList();

        var from = State.PriceMap[input.TraceId];
        var newPrice = sortedList[sortedList.Count / 2];
        State.PriceMap[input.TraceId] = newPrice;
        Context.Fire(new PriceUpdated()
        {
            From = from,
            To = newPrice,
            TraceId = input.TraceId,
            UpdateAt = Context.CurrentBlockTime
        });
    }
}