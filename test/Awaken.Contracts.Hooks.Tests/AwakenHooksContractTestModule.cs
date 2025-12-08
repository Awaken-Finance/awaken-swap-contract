using AElf.Boilerplate.TestBase;
using AElf.ContractTestBase.ContractTestKit;
using AElf.Kernel.SmartContract;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Awaken.Contracts.Hooks
{
    [DependsOn(typeof(MainChainDAppContractTestModule))]
    public class AwakenHooksContractTestModule : MainChainDAppContractTestModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<ContractOptions>(o=>o.ContractDeploymentAuthorityRequired=false); 
            context.Services.AddSingleton<IBlockTimeProvider, BlockTimeProvider>();
            
        }
    }
}