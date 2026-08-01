---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-EXECUTION
path: src/Meridian.Execution
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-07-25
---

# src/Meridian.Execution

## Purpose

Execution owns order routing, execution services, allocation, margin, derivatives, tax-lot, and broker gateway behavior.

## Layer responsibility

This layer implements execution behavior and broker-facing runtime services while preserving paper-first and evidence-backed gating rules.

## Key folders and files

- `Interfaces/` - order gateway and execution abstractions.
- `Services/` - execution services and orchestration.
- `Models/`, `Events/`, and `Serialization/` - execution payloads and event contracts.
- `Allocation/`, `Margin/`, `Derivatives/`, and `TaxLotAccounting/` - execution support domains.

## Important workflows

Use this module for paper session execution, broker gateway behavior, order lifecycle, and execution evidence.

The OMS owns settlement of pre-trade risk reservations. `IRiskValidator` returns a
`RiskValidationOutcome` carrying any capacity a stateful rule took while evaluating (today, the
order-rate window); passing the gate is not the same as routing, so the validator transfers those
reservations rather than committing them. The OMS then settles at the routing boundary:

- gateway returns an accepted report — commit;
- gateway returns `OrderStatus.Rejected`, the order is rejected by risk or operator controls, or the
  client order id is a duplicate — roll back, because nothing reached a venue;
- gateway submission **throws** — commit. The dispatch is ambiguous and the order may still execute,
  so a rate limiter has to over-count; under-counting would let a runaway algorithm bypass the
  ceiling by producing ambiguous submissions. That path is audited with
  `Reason = OrderManagementSystem.AmbiguousSubmissionReason`, the only `OrderRejected` entry that
  keeps its slot, and is persisted with `CancellationToken.None` so a cancelled caller cannot erase
  the record of capacity the throttle still holds.

Every path between the gate and the venue must settle exactly once; a leaked reservation
permanently consumes capacity and eventually blocks every later order.

**Logging convention in this module:** caller-supplied order text — the client order id, the symbol,
and rejection reasons, which embed the symbol via rule text — is rendered through
`ExecutionLogText.ForLog` before it reaches a logger. `OrderRequest.ClientOrderId` and `Symbol` are
submitted values that nothing upstream is required to constrain (the Security Master gate is
optional), so a line break in either would render as an extra line in a text sink and let a submitter
forge execution log entries. This holds for every `_logger` call in `OrderManagementSystem`; a raw
caller value in a new log call is a defect, not a style choice.

This convention is the compensating control for excluding `cs/log-forging` in
`.github/codeql/codeql-config.yml` — that query reports log sites reached by user input rather than
unsanitized ones, and does not model `ExecutionLogText.ForLog`, so it cannot tell a fixed call site
from an unfixed one. The invariant is mechanically checkable: search `_logger.Log*` calls for
`orderId`, `request.Symbol`, or `safeRequest.Symbol` appearing without `ForLog`.
Broker-backed order placement fails closed unless `BrokerageConfiguration` names the active
gateway and all live-routing, phase, validation, and sign-off gates are explicitly green; missing
brokerage configuration remains allowed only for the default paper gateway.
After the brokerage gate allows a non-paper broker, the OMS also requires `runId` metadata,
`OrderRequest.FundAccountId`, and a registered `ILiveOrderReadinessGate` approval with a retained
evidence reference before submitting to the gateway; shared HTTP order submission must authorize any
present `FundAccountId` against the authenticated actor's account-scoped `ManageOrders` access
before the OMS can use that account in live-readiness evaluation. Missing run/account context,
missing readiness registration, rejected readiness, or an approval without retained evidence
produces an audited rejection instead of a broker submit.
Broker-backed readiness also includes open-order reconciliation: `BrokerageExecutionReconciliationService`
compares broker-reported open orders with the OMS open-order ledger, treats missing client order IDs
as untraceable breaks, and reports OMS/broker divergence before live operators rely on the gateway.
Ledger posting from trade-fill events is explicit and Security Master gated. `AddBrokerageExecution`
injects an `ITradeEventPublisher` into the OMS only when one has been composed; the execution host
does not infer an accounting book or period. A book-owning composition root can call
`AddTradeFillLedgerPosting` with a ledger factory, exact posting scope, and caller-owned
`ITradeFillPostingStore`. `WalTradeFillPostingStore` durably accepts each fill before publication
returns, retains per-fill failure/reconciliation records, replays unacknowledged fills after restart,
and acknowledges only after the required trade and commission journals exist. Postings still require
a configured validation gate, resolved Security Master identity, non-blocked validation, and journal
metadata that preserves the Security Master ID, fill ID, symbol, posting scope, and gate evidence for
provenance. `UiServer` intentionally supplies no ledger consumer by default because its paper
portfolios may own session ledgers and the host has no safe global book/period scope. A book-owning
deployment enables the production composition with
`Execution:TradeFillLedgerPosting:Enabled=true` plus non-empty `AggregateId`, `PeriodId`, and
`LedgerBookId`; the host derives one canonical scope, requires
`MERIDIAN_LEDGER_CONNECTION_STRING`, and composes the governed target, WAL store, independent
handoff-failure store, and OMS together. Changing book or period requires updating that scope and
restarting the host rather than replaying retained fills into a new accounting context.
The independent `ITradeFillHandoffFailureStore` must declare the same exact posting scope as the
ledger consumer; composition fails before startup when the scopes differ, so restart recovery cannot
route a prior book or period's fill into the current ledger. Exact identities compare the scope label,
aggregate, period, and ledger book. Legacy custom publishers and stores that expose only a label can
compose with each other by that label, but mixing an exact identity with a label-only identity fails
closed. OMS disposal closes public-operation
admission and drains operations admitted before shutdown, including an in-flight broker submit and
its synchronous fill handoff, before cancelling intake and awaiting both report and retained-handoff
pumps. The publisher or failure store can therefore be disposed only after every OMS access ends.
The book-owning caller must provide the ledger's persistence/hydration lifecycle and must not attach
this publisher to a `PaperTradingPortfolio` that already posts the same fills into that ledger.
For sell accounting, the caller must also attach portfolio state that supplies the fill's realized
P&amp;L, or publish an enriched `TradeExecutedEvent` through the abstraction instead of relying on
the OMS fallback value.
The OMS sends only fills for its own tracked orders to the accounting publisher; untracked broker
stream reports remain observable through `ExecutionReports` but cannot contaminate the configured book.
For tracked orders, each streamed fill delta is capped to the remaining broker-authorized quantity,
including an accepted quantity amendment, before portfolio state, public reports, and the accounting
publisher observe it. An oversized or repeated callback therefore cannot overstate positions or
post more than the accepted order quantity.
After a broker acknowledges a fill, the OMS admits it to the accounting publisher before attempting
cancelable paper-session history or audit bookkeeping. Report-pump shutdown likewise cannot cancel a
dequeued fill before durable accounting admission; downstream session/channel work may be cancelled
after that boundary without rewriting the broker-filled order as rejected.
Live execution controls include persisted circuit-breaker state, position limits, and manual
overrides. Run-scoped manual overrides are matched against order `runId` metadata, and submitted
paper orders that use an override carry the applied override ID, run/strategy/symbol scope, and
control decision metadata in the execution audit trail. Live broker orders evaluate operator
controls with broker-account, override, and live-readiness metadata stripped from the client
request, so client-supplied override IDs cannot bypass kill-switch or position controls before
broker routing. Orders rejected by operator controls carry stable reject codes such as
`CIRCUIT_BREAKER_OPEN`, `POSITION_LIMIT_EXCEEDED`, or `MANUAL_FORCE_BLOCK` plus the same
run/strategy/symbol audit scope so operations can distinguish policy failures from broker failures
during review. The OMS also records durable audit outcomes for
submitted, rejected, cancelled, cancel-rejected, modified, and modify-rejected order lifecycle
events with broker, order, symbol, scope, reject reason, and operation metadata for operations
review. Shared `/api/execution/controls/*` endpoints expose the snapshot plus secured mutations for
the global circuit breaker, default position limit, symbol position limits, and manual override
create/clear actions so browser and desktop clients do not need client-local execution-control
state.
OMS runtime guardrails are configuration-backed under `Execution:OrderManagement`:
`MaxRetainedOrders`, `ExecutionChannelCapacity`, and `CancelAllMaxConcurrency`. Reg T margin rates
are configuration-bindable through `Execution:Margin:RegT` while preserving the standard defaults.
Fill-report publication observes bounded-channel `WriteAsync` backpressure, and duplicate gateway
reports resume only unfinished portfolio, durable-accounting, session, or subscriber side effects.
Paper-session persistence resolves session identifiers through the Core rooted-path guard. It
rejects traversal, rooted, reserved, and ambiguous path segments, refuses existing descendant
links/reparse points, and ignores retained metadata whose session identity does not match its
directory before reading or appending session evidence.

## Diagrams

See `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-EXECUTION -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-EXECUTION -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep live actions explicitly gated and paper-first unless roadmap acceptance evidence allows broader scope.

## Related docs

- `docs/plans/paper-trading-cockpit-reliability-sprint.md`
- `docs/source/generated/source-roadmap-traceability.md`
