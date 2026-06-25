# Meridian Shared Project Context

> Last verified: 2026-06-20
> Canonical companions: `CLAUDE.md`, `docs/ai/assistant-workflow-contract.md`,
> `docs/product/meridian-design-document.md`,
> `docs/architecture/meridian-development-intelligence-framework.md`, and
> `docs/architecture/project-structure.md`

Use this file as the common source of truth for Meridian-specific terminology, current product
direction, commands, and architecture when a Codex skill needs repository grounding without
repeating the same facts in every `SKILL.md`.

## Platform Snapshot

- Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery; fund management is a first-class specialization, not the root model for every workflow.
- The authoritative local checkout path for this workspace is `D:\Meridian-main`.
- The repo already includes strong provider, storage, replay, backtesting, execution, ledger,
  QuantScript, MCP, and workstation foundations.
- Current delivery direction is evidence-led: use current source, the roadmap registry, and the
  design charter to decide whether a prior baseline, named productization target, or later expansion
  lane is the right scope.
- Treat prior baselines and named productization targets as roadmap/status evidence, not development
  ceilings.
- MDIF is the required context spine for broad generation, domain modeling, workflow design, and
  architecture-sensitive refactors: load the MDIF framework, vision, domain model, relevant domain
  dictionary pages, and context packs before implementation.
- Expansion lanes such as Backtesting Studio, live-readiness, treasury payment execution,
  alternative asset operations, forecasting/scenario engines, enterprise risk, client portal, and
  no-code workflow design can proceed when current source, roadmap, or user direction supports them.
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

## Planning Anchors

Use these together before changing AI guidance, routing, or workflow-oriented skills:

- `AGENTS.md`
- `CLAUDE.md`
- `.codex/AGENTS.md`
- `.codex/skills/_shared/codex-execution-contract.md`
- `docs/ai/codex/memory-system.md`
- `.codex/memory/index.yml`
- `.codex/memory/tasks/*.yml`
- `.codex/memory/goals/*.yml`
- `.github/copilot-instructions.md`
- `.github/agents/implementation-assurance-agent.md`
- `.github/workflows/README.md`
- `.claude/skills/_shared/project-context.md`
- `.agents/skills/_shared/project-context.md`
- `README.md`
- `docs/README.md`
- `docs/start/README.md`
- `docs/product/README.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/meridian-development-intelligence-framework.md`
- `docs/architecture/meridian-vision.md`
- `docs/architecture/meridian-domain-model.md`
- `docs/domain/README.md`
- `docs/ai/context/README.md`
- `docs/engineering/README.md`
- `docs/operators/README.md`
- `docs/documentation-ownership.md`
- `docs/architecture/project-structure.md`
- `docs/architecture/module-map.md`
- `docs/architecture/mvvm-guidelines.md`
- `docs/prompts/repo-maintenance-prompts.md`
- `docs/roadmap/README.md`
- `docs/roadmap/data/roadmap-items.yml`

## Codex Memory Routing

- For memory-aware Codex tasks, inspect `.codex/memory/index.yml` before loading durable memory.
- Use `.codex/memory/tasks/<task-id>.yml` descriptors when the task has a named scope, prompt
  family, issue, or planned-path boundary; use `.codex/memory/goals/<goal-id>.yml` inventories for
  long-running goals that need progress, evidence, blockers, next actions, and promotion candidates
  across compaction or continuation.
- Load only memory entries selected by the descriptor, current intent, selected skill, changed
  paths, branch, or explicit tags. Do not load the full memory store as startup context.
- Canonical docs, source code, tests, scripts, applicable `AGENTS.md` files, and selected
  `SKILL.md` files remain authoritative when memory is stale or conflicts.
- When memory routing is used, include a compact receipt with selected memory IDs, match reasons,
  stale warnings, active-goal progress count, and task or branch entries skipped because scope did
  not match.
- After changing `.codex/memory/index.yml`, indexed Markdown entries, task descriptors, goal
  inventories, or memory validation tooling, run `python build/scripts/docs/check-codex-memory.py
  --summary`; use `--task <descriptor> --receipt --summary` or `--goal <inventory> --receipt
  --summary` when validating task or goal routing.

## Source Documentation Mesh

- Active documentation is being rebuilt around `docs/start/`, `docs/product/`,
  `docs/engineering/`, `docs/operators/`, `docs/ai/`, `docs/roadmap/`, `docs/source/`,
  `docs/reference/`, and `docs/generated/`. Treat older hand-authored folders such as
  `docs/plans/`, `docs/status/`, `docs/developer/`, `docs/development/`,
  `docs/operations/`, `docs/evaluations/`, and `docs/audits/` as canonical only when linked from
  the rebuilt indexes or registry-owned workflows.
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
  whose code or README hashes need documentation review, then use `--stale-only` source README
  sync/render commands when only outdated docs should be touched.
- Use `python3 build/scripts/docs/validate-doc-hashes.py --summary` to detect code/docs drift for
  registered modules. Refresh reviewed stale module entries with
  `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary`;
  reserve broad `--write --summary` for a full accepted-baseline review.
- Source READMEs may include conditional sections for plans, end-user value, benchmarks and
  performance, operational evidence, security or credential handling, API/contract notes, and
  migration/archive notes when those sections add real module-specific context.

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
gh workflow run targeted-test.yml --ref <branch> -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the files being changed.
For completed PR-ready work, use `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate`
is the authoritative merge result and work should flow through a `codex/<short-task-name>` branch
and PR targeting `main`.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. The .NET lane requires a repo-relative test project under
`tests/` plus `dotnet_filter` to keep the remote run scoped to the failing slice.
After a timed-out generation, build, or test attempt, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

For Codex skill, catalog, prompt, docs-automation, or AI workflow changes, prefer this deterministic
validation stack before broad build or test runs:

```bash
python build/scripts/docs/check-codex-skills.py --summary
python build/scripts/docs/check-codex-memory.py --summary
python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
python -m unittest build.scripts.docs.tests.test_check_codex_memory
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/validate-skill-packages.py
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/scan-source-todos.py --summary
python build/scripts/docs/mark-stale-docs.py --write --summary
python build/scripts/docs/validate-doc-hashes.py --summary
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --json
git diff --check
```

If `make` is unavailable in the local Windows shell, run the underlying Python command directly and
report that the wrapper was not available.

## Codex Skill Routing

Use `.codex/skills/` as the canonical repo-local Codex skill set. Keep the public skill names stable
and choose the narrowest lane that matches the user's request:

| Lane | Skill | Boundary |
| --- | --- | --- |
| Orient | `meridian-repo-navigation` | Route the task, name owners and docs, then hand off. |
| Ideate | `meridian-brainstorm` | Generate options; do not turn one option into a spec. |
| Plan | `meridian-blueprint` | Turn one selected idea into a decision-complete technical design. |
| Implement or verify | `meridian-implementation-assurance` | Build or certify work with evidence, docs sync, and deterministic gates. |
| Review | `meridian-code-review` | Report bugs, regressions, missing tests, and architecture drift before summaries. |
| Test | `meridian-test-writer` | Add scenario-first tests in the right project and validation lane. |
| Provider | `meridian-provider-builder` | Build or extend provider adapters and hand off to assurance for rollout proof. |
| Archive | `meridian-archive-organizer` | Classify and move stale material with reference evidence. |
| Roadmap | `meridian-roadmap-strategist` | Reconcile product direction, waves, and target-state docs from repo evidence. |
| Cleanup | `meridian-cleanup` | Preserve behavior while removing dead code, duplication, or stale guidance. |
| Simulated user review | `meridian-simulated-user-panel` | Critique concrete artifacts with personas and separate verified evidence from inference. |
| CoS runtime / ADK | `cos-runtime-development` | Implement or extend CoS ADK nodes, HTTP host, MCP wiring, and runtime tests. |

## Solution Map

- `src/Meridian/`: primary host entry point, CLI, desktop-local API host
- `src/Meridian.Application/`: orchestration, pipeline, commands, config
- `src/Meridian.Contracts/`: DTOs and cross-project contracts
- `src/Meridian.Core/`: configuration, exceptions, logging, serialization
- `src/Meridian.Domain/`: collectors, events, core domain logic
- `src/Meridian.FSharp/`: F# domain models and calculations
- `src/Meridian.Infrastructure/`: provider adapters, resilience, HTTP integration
- `src/Meridian.ProviderSdk/`: provider-facing contracts such as `IMarketDataClient`
- `src/Meridian.Storage/`: WAL, sinks, archival, packaging, lineage
- `src/Meridian.Backtesting/`, `src/Meridian.Backtesting.Sdk/`: replay engine and strategy SDK
- `src/Meridian.Execution/`, `src/Meridian.Execution.Sdk/`: execution and broker abstractions
- `src/Meridian.Ledger/`, `src/Meridian.FSharp.Ledger/`: ledger and accounting surfaces
- `src/Meridian.Risk/`: pre-trade risk validation
- `src/Meridian.Strategies/`: strategy lifecycle, run storage, shared read models
- `src/Meridian.QuantScript/`: strategy analytics scripting and charting-oriented tooling
- `src/Meridian.Mcp/`, `src/Meridian.McpServer/`: MCP hosts, tools, and resources
- `tools/chief-of-staff-runtime/`: out-of-process Chief of Staff ADK node pipeline scaffold
- `src/Meridian.Ui/dashboard/`: active browser-based operator workstation dashboard
- `src/Meridian.Ui/wwwroot/workstation/`: built web workstation assets served by `Meridian.Ui`
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, `src/Meridian.Wpf/`: shared UI
  services, workstation endpoints, and the WPF shell
- `tests/`: cross-platform, F#, UI-service, and WPF test projects
- `benchmarks/`: BenchmarkDotNet performance suites

## Verified Entry Points

- Main host: `src/Meridian/Meridian.csproj`
- Minimal MCP host: `src/Meridian.Mcp/Meridian.Mcp.csproj`
- Market-data MCP host: `src/Meridian.McpServer/Meridian.McpServer.csproj`
- CoS runtime ADK scaffold: `tools/chief-of-staff-runtime/runtime.py` (implemented via `cos-runtime-development` Codex skill)
- Web workstation dashboard: `src/Meridian.Ui/dashboard`
- Host-served workstation route: `http://localhost:8080/workstation/`
- WPF desktop workstation: `src/Meridian.Wpf/Meridian.Wpf.csproj`

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
- `src/Meridian.Wpf/Shell/`: WPF shell route, launch, session, refresh, and presentation seams
- `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`: WPF desktop shell view model and workstation navigation anchor

## Review Guardrails

- Use `CancellationToken` on async methods and preserve cancellation flow.
- Use structured logging, not string interpolation inside log calls.
- Use `IOptionsMonitor<T>` for runtime-mutable configuration.
- Use ADR-014 source-generated JSON serialization.
- Use `EventPipelinePolicy.*.CreateChannel<T>()`, not ad hoc channels.
- Route durable storage through WAL or `AtomicFileWriter`, not direct file writes.
- Avoid constructor sync-over-async and fire-and-forget persistence on lifecycle-sensitive
  services; await initialization and terminal metadata writes at the service boundary.
- Do not add package versions directly to project files; central package management lives in
  `Directory.Packages.props`.
