---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-28
---

# src/Meridian.Ui/dashboard

## Purpose

Browser workstation dashboard is the active browser operator workstation.

## Layer responsibility

This module owns the browser UI source for operator workflows. Keep shared contracts and read-model
logic in `src/Meridian.Ui.Shared` or `src/Meridian.Ui.Services` when the same behavior is consumed
by desktop or host surfaces.

The browser workstation visual system is dark-first with a compact ledger-grid treatment inspired
by the internal `cash-flow-factor-sch` reference: hard-offset shadows, sharp bordered surfaces,
dense data tables, sticky workstation chrome, and bright but semantic state accents. Keep visual
changes on the shared tokens and primitives in `src/styles/index.css` and `src/components/ui/`
instead of introducing one-off screen styling.

## Key folders and files

- `src/` - React/TypeScript workstation source.
- `package.json` - dashboard build, test, and tooling commands.
- Test files - browser workflow and component coverage.

## Important workflows

This is the active operator UI lane; keep shared contract parity with the WPF desktop. Security
Master Governance detail uses the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` projections for cash-flow schedules, factor provenance, and open-lot exposure
review.

No-host browser previews must keep fixture data visibly labeled as demo data. The shell banner
routes operators through the typed demo evidence path: watchlist, live quote evidence, trading
readiness, and provider setup, while keeping retry-to-live behavior available.

Shared workflow targets must land on the same operator lane as WPF. `FundTrialBalance` resolves to
the browser accounting ledger route (`/accounting/ledger`) so Lane B/W3 continuity actions from the
shared workflow registry do not collapse to the accounting root.
The close-lane workflow targets `OperationsContinuity` and `OperationsClose` both resolve to
`/accounting/operations-continuity`, matching the WPF Fund Operations aliases and keeping close
workflow commands backed by shared workflow metadata.
The browser page-tag route catalog is exported and covered by route tests so shared workflow target
tags stay reviewable. Workflow command labels now surface shared action tones (`Primary`,
`Warning`, `Critical`/`Blocked`) as visible command states instead of presenting every backend
workflow action as a neutral route.
Trading readiness work-item actions consume shared route metadata when it is specific enough to
resolve locally. Execution-control and promotion-review items carrying the shared
`/api/workstation/trading/readiness` route land on `/trading`, while paper-replay items keep the
session replay panel hash target so replay verification remains directly actionable.
Research run comparison and diff panels consume the richer shared workstation run comparison
contract, including artifact completeness, compatibility warnings, engine/mode context, final
equity deltas, drawdown deltas, Sharpe deltas, strategy id/version metadata, lineage relation,
and compatibility level. This keeps cross-run portfolio comparison evidence aligned with backend
run-read models instead of relying on the older P&L-only browser shape.
The Strategy run library also projects a view-model-owned run-history coverage summary from the
same shared run records so operators can see retained-run count, backtest/paper/live coverage,
paper/live-adjacent lineage, and normalized engine coverage across native and Lean-style results
before opening compare or diff.
The Portfolio run-linked equity panel now derives a portfolio comparison summary from the same
run rows. It highlights the selected run rank by P&L, best and weakest P&L, best Sharpe, and
mode/engine coverage before the operator opens a run evidence packet.
The selected-run Portfolio drill-in panel loads shared attribution, equity-curve/drawdown,
cash-flow, and fill-summary endpoints on demand so browser drill-ins stay tied to retained
run evidence instead of client-local portfolio state.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, required sign-off role/status, and decision note so browser recovery posture
matches the retained WPF Fund Ledger detail panel. When the shared queue item includes
`breakExplanation`, the browser detail panel uses the contract-owned source systems, probable
cause, ledger impact, suggested next action, and evidence links instead of rebuilding "Explain the
Break" narrative text locally.
Accounting ledger preview now calls the shared Investment Accounting Transaction Lab endpoint
(`/api/fund-structure/accounting/transaction-lab/preview`) from the ledger workstream. The browser
projects the returned expected journal, ledger impact, trial-balance impact, reconciliation
expectation, and evidence ids instead of keeping Books Before Broker preview state client-local.
Operations Continuity displays the server-derived close checklist from the shared workstation DTO,
including required evidence, owners, due dates, required approval counts, expiration dates, evidence
pointers, remediation routes, and close-readiness blockers, so the browser reads the same approval
gate state enforced by the API and WPF clients.
The Reporting Evidence Workbench keeps packet grouping, command state, export-result labels, and
retained vault artifact bundle rows in `evidence-workbench-screen.view-model.ts`. Retained vault
exports display vault id, storage kind, copied artifact path, hash, size, source route, and
canonical subject from shared evidence contracts instead of deriving those labels in JSX. It also
renders the shared Meridian Assurance score, assurance components, evidence SLA breaches, and
orphan-evidence ids from `EvidenceCompleteness`, so freshness and no-orphan posture stay backed by
the shared packet contract rather than local browser state.
The report-pack task panel also renders shared report-pack workflow restatement records when the
workspace payload includes them. Restatement review shows the reason, approver, prior report id,
changed lines, and evidence links from the shared workflow DTO rather than rebuilding restatement
state in the browser.
Settings includes the Fund Operations Control Center, which loads ledger mapping posture, the role
and permission catalog, the Operations Continuity approval policy matrix, and the account close
calendar from shared API endpoints. It also submits governed ledger-mapping assignments and custom
role-profile grants through shared contracts, and lets administrators update approval-policy
rules for reviewer role, evidence, distinct approvals, and close controls. It also lets
administrators configure account close calendar task owners and due dates. Each write preserves
actor, rationale, correlation, and audit evidence in the returned status output.

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in
`docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-DASHBOARD -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-DASHBOARD-001` | Add browser workstation route diagram coverage for paper readiness | open | medium |
<!-- source-todos:end -->

## Validation

```bash
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

## Change rules

Do not create mobile-first workflows or native mobile clients. Prefer shared read models and
endpoint contracts for behavior also consumed by WPF or host workflows.

## Related docs

- `src/Meridian.Ui/README.md`
- `docs/plans/web-ui-development-pivot.md`
- `docs/source/generated/source-module-index.md`
