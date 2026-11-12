# Release notes — 3.5.0 (12 Aug 2026)

**Strategy formation — one identity, one artifact.** The six-stage formation redesign lands as a
first-class Meridian capability. Full change ledger: `CHANGELOG.md › 3.5.0`; migration notes:
`docs/UPGRADING.md › → 3.5`; QA evidence and deferrals: `docs/QA_REPORT_3.5.0.md`.

## The contract layer

- `strategy` is the **seventeenth object** in the ObjectModel ledger.
- `core/StrategyModel` (family `coordination`) exports `STRATEGY_LIFECYCLE`,
  `STRATEGY_FORMATION_STEPS`, and the pure carrier: stage gates recomputed from current values,
  canonical mjcf/1 JSON + SHA-256, hash-bound review acknowledgment, checkpoint snapshots,
  guarded lifecycle transitions.
- `operations/StrategyRunModel` (family `evidence`) exports `STRATEGY_RUN_AXES` and the carrier
  for the immutable **StrategyRunArtifact v1**: input hashing, `verifyProof`, derived
  `freshness(artifact, draft)`, one-time metric derivation (ACT/365.2425 CAGR, rolling-peak
  drawdown, declared-method Sharpe, win rate, holding period, turnover, fees — missing evidence
  is null **plus** an issue, never a fixture value), deep-freeze finalization, legacy `Run`
  adapters. Environment · run lifecycle · validation disposition · proof integrity · freshness
  are independent axes.
- Wire shapes: `guidelines/ENTITY_SCHEMAS.md` (Strategy · StrategyFormationSnapshot ·
  StrategyRunArtifact v1).

## The product flow

Seven executor screens on one shared draft and artifact, one nav rail, one suite store
(`meridian.strategyExecutor.store.v1`; fixture-first with a visible session-only fallback), and
URL-carried identity (`strategy`/`run`/`runA`/`runB`/`formationStage` — never artifact JSON):

Formation (DecisionFlowProvider + WorkspaceContextBar + ObjectHeader + TimeContext +
ActionGuardList + ContinuityFrame + version-conflict handling) → Builder & Field & Formula (one
worksheet) → Backtest (draft snapshots into a new immutable artifact; stale proofs gate export
and staging; revert-to-run-inputs) → Runs (derived freshness, trace, gates, rerun) → Compare
(compatible completed artifacts; declared-method deltas; promote gated) → Governance (lifecycle
transitions; version- and proof-bound attestations whose standing/superseded state is derived).

Adapters: Basket Builder **Use in strategy** → Portfolio rules; Covered-Call Lab on the shared
artifact with deterministic scenario recompute, share/contract reconciliation, and the headline
figure relabeled **Period return** (annualized CAGR shown alongside).

## Accessibility extension

Additive across Stepper, SegmentedControl, Slider, Tabs, Select, WorksheetGrid, DenseDataTable,
ChartFrame (+ nine wrappers), StatusBanner, DataStateSwitch, MetricCard. Role-semantic notes in
`docs/UPGRADING.md`; keyboard map in `guidelines/ACCESSIBILITY.md › 3.5`.

## Tooling and tests

`scripts/` restores the documented offline tooling (inventory, governance, catalog, scaffold,
compile-plan validation, release gate; stdlib-only) with `THIRD_PARTY_NOTICES.md`;
`tests/unit.html` runs the offline unit suite in any browser; `tests/BROWSER_TEST_PLAN.md`
prepares — and explicitly defers — keyboard/ARIA (NVDA) and responsive validation.
