// using AElf.Standards.ACS1;
// using Google.Protobuf.WellKnownTypes;
//
// namespace Awaken.Contracts.Token
// {
//     public partial class TokenContract
//     {
//         public override MethodFees GetMethodFee(StringValue input)
//         {
//             if (input.Value == nameof(Create))
//             {
//                 return new MethodFees
//                 {
//                     MethodName = input.Value,
//                     Fees =
//                     {
//                         new MethodFee
//                         {
//                             Symbol = Context.Variables.NativeSymbol,
//                             BasicFee = 100_00000000
//                         }
//                     }
//                 };
//             }
//
//             return State.TransactionFeesMap[input.Value];
//         }
//
//         public override AuthorityInfo GetMethodFeeController(Empty input)
//         {
//             return State.MethodFeeController.Value;
//         }
//
//         public override Empty SetMethodFee(MethodFees input)
//         {
//             Assert(Context.Sender == State.MethodFeeController.Value.OwnerAddress, "No permission.");
//
//             // Ignore Contract Address.
//
//             foreach (var symbolToAmount in input.Fees)
//             {
//                 ValidTokenExisting(symbolToAmount.Symbol);
//             }
//
//             State.TransactionFeesMap[input.MethodName] = input;
//             return new Empty();
//         }
//
//         public override Empty ChangeMethodFeeController(AuthorityInfo input)
//         {
//             Assert(State.Owner.Value != null, "Owner not set.");
//             AssertSenderIsOwner();
//             State.MethodFeeController.Value = input;
//             return new Empty();
//         }
//     }
// }