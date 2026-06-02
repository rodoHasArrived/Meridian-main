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
Portfolio run evidence, Trading recent fills, Data backfill queue rows, Data export rows, and
Security Master lots.

## Important workflows

The browser workstation exposes `/accounting/entity-setup` for the shared fund-structure setup wizard. The feature posts drafts to `/api/fund-structure/setup-drafts/validate` for validation and preview, then `/api/fund-structure/setup-drafts/create` for review-and-create instead of reimplementing setup orchestration in React.


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
Browser workflow target routing accepts the desktop compatibility page tags
`ResearchShell`, `DataOperationsShell`, and `GovernanceShell`, but materializes them as canonical
browser routes under `/strategy`, `/data`, and `/accounting`.
Trading readiness work-item actions consume shared route metadata when it is specific enough to
resolve locally. Execution-control and promotion-review items carrying the shared
`/api/workstation/trading/readiness` route land on `/trading`, while paper-replay items keep the
session replay panel hash target so replay verification remains directly actionable.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, priority, SLA badge label/tone, age band, root cause, resolution code, last
comment excerpt, comment/evidence counts, related-case counts, required sign-off role/status,
source origin/fingerprint, and decision note so browser recovery posture matches the WPF desktop
Fund Ledger detail panel without reimplementing casework rules.
Accounting reconciliation narratives use canonical Accounting review language while retained
Governance view-model names remain compatibility seams.
Accounting reconciliation statement runs now use the shared statement-run endpoint/client seam for
broker or custodian, account, period, status, validation, match, break, case, and import timing
read models; React components only render these values and do not reimplement matching, tolerance,
validation, or case-state rules.
The Accounting screen also carries a stable Investment Accounting Transaction Lab panel view model
so the browser renders the Books Before Broker preview entry point without crashing while endpoint
request wiring remains a follow-on workflow.
Operations Continuity close-checklist fields mirror the shared workstation DTO, including required
approval counts, expiration dates, and close-readiness blockers, so the browser reads the same
approval gate state enforced by the API and WPF clients. The browser checklist summary is also
derived from those shared task fields: ready, blocked, acknowledged, approval, evidence-pointer,
and next-due counts are display projections only and must not become client-local close state.
The close-package panel is likewise a read-only projection of the shared operations-continuity
publication metadata: signer, sign-off rationale, retained manifest route, evidence hash, report
pack id, retained evidence links, and checklist control approvals come from the server workflow
payload rather than browser-local publication state.
The Operations Continuity detail view also renders the shared accounting-record summary from the
workflow payload. Retained source records, normalized activity, reconciliation case history, ledger
evidence, approvals, and report-pack lineage are displayed as server-owned evidence categories, not
browser-local audit-readiness rules. Each row also displays the contract-owned required evidence
labels, including document attachments, export manifests, and restatement lineage for report-pack
evidence. Browser evidence clients also carry the shared vault lookup and export linkage contract,
including `accountingRecordId`, so retained accounting-record manifests can be rediscovered through
the same API shape used by WPF and host endpoints.
No-host browser previews include the same accounting-record evidence subject, packet, validation,
manifest export, and vault-search fixture path so operators can inspect the evidence workbench demo
without a live Meridian API host.
Shared workflow targets that carry `EvidenceWorkbench:accounting-record/{recordId}` resolve to the
browser evidence workbench with the accounting-record subject query intact, matching the WPF
desktop route-parity rule while keeping the browser page tag catalog explicit. Evidence Workbench
packet actions use the same subject-aware route while displaying the operator target as
`Evidence Workbench` instead of leaking parameterized page-tag syntax. Saved workflow presets use
the same route helper, so pinned accounting-record evidence commands preserve subject and operating
scope when launched from the command palette.
Operator readiness console API source identifiers use the canonical workspace roots
`strategy`, `data`, `accounting`, and `reporting`; legacy payload type names and retained
compatibility routes must not reintroduce visible `Research`, `Data Operations`, or `Governance`
root keys. Demo fixtures, trust-gate owner lists, affected-workflow labels, and Security Master
evidence packet distribution labels follow the same canonical root naming.
The app-shell trading continuity title uses `Trading Controls` so cross-workspace recovery copy
does not reintroduce `Governance` as a visible workspace label.
Reporting workspace status rows consume shared template metadata and recent run projections for investor statements, SEC filing packets, and shadow NAV packs; React renders approval status, retry attempts, audit actions, and lineage completeness rather than reimplementing report orchestration rules.
Portfolio run drill-ins consume the shared attribution, equity-curve, cash-flow, and fill payloads
and project browser view state for run comparison, realized/unrealized P&L bridge rows, and recent
trade evidence. Keep those projections in the Portfolio view model so the React screen renders
shared run evidence instead of recalculating attribution, drawdown, or fill semantics.

Portfolio now includes `/portfolio/family-office`, a family-office route that keeps route metadata,
summary labels, empty states, disabled reasons, and ownership graph/table accessibility labels in the
Family Office view-model seam. The screen view model now derives summary panels and ownership graph
rows from a family-office entity structure shaped like the shared `FamilyOfficeOverviewDto`/
`FamilyEntityDto` contracts, so React renders entity, asset, commitment, reconciliation, and stale
valuation projections instead of carrying a separate graph fixture. The workspace navigation and
command palette surface the route from their route-catalog/view-model seams so discovery labels
remain centralized.
Strategy workspace navigation uses canonical Strategy labels for subroutes, including the retained
`/strategy/lab` route, so browser discovery does not expose `Research` as a visible root or
lane name while compatibility routes continue to resolve.
Strategy run-library live-region announcements and command failure messages also use canonical
Strategy wording while retained `Research*` DTO and component names remain compatibility seams.
Strategy Builder promotion-review warnings use risk/control wording in the browser view model,
matching the shared strategy-service validation copy while retained cell kinds remain compatibility
inputs.
The browser `DataScreen` owns the canonical Data workspace module under `src/screens/data-screen*`.
Retained `DataOperations*` DTO, endpoint, and fixture names are compatibility seams only. Data
workspace navigation and command-palette discovery surface `/data/providers` as the canonical
provider catalog and onboarding lane, alongside watchlist, quotes, alerts, and backfill queues.
The provider setup dialog includes Plaid with client-id/secret labels and bank account, identity,
and investment evidence capabilities. Treat Plaid as a server-owned account-evidence connector from
the browser: React submits credentials through the shared provider setup API and links operators
back to `/data/providers` rather than creating browser-local bank-link or market-data routing state.
Provider setup also projects the design-document Data & Integration flow from its view model:
`Connect Source`, `Acquire Data`, `Validate Data`, `Normalize Data`, `Store Data`, and `Publish Data`.
React renders that flow as status copy only; credential validation, routing, storage, and publication
state remain server/shared-service responsibilities.
The app shell workflow-continuity dock now also projects the design-document primary operator
workflow as a browser-wide strip: `Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, and
`Report`. Route-specific trails such as Market Data To Paper, Research To Paper, or Accounting Closeout remain intact,
while the primary strip anchors every workspace to the financial-operations flow.
Accounting Closeout also exposes the design-document Financial Operations lane directly:
`Receive Activity`, `Match Records`, `Resolve Exceptions`, `Approve Results`, and `Produce Evidence`.
The browser `AccountingScreen` renders the same Financial Operations lane in-screen above the
existing ledger, reconciliation, security coverage, and reporting evidence panels. Retained
`Governance*` view-model, DTO, endpoint, and test fixture names are compatibility seams only; new
browser component routing should use Accounting naming.
Workspace navigation and command-palette root commands canonicalize caller-provided workspace
metadata to the design-document root set: `Trading`, `Portfolio`, `Accounting`, `Reporting`,
`Strategy`, `Data`, and `Settings`. Legacy root labels such as `Research`, `Governance`, and
`Data Operations` remain route aliases and internal compatibility concepts only. App-shell overview
event labels also normalize retained source names before entering the visible evidence timeline.

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
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-DASHBOARD-001` | Add browser workstation route diagram coverage for paper readiness | open | medium |
| `TODO-SRC-UI-DASHBOARD-002` | Wire Accounting Transaction Lab browser endpoint requests to shared API contracts | done | high |
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
- `docs/product/meridian-design-document.md`
- `docs/architecture/desktop-layers.md`
- `docs/source/generated/source-module-index.md`

Browser reconciliation route helpers include the shared Accounting casework family for assignment, lifecycle transitions, comments, taxonomy, sign-off, reopen, audit, bulk triage, and bulk status/result lookup; keep these helpers aligned with `UiApiRoutes` and WPF consumers.

## Accounting close browser surface

The Accounting route reuses fund-operations ledger views and now includes trial-balance source-event and approval drill-through affordances. Keep browser-only rendering in `src/screens/accounting-screen.tsx` and shared accounting close contracts in `src/features/accounting/accountingCloseModels.ts`.
