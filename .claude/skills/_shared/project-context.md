# Meridian — Shared Project Context

> **Canonical reference.** Update this file first when Meridian's product framing, workstation
> routing, runtime semantics, or key architecture guidance changes; mirrored Codex and GitHub AI
> surfaces should follow from here.
>
> **Last verified:** 2026-04-13
> **Primary grounding docs:** `README.md`, `docs/status/ROADMAP.md`,
> `docs/plans/trading-workstation-migration-blueprint.md`,
> `docs/plans/governance-fund-ops-blueprint.md`

---

## Platform Snapshot

- Meridian is a .NET 9 fund-management and trading-platform codebase in active delivery.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- The current delivery problem is product-shaped rather than primitive-shaped: prove operator
  trust, close workflow gaps, and deepen governance/fund-operations without splitting the product
  into parallel subsystems.
- `src/Meridian.Wpf/` is the primary operator shell. `src/Meridian.Ui.Services/` and
  `src/Meridian.Ui.Shared/` are the shared desktop-facing service and endpoint layers that support
  the local workstation experience.
- `src/Meridian.Ui/` remains a supporting web/API surface, but new operator workflow guidance
  should optimize for the desktop shell first.
- Keep the top-level workstation to four workspaces: `Research`, `Trading`, `Data Operations`, and
  `Governance`. Fund-ops, banking, portfolio, and ledger expansions should surface as Governance
  aliases, tabs, or command-palette routes instead of brand-new root workspaces.

---

## Planning Source Of Truth

Read these before changing skills, agents, or workflow guidance:

- `README.md`
- `docs/status/ROADMAP.md`
- `docs/status/FEATURE_INVENTORY.md`
- `docs/status/IMPROVEMENTS.md`
- `docs/status/production-status.md`
- `docs/plans/trading-workstation-migration-blueprint.md`
- `docs/plans/governance-fund-ops-blueprint.md`
- `docs/plans/meridian-6-week-roadmap.md`

---

## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
make test
make desktop-run
pwsh ./scripts/dev/run-desktop.ps1
python3 build/scripts/ai-repo-updater.py known-errors
```

Prefer the narrowest validation command that matches the touched files.

---

## Solution Map

- `src/Meridian/`: primary host entry point, CLI, desktop-local API host
- `src/Meridian.Application/`: orchestration, pipeline, commands, config
- `src/Meridian.Contracts/`: DTOs and cross-project contracts
- `src/Meridian.Core/`: configuration, exceptions, logging, serialization
- `src/Meridian.Domain/`: collectors, events, domain logic
- `src/Meridian.FSharp/`: F# domain models and calculations
- `src/Meridian.Infrastructure/`: provider adapters, resilience, HTTP integration
- `src/Meridian.ProviderSdk/`: provider-facing contracts such as `IMarketDataClient`
- `src/Meridian.Storage/`: WAL, sinks, archival, lineage, packaging
- `src/Meridian.Backtesting/`, `src/Meridian.Backtesting.Sdk/`: replay and backtesting SDK
- `src/Meridian.Execution/`, `src/Meridian.Execution.Sdk/`: execution and broker abstractions
- `src/Meridian.Ledger/`, `src/Meridian.FSharp.Ledger/`: ledger and accounting surfaces
- `src/Meridian.Risk/`: pre-trade risk validation
- `src/Meridian.Strategies/`: strategy lifecycle, run storage, shared read models
- `src/Meridian.QuantScript/`: scripting and charting-oriented tooling
- `src/Meridian.Mcp/`, `src/Meridian.McpServer/`: MCP hosts, tools, and resources
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, `src/Meridian.Wpf/`: shared UI
  services, workstation endpoints, and the WPF shell
- `tests/`: cross-platform, F#, UI-service, and WPF test projects
- `benchmarks/`: performance suites

---

## Desktop Persistence Baseline

- Installed WPF builds store runtime config at `%LocalAppData%\\Meridian\\appsettings.json`; the
  repo-local `config/appsettings.json` path is the normal CLI, server, and development config
  surface.
- Relative `DataRoot` values resolve from the active config file base via
  `MeridianPathDefaults.ResolveDataRoot`, not from the executable directory.
- `Storage.BaseDirectory` is legacy migration input only; new code and docs should prefer top-level
  `DataRoot`.
- Desktop-retained artifacts such as workspace state, watchlists, credentials, activity logs,
  collection sessions, symbol mappings, schema dictionaries, and catalog metadata should stay under
  the resolved external config and data roots so upgrades do not depend on the install directory.
- Wizard review/save flows should use `AppConfigJsonOptions` plus `ConfigStore` so previewed JSON
  and persisted config share the same serializer and resolved config path.
- Paper-session order history is lifecycle-sensitive metadata; await the durable append before
  treating an order update as committed.

---

## Key Abstractions

- `src/Meridian.ProviderSdk/IMarketDataClient.cs`: streaming provider contract
- `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`: historical/backfill
  provider contract
- `src/Meridian.Storage/Interfaces/IStorageSink.cs`: persistence sink contract
- `src/Meridian.Application/Pipeline/EventPipeline.cs`: hot-path channel coordinator
- `src/Meridian.Storage/Archival/WriteAheadLog.cs`: WAL durability
- `src/Meridian.Storage/Archival/AtomicFileWriter.cs`: crash-safe file writes
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: source-generated JSON context
- `src/Meridian.Execution/Interfaces/IOrderGateway.cs`: order routing abstraction
- `src/Meridian.Risk/IRiskRule.cs`: pre-trade rule contract
- `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`: strategy lifecycle contract
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`: shared run read-model seam
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`: shared workstation surface
- `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`: current shell orchestration anchor

---

## Review Guardrails

- Preserve `CancellationToken`, nullability, and async flow.
- Use structured logging, not string interpolation inside log calls.
- Use `IOptionsMonitor<T>` for runtime-mutable configuration.
- Use ADR-014 source-generated JSON serialization.
- Use `EventPipelinePolicy.*.CreateChannel<T>()`, not ad hoc channels.
- Route durable storage through WAL or `AtomicFileWriter`, not direct file writes.
- Avoid constructor sync-over-async and fire-and-forget persistence on lifecycle-sensitive
  services; await initialization and terminal metadata writes at the service boundary.
- Do not add package versions directly to project files; central package management lives in
  `Directory.Packages.props`.

