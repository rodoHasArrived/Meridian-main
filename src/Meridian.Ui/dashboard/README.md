---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-27
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
Trading readiness work-item actions consume shared route metadata when it is specific enough to
resolve locally. Execution-control and promotion-review items carrying the shared
`/api/workstation/trading/readiness` route land on `/trading`, while paper-replay items keep the
session replay panel hash target so replay verification remains directly actionable.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, required sign-off role/status, and decision note so browser recovery posture
matches the retained WPF Fund Ledger detail panel.
Operations Continuity close-checklist fields mirror the shared workstation DTO, including required
approval counts, expiration dates, and close-readiness blockers, so the browser reads the same
approval gate state enforced by the API and WPF clients.

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

Browser reconciliation route helpers include the shared Accounting casework family for assignment, lifecycle transitions, comments, taxonomy, sign-off, reopen, audit, bulk triage, and bulk status/result lookup; keep these helpers aligned with `UiApiRoutes` and WPF consumers.
