# Meridian AI Repo Navigation

> Auto-generated on 2026-05-31T12:05:55Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

## Quick Start

Use this file when an assistant needs fast orientation before reading subsystem-specific guidance.

| Task shape | Start here | Authoritative docs |
|---|---|---|
| Provider implementation and provider bugs | `Meridian.ProviderSdk`, `Meridian.Infrastructure`, `Meridian.Storage` | `docs/ai/claude/CLAUDE.providers.md`, `docs/development/provider-implementation.md`, `docs/ai/ai-known-errors.md` |
| Browser workstation and dashboard UI issues | `Meridian.Ui.Dashboard`, `Meridian.Ui.Services`, `Meridian.Ui.Shared` | `docs/ai/navigation/README.md`, `docs/ai/ai-known-errors.md` |
| WPF and workstation workflow issues | `Meridian.Wpf`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian` | `docs/plans/trading-workstation-migration-blueprint.md`, `docs/ai/ai-known-errors.md` |
| Storage and WAL investigations | `Meridian.Storage`, `Meridian.Application` | `docs/ai/claude/CLAUDE.storage.md`, `docs/ai/ai-known-errors.md` |
| MCP tools, prompts, and resources | `Meridian.McpServer`, `Meridian.Mcp` | `docs/ai/navigation/README.md`, `docs/ai/README.md` |

## Subsystems

### Host and Composition

Runtime startup, application composition, shared contracts, and cross-cutting infrastructure.

- Projects: `Meridian`, `Meridian.Application`, `Meridian.Contracts`, `Meridian.Core`
- Entrypoints: `src/Meridian.Application/Composition`, `src/Meridian.Application/Pipeline`, `src/Meridian.Contracts`, `src/Meridian.Core/Serialization`
- Key contracts: `src/Meridian.Application/Pipeline/EventPipeline.cs`, `src/Meridian.Contracts`, `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`, `src/Meridian/Program.cs`
- Common tasks: startup debugging, service composition, configuration, shared contracts
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`

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
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`, `docs/plans/trading-workstation-migration-blueprint.md`

### Backtesting and Research

Replay engine, backtesting SDK, and quant research workflows.

- Projects: `Meridian.Backtesting`, `Meridian.Backtesting.Sdk`, `Meridian.QuantScript`
- Entrypoints: `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript`
- Key contracts: `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `src/Meridian.QuantScript`
- Common tasks: backtesting bug, simulation, research scripting
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
- Entrypoints: `src/Meridian.Mcp/Program.cs`
- Key contracts: `src/Meridian.Mcp/Program.cs`
- Common tasks: mcp work, new mcp tool, resource routing
- Related docs: `docs/ai/README.md`, `docs/ai/ai-known-errors.md`, `docs/ai/navigation/README.md`

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

## Dependency Highlights

| From | To | Why it matters |
|---|---|---|
| `Meridian` | `Meridian.Application` | Meridian references Meridian.Application directly via project reference. |
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
| `Meridian.Application` | `Meridian.Storage` | Meridian.Application references Meridian.Storage directly via project reference. |

## What Changed Recently

Recent source-file activity from the last 14 days.

| File | Subsystem | Last commit | Touches |
|---|---|---|---|
| `src/Meridian.Infrastructure/Adapters/Templates/TemplateBrokerageGateway.cs` | Providers and Storage | `33dd6fa9d` (2026-05-31T03:54:29-07:00) | 3 |
| `src/Meridian.Wpf/Services/ModelRoutingPolicyValidator.cs` | Desktop and UI Workflows | `aae51bdea` (2026-05-31T03:46:52-07:00) | 1 |
| `src/Meridian.Wpf/App.xaml.cs` | Desktop and UI Workflows | `50d11b246` (2026-05-31T03:37:12-07:00) | 14 |
| `src/Meridian.Wpf/Views/Pages.cs` | Desktop and UI Workflows | `50d11b246` (2026-05-31T03:37:12-07:00) | 1 |
| `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 8 |
| `src/Meridian.Wpf/Styles/ThemeControls.xaml` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 3 |
| `src/Meridian.Wpf/Views/ApiKeyDialog.xaml` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 2 |
| `src/Meridian.Wpf/Views/EditScheduledJobDialog.xaml` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 2 |
| `src/Meridian.Wpf/Views/SaveWatchlistDialog.xaml` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 2 |
| `src/Meridian.Wpf/Views/WorkspaceDialogChromeControl.xaml.cs` | Desktop and UI Workflows | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 2 |
| `src/Meridian.Contracts/Domain/Enums/InstrumentType.cs` | Host and Composition | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 1 |
| `src/Meridian.Contracts/Domain/Enums/LiquidityProfile.cs` | Host and Composition | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 1 |
| `src/Meridian.Contracts/Domain/Enums/OptionRight.cs` | Host and Composition | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 1 |
| `src/Meridian.Contracts/Domain/Enums/OptionStyle.cs` | Host and Composition | `4a9ed0ef3` (2026-05-30T01:22:56-07:00) | 1 |
| `src/Meridian.Application/README.md` | Host and Composition | `b8171ed82` (2026-05-29T23:27:51-07:00) | 34 |

