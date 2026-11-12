# Project Dependency Graph

> Generated: 2026-11-12 23:03:13 UTC

## Summary

| Metric | Value |
|--------|-------|
| Total Projects | 61 |
| Foundation Projects | 11 |
| Runtime Projects | 26 |
| UI/Host Projects | 5 |
| Test Projects | 16 |
| Build/Benchmark Projects | 3 |
| Root Projects | 17 |
| Leaf Projects | 9 |
| Circular Dependencies | 0 |

> This file is auto-generated. Do not edit manually.

## Project Family Overview

```mermaid
flowchart LR
    BuildTools["Build Tools<br/><br/>DocGenerator<br/>FSharpInteropGenerator"]
    Foundation["Foundation<br/><br/>Meridian.Contracts<br/>Meridian.Core<br/>Meridian.Domain<br/>Meridian.Execution.Sdk<br/>Meridian.FSharp<br/>Meridian.FSharp.DirectLending.Aggregates<br/>Meridian.FSharp.Ledger<br/>Meridian.FSharp.Trading<br/>Meridian.IbApi.SmokeStub<br/>Meridian.Ledger<br/>Meridian.ProviderSdk"]
    Runtime["Runtime<br/><br/>Meridian.Application<br/>Meridian.Audit<br/>Meridian.Backtesting<br/>Meridian.Backtesting.Sdk<br/>Meridian.DataIntegration<br/>Meridian.Documents<br/>Meridian.Entities<br/>Meridian.Execution<br/>Meridian.FinancialOperations<br/>Meridian.Identity<br/>Meridian.Infrastructure<br/>Meridian.Instruments<br/>Meridian.Launcher<br/>Meridian.LifecycleSupervisor<br/>Meridian.Platform<br/>Meridian.PortfolioRecords<br/>Meridian.ProcessIsolation<br/>Meridian.QuantScript<br/>Meridian.QuantScript.Worker<br/>Meridian.ReferenceData<br/>Meridian.Reporting<br/>Meridian.Risk<br/>Meridian.Setup<br/>Meridian.Storage<br/>Meridian.Strategies<br/>Meridian.Workflow"]
    UIShared["UI Shared<br/><br/>Meridian.Ui.Services<br/>Meridian.Ui.Shared"]
    Hosts["Hosts<br/><br/>Meridian<br/>Meridian.Mcp<br/>Meridian.Wpf"]
    Benchmarks["Benchmarks<br/><br/>Meridian.Benchmarks"]
    Tests["Tests<br/><br/>Meridian.Backtesting.Tests<br/>Meridian.Contracts.UnitTests<br/>Meridian.Core.UnitTests<br/>Meridian.DesignModules.Tests<br/>Meridian.DirectLending.Tests<br/>Meridian.FSharp.Tests<br/>Meridian.FundStructure.Tests<br/>Meridian.Lifecycle.Tests<br/>Meridian.LifecycleSupervisor.Tests<br/>Meridian.ProcessTestHelper<br/>Meridian.QuantScript.Tests<br/>Meridian.Setup.Tests<br/>Meridian.TestSupport<br/>Meridian.Tests<br/>Meridian.Ui.Tests<br/>Meridian.Wpf.Tests"]
    BuildTools --> Foundation
    Foundation --> Runtime
    Runtime --> UIShared
    UIShared --> Hosts
    Foundation --> Hosts
    Runtime --> Hosts
    Foundation --> Tests
    Runtime --> Tests
    UIShared --> Tests
    Hosts --> Tests
    Runtime --> Benchmarks
    Hosts --> Benchmarks
```

## Runtime Project Graph

Shows the current source/runtime dependency shape without tests, benchmarks, or build tooling noise.

```mermaid
graph LR
    Meridian[Meridian]
    Meridian_Application[Meridian.Application]
    Meridian_Audit[Meridian.Audit]
    Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Contracts[Meridian.Contracts]
    Meridian_Core[Meridian.Core]
    Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Documents[Meridian.Documents]
    Meridian_Domain[Meridian.Domain]
    Meridian_Entities[Meridian.Entities]
    Meridian_Execution[Meridian.Execution]
    Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FSharp[Meridian.FSharp]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates]
    Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_FSharp_Trading[Meridian.FSharp.Trading]
    Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_IbApi_SmokeStub[Meridian.IbApi.SmokeStub]
    Meridian_Identity[Meridian.Identity]
    Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Instruments[Meridian.Instruments]
    Meridian_Launcher[Meridian.Launcher]
    Meridian_Ledger[Meridian.Ledger]
    Meridian_LifecycleSupervisor[Meridian.LifecycleSupervisor]
    Meridian_Mcp[Meridian.Mcp]
    Meridian_Platform[Meridian.Platform]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_ProcessIsolation[Meridian.ProcessIsolation]
    Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_QuantScript[Meridian.QuantScript]
    Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Reporting[Meridian.Reporting]
    Meridian_Risk[Meridian.Risk]
    Meridian_Setup[Meridian.Setup]
    Meridian_Storage[Meridian.Storage]
    Meridian_Strategies[Meridian.Strategies]
    Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Workflow[Meridian.Workflow]
    Meridian_Wpf[Meridian.Wpf]
    Meridian[Meridian] --> Meridian_Application[Meridian.Application]
    Meridian[Meridian] --> Meridian_Audit[Meridian.Audit]
    Meridian[Meridian] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian[Meridian] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian[Meridian] --> Meridian_Storage[Meridian.Storage]
    Meridian[Meridian] --> Meridian_Core[Meridian.Core]
    Meridian[Meridian] --> Meridian_Domain[Meridian.Domain]
    Meridian[Meridian] --> Meridian_Contracts[Meridian.Contracts]
    Meridian[Meridian] --> Meridian_Identity[Meridian.Identity]
    Meridian[Meridian] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian[Meridian] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian[Meridian] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian[Meridian] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian[Meridian] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Application[Meridian.Application] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Application[Meridian.Application] --> Meridian_Storage[Meridian.Storage]
    Meridian_Application[Meridian.Application] --> Meridian_Core[Meridian.Core]
    Meridian_Application[Meridian.Application] --> Meridian_Domain[Meridian.Domain]
    Meridian_Application[Meridian.Application] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Application[Meridian.Application] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_Application[Meridian.Application] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Application[Meridian.Application] --> Meridian_Entities[Meridian.Entities]
    Meridian_Application[Meridian.Application] --> Meridian_Identity[Meridian.Identity]
    Meridian_Application[Meridian.Application] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_Application[Meridian.Application] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Application[Meridian.Application] --> Meridian_Platform[Meridian.Platform]
    Meridian_Application[Meridian.Application] --> Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_Application[Meridian.Application] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Application[Meridian.Application] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Application[Meridian.Application] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Audit[Meridian.Audit] --> Meridian_Identity[Meridian.Identity]
    Meridian_Audit[Meridian.Audit] --> Meridian_Storage[Meridian.Storage]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Storage[Meridian.Storage]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Core[Meridian.Core] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Core[Meridian.Core] --> Meridian_Domain[Meridian.Domain]
    Meridian_Core[Meridian.Core] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Core[Meridian.Core]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Domain[Meridian.Domain]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Storage[Meridian.Storage]
    Meridian_Documents[Meridian.Documents] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Documents[Meridian.Documents] --> Meridian_Core[Meridian.Core]
    Meridian_Documents[Meridian.Documents] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Documents[Meridian.Documents] --> Meridian_Storage[Meridian.Storage]
    Meridian_Domain[Meridian.Domain] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Domain[Meridian.Domain] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Entities[Meridian.Entities] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Execution[Meridian.Execution] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Execution[Meridian.Execution] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Execution[Meridian.Execution] --> Meridian_Core[Meridian.Core]
    Meridian_Execution[Meridian.Execution] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Execution[Meridian.Execution] --> Meridian_Storage[Meridian.Storage]
    Meridian_Execution_Sdk[Meridian.Execution.Sdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Identity[Meridian.Identity]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Domain[Meridian.Domain]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Identity[Meridian.Identity]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Storage[Meridian.Storage]
    Meridian_Identity[Meridian.Identity] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Identity[Meridian.Identity] --> Meridian_Storage[Meridian.Storage]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Core[Meridian.Core]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Domain[Meridian.Domain]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_IbApi_SmokeStub[Meridian.IbApi.SmokeStub]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Core[Meridian.Core]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Domain[Meridian.Domain]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Storage[Meridian.Storage]
    Meridian_Launcher[Meridian.Launcher] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ledger[Meridian.Ledger] --> Meridian_Core[Meridian.Core]
    Meridian_Ledger[Meridian.Ledger] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_LifecycleSupervisor[Meridian.LifecycleSupervisor] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Mcp[Meridian.Mcp] --> Meridian_Core[Meridian.Core]
    Meridian_Platform[Meridian.Platform] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Platform[Meridian.Platform] --> Meridian_Core[Meridian.Core]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords] --> Meridian_Storage[Meridian.Storage]
    Meridian_ProviderSdk[Meridian.ProviderSdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_ProcessIsolation[Meridian.ProcessIsolation]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Application[Meridian.Application]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Storage[Meridian.Storage]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Domain[Meridian.Domain]
    Meridian_QuantScript_Worker[Meridian.QuantScript.Worker] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_ReferenceData[Meridian.ReferenceData] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Reporting[Meridian.Reporting] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Reporting[Meridian.Reporting] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Risk[Meridian.Risk] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Risk[Meridian.Risk] --> Meridian_Execution[Meridian.Execution]
    Meridian_Risk[Meridian.Risk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Risk[Meridian.Risk] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Storage[Meridian.Storage] --> Meridian_Core[Meridian.Core]
    Meridian_Storage[Meridian.Storage] --> Meridian_Domain[Meridian.Domain]
    Meridian_Storage[Meridian.Storage] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Storage[Meridian.Storage] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Storage[Meridian.Storage] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Storage[Meridian.Storage] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Core[Meridian.Core]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Execution[Meridian.Execution]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp_Trading[Meridian.FSharp.Trading]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Platform[Meridian.Platform]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Storage[Meridian.Storage]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Application[Meridian.Application]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Audit[Meridian.Audit]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Documents[Meridian.Documents]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Entities[Meridian.Entities]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Execution[Meridian.Execution]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Identity[Meridian.Identity]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Platform[Meridian.Platform]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Risk[Meridian.Risk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Storage[Meridian.Storage]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Entities[Meridian.Entities]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Storage[Meridian.Storage]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Identity[Meridian.Identity]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Platform[Meridian.Platform]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Storage[Meridian.Storage]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
```

## Entry Point Projects

These projects are not referenced by other projects:

- **DocGenerator**
  - NuGet Packages: 1
- **FSharpInteropGenerator**
- **Meridian.Backtesting.Tests**
  - Dependencies: 3
  - NuGet Packages: 6
- **Meridian.Benchmarks**
  - Dependencies: 8
  - NuGet Packages: 2
- **Meridian.Contracts.UnitTests**
  - Dependencies: 1
  - NuGet Packages: 5
- **Meridian.Core.UnitTests**
  - Dependencies: 1
  - NuGet Packages: 5
- **Meridian.DesignModules.Tests**
  - Dependencies: 12
  - NuGet Packages: 4
- **Meridian.DirectLending.Tests**
  - Dependencies: 4
  - NuGet Packages: 7
- **Meridian.FSharp.Tests**
  - Dependencies: 9
  - NuGet Packages: 7
- **Meridian.FundStructure.Tests**
  - Dependencies: 6
  - NuGet Packages: 4
- **Meridian.Lifecycle.Tests**
  - Dependencies: 3
  - NuGet Packages: 5
- **Meridian.LifecycleSupervisor.Tests**
  - Dependencies: 3
  - NuGet Packages: 5
- **Meridian.QuantScript.Tests**
  - Dependencies: 2
  - NuGet Packages: 8
- **Meridian.Setup.Tests**
  - Dependencies: 1
  - NuGet Packages: 4
- **Meridian.Tests**
  - Dependencies: 30
  - NuGet Packages: 15
- **Meridian.Ui.Tests**
  - Dependencies: 1
  - NuGet Packages: 6
- **Meridian.Wpf.Tests**
  - Dependencies: 3
  - NuGet Packages: 7

## Most Complex Projects

Projects with the most dependencies:

| Project | Project Deps | Package Deps | Total |
|---------|--------------|--------------|-------|
| Meridian.Tests | 30 | 15 | 45 |
| Meridian.Application | 19 | 13 | 32 |
| Meridian.Wpf | 13 | 14 | 27 |
| Meridian.Ui.Shared | 23 | 3 | 26 |
| Meridian | 14 | 8 | 22 |
| Meridian.Infrastructure | 6 | 12 | 18 |
| Meridian.Storage | 6 | 11 | 17 |
| Meridian.DesignModules.Tests | 12 | 4 | 16 |
| Meridian.FSharp.Tests | 9 | 7 | 16 |
| Meridian.QuantScript | 8 | 6 | 14 |

## Full Dependency Graph

```mermaid
graph LR
    DocGenerator[DocGenerator]
    FSharpInteropGenerator[FSharpInteropGenerator]
    Meridian[Meridian]
    Meridian_Application[Meridian.Application]
    Meridian_Audit[Meridian.Audit]
    Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Backtesting_Tests[Meridian.Backtesting.Tests]
    Meridian_Benchmarks[Meridian.Benchmarks]
    Meridian_Contracts[Meridian.Contracts]
    Meridian_Contracts_UnitTests[Meridian.Contracts.UnitTests]
    Meridian_Core[Meridian.Core]
    Meridian_Core_UnitTests[Meridian.Core.UnitTests]
    Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests]
    Meridian_DirectLending_Tests[Meridian.DirectLending.Tests]
    Meridian_Documents[Meridian.Documents]
    Meridian_Domain[Meridian.Domain]
    Meridian_Entities[Meridian.Entities]
    Meridian_Execution[Meridian.Execution]
    Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FSharp[Meridian.FSharp]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates]
    Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests]
    Meridian_FSharp_Trading[Meridian.FSharp.Trading]
    Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests]
    Meridian_IbApi_SmokeStub[Meridian.IbApi.SmokeStub]
    Meridian_Identity[Meridian.Identity]
    Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Instruments[Meridian.Instruments]
    Meridian_Launcher[Meridian.Launcher]
    Meridian_Ledger[Meridian.Ledger]
    Meridian_Lifecycle_Tests[Meridian.Lifecycle.Tests]
    Meridian_LifecycleSupervisor[Meridian.LifecycleSupervisor]
    Meridian_LifecycleSupervisor_Tests[Meridian.LifecycleSupervisor.Tests]
    Meridian_Mcp[Meridian.Mcp]
    Meridian_Platform[Meridian.Platform]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_ProcessIsolation[Meridian.ProcessIsolation]
    Meridian_ProcessTestHelper[Meridian.ProcessTestHelper]
    Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_QuantScript[Meridian.QuantScript]
    Meridian_QuantScript_Tests[Meridian.QuantScript.Tests]
    Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Reporting[Meridian.Reporting]
    Meridian_Risk[Meridian.Risk]
    Meridian_Setup[Meridian.Setup]
    Meridian_Setup_Tests[Meridian.Setup.Tests]
    Meridian_Storage[Meridian.Storage]
    Meridian_Strategies[Meridian.Strategies]
    Meridian_TestSupport[Meridian.TestSupport]
    Meridian_Tests[Meridian.Tests]
    Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Ui_Tests[Meridian.Ui.Tests]
    Meridian_Workflow[Meridian.Workflow]
    Meridian_Wpf[Meridian.Wpf]
    Meridian_Wpf_Tests[Meridian.Wpf.Tests]
    Meridian[Meridian] --> Meridian_Application[Meridian.Application]
    Meridian[Meridian] --> Meridian_Audit[Meridian.Audit]
    Meridian[Meridian] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian[Meridian] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian[Meridian] --> Meridian_Storage[Meridian.Storage]
    Meridian[Meridian] --> Meridian_Core[Meridian.Core]
    Meridian[Meridian] --> Meridian_Domain[Meridian.Domain]
    Meridian[Meridian] --> Meridian_Contracts[Meridian.Contracts]
    Meridian[Meridian] --> Meridian_Identity[Meridian.Identity]
    Meridian[Meridian] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian[Meridian] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian[Meridian] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian[Meridian] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian[Meridian] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Application[Meridian.Application] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Application[Meridian.Application] --> Meridian_Storage[Meridian.Storage]
    Meridian_Application[Meridian.Application] --> Meridian_Core[Meridian.Core]
    Meridian_Application[Meridian.Application] --> Meridian_Domain[Meridian.Domain]
    Meridian_Application[Meridian.Application] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Application[Meridian.Application] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_Application[Meridian.Application] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Application[Meridian.Application] --> Meridian_Entities[Meridian.Entities]
    Meridian_Application[Meridian.Application] --> Meridian_Identity[Meridian.Identity]
    Meridian_Application[Meridian.Application] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_Application[Meridian.Application] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Application[Meridian.Application] --> Meridian_Platform[Meridian.Platform]
    Meridian_Application[Meridian.Application] --> Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_Application[Meridian.Application] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Application[Meridian.Application] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates]
    Meridian_Application[Meridian.Application] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Application[Meridian.Application] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Audit[Meridian.Audit] --> Meridian_Identity[Meridian.Identity]
    Meridian_Audit[Meridian.Audit] --> Meridian_Storage[Meridian.Storage]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Storage[Meridian.Storage]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Backtesting[Meridian.Backtesting] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Backtesting_Tests[Meridian.Backtesting.Tests] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Backtesting_Tests[Meridian.Backtesting.Tests] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Backtesting_Tests[Meridian.Backtesting.Tests] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian[Meridian]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Application[Meridian.Application]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Core[Meridian.Core]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Domain[Meridian.Domain]
    Meridian_Benchmarks[Meridian.Benchmarks] --> Meridian_Storage[Meridian.Storage]
    Meridian_Contracts_UnitTests[Meridian.Contracts.UnitTests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Core[Meridian.Core] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Core[Meridian.Core] --> Meridian_Domain[Meridian.Domain]
    Meridian_Core[Meridian.Core] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Core_UnitTests[Meridian.Core.UnitTests] --> Meridian_Core[Meridian.Core]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Core[Meridian.Core]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Domain[Meridian.Domain]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_DataIntegration[Meridian.DataIntegration] --> Meridian_Storage[Meridian.Storage]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Audit[Meridian.Audit]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Documents[Meridian.Documents]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Entities[Meridian.Entities]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Identity[Meridian.Identity]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Platform[Meridian.Platform]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_DesignModules_Tests[Meridian.DesignModules.Tests] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_DirectLending_Tests[Meridian.DirectLending.Tests] --> Meridian_Application[Meridian.Application]
    Meridian_DirectLending_Tests[Meridian.DirectLending.Tests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_DirectLending_Tests[Meridian.DirectLending.Tests] --> Meridian_Storage[Meridian.Storage]
    Meridian_DirectLending_Tests[Meridian.DirectLending.Tests] --> Meridian_TestSupport[Meridian.TestSupport]
    Meridian_Documents[Meridian.Documents] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Documents[Meridian.Documents] --> Meridian_Core[Meridian.Core]
    Meridian_Documents[Meridian.Documents] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Documents[Meridian.Documents] --> Meridian_Storage[Meridian.Storage]
    Meridian_Domain[Meridian.Domain] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Domain[Meridian.Domain] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Entities[Meridian.Entities] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Execution[Meridian.Execution] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Execution[Meridian.Execution] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Execution[Meridian.Execution] --> Meridian_Core[Meridian.Core]
    Meridian_Execution[Meridian.Execution] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Execution[Meridian.Execution] --> Meridian_Storage[Meridian.Storage]
    Meridian_Execution_Sdk[Meridian.Execution.Sdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Identity[Meridian.Identity]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FSharp[Meridian.FSharp] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_Identity[Meridian.Identity]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_FSharp_DirectLending_Aggregates[Meridian.FSharp.DirectLending.Aggregates]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_FSharp_Trading[Meridian.FSharp.Trading]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_FSharp_Tests[Meridian.FSharp.Tests] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Domain[Meridian.Domain]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Identity[Meridian.Identity]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_FinancialOperations[Meridian.FinancialOperations] --> Meridian_Storage[Meridian.Storage]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_Application[Meridian.Application]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_Entities[Meridian.Entities]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_FundStructure_Tests[Meridian.FundStructure.Tests] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Identity[Meridian.Identity] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Identity[Meridian.Identity] --> Meridian_Storage[Meridian.Storage]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Core[Meridian.Core]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Domain[Meridian.Domain]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Infrastructure[Meridian.Infrastructure] --> Meridian_IbApi_SmokeStub[Meridian.IbApi.SmokeStub]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Core[Meridian.Core]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Domain[Meridian.Domain]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Instruments[Meridian.Instruments] --> Meridian_Storage[Meridian.Storage]
    Meridian_Launcher[Meridian.Launcher] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ledger[Meridian.Ledger] --> Meridian_Core[Meridian.Core]
    Meridian_Ledger[Meridian.Ledger] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Lifecycle_Tests[Meridian.Lifecycle.Tests] --> Meridian_Application[Meridian.Application]
    Meridian_Lifecycle_Tests[Meridian.Lifecycle.Tests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Lifecycle_Tests[Meridian.Lifecycle.Tests] --> Meridian_Storage[Meridian.Storage]
    Meridian_LifecycleSupervisor[Meridian.LifecycleSupervisor] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_LifecycleSupervisor_Tests[Meridian.LifecycleSupervisor.Tests] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_LifecycleSupervisor_Tests[Meridian.LifecycleSupervisor.Tests] --> Meridian_Launcher[Meridian.Launcher]
    Meridian_LifecycleSupervisor_Tests[Meridian.LifecycleSupervisor.Tests] --> Meridian_LifecycleSupervisor[Meridian.LifecycleSupervisor]
    Meridian_Mcp[Meridian.Mcp] --> Meridian_Core[Meridian.Core]
    Meridian_Platform[Meridian.Platform] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Platform[Meridian.Platform] --> Meridian_Core[Meridian.Core]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_PortfolioRecords[Meridian.PortfolioRecords] --> Meridian_Storage[Meridian.Storage]
    Meridian_ProcessTestHelper[Meridian.ProcessTestHelper] --> Meridian_Application[Meridian.Application]
    Meridian_ProcessTestHelper[Meridian.ProcessTestHelper] --> Meridian_Documents[Meridian.Documents]
    Meridian_ProcessTestHelper[Meridian.ProcessTestHelper] --> Meridian_Storage[Meridian.Storage]
    Meridian_ProviderSdk[Meridian.ProviderSdk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_ProcessIsolation[Meridian.ProcessIsolation]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Application[Meridian.Application]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Storage[Meridian.Storage]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_QuantScript[Meridian.QuantScript] --> Meridian_Domain[Meridian.Domain]
    Meridian_QuantScript_Tests[Meridian.QuantScript.Tests] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_QuantScript_Tests[Meridian.QuantScript.Tests] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian_QuantScript_Worker[Meridian.QuantScript.Worker] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_ReferenceData[Meridian.ReferenceData] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Reporting[Meridian.Reporting] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Reporting[Meridian.Reporting] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Risk[Meridian.Risk] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Risk[Meridian.Risk] --> Meridian_Execution[Meridian.Execution]
    Meridian_Risk[Meridian.Risk] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Risk[Meridian.Risk] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Setup_Tests[Meridian.Setup.Tests] --> Meridian_Setup[Meridian.Setup]
    Meridian_Storage[Meridian.Storage] --> Meridian_Core[Meridian.Core]
    Meridian_Storage[Meridian.Storage] --> Meridian_Domain[Meridian.Domain]
    Meridian_Storage[Meridian.Storage] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Storage[Meridian.Storage] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Storage[Meridian.Storage] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Storage[Meridian.Storage] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Core[Meridian.Core]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Execution[Meridian.Execution]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_FSharp_Trading[Meridian.FSharp.Trading]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Strategies[Meridian.Strategies] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Tests[Meridian.Tests] --> Meridian[Meridian]
    Meridian_Tests[Meridian.Tests] --> Meridian_Application[Meridian.Application]
    Meridian_Tests[Meridian.Tests] --> Meridian_Audit[Meridian.Audit]
    Meridian_Tests[Meridian.Tests] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Tests[Meridian.Tests] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Tests[Meridian.Tests] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Tests[Meridian.Tests] --> Meridian_Documents[Meridian.Documents]
    Meridian_Tests[Meridian.Tests] --> Meridian_Entities[Meridian.Entities]
    Meridian_Tests[Meridian.Tests] --> Meridian_Execution[Meridian.Execution]
    Meridian_Tests[Meridian.Tests] --> Meridian_Execution_Sdk[Meridian.Execution.Sdk]
    Meridian_Tests[Meridian.Tests] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_Tests[Meridian.Tests] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Tests[Meridian.Tests] --> Meridian_Identity[Meridian.Identity]
    Meridian_Tests[Meridian.Tests] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Tests[Meridian.Tests] --> Meridian_Ledger[Meridian.Ledger]
    Meridian_Tests[Meridian.Tests] --> Meridian_Mcp[Meridian.Mcp]
    Meridian_Tests[Meridian.Tests] --> Meridian_ProcessTestHelper[Meridian.ProcessTestHelper]
    Meridian_Tests[Meridian.Tests] --> Meridian_Platform[Meridian.Platform]
    Meridian_Tests[Meridian.Tests] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian_Tests[Meridian.Tests] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Tests[Meridian.Tests] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Tests[Meridian.Tests] --> Meridian_Risk[Meridian.Risk]
    Meridian_Tests[Meridian.Tests] --> Meridian_Storage[Meridian.Storage]
    Meridian_Tests[Meridian.Tests] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Tests[Meridian.Tests] --> Meridian_Core[Meridian.Core]
    Meridian_Tests[Meridian.Tests] --> Meridian_Domain[Meridian.Domain]
    Meridian_Tests[Meridian.Tests] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Tests[Meridian.Tests] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Tests[Meridian.Tests] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_Tests[Meridian.Tests] --> Meridian_TestSupport[Meridian.TestSupport]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Platform[Meridian.Platform]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Storage[Meridian.Storage]
    Meridian_Ui_Services[Meridian.Ui.Services] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Application[Meridian.Application]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Audit[Meridian.Audit]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Backtesting_Sdk[Meridian.Backtesting.Sdk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_DataIntegration[Meridian.DataIntegration]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Documents[Meridian.Documents]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Entities[Meridian.Entities]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Execution[Meridian.Execution]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FSharp[Meridian.FSharp]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FSharp_Ledger[Meridian.FSharp.Ledger]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_FinancialOperations[Meridian.FinancialOperations]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Identity[Meridian.Identity]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Instruments[Meridian.Instruments]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Platform[Meridian.Platform]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_PortfolioRecords[Meridian.PortfolioRecords]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_ReferenceData[Meridian.ReferenceData]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Risk[Meridian.Risk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Storage[Meridian.Storage]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_ProviderSdk[Meridian.ProviderSdk]
    Meridian_Ui_Shared[Meridian.Ui.Shared] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Ui_Tests[Meridian.Ui.Tests] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Entities[Meridian.Entities]
    Meridian_Workflow[Meridian.Workflow] --> Meridian_Storage[Meridian.Storage]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Contracts[Meridian.Contracts]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Ui_Services[Meridian.Ui.Services]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Ui_Shared[Meridian.Ui.Shared]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Identity[Meridian.Identity]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Backtesting[Meridian.Backtesting]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Infrastructure[Meridian.Infrastructure]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Platform[Meridian.Platform]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Storage[Meridian.Storage]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Workflow[Meridian.Workflow]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_Strategies[Meridian.Strategies]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_QuantScript[Meridian.QuantScript]
    Meridian_Wpf[Meridian.Wpf] --> Meridian_QuantScript_Worker[Meridian.QuantScript.Worker]
    Meridian_Wpf_Tests[Meridian.Wpf.Tests] --> Meridian_Wpf[Meridian.Wpf]
    Meridian_Wpf_Tests[Meridian.Wpf.Tests] --> Meridian_Reporting[Meridian.Reporting]
    Meridian_Wpf_Tests[Meridian.Wpf.Tests] --> Meridian_Ui_Services[Meridian.Ui.Services]
```

## Project Details

### DocGenerator

**Path:** `build/dotnet/DocGenerator/DocGenerator.csproj`

**NuGet Packages (1):**
- System.CommandLine

### FSharpInteropGenerator

**Path:** `build/dotnet/FSharpInteropGenerator/FSharpInteropGenerator.csproj`

### Meridian

**Path:** `src/Meridian/Meridian.csproj`

**Project References:**
- Meridian.Application
- Meridian.Audit
- Meridian.Backtesting
- Meridian.Contracts
- Meridian.Core
- Meridian.Domain
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.ProviderSdk
- Meridian.QuantScript
- Meridian.QuantScript.Worker
- Meridian.Storage
- Meridian.Ui.Services
- Meridian.Ui.Shared

**Referenced By:**
- Meridian.Benchmarks
- Meridian.Tests

**NuGet Packages (8):**
- Microsoft.AspNetCore.OpenApi
- QuantConnect.Common
- QuantConnect.Indicators
- QuantConnect.Lean
- QuantConnect.Lean.Engine
- Serilog
- Serilog.Extensions.Logging
- Swashbuckle.AspNetCore

### Meridian.Application

**Path:** `src/Meridian.Application/Meridian.Application.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.DataIntegration
- Meridian.Domain
- Meridian.Entities
- Meridian.FSharp
- Meridian.FSharp.DirectLending.Aggregates
- Meridian.FSharp.Ledger
- Meridian.FinancialOperations
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.Ledger
- Meridian.Platform
- Meridian.PortfolioRecords
- Meridian.ProviderSdk
- Meridian.ReferenceData
- Meridian.Storage
- Meridian.Workflow

**Referenced By:**
- Meridian
- Meridian.Benchmarks
- Meridian.DirectLending.Tests
- Meridian.FundStructure.Tests
- Meridian.Lifecycle.Tests
- Meridian.ProcessTestHelper
- Meridian.QuantScript
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (13):**
- FluentValidation
- Microsoft.Extensions.Http.Polly
- Npgsql
- Polly
- Polly.Extensions
- Serilog
- Serilog.Extensions.Logging
- Serilog.Settings.Configuration
- Serilog.Sinks.Console
- Serilog.Sinks.File
- ... and 3 more

### Meridian.Audit

**Path:** `src/Meridian.Audit/Meridian.Audit.csproj`

**Project References:**
- Meridian.Identity
- Meridian.Storage

**Referenced By:**
- Meridian
- Meridian.DesignModules.Tests
- Meridian.Tests
- Meridian.Ui.Shared

### Meridian.Backtesting

**Path:** `src/Meridian.Backtesting/Meridian.Backtesting.csproj`

**Project References:**
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.Instruments
- Meridian.Storage
- Meridian.Strategies

**Referenced By:**
- Meridian
- Meridian.Backtesting.Tests
- Meridian.QuantScript
- Meridian.Tests
- Meridian.Ui.Shared
- Meridian.Wpf

**NuGet Packages (1):**
- Microsoft.Extensions.Logging.Abstractions

### Meridian.Backtesting.Sdk

**Path:** `src/Meridian.Backtesting.Sdk/Meridian.Backtesting.Sdk.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Ledger

**Referenced By:**
- Meridian.Backtesting
- Meridian.Backtesting.Tests
- Meridian.FSharp
- Meridian.FSharp.Tests
- Meridian.QuantScript
- Meridian.Strategies
- Meridian.Tests
- Meridian.Ui.Shared

### Meridian.Backtesting.Tests

**Path:** `tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj`

**Project References:**
- Meridian.Backtesting
- Meridian.Backtesting.Sdk
- Meridian.Infrastructure

**NuGet Packages (6):**
- FluentAssertions
- FsCheck.Xunit
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.Benchmarks

**Path:** `benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj`

**Project References:**
- Meridian
- Meridian.Application
- Meridian.Core
- Meridian.DataIntegration
- Meridian.Domain
- Meridian.Instruments
- Meridian.Storage
- Meridian.Strategies

**NuGet Packages (2):**
- BenchmarkDotNet
- BenchmarkDotNet.Diagnostics.Windows

### Meridian.Contracts

**Path:** `src/Meridian.Contracts/Meridian.Contracts.csproj`

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Backtesting
- Meridian.Backtesting.Sdk
- Meridian.Contracts.UnitTests
- Meridian.Core
- Meridian.DataIntegration
- Meridian.DirectLending.Tests
- Meridian.Documents
- Meridian.Domain
- Meridian.Entities
- Meridian.Execution
- Meridian.Execution.Sdk
- Meridian.FSharp
- Meridian.FSharp.DirectLending.Aggregates
- Meridian.FSharp.Tests
- Meridian.FinancialOperations
- Meridian.FundStructure.Tests
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.Launcher
- Meridian.Lifecycle.Tests
- Meridian.LifecycleSupervisor
- Meridian.LifecycleSupervisor.Tests
- Meridian.Platform
- Meridian.PortfolioRecords
- Meridian.ProviderSdk
- Meridian.QuantScript
- Meridian.ReferenceData
- Meridian.Reporting
- Meridian.Risk
- Meridian.Storage
- Meridian.Strategies
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Workflow
- Meridian.Wpf

### Meridian.Contracts.UnitTests

**Path:** `tests/Meridian.Contracts.UnitTests/Meridian.Contracts.UnitTests.csproj`

**Project References:**
- Meridian.Contracts

**NuGet Packages (5):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.Core

**Path:** `src/Meridian.Core/Meridian.Core.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Domain
- Meridian.ProviderSdk

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Benchmarks
- Meridian.Core.UnitTests
- Meridian.DataIntegration
- Meridian.Documents
- Meridian.Execution
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.Ledger
- Meridian.Mcp
- Meridian.Platform
- Meridian.Storage
- Meridian.Strategies
- Meridian.Tests

**NuGet Packages (6):**
- FluentValidation
- Microsoft.Extensions.Configuration
- Serilog
- Serilog.Settings.Configuration
- Serilog.Sinks.Console
- Serilog.Sinks.File

### Meridian.Core.UnitTests

**Path:** `tests/Meridian.Core.UnitTests/Meridian.Core.UnitTests.csproj`

**Project References:**
- Meridian.Core

**NuGet Packages (5):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.DataIntegration

**Path:** `src/Meridian.DataIntegration/Meridian.DataIntegration.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Domain
- Meridian.FSharp
- Meridian.ProviderSdk
- Meridian.Storage

**Referenced By:**
- Meridian.Application
- Meridian.Benchmarks
- Meridian.DesignModules.Tests
- Meridian.FinancialOperations
- Meridian.FundStructure.Tests
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (2):**
- Microsoft.Extensions.Logging.Abstractions
- System.Security.Cryptography.ProtectedData

### Meridian.DesignModules.Tests

**Path:** `tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj`

**Project References:**
- Meridian.Audit
- Meridian.DataIntegration
- Meridian.Documents
- Meridian.Entities
- Meridian.FinancialOperations
- Meridian.Identity
- Meridian.Instruments
- Meridian.Platform
- Meridian.PortfolioRecords
- Meridian.ReferenceData
- Meridian.Reporting
- Meridian.Workflow

**NuGet Packages (4):**
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.DirectLending.Tests

**Path:** `tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj`

**Project References:**
- Meridian.Application
- Meridian.Contracts
- Meridian.Storage
- Meridian.TestSupport

**NuGet Packages (7):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- Npgsql
- Testcontainers.PostgreSql
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.Documents

**Path:** `src/Meridian.Documents/Meridian.Documents.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Ledger
- Meridian.Storage

**Referenced By:**
- Meridian.DesignModules.Tests
- Meridian.ProcessTestHelper
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (3):**
- ClosedXML
- Microsoft.Extensions.DependencyInjection.Abstractions
- QuestPDF

### Meridian.Domain

**Path:** `src/Meridian.Domain/Meridian.Domain.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.ProviderSdk

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Benchmarks
- Meridian.Core
- Meridian.DataIntegration
- Meridian.FinancialOperations
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.QuantScript
- Meridian.Storage
- Meridian.Tests

**NuGet Packages (1):**
- Microsoft.Extensions.Logging.Abstractions

### Meridian.Entities

**Path:** `src/Meridian.Entities/Meridian.Entities.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.FundStructure.Tests
- Meridian.Tests
- Meridian.Ui.Shared
- Meridian.Workflow

### Meridian.Execution

**Path:** `src/Meridian.Execution/Meridian.Execution.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Execution.Sdk
- Meridian.Ledger
- Meridian.Storage

**Referenced By:**
- Meridian.Risk
- Meridian.Strategies
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (3):**
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting.Abstractions
- Microsoft.Extensions.Logging.Abstractions

### Meridian.Execution.Sdk

**Path:** `src/Meridian.Execution.Sdk/Meridian.Execution.Sdk.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian.Execution
- Meridian.FSharp
- Meridian.FSharp.Tests
- Meridian.FinancialOperations
- Meridian.Infrastructure
- Meridian.Risk
- Meridian.Strategies
- Meridian.Tests

### Meridian.FSharp

**Path:** `src/Meridian.FSharp/Meridian.FSharp.fsproj`

**Project References:**
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.Execution.Sdk
- Meridian.Identity

**Referenced By:**
- Meridian.Application
- Meridian.DataIntegration
- Meridian.FSharp.DirectLending.Aggregates
- Meridian.FSharp.Tests
- Meridian.FinancialOperations
- Meridian.Risk
- Meridian.Strategies
- Meridian.Ui.Shared

**NuGet Packages (1):**
- FSharp.Core

### Meridian.FSharp.DirectLending.Aggregates

**Path:** `src/Meridian.FSharp.DirectLending.Aggregates/Meridian.FSharp.DirectLending.Aggregates.fsproj`

**Project References:**
- Meridian.Contracts
- Meridian.FSharp

**Referenced By:**
- Meridian.Application
- Meridian.FSharp.Tests

**NuGet Packages (2):**
- FSharp.Core
- Sharpino

### Meridian.FSharp.Ledger

**Path:** `src/Meridian.FSharp.Ledger/Meridian.FSharp.Ledger.fsproj`

**Referenced By:**
- Meridian.Application
- Meridian.FSharp.Tests
- Meridian.Ledger
- Meridian.Strategies
- Meridian.Ui.Shared

**NuGet Packages (1):**
- FSharp.Core

### Meridian.FSharp.Tests

**Path:** `tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj`

**Project References:**
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.Execution.Sdk
- Meridian.FSharp
- Meridian.FSharp.DirectLending.Aggregates
- Meridian.FSharp.Ledger
- Meridian.FSharp.Trading
- Meridian.Identity
- Meridian.Ledger

**NuGet Packages (7):**
- FSharp.Core
- FsCheck.Xunit
- FsUnit.xUnit
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.FSharp.Trading

**Path:** `src/Meridian.FSharp.Trading/Meridian.FSharp.Trading.fsproj`

**Referenced By:**
- Meridian.FSharp.Tests
- Meridian.Strategies

**NuGet Packages (1):**
- FSharp.Core

### Meridian.FinancialOperations

**Path:** `src/Meridian.FinancialOperations/Meridian.FinancialOperations.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.DataIntegration
- Meridian.Domain
- Meridian.Execution.Sdk
- Meridian.FSharp
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.Ledger
- Meridian.ProviderSdk
- Meridian.Storage

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (2):**
- Microsoft.Extensions.Logging.Abstractions
- Npgsql

### Meridian.FundStructure.Tests

**Path:** `tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj`

**Project References:**
- Meridian.Application
- Meridian.Contracts
- Meridian.DataIntegration
- Meridian.Entities
- Meridian.Ui.Shared
- Meridian.Workflow

**NuGet Packages (4):**
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.IbApi.SmokeStub

**Path:** `src/Meridian.IbApi.SmokeStub/Meridian.IbApi.SmokeStub.csproj`

**Referenced By:**
- Meridian.Infrastructure

### Meridian.Identity

**Path:** `src/Meridian.Identity/Meridian.Identity.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Storage

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Audit
- Meridian.DesignModules.Tests
- Meridian.FSharp
- Meridian.FSharp.Tests
- Meridian.FinancialOperations
- Meridian.Tests
- Meridian.Ui.Shared
- Meridian.Wpf

**NuGet Packages (2):**
- Microsoft.Extensions.Caching.Memory
- Npgsql

### Meridian.Infrastructure

**Path:** `src/Meridian.Infrastructure/Meridian.Infrastructure.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Domain
- Meridian.Execution.Sdk
- Meridian.IbApi.SmokeStub
- Meridian.ProviderSdk

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Backtesting.Tests
- Meridian.FinancialOperations
- Meridian.Strategies
- Meridian.Tests
- Meridian.Ui.Services
- Meridian.Wpf

**NuGet Packages (12):**
- FluentValidation
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Http
- Microsoft.Extensions.Http.Polly
- Microsoft.Extensions.ObjectPool
- Polly
- Polly.Extensions
- SSH.NET
- Serilog
- ... and 2 more

### Meridian.Instruments

**Path:** `src/Meridian.Instruments/Meridian.Instruments.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Domain
- Meridian.ProviderSdk
- Meridian.Storage

**Referenced By:**
- Meridian.Application
- Meridian.Backtesting
- Meridian.Benchmarks
- Meridian.DesignModules.Tests
- Meridian.FinancialOperations
- Meridian.Strategies
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (2):**
- Microsoft.Extensions.Logging.Abstractions
- Skender.Stock.Indicators

### Meridian.Launcher

**Path:** `src/Meridian.Launcher/Meridian.Launcher.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian.LifecycleSupervisor.Tests

### Meridian.Ledger

**Path:** `src/Meridian.Ledger/Meridian.Ledger.csproj`

**Project References:**
- Meridian.Core
- Meridian.FSharp.Ledger

**Referenced By:**
- Meridian.Application
- Meridian.Backtesting.Sdk
- Meridian.Documents
- Meridian.Execution
- Meridian.FSharp.Tests
- Meridian.FinancialOperations
- Meridian.Reporting
- Meridian.Storage
- Meridian.Tests

### Meridian.Lifecycle.Tests

**Path:** `tests/Meridian.Lifecycle.Tests/Meridian.Lifecycle.Tests.csproj`

**Project References:**
- Meridian.Application
- Meridian.Contracts
- Meridian.Storage

**NuGet Packages (5):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.LifecycleSupervisor

**Path:** `src/Meridian.LifecycleSupervisor/Meridian.LifecycleSupervisor.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian.LifecycleSupervisor.Tests

**NuGet Packages (1):**
- System.Security.Cryptography.ProtectedData

### Meridian.LifecycleSupervisor.Tests

**Path:** `tests/Meridian.LifecycleSupervisor.Tests/Meridian.LifecycleSupervisor.Tests.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Launcher
- Meridian.LifecycleSupervisor

**NuGet Packages (5):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.Mcp

**Path:** `src/Meridian.Mcp/Meridian.Mcp.csproj`

**Project References:**
- Meridian.Core

**Referenced By:**
- Meridian.Tests

**NuGet Packages (2):**
- Microsoft.Extensions.Hosting
- ModelContextProtocol

### Meridian.Platform

**Path:** `src/Meridian.Platform/Meridian.Platform.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.Tests
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Wpf

**NuGet Packages (10):**
- Microsoft.Extensions.Hosting.Abstractions
- Microsoft.Extensions.Logging.Abstractions
- OpenTelemetry
- OpenTelemetry.Api
- OpenTelemetry.Exporter.Console
- OpenTelemetry.Exporter.OpenTelemetryProtocol
- OpenTelemetry.Exporter.Prometheus.AspNetCore
- OpenTelemetry.Extensions.Hosting
- OpenTelemetry.Instrumentation.AspNetCore
- OpenTelemetry.Instrumentation.Http

### Meridian.PortfolioRecords

**Path:** `src/Meridian.PortfolioRecords/Meridian.PortfolioRecords.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Storage

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.Ui.Shared

### Meridian.ProcessIsolation

**Path:** `src/Meridian.ProcessIsolation/Meridian.ProcessIsolation.csproj`

**Referenced By:**
- Meridian.QuantScript

### Meridian.ProcessTestHelper

**Path:** `tests/Meridian.ProcessTestHelper/Meridian.ProcessTestHelper.csproj`

**Project References:**
- Meridian.Application
- Meridian.Documents
- Meridian.Storage

**Referenced By:**
- Meridian.Tests

### Meridian.ProviderSdk

**Path:** `src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Core
- Meridian.DataIntegration
- Meridian.Domain
- Meridian.FinancialOperations
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.QuantScript
- Meridian.Storage
- Meridian.Ui.Shared

**NuGet Packages (2):**
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Logging.Abstractions

### Meridian.QuantScript

**Path:** `src/Meridian.QuantScript/Meridian.QuantScript.csproj`

**Project References:**
- Meridian.Application
- Meridian.Backtesting
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.Domain
- Meridian.ProcessIsolation
- Meridian.ProviderSdk
- Meridian.Storage

**Referenced By:**
- Meridian
- Meridian.QuantScript.Tests
- Meridian.QuantScript.Worker
- Meridian.Ui.Shared
- Meridian.Wpf

**NuGet Packages (6):**
- Microsoft.CodeAnalysis.CSharp.Scripting
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.Options
- Microsoft.Extensions.Options.ConfigurationExtensions
- Skender.Stock.Indicators

### Meridian.QuantScript.Tests

**Path:** `tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj`

**Project References:**
- Meridian.QuantScript
- Meridian.QuantScript.Worker

**NuGet Packages (8):**
- FluentAssertions
- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.Options
- Microsoft.NET.Test.Sdk
- Moq
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.QuantScript.Worker

**Path:** `src/Meridian.QuantScript.Worker/Meridian.QuantScript.Worker.csproj`

**Project References:**
- Meridian.QuantScript

**Referenced By:**
- Meridian
- Meridian.QuantScript.Tests
- Meridian.Tests
- Meridian.Wpf

### Meridian.ReferenceData

**Path:** `src/Meridian.ReferenceData/Meridian.ReferenceData.csproj`

**Project References:**
- Meridian.Contracts

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.Tests
- Meridian.Ui.Shared

### Meridian.Reporting

**Path:** `src/Meridian.Reporting/Meridian.Reporting.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Ledger

**Referenced By:**
- Meridian.DesignModules.Tests
- Meridian.Storage
- Meridian.Tests
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Wpf
- Meridian.Wpf.Tests

**NuGet Packages (1):**
- Microsoft.Extensions.Logging.Abstractions

### Meridian.Risk

**Path:** `src/Meridian.Risk/Meridian.Risk.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Execution
- Meridian.Execution.Sdk
- Meridian.FSharp

**Referenced By:**
- Meridian.Tests
- Meridian.Ui.Shared

**NuGet Packages (2):**
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Logging.Abstractions

### Meridian.Setup

**Path:** `src/Meridian.Setup/Meridian.Setup.csproj`

**Referenced By:**
- Meridian.Setup.Tests

### Meridian.Setup.Tests

**Path:** `tests/Meridian.Setup.Tests/Meridian.Setup.Tests.csproj`

**Project References:**
- Meridian.Setup

**NuGet Packages (4):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- xunit
- xunit.runner.visualstudio

### Meridian.Storage

**Path:** `src/Meridian.Storage/Meridian.Storage.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Core
- Meridian.Domain
- Meridian.Ledger
- Meridian.ProviderSdk
- Meridian.Reporting

**Referenced By:**
- Meridian
- Meridian.Application
- Meridian.Audit
- Meridian.Backtesting
- Meridian.Benchmarks
- Meridian.DataIntegration
- Meridian.DirectLending.Tests
- Meridian.Documents
- Meridian.Execution
- Meridian.FinancialOperations
- Meridian.Identity
- Meridian.Instruments
- Meridian.Lifecycle.Tests
- Meridian.PortfolioRecords
- Meridian.ProcessTestHelper
- Meridian.QuantScript
- Meridian.Tests
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Workflow
- Meridian.Wpf

**NuGet Packages (11):**
- Apache.Arrow
- Blake3.Native
- DuckDB.NET.Data.Full
- K4os.Compression.LZ4.Streams
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting
- Npgsql
- Parquet.Net
- Serilog
- ZstdSharp.Port
- ... and 1 more

### Meridian.Strategies

**Path:** `src/Meridian.Strategies/Meridian.Strategies.csproj`

**Project References:**
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.Core
- Meridian.Execution
- Meridian.Execution.Sdk
- Meridian.FSharp
- Meridian.FSharp.Ledger
- Meridian.FSharp.Trading
- Meridian.Infrastructure
- Meridian.Instruments

**Referenced By:**
- Meridian.Backtesting
- Meridian.Benchmarks
- Meridian.Tests
- Meridian.Ui.Shared
- Meridian.Wpf

**NuGet Packages (2):**
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Logging.Abstractions

### Meridian.TestSupport

**Path:** `tests/Meridian.TestSupport/Meridian.TestSupport.csproj`

**Referenced By:**
- Meridian.DirectLending.Tests
- Meridian.Tests

**NuGet Packages (2):**
- Npgsql
- Testcontainers.PostgreSql

### Meridian.Tests

**Path:** `tests/Meridian.Tests/Meridian.Tests.csproj`

**Project References:**
- Meridian
- Meridian.Application
- Meridian.Audit
- Meridian.Backtesting
- Meridian.Backtesting.Sdk
- Meridian.Core
- Meridian.DataIntegration
- Meridian.Documents
- Meridian.Domain
- Meridian.Entities
- Meridian.Execution
- Meridian.Execution.Sdk
- Meridian.FinancialOperations
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.Instruments
- Meridian.Ledger
- Meridian.Mcp
- Meridian.Platform
- Meridian.ProcessTestHelper
- Meridian.QuantScript.Worker
- Meridian.ReferenceData
- Meridian.Reporting
- Meridian.Risk
- Meridian.Storage
- Meridian.Strategies
- Meridian.TestSupport
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Workflow

**NuGet Packages (15):**
- Bogus
- FluentAssertions
- FsCheck.Xunit
- Microsoft.AspNetCore.Mvc.Testing
- Microsoft.NET.Test.Sdk
- Moq
- NSubstitute
- Npgsql
- System.Reactive
- Testcontainers.PostgreSql
- ... and 5 more

### Meridian.Ui.Services

**Path:** `src/Meridian.Ui.Services/Meridian.Ui.Services.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Infrastructure
- Meridian.Platform
- Meridian.Reporting
- Meridian.Storage
- Meridian.Ui.Shared

**Referenced By:**
- Meridian
- Meridian.Tests
- Meridian.Ui.Tests
- Meridian.Wpf
- Meridian.Wpf.Tests

**NuGet Packages (4):**
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Http
- Microsoft.Extensions.Http.Polly
- ZstdSharp.Port

### Meridian.Ui.Shared

**Path:** `src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj`

**Project References:**
- Meridian.Application
- Meridian.Audit
- Meridian.Backtesting
- Meridian.Backtesting.Sdk
- Meridian.Contracts
- Meridian.DataIntegration
- Meridian.Documents
- Meridian.Entities
- Meridian.Execution
- Meridian.FSharp
- Meridian.FSharp.Ledger
- Meridian.FinancialOperations
- Meridian.Identity
- Meridian.Instruments
- Meridian.Platform
- Meridian.PortfolioRecords
- Meridian.ProviderSdk
- Meridian.QuantScript
- Meridian.ReferenceData
- Meridian.Reporting
- Meridian.Risk
- Meridian.Storage
- Meridian.Strategies

**Referenced By:**
- Meridian
- Meridian.FundStructure.Tests
- Meridian.Tests
- Meridian.Ui.Services
- Meridian.Wpf

**NuGet Packages (3):**
- Microsoft.AspNetCore.OpenApi
- Serilog
- Swashbuckle.AspNetCore

### Meridian.Ui.Tests

**Path:** `tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj`

**Project References:**
- Meridian.Ui.Services

**NuGet Packages (6):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- Moq
- coverlet.collector
- xunit
- xunit.runner.visualstudio

### Meridian.Workflow

**Path:** `src/Meridian.Workflow/Meridian.Workflow.csproj`

**Project References:**
- Meridian.Contracts
- Meridian.Entities
- Meridian.Storage

**Referenced By:**
- Meridian.Application
- Meridian.DesignModules.Tests
- Meridian.FundStructure.Tests
- Meridian.Tests
- Meridian.Wpf

### Meridian.Wpf

**Path:** `src/Meridian.Wpf/Meridian.Wpf.csproj`

**Project References:**
- Meridian.Backtesting
- Meridian.Contracts
- Meridian.Identity
- Meridian.Infrastructure
- Meridian.Platform
- Meridian.QuantScript
- Meridian.QuantScript.Worker
- Meridian.Reporting
- Meridian.Storage
- Meridian.Strategies
- Meridian.Ui.Services
- Meridian.Ui.Shared
- Meridian.Workflow

**Referenced By:**
- Meridian.Wpf.Tests

**NuGet Packages (14):**
- AvalonEdit
- CommunityToolkit.Mvvm
- Dirkster.AvalonDock
- FSharp.Core
- LiveChartsCore.SkiaSharpView.WPF
- MaterialDesignThemes
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Http
- Microsoft.Extensions.Http.Polly
- ... and 4 more

### Meridian.Wpf.Tests

**Path:** `tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj`

**Project References:**
- Meridian.Reporting
- Meridian.Ui.Services
- Meridian.Wpf

**NuGet Packages (7):**
- FluentAssertions
- Microsoft.NET.Test.Sdk
- Moq
- NSubstitute
- coverlet.collector
- xunit
- xunit.runner.visualstudio

---

*This report is auto-generated. Run `python3 build/scripts/docs/generate-dependency-graph.py` to regenerate.*
