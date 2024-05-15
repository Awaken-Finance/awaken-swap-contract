using System.Collections.Generic;
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
using Awaken.Contracts.Token;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Threading;
using CreateInput = AElf.Contracts.MultiToken.CreateInput;
using ExternalInfo = AElf.Contracts.MultiToken.ExternalInfo;
using IssueInput = AElf.Contracts.MultiToken.IssueInput;

namespace Awaken.Contracts.Swap
{
    public class AwakenSwapContractTestBase : ContractTestBase<AwakenSwapContractTestModule>
    {
        internal readonly Address AwakenSwapContractAddress;

        internal readonly Address LpTokentContractAddress;

        internal readonly IBlockchainService blockChainService;
        internal ACS0Container.ACS0Stub ZeroContractStub { get; set; }

        internal ParliamentContractImplContainer.ParliamentContractImplStub ParliamentContractStub =>
            GetParliamentContractTester(SampleAccount.Accounts.First().KeyPair);

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
                .Create<Awaken.Contracts.Token.TokenContractContainer.TokenContractStub>(LpTokentContractAddress,
                    senderKeyPair);
        }

        // You can get address of any contract via GetAddress method, for example:
        // internal Address DAppContractAddress => GetAddress(DAppSmartContractAddressNameProvider.StringName);
        public AwakenSwapContractTestBase()
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
                        File.ReadAllBytes(typeof(Token.TokenContract).Assembly.Location))
                }));
            LpTokentContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
            blockChainService = Application.ServiceProvider.GetRequiredService<IBlockchainService>();

            // AsyncHelper.RunSync(() => SubmitAndApproveProposalOfDefaultParliament(TokenContractAddress,
            //     nameof(TokenContractStub.Create), new CreateInput()
            //     {
            //         Symbol = "ELF",
            //         Decimals = 8,
            //         IsBurnable = true,
            //         TokenName = "ELF2",
            //         TotalSupply = 100_000_000_000_000_000L,
            //         Issuer = DefaultAddress,
            //         ExternalInfo = new ExternalInfo(),
            //         Owner = DefaultAddress
            //     }));

            AsyncHelper.RunSync(() => CreateSeedNftCollection(TokenContractImplStub));
        }

        private async Task<Address> DeployContractAsync(int category, byte[] code, ECKeyPair keyPair)
        {
            var addressService = Application.ServiceProvider.GetRequiredService<ISmartContractAddressService>();
            var stub = GetTester<ACS0Container.ACS0Stub>(addressService.GetZeroSmartContractAddress(),
                keyPair);
            var executionResult = await stub.DeploySmartContract.SendAsync(new ContractDeploymentInput
            {
                Category = category,
                Code = ByteString.CopyFrom(code)
            });
            return executionResult.Output;
        }

        private ECKeyPair AdminKeyPair { get; set; } = SampleAccount.Accounts[0].KeyPair;
        private ECKeyPair UserTomKeyPair { get; set; } = SampleAccount.Accounts.Last().KeyPair;
        private ECKeyPair UserLilyKeyPair { get; set; } = SampleAccount.Accounts.Reverse().Skip(1).First().KeyPair;

        internal Address UserTomAddress => Address.FromPublicKey(UserTomKeyPair.PublicKey);
        internal Address UserLilyAddress => Address.FromPublicKey(UserLilyKeyPair.PublicKey);

        internal Address AdminAddress => Address.FromPublicKey(AdminKeyPair.PublicKey);

        protected List<ECKeyPair> InitialCoreDataCenterKeyPairs =>
            Accounts.Take(InitialCoreDataCenterCount).Select(a => a.KeyPair).ToList();

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

        private async Task SubmitAndApproveProposalOfDefaultParliament(Address contractAddress, string methodName,
            IMessage message)
        {
            var defaultParliamentAddress =
                await ParliamentContractStub.GetDefaultOrganizationAddress.CallAsync(new Empty());
            var proposalId = await CreateProposalAsync(TokenContractAddress,
                defaultParliamentAddress, methodName, message);
            await ApproveWithMinersAsync(proposalId);
            var releaseResult = await ParliamentContractStub.Release.SendAsync(proposalId);
        }

        private async Task<Hash> CreateProposalAsync(Address contractAddress, Address organizationAddress,
            string methodName, IMessage input)
        {
            var proposal = new CreateProposalInput
            {
                OrganizationAddress = organizationAddress,
                ContractMethodName = methodName,
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1),
                Params = input.ToByteString(),
                ToAddress = contractAddress
            };

            var createResult = await ParliamentContractStub.CreateProposal.SendAsync(proposal);
            var proposalId = createResult.Output;

            return proposalId;
        }

        private async Task ApproveWithMinersAsync(Hash proposalId)
        {
            var tester = GetParliamentContractTester(AdminKeyPair);
            var approveResult = await tester.Approve.SendAsync(proposalId);
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