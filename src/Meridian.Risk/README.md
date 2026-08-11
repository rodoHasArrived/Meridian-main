---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-RISK
path: src/Meridian.Risk
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-08-11
---

# src/Meridian.Risk

## Purpose

Risk contains pre-trade validation rules and order-safety checks used before execution workflows proceed.

## Layer responsibility

This layer owns risk decision logic and reusable rules. It should stay independent of UI presentation and broker-specific adapter details.

## Key folders and files

- `Rules/` - individual risk rules: position limit, drawdown circuit breaker, order-rate
  throttle, the order-entry fat-finger gate, and the portfolio-aware gross-exposure,
  symbol-concentration, and order-notional gates.
- `PortfolioExposure.cs` - `IPortfolioExposureProvider` and the exposure snapshot the
  portfolio-aware rules consume (fed from `IAggregatePortfolioService` by the host).
- Risk interfaces and shared validation primitives.

## Order-entry fat-finger gate

`Rules/FatFingerRule.cs` catches mistyped orders before any portfolio rule sees them
(`Priority = -10`, severity `Error`), so an inflated order is attributed to the slip rather than
to whichever exposure ceiling its size happened to breach. Both thresholds are operator-tuned
through the runtime service and null-disables the limb; an entirely unconfigured rule approves
without measuring.

- **Quantity ceiling** - the largest quantity any single leg actually routes, which for a package
  is `Quantity x RatioQuantity` rather than the top-level count. Skipped for a broker-notional
  order, whose `Quantity` field carries dollars rather than a share count; those are gated by
  `OrderNotionalRule` instead.
- **Price-deviation band** - directional, not symmetric. Only the aggressive side is measured
  (a buy paying above the reference, a sell hitting below it), because a symmetric band would
  reject the entire resting book.
- **Wrong-side stop trigger** - the mirror of the band above. A correctly placed buy stop sits
  *above* the market and a sell stop *below* it, so a buy stop typed beneath the market is already
  crossed and a stop-market order that triggers on acceptance routes with no price protection at
  all. Trigger deviation is measured on the wrong side only, so a protective stop placed correctly
  never breaches however far away it sits.

The two price limbs read *different* references, because a limit and a trigger answer different
questions:

- **A limit** is measured against `IPortfolioExposureProvider.TryGetTouchPrice` — the raw crossing
  side of the book, not the conservative `TryGetExecutablePrice` valuation mark the sizing rules
  use. Sizing must never *under*-measure the exposure an order creates, so its mark takes the larger
  of mid and touch; a price control must compare against what the order can actually trade at.
  Measuring a sell against that mark would make an ordinary marketable sell at the bid look priced
  through the market by half the spread.
- **A trigger** is measured against `TryGetTriggerReferencePrice` — last trade, then bar close,
  then the crossing side — the same *precedence* `PaperOrderMatchingPolicy` uses
  (`LastTradePrice ?? BarClose`, quote only as a last resort). Consulting a quote earlier disagrees
  with the matcher whenever the two differ, in both directions: with a 100/120 quote and a 100
  print, a buy stop at 105 is resting but looks crossed against the ask or midpoint; with a 130
  print, a buy stop at 125 sits above the ask and looks correctly placed while the matcher fires it
  immediately. Dropping the bar-close leg reproduces the second failure on a bar-driven session,
  where there is no print at all. A control that disagrees with the engine about whether an order
  has already triggered is not measuring the same market the engine is.

Note that these limbs gate **submission**. `OrderManagementSystem.IsRiskIncreasing` revalidates only
quantity increases and numerically *higher* limit or stop prices, so an amendment that moves a price
the dangerous way — a sell limit downward, or a buy stop down onto the wrong side of the market —
does not currently re-enter the validator. Closing that is an OMS-wide change tracked on
`W9-SAFETY-007`, not a property of this rule.
- **A stop-limit's limit** is measured against *its own trigger*, which is what it is priced off.

Both thresholds are read as a single `FatFingerThresholds` value rather than two accessors, so an
evaluation cannot straddle a two-field configuration update and observe a pair that never existed.

Market, auction (`LimitOnOpen`/`LimitOnClose`), trailing-stop, and multi-leg orders contribute no
price at all; `FatFingerRule`'s type remarks carry the reason for each exclusion. With the band
configured, a measurable order whose symbol has no reference price is refused as
`FAT_FINGER_UNMEASURABLE` rather than approved — a band an unpriceable order sails past is not a
band — and that refusal is deliberately unmeasurable rather than a breach, so a pricing gap does not
trip the circuit breaker.

## Important workflows

Use this module for pre-trade checks, limits, safety gates, and execution-control evidence.
Composite risk validation runs rules by priority and uses synchronous fast paths when a rule
exposes one. Rule severity maps to a real outcome in `CompositeRiskValidator`:

- `Info`/`Warning` - the breach becomes a warning flag on the result; evaluation continues.
- `Error` - the order is rejected.
- `Escalate` (or a rule returning `RiskValidationResult.Escalated`) - the order parks in the
  execution-side governed-approval queue (`RiskEscalationQueueService`); an operator approval
  arms a one-shot, fingerprint-matched release token that lets exactly that order back through
  the escalation while every other rule still enforces.
- `Critical` - the order is rejected and the execution circuit breaker
  (`ExecutionOperatorControlService`) trips, halting routing until an operator closes it.

Portfolio-aware rules read a `PortfolioExposureSnapshot` per evaluation, so thresholds tuned
through the UI runtime service apply immediately and enforcement always sees the same
aggregated cross-run exposure the Portfolio workspace reports. Thresholds are operator-tuned
(null means unconfigured and the rule approves); order notional resolves from the limit/stop
price or the symbol's reference price and never guesses a price for unknown symbols.

Every rule is evaluated before any decision is taken, so an order breaching several limits reports
all of them rather than the first one encountered. The outcome is still chosen by the first blocking
rule in priority order — the severity map above is unchanged — but the full set is carried on
`RiskValidationResult.Violations`, each attributed to the rule that raised it with that rule's own
declared severity and a stable code. Evaluation is side-effect free by contract, which is what makes
evaluating past a block safe.

Rules that consume finite capacity (the order-rate window) implement `IReservingRiskRule` and
reserve atomically during evaluation. The validator releases those reservations on every
non-approved decision and on any exception, and otherwise transfers them to the caller on
`RiskValidationResult.Reservations`; the OMS commits only once the order has actually routed. A rule
that throws or exceeds the per-rule timeout yields a fail-closed `RISK_RULE_EVALUATION_FAILED`
refusal that is deliberately *unmeasurable* rather than a breach — the rule established nothing, so
a flaky rule refuses its own orders without tripping the circuit breaker and halting the desk.

The per-rule timeout bounds *asynchronous* evaluation only. It does not bound `TryEvaluate`, nor any
synchronous work a rule performs before returning its `Task`: both run on the calling thread, and a
synchronous call cannot be abandoned, so bounding them would leak the blocked thread while still
hanging the submission. The contract is therefore that rules do not block — they either use the
synchronous fast path because they need no I/O, or they return promptly and wait inside the task,
where the timeout applies.

Risk decisions are log evidence, so the gate renders caller-supplied text through
`LogSanitizer.Sanitize` before logging it. Both the symbol and a rule's reason can carry it — the
F# position-limit rule embeds the symbol in its reason — and an unconstrained value containing a
line break would otherwise render as a second log line that reads like a risk decision.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-RISK -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
| `W9-SAFETY-007` | Kill-switch cancel-all and fat-finger, notional, and collar rules |
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
