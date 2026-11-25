# Meridian AI Repo Navigation

> Auto-generated on 2026-11-25T19:53:55Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

## Quick Start

Use this file when an assistant needs fast orientation before reading subsystem-specific guidance.

| Task shape | Start here | Authoritative docs |
|---|---|---|
| Consumer launcher, lifecycle supervisor, and installation | `Meridian.Launcher`, `Meridian.LifecycleSupervisor`, `Meridian.Setup` | `docs/reference/lifecycle-control-plane.md`, `docs/reference/verified-operation-outcomes.md`, `docs/operators/browser-workstation-installer.md` |
| Provider implementation and provider bugs | `Meridian.ProviderSdk`, `Meridian.Infrastructure`, `Meridian.Storage` | `docs/ai/claude/CLAUDE.providers.md`, `docs/development/provider-implementation.md`, `docs/ai/ai-known-errors.md` |
| Browser workstation and dashboard UI issues | `Meridian.Ui.Dashboard`, `Meridian.Ui.Services`, `Meridian.Ui.Shared` | `docs/ai/navigation/README.md`, `docs/ai/ai-known-errors.md` |
| WPF and workstation workflow issues | `Meridian.Wpf`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian` | `docs/engineering/plans/wpf-web-ui-alignment-plan.md`, `docs/ai/ai-known-errors.md` |
| Storage and WAL investigations | `Meridian.Storage`, `Meridian.Application` | `docs/ai/claude/CLAUDE.storage.md`, `docs/ai/ai-known-errors.md` |
| MCP tools, prompts, and resources | `Meridian.Mcp` | `docs/ai/navigation/README.md`, `docs/ai/README.md` |
| Accounting, posting controls, and ledger reads | `Meridian.Mcp`, `Meridian.Ledger` | `src/Meridian.Mcp/README.md`, `docs/ai/context/accounting-context.md` |

## Subsystems

### Host and Composition

Runtime startup, application composition, shared contracts, and cross-cutting infrastructure.

- Projects: `Meridian`, `Meridian.Application`, `Meridian.Contracts`, `Meridian.Core`, `Meridian.Launcher`, `Meridian.LifecycleSupervisor`, `Meridian.Setup`
- Entrypoints: `src/Meridian.Application/Composition`, `src/Meridian.Application/Pipeline`, `src/Meridian.Contracts`, `src/Meridian.Core/Serialization`
- Key contracts: `src/Meridian.Application/Pipeline/EventPipeline.cs`, `src/Meridian.Contracts`, `src/Meridian.Contracts/Lifecycle`, `src/Meridian.Contracts/Operations`
- Common tasks: startup debugging, service composition, configuration, shared contracts
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`, `docs/reference/verified-operation-outcomes.md`

### Providers and Storage

Provider contracts, adapter implementations, storage catalog, WAL, and archival behavior.

- Projects: `Meridian.Infrastructure`, `Meridian.ProviderSdk`, `Meridian.Storage`
- Entrypoints: `src/Meridian.Infrastructure/Adapters`, `src/Meridian.ProviderSdk/IMarketDataClient.cs`, `src/Meridian.Storage/Archival`, `src/Meridian.Storage/Interfaces`
- Key contracts: `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`, `src/Meridian.ProviderSdk/IMarketDataClient.cs`, `src/Meridian.Storage/Archival/WriteAheadLog.cs`, `src/Meridian.Storage/Interfaces/IStorageSink.cs`
- Common tasks: add provider, provider bug, storage regression, catalog query
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/claude/CLAUDE.providers.md`, `docs/ai/claude/CLAUDE.storage.md`

### Desktop and UI Workflows

WPF desktop shell, shared UI services, and browser-facing UI surfaces.

- Projects: `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian.Wpf`, `Meridian.Ui.Dashboard`
- Entrypoints: `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, `src/Meridian.Ui/dashboard/package.json`, `src/Meridian.Ui/dashboard/src/app.tsx`
- Key contracts: `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, `src/Meridian.Ui/dashboard/package.json`, `src/Meridian.Ui/dashboard/src/main.tsx`
- Common tasks: wpf issue, viewmodel routing, workspace flow, ui polish
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`, `docs/engineering/plans/wpf-web-ui-alignment-plan.md`

### Backtesting and Strategy Analytics

Replay engine, backtesting SDK, and strategy analytics workflows.

- Projects: `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.QuantScript`
- Entrypoints: `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript`
- Key contracts: `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript`
- Common tasks: backtesting bug, simulation, strategy analytics scripting
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`

### Execution, Risk, and Strategies

Order routing, gateways, risk rules, and strategy lifecycle management.

- Projects: `Meridian.Execution`, `Meridian.Execution.Sdk`, `Meridian.Risk`, `Meridian.Strategies`
- Entrypoints: `src/Meridian.Execution.Sdk`, `src/Meridian.Execution/Interfaces`, `src/Meridian.Risk/IRiskRule.cs`, `src/Meridian.Strategies/Interfaces`
- Key contracts: `src/Meridian.Execution.Sdk`, `src/Meridian.Execution/Interfaces/IOrderGateway.cs`, `src/Meridian.Risk/IRiskRule.cs`, `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`
- Common tasks: execution issue, risk validation, strategy lifecycle
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`

### Domain, Ledger, and F#

Core domain rules, F# interop, ledger logic, and direct lending aggregates.

- Projects: `Meridian.Domain`, `Meridian.Ledger`, `Meridian.FSharp`, `Meridian.FSharp.DirectLending.Aggregates`, `Meridian.FSharp.Ledger`, `Meridian.FSharp.Trading`
- Entrypoints: `src/Meridian.Domain/Collectors`, `src/Meridian.FSharp.DirectLending.Aggregates`, `src/Meridian.FSharp.Ledger`, `src/Meridian.FSharp.Trading`
- Key contracts: `src/Meridian.Domain`, `src/Meridian.FSharp`, `src/Meridian.FSharp.DirectLending.Aggregates`, `src/Meridian.FSharp.Ledger`
- Common tasks: fsharp interop, domain rule, ledger behavior, direct lending
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/claude/CLAUDE.fsharp.md`, `docs/ai/navigation/README.md`

### MCP Integration

MCP hosts, tools, prompts, and resources that expose Meridian capabilities to LLMs.

- Projects: `Meridian.Mcp`
- Entrypoints: `src/Meridian.Mcp/Program.cs`, `src/Meridian.Mcp/Prompts`, `src/Meridian.Mcp/Resources`, `src/Meridian.Mcp/Tools`
- Key contracts: `src/Meridian.Mcp/Program.cs`
- Common tasks: mcp work, new mcp tool, resource routing
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`, `src/Meridian.Mcp/README.md`

## Project Index

Every project under `src/` (45) and `tests/` (19). Subsystem routing above covers only catalogued projects.

17 source project(s) are uncatalogued: they have no `PROJECT_SEEDS` entry in `build/scripts/docs/generate-ai-navigation.py`, so no subsystem or task route points at them yet.

### Source projects

| Project | Path | Subsystem |
|---|---|---|
| `Meridian` | `src/Meridian` | `host-shell` |
| `Meridian.Application` | `src/Meridian.Application` | `host-shell` |
| `Meridian.Audit` | `src/Meridian.Audit` | _uncatalogued_ |
| `Meridian.Backtesting` | `src/Meridian.Backtesting` | `backtesting-research` |
| `Meridian.Backtesting.Sdk` | `src/Meridian.Backtesting.Sdk` | `backtesting-research` |
| `Meridian.Contracts` | `src/Meridian.Contracts` | `host-shell` |
| `Meridian.Core` | `src/Meridian.Core` | `host-shell` |
| `Meridian.DataIntegration` | `src/Meridian.DataIntegration` | _uncatalogued_ |
| `Meridian.Documents` | `src/Meridian.Documents` | _uncatalogued_ |
| `Meridian.Domain` | `src/Meridian.Domain` | `domain-ledger-fsharp` |
| `Meridian.Entities` | `src/Meridian.Entities` | _uncatalogued_ |
| `Meridian.Execution` | `src/Meridian.Execution` | `execution-risk` |
| `Meridian.Execution.Sdk` | `src/Meridian.Execution.Sdk` | `execution-risk` |
| `Meridian.FSharp` | `src/Meridian.FSharp` | `domain-ledger-fsharp` |
| `Meridian.FSharp.DirectLending.Aggregates` | `src/Meridian.FSharp.DirectLending.Aggregates` | `domain-ledger-fsharp` |
| `Meridian.FSharp.Ledger` | `src/Meridian.FSharp.Ledger` | `domain-ledger-fsharp` |
| `Meridian.FSharp.Trading` | `src/Meridian.FSharp.Trading` | `domain-ledger-fsharp` |
| `Meridian.FinancialOperations` | `src/Meridian.FinancialOperations` | _uncatalogued_ |
| `Meridian.IbApi.SmokeStub` | `src/Meridian.IbApi.SmokeStub` | _uncatalogued_ |
| `Meridian.Identity` | `src/Meridian.Identity` | _uncatalogued_ |
| `Meridian.Infrastructure` | `src/Meridian.Infrastructure` | `providers-data` |
| `Meridian.Instruments` | `src/Meridian.Instruments` | _uncatalogued_ |
| `Meridian.Launcher` | `src/Meridian.Launcher` | `host-shell` |
| `Meridian.Ledger` | `src/Meridian.Ledger` | `domain-ledger-fsharp` |
| `Meridian.LifecycleSupervisor` | `src/Meridian.LifecycleSupervisor` | `host-shell` |
| `Meridian.Mcp` | `src/Meridian.Mcp` | `mcp-integration` |
| `Meridian.Platform` | `src/Meridian.Platform` | _uncatalogued_ |
| `Meridian.PortfolioRecords` | `src/Meridian.PortfolioRecords` | _uncatalogued_ |
| `Meridian.ProcessIsolation` | `src/Meridian.ProcessIsolation` | _uncatalogued_ |
| `Meridian.ProviderSdk` | `src/Meridian.ProviderSdk` | `providers-data` |
| `Meridian.QuantScript` | `src/Meridian.QuantScript` | `backtesting-research` |
| `Meridian.QuantScript.Worker` | `src/Meridian.QuantScript.Worker` | _uncatalogued_ |
| `Meridian.ReferenceData` | `src/Meridian.ReferenceData` | _uncatalogued_ |
| `Meridian.Reporting` | `src/Meridian.Reporting` | _uncatalogued_ |
| `Meridian.Reporting.ReadModels` | `src/Meridian.Reporting.ReadModels` | _uncatalogued_ |
| `Meridian.Risk` | `src/Meridian.Risk` | `execution-risk` |
| `Meridian.Setup` | `src/Meridian.Setup` | `host-shell` |
| `Meridian.Storage` | `src/Meridian.Storage` | `providers-data` |
| `Meridian.Storage.Accounting` | `src/Meridian.Storage.Accounting` | _uncatalogued_ |
| `Meridian.Strategies` | `src/Meridian.Strategies` | `execution-risk` |
| `Meridian.Ui.Services` | `src/Meridian.Ui.Services` | `workstation-ui` |
| `Meridian.Ui.Shared` | `src/Meridian.Ui.Shared` | `workstation-ui` |
| `Meridian.Workflow` | `src/Meridian.Workflow` | _uncatalogued_ |
| `Meridian.Wpf` | `src/Meridian.Wpf` | `workstation-ui` |
| `Meridian.Ui.Dashboard` | `src/Meridian.Ui/dashboard` | `workstation-ui` |

### Test projects

| Project | Path | Covers |
|---|---|---|
| `Meridian.Backtesting.Tests` | `tests/Meridian.Backtesting.Tests` | `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.Infrastructure` |
| `Meridian.Contracts.UnitTests` | `tests/Meridian.Contracts.UnitTests` | `Meridian.Contracts` |
| `Meridian.Core.UnitTests` | `tests/Meridian.Core.UnitTests` | `Meridian.Core` |
| `Meridian.Demo.Tests` | `tests/Meridian.Demo.Tests` | `Meridian` |
| `Meridian.DesignModules.Tests` | `tests/Meridian.DesignModules.Tests` | `Meridian.Audit`, `Meridian.DataIntegration`, `Meridian.Documents`, `Meridian.Entities`, `Meridian.FinancialOperations`, `Meridian.Identity`, `Meridian.Instruments`, `Meridian.Platform`, `Meridian.PortfolioRecords`, `Meridian.ReferenceData`, `Meridian.Reporting`, `Meridian.Workflow` |
| `Meridian.DirectLending.Tests` | `tests/Meridian.DirectLending.Tests` | `Meridian.Application`, `Meridian.Contracts`, `Meridian.Storage`, `Meridian.Storage.Accounting` |
| `Meridian.Execution.Tests` | `tests/Meridian.Execution.Tests` | `Meridian.Application`, `Meridian.Backtesting`, `Meridian.Core`, `Meridian.Domain`, `Meridian.Execution`, `Meridian.Execution.Sdk`, `Meridian.FinancialOperations`, `Meridian.Identity`, `Meridian.Infrastructure`, `Meridian.Ledger`, `Meridian.Reporting`, `Meridian.Risk`, `Meridian.Storage`, `Meridian.Storage.Accounting` |
| `Meridian.FSharp.Tests` | `tests/Meridian.FSharp.Tests` | `Meridian.Backtesting.Sdk`, `Meridian.Contracts`, `Meridian.Execution.Sdk`, `Meridian.FSharp`, `Meridian.FSharp.DirectLending.Aggregates`, `Meridian.FSharp.Ledger`, `Meridian.FSharp.Trading`, `Meridian.Identity`, `Meridian.Ledger` |
| `Meridian.FundStructure.Tests` | `tests/Meridian.FundStructure.Tests` | `Meridian.Application`, `Meridian.Contracts`, `Meridian.DataIntegration`, `Meridian.Entities`, `Meridian.Ui.Shared`, `Meridian.Workflow` |
| `Meridian.Lifecycle.Tests` | `tests/Meridian.Lifecycle.Tests` | `Meridian.Application`, `Meridian.Contracts`, `Meridian.Storage` |
| `Meridian.LifecycleSupervisor.Tests` | `tests/Meridian.LifecycleSupervisor.Tests` | `Meridian.Contracts`, `Meridian.Launcher`, `Meridian.LifecycleSupervisor` |
| `Meridian.ProcessTestHelper` | `tests/Meridian.ProcessTestHelper` | `Meridian.Application`, `Meridian.Documents`, `Meridian.Storage`, `Meridian.Storage.Accounting` |
| `Meridian.QuantScript.Tests` | `tests/Meridian.QuantScript.Tests` | `Meridian.QuantScript`, `Meridian.QuantScript.Worker` |
| `Meridian.Setup.Tests` | `tests/Meridian.Setup.Tests` | `Meridian.Setup` |
| `Meridian.Storage.Tests` | `tests/Meridian.Storage.Tests` | `Meridian.Core`, `Meridian.Domain`, `Meridian.Infrastructure`, `Meridian.Storage` |
| `Meridian.TestSupport` | `tests/Meridian.TestSupport` | — |
| `Meridian.Tests` | `tests/Meridian.Tests` | `Meridian`, `Meridian.Application`, `Meridian.Audit`, `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.Core`, `Meridian.DataIntegration`, `Meridian.Documents`, `Meridian.Domain`, `Meridian.Entities`, `Meridian.Execution`, `Meridian.Execution.Sdk`, `Meridian.FinancialOperations`, `Meridian.Identity`, `Meridian.Infrastructure`, `Meridian.Instruments`, `Meridian.Ledger`, `Meridian.Mcp`, `Meridian.Platform`, `Meridian.QuantScript.Worker`, `Meridian.ReferenceData`, `Meridian.Reporting`, `Meridian.Risk`, `Meridian.Storage`, `Meridian.Storage.Accounting`, `Meridian.Strategies`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian.Workflow` |
| `Meridian.Ui.Tests` | `tests/Meridian.Ui.Tests` | `Meridian.Ui.Services` |
| `Meridian.Wpf.Tests` | `tests/Meridian.Wpf.Tests` | `Meridian.Reporting`, `Meridian.Ui.Services`, `Meridian.Wpf` |

## High-Signal Symbols

| Symbol | Kind | Project | Why it matters |
|---|---|---|---|
| `IMarketDataClient` | interface | `Meridian.ProviderSdk` | Primary entrypoint for streaming provider work. |
| `IHistoricalDataProvider` | interface | `Meridian.Infrastructure` | Primary contract for historical/backfill providers. |
| `MarketDataJsonContext` | json-context | `Meridian.Core` | Source-generated JSON context used in hot-path and provider serialization. |
| `EventPipeline` | pipeline | `Meridian.Application` | High-signal coordination point for runtime event flow. |
| `WriteAheadLog` | storage | `Meridian.Storage` | Authoritative WAL implementation for durability and storage integrity work. |
| `AtomicFileWriter` | storage | `Meridian.Storage` | Crash-safe file write boundary used by storage-sensitive changes. |
| `IOrderGateway` | interface | `Meridian.Execution` | Primary execution abstraction for routing order-flow investigations. |
| `IRiskRule` | interface | `Meridian.Risk` | Key contract for pre-trade risk validation work. |
| `IStrategyLifecycle` | interface | `Meridian.Strategies` | Primary lifecycle abstraction for strategy run work. |
| `MainWindow` | wpf-shell | `Meridian.Wpf` | Desktop shell entrypoint for WPF workflow and navigation issues. |
| `LedgerTools` | mcp-tools | `Meridian.Mcp` | Read-only MCP tools for run ledgers (runId) and posted books (periodId); they call the workstation API. |
| `Program` | mcp-entrypoint | `Meridian.Mcp` | Registration point for MCP tools, resources, and prompts. |

## Dependency Highlights

| From | To | Why it matters |
|---|---|---|
| `Meridian` | `Meridian.Application` | Meridian references Meridian.Application directly via project reference. |
| `Meridian` | `Meridian.Backtesting` | Meridian references Meridian.Backtesting directly via project reference. |
| `Meridian` | `Meridian.Contracts` | Meridian references Meridian.Contracts directly via project reference. |
| `Meridian` | `Meridian.Core` | Meridian references Meridian.Core directly via project reference. |
| `Meridian` | `Meridian.Domain` | Meridian references Meridian.Domain directly via project reference. |
| `Meridian` | `Meridian.Infrastructure` | Meridian references Meridian.Infrastructure directly via project reference. |
| `Meridian` | `Meridian.ProviderSdk` | Meridian references Meridian.ProviderSdk directly via project reference. |
| `Meridian` | `Meridian.QuantScript` | Meridian references Meridian.QuantScript directly via project reference. |
| `Meridian` | `Meridian.Storage` | Meridian references Meridian.Storage directly via project reference. |
| `Meridian` | `Meridian.Ui.Services` | Meridian references Meridian.Ui.Services directly via project reference. |
| `Meridian` | `Meridian.Ui.Shared` | Meridian references Meridian.Ui.Shared directly via project reference. |
| `Meridian.Application` | `Meridian.Contracts` | Meridian.Application references Meridian.Contracts directly via project reference. |
| `Meridian.Application` | `Meridian.Core` | Meridian.Application references Meridian.Core directly via project reference. |
| `Meridian.Application` | `Meridian.Domain` | Meridian.Application references Meridian.Domain directly via project reference. |
| `Meridian.Application` | `Meridian.FSharp` | Meridian.Application references Meridian.FSharp directly via project reference. |
| `Meridian.Application` | `Meridian.FSharp.DirectLending.Aggregates` | Meridian.Application references Meridian.FSharp.DirectLending.Aggregates directly via project reference. |
| `Meridian.Application` | `Meridian.FSharp.Ledger` | Meridian.Application references Meridian.FSharp.Ledger directly via project reference. |
| `Meridian.Application` | `Meridian.Infrastructure` | Meridian.Application references Meridian.Infrastructure directly via project reference. |
| `Meridian.Application` | `Meridian.Ledger` | Meridian.Application references Meridian.Ledger directly via project reference. |
| `Meridian.Application` | `Meridian.ProviderSdk` | Meridian.Application references Meridian.ProviderSdk directly via project reference. |

## What Changed Recently

Recent source-file activity from the last 14 days.

| File | Subsystem | Last commit | Touches |
|---|---|---|---|
| `src/Meridian.Mcp/README.md` | MCP Integration | `9bf7b131` (2026-11-25T19:50:44+00:00) | 2 |
| `src/Meridian.Mcp/Resources/AdrResources.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Services/RepoPathService.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Tools/AdrTools.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Tools/AuditTools.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Tools/ProviderTools.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Tools/RepoEditTools.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 2 |
| `src/Meridian.Mcp/Tools/PythonLauncher.cs` | MCP Integration | `1ca2a0eb` (2026-11-25T19:34:30+00:00) | 1 |
| `src/Meridian.Ui.Services/README.md` | Desktop and UI Workflows | `464f53d7` (2026-11-25T12:10:16-07:00) | 4 |
| `src/Meridian.Ui.Services/Services/HttpClientConfiguration.cs` | Desktop and UI Workflows | `464f53d7` (2026-11-25T12:10:16-07:00) | 3 |
| `src/Meridian.Ui/dashboard/README.md` | Desktop and UI Workflows | `dc55ac89` (2026-11-25T09:31:55-07:00) | 24 |
| `src/Meridian.Ui.Shared/README.md` | Desktop and UI Workflows | `57f8f306` (2026-11-25T07:27:41-07:00) | 15 |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | Desktop and UI Workflows | `57f8f306` (2026-11-25T07:27:41-07:00) | 10 |
| `src/Meridian.Application/Backfill/BackfillCoordinator.cs` | Host and Composition | `57f8f306` (2026-11-25T07:27:41-07:00) | 6 |
| `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs` | Providers and Storage | `57f8f306` (2026-11-25T07:27:41-07:00) | 6 |

