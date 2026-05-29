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


## Dense row detail accessibility contract

Dense row lists that drive an adjacent detail panel must use the shared contract in
`src/components/meridian/dense-row-detail-accessibility.tsx` through `DenseDataTable` and
`DenseRowDetailPanel` instead of screen-local keyboard or ARIA implementations.

The contract standardizes:

- Arrow Up/Down, Home, and End as row focus plus selection movement.
- Enter and Space as row activation that selects the focused row and hands focus to the controlled
  detail panel.
- Escape from a detail panel as the return path to the selected controlling row.
- `aria-selected`, `aria-expanded`, and `aria-controls` on selectable rows, with labelled,
  programmatically focusable detail regions that announce updates politely.
- A table-scoped live region that announces selection changes and panel refreshes.

Current dense-row detail consumers covered by regression tests include Portfolio positions,
Portfolio run evidence, Trading recent fills, Data backfill queue rows, and Security Master lots.

## Important workflows

This is the active operator UI lane; keep shared contract parity with the WPF desktop. Security
Master Governance detail uses the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` projections for cash-flow schedules, factor provenance, and open-lot exposure
review.
The Settings workspace also owns the browser asset-profile governance surface for custom Security
Master assets: it reads approved profiles, drafts profile variants from starter templates, submits
approval/rollback lineage actions through the shared Security Master endpoints, and creates
profile-backed `CustomAsset` records pinned to the approved profile version. React should keep this
as a thin orchestration surface over shared DTOs and API validation; do not add browser-local
scripted validation rules.

No-host browser previews must keep fixture data visibly labeled as demo data. The shell banner
routes operators through the typed demo evidence path: watchlist, live quote evidence, trading
readiness, and provider setup, while keeping retry-to-live behavior available.

Refresh-capable browser modules use the shared `useRequestLifecycle` hook for request versioning,
stale response discard, unmount-safe state updates, AbortController handoff, and retry/backoff status
metadata. Keep overview bootstrap, live quotes, backfill preview/run, trading readiness handoffs, and
command-triggered refresh paths on that lifecycle instead of adding ad-hoc revision refs.

Shared workflow targets must land on the same operator lane as WPF. `FundTrialBalance` resolves to
the browser accounting ledger route (`/accounting/ledger`) so Lane B/W3 continuity actions from the
shared workflow registry do not collapse to the accounting root.
Trading readiness work-item actions consume shared route metadata when it is specific enough to
resolve locally. Execution-control and promotion-review items carrying the shared
`/api/workstation/trading/readiness` route land on `/trading`, while paper-replay items keep the
session replay panel hash target so replay verification remains directly actionable.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, priority, SLA badge label/tone, age band, root cause, resolution code, last
comment excerpt, comment/evidence counts, related-case counts, required sign-off role/status,
source origin/fingerprint, and decision note so browser recovery posture matches the retained WPF
Fund Ledger detail panel without reimplementing casework rules.
Accounting reconciliation statement runs now use the shared statement-run endpoint/client seam for
broker or custodian, account, period, status, validation, match, break, case, and import timing
read models; React components only render these values and do not reimplement matching, tolerance,
validation, or case-state rules.
Operations Continuity close-checklist fields mirror the shared workstation DTO, including required
approval counts, expiration dates, and close-readiness blockers, so the browser reads the same
approval gate state enforced by the API and WPF clients. The browser checklist summary is also
derived from those shared task fields: ready, blocked, acknowledged, approval, evidence-pointer,
and next-due counts are display projections only and must not become client-local close state.
The close-package panel is likewise a read-only projection of the shared operations-continuity
publication metadata: signer, sign-off rationale, retained manifest route, evidence hash, report
pack id, retained evidence links, and checklist control approvals come from the server workflow
payload rather than browser-local publication state.
Reporting workspace status rows consume shared template metadata and recent run projections for investor statements, SEC filing packets, and shadow NAV packs; React renders approval status, retry attempts, audit actions, and lineage completeness rather than reimplementing report orchestration rules.
Portfolio run drill-ins consume the shared attribution, equity-curve, cash-flow, and fill payloads
and project browser view state for run comparison, realized/unrealized P&L bridge rows, and recent
trade evidence. Keep those projections in the Portfolio view model so the React screen renders
shared run evidence instead of recalculating attribution, drawdown, or fill semantics.

Portfolio now includes `/portfolio/family-office`, a family-office route that keeps route metadata,
summary labels, empty states, disabled reasons, and ownership graph/table accessibility labels in the
Family Office view-model seam. The React screen renders household net worth, entity and asset-class
breakdowns, cash/liability posture, private assets, unfunded commitments, reconciliation breaks,
stale valuation warnings, and a keyboard-navigable ownership graph with a dense table fallback.

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

## Portfolio alternatives browser surface

Portfolio now includes `/portfolio/alternatives` as a private alternatives register reachable from
Portfolio navigation and the family-office route. The screen keeps fixture/import-backed private
asset records in a browser view-model seam until custodian and private-market integrations are
available, and renders explicit operator warnings for `manual valuation`, `statement imported`,
`unreconciled`, `missing source document`, and `stale NAV` across the asset list, detail drawer,
commitment summary, valuation history, document/evidence panel, capital activity timeline, and stale
data warnings.

## Related docs

- `src/Meridian.Ui/README.md`
- `docs/plans/web-ui-development-pivot.md`
- `docs/source/generated/source-module-index.md`

Browser reconciliation route helpers include the shared Accounting casework family for assignment, lifecycle transitions, comments, taxonomy, sign-off, reopen, audit, bulk triage, and bulk status/result lookup; keep these helpers aligned with `UiApiRoutes` and WPF consumers.

## Accounting close browser surface

The Accounting route reuses governance ledger views and now includes trial-balance source-event and approval drill-through affordances. Keep browser-only rendering in `src/screens/governance-screen.tsx` and shared accounting close contracts in `src/features/accounting/accountingCloseModels.ts`.
