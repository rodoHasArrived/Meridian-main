# Middle-Office Operations

Middle-office managed-service patterns expressed as internal workflow primitives. This area is a
self-contained, in-memory service that composes over — rather than replaces — the platform's existing
reconciliation matching, break classification, and secure-distribution pipeline.

## Surface

`MiddleOfficeOperationsService` provides:

- **T+0 booking / T+1 reconciliation** — `BookTrade` records a trade on trade date (T+0), derives its
  settlement date from the instrument's settlement cycle, and exposes the T+1 date its
  trade / cash / position reconciliation is due (`TradeBooking`, `ReconciliationDimension`).
- **True-break escalation** — `RaiseTrueBreak` escalates only genuine or potential breaks, keyed to
  the canonical `Meridian.Domain.Reconciliation.BreakClassification`. Escalations advance a level when
  their SLA breaches (`TrueBreakEscalation`).
- **Generic SLA timers** — `WorkflowSlaTimer` / `WorkflowSlaPolicy` compute on-track / warning /
  breached / stopped states for any middle-office deadline (booking cut-offs, break resolution, file
  distribution). This is a general timer, distinct from the reconciliation-specific
  `ReconciliationSlaCalculator`.
- **Normalized file distribution** — `Distribute` fans a file out to normalized recipients
  (`Administrator`, `Custodian`, `Counterparty`), producing an **archived delivery log** of immutable
  `FileDeliveryRecord` entries.

## Immutable delivery & escalation log

Every escalation, SLA breach, and delivery is mirrored into the shared
`Meridian.Ledger.FundAdministrationEventLog`, so the middle-office governance trail is append-only and
hash-chained alongside the fund-administration control events.
