# Governance Operator Workflow

This runbook documents the baseline governance slice in the workstation UI.

## 1) Security Master operator panels

1. Open **Governance → Security Master**.
2. Use **Security search** to query by ticker/ISIN/CUSIP/FIGI.
3. Review identifiers + classification fields from each result row.
4. Use **Refresh Workflow** or the live poller to confirm the ingest posture from `GET /api/security-master/ingest/status`.
5. In **Conflict Queue**, resolve each open ingest-time identifier conflict with:
   - **Accept A**
   - **Accept B**
   - **Dismiss**
6. Use **Operator Next Steps** to continue directly into:
   - **Fund Portfolio**
   - **Fund Ledger**
   - **Reconciliation Queue**
   - **Cash & Financing**
   - **Report Pack**

## 2) Multi-ledger + trial-balance view

1. Open **Governance → Ledger**.
2. Select a run from the reconciliation queue.
3. Review **Multi-ledger trial balance** rows (account/type/balance/entry counts).
4. Use the explicit **Report Pack** tab to preview governance handoff content inside the same fund context.
5. From **Portfolio**, **Cash & Financing**, **Trial Balance**, and **Audit Trail**, keep Security Master and report-pack drill-ins in the same operator journey.

## 3) Reconciliation break queue workflow

1. Open **Governance → Reconciliation**.
2. In **Reconciliation break queue**, move breaks through:
   - **Open** → **Assign** (moves to `InReview`, captures reviewer + assignee)
   - **InReview** → **Resolve** (moves to `Resolved`, captures resolver + note)
   - Any active break → **Dismiss** (moves to `Dismissed` with audit metadata)
3. Confirm break status/assignment metadata updates inline after each action.

## 4) Governance report/export handoff

Use the explicit **Open Report Pack** action from either **Security Master** or **Governance → Ledger** to stage the reporting handoff, then use the governance export entry points:

- `/api/export/preview`
- `/api/export/analysis`
- `/api/export/formats`

These routes are intended for operator handoff to downstream reporting tools and control evidence packs.

## 5) Release evidence

Before release, capture journey evidence that covers:

1. Security Master ingest polling and conflict resolution.
2. Security Master → fund portfolio / ledger / reconciliation / cash / report-pack drill-ins.
3. Governance report-pack preview continuity inside the fund workspace.
4. Reconciliation queue actions and downstream reporting handoff.
