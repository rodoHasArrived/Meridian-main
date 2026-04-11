# Governance Operator Workflow

This runbook documents the baseline governance slice in the workstation UI.

## 1) Security Master operator panels

1. Open **Governance → Security Master**.
2. Use **Security search** to query by ticker/ISIN/CUSIP/FIGI.
3. Review identifiers + classification fields from each result row.
4. In **Identifier conflicts**, resolve each open conflict with:
   - **Accept A**
   - **Accept B**
   - **Dismiss**

## 2) Multi-ledger + trial-balance view

1. Open **Governance → Ledger**.
2. Select a run from the reconciliation queue.
3. Review **Multi-ledger trial balance** rows (account/type/balance/entry counts).
4. Use **Governance exports** buttons for export preview and execution entry points.

## 3) Reconciliation break queue workflow

1. Open **Governance → Reconciliation**.
2. In **Reconciliation break queue**, move breaks through:
   - **Open** → **Assign** (moves to `InReview`, captures reviewer + assignee)
   - **InReview** → **Resolve** (moves to `Resolved`, captures resolver + note)
   - Any active break → **Dismiss** (moves to `Dismissed` with audit metadata)
3. Confirm break status/assignment metadata updates inline after each action.

## 4) Governance report/export handoff

Use the governance export entry points:

- `/api/export/preview`
- `/api/export/analysis`
- `/api/export/formats`

These routes are intended for operator handoff to downstream reporting tools and control evidence packs.
