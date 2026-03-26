# Meridian Shared Project Context

Use this file as the common source of truth for Meridian-specific terminology, commands, and architecture when a skill needs repository grounding without repeating the same facts in every `SKILL.md`.

## Platform Snapshot

- Meridian is a self-hosted trading platform built on .NET 9, C# 13, and F# 8.
- The platform spans four connected pillars: data collection, backtesting, execution, and strategy lifecycle management.
- The active product direction is a workflow-centric trading workstation that unifies research, trading, data operations, and governance surfaces.
- The repository includes the web UI, provider adapters, the backtesting engine, execution/paper trading, ledger support, MCP server layers, and shared UI services.

## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
make test
make ai-verify
python3 build/scripts/ai-repo-updater.py known-errors
```

Prefer the narrowest validation command that matches the files being changed.

## Solution Layout

- `src/Meridian/`: host entry point, CLI, UI server
- `src/Meridian.Application/`: application services, orchestration, pipeline
- `src/Meridian.Contracts/`: DTOs and cross-project contracts
- `src/Meridian.Core/`: configuration, exceptions, logging, serialization
- `src/Meridian.Domain/`: collectors, events, core domain logic
- `src/Meridian.FSharp/`: F# domain models and calculations
- `src/Meridian.Infrastructure/`: provider adapters, resilience, HTTP integration
- `src/Meridian.ProviderSdk/`: provider-facing contracts such as `IMarketDataClient`
- `src/Meridian.Storage/`: WAL, sinks, archival, packaging
- `src/Meridian.Backtesting/`, `src/Meridian.Backtesting.Sdk/`: replay engine and strategy SDK
- `src/Meridian.Execution/`, `src/Meridian.Execution.Sdk/`: execution and broker gateway abstractions
- `src/Meridian.Ledger/`: double-entry accounting ledger
- `src/Meridian.Risk/`: pre-trade risk validation
- `src/Meridian.Strategies/`: strategy lifecycle and run storage
- `src/Meridian.Ui/`, `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`: web UI and shared UI services
- `src/Meridian.Wpf/`: WPF desktop app — included in solution build (full WPF on Windows, CI stub on Linux/macOS)
- `tests/`: cross-platform, F#, UI-service, and WPF test projects (all included in solution; Meridian.Wpf.Tests builds full tests on Windows, empty stub on Linux/macOS)

## Key Abstractions

- `src/Meridian.ProviderSdk/IMarketDataClient.cs`: streaming provider contract
- `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`: historical/backfill provider contract
- `src/Meridian.Storage/Interfaces/IStorageSink.cs`: persistence sink contract
- `src/Meridian.Application/Pipeline/EventPipeline.cs`: hot-path channel coordinator
- `src/Meridian.Storage/Archival/WriteAheadLog.cs`: WAL durability
- `src/Meridian.Storage/Archival/AtomicFileWriter.cs`: crash-safe file writes
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: source-generated JSON context
- `src/Meridian.Execution/Interfaces/IOrderGateway.cs`: order routing abstraction
- `src/Meridian.Risk/IRiskRule.cs`: pre-trade rule contract
- `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`: run lifecycle contract

## Review Guardrails

- Use `CancellationToken` on async methods.
- Use structured logging, not string interpolation in log calls.
- Use `IOptionsMonitor<T>` for runtime-mutable configuration.
- Use ADR-014 source-generated JSON serialization.
- Use `EventPipelinePolicy.*.CreateChannel<T>()`, not ad hoc unbounded channels.
- Route durable storage through WAL or `AtomicFileWriter`, not direct file writes.
- Do not add package versions directly to project files; central package management lives in `Directory.Packages.props`.

## Migration Context

When a task touches user workflows, align with the trading workstation migration blueprint:

- Research workspace
- Trading workspace
- Data Operations workspace
- Governance workspace

Reference: `docs/plans/trading-workstation-migration-blueprint.md`
