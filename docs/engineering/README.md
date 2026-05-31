# Engineering Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This is the canonical developer and coding-agent entrypoint for Meridian. It consolidates the old `docs/developer/`, `docs/development/`, and engineering-facing parts of `docs/architecture/` without deleting those source-material pages yet.

## Engineering Paths

| Need | Start here |
| --- | --- |
| First local setup | [Start Here](../start/README.md) |
| Repository structure | [Project Structure](../architecture/project-structure.md) |
| Module boundaries | [Module Map](../architecture/module-map.md) |
| Build/test/run commands | [Build, Test, Run](../developer/build-test-run.md) and [HELP](../HELP.md) |
| Source ownership and README workflow | [Source Documentation Mesh](../source/README.md) |
| Roadmap registry workflow | [Roadmap Registry](../roadmap/README.md) |
| AI-safe execution rules | [AI Development Contract](../ai/assistant-workflow-contract.md) |
| WPF validation | [Desktop Testing Guide](../development/desktop-testing-guide.md) |
| Browser workstation development | `src/Meridian.Ui/dashboard/` and [Web UI Development Pivot](../plans/web-ui-development-pivot.md) |
| Documentation ownership | [Documentation Ownership Contract](../documentation-ownership.md) |

## Solution Map

| Layer | Projects | Rule |
| --- | --- | --- |
| Host | `src/Meridian/` | Compose services, expose CLI/API modes, and host workstation endpoints. |
| Application | `src/Meridian.Application/` | Coordinate workflows, commands, and pipelines; keep UI and provider specifics out. |
| Domain/Core/Contracts | `src/Meridian.Domain/`, `src/Meridian.Core/`, `src/Meridian.Contracts/` | Keep business and contract types UI-independent. |
| Providers/Infrastructure | `src/Meridian.Infrastructure*/`, `src/Meridian.ProviderSdk/` | Isolate external API integration behind provider contracts. |
| Storage | `src/Meridian.Storage/` | Preserve WAL, atomic-write, and durability expectations. |
| Execution/Risk | `src/Meridian.Execution*/`, `src/Meridian.Risk/` | Isolate broker gateways, paper/live controls, and pre-trade validation. |
| Strategy/Backtesting | `src/Meridian.Strategies/`, `src/Meridian.Backtesting*/`, `src/Meridian.QuantScript/` | Keep strategy lifecycle, replay, and scripting reusable outside UI. |
| Accounting/Ledger/F# | `src/Meridian.Ledger/`, `src/Meridian.FSharp*/` | Keep accounting and domain-specialized models testable outside UI. |
| UI Shared | `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` | Own workstation DTO projection and endpoint/read-model support. |
| UI Surfaces | `src/Meridian.Ui/dashboard/`, `src/Meridian.Wpf/` | Keep views thin; put state, labels, disabled reasons, and commands in view models/read models. |
| Tests/Benchmarks | `tests/`, `benchmarks/` | Provide focused regression, integration, UI-service, WPF, F#, and performance proof lanes. |

## Build, Test, Run

Use the narrowest command that covers your change.

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

For active browser-workstation backend work, the focused solution filter avoids WPF, benchmarks, MCP hosts, and optional integration projects:

```powershell
dotnet restore Meridian.WebWorkstation.slnf /p:EnableWindowsTargeting=true
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
```

For automation or concurrent local builds, prefer the isolated build controller:

```powershell
python build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key docs-rebuild
```

## Local Run Commands

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
npm --prefix src/Meridian.Ui/dashboard run dev
pwsh ./scripts/dev/run-desktop.ps1
```

Use `--mode desktop` only when intentionally running the retained desktop-local host with the UI server and streaming collector together.

## Source Documentation Rule

Before editing `src/**`:

1. Read the nearest source `README.md`.
2. Identify the module in `docs/source/data/source-modules.yml`.
3. Update source README or registry records when ownership, behavior, validation, diagrams, or TODO scope changes.
4. Never hand-edit generated source docs outside approved generated blocks.

Source-module truth remains in `docs/source/data/*.yml`; generated source views remain script-owned.

## Shared Workstation Rule

When product behavior is common to browser and desktop, put it behind shared contracts, read models, endpoints, or services before expanding either client. Do not fork business behavior into WPF-only or browser-only code unless the difference is explicitly UI-specific.

Visible root operator navigation remains limited to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.

## Structural Proposal Rule

Historical architecture proposals are source material, not active migration orders. Before applying old restructuring advice, verify the current source owner, nearest source `README.md`, and `docs/source/data/*.yml` registry record. Keep provider, storage, execution, and UI boundary changes small enough to prove with focused tests and source documentation updates.

## Provider Configuration Engineering Rule

Provider settings are configuration orchestration, not provider logic forks. Keep shared provider contracts, credential resolution, fallback ordering, rate-limit policy, and provider health evidence reusable across WPF, browser, and CLI surfaces. Desktop or browser settings should mutate typed configuration seams and show effective state, not duplicate secrets or reimplement `CompositeHistoricalDataProvider` behavior locally.

## Ingestion Orchestration Rule

Realtime collection, historical backfill, gap-fill, and scheduled work should remain first-class managed workloads with explicit job state, checkpointing, retry intent, idempotency, and operator-visible audit trails. Changes to ingestion orchestration must preserve deterministic resume behavior, deduplication semantics, backpressure visibility, dead-letter/audit evidence, and SLO reporting.

## Streaming Pipeline Rule

Streaming changes must preserve provider isolation, bounded-channel backpressure, WAL durability, event validation, dead-letter routing, persistent deduplication, failover behavior, latency/freshness telemetry, and alert-to-runbook mapping. Historical streaming evaluations describe prior architecture reviews; current changes need source evidence and focused tests for the touched pipeline, provider, storage, or monitoring surface.

## Storage Architecture Rule

Storage changes must preserve crash-safe writes, checksum/manifest integrity, schema evolution, catalog synchronization, tier/retention behavior, quota controls, data lineage, and replay compatibility. Keep JSONL, Parquet, WAL, catalog, export, and portable-package behavior aligned through source README/registry updates and focused storage validation.

## Performance Work Rule

Performance opportunities are not active work until revalidated against current source and benchmark evidence. Prefer algorithmic, allocation, and data-layout improvements in known hot paths before hand-written assembly or hardware intrinsics. Any SIMD/intrinsics work needs scalar fallback behavior, deterministic tests, and a benchmark that proves the target path is still material.

## WPF Development Rule

- Keep views focused on rendering and binding.
- Put commands, labels, disabled reasons, and workflow state in view models or shared read models.
- Use the WPF launcher for local desktop work: `pwsh ./scripts/dev/run-desktop.ps1`.
- Use WPF-specific proof only when the changed surface requires it:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

Historical desktop improvement guides are source material only. Use current desktop source READMEs, WPF tests, shared UI service tests, and this engineering page for active desktop changes.

## Browser Workstation Rule

- Keep React components presentation-focused.
- Prefer shared read models, endpoint DTOs, and view-model helpers over screen-local business state.
- Run TypeScript, Vitest, or build lanes based on touched files.
- Keep visible demo/fixture states labeled; do not imply live readiness without provider/runtime evidence.

## Workstation Visual Consistency Rule

- Prefer shared spacing, card, badge, tile, and semantic callout patterns before adding bespoke screen-level styling.
- Keep summary tiles ordered as label, value, then trend or detail.
- Use semantic badge/callout states such as info, warning, success, and danger instead of hardcoded one-off colors.
- Preserve intentional contextual hierarchy, but document any bespoke treatment as a design-system follow-up rather than leaving it as silent drift.

## Documentation And Registry Proof Lanes

```powershell
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-ai-handoff.py --output docs/status/ai-handoff-checklist-report.md
python build/scripts/docs/run-docs-automation.py --scripts check-ai-handoff-strict
python build/scripts/docs/check-codex-skills.py --summary
python build/scripts/docs/validate-roadmap-registry.py --summary
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/validate-source-readmes.py --summary
python build/scripts/docs/validate-doc-hashes.py --summary
```

Roadmap truth remains in `docs/roadmap/data/*.yml`. Source-module truth remains in `docs/source/data/*.yml`. Generated docs remain script-owned.

## Backtesting Engineering Rule

Backtesting changes are consequential because they can change strategy evidence and operator decisions. Treat historical backtesting audits as source material only, and revalidate findings against the current `src/Meridian.Backtesting*/` code before creating active work.

For backtesting engine, metrics, fill-model, or Backtest Studio work:

- Keep timestamp/date filtering deterministic and UTC-based.
- Treat rejected trades, margin failures, and shortability violations as domain outcomes when possible, not infrastructure crashes.
- Keep trade statistics and realised PnL tied to round-trip or lot-pairing semantics, not isolated fill-level heuristics.
- Avoid duplicate FIFO/lot accounting implementations that can diverge.
- Document fill-model assumptions such as midpoint convention, spread/slippage calibration, time-in-force behavior, and LOB tie-breaking.
- Add focused tests for timezone boundaries, rejected fills, drawdown recovery, round-trip statistics, FIFO parity, and deterministic merge ordering when those surfaces change.

## Known Local Risks

On this Windows checkout, validation can be affected by stale `dotnet` or MSBuild workers, locked Node/Rollup binaries, OneDrive/offline placeholders, and low disk space. Treat those as environment blockers until proven otherwise; do not paper over blocked evidence in docs.

If a cleanup stops on a locked `bin/` or `obj/` file, close the owning `dotnet` or Meridian process before retrying. Do not use broad destructive cleanup without proving the tracked-file boundary first.

## Simplification And Cleanup Rule

Historical cleanup audits are source material, not active work orders. Before acting on an archived simplification item, revalidate that the named files, wrappers, endpoints, or duplicated models still exist in the current checkout and that the recommended change still fits the module owner and validation lane.

Prefer small structural cleanup PRs with an explicit before/after scope, a tracked-file boundary, and the narrowest proof command that covers the touched surface.

## Legacy Source Material

The older pages below remain active source material during migration. New docs should link to this engineering index unless a specific legacy guide is needed.

- [Developer Quick Guides](../developer/README.md)
- [Development Guides](../development/README.md)
- [Architecture Documentation](../architecture/README.md)
- [Module Map](../architecture/module-map.md)
- [Desktop Testing Guide](../development/desktop-testing-guide.md)
