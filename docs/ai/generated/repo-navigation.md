# Meridian AI Repo Navigation

> Auto-generated on 2026-06-25T02:00:53Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

## Quick Start

Use this file when an assistant needs fast orientation before reading subsystem-specific guidance.

| Task shape | Start here | Authoritative docs |
|---|---|---|
| Provider implementation and provider bugs | `Meridian.ProviderSdk`, `Meridian.Infrastructure`, `Meridian.Storage` | `docs/ai/claude/CLAUDE.providers.md`, `docs/development/provider-implementation.md`, `docs/ai/ai-known-errors.md` |
| Browser workstation and dashboard UI issues | `Meridian.Ui.Dashboard`, `Meridian.Ui.Services`, `Meridian.Ui.Shared` | `docs/ai/navigation/README.md`, `docs/ai/ai-known-errors.md` |
| WPF and workstation workflow issues | `Meridian.Wpf`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian` | `docs/plans/trading-workstation-migration-blueprint.md`, `docs/ai/ai-known-errors.md` |
| Storage and WAL investigations | `Meridian.Storage`, `Meridian.Application` | `docs/ai/claude/CLAUDE.storage.md`, `docs/ai/ai-known-errors.md` |
| MCP tools, prompts, and resources | `Meridian.Mcp` | `docs/ai/navigation/README.md`, `docs/ai/README.md` |

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
| `Program` | mcp-entrypoint | `Meridian.Mcp` | Registration point for MCP tools, resources, and prompts. |

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
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | Desktop and UI Workflows | `635d1167c` (2026-06-24T16:15:05Z) | 13 |
| `src/Meridian.Ui/dashboard/src/components/meridian/report-writer-grid-diff-view.tsx` | Desktop and UI Workflows | `a4d453a7e` (2026-06-24T15:33:27Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/report-writer-grid-diff.test.ts` | Desktop and UI Workflows | `a4d453a7e` (2026-06-24T15:33:27Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/report-writer-grid-diff.ts` | Desktop and UI Workflows | `a4d453a7e` (2026-06-24T15:33:27Z) | 1 |
| `src/Meridian.Ui/dashboard/src/components/meridian/report-writer-chart-preview.tsx` | Desktop and UI Workflows | `d2ba8b1cf` (2026-06-24T15:28:29Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/report-writer-grid-format.test.ts` | Desktop and UI Workflows | `d2ba8b1cf` (2026-06-24T15:28:29Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/report-writer-grid-format.ts` | Desktop and UI Workflows | `d2ba8b1cf` (2026-06-24T15:28:29Z) | 1 |
| `src/Meridian.Ui/dashboard/src/components/meridian/reporting-hub.tsx` | Desktop and UI Workflows | `fd1591527` (2026-06-24T15:22:11Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/reporting-hub.test.ts` | Desktop and UI Workflows | `fd1591527` (2026-06-24T15:22:11Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/reporting-hub.ts` | Desktop and UI Workflows | `fd1591527` (2026-06-24T15:22:11Z) | 1 |
| `src/Meridian.Ui/dashboard/src/components/meridian/reporting-period-switcher.tsx` | Desktop and UI Workflows | `a188e17bc` (2026-06-24T15:15:41Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/reporting-periods.test.ts` | Desktop and UI Workflows | `a188e17bc` (2026-06-24T15:15:41Z) | 1 |
| `src/Meridian.Ui/dashboard/src/lib/reporting-periods.ts` | Desktop and UI Workflows | `a188e17bc` (2026-06-24T15:15:41Z) | 1 |
| `src/Meridian.Ui/dashboard/src/types.ts` | Desktop and UI Workflows | `e4921294b` (2026-06-24T15:08:16Z) | 57 |
| `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs` | Host and Composition | `e4921294b` (2026-06-24T15:08:16Z) | 8 |

