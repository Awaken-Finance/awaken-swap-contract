using AElf.Standards.ACS2;
using AElf.Types;

namespace Awaken.Contracts.Token
{
    public partial class TokenContract
    {
        public override ResourceInfo GetResourceInfo(Transaction txn)
        {
            switch (txn.MethodName)
            {
                case nameof(Transfer):
                {
                    var args = TransferInput.Parser.ParseFrom(txn.Params);
                    var resourceInfo = new ResourceInfo
                    {
                        WritePaths =
                        {
                            GetPath(nameof(TokenContractState.BalanceMap), txn.From.ToString(), args.Symbol),
                            GetPath(nameof(TokenContractState.BalanceMap), args.To.ToString(), args.Symbol),
                        },
                        ReadPaths =
                        {
                            GetPath(nameof(TokenContractState.TokenInfoMap), args.Symbol),
                        }
                    };

                    AddPathForTransactionFee(resourceInfo, txn.From);
                    return resourceInfo;
                }

                case nameof(TransferFrom):
                {
                    var args = TransferFromInput.Parser.ParseFrom(txn.Params);
                    var resourceInfo = new ResourceInfo
                    {
                        WritePaths =
                        {
                            GetPath(nameof(TokenContractState.AllowanceMap), args.From.ToString(), txn.From.ToString(),
                                args.Symbol),
                            GetPath(nameof(TokenContractState.BalanceMap), args.From.ToString(), args.Symbol),
                            GetPath(nameof(TokenContractState.BalanceMap), args.To.ToString(), args.Symbol),
                        },
                        ReadPaths =
                        {
                            GetPath(nameof(TokenContractState.TokenInfoMap), args.Symbol),
                        }
                    };
                    AddPathForTransactionFee(resourceInfo, txn.From);
                    return resourceInfo;
                }

                default:
                    return new ResourceInfo {NonParallelizable = true};
            }
        }

        private void AddPathForTransactionFee(ResourceInfo resourceInfo, Address from)
        {
            var path = GetPath(nameof(TokenContractState.BalanceMap), from.ToString(), Context.Variables.NativeSymbol);
            if (!resourceInfo.WritePaths.Contains(path))
            {
                resourceInfo.WritePaths.Add(path);
            }
        }

        private ScopedStatePath GetPath(params string[] parts)
        {
            return new ScopedStatePath
            {
                Address = Context.Self,
                Path = new StatePath
                {
                    Parts =
                    {
                        parts
                    }
                }
            };
        }
    }
}