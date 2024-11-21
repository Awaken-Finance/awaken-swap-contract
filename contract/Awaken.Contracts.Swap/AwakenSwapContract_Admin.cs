using System.Linq;
using AElf;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContract
    {
        public override Empty SetFeeRate(Int64Value input)
        {
            AssertSenderIsAdmin();
            Assert(input != null && input.Value > 0 && input.Value <= FeeRateMax,"Invalid input.");
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
            Assert(input.Value.Any() && !input.Value.IsNullOrEmpty(),"Invalid input.");
            State.Admin.Value = input;
            return new Empty();
        }
    }
}