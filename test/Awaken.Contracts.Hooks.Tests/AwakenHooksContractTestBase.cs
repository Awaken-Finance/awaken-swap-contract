using System.IO;
using System.Threading.Tasks;
using AElf.Cryptography.ECDSA;
using AElf.Kernel;
using AElf.Kernel.SmartContract.Application;
using AElf.Types;
using Google.Protobuf;
using System.Linq;
using System.Threading;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Parliament;
using AElf.ContractTestBase.ContractTestKit;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Kernel.Blockchain.Application;
using AElf.Kernel.Token;
using AElf.Standards.ACS0;
using AElf.Standards.ACS3;
using Awaken.Contracts.Swap;
using Awaken.Contracts.Token;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Threading;
using CreateInput = AElf.Contracts.MultiToken.CreateInput;
using ExternalInfo = AElf.Contracts.MultiToken.ExternalInfo;
using IssueInput = AElf.Contracts.MultiToken.IssueInput;
using TokenContract = Awaken.Contracts.Token.TokenContract;

namespace Awaken.Contracts.Hooks
{
    public class AwakenHooksContractTestBase : ContractTestBase<AwakenHooksContractTestModule>
    {
        internal readonly Address AwakenHooksContractAddress;
        
        internal readonly Address AwakenSwapContractAddress;

        internal readonly Address LpTokenContractAddress;

        internal readonly IBlockchainService blockChainService;
        internal ACS0Container.ACS0Stub ZeroContractStub { get; set; }
        
        protected Address DefaultAddress => Accounts[0].Address;

        protected int SeedNum = 0;
        protected string SeedNFTSymbolPre = "SEED-";

        private Address tokenContractAddress => GetAddress(TokenSmartContractAddressNameProvider.StringName);

        internal AwakenSwapContractContainer.AwakenSwapContractStub GetAwakenSwapContractStub(ECKeyPair senderKeyPair)
        {
            return GetTester<AwakenSwapContractContainer.AwakenSwapContractStub>(AwakenSwapContractAddress,
                senderKeyPair);
        }


        internal AwakenSwapContractContainer.AwakenSwapContractStub AwakenSwapContractStub =>
            GetAwakenSwapContractStub(SampleAccount.Accounts.First().KeyPair);

        internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractStub TokenContractStub =>
            GetTokenContractStub(SampleAccount.Accounts.First().KeyPair);

        internal TokenContractImplContainer.TokenContractImplStub TokenContractImplStub =>
            GetTokenImplContractStub(SampleAccount.Accounts.First().KeyPair);

        internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractStub GetTokenContractStub(
            ECKeyPair senderKeyPair)
        {
            return Application.ServiceProvider.GetRequiredService<IContractTesterFactory>()
                .Create<AElf.Contracts.MultiToken.TokenContractContainer.TokenContractStub>(tokenContractAddress,
                    senderKeyPair);
        }

        internal AElf.Contracts.MultiToken.TokenContractImplContainer.TokenContractImplStub GetTokenImplContractStub(
            ECKeyPair senderKeyPair)
        {
            return Application.ServiceProvider.GetRequiredService<IContractTesterFactory>()
                .Create<AElf.Contracts.MultiToken.TokenContractImplContainer.TokenContractImplStub>(
                    tokenContractAddress, senderKeyPair);
        }

        internal Awaken.Contracts.Token.TokenContractContainer.TokenContractStub GetLpContractStub(
            ECKeyPair senderKeyPair)
        {
            return Application.ServiceProvider.GetRequiredService<IContractTesterFactory>()
                .Create<Awaken.Contracts.Token.TokenContractContainer.TokenContractStub>(LpTokenContractAddress,
                    senderKeyPair);
        }
        
        internal AwakenHooksContractContainer.AwakenHooksContractStub GetHooksContractStub(
            ECKeyPair senderKeyPair)
        {
            return Application.ServiceProvider.GetRequiredService<IContractTesterFactory>()
                .Create<AwakenHooksContractContainer.AwakenHooksContractStub>(AwakenHooksContractAddress,
                    senderKeyPair);
        }

        // You can get address of any contract via GetAddress method, for example:
        // internal Address DAppContractAddress => GetAddress(DAppSmartContractAddressNameProvider.StringName);
        public AwakenHooksContractTestBase()
        {
            ZeroContractStub = GetContractZeroTester(SampleAccount.Accounts[0].KeyPair);
            var result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
                new ContractDeploymentInput
                {
                    Category = KernelConstants.CodeCoverageRunnerCategory,
                    Code = ByteString.CopyFrom(
                        File.ReadAllBytes(typeof(AwakenSwapContract).Assembly.Location))
                }));
            AwakenSwapContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);

            result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
                new ContractDeploymentInput
                {
                    Category = KernelConstants.CodeCoverageRunnerCategory,
                    Code = ByteString.CopyFrom(
                        File.ReadAllBytes(typeof(TokenContract).Assembly.Location))
                }));
            LpTokenContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
            result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
                new ContractDeploymentInput
                {
                    Category = KernelConstants.CodeCoverageRunnerCategory,
                    Code = ByteString.CopyFrom(
                        File.ReadAllBytes(typeof(AwakenHooksContract).Assembly.Location))
                }));
            AwakenHooksContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
            
            blockChainService = Application.ServiceProvider.GetRequiredService<IBlockchainService>();
            
            AsyncHelper.RunSync(() => CreateSeedNftCollection(TokenContractImplStub));
        }

        private ECKeyPair AdminKeyPair { get; set; } = SampleAccount.Accounts[0].KeyPair;
        private ECKeyPair UserTomKeyPair { get; set; } = SampleAccount.Accounts.Last().KeyPair;
        private ECKeyPair UserLilyKeyPair { get; set; } = SampleAccount.Accounts.Reverse().Skip(1).First().KeyPair;

        internal Address UserTomAddress => Address.FromPublicKey(UserTomKeyPair.PublicKey);
        internal Address UserLilyAddress => Address.FromPublicKey(UserLilyKeyPair.PublicKey);

        internal Address AdminAddress => Address.FromPublicKey(AdminKeyPair.PublicKey);

        internal AwakenSwapContractContainer.AwakenSwapContractStub UserTomStub =>
            GetAwakenSwapContractStub(UserTomKeyPair);

        internal AwakenSwapContractContainer.AwakenSwapContractStub UserLilyStub =>
            GetAwakenSwapContractStub(UserLilyKeyPair);

        internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractStub UserTomTokenContractStub =>
            GetTokenContractStub(UserTomKeyPair);

        internal AElf.Contracts.MultiToken.TokenContractContainer.TokenContractStub UserLilyTokenContractStub =>
            GetTokenContractStub(UserLilyKeyPair);

        internal Awaken.Contracts.Token.TokenContractContainer.TokenContractStub AdminLpStub =>
            GetLpContractStub(AdminKeyPair);

        internal Awaken.Contracts.Token.TokenContractContainer.TokenContractStub TomLpStub =>
            GetLpContractStub(UserTomKeyPair);

        internal Hooks.AwakenHooksContractContainer.AwakenHooksContractStub AdminHooksStud =>
            GetHooksContractStub(AdminKeyPair);
        
        internal Hooks.AwakenHooksContractContainer.AwakenHooksContractStub TomHooksStud =>
            GetHooksContractStub(UserTomKeyPair);
        
        internal Hooks.AwakenHooksContractContainer.AwakenHooksContractStub LilyHooksStud =>
            GetHooksContractStub(UserLilyKeyPair);

        private Address GetAddress(string contractName)
        {
            var addressService = Application.ServiceProvider.GetRequiredService<ISmartContractAddressService>();
            var blockChainService = Application.ServiceProvider.GetRequiredService<IBlockchainService>();
            var chain = AsyncHelper.RunSync(blockChainService.GetChainAsync);
            var address = AsyncHelper.RunSync(() => addressService.GetSmartContractAddressAsync(new ChainContext()
            {
                BlockHash = chain.BestChainHash,
                BlockHeight = chain.BestChainHeight
            }, contractName)).SmartContractAddress.Address;
            return address;
        }

        internal ACS0Container.ACS0Stub GetContractZeroTester(
            ECKeyPair keyPair)
        {
            return GetTester<ACS0Container.ACS0Stub>(BasicContractZeroAddress,
                keyPair);
        }

        internal ParliamentContractImplContainer.ParliamentContractImplStub GetParliamentContractTester(
            ECKeyPair keyPair)
        {
            return GetTester<ParliamentContractImplContainer.ParliamentContractImplStub>(ParliamentContractAddress,
                keyPair);
        }

        internal async Task CreateSeedNftCollection(TokenContractImplContainer.TokenContractImplStub stub)
        {
            var input = new CreateInput
            {
                Symbol = SeedNFTSymbolPre + SeedNum,
                Decimals = 0,
                IsBurnable = true,
                TokenName = "seed Collection",
                TotalSupply = 1,
                Issuer = DefaultAddress,
                Owner = DefaultAddress,
                ExternalInfo = new ExternalInfo()
            };
            await stub.Create.SendAsync(input);
        }


        internal async Task<CreateInput> CreateSeedNftAsync(TokenContractImplContainer.TokenContractImplStub stub,
            CreateInput createInput)
        {
            var input = BuildSeedCreateInput(createInput);
            await stub.Create.SendAsync(input);
            await stub.Issue.SendAsync(new IssueInput
            {
                Symbol = input.Symbol,
                Amount = 1,
                Memo = "ddd",
                To = AdminAddress
            });
            return input;
        }

        internal CreateInput BuildSeedCreateInput(CreateInput createInput)
        {
            Interlocked.Increment(ref SeedNum);
            var input = new CreateInput
            {
                Symbol = SeedNFTSymbolPre + SeedNum,
                Decimals = 0,
                IsBurnable = true,
                TokenName = "seed token" + SeedNum,
                TotalSupply = 1,
                Issuer = DefaultAddress,
                Owner = DefaultAddress,
                ExternalInfo = new ExternalInfo(),
                LockWhiteList = { TokenContractAddress }
            };
            input.ExternalInfo.Value["__seed_owned_symbol"] = createInput.Symbol;
            input.ExternalInfo.Value["__seed_exp_time"] = TimestampHelper.GetUtcNow().AddDays(1).Seconds.ToString();
            return input;
        }

        internal async Task<IExecutionResult<Empty>> CreateMutiTokenAsync(
            TokenContractImplContainer.TokenContractImplStub stub,
            CreateInput createInput)
        {
            await CreateSeedNftAsync(stub, createInput);
            return await stub.Create.SendAsync(createInput);
        }
    }
}