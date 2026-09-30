# Meridian — Shared Project Context

> Mirror of the canonical `.claude/skills/_shared/project-context.md` for Agent Skills-compatible
> hosts.
>
> **Last verified:** 2026-09-28
> **Primary grounding docs:** `README.md`, `docs/roadmap/data/*.yml`,
> `docs/roadmap/generated/ROADMAP_SUMMARY.md`, `docs/product/meridian-design-document.md`,
> `docs/architecture/meridian-development-intelligence-framework.md`
>
> The section between the `shared-context` markers is identical in all three copies:
> `.claude/skills/_shared/project-context.md` (canonical, edit it first),
> `.codex/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`.
> Copy any change to the other two; `build/scripts/docs/check-ai-inventory.py` fails when they
> diverge. Host-specific guidance goes after the shared section.

<!-- shared-context:begin — identical in .claude/, .codex/, and .agents/ copies; check-ai-inventory.py enforces it -->

## Platform Snapshot

- Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery; fund
  management is a first-class specialization, not the root model for every workflow.
- The repo already contains strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- Current delivery direction is evidence-led: use current source, the roadmap registry, and the
  design charter to decide whether a prior baseline, named productization target, or later expansion
  lane is the right scope.
- Treat prior baselines and named productization targets as roadmap/status evidence, not development
  ceilings.
- Expansion lanes such as Backtesting Studio, live-readiness, treasury payment execution,
  alternative asset operations, forecasting/scenario engines, enterprise risk, client portal, and
  no-code workflow design can proceed when current source, roadmap, or user direction supports them.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- Operator UI work runs across two active co-equal lanes: the browser workstation in
  `src/Meridian.Ui/dashboard/` and the reactivated WPF desktop workstation in `src/Meridian.Wpf/`.
- `src/Meridian.Wpf/` is an active product/UI lane; its immediate focus is web-UI parity
  (`W8-WPF-PARITY-001`, see `docs/engineering/plans/wpf-web-ui-alignment-plan.md`) over the existing
  Windows desktop shell, compatibility, tests, launch automation, and desktop validation.
- `src/Meridian.Ui/dashboard/` remains an active browser-based workstation lane, with production
  assets built into `src/Meridian.Ui/wwwroot/workstation/`.
- `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` provide shared API/read-model layers
  that should support the browser workstation and retained WPF compatibility without duplicating
  business logic.
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
- `docs/README.md`
- `docs/roadmap/README.md`
- `docs/roadmap/data/*.yml`
- `docs/roadmap/generated/ROADMAP_SUMMARY.md`
- `docs/roadmap/generated/roadmap-register.md`
- `docs/product/meridian-design-document.md`
- `docs/product/README.md`
- `docs/engineering/blueprints/README.md` (the single Plans and Blueprints Register)
- `docs/architecture/meridian-development-intelligence-framework.md`
- `docs/architecture/meridian-vision.md`
- `docs/architecture/meridian-domain-model.md`
- `docs/architecture/project-structure.md`
- `docs/architecture/module-map.md`
- `docs/architecture/mvvm-guidelines.md`
- `docs/domain/README.md`
- `docs/ai/context/README.md`
- `docs/prompts/repo-maintenance-prompts.md`
- `docs/status/README.md` for migration routing only

---

## Source Documentation Mesh

- Active documentation is organized around `docs/start/`, `docs/product/`, `docs/engineering/`,
  `docs/operators/`, `docs/ai/`, `docs/roadmap/`, `docs/source/`, `docs/reference/`, and
  `docs/generated/`. Treat `docs/status/`, `docs/development/`, and `docs/operations/` as canonical
  only when linked from those indexes or registry-owned workflows. `docs/plans/` holds redirect stubs
  only; every plan is listed in the Plans and Blueprints Register.
- Roadmap truth lives in `docs/roadmap/data/*.yml`; generated roadmap views live in
  `docs/roadmap/generated/`.
- Source/module truth lives in `docs/source/data/*.yml`; registered modules have local
  `src/**/README.md` files with purpose, ownership, diagrams, roadmap traceability, TODOs, and
  validation commands.
- Before editing `src/**`, read the nearest source README, identify the module ID in
  `docs/source/data/source-modules.yml`, and update source README or registry records when behavior,
  validation, ownership, diagrams, or TODO scope changes.
- Do not hand-edit generated roadmap/source docs. Update registry data or renderers under
  `build/scripts/docs/`, then rerun the narrow generator.
- Use `python3 build/scripts/docs/mark-stale-docs.py --write --summary` to mark registered modules
  whose code or README hashes need documentation review.
- Use `python3 build/scripts/docs/validate-doc-hashes.py --summary` to detect code/docs drift for
  registered modules. Refresh reviewed stale module entries with
  `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary`;
  reserve broad `--write --summary` for a full accepted-baseline review.

---

## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
bash scripts/ci.sh
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the touched files.
For completed PR-ready work, use `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate`
is the authoritative merge result. Local work may happen on `main` when the user explicitly requests
it or the checkout is intentionally operating there, but PR-ready publishing should flow through a
`codex/<short-task-name>` branch and PR targeting `main` unless GitHub repository rules explicitly
allow the requested protected-branch flow.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. Select a whitelisted `mode`; for .NET slices, use
`mode=dotnet-filtered` with a repo-relative test project under `tests/` plus `dotnet_filter`.
After a timed-out generation, build, or test attempt, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

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
- `src/Meridian.QuantScript/`: strategy analytics scripting and charting-oriented tooling
- `src/Meridian.Mcp/`: active MCP host, tools, and resources
- `src/Meridian.Ui/dashboard/`: browser-based operator workstation
- `src/Meridian.Ui/wwwroot/workstation/`: built web workstation assets served by `Meridian.Ui`
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, `src/Meridian.Wpf/`: shared UI
  services, workstation endpoints, and the active WPF desktop shell
- `tests/`: cross-platform, F#, UI-service, and WPF test projects
- `benchmarks/`: BenchmarkDotNet performance suites

The retired Chief of Staff runtime and `Meridian.McpServer` are not part of the tree: their archive
copies were removed by the 2026-09-11 archive cleanup (`982eea2d`) and are recoverable from commit
`8a420730`. Do not route new implementation work to them.

---

## Verified Entry Points

- Main host: `src/Meridian/Meridian.csproj`
- Minimal MCP host: `src/Meridian.Mcp/Meridian.Mcp.csproj`
- Web workstation dashboard: `src/Meridian.Ui/dashboard`
- Host-served workstation route: `http://localhost:8080/workstation/`
- Active WPF desktop workstation: `src/Meridian.Wpf/Meridian.Wpf.csproj`

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
- Provider credentials saved by browser workstation flows use the shared encrypted
  `IProviderCredentialStore` under the resolved data root; environment variables are read-only
  legacy fallback and new flows must not write provider secrets to user-level env vars.
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
- `src/Meridian.Wpf/Shell/`: retained WPF shell route, launch, session, refresh, and presentation seams
- `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`: retained WPF desktop shell view model and
  workstation navigation anchor

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

CI/CD ownership: Meridian CI runs the four canonical `scripts/ci.sh` lanes; legacy CI
runs Secret Scan and nightly/manual coverage. See `docs/engineering/ci-cd-optimization.md`
for measurement gates, human governance review, and the separate administrator rollout.

<!-- shared-context:end -->
