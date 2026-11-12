# Meridian 3.5 — Strategy Formation Extension · Change & QA report

Date: 2026-08-12 · Release: 3.5.0 (additive minor)

## What shipped
- **Strategy = object #17** with full lifecycle (draft → validating → blocked/ready-for-review → in-review → approved/changes-requested → active/suspended → superseded/retired); public `StrategyModel`, `STRATEGY_LIFECYCLE`, `STRATEGY_FORMATION_STEPS`.
- **Contracts**: `StrategyFormationSnapshot` + immutable `StrategyRunArtifact` v1 in `guidelines/ENTITY_SCHEMAS.md`; environment, run lifecycle, validation disposition, proof integrity, and draft freshness kept as independent axes.
- **`StrategyRunModel`**: canonical SHA-256 input hashes, proof freshness, once-only metric derivation from raw series/trades (total/benchmark/excess return, ACT/365.2425 CAGR, rolling-peak drawdown, declared-method Sharpe, win rate, holding period, turnover, fees; missing evidence ⇒ null + validation issue), finalization, legacy Run adapters.
- **Product flow**: DecisionFlowProvider formation (persisted by Strategy ID/version + formationStage URL state, gates recomputed from current values, edits invalidate dependents); suite store `meridian.strategyExecutor.store.v1` with URL → store → fixture identity precedence and safe read-only fallback; strategy/run/runA/runB carried across relative navigation; no artifact JSON in URLs; stale/current derived centrally from draft hash — editing material inputs gates export/promotion/approval.
- **Screens**: seven Strategy Executor screens on the shared draft + artifact; Basket Builder "Use in strategy" → Portfolio rules; Covered-Call Lab on the shared artifact (scenario recompute, shares/contracts reconciliation, corrected period-return label, repaired navigation).
- **Accessibility (additive)**: Stepper progress semantics; SegmentedControl roving radio focus; Slider label association; Tabs panel-ref hygiene; named Select combobox; WorksheetGrid full grid semantics + active-descendant + focus restore; DenseDataTable regions/captions, keyboard row activation, accurate virtual positions, labeled selection, keyboard resize/reorder/pin; ChartFrame `ariaLabel`/`ariaDescription`/`accessibleData` forwarded through wrappers; StatusBanner/DataStateSwitch configurable announcements; MetricCard value/delta/context/trend/tone/sparkline descriptions. No prop or callback removed.
- **Tooling**: offline `scripts/` (Python 3 stdlib only) — compile-plan validation (`compile_bundle_extensions.py`), inventory generation, static validation, release gate (`validate_release.py`); `THIRD_PARTY_NOTICES.md`. Bundle/manifest/adherence/inventory are regenerated idempotently, never hand-edited.

## QA — completed checks
- **Unit tests** (`tests/unit.html`, offline): **49/49 pass** — schemas, lifecycle transitions, canonical hashing, invalidation, revert-to-current, metrics, time coherence, legacy adapters, persistence failures, migrations, URL identity, exports/manifest parity. (Two time-coherence failures found and fixed: the harness now stubs React for module eval; no network.)
- **Static/generated checks**: component exports vs manifest, compiler idempotence, template contracts, route integrity, ARIA-reference integrity, source/generated parity — pass via `scripts/validate_release.py`.
- **Screen smoke (console-error pass)**: Formation, Strategy Builder, Field & Formula, Backtest Builder, Strategy Runs, Run Compare, Governance, Basket Builder, Covered-Call Lab — all load with zero errors. Two real defects found and fixed during smoke: `ChartCard` readout contract violations in Backtest Builder and Run Compare (string passed where `{label,value}[]` expected).
- **Acceptance journey** exercised on the fixture: complete formation → resolve worksheet blocker → run → edit dataset (stale proof + gated actions observed) → revert/rerun → compare → approve → edit again (approval superseded).

## Deferred (not claimed in this delivery)
- Keyboard/ARIA validation with a real screen reader (NVDA) and responsive browser matrix — prepared in `tests/`, requires a browser install that is out of scope.
- The obsolete setup-wizard thumbnail is archived under `docs/archive/`; the formation concept image remains labeled as a design reference.

## Assumptions honored
Fixture-first persistence only; no backend, live execution, network dependency, or browser installation. Ships additive with role-semantic migration notes in `docs/UPGRADING.md`.
