---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-09
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
Settings also renders the scoped-access assignment console over the shared Auth API. The browser
lists active or revoked assignments, grants principal authority with role, optional profile, scope,
permission names, effective dates, approval-limit metadata, segregation-of-duties rule text,
rationale, and correlation metadata, and revokes assignments with the current assignment version so
optimistic-concurrency and audit evidence remain server-owned.

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
session replay panel hash target so replay verification remains directly actionable. This is the
browser PaperSession route handoff covered by `DIA-BROWSER-WORKSTATION` and
`DIA-PAPER-SESSION-REPLAY`; keep route changes aligned with those diagram records.
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
The Accounting external-GL panel renders the shared accounting-system reconciliation evidence
packages for external import, Meridian ledger support, and GL tie-out posture when the API returns
them, keeping package readiness and required actions service-owned rather than deriving package
state from browser table rows.
The Accounting screen also carries a stable Investment Accounting Transaction Lab panel view model
so the browser renders the Books Before Broker preview entry point without crashing while endpoint
request wiring remains a follow-on workflow.
The Accounting workspace workflow launch strip is derived from the Accounting view model and shared
route catalog, covering setup, journal entries, ledger review, reconciliation, exception casework,
Security Master readiness, approvals, and retained evidence packaging without browser-local close
state.
`FinancialRecordExplorerShell` is the browser presentation for the shared Financial Record Explorer
DTO. Accounting loads the `ledger` and Accounting-hosted `security-instrument` explorers from
`/api/workstation/financial-record-explorers/{explorerId}`, Portfolio loads `portfolio`, Reporting
renders the shared `report-line-provenance` explorer from the reporting payload, and Data links to
the existing Security Master lane instead of restoring the old static Data workbench.
Saved views post back through the shared saved-view endpoint only after a material filter/search
change, and blocked or empty DTOs keep proof actions disabled with the server-provided reason.
Operations Continuity close-checklist fields mirror the shared workstation DTO, including required
approval counts, expiration dates, and close-readiness blockers, so the browser reads the same
approval gate state enforced by the API and WPF clients. The browser checklist summary is also
derived from those shared task fields: ready, blocked, acknowledged, approval, evidence-pointer,
and next-due counts are display projections only and must not become client-local close state.
Operations Continuity also renders the shared close-calendar read model from
`getOperationsCloseCalendar`, scoped by the selected workflow fund account and period, so due task,
owner, readiness, blocker, checklist, approval, and route posture stay server-owned workflow-control
evidence rather than browser-local calendar state.
Checklist rows also display shared acknowledgement command posture and the expected workflow
version guard beside retained acknowledgement evidence, keeping close-control acknowledgement
available through the operations-continuity command API instead of browser-local checklist state.
When a checklist task is command-ready, the screen posts acknowledgement through the same shared API
with the workflow version guard, browser operator actor, rationale, and correlation id, then
refreshes from the server-owned workflow payload.
Operations Continuity break assignment and escalation rows likewise render owner, due date,
SLA state/due time, materiality, root cause, approval posture, blocked downstream outputs,
escalation, variance, suggested action, retained evidence counts, and the first local retained
evidence route from the shared workflow payload. They also route operators to the Accounting
exception casework lane with workflow and break context while assignment/escalation and resolution
commands stay on the shared operations-continuity API instead of browser-local break state.
The same rows expose guarded assignment and resolution commands: unassigned breaks can be assigned
to the browser operator with the workflow version, escalation, due-date, and rationale carried to
the shared API, while assigned breaks can resolve only when retained evidence links are present.
The browser API client exposes the same shared operations-continuity command spine for workflow
start, broker import/normalize, gate posture refresh, Security Master resolution, ledger
draft/validate/post, reconciliation run, and approval submission so browser surfaces can call
server-owned workflow transitions without adding local Financial Operations rules.
The Operations Continuity screen projects that spine from shared dashboard metrics as read-only
Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close
Support rows with server-owned guard, evidence, and route labels. The Approve Results row also
exposes a guarded approval-submission command when the selected workflow has no open breaks,
report-pack readiness includes a retained report-pack id, and reviewer ownership can be resolved
from the shared workflow payload. The Produce Evidence row exposes a guarded close-package
publication command only when the shared close-readiness payload is ready and the workflow supplies
retained report-pack evidence plus checklist-control approvals for the close request; permission,
transition, and evidence-hash enforcement remain server-owned.
The Operations Continuity screen also renders the shared Financial Operations operational
dashboard. Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence,
and Close Support metric state, retained evidence, route hints, and required actions come from the
workflow detail payload, so the browser dashboard is a display projection rather than a local
workflow engine. The same screen now renders the workflow detail `Reviewed automation` summary as
read-only stage, review-state, allowed-use, prohibited-action, retained-evidence, required-action,
and reviewed-output artifact posture so automation review remains visible without browser-local
automation policy.
The Financial Operations operator queue on that screen is also derived from workflow detail, the
shared close-calendar projection, and the private-capital close cockpit: reconciliation break cases,
non-ready reconciliation lane posture, workflow blockers, close checklist tasks, close-calendar due
items, non-ready private-capital proof lanes, NAV support packages, workflow approvals,
evidence-package readiness, and non-ready Receive Activity, Match Records, Resolve Exceptions,
Approve Results, Produce Evidence, and Close Support command stages stay source-backed while React
only groups the active work items for review. If the close-calendar or private-capital close cockpit
projection cannot load, the queue adds a blocked unavailable item so workflow control fails closed.
The same workflow approval payload also feeds the approval-history table with submission timing,
reviewer/operator attribution, rationale, status, and retained approval evidence routes. Pending
rows expose guarded approve/reject commands only when the selected workflow supplies version,
reviewer, report-pack, and checklist-control evidence required by the shared operations API; the
server still owns permission, transition, audit, and evidence validation.
It also renders the shared evidence-package table from the workflow detail payload: accounting
record evidence, report-pack evidence, close-package manifest, and audit-support package rows show
server-owned readiness, category completeness, retained evidence counts, local routes, and required
actions instead of stitching package state together in React.
The Accounting approvals workstream at `/accounting/approvals` reads the same operations-continuity
workflow list/detail payload and posts approve/reject decisions through the shared approval
endpoints, keeping signer, report-pack, blocker, and audit-trail evidence server-owned.
The bootstrap hook also fetches the shared workflow-summary endpoint and passes the active
`fundAccountId` from route or stored shell scope when that account scope is present, allowing the
Accounting Closeout strip to project source-backed Operations Continuity exceptions, approval
history, close readiness, and evidence package posture instead of overloading profile identifiers.
The app shell consumes the shared Financial Operations `Reviewed automation` evidence badge as an
operator-focus item only when the badge requires review. React may route the operator back to the
source-backed workflow step, but it must not approve, post, publish, release payments, erase
evidence, or invent autonomous automation state outside the shared summary. Browser DTO typings now
carry `OperationsReviewedAutomationSummary` plus optional material-command `actionOrigin` fields,
while the shared endpoints force authenticated human origin before Financial Operations accepts
ledger posting, approval, payment release, report publication, report delivery package creation,
close-package publication, or governed reopen commands.
The close-package panel is likewise a read-only projection of the shared operations-continuity
publication metadata: signer, sign-off rationale, retained manifest route, evidence hash, report
pack id, retained evidence links, and checklist control approvals come from the server workflow
payload rather than browser-local publication state.
The browser API client also exposes `getPrivateCapitalCloseCockpit` for the shared v0.18
private-capital close cockpit endpoint, keeping fund/book/period/entity close lanes server-owned
instead of rebuilding close readiness, delivery, report, or period-lock posture in React.
The Operations Continuity screen renders that private-capital cockpit as a compact read-only lane
and scoped-workflow panel, including shared readiness, blockers, fund-event counts, report-output
delivery, capability labels, server-routed next action state, and a Financial Operations proof-lane
summary for partner capital tie-outs, expense/fee allocation review, NAV support, evidence packages,
and period-lock evidence. It also renders shared approval history from workflow approval decisions
and checklist-control approvals, private-capital evidence package rows for fund-event accounting,
partner capital tie-outs, NAV support, and close approval/audit evidence, plus NAV support package
rows for positions, cash, pricing, shadow NAV, and retained evidence links so approval,
evidence-package, and NAV-support evidence are visible without browser-local timeline or
report-output reconstruction. Approval-history and evidence-package rows preserve the first local
retained evidence route alongside the workflow or package route so audit evidence remains directly
reachable from the close cockpit tables.
The period lock and reopen evidence panel stays read-only as well: locked/open/reopened posture,
close audit hash, close package hash, reopen incident correlation, rationale, and retained reopen
evidence are derived from the shared workflow status and timeline. The panel can submit a governed
reopen packet only when the selected workflow is closed and an operator enters incident id,
approval reference, justification, and impact summary; the shared endpoint still owns actor,
governed-admin permission, close-lock, and transition enforcement.
The Operations Continuity detail view also renders the shared accounting-record summary from the
workflow payload. Retained source records, normalized activity, reconciliation case history, ledger
evidence, approvals, and report-pack lineage are displayed as server-owned evidence categories, not
browser-local audit-readiness rules. Each row also displays the contract-owned required evidence
labels, including document attachments, export manifests, and restatement lineage for report-pack
evidence. Browser evidence clients also carry the shared vault lookup and export linkage contract,
including `accountingRecordId`, so retained accounting-record manifests can be rediscovered through
the same API shape used by WPF and host endpoints.
The same detail view renders contract-owned reconciliation lane coverage for cash, position, trade,
income, MBS factor, bank, and GL support. Lane readiness, break counts, retained evidence counts,
first local retained evidence routes, lane route hints, and required actions come from the
Operations Continuity payload rather than browser-local reconciliation heuristics.
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
Evidence Workbench also renders the shared v0.18 Operational Evidence Graph proof-chain coverage
for Source, Normalization, Reconciliation, Ledger, Capital accounts, Close, Reporting, Delivery,
and Audit layers before the existing lineage and node inspectors. Keep this panel driven by the
packet `proofChain` contract rather than browser-local node classification. Manifest export results
also render the shared vault request-list groups and support request rows beside retained artifacts,
including target kind, target id, highest severity, evidence kinds, missing or blocked evidence,
source system, work item, and blocked output metadata from the server contract instead of rebuilding
audit, tax, close, or report-package request lists in React. The workbench also calls the shared
`/api/workstation/evidence/vault/request-lists` endpoint to show open frozen request lists for the
selected subject, or the latest open vault request lists when no subject is selected. Retained
artifact rows also surface server-provided capture channel/source metadata and extracted-field
validation summaries so the browser does not infer document intake, confidence, or reviewer state
locally. Evidence nodes may also carry optional metadata for typed proof identifiers such as
report-pack delivery package ids; React treats that metadata as server-owned context and does not
parse node summaries to rebuild vault linkage.
Operator readiness console API source identifiers use the canonical workspace roots
`strategy`, `data`, `accounting`, and `reporting`; legacy payload type names and retained
compatibility routes must not reintroduce visible `Research`, `Data Operations`, or `Governance`
root keys. Demo fixtures, trust-gate owner lists, affected-workflow labels, and Security Master
evidence packet distribution labels follow the same canonical root naming.
The app-shell trading continuity title uses `Trading Controls` so cross-workspace recovery copy
does not reintroduce `Governance` as a visible workspace label.
Reporting workspace status rows consume shared template metadata and recent run projections for investor statements, SEC filing packets, and shadow NAV packs; React renders approval status, retry attempts, audit actions, and lineage completeness rather than reimplementing report orchestration rules.
The Reporting workspace also renders the shared `AccessAudit` summary from `WorkstationReportingPayload`,
showing matched user/group/company scopes plus aggregate visible/hidden counts for templates, report
packs, schedules, deliveries, and structured exports. React displays the service-owned denial
reasons without probing or naming hidden report objects.
Report-pack delivery history rows link delivered packages to the shared `report-pack-delivery`
Evidence Workbench subject using the backend `reportId:attemptId` identity, so React does not build
delivery evidence packets or audit graph state locally. Publication review and delivery package
panels render retained line provenance with the server-provided Financial Record Explorer hrefs
rather than constructing ledger, portfolio, or Security & Instrument routes in React. The delivery
history also renders backend-owned access, channel, and download summaries for email-link,
secure-portal, evidence-vault, and internal-route packages instead of deriving recipient-facing
copy from URL shape. When the shared package includes an access-expiry timestamp, React displays it
beside the token-gated delivery link so operators can see when email-link or portal access closes.
Schedule delivery-plan cards render the latest retained delivery access expiry, access/channel summary, and entitlement
scope from the shared plan payload beside artifact integrity, retained download summary, notification proof, report-writer
dataset/grid summaries, branding, and retained access links, so operators can verify private or restricted scheduled packages without
opening the package manifest first.
The Reporting workspace also renders shared `portfolioCuts` rows for fund, strategy, and tag
reporting views. The browser shows exposure, cash, P&L, shadow-NAV, variance, source-count, and
version stamp from the backend payload instead of recomputing those cuts in React.
Structured export cards render regulatory, data-warehouse, and investment-decision rows from the
shared `structuredExports` payload, including retained paths, retained manifest paths, schema
version, source counts, integrity summaries, raw SHA-256 hashes, data-dictionary links, evidence
links, and classification tags before exposing JSON/CSV/XLSX downloads. Operator-facing XLS or
Excel requests use the shared route's `format=xls`/`format=excel` aliases and receive the same
canonical `.xlsx` workbook artifact. The shared download routes stamp JSON, CSV, and XLSX responses
with export id, generated-at, actor, company, report group, version, and integrity headers while
keeping browser tables read-only over backend-owned export state.
Cross-fund consolidation cards follow the same rule: fund/entity counts, gross/net exposure, cash,
P&L, shadow-NAV, variance, source counts, readiness, and drill-through routes are rendered from
`crossFundConsolidations` without browser-local roll-up math.
Report-pack delivery cards render branding evidence from the shared package payload, while the
download routes keep the actual styled artifact generation in shared services. HTML/PDF package
downloads apply the selected theme colors, firm, logo, footer, and disclaimer server-side, and XLSX
packages retain a Branding worksheet, so the browser exposes branded packets without recreating
presentation rules in React. Email-link and secure-portal access links open token-gated shared HTML
package views; callers that need the raw manifest use the same route with `format=json`.
It also renders shared `livePortfolioViews` rows for tick-linked portfolio reporting. React shows
the backend-owned live/source-backed/stale/blocked state, gross/net exposure, cash, pending
settlement, P&L, shadow NAV, liquidity and telemetry copy, source/cut freshness stamps, and links to
the portfolio-summary and cash-ladder routes rather than deriving live freshness locally. The rows
also render contract-owned market tick timestamp, tick age, safe tick sequence, provider label,
tick freshness summary, freshness-policy thresholds, policy reason, and live-link flag so operators
can distinguish true live-linked portfolio telemetry from retained source-backed snapshots. The
Reporting route participates in the same portfolio refresh lane as Portfolio so the server-owned
`livePortfolioViews` payload is refreshed with the source portfolio evidence while preserving
backend freshness classification. The Reporting live-views panel exposes that shared refresh action
directly and also schedules a bounded 60-second auto-refresh only when at least one live view is
`LiveLinked` or marked with `isMarketTickLinked`, so tick-linked reporting views follow the shared
portfolio route without introducing browser-local market-data state. It shows success or failure
feedback from the portfolio refresh lane instead of asking operators to leave Reporting.
`LiveLinked` is rendered only when the shared backend marks the source snapshot inside its freshness
window; otherwise the browser preserves the emitted `SourceBacked`, `Stale`, or `Blocked` state and
renders any contract-owned readiness blockers instead of inventing browser-local live-data copy.
It also renders shared `pnlSlices` rows for daily, weekly, monthly, and yearly P&L. React shows
date windows, realized/unrealized/current/prior/change values, source-backed or blocked posture,
readiness text, version stamps, and backend routes from the payload instead of fabricating browser
period bridges.
The Reporting workspace reuses the shared private-capital activity projection and readiness state
already carried by the manual journal workbench. React projects fund-event ledger records,
capital-account subledger references, ledger impacts, retained evidence categories, approval state,
and report-output posture into a read-only reporting panel, including the shared distinction between
published output and report-ready output when report-specific evidence is missing. It must not
invent browser-local fund-event eligibility, capital-account roll-forward, ledger-impact, or
report-ready rules.
It also renders shared `analyticsRows` rows for Top-N winners, Top-N laggards, and contribution
breakdowns. React shows security/strategy/asset-class scope, rank, P&L, contribution percent,
heat-map intensity, source counts, readiness text, version stamps, and backend routes from the
payload instead of recalculating portfolio contribution analytics in the browser.
It also renders shared `crossFundConsolidations` rows for company, fund, and legal-entity rollups.
React shows source counts, exposure, cash, P&L, readiness, version stamps, and backend routes from
the payload instead of aggregating multi-fund state in the browser.
It also renders shared `structuredExports` rows for regulatory, warehouse, and investment-decision
outputs. The browser displays readiness, format, row/field/source counts, schema version, retained
path, retained manifest path, row-lineage count, SHA-256 integrity summary, raw SHA-256 hash,
dataset id, as-of timestamp, version stamp, exact backend API route, and direct backend links from
the contract. Each ready export exposes JSON, CSV, and XLSX download actions by normalizing the
shared retained route's `format` query instead of building browser-local export payloads. Blocked
exports stay visible with validation copy, but the browser renders contract-owned readiness blockers
and disabled download controls without anchor `href` values until the backend marks the descriptor
ready.
It does this instead of deriving export inventory from report-profile labels.
The Reporting workspace also renders shared `brandingThemes` rows with firm identity, built-in or
custom posture, color swatches, logo URI, footer text, and disclaimer copy. When the payload carries
fund context, React can first preview the governed BoardPacket through the shared report-pack
preview endpoint, then generate a governed BoardPacket report pack with PDF/XLSX/CSV artifacts by
posting the selected `brandingThemeId` to the shared report-pack endpoint; without fund context the
commands stay disabled instead of using a browser-local default. The preview request carries the
selected branding theme id, and the preview status displays the service-owned report totals,
trial-balance line count, asset-class sections, and normalized branding identity without calculating
those values in React. Operators can also preview and generate a one-off custom branded pack by
entering a theme id, firm name, colors, logo URI, footer, and disclaimer; the browser posts the
shared `BrandingThemeOverride` contract for preview and generation and lets the backend normalize,
validate, and retain the branded PDF/XLSX/CSV artifacts.
It also renders shared `scheduleDeliveryPlans` rows for scheduled report packs, including recipient,
channel, delivery mode, PDF/XLS/CSV formats, readiness, retained package links, access-expiry
timestamps, retained download summaries, latest artifact counts, checksum integrity summaries,
schedule/package branding theme, and version stamps from the backend payload. Each ready
delivery-plan row can also run its owning schedule through the shared schedule-run endpoint and
reports the returned run id, delivery count, recipient-specific delivery count, and target
delivery mode in the common schedule status lane.
Those rows render typed drilldown links and next-action references from the shared payload, opening
browser-safe evidence routes while executing shared POST actions for approval submission/review,
publication, archive, and report-pack delivery. Restatements remain guarded by changed-line
evidence requirements instead of being submitted as a one-click browser action.
The run cards also surface exact audit metadata from the shared run projection, including run id,
template id, as-of date, trigger, status, attempt count, section count, linked-lineage count, and
retained artifact names, so version-control review does not depend on parsing a prose lineage
summary.
The Reporting workspace also renders operator-managed schedule rows from the shared schedule
payload and wires schedule save/upsert, due-run, run-now, pause, and resume controls through the shared
schedule endpoints. Schedule drafts default from the current schedule, approved template, and
distribution payload before posting back to the server. Retained delivery attempts render from the
shared delivery-history payload, so browser Reporting shows actual
recipient delivery state and retry history instead of static status chips. Delivered attempts also
show the shared package mode, requested artifact formats, secure link, retained manifest path, and
publication-approved branding theme, plus token-gated package artifact download links when the backend includes
`ReportPackDeliveryPackageDto`; app-relative secure links render as anchors so operators can open
the token-gated email-link or secure-portal package page, while artifact `DownloadRoute` values
render as direct retained PDF/XLSX/CSV links with retained path, byte size, evidence id, SHA-256
checksum, and version stamp integrity details. When the package or schedule plan carries
`accessLinks`, the browser renders labelled access chips for secure portal/email package access,
operator routes, retained manifests, and token-gated artifact downloads instead of exposing only
raw secure-link strings. Workbook delivery packages can include an `artifact-xls` compatibility
access link; React renders that server-provided `format=xls` route as an XLS package download while
the backend keeps the canonical XLSX artifact and content type. When the package includes
server-owned delivery notifications, React renders the notification subject, status, recipient,
created/expires timestamps, body, and token-gated package href so operators can review the email-link
or secure-portal outbox evidence without deriving it from raw secure links. Delivery-history rows can also record a failed email-link or portal
delivery through the shared delivery-failure endpoint, preserving the original attempt as evidence
for the failed retry path instead of keeping failure state only in operator notes. When a schedule targets a custom report-writer
template that has not produced a published report-pack workflow record, those package rows still
render because the backend now falls back to the generated reporting run and includes its retained
manifest/report-writer artifact provenance on the delivery package; the browser renders the
reporting run, template, schedule, report-writer dataset source, source-artifact provenance, and
generated-run evidence-packet package contents, support evidence IDs, delivery evidence links,
access entitlement, recipients, approval chain, request history, amendments/restatements, audit
references, and blocked downstream outputs beside the package links.
When the package carries generated report-writer grid metadata, the delivery panel also shows the
grid title, kind, dimension/metric/formula counts, and validation summary counts from the shared
package manifest instead of parsing source-artifact strings. If the package also carries rendered report-writer grids, the same
panel shows rendered row/column counts, retained data-dictionary fields, generated-field counts, and
backend validation checks so operators can distinguish descriptor-only packages from source-backed
pivot, Top-N, contribution, and formula output delivered by the backend.
Operators can also save or update schedule records
from Reporting by choosing a template, cron, as-of date, due timestamp, recipient distribution,
delivery mode, and PDF/XLSX/CSV formats, then staging multiple delivery targets before save so a
single governed schedule can distribute secure-portal, email-link, evidence-vault, or internal-route
packs to separate recipients. The browser posts the shared `ReportingScheduleUpsertRequestDto`
instead of maintaining local schedule rules, including optional governed dataset rows when the
backend has supplied them for report-writer grids and the current custom branding override so
recurring generated-run packages retain the selected firm styling. Schedule cards also show configured delivery targets
and the run-now status reports returned delivery counts or warnings from
`ReportingScheduleRunResultDto`, keeping generated report runs distinct from packaged
client/internal distributions. The backend applies the same governed template access policy to
schedule saves and manual schedule runs as it does to ad-hoc report runs, so browser controls cannot
schedule or execute user-locked custom templates outside the authorized owner, group, or company.
The Reporting payload also omits schedule rows, schedule-delivery-plan rows, and delivery attempts
whose template or report-pack workflow is not visible to the current user, so browser state cannot
reveal locked recipients, cadence, package links, or delivery status for another user or group.
Approved accessible report template rows, including governed custom report-writer templates, expose
an on-demand `Run report` command that posts to the shared
`/api/fund-structure/reporting/runs` endpoint and reports the generated ad-hoc run id back to the
operator. Draft, in-review, rejected, superseded, or inaccessible template rows stay disabled for
this command so browser Reporting cannot bypass shared template approval and access policy gates.
When the backend generates an approved custom no-code template, retained
`report-writer://.../grids/{gridId}` artifacts flow back through recent-run rows so browser
Reporting can show that pivot, Top-N, contribution, and custom-formula grids were part of the
actual run evidence, not just a preview-only authoring state. Recent-run cards and delivery
package metadata expose JSON, CSV, PDF, XLS, and XLSX links for each retained grid through the shared
`reportingRunReportWriterGridEndpoint` helper, keeping grid download URLs contract-derived. The
browser XLS link uses the shared `format=xls` compatibility alias and receives the canonical
workbook artifact from the backend, while the browser PDF link uses the shared `format=pdf` route
for allocator-ready retained-grid previews.
The Reporting workspace also exposes `/reporting/operations-record` as the polished W1-W5 release
path. It stays under the canonical Reporting root and projects the loaded Data provider posture,
Operations Continuity accounting-record evidence, close-package state, and report-pack publication
metadata into one fail-closed demo path from source data to accounting record to report pack.
Report-pack distribution UI consumes shared `reportPackDistributions` recipient records, not
static target strings, so the browser surface shows recipient, channel, shared delivery state,
owner, pending item count, due/last-sent posture, route, and pending summary for each governed
package delivery lane.
The Reporting report-pack task also renders publication metadata from the shared backend workflow
record, including signed-off-by, evidence hash, retained manifest path, publication time, and
retained evidence-link count instead of using browser-only publication aliases. Publication evidence
links render as their own retained list, so sign-off manifests and approval packets remain
inspectable even when they are not attached to a specific report line.
The Reporting template panel also renders the shared template authoring lifecycle: built-in versus
custom source, draft/in-review/approved state, latest-approved posture, approval summary, and the
server-owned authoring route for drafting or reviewing a version. No-host fixtures include both an
approved built-in template and an in-review custom revision so the browser surface demonstrates the
version-approval workflow without adding browser-local lifecycle rules. Custom draft/rejected rows
post submit-for-review decisions to
`/api/fund-structure/reporting/templates/{templateName}/versions/{version}/submit`, and in-review
rows post approval or rejection decisions to the matching `/approve` and `/reject` shared
governance endpoints. When the shared payload marks a template with report-writer grids, the browser
summarizes those grid counts from `reportWriterGrids` and renders a no-code grid designer with
source fields, row/column zones, metrics, formulas, grid-type and Top-N controls, sort posture, and operator-authored
custom formula name/label/expression fields plus saved filter controls for field/operator/value
slicing. Source fields come from the shared report-writer field catalog when present, so the browser
palette can include source-backed portfolio, analytics, consolidation, and generated contribution
fields even when a saved grid has not already placed those fields. The designer also renders a live layout summary from the current draft zones so operators
can confirm dimension, metric, formula, filter, and Top-N posture before preview or save. Operators can save the current grid layout as a
governed template draft through
`/api/fund-structure/reporting/templates/drafts`, including company-wide, restricted group/company,
or user-locked access policy metadata. Placed row, column, metric, and formula tokens can be
reordered, moved between zones, or removed individually before preview or save, so operators can
correct a draft without resetting the whole grid. The designer renders the computed draft policy beside the
controls and locks private drafts to a user owner before save. The designer Preview action posts the current unsaved layout
plus an explicit preview dataset profile and bounded sample rows to
`/api/fund-structure/reporting/templates/render`, then renders the returned grid rows, columns,
column roles, data-dictionary fields, backend validation checks, labelled warnings, and audit trace
before publication. Operators can switch previews across combined retained reporting rows, dedicated
portfolio-cut rows, Top-N/contribution analytics rows, cross-fund consolidation rows, and the
certified operational data-mart source from `reportWriterDatasetSources`, representative
portfolio-position, ledger-fact, cash-ladder, and pasted custom JSON/CSV row shapes while the
shared renderer still owns pivot, Top-N, contribution, filter, and formula output. Each
source-backed option posts the selected payload-owned rows to the render endpoint; the browser also
renders the source-owned field catalog with role and data-type metadata before preview. Formula
preview row generation recognizes brace references, bare identifiers, `total(...)`, and shared
formula functions such as `abs(...)`, `min(...)`, `max(...)`, `safeDivide(...)`, `percent(...)`,
`basisPoints(...)`, and `round(...)`, so local sample rows include the same formula source fields
the shared renderer will evaluate. The
certified operational data-mart option is server-projected with row-lineage keys, lineage manifest
pointers, evidence-index links, source run ids, validation/reconciliation posture, certification
state, and permitted consumers; the browser should display those payload fields instead of deriving
mart metadata locally. The browser uses bounded fixture rows only for the explicit fixture preview
profiles. When an operator changes a draft to a
Contribution grid, the browser preview request prefers the P&L metric as the contribution base,
sorts by `contributionAbsPercent`, and omits generated contribution fields from sample rows so the
shared renderer remains the source of truth for signed and absolute contribution percentages. That trace displays input/output and
filtered-input row counts, source fields, metric source mappings, formula expressions/dependencies,
and filter lineage from the shared renderer. Grid calculation, formula evaluation, filter application,
approval, and retained template truth remain server/shared-service owned.
Approved report-writer template runs and schedules can also choose one of those retained dataset
sources; the browser sends `datasetSourceId` for on-demand and scheduled report-writer automation
instead of embedding raw source rows in every request. Recent-run cards render the resolved source
label and row count returned by the shared run projection, so operators can see which retained
dataset powered the generated grids without parsing artifact names.
Template cards also show a shared access-governance panel with company-wide, user/group, or
user-locked mode, resolved scope, runnable versus blocked posture, and the backend access summary;
inaccessible template rows stay disabled instead of allowing browser-local report runs. Template
cards also render shared audit and version-control metadata: based-on template version,
latest-approved posture, retained audit-event count and last transition, validation issue count,
reviewer, decision rationale, and approval reference all come from the Reporting payload. Recent
generic Reporting run cards use the shared audit drilldown route
`/api/fund-structure/reporting/runs/{runId}/audit` for browser-navigable audit links, so operators
can inspect retained actor/timestamp/action/notes rows directly from the run card.
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
The same screen renders the shared data-upload intake panel from the `WorkstationDataPayload`
template catalog. React builds CSV downloads and submits selected files to the shared preview
endpoint only; validation, retained source evidence, and any downstream reconciliation or approval
handoff remain server/shared-service owned.
The provider setup dialog includes Plaid with client-id/secret labels and bank account, identity,
and investment evidence capabilities. Treat Plaid as a server-owned account-evidence connector from
the browser: React submits credentials through the shared provider setup API and links operators
back to `/data/providers` rather than creating browser-local bank-link or market-data routing state.
The Settings Provider Connection Center also captures QuickBooks Online client id, client secret,
refresh token, company realm id, and optional company name through the same shared provider setup
API. React never handles access-token exchange directly; the shared provider service uses that
local config to mark the selected QuickBooks company ready for read-only GL evidence import. Its
inline credential editor renders provider fields and environment choices from shared
provider-connection metadata rather than hard-coded browser provider forms.
Provider setup also projects the design-document Data & Integration flow from its view model:
`Connect Source`, `Acquire Data`, `Validate Data`, `Normalize Data`, `Store Data`, and `Publish Data`.
React renders that flow as status copy only; credential validation, routing, storage, and publication
state remain server/shared-service responsibilities.
The Data provider cockpit consumes the shared `/api/providers/readiness` model as its primary
command-center source, then enriches rows with existing provider connection, routing, trust, and
workspace evidence. React renders provider health, broken credential state, degradation/fallback
evidence, credential field metadata, allowed environments, and the next recovery action from that
shared model instead of recalculating provider readiness locally.
The app shell workflow-continuity dock now also projects the design-document primary operator
workflow as a browser-wide strip: `Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, and
`Report`. Route-specific trails such as Market Data To Paper, Research To Paper, or Accounting Closeout remain intact,
while the primary strip anchors every workspace to the financial-operations flow.
Accounting Closeout keeps the design-document Financial Operations lane in the app-shell continuity
dock with Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence,
and Close Support while the browser `AccountingScreen` focuses on the owned ledger, reconciliation,
approvals, exceptions, security coverage, and reporting evidence panels. Retained `Governance*` view-model, DTO,
endpoint, and test fixture names are compatibility seams only; new browser component routing should
use Accounting naming.
The Accounting ledger workstream includes a GL account inquiry surface: operators can filter
trial-balance ledger rows by General Ledger account text, select a narrowed account row, inspect
attached ledger-line support when the shared payload includes journal references, and open linked
review packets, source events, journal references, and approval evidence without moving posting
logic into React. Missing journal, source-event, or approval references remain explicit empty states
instead of browser-derived support rows.
The same `/accounting/ledger` route is the first browser implementation of the shared Financial
Record Explorer pattern from the design document. It wraps the existing shared trial-balance,
ledger-line, reconciliation, evidence-packet, audit-packet, and report-usage read models with
explorer scope, saved-view labels, filter chips, summary signals, and proof drill-through actions
without creating a separate browser ledger state or adding new root navigation.
That shared explorer shell also wraps `/portfolio` and `/accounting/security-master`: Portfolio
anchors open holdings, selected run evidence, brokerage posture, and coverage proof to the existing
Portfolio view model, while Security Master anchors instrument search, identity evidence, conflicts,
schedules, lots, and trading controls to the Accounting-owned Security Master view model.
The Accounting journal-entry workstream at `/accounting/journal-entries` is a thin browser surface
over the shared manual journal entry workbench endpoints. React renders draft headers, GL account
selection, selected-line Security Master search/picker results, line validation badges, typed source
evidence attachments, treasury-context readiness, save draft, validate, and submit approval commands
from the shared DTOs while versioning, validation, persistence, private-capital fund-event context,
evidence gating, and approval handoff remain server-owned. The same workstream renders the shared
private-capital activity projection as fund-event rows, capital-account aggregates, signed net
activity, ordered capital-account subledger movements with running net activity, posted fund-event
counts, ledger-impact readiness, published report-output counts, report-output readiness
candidates, report-pack workflow/publication/provenance metadata, per-event ledger-record counts,
and projection warnings rather than deriving capital-account, GL-impact, or stakeholder-package
state in React. The browser
API catalog also exposes `/api/ledger/private-capital/activity`,
`/api/ledger/private-capital/fund-event-record`,
`/api/ledger/private-capital/fund-event-command-center`,
`/api/ledger/private-capital/capital-account-subledger`,
`/api/ledger/private-capital/report-output`, `getPrivateCapitalActivity`,
`getPrivateCapitalFundEventRecord`, `getPrivateCapitalFundEventCommandCenter`,
`getPrivateCapitalCapitalAccountSubledger`, `getPrivateCapitalReportOutput`, and the Settings
backend-capability diagnostics so operators can verify the first-class private-capital review
endpoints outside the manual journal editor. Accounting fund-event ledger rows and Reporting
private-capital readiness rows expose the shared fund-event command-center route so operators can
reconstruct evidence, workflow, ledger, capital-account, treasury, reconciliation, report,
delivery, tax, and audit support from either workspace. The activity API helper accepts fund, ledger-book,
fund-event, capital-account, investor, and payment-intent filters for report-pack and cash-evidence
drill-throughs, while the direct record helpers resolve one fund event, one capital account, or one
report output to the same server-owned DTOs the aggregate projection carries. Payment-intent rows
render the shared `/api/ledger/private-capital/activity?paymentIntentId=...` route supplied by the
backend so operators open the filtered proof-chain projection instead of an ignored manual-journal
query. They also render the shared approval chain, retained bank/cash evidence with retaining
operator attribution, reconciliation links, audit events, payee, account scope, business purpose, approval policy, and retained source
evidence counts as a read-only cash-evidence drilldown while preserving the v0.18
execution-deferred posture. `/accounting/capital-accounts` consumes
`getCapitalAccountWorkbench` from the shared capital-account workbench endpoint so React renders
investor-level capital-account evidence, governed allocation policy traces, approval and replay
inputs, statement/restatement changed-line lineage, audit drill-through rows, and
live-versus-planned capability labels without browser-local accounting rules. The same workbench
now promotes source-backed fund-event command-center rows from the investor-account records, keeping
command-center, activity, and evidence routes visible beside partner capital tie-out posture.
Browser report-output rows prefer the server-built direct report-output route before falling back to
the aggregate report route. The React view model preserves
posted-event and published-output labels from the shared DTOs and renders server-owned fund-event
ledger records as the primary event-level table before the account-level capital-account subledger,
decomposed movement, GL-impact, and report-output tables. The account-level subledger table renders
the shared subledger route, opening/ending roll-forward, activity totals, approval/posting/report
counts, validation issue count, account-level readiness label/reason, next-action route, and
evidence-category readiness from
`PrivateCapitalCapitalAccountSubledgerDto`. Event-ledger rows display promoted
memo, payment/settlement reference, gross activity, capital-account opening/ending net activity,
row-count, canonical activity route, evidence-packet route, approval route when an approval id is
present, primary report route, workflow, provenance, readiness badge/reason, and next-action route
from the shared event record rather than reopening nested arrays for basic posture. The same table
renders the shared evidence-category readiness set for source support, capital-account subledger,
ledger impact, approval state, and report output so operators see the retained-evidence gaps behind
the event posture. The browser type model treats those projection arrays as non-null shared-contract
collections, so an empty private-capital state renders as empty tables instead of client-local
fallback counts.
Report-output rows also display the server-projected readiness label, reason, next action, and
next-action route from `PrivateCapitalReportOutputDto`, rather than reducing report posture to a
browser-local ready/review boolean.
This browser slice is deliberately a read/review surface for the unified private-capital event
ledger and account subledger model; React must not grow cap-table administration, broad LP portal,
native live-payment execution, full forecasting, or Backtesting Studio behavior from these rows.
The Accounting entry screen now includes a CFO / Controller close command center that derives
ready, blocked, and at-risk close posture from the latest operations-continuity workflow, retained
accounting-record evidence, reconciliation breaks, approvals, external GL provider warnings,
multi-asset valuation readiness, report-pack readiness, and close-package sign-off state. React
renders the shared status, metrics, blockers, and action rows without adding browser-local close
rules. External GL provider warnings compare read-only provider evidence against Meridian-owned
ledger truth; they must not make the external GL the source of ledger authority. When QuickBooks
Online local config is ready, the Accounting GL evidence panel previews the selected company
instead of the deterministic fixture; without that config it remains on `quickbooks-fixture`.
During workstation bootstrap, the shell lets the Accounting route render its own actionable loading
workspace. That loading view shows the route/workstream being prepared, the Accounting payload groups
still loading, and links to continuity, entity setup, provider posture, and retained report evidence
so operators are not left with only generic shell diagnostics.
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
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-DASHBOARD -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-DASHBOARD-001` | Add browser workstation route diagram coverage for paper readiness | done | medium |
| `TODO-SRC-UI-DASHBOARD-002` | Wire Accounting Transaction Lab browser endpoint requests to shared API contracts | done | high |
<!-- source-todos:end -->

## Validation

```bash
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
npm --prefix src/Meridian.Ui/dashboard run smoke:workstation
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

Browser extensibility route helpers expose the shared core extensibility catalog plus tenant-template activation-readiness, activation, and activation-history endpoints; keep `src/types.ts` aligned with `Meridian.Contracts.Extensibility` instead of adding UI-local workflow or rule shapes.

## Accounting close browser surface

The Accounting route reuses fund-operations ledger views and now includes trial-balance source-event and approval drill-through affordances. Keep browser-only rendering in `src/screens/accounting-screen.tsx` and shared accounting close contracts in `src/features/accounting/accountingCloseModels.ts`.
