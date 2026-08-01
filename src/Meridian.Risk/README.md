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

- `Rules/` - individual risk rules.
- Risk interfaces and shared validation primitives.

## Important workflows

Use this module for pre-trade checks, limits, safety gates, and execution-control evidence.

Composite risk validation runs every rule by priority and aggregates all findings, rather than
stopping at the first rejection: an order breaching several limits reports all of them. Each rule's
declared `Severity` decides admission — `Info` and `Warning` annotate and admit, `Error` and
`Critical` block — so a rule cannot contradict its own severity. A rule reports a `RiskFinding`
describing what it measured; it never chooses the outcome.

Risk decisions are log evidence, so the gate renders caller-supplied text through
`ExecutionLogText.ForLog` before logging it. Both the symbol and a rule's reason can carry it — the
F# position-limit rule embeds the symbol in its reason — and an unconstrained value containing a
line break would otherwise render as a second log line that reads like a risk decision.

Rules that consume finite capacity (the order-rate window) implement `IReservingRiskRule` and
reserve atomically during evaluation. The validator releases those reservations if evaluation
throws or is cancelled, and otherwise transfers them to the caller, which commits only once the
order is actually routed. Each rule is bounded by a timeout, and a rule that throws yields a
`Critical` `RISK_RULE_EVALUATION_FAILED` violation so the gate fails closed.

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
