# Strategy Engine Foundation

Meridian's Strategy Engine foundation turns strategy work into an executable, validated, and
auditable workflow instead of a set of disconnected scripts or browser screens.

## Current Architecture Map

- **Strategy definitions:** `StrategyEngineDefinition` describes stable strategy ID, version, type,
  description, asset universe, required data inputs, parameter schema, supported modes, owner,
  source, UI metadata, and strategy-specific extensions.
- **Parameters:** `StrategyEngineParameterDefinition` captures typed values, defaults, bounds,
  allowed values, validation rules, display metadata, and sweep hints.
- **Data dependencies:** `StrategyEngineDataDependency` declares price bars, quotes, fundamentals,
  Security Master, options chain, positions, open lots, portfolio, ledger, corporate actions, and
  backfill dependencies with missing-data policy.
- **Run requests:** `StrategyEngineRunRequest` captures strategy/version, parameters, universe,
  date range, data source, run mode, cost/slippage assumptions, risk constraints, run reason, and
  operator identity.
- **Run validation:** `StrategyEngineValidationService` validates definitions, modes, date ranges,
  parameters, dependency availability, degraded-data behavior, and live-disabled posture before a
  request becomes durable run history or a promotion candidate.
- **Evidence:** `StrategyEngineRunEvidence` records input hash, data source references, warnings,
  review route, evidence route, and report-pack route for each validation pass.
- **Browser/API surface:** `GET /api/workstation/strategy/engine/definitions` exposes registered
  definitions, and `POST /api/workstation/strategy/engine/validate-run` validates requests before
  execution.

## Execution Pipeline

The Strategy Engine pre-run path is:

1. Load a registered `StrategyEngineDefinition`.
2. Validate the requested run mode.
3. Validate the universe and date range.
4. Validate typed parameters, bounds, allowed values, and required values.
5. Compare declared data dependencies with supplied data-availability evidence.
6. Block required missing data, or mark the request degraded when the dependency policy allows it.
7. Compute a deterministic input hash from the canonical request.
8. Emit an evidence manifest with strategy, data, warning, and workstation handoff routes.

Execution runners such as Strategy Designer, QuantScript, covered call, backtest, replay, and paper
validation should call this pre-run path before recording `StrategyRunEntry` results or exposing a
promotion handoff.

## Extensibility Rules

New strategy types should register explicit definitions instead of scattering parameter and data
rules across React components, ad hoc scripts, or one-off endpoints. A strategy definition should
include:

- parameter schema with validation bounds;
- declared data dependencies and missing-data policy;
- supported modes such as backtest, replay, simulation, paper validation, review only, and
  live-disabled;
- UI metadata for browser workstation routes;
- source/owner metadata for audit traceability.

This is intentionally not a dynamic plugin framework. It is a shared contract and validation seam
that can stabilize Strategy Designer, QuantScript, covered call, and future strategy types before
Meridian adds broader dynamic loading.

## Safety Boundaries

- Live promotion remains represented as `LiveDisabled`; validation does not place live orders.
- Missing required data blocks execution unless the dependency explicitly allows degraded evidence.
- Validation returns actionable findings with workstation handoff routes instead of generic errors.
- Tests use synthetic availability records and do not require provider credentials or live trading
  endpoints.
