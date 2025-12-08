using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract
    {
        public override Empty SetFeeRate(Int64Value input)
        {
            AssertSenderIsAdmin();
            State.FeeRate.Value = input.Value;
            return new Empty();
        }
        

        public override Empty SetFeeTo(Address input)
        {
            AssertSenderIsAdmin();
            State.FeeTo.Value = input;
            return new Empty();
        }

        public override Empty ChangeOwner(Address input)
        {
            AssertSenderIsAdmin();
            State.Admin.Value = input;
            return new Empty();
        }
    }
}