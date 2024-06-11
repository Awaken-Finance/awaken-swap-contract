using System;
using AElf;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Token
{
    public partial class TokenContract
    {
        public override Empty Create(CreateInput input)
        {
            AssertSenderIsMinter();
            Assert(!string.IsNullOrWhiteSpace(input.Symbol), "Invalid symbol.");
            Assert(!string.IsNullOrWhiteSpace(input.TokenName), "Invalid token name.");
            Assert(input.TotalSupply > 0, "Invalid total supply.");
            Assert(State.TokenInfoMap[input.Symbol] == null, $"Token {input.Symbol} already exists.");

            var issuer = input.Issuer ?? Context.Sender;
            var tokenInfo = new TokenInfo
            {
                Symbol = input.Symbol,
                TokenName = input.TokenName,
                Decimals = input.Decimals,
                IsBurnable = input.IsBurnable,
                Issuer = issuer,
                TotalSupply = input.TotalSupply,
                ExternalInfo = input.ExternalInfo
            };
            State.TokenInfoMap[input.Symbol] = tokenInfo;

            Context.Fire(new TokenCreated
            {
                Symbol = tokenInfo.Symbol,
                TokenName = tokenInfo.TokenName,
                Decimals = tokenInfo.Decimals,
                IsBurnable = tokenInfo.IsBurnable,
                Issuer = tokenInfo.Issuer,
                TotalSupply = tokenInfo.TotalSupply,
                ExternalInfo = tokenInfo.ExternalInfo,
            });
            return new Empty();
        }

        public override Empty AddWhiteList(Address input)
        {
            Assert(input != null && !input.Value.IsNullOrEmpty(), "Invalid input.");
            ValidPermission();
            State.WhiteList.Value ??= new WhiteList();
            Assert(!State.WhiteList.Value.Value.Contains(input), "Address is exist");

            State.WhiteList.Value.Value.Add(input);

            return new Empty();
        }

        public override Empty RemoveWhiteList(Address input)
        {
            Assert(input != null && !input.Value.IsNullOrEmpty(), "Invalid input.");
            ValidPermission();
            Assert(State.WhiteList.Value != null && State.WhiteList.Value.Value.Contains(input), "Address not exist");
            State.WhiteList.Value?.Value.Remove(input);
            return new Empty();
        }

        private void ValidPermission()
        {
            if (State.Admin.Value == null)
            {
                Assert(State.GenesisContract.GetContractAuthor.Call(Context.Self) == Context.Sender,
                    "No permission to add address.");
            }
            else
            {
                Assert(State.Admin.Value == Context.Sender, "No permission to add address.");
            }
        }

        public override Empty SetAdmin(Address input)
        {
            Assert(input != null && !input.Value.IsNullOrEmpty(), "Invalid input.");
            ValidPermission();
            State.Admin.Value = input;
            return new Empty();
        }

        public override Empty Issue(IssueInput input)
        {
            if (input.Amount <= 0) return new Empty();
            Assert(input.To != null, "To address not filled.");
            var tokenInfo = ValidTokenExisting(input.Symbol);
            AssertSenderIsIssuer(tokenInfo.Issuer);
            // tokenInfo.Issued = tokenInfo.Issued.Add(input.Amount);
            tokenInfo.Supply = tokenInfo.Supply.Add(input.Amount);

            // Assert(tokenInfo.Issued <= tokenInfo.TotalSupply, "Total supply exceeded.");
 
            State.TokenInfoMap[input.Symbol] = tokenInfo;
            ModifyBalance(input.To, input.Symbol, input.Amount);
            Context.Fire(new Issued
            {
                Symbol = input.Symbol,
                Amount = input.Amount,
                To = input.To,
                Memo = input.Memo
            });
            return new Empty();
        }


        public override Empty Transfer(TransferInput input)
        {
            if (input.Amount <= 0) return new Empty();
            ValidTokenExisting(input.Symbol);
            DoTransfer(Context.Sender, input.To, input.Symbol, input.Amount, input.Memo);
            DealWithExternalInfoDuringTransfer(new TransferFromInput
            {
                From = Context.Sender,
                To = input.To,
                Amount = input.Amount,
                Symbol = input.Symbol,
                Memo = input.Memo
            });
            return new Empty();
        }

        public override Empty TransferFrom(TransferFromInput input)
        {
            if (State.WhiteList.Value != null && State.WhiteList.Value.Value.Count > 0)
            {
                Assert(State.WhiteList.Value.Value.Contains(Context.Sender), "Sender is not in white list");
            }

            if (input.Amount <= 0) return new Empty();
            ValidTokenExisting(input.Symbol);
            var allowance = State.AllowanceMap[input.From][Context.Sender][input.Symbol];
            if (allowance < input.Amount)
            {
                Assert(false,
                    $"Insufficient allowance. Token: {input.Symbol}; {allowance}/{input.Amount}.\n" +
                    $"From:{input.From}\tSpender:{Context.Sender}\tTo:{input.To}");
            }

            DoTransfer(input.From, input.To, input.Symbol, input.Amount, input.Memo);
            DealWithExternalInfoDuringTransfer(input);
            State.AllowanceMap[input.From][Context.Sender][input.Symbol] = allowance.Sub(input.Amount);
            return new Empty();
        }

        public override Empty Approve(ApproveInput input)
        {
            if (input.Amount <= 0) return new Empty();
            ValidTokenExisting(input.Symbol);
            State.AllowanceMap[Context.Sender][input.Spender][input.Symbol] =
                State.AllowanceMap[Context.Sender][input.Spender][input.Symbol].Add(input.Amount);
            Context.Fire(new Approved
            {
                Owner = Context.Sender,
                Spender = input.Spender,
                Symbol = input.Symbol,
                Amount = input.Amount
            });
            return new Empty();
        }

        public override Empty UnApprove(UnApproveInput input)
        {
            if (input.Amount <= 0) return new Empty();
            ValidTokenExisting(input.Symbol);
            var oldAllowance = State.AllowanceMap[Context.Sender][input.Spender][input.Symbol];
            var amountOrAll = Math.Min(input.Amount, oldAllowance);
            State.AllowanceMap[Context.Sender][input.Spender][input.Symbol] = oldAllowance.Sub(amountOrAll);
            Context.Fire(new UnApproved
            {
                Owner = Context.Sender,
                Spender = input.Spender,
                Symbol = input.Symbol,
                Amount = amountOrAll
            });
            return new Empty();
        }

        public override Empty Burn(BurnInput input)
        {
            if (input.Amount <= 0) return new Empty();
            var tokenInfo = ValidTokenExisting(input.Symbol);
            Assert(tokenInfo.IsBurnable, "The token is not burnable.");
            ModifyBalance(Context.Sender, input.Symbol, -input.Amount);
            tokenInfo.Supply = tokenInfo.Supply.Sub(input.Amount);
            Context.Fire(new Burned
            {
                Burner = Context.Sender,
                Symbol = input.Symbol,
                Amount = input.Amount
            });
            return new Empty();
        }

        public override Empty ResetExternalInfo(ResetExternalInfoInput input)
        {
            var tokenInfo = State.TokenInfoMap[input.Symbol];
            Assert(tokenInfo.Issuer == Context.Sender, "No permission to reset external info.");
            tokenInfo.ExternalInfo = input.ExternalInfo;
            State.TokenInfoMap[input.Symbol] = tokenInfo;
            Context.Fire(new ExternalInfoChanged
            {
                Symbol = input.Symbol,
                ExternalInfo = input.ExternalInfo
            });
            return new Empty();
        }

        public override Empty AddMinter(Address input)
        {
            AssertSenderIsOwner();
            State.MinterMap[input] = true;
            return new Empty();
        }

        public override Empty RemoveMinter(Address input)
        {
            AssertSenderIsOwner();
            State.MinterMap[input] = false;
            return new Empty();
        }
    }
}