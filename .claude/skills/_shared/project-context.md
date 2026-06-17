# Meridian — Shared Project Context

> **Canonical reference.** Update this file first when Meridian's product framing, workstation
> routing, runtime semantics, or key architecture guidance changes; mirrored Codex and GitHub AI
> surfaces should follow from here.
>
> **Last verified:** 2026-06-16
> **Primary grounding docs:** `README.md`, `docs/roadmap/data/*.yml`,
> `docs/roadmap/generated/ROADMAP_SUMMARY.md`, `docs/product/meridian-design-document.md`,
> `docs/architecture/meridian-development-intelligence-framework.md`

---

## Platform Snapshot

- Meridian is a .NET 10 fund-management and trading-platform codebase in active delivery.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- The current delivery focus is productization around the closed W1-W5 operational record baseline:
  data confidence, retained source evidence, reconciliation, approvals, accounting records,
  multi-asset operational coverage, and governed reports across `Trading`, `Portfolio`,
  `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- The active W5X productization targets are the shared Financial Record Explorers and the Financial
  Operations control center for reconciliation, exception management, close support, workflow
  control, and audit evidence.
- Defer Backtesting Studio, live-readiness beyond paper-first governance, full treasury payment
  execution, full alternative asset operations, forecasting/scenario engines, enterprise risk,
  client portal, no-code workflow design, mobile, and other broad expansion lanes unless the work
  directly strengthens that operational record workflow.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- The authoritative local checkout path for this workspace is `D:\Meridian-main`.
- Active operator UI work spans `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/`.
- `src/Meridian.Wpf/` is again a first-class Windows desktop operator surface for workstation
  workflows, launch automation, and desktop validation.
- `src/Meridian.Ui/dashboard/` remains an active browser-based workstation lane, with production
  assets built into `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` provide shared API/read-model layers
  that should support both desktop and browser surfaces without duplicating business logic.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation is allowed only to keep the browser
  workstation usable at supported viewport sizes.
- Keep top-level operator navigation to seven workspaces: `Trading`, `Portfolio`, `Accounting`,
  `Reporting`, `Strategy`, `Data`, and `Settings`. Legacy `Research`, `Data Operations`, and
  `Governance` names remain compatibility aliases, not visible root workspaces.

---

## Planning Source Of Truth

Read these before changing skills, agents, or workflow guidance:

- `README.md`
- `docs/roadmap/README.md`
- `docs/roadmap/data/*.yml`
- `docs/roadmap/generated/ROADMAP_SUMMARY.md`
- `docs/roadmap/generated/roadmap-register.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/meridian-development-intelligence-framework.md`
- `docs/architecture/meridian-vision.md`
- `docs/architecture/meridian-domain-model.md`
- `docs/domain/README.md`
- `docs/ai/context/README.md`
- `docs/product/README.md`
- `docs/status/README.md` for migration routing only

---

## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh ./scripts/dev/test-wpf-dev.ps1
gh workflow run targeted-test.yml --ref <branch> -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the touched files.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. The .NET lane requires a repo-relative test project under
`tests/` plus `dotnet_filter`.

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
- `src/Meridian.Ui/dashboard/`: browser-based operator workstation
- `src/Meridian.Ui/wwwroot/workstation/`: built web workstation assets served by `Meridian.Ui`
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, `src/Meridian.Wpf/`: shared UI
  services, workstation endpoints, and the WPF desktop shell
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
