---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-RISK
path: src/Meridian.Risk
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-05-20
---

# src/Meridian.Risk

## Purpose

Risk contains pre-trade validation rules and order-safety checks used before execution workflows proceed.

## Layer responsibility

This layer owns risk decision logic and reusable rules. It should stay independent of UI presentation and broker-specific adapter details.

## Key folders and files

- `Rules/` - individual risk rules: position limit, drawdown circuit breaker, order-rate
  throttle, and the portfolio-aware gross-exposure, symbol-concentration, and order-notional
  gates.
- `PortfolioExposure.cs` - `IPortfolioExposureProvider` and the exposure snapshot the
  portfolio-aware rules consume (fed from `IAggregatePortfolioService` by the host).
- Risk interfaces and shared validation primitives.

## Important workflows

Use this module for pre-trade checks, limits, safety gates, and execution-control evidence.
Composite risk validation runs rules by priority and uses synchronous fast paths when a rule
exposes one. Rule severity maps to a real outcome in `CompositeRiskValidator`:

- `Info`/`Warning` - the breach becomes a warning flag on the result; evaluation continues.
- `Error` - the order is rejected (first rejection stops evaluation, so attribution and
  latency stay predictable on the order path).
- `Escalate` (or a rule returning `RiskValidationResult.Escalated`) - the order parks in the
  execution-side governed-approval queue (`RiskEscalationQueueService`); an operator approval
  arms a one-shot, fingerprint-matched release token that lets exactly that order back through
  the escalation while every other rule still enforces.
- `Critical` - the order is rejected and the execution circuit breaker
  (`ExecutionOperatorControlService`) trips, halting routing until an operator closes it.

Portfolio-aware rules read a `PortfolioExposureSnapshot` per evaluation, so thresholds tuned
through the UI runtime service apply immediately and enforcement always sees the same
aggregated cross-run exposure the Portfolio workspace reports. Thresholds are operator-tuned
(null means unconfigured and the rule approves); capped buy limits resolve from their limit,
while uncapped orders require a current symbol reference price. A configured rule rejects an
order it cannot price rather than guessing or approving it unmeasured.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-RISK -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-RISK -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Risk changes must be deterministic, explainable, and covered by scenario tests before affecting execution behavior.

## Related docs

- `docs/plans/paper-trading-cockpit-reliability-sprint.md`
- `docs/source/generated/source-roadmap-traceability.md`
