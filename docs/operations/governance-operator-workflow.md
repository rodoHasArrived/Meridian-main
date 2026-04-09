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
2. Use the sticky context strip to confirm the active fund, legal entity, base currency, default ledger scope, workspace as-of date, and last refresh timestamp before triaging exceptions.
3. Review the KPI/filter strip for:
   - **Open Breaks**
   - **In Review**
   - **Runs**
   - **Security Issues**
4. Narrow the queue with:
   - break filters: **Open**, **In Review**, or **All**
   - scope filter: **All**, **Strategy**, or **Account**
   - local search (`Ctrl+F` focuses the search box)
5. Work from the left queue pane:
   - **Break Queue** shows actionable strategy-run breaks sorted by status priority, then largest variance, then newest detection time
   - **Runs** shows strategy and account reconciliations sorted exception-first
6. Inspect row-level detail in the right pane:
   - **Exceptions** shows the active mismatch set and highlights the break selected from **Break Queue**
   - **All Checks** shows matched + unmatched checks together
   - **Security Coverage** shows unresolved Security Master gaps
   - **Audit** shows freshness and operator-action history
7. For strategy-run break queue items, move breaks through:
   - **Open** → **Start Review** (moves to `InReview`, captures reviewer + assignee)
   - **Open/InReview** → **Resolve** (moves to `Resolved`, requires resolver + note)
   - **Open/InReview** → **Dismiss** (moves to `Dismissed`, requires resolver + note)
8. Use **Open Account Workflow** for account-level reconciliations. Account detail is read-only inside Governance in this pass.
9. Confirm break status, assignment metadata, and last-refresh cues update inline after each action without losing the selected row.

## 4) Governance report/export handoff

Use the governance export entry points:

- `/api/export/preview`
- `/api/export/analysis`
- `/api/export/formats`

These routes are intended for operator handoff to downstream reporting tools and control evidence packs.
