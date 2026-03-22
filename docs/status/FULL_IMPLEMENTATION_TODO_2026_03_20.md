# Full Implementation Backlog (Non-Assembly Scope)

**Last Updated:** 2026-03-21
**Status:** Active normalized backlog
**Purpose:** Single current backlog for finishing the remaining planned non-assembly work

This document is the normalized execution backlog for the repository's remaining product and structural work outside Phase 16 assembly/SIMD optimization.

Use it with:

- `ROADMAP.md` for wave and phase sequencing
- `FEATURE_INVENTORY.md` for current-vs-target capability status
- `IMPROVEMENTS.md` for completed improvement history
- `../plans/trading-workstation-migration-blueprint.md`
- `../plans/governance-fund-ops-blueprint.md`
- `../plans/quant-script-environment-blueprint.md`
- `../plans/l3-inference-implementation-plan.md`

---

## Current Baseline

The repo is no longer blocked by the earlier partial items that used to dominate the backlog.

Closed platform work:

- C3 provider lifecycle consolidation for the active platform baseline
- G2 end-to-end trace propagation for the current ingestion/storage path
- I3 checked-in config schema generation and validation
- J8 canonicalization drift reporting and fixture-maintenance workflow

Implemented foundations now available to build on:

- workspace categories aligned around `Research`, `Trading`, `Data Operations`, and `Governance`
- shared run browser/detail/portfolio/ledger WPF surfaces
- Security Master application/storage/domain foundation
- coordination services and lease/ownership primitives for future multi-instance work

The remaining backlog is therefore about turning those foundations into a complete operator-facing product.

---

## Backlog Tracks

### Track A: Provider confidence and current-functionality hardening

Goal: make the currently shipped platform easier to trust and easier to operate.

Open work:

- expand Polygon replay coverage across more feeds and edge cases
- strengthen NYSE shared-lifecycle regression coverage
- keep IB runtime/bootstrap guidance aligned with the official vendor surface
- keep StockSharp connector/runtime guidance aligned with validated adapters
- expand under-tested provider coverage for TwelveData, Nasdaq Data Link, Alpha Vantage, Finnhub, Stooq, and OpenFIGI
- continue backtesting-engine and strategy-run persistence coverage expansion

Exit signal:

Current functionality is supported by provider/runtime docs and tests that match the real supported paths.

### Track B: Multi-instance coordination

Goal: finish the optional scale-out story cleanly instead of leaving it half-implied.

Open work:

- turn the coordination and lease primitives into a supported ownership model
- define duplicate-prevention semantics for subscriptions, scheduled work, and backfill execution
- document a supported multi-node topology and failure/recovery model
- add tests or simulations for lease ownership and failover behavior

Primary anchors:

- `src/Meridian.Application/Coordination/`
- `src/Meridian.Core/Config/CoordinationConfig.cs`
- `tests/Meridian.Tests/Application/Coordination/`

### Track C: Trading workstation completion

Goal: turn the current workstation vocabulary and first shared surfaces into a coherent operator shell.

Open work:

- make workspace-first navigation the primary desktop model
- keep command palette, workspace shells, and navigation hierarchy aligned
- extend shared run surfaces beyond backtest-first usage into paper/live history
- add richer portfolio, ledger, and comparison workflows on top of the shared model
- remove or re-scope orphan and placeholder-only workstation surfaces

Primary anchors:

- `../plans/trading-workstation-migration-blueprint.md`
- `src/Meridian.Wpf/ViewModels/StrategyRun*.cs`
- `src/Meridian.Wpf/ViewModels/RunMatViewModel.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs`

### Track D: Governance and fund-operations productization

Goal: finish the middle- and back-office product track instead of leaving it blueprint-only.

Open work:

- productize Security Master beyond its current foundational services
- add account/entity and strategy-structure workflows
- deepen portfolio and ledger surfaces into first-class governance tooling
- add multi-ledger, trial-balance, and cash-flow views
- implement reconciliation workflows and governed reporting/report-pack generation

Primary anchors:

- `../plans/governance-fund-ops-blueprint.md`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`

### Track E: QuantScript

Goal: ship the QuantScript capability as a real project, not only a blueprint.

Open work:

- add the project and test project
- implement the compiler/runner pipeline
- add execution context, parameter discovery, and plotting/output handling
- add a real WPF entry surface
- add samples, tests, and operating docs

Reference:

- `../plans/quant-script-environment-blueprint.md`

### Track F: L3 inference and queue-aware execution simulation

Goal: ship the queue-aware simulation stack as a real capability.

Open work:

- add contracts, config, and deterministic fixtures
- build reconstruction and replay-alignment layers
- implement the inference model and execution simulator
- add CLI/API/WPF integration
- add calibration, confidence, and degradation behavior
- add tests and operator docs

Reference:

- `../plans/l3-inference-implementation-plan.md`

### Track G: Structural closure and documentation convergence

Goal: keep the repo coherent as the remaining product work lands.

Open work:

- continue composition-root and startup readability work
- keep typed service/query seams as the default integration boundary
- continue WPF page-to-viewmodel/service extraction in active workflow areas
- keep CI/doc generation and planning/status docs synchronized
- archive newly historical planning material once it truly stops being active guidance

References:

- `../plans/codebase-audit-cleanup-roadmap.md`
- `../plans/readability-refactor-roadmap.md`

---

## Recommended Delivery Order

### Wave 1

- Track A provider confidence and hardening
- Track C workstation shell consolidation

### Wave 2

- Track C shared run/portfolio/ledger deepening
- Track D governance and Security Master productization baseline

### Wave 3

- Track C trading cockpit and promotion workflow
- Track D reconciliation/reporting flows

### Wave 4

- Track E QuantScript
- Track F L3 inference/simulation foundation

### Wave 5

- Track B multi-instance coordination
- Track G remaining structural/documentation closure

---

## Practical Definition of Done

The repository can reasonably claim that the planned non-assembly work is complete only when all of the following are true:

- provider/runtime docs and tests support the current functionality claims
- the workstation experience is centered on real `Research`, `Trading`, `Data Operations`, and `Governance` shells
- shared run, portfolio, and ledger concepts support research, paper, and live-adjacent workflows through one recognizable model family
- Security Master, governance, reconciliation, and reporting are product surfaces rather than only foundations or blueprints
- QuantScript and L3 simulation exist as code, tests, docs, and discoverable user-facing entry points
- any remaining cleanup/readability work is explicitly deferred by decision rather than left as stale implied backlog

Until then, Meridian is best described as feature-rich and structurally strong, but still in active productization rather than fully complete.
