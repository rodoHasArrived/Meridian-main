# Meridian AI Repo Navigation

> Auto-generated on 2026-04-01T22:22:59Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

## Quick Start

Use this file when an assistant needs fast orientation before reading subsystem-specific guidance.

| Task shape | Start here | Authoritative docs |
|---|---|---|
| Provider implementation and provider bugs | `Meridian.ProviderSdk`, `Meridian.Infrastructure`, `Meridian.Storage` | `docs/ai/claude/CLAUDE.providers.md`, `docs/development/provider-implementation.md`, `docs/ai/ai-known-errors.md` |
| WPF and workstation workflow issues | `Meridian.Wpf`, `Meridian.Ui.Services`, `Meridian.Ui.Shared` | `docs/plans/trading-workstation-migration-blueprint.md`, `docs/ai/ai-known-errors.md` |
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

- Projects: `Meridian.Ui`, `Meridian.Ui.Services`, `Meridian.Ui.Shared`, `Meridian.Wpf`
- Entrypoints: `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, `src/Meridian.Ui/Program.cs`, `src/Meridian.Wpf/App.xaml.cs`
- Key contracts: `src/Meridian.Ui`, `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, `src/Meridian.Wpf/App.xaml.cs`
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

- Projects: `Meridian.Mcp`, `Meridian.McpServer`
- Entrypoints: `src/Meridian.Mcp/Program.cs`, `src/Meridian.McpServer/Program.cs`, `src/Meridian.McpServer/Prompts`, `src/Meridian.McpServer/Resources`
- Key contracts: `src/Meridian.Mcp/Program.cs`, `src/Meridian.McpServer/Program.cs`
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
| `Program` | mcp-entrypoint | `Meridian.McpServer` | Registration point for MCP tools, resources, and prompts. |

## Dependency Highlights

| From | To | Why it matters |
|---|---|---|

