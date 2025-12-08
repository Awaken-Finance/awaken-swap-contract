using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;

namespace Awaken.Contracts.Token
{
    public partial class TokenContract
    {
        private void ModifyBalance(Address address, string symbol, long addAmount)
        {
            var before = GetBalance(address, symbol);
            if (addAmount < 0 && before < -addAmount)
            {
                Assert(false,
                    $"Insufficient balance of {symbol}. Need balance: {-addAmount}; Current balance: {before}");
            }

            var target = before.Add(addAmount);
            State.BalanceMap[address][symbol] = target;
        }

        private TokenInfo ValidTokenExisting(string symbol)
        {
            var tokenInfo = State.TokenInfoMap[symbol];
            if (tokenInfo == null)
            {
                throw new AssertionException($"Token {symbol} not found.");
            }

            return tokenInfo;
        }

        private long GetBalance(Address address, string symbol)
        {
            return State.BalanceMap[address][symbol];
        }

        private void DoTransfer(Address from, Address to, string symbol, long amount, string memo = null)
        {
            Assert(from != to, "Can't do transfer to sender itself.");
            ModifyBalance(from, symbol, -amount);
            ModifyBalance(to, symbol, amount);
            Context.Fire(new Transferred
            {
                From = from,
                To = to,
                Symbol = symbol,
                Amount = amount,
                Memo = memo ?? string.Empty
            });
        }

        private void DealWithExternalInfoDuringTransfer(TransferFromInput input)
        {
            var tokenInfo = State.TokenInfoMap[input.Symbol];
            if (tokenInfo.ExternalInfo == null) return;
            if (tokenInfo.ExternalInfo.Value.ContainsKey(TransferCallbackExternalInfoKey))
            {
                var callbackInfo =
                    JsonParser.Default.Parse<CallbackInfo>(
                        tokenInfo.ExternalInfo.Value[TransferCallbackExternalInfoKey]);
                Context.SendInline(callbackInfo.ContractAddress, callbackInfo.MethodName, input);
            }

            FireExternalLogEvent(tokenInfo, input);
        }

        private void FireExternalLogEvent(TokenInfo tokenInfo, TransferFromInput input)
        {
            if (tokenInfo.ExternalInfo.Value.ContainsKey(LogEventExternalInfoKey))
            {
                Context.FireLogEvent(new LogEvent
                {
                    Name = tokenInfo.ExternalInfo.Value[LogEventExternalInfoKey],
                    Address = Context.Self,
                    NonIndexed = input.ToByteString()
                });
            }
        }

        private void AssertSenderIsOwner()
        {
            Assert(Context.Sender == State.Owner.Value, "No permission.");
        }
        
        
        private void AssertSenderIsMinter()
        {
            Assert( State.MinterMap[Context.Sender], "No permission.");
        }
        
        private void AssertSenderIsIssuer(Address issuer)
        {
            Assert( Context.Sender == issuer , "No permission.");
        }
    }
}