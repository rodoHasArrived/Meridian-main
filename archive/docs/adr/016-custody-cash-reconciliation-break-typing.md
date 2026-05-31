# ADR 016: Distinct custody-position and cash reconciliation breaks with shared workflow envelope

- Status: Accepted
- Date: 2026-05-01

## Context

Custody/position reconciliation and cash reconciliation represent distinct failure modes.
Position breaks are symbol and quantity-centric, while cash breaks are currency and balance-centric.
However, operators triage and resolve both using the same queue, assignment, and sign-off workflow.

## Decision

- Model two explicit break types:
  - `PositionReconciliationBreak`
  - `CashReconciliationBreak`
- Provide a shared `AccountReconciliationBreak` envelope for aggregation across UI/API workflows.
- Persist under a single storage table name (`account_reconciliation_breaks`) with a `break_type` discriminator.

## Consequences

- Reconciliation modeling is clearer and easier to evolve per break family.
- API and UI keep one queue/workflow contract for assignment/review/resolve.
- Storage naming is stabilized early to avoid avoidable migration churn.
