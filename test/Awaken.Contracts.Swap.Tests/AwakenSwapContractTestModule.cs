using System.Collections.Generic;
using System.IO;
using AElf.Boilerplate.TestBase;
using AElf.ContractTestBase;
using AElf.ContractTestBase.ContractTestKit;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Modularity;

namespace Awaken.Contracts.Swap
{
    [DependsOn(typeof(MainChainDAppContractTestModule))]
    public class AwakenSwapContractTestModule : MainChainDAppContractTestModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<ContractOptions>(o=>o.ContractDeploymentAuthorityRequired=false); 
            context.Services.AddSingleton<IBlockTimeProvider, BlockTimeProvider>();
            
        }
    }
}