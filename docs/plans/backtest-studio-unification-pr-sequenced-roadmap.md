# Backtest Studio Unification PR-Sequenced Roadmap

**Owner:** Core Team
**Audience:** Engineering leads, implementers, and reviewers
**Last Updated:** 2026-04-09
**Status:** Active execution roadmap aligned to Wave 5 Backtest Studio unification

> **Retirement note (2026-04-09):** References in this roadmap to `src/Meridian.Ui/dashboard` or "web workstation" slices are historical remnants of the retired browser dashboard. Re-scope those slices to WPF or the retained desktop-local API surface before implementation.

## Purpose

This document translates the Backtest Studio unification blueprint into PR-sized execution slices with:

- explicit dependency order
- recommended parallel lanes
- suggested ownership boundaries
- low-conflict file and module groupings

The goal is to finish Wave 5 in a way that preserves Meridian's existing shared run model, avoids a parallel Lean result stack, and gives multiple contributors a safe way to work concurrently. This roadmap assumes the Wave 1-4 trust, cockpit, shared-model, and governance path remains the active priority unless a narrower Backtest Studio slice is explicitly pulled forward.

Use this with:

- [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md)
- [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md)
- [../status/ROADMAP.md](../status/ROADMAP.md)
- [../status/FULL_IMPLEMENTATION_TODO_2026_03_20.md](../status/FULL_IMPLEMENTATION_TODO_2026_03_20.md)

## How to Use This Document

- Treat each `PR-XX` item as a reviewable implementation slice.
- Prefer merging contract and normalization slices before UX-heavy Backtest Studio work.
- Use the `Can run with` column to identify safe concurrency lanes.
- Avoid combining slices that share the same primary write set unless they are intentionally coordinated.
- Keep Lean setup and transport work distinct from canonical run-storage and workstation read-model work.

## Parallel Delivery Lanes

| Lane | Theme | Primary write scope |
|------|-------|---------------------|
| Lane A | Canonical SDK and native engine seams | `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk`, `tests/Meridian.Backtesting.Tests` |
| Lane B | Shared run orchestration and workstation contracts | `src/Meridian.Application`, `src/Meridian.Contracts`, `src/Meridian.Strategies`, `src/Meridian.Ui.Shared` |
| Lane C | Lean integration and result ingestion | `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared/Endpoints`, selected application services |
| Lane D | Research UX, WPF, and workstation client surfaces | `src/Meridian.Wpf`, `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard` |
| Lane E | Performance, evidence, and validation | `tests/`, targeted read-model services, targeted backtesting hot paths |

## Dependency Rules

- Canonical `BacktestResult` metadata and normalization seams merge before any richer comparison or diff UI work.
- Lean ingestion should normalize into `StrategyRunEntry` before mixed-engine comparison work ships.
- Shared workstation research contracts merge before WPF or web research screens bind to them.
- Fill-profile work should land before comparison surfaces rely on fill-profile identity.
- Performance slices should follow working end-to-end functionality and target measured bottlenecks only.

## PR Roadmap

| PR | Title | Primary lane | Depends on | Can run with | Primary write scope |
|----|-------|--------------|------------|--------------|---------------------|
| PR-01 | Canonical backtest result metadata | Lane A | None | PR-02, PR-03 | `Meridian.Backtesting.Sdk`, `Meridian.Backtesting`, `Meridian.Backtesting.Tests` |
| PR-02 | Backtest Studio orchestration contracts | Lane B | None | PR-01, PR-03 | `Meridian.Application`, `Meridian.Contracts`, `Meridian.Strategies` |
| PR-03 | Lean raw artifact and ingest contract cleanup | Lane C | None | PR-01, PR-02 | `Meridian.Ui.Services`, `Meridian.Ui.Shared/Endpoints` |
| PR-04 | Native engine adapter and run-orchestrator wiring | Lane A / B | PR-01, PR-02 | PR-05 | `Meridian.Application`, `Meridian.Backtesting`, `Meridian.Strategies` |
| PR-05 | Lean result normalization into shared runs | Lane B / C | PR-01, PR-02, PR-03 | PR-04, PR-06 | `Meridian.Application`, `Meridian.Strategies`, `Meridian.Ui.Shared`, `Meridian.Ui.Services` |
| PR-06 | Strategy run research contract expansion | Lane B | PR-01, PR-02 | PR-05, PR-07 | `Meridian.Contracts`, `Meridian.Strategies`, `Meridian.Ui.Shared` |
| PR-07 | Dedicated run comparison service | Lane B | PR-05, PR-06 | PR-08, PR-09 | `Meridian.Strategies`, `Meridian.Ui.Shared`, `tests/Meridian.Tests` |
| PR-08 | Research compare and diff endpoint upgrade | Lane B / D | PR-06, PR-07 | PR-09, PR-10 | `Meridian.Ui.Shared`, `Meridian.Contracts`, `Meridian.Ui.Services` |
| PR-09 | Fill-model profile system | Lane A | PR-01 | PR-07, PR-08, PR-10 | `Meridian.Backtesting.Sdk`, `Meridian.Backtesting`, `Meridian.Backtesting.Tests` |
| PR-10 | WPF Backtest Studio unification shell | Lane D | PR-05, PR-06, PR-08, PR-09 | PR-11 | `Meridian.Wpf`, `Meridian.Ui.Services` |
| PR-11 | Web workstation research integration | Lane D | PR-08, PR-09 | PR-12 | `Meridian.Ui/dashboard`, `Meridian.Ui.Services`, `Meridian.Ui.Shared` |
| PR-12 | Benchmark and attribution diff depth | Lane B / D | PR-07, PR-08, PR-09 | PR-13 | `Meridian.Contracts`, `Meridian.Strategies`, `Meridian.Ui.Shared`, `Meridian.Wpf`, `Meridian.Ui/dashboard` |
| PR-13 | Larger-window read and comparison performance pass | Lane E | PR-07, PR-08, PR-12 | PR-14 | `Meridian.Strategies`, `Meridian.Backtesting`, `tests/` |
| PR-14 | Backtest Studio validation and operator evidence closure | Lane E | PR-10, PR-11, PR-12, PR-13 | None | `tests/`, `docs/status`, selected services/endpoints |

## PR Details

### PR-01: Canonical Backtest Result Metadata

**Goal**

Add engine-neutral result metadata so mixed-engine comparisons can represent completeness honestly instead of implying false parity.

**Primary anchors**

- `src/Meridian.Backtesting.Sdk/BacktestResult.cs`
- `src/Meridian.Backtesting.Sdk/BacktestRequest.cs`
- `src/Meridian.Backtesting/Engine/BacktestEngine.cs`
- `tests/Meridian.Backtesting.Tests/`

**Deliverables**

- additive `BacktestArtifactStatus`, `BacktestArtifactCoverage`, and `BacktestEngineMetadata`
- additive fields on `BacktestResult`
- native engine populates coverage and engine metadata
- tests proving backward-safe serialization and construction paths

### PR-02: Backtest Studio Orchestration Contracts

**Goal**

Introduce the application-level orchestration seam that lets native and Lean engines participate in one workflow.

**Primary anchors**

- new `src/Meridian.Application/Backtesting/`
- `src/Meridian.Strategies/Models/StrategyRunEntry.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`

**Deliverables**

- `BacktestStudioRunRequest`
- `BacktestStudioRunHandle`
- `BacktestStudioRunStatus`
- `IBacktestStudioEngine`
- `BacktestStudioRunOrchestrator`

### PR-03: Lean Raw Artifact and Ingest Contract Cleanup

**Goal**

Make Lean transport and ingest payloads explicit enough to support later canonical normalization without UI-only ambiguity.

**Primary anchors**

- `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`
- `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs`
- `src/Meridian.Wpf/Models/LeanModels.cs`

**Deliverables**

- explicit raw Lean artifact models
- cleaner ingest request and response shapes
- clearer separation between launcher status, raw artifact fetch, and canonical run ingestion

### PR-04: Native Engine Adapter and Run-Orchestrator Wiring

**Goal**

Put the native backtest engine behind the shared Backtest Studio orchestration seam.

**Primary anchors**

- `src/Meridian.Backtesting/Engine/BacktestEngine.cs`
- new adapter under `src/Meridian.Application/Backtesting/`
- `src/Meridian.Strategies/Models/StrategyRunEntry.cs`

**Deliverables**

- `MeridianNativeBacktestStudioEngine`
- orchestrator creates and completes `StrategyRunEntry` for native runs
- native run path proves the new abstraction without behavior drift

### PR-05: Lean Result Normalization into Shared Runs

**Goal**

Convert completed Lean runs into canonical `BacktestResult` and persist them through the same run-storage path as native runs.

**Primary anchors**

- new normalization services under `src/Meridian.Application/Backtesting/`
- `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`
- `src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs`
- `src/Meridian.Strategies/Models/StrategyRunEntry.cs`

**Deliverables**

- `ILeanBacktestResultNormalizer`
- `LeanBacktestStudioEngine`
- Lean-completed runs persisted with `Engine = Lean`
- mixed native/Lean runs visible through the same `StrategyRunReadService` path

### PR-06: Strategy Run Research Contract Expansion

**Goal**

Extend shared workstation contracts with research-specific models without forking the central run model.

**Primary anchors**

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

**Deliverables**

- `StrategyRunResearchSummary`
- `StrategyRunMetricDelta`
- `StrategyRunResearchDiff`
- benchmark-related additive research DTOs
- contract coverage for mixed-engine visibility and artifact warnings

### PR-07: Dedicated Run Comparison Service

**Goal**

Move heavier research comparison logic out of `StrategyRunReadService` and into a focused service that can evolve safely.

**Primary anchors**

- new `src/Meridian.Strategies/Services/StrategyRunComparisonService.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `tests/Meridian.Tests/`

**Deliverables**

- `IStrategyRunComparisonService`
- compare and diff rules for native/native, native/Lean, and Lean/Lean
- coverage warnings for partial artifacts
- tests for metric comparability and mixed-engine diff behavior

### PR-08: Research Compare and Diff Endpoint Upgrade

**Goal**

Expose richer research-oriented comparison and diff through workstation routes without replacing the existing API direction.

**Primary anchors**

- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- `src/Meridian.Contracts/Api/UiApiRoutes.cs`
- `src/Meridian.Ui.Services/`

**Deliverables**

- `/api/workstation/runs/{runId}/research`
- `/api/workstation/runs/compare/research`
- `/api/workstation/runs/diff/research`
- endpoint payloads that surface coverage, engine identity, and research deltas

### PR-09: Fill-Model Profile System

**Goal**

Replace fill-realism booleans and ad hoc selection with explicit user-facing fill profiles that can be compared and explained.

**Primary anchors**

- `src/Meridian.Backtesting.Sdk/BacktestRequest.cs`
- `src/Meridian.Backtesting.Sdk/Order.cs`
- `src/Meridian.Backtesting/FillModels/`
- `src/Meridian.Backtesting/Engine/BacktestEngine.cs`
- `tests/Meridian.Backtesting.Tests/`

**Deliverables**

- `BacktestFillProfile`
- `IFillModelProfileResolver`
- profile-aware fill model selection
- run metadata includes selected fill profile identity

### PR-10: WPF Backtest Studio Unification Shell

**Goal**

Turn WPF research entry points into one Backtest Studio flow instead of separate native and Lean launch surfaces.

**Primary anchors**

- `src/Meridian.Wpf/ViewModels/BacktestViewModel.cs`
- `src/Meridian.Wpf/ViewModels/LeanIntegrationViewModel.cs`
- `src/Meridian.Wpf/Views/BacktestPage.xaml.cs`
- `src/Meridian.Wpf/Views/LeanIntegrationPage.xaml.cs`
- `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`

**Deliverables**

- single engine selector in the research workflow
- unified scenario, parameter, and dataset entry
- completed runs open through the shared run browser and drill-ins
- Lean and native launch flows feel like one product capability

### PR-11: Web Workstation Research Integration

**Goal**

Expose the same unified research workflow direction in the web workstation without introducing a web-only result model.

**Primary anchors**

- `src/Meridian.Ui/dashboard/`
- `src/Meridian.Ui.Services/`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

**Deliverables**

- research workspace consume new compare and diff endpoints
- engine selection and run detail display share canonical workstation models
- partial artifact warnings are visible in the web experience

### PR-12: Benchmark and Attribution Diff Depth

**Goal**

Deepen comparison from top-line metrics into the analysis that actually helps a researcher decide whether two runs are meaningfully different.

**Primary anchors**

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Strategies/Services/StrategyRunComparisonService.cs`
- `src/Meridian.Wpf/`
- `src/Meridian.Ui/dashboard/`

**Deliverables**

- benchmark-relative comparison
- attribution delta leaders
- fill drift or trade drift sections where source evidence exists
- clearer explanation of missing versus partial comparison evidence

### PR-13: Larger-Window Read and Comparison Performance Pass

**Goal**

Improve the experience for larger historical windows only where comparison and run-detail workflows are measurably slowed.

**Primary anchors**

- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Strategies/Services/StrategyRunComparisonService.cs`
- `src/Meridian.Backtesting/`
- `tests/`

**Deliverables**

- reduced repeated materialization of large snapshot and fill collections
- lazy or windowed heavy artifact loading where appropriate
- targeted caching of expensive derived summaries
- benchmark or allocation evidence attached to the PR

### PR-14: Backtest Studio Validation and Operator Evidence Closure

**Goal**

Close Wave 5 with the evidence needed to say Backtest Studio is one coherent workflow, not just a merged UI.

**Primary anchors**

- `tests/Meridian.Backtesting.Tests/`
- `tests/Meridian.Tests/`
- `tests/Meridian.Wpf.Tests/`
- `docs/status/`

**Deliverables**

- mixed-engine regression coverage
- fill-profile regression coverage
- run compare and diff coverage for artifact gaps
- documentation or status updates capturing Wave 5 completion evidence

## Suggested Merge Groups

Use these if we want a slightly coarser execution rhythm while preserving reviewability:

- Group 1: PR-01 to PR-03
  - locks contracts, metadata, and Lean raw artifact seams
- Group 2: PR-04 to PR-06
  - makes both engines land in shared run storage with additive research contracts
- Group 3: PR-07 to PR-09
  - deepens comparison and fill realism
- Group 4: PR-10 to PR-12
  - lands the operator-facing Backtest Studio experience across WPF and web
- Group 5: PR-13 to PR-14
  - performance pass and evidence closure

## Recommended First Three PRs

If the goal is to start implementation immediately with the least rework risk:

1. `PR-01` because mixed-engine parity work needs explicit artifact coverage first.
2. `PR-02` because the orchestrator and engine interface define the seams everything else hangs from.
3. `PR-03` because Lean transport needs to stop being a UI-only result path before normalization work begins.

## Exit Check

Wave 5 is functionally complete when all of the following are true:

- native and Lean completed backtests are both persisted as shared strategy runs
- compare and diff workflows use canonical run storage instead of engine-specific result DTOs
- fill realism is expressed through explicit profiles visible to operators
- larger-window performance is materially improved where run detail and comparison were previously painful
- WPF and web research flows both present backtesting as one coherent workflow with engine choice as a parameter, not a product split
