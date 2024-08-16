using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Token
{
    public partial class TokenContract
    {
        public override WhiteList GetWhiteList(Empty input)
        {
            return State.WhiteList.Value;
        }

        public override Address GetAdmin(Empty input)
        {
            return State.Admin.Value;
        }

        public override Balance GetBalance(GetBalanceInput input)
        {
            var owner = input.Owner ?? Context.Sender;
            return new Balance
            {
                Amount = State.BalanceMap[owner][input.Symbol],
                Owner = owner,
                Symbol = input.Symbol
            };
        }

        public override Balances GetBalances(GetBalancesInput input)
        {
            var owner = input.Owner ?? Context.Sender;
            var balances = new Balances();
            foreach (var symbol in input.Symbols)
            {
                balances.Value.Add(GetBalance(new GetBalanceInput
                {
                    Symbol = symbol,
                    Owner = owner
                }));
            }

            return balances;
        }

        public override Allowance GetAllowance(GetAllowanceInput input)
        {
            return new Allowance
            {
                Amount = State.AllowanceMap[input.Owner][input.Spender][input.Symbol],
                Owner = input.Owner,
                Spender = input.Spender,
                Symbol = input.Symbol
            };
        }

        public override TokenInfo GetTokenInfo(GetTokenInfoInput input)
        {
            return State.TokenInfoMap[input.Symbol];
        }

        public override Address GetOwner(Empty input)
        {
            return State.Owner.Value;
        }
    }
}