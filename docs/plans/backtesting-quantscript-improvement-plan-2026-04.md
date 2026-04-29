# Backtesting + QuantScript Improvement Plan (2026-04)

**Owner:** Research Platform / Backtesting / Workstation
**Audience:** Backtesting, QuantScript, WPF, Application, and API contributors
**Last Updated:** 2026-04-29
**Status:** Proposed

> Companion documents:
> - [research-backtest-trust-and-velocity-blueprint.md](research-backtest-trust-and-velocity-blueprint.md)
> - [backtest-studio-unification-blueprint.md](backtest-studio-unification-blueprint.md)
> - [quant-script-environment-blueprint.md](quant-script-environment-blueprint.md)
> - [quant-script-page-implementation-guide.md](quant-script-page-implementation-guide.md)

## Summary

This plan improves Meridian's **research loop** end-to-end by tightening the path from idea → script experiment → trusted backtest → parameter sweep → run comparison → promotion readiness.

Primary objective: make backtesting and QuantScript feel like one coherent product workflow while preserving existing layering across `Meridian.QuantScript`, `Meridian.Backtesting`, `Meridian.Application`, `Meridian.Strategies`, and workstation surfaces.

## Scope

### In scope

1. Backtest trust improvements (preflight confidence, stage-aware progress, reproducible artifacts).
2. Backtest velocity improvements (batch/sweep throughput, cached data access, fewer manual hops).
3. QuantScript workflow improvements (notebook ergonomics, strategy handoff to backtesting, result introspection).
4. Shared contracts and UI-service seams so WPF and API surfaces stay aligned.
5. Test and performance gates for each delivery phase.

### Out of scope

- Full replacement of current Research workspace navigation model.
- Non-.NET scripting runtimes (Python/R kernels) in this phase.
- New broker integrations unrelated to research/backtest workflow.
- Large execution-engine rewrites that are not required for trust/velocity goals.

### Assumptions

- The web dashboard is now the active operator UI lane for new research workflow proof; WPF remains retained support and shared-contract regression coverage for this plan.
- Native Meridian backtesting remains the first-class research engine in this phase; Lean parity continues in parallel through existing Wave 5 planning.
- Additive contracts are preferred over breaking changes.

## Architecture

## 1) Unified Research Session contract

Create a lightweight session model that links QuantScript notebooks, script runs, backtests, and batch sweeps under one correlation identity.

- New app seam:
  - `Meridian.Application.Research.IResearchSessionService`
  - `Meridian.Application.Research.ResearchSessionService`
- New contract surface:
  - `Meridian.Contracts.Workstation.ResearchSession*` models

Purpose:
- Persist a consistent `ResearchSessionId` across script execution and backtest submissions.
- Allow run browser/history pages to answer: *which notebook cell or script produced this run?*

## 2) Backtest Trust Gate v2

Extend the current preflight path into a stronger, structured trust gate.

Enhancements:
- Coverage completeness score (window coverage by symbol/time bucket).
- Explicit missing-data classification (no files vs partial files vs stale files).
- Security Master normalization warnings (symbol mapping drift, unknown identifiers).
- Replay/fill-model compatibility checks (required data type availability for requested fill model).

Likely seams:
- `Meridian.Application.Backtesting.IBacktestPreflightService`
- `Meridian.Application.Backtesting.BacktestPreflightService`
- `Meridian.Contracts.Workstation.ResearchBacktestModels`

## 3) Stage-aware Backtest Runtime telemetry

Promote progress reporting from coarse percentage to actionable stages with timing.

Add stage states:
- `ValidatingRequest`
- `ValidatingCoverage`
- `LoadingData`
- `ApplyingCorporateActions`
- `Replaying`
- `SimulatingFills`
- `ComputingMetrics`
- `PersistingArtifacts`
- `Completed`

Add per-stage durations and a total critical path duration so operators can identify bottlenecks.

Likely seams:
- `Meridian.Backtesting.Sdk.BacktestProgressEvent`
- `Meridian.Backtesting.BacktestStudioRunOrchestrator`
- `Meridian.Strategies.Services.StrategyRunReadService`

## 4) Parameter Lab v2 (real sweeps + robust analysis)

Turn parameter sweeps into a first-class workflow with reproducibility guarantees.

Enhancements:
- Sweep definition schema with deterministic ordering and hash (`SweepDefinitionHash`).
- Persist sweep lineage (`SweepId`, source strategy descriptor, parameter grid, objective function).
- Add post-run ranking profiles (Sharpe, max drawdown, turnover-aware objective).
- Add overfitting guardrails: in-sample/out-of-sample split and walk-forward mode as optional flags.

Likely seams:
- `Meridian.Backtesting.IBatchBacktestService`
- `Meridian.Backtesting.BatchBacktestService`
- `Meridian.Contracts.Workstation.StrategyRunReadModels`

## 5) QuantScript → Backtest handoff

Enable direct submission of script-defined strategy configurations into the native backtest launcher.

Capabilities:
- Extract strategy config blocks from QuantScript documents.
- One-click “Run in Backtest Studio” from script context.
- Save handoff manifests as reproducible artifacts (`.researchhandoff.json`).

Likely seams:
- `Meridian.QuantScript` document and execution services
- `Meridian.Wpf.ViewModels.QuantScriptViewModel`
- shared run submission service in `Meridian.Application`

## 6) QuantScript notebook productivity upgrades

Raise researcher throughput with pragmatic UX and execution controls.

Improvements:
- Cell-level execution timing and memory stats.
- Background execution queue with cancellation and stale-output invalidation.
- Inline result pinning (persist selected output snapshots for comparison).
- Result export templates for run notes and governance review packs.

## 7) Post-simulation interpretation and comparison

Standardize structured run comparison and interpretation outputs.

Deliverables:
- Side-by-side run comparison model (PnL, drawdown regimes, cost attribution, fill-quality deltas).
- Narrative summary generator seam (rule-based first, model-assisted optional behind feature flag).
- Promotion readiness checklist populated from backtest artifacts.

## Interfaces and Models

Prioritized public-surface additions (additive):

- `ResearchSessionSummaryDto`
- `ResearchArtifactLinkDto`
- `BacktestPreflightReportV2Dto`
- `BacktestStageTelemetryDto`
- `SweepDefinitionDto`
- `SweepObjectiveProfile`
- `QuantScriptBacktestHandoffDto`
- `RunComparisonReportDto`

Design rules:
- Keep DTOs UI-agnostic in `Meridian.Contracts`.
- Keep orchestration in `Meridian.Application`.
- Keep simulation logic in `Meridian.Backtesting`.
- Keep notebook/scripting behavior in `Meridian.QuantScript`.

## Data Flow

1. Researcher edits notebook/script in QuantScript.
2. QuantScript execution produces results + optional strategy config block.
3. User triggers backtest handoff (or API client submits handoff).
4. Preflight Trust Gate v2 evaluates readiness and returns structured report.
5. Backtest run starts with stage telemetry streaming.
6. Run artifacts persist through shared run store/read models.
7. Optional Parameter Lab sweep executes linked variants.
8. Comparison and interpretation service generates report.
9. Promotion workflow consumes run/sweep evidence.

## Edge Cases and Risks

- **Contract drift risk:** duplicated run metadata across QuantScript and backtest contracts.
  - Mitigation: introduce `ResearchSessionId` once and reuse broadly.
- **Performance regression risk:** richer telemetry adds overhead.
  - Mitigation: benchmark with sampling controls; avoid per-event heavy serialization.
- **Overfitting risk in sweeps:** users optimize to noise.
  - Mitigation: require objective profile declaration + optional OOS/walk-forward gates.
- **UX complexity risk:** too many advanced controls on first-use flows.
  - Mitigation: progressive disclosure (basic mode default, advanced toggles).

## Phased Delivery Plan

### Phase 1 (2-3 weeks): Trust + Observability foundation

- Backtest Trust Gate v2 schema + service.
- Stage-aware telemetry in backtest orchestration.
- WPF run console updates for stage visibility.

**Exit criteria:**
- Every run emits stage/timing telemetry.
- Preflight report is structured and persisted with run metadata.

### Phase 2 (2-3 weeks): QuantScript handoff + session continuity

- `ResearchSessionId` model and persistence.
- QuantScript “Run in Backtest Studio” handoff path.
- Run browser links back to source notebook/script.

**Exit criteria:**
- A user can trace any new run back to originating QuantScript artifact.

### Phase 3 (3-4 weeks): Parameter Lab v2 and robustness features

- Deterministic sweep definitions + lineage.
- Objective profiles + ranking.
- Optional IS/OOS and walk-forward mode.

**Exit criteria:**
- Sweep results are reproducible and sortable by declared objectives.

### Phase 4 (2 weeks): Comparison + promotion readiness

- Structured run comparison reports.
- Promotion checklist auto-population from artifacts.
- Export pack templates for governance review.

**Exit criteria:**
- Operators can review a comparison report and promotion evidence without manual cross-referencing.

## Test Plan

### Unit tests

- Preflight scoring/classification logic.
- Stage telemetry transitions and duration math.
- Sweep hash determinism.
- QuantScript handoff serialization/deserialization.

### Integration tests

- QuantScript execution → handoff → backtest submission.
- Backtest run persistence and read-model retrieval with session links.
- Batch sweep execution with objective ranking.

### Performance checks

- Backtest telemetry overhead benchmark (baseline vs v2).
- Sweep throughput benchmark with bounded concurrency.
- QuantScript execution queue latency under mixed workloads.

### Validation commands (targeted)

```bash
dotnet test tests/Meridian.QuantScript.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
```

## Success Metrics

- 30% reduction in time from script experiment to completed backtest run.
- 50% reduction in “failed after launch” runs due to missing coverage/config issues.
- 100% of new runs include stage telemetry + source-session lineage.
- 80%+ of sweep runs use declared objective profile and reproducible sweep hash.

## Open Questions

1. Should walk-forward be mandatory for certain strategy classes, or optional-only in this cycle?
2. Where should interpretation logic live first: `Meridian.Application` service layer or separate `Meridian.Research` module?
3. Should run-comparison exports target existing governance report pack formats or define a research-specific artifact first?
4. Do we keep Lean and native telemetry schemas aligned now, or align in the Wave 5 unification slice?
