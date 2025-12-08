using AElf.Sdk.CSharp.State;
using AElf.Types;
using Awaken.Contracts.Token;
using Google.Protobuf.WellKnownTypes;

namespace Awaken.Contracts.Swap
{
    public partial class AwakenSwapContractState : ContractState
    {
        public BoolState IsInitialized { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public MappedState<Address, string, long> TotalReservesMap { get; set; }

        public MappedState<Address, string, BigIntValue> PriceCumulativeLast { get; set; }

        /// <summary>
        /// Token A -> Token B -> Pair Virtual Address (as the unique id)
        /// </summary>
        public MappedState<string, string, Address> PairVirtualAddressMap { get; set; }

        public SingletonState<StringList> PairList { get; set; }

        /// <summary>
        /// Pair Virtual Address -> Latest Liquidity Adding Timestamp
        /// </summary>
        public MappedState<Address, long> LastBlockTimestampMap { get; set; }

        /// <summary>
        /// Pair Virtual Address -> Latest K
        /// </summary>
        public MappedState<Address, BigIntValue> KValueMap { get; set; }
        

        public SingletonState<long> FeeRate { get; set; }

        public SingletonState<Address> Admin { get; set; }

        public SingletonState<Address> FeeTo { get; set; }

        /// <summary>
        /// Pair Virtual Address -> Token Symbol -> Pool Balance
        /// </summary>
        public MappedState<Address, string, long> PoolBalanceMap { get; set; }
        
        
    }
}