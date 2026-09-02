---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-DASHBOARD
path: src/Meridian.Ui/dashboard
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-09-02
---

# src/Meridian.Ui/dashboard

First launch is browser-primary. `/setup` renders the first-run concierge while the
shared first-run API remains the source of truth for starter kits, sample safety labels,
recommendations, and completed activation outcomes. Sample mode stays offline-capable
and visibly labelled `SAMPLE · PAPER` throughout the shell. Until activation status is known, the
normal shell stays closed and a failed status read exposes a retry instead of assuming setup is complete.

After setup the masthead `Getting started n/m` chip opens the activation checklist, which lists
every outcome the host tracks and routes to the surface that completes the next one. Completion is
reported by the surface that did the work -- statement import commit, reconciliation break
resolution, report run, and analysis export each call `recordActivationOutcome` in
`src/lib/first-run/activation.ts` -- so the count never advances on a page visit alone.

## Purpose

Browser workstation dashboard is the active browser operator workstation.

## Layer responsibility

This module owns the browser UI source for operator workflows. Keep shared contracts and read-model
logic in `src/Meridian.Ui.Shared` or `src/Meridian.Ui.Services` when the same behavior is consumed
by desktop or host surfaces.

The browser workstation visual system is light-first and aligned to the Meridian Design System's
Institutional Ops palette: paper canvas, white cards, near-black masthead/status chrome, hairline
borders, shallow shadows, Segoe UI body/display type, and Cascadia/JetBrains Mono data text. Keep
visual changes on the shared tokens and primitives in `src/styles/index.css` and `src/components/ui/`
instead of introducing one-off screen styling.

## Key folders and files

- `src/` - React/TypeScript workstation source.
- `src/app-shell.command-palette.ts` - app-shell command-palette trigger and keyboard shortcut view models.
- `src/app-shell.development-fixture-notice.ts` - app-shell no-host demo-data notice and evidence path view models.
- `src/app-shell.route-focus.ts` - app-shell route announcement, document title, and hash-target focus view models.
- `src/app-shell.status-panel.ts` - app-shell bootstrap, degraded workspace, and recovery status view models.
- `src/app-shell.trust-strip.ts` - app-shell build, mode, source, and provider posture view models.
- `src/app-shell.workflow-continuity-types.ts` - shell workflow-continuity view model contract.
- `src/components/ui/` - shared Meridian Design System primitives, including buttons, inputs, selects, badges, tooltips, dialogs/modals, sheets, checkbox/toggle, breadcrumb, form rows/grids, tabs, status banners, context menus, multi-select, toast, and panel surfaces.
- `src/design-system/assets.ts` - dashboard bridge for the checked-in `Meridian Design System/` package, centralizing brand and workspace icon imports before app-shell or navigation components consume them.
- `src/assets/` - browser-bundled brand and icon copies from the `Meridian Design System/assets/` source package, including the app icon and PNG tile.
- `src/types.ts` - compatibility barrel for browser DTO mirrors. Add new domain-specific DTO mirrors under `src/types/` and re-export them from this file instead of growing the barrel directly.
- `src/lib/dev-fixtures.ts` - compatibility facade for no-host fixtures. Add new screen or domain fixture payloads under `src/lib/dev-fixtures/` and register them through the resolver map instead of adding another large block to the facade.
- `package.json` - dashboard build, test, and tooling commands.
- Test files - browser workflow and component coverage.

Legacy `/overview/*` links remain compatibility redirects in the app shell. The retired overview
screen, Today panel, and unrouted Settings admin operations console are recorded as comment-only
tombstones under `archive/code/src/Meridian.Ui/dashboard/src/screens/`.


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
Security Master lots. The Daily Control Tower finance queue uses the same table/inspector contract,
including row selection, focus handoff, and Escape return behavior.

The Data backfill workstream reads `/api/backfill/executions` as the durable remediation evidence
source. Its remediation SLA queue keeps server-owned tier, deadline, status, provider, workflow,
owner-assignment, outcome, and compatibility-derived provenance visible, with operator sorting on
SLA tier and deadline. Live provider-attempt progress remains a separate bounded projection so a
dropped transient notification cannot erase the retained execution/SLA record.

## Important workflows

The browser workstation exposes `/accounting/entity-setup` for the shared fund-structure setup wizard. The feature posts drafts to `/api/fund-structure/setup-drafts/validate` for validation and preview, then `/api/fund-structure/setup-drafts/create` for review-and-create instead of reimplementing setup orchestration in React.


This is the active operator UI lane; keep shared contract compatibility with retained WPF consumers. Security
Master Governance detail uses the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` projections for cash-flow schedules, factor provenance, and open-lot exposure
review. Open-lot rows render explicit Long/Short direction in the table, detail panel, and accessible
row description rather than inferring direction from the positive lot quantity.
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
Settings also exposes the Admin Ops task console over shared maintenance, storage, retention,
cleanup, schedule, and data-package endpoints. React renders returned posture, command results, and
typed package/schedule evidence without inventing local maintenance policy or retention decisions.

Evidence Workbench consumes the shared Evidence Vault request-list and document queue endpoints. The
same intake queue is reachable from the Accounting and Data workspace subnavigation, while the
legacy Reporting evidence route remains a compatible evidence-packet entrypoint. The
browser renders retained documents with classification, source hash, typed channel/source, actor,
tenant/scope, extraction status, reviewer state, linked operational objects, open support-request
count, support-only authority posture, and manifest links, while keeping intake and readiness policy
in shared contracts/endpoints.
When the statement-run service returns nothing, the reconciliation desk derives run rows from the
reconciliation queue. The queue carries break and case counts but no match totals, so derived rows
report match counts as not reported (`—`, with the reason on the row) rather than printing the
placeholder zeros, and the Positions, Cash, and Transactions detail tabs drop their badge and say
the totals were not reported instead of crediting the reconciliation service for them. A
service-reported run that genuinely matched nothing still reads `0`: zero and unknown are different
facts in a reconciliation.
Statement import previews an uploaded file against the fund account and reporting period from the
Commit import form, so the panel cannot parse anything until those fields are complete. That
dependency is stated on the panel: selecting a file with the form still blank names the outstanding
fields instead of silently doing nothing, and the commit control reports the same fields rather than
asking for a preview it is holding back. Picking a connector fills Source institution from that
connector's display name when the field is still blank; fund account and period are never guessed.
Statement import accepts either a bounded file upload or a remote fetch through a fetch-capable
provider connection. The scheduled-fetch tab previews remote activity with the same canonical
column-confidence and per-kind breakdown as file import, then lets operators create, edit, pause,
delete, refresh, or run persisted schedules with an explicit broker/custodian classification without
collecting credentials in the browser. Run-now
results render the shared Evidence Vault and reconciliation routes. File-import commit results also
render Evidence Vault identity, the Evidence Workbench route, reconciliation route, and structured
account-margin, activity-completeness, option-lifecycle, tax-lot, and borrow evidence retained beside
the canonical CSV. `/accounting/margin-control` projects that retained evidence across accounts and
prime brokers, compares provider-authoritative values with a labelled Meridian shadow estimate, and
permits durable end-of-day certification only through the shared permission-checked endpoint.
reconciliation case links directly from the commit response, including status, priority, reason,
and suggested next action. The browser blocks file commit while preview errors remain, so operators
can move from imported custodian/broker source to retained proof and exact casework without
browser-local routing rules or avoidable server rejections.
The request-list queue renders typed close, audit, tax, report-package, and operational-event family
badges beside each frozen support list so operators can distinguish close binder blockers from audit
or report-support package gaps without parsing manifest JSON.
Selecting a retained document opens a read-only review panel with reviewer notes, source metadata,
immutable source-record receipt, extracted fields for human confirmation, human-confirmed field
counts, authority boundary, object links, audit events, support-request context, and manifest access
without introducing browser-owned approval or accounting mutation.
Operators can also retain an uploaded document, local-file path, or imported-file reference from
the selected evidence subject, classify it, record actor and tenant/scope metadata, set extraction
and reviewer state, and attach one linked operational object before the shared vault intake
endpoint copies the payload and computes the source hash. The intake form exposes the v1
document-classification vocabulary, `Pending` extraction posture, and linked fund, account, period,
close, reconciliation, journal, report, instrument, and portfolio object targets; it leaves `Accepted`
review out of first-pass intake because accepted evidence must pass through the shared review
endpoint with human-confirmed fields.
When an operator accepts a retained document from the browser, the shared review endpoint receives
human-confirmed extracted fields plus the immutable source-hash field so accepted evidence is not
represented by a status-only transition; the retained document authority still cannot approve,
post, certify, or release.
The TypeScript contract also mirrors `EvidenceDocumentIntakeChannelDto` and
`EvidenceDocumentIntakeSourceDto` so uploaded, email, SFTP, API, portal-download, local-file, and
imported-file reference intake use the same shared channel/source vocabulary. Browser intake keeps
email, SFTP, API, and portal-download as upload-backed adapter seams in v1: operators retain the
document bytes and typed source URI now, while later connector implementations can fetch from those
channels without changing the retained source-record shape.
The TypeScript vault identity mirror includes the public `manifestSnapshot` so browser close,
report, audit, and tax package views can inspect frozen package documents, support requests, object
links, typed request-list family, typed frozen package family, and content hash without parsing
retained manifest JSON.
Manifest export results surface that typed frozen package family directly, so operators can tell
whether the retained package is a close binder, audit packet, report support package, tax support
package, or operational-event support package before opening the manifest file.
The browser DTO mirror also preserves `documentSnapshots` on Operations Continuity close-package
requests and publications so close binder documents can be frozen by Financial Operations instead
of recomputed in React.
The browser route catalog, endpoint helpers, DTO mirrors, and API client also expose the shared
v0.19 provider-integration runtime for template catalog/detail, OpenAPI import, setup-save,
activation-readiness, dry-runs, activation, monitor, sync-run history, sync planning, due-sync
execution, schema-drift checks, staging review, identity resolution, promotion readiness,
reconciliation handoff, quarantine review, quarantine resolution, and quarantine replay. Browser
screens should consume those shared endpoints when binding setup and review panels instead of
deriving provider runtime state locally.
The Settings Provider Connection Center now loads the shared provider-integration monitor,
sync-run history, sync plan, staging review, identity-resolution preview, promotion readiness,
reconciliation-handoff history, and quarantine review on demand per routed connection. Retained
staging, identity, quarantine, promotion, and handoff evidence stays service-owned while the browser
surfaces the operator review posture beside credential and routing status. The runtime panel can
seed an OpenAPI draft manifest, run due read-only sync capabilities, create reconciliation handoff
evidence for unhanded promotion-ready staging rows, record retained quarantine-record decisions for
review-only, replay-after-mapping, ignore-provider-record, and mark-as-cash-position actions, trigger
the shared quarantine replay endpoint for the currently retained quarantine batch, and show the
backend-computed pending, decisioned, replay-requested, ignored, and cash-position posture counts
after evidence reload.

No-host browser previews must keep fixture data visibly labeled as demo data. The masthead's compact,
non-dismissable provenance strip routes operators through the typed demo evidence path: watchlist,
live quote evidence, trading readiness, and provider setup, while keeping retry-to-live behavior
available. Do not add a second full-width seeded-data warning below the masthead.
The app-shell data-provenance badge combines the server-owned `/api/demo/mode` response with actual
fixture usage. Explicit demo or fixture data always wins over a nominal live posture, and a missing
or malformed mode response fails closed to simulated/unverified rather than being presented as real
provider data.

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
Broker execution reconciliation readiness from the shared Trading payload is rendered as a
broker-order checkpoint in the readiness console and a Trading summary row when present; critical
broker/OMS parity work items route back to the Trading readiness surface instead of being treated as
anonymous inbox rows. The browser also renders shared live-operation readiness as its own Trading
summary row and Operator Readiness Console checkpoint, using `LiveOperationBlockers` instead of
promoting paper-ready posture into live readiness. When the shared payload carries
`LiveOperationRequirements`, Trading summary rows also show each W7 requirement from the
service-owned matrix so browser copy names the exact trusted-data, reconciliation, governance,
rollback, retention, or broker-parity evidence gap. Eligible live promotion evaluations also
project the full W7 checklist instead of the paper baseline alone, including broker execution
reconciliation evidence before the approval request can carry a live-ready checklist. The browser
promotion form keeps retained evidence references as an explicit operator-entered field and
validates live approvals against the checklist tokens instead of fabricating evidence from the
checklist alone. Live evidence references must include retained evidence after each `TOKEN:` prefix,
and the live-override reference must name the active override id before the form can submit.
The Trading order ticket also carries the active normalized `fundAccountId` into order-submit
mutations so the execution-layer live-readiness gate evaluates broker sync and broker/OMS
reconciliation against the same account scope shown in the browser readiness payload.
Close-position confirmations use the same normalized `fundAccountId` for keyed position action
mutations, while non-GUID broker labels are omitted so account scope is not inferred from display
text.
Accounting reconciliation break detail preserves shared queue metadata such as exception route,
tolerance profile, priority, SLA badge label/tone, age band, root cause, resolution code, last
comment excerpt, comment/evidence counts, related-case counts, required sign-off role/status,
source origin/fingerprint, and decision note so browser recovery posture matches the WPF desktop
Fund Ledger detail panel without reimplementing casework rules.
Accounting reconciliation casework actions now consume the server-owned verified outcome on every
assign, resolve, waive,
supersede, comment, and lifecycle transition. The queue exposes item-level value, quantity, and
cost-basis measures, exact fund/book/period/as-of scope, continuity blockers, immutable evidence and
approval lineage, and conflict-safe replay receipts. Browser success UI is driven only by
`Succeeded` or `CompletedWithWarnings`; `Blocked` and `Failed` retain recovery guidance and do not
optimistically mutate local state. Material waiver or supersession remains unavailable when the
server cannot resolve independent approval evidence.

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
The panel also renders provider-by-provider import posture from the shared AccountingSystem provider
catalog, so QuickBooks, Xero, NetSuite, and fixture providers show read-only import capabilities,
credential state, retained mapping-profile coverage, and live-posting-disabled posture before an
operator prepares a guarded export package.
The same panel now reads external GL mapping profiles from the shared AccountingSystem endpoint,
shows certified account and dimension coverage beside the reconciliation, and posts guarded export
package requests with mapping, reconciliation, fund, period, and evidence context. It can also post
the retained export package id, reviewer notes, and evidence to the shared export certification
endpoint. The returned package is retained as a review artifact with generated mapped export lines,
certification, validation state, and a controlled manifest hash; certification evidence must
identify the retained export package id or exact export period before Financial Operations will
certify it. The browser also renders export
safeguards for balanced reconciliation, certified mapping coverage, critical package blockers,
manifest hash/line retention, and disabled live-posting posture while shared policy remains
authoritative. Live external GL posting remains disabled by shared policy and is not inferred in
React. The same panel queries retained guarded export package history for the selected provider,
fund, and ledger book, then renders certification state, evidence count, validation issue count,
period, created timestamp, and disabled-posting posture so operators can review prior export
artifacts without re-entering package ids.
The Accounting screen also carries a stable Investment Accounting Transaction Lab panel view model
so the browser renders the Books Before Broker preview entry point without crashing while endpoint
request wiring remains a follow-on workflow.
The Accounting workspace workflow launch strip is derived from the Accounting view model and shared
route catalog, covering setup, journal entries, ledger review, reconciliation, exception casework,
Security Master readiness, approvals, and retained evidence packaging without browser-local close
state.
The Accounting Configure workstream at `/accounting/configure` renders the shared Accounting Rules
Studio from `AccountingRulesStudioDto`, `PostingRuleDto`, `RuleDryRunResultDto`, and rule-test suite
DTOs: server-computed rule counts, promotion queues, activation readiness, effective dates,
priority, dimensional scope, event predicates, grouped `All`/`Any` predicates, formulas, allocations, generated posting metadata,
retained versions, promotion approvals, dry-run preview results, saved regression cases, and
regression test-case results are browser projections over the shared configuration service rather
than client-local rule logic. Operators can duplicate the selected posting rule into a
promotion-gated draft through the shared posting-rule upsert endpoint; the browser clears carried
approval state, retains browser evidence links, selects the returned draft rule, and lets service
validation/audit own the canonical workspace state. Rule mutation, promotion approval, and
regression-test actions carry the active `ledgerBookId` so book-scoped configuration changes do not
fall back to fund-level rules while the operator is working inside a selected ledger book. Operators
also see setup-readiness rows from shared validation; a missing registered ledger book is surfaced
as the critical `configuration.ledger-book-missing` activation blocker instead of being hidden in
the generic validation list. Retained migration run artifacts render their canonical fund, book,
entity, cost-center, counterparty, and external-GL dimensions so dimensional backfill proof is
visible in the same browser control plane that consumes the shared readiness blocker codes.
Tenant-administration setup in this browser workstream retains structured approval-queue and
dimension-mapping configuration in the shared accounting tenant-administration profile, including
provider id, Meridian/provider dimension rows, and evidence requirements instead of a checkbox-only
readiness claim.
Operators
can apply the selected dry-run source
event as a required predicate through the same shared upsert route, replacing stale event-kind
predicates, clearing stale promotion approval, and requiring fresh promotion before activation.
Operators can apply the selected dry-run amount as a required amount-threshold predicate through the
same shared upsert route, which updates the retained rule definition, clears stale promotion
approval, and requires fresh promotion before activation. Operators can also apply the selected dry-run date as the retained rule effective start,
with stale promotion approval cleared before the updated effective window can activate. Operators
can capture dry-run generated posting lines back onto the selected rule through that shared upsert
path, turning template-derived previews into retained multi-line generated posting definitions that
require fresh promotion. Operators can also apply a dry-run generated posting amount back onto the
matching retained rule formula through the same upsert path, clearing stale promotion approval before
the recalibrated formula can activate. Operators can apply unambiguous generated posting dimensions
onto retained allocation targets when formula-linked generated postings produce deterministic target
scope, again clearing stale promotion approval before activation. Operators can apply unambiguous
fund, entity, instrument, counterparty, cost-center, tax-lot, and external GL dimensions from
generated posting metadata back onto the retained rule scope through the same upsert path, with stale
promotion approval cleared before the scoped rule can activate. Operators can raise the selected
rule priority through the same retained upsert path, clearing stale promotion approval before the
reprioritized rule can activate or be duplicated into a draft. Operators can also archive the selected posting rule through the same
upsert route so obsolete rules leave active matching without deleting retained history or bypassing
audit evidence. Operators can save the selected dry-run preview as a retained regression case through
the shared test-case endpoint, including browser evidence links, the selected rule version, and expected generated posting-line
assertions copied from the dry-run preview, and the refreshed workspace becomes
the source of truth for saved cases. When saved cases exist, the browser lets the shared service
execute the persisted workspace suite instead of rebuilding temporary cases locally. Promotion
approval controls use the shared promotion-approval route so stale rule versions, missing approver
notes, weak evidence, missing saved current-version regression coverage, and failing saved dry-run
assertions are rejected by the service before the retained version snapshot is updated;
the Accounting Rules Studio toolbar updates from the returned workspace rather than fabricating
approval state locally. The selected-rule detail also renders a promotion readiness checklist for
version history, approval evidence, saved regression coverage, latest suite result, generated
posting lines, dimensional scope coverage, and the activation gate. Activation controls surface the
shared readiness blockers for promotion-gated rules, including missing approved promotion evidence,
missing current-version saved regression coverage, and the latest failing rule-test suite. Direct
Rules Studio callbacks for duplicate draft, archive, promotion approval, and activation now return
the same disabled reasons before shared-service mutation when no active rule is selected, promotion
is already approved, or activation blockers remain.
When shared validation reports a missing selected ledger book and the workspace includes a
server-derived setup candidate, the browser Accounting Configure surface can create the ledger book
through the shared `/api/ledger/books` endpoint and then refresh the workspace; React does not infer
fund-structure node ids, accounting basis, or policy ids locally.
The same surface now renders a ledger-book administration catalog from the shared configuration
workspace, showing the selected book, available books, fund/entity scope, basis, currency, policy,
description, and update timestamp before production-readiness review so operators can inspect
which book is being certified without relying on hidden fund-level context. When operators save tenant-administration controls with book
administration enabled, the browser adds a retained tenant-admin evidence reference for the selected
`ledgerBookId` unless the operator already supplied one, keeping the setup editor aligned with the
shared production-readiness gate for multi-ledger administration. The browser tenant-admin setup
editor persists chart administration, Rules Studio test/promotion, close setup, provider mapping,
tenant/company/report-group setup, audit review, bulk import/export safeguards, performance
validation, recovery runbooks, ledger-book administration, posting-rule authoring, approval queues,
dimension mapping, and sandbox validation as independent shared profile controls, and its
implementation-sandbox proof action retains selected-ledger-book implementation-sandbox,
sandbox-validation, fixture-validation, and implementation-fixture evidence while carrying the
current approval-queue and dimension-mapping setup payloads before refreshing readiness; the sandbox
action is disabled until any configured approval-queue or dimension-mapping setup is complete.
The same Configure surface also calls the shared `/api/accounting-system/production-readiness`
assessment for the active fund and ledger-book scope, then renders the service-owned control-plane
posture for ledger books, Rules Studio, posting rules, JE lifecycle, dimensions, external GL,
close/reporting, migration rollout, and tenant administration. The panel displays returned blockers, suggested
actions, evidence counts, retained migration run artifact posture, explicit ledger-book-native
workflow control counts and retained ledger-book-scoped workflow evidence for posting rules,
JE lifecycle, close/reporting, external GL, reconciliation, direct-lending projections, and
strategy ledger reads, dimensional ledger/query/report/export control counts
with retained ledger-book-scoped evidence, certified external-GL mapping coverage, and the disabled live-posting stance without deriving
production-readiness policy in React. Tenant administration uses the shared
`AccountingTenantAdministrationReadinessDto` to render
tenant, company, admin-role, scoped-access, reporting-group, aggregate operator-surface, browser
accounting admin-studio, WPF accounting admin-studio, chart administration, rule-test/promotion
setup, close setup, provider/external-GL mapping setup, tenant/company/report-group setup, audit
review tooling, bulk import/export safeguards, performance validation, disaster-recovery runbooks,
ledger-book administration, posting-rule authoring, approval queues, dimension mapping, implementation sandbox validation,
and retained-evidence controls instead of treating setup readiness as a generic component row, and the
dashboard route catalog exposes the retained tenant-administration profile endpoint. The Configure
surface can load, edit, and save that retained profile with browser accounting admin-studio,
enterprise configuration studio lane coverage, approval queue setup fields for queue id, workflow
kind, required role/count, segregation policy, and retained evidence requirement, and setup evidence
before refreshing production-readiness posture from the shared Accounting System service. The browser
save model blocks configured approval queue or dimension mapping studios until the typed setup
payloads are complete, and its save callback now rejects missing retained tenant-admin evidence
before any shared-service submission, matching the shared store's fail-closed profile invariant. The shared browser
contract also carries structured tenant-admin dimension mapping rows with mapping/provider ids,
Meridian dimensions, provider dimensions, and evidence requirements for parity with retained
Accounting Configure profile data. The Configure surface also
loads, edits, and saves the retained production-certification profile for ledger-book-native
workflow controls, including reconciliation, direct-lending, and strategy-ledger-read
certification, plus dimensional ledger-line, trial-balance, reporting, provenance, and export
controls, preserving tenant/company/fund/book
scope plus retained evidence through the shared Accounting System store rather than request-only
flags. After the operator supplies retained evidence, checked-control saves augment that evidence with
scoped per-control markers for posting candidates, journal lifecycle, close/reporting, external GL,
reconciliation, direct lending, strategy ledger reads, and dimensional query/export coverage so the
shared store can enforce category-specific certification evidence. Production-certification saves also
retain typed workflow, dimensional, and tenant-administration certification artifacts so enterprise
configuration studio controls can feed readiness through executable lane rows instead of broad flags.
The same Configure panel can author retained external-GL provider mapping profiles, including
provider/profile identifiers, account mappings, editable Meridian and provider dimension maps for
fund/book plus customer/vendor/project-style scope, human-origin mapping certification evidence,
and readiness refresh through the shared Accounting System mapping profile endpoint; disabled saves
fail locally when required identifiers, mappings, or retained evidence are missing.
The Configure panel also exposes a chart account setup editor that saves chart nodes through the
shared accounting configuration chart endpoint with the active fund/book scope, parent path,
financial account id, and retained setup evidence, so browser operators can add operational chart
accounts without using raw JSON or direct endpoint tooling; required chart identifiers and account
path/name/type are guarded before endpoint submission.
It also lists retained migration run artifacts from
`/api/accounting-system/migration-run-artifacts`, including run kind, certification status,
fund/book scope, migrated-record and issue counts, and evidence reference counts, so operators can
inspect migration proof retained in the shared Accounting System store rather than re-entering
request-only evidence. The dashboard API layer also exposes the retained guarded external-GL export
package list query so Accounting surfaces can review package history by provider, fund, book,
certification state, tenant, and company before loading a specific manifest. The same
production-readiness panel renders the generated migration rollout
plan for ledger-book scope, historical journal backfill, dimensional backfill, configuration
promotion, and close/reporting evidence migration, including latest retained run, blocking issue
codes, and required actions for each lane.
It also renders the shared production-gap checklist so configurable multi-ledger accounting,
enterprise configuration studio coverage, guarded external GL integration, dimensional ledger and
reporting coverage, and production-control hardening remain service-owned review items in the
browser Accounting Configure surface, including the issue messages supplied by the shared
production-readiness service rather than only stable blocker codes.
After a dry run selects a rule, operators can build a governed journal draft candidate through the
shared posting-rule candidate endpoint. The browser carries the selected event, amount, dimensions,
policy, counterparty, source evidence, tenant/company context, and browser correlation metadata to
the service, then renders the returned generated lines, retained evidence count, candidate issues,
and pending approval-gated posting command without appending ledger entries or bypassing
journal-entry lifecycle controls.
The Accounting Manual Journal Entry workbench also exposes the shared lifecycle-action endpoint for
evidence attachment, approve, reject, post, reverse, rebook, and lock-after-close. Browser evidence
attachment posts the retained draft version, typed attachment metadata, actor, correlation id, and
evidence links to the shared evidence endpoint, then refreshes from the returned draft instead of
fabricating retained evidence locally. Browser lifecycle commands pass the retained draft version
and evidence links, render the returned transition audit rows with transition id, correlation id,
and retained evidence routes, and show generated reversal/rebook drafts as separate entries instead
of mutating posted entries. The workbench also renders a lifecycle checklist for draft version,
validation, evidence, submission, approval, posting, reversal, rebook, close-lock, and transition
audit posture so operators can see which server-owned gate is ready or blocked before acting.
The Accounting close/report package cockpit reads the shared close-management period plan and
accounting report-package history endpoints, showing checklist dependencies, sign-offs,
materiality, close-calendar milestones, period-lock posture, late adjustments, package
certification, investor statement counts, realized gain/loss, NAV, statement-line provenance,
export artifact certification state, restatement state, validation issues, and retained evidence
counts. It renders service-owned close/report readiness rows from the package bundle when present,
and it scopes retained package history by the loaded close plan's fund, period, and ledger book so
book-specific close packages do not blend into fund-level history.
The cockpit now also renders the close plan's service-owned operating coverage rows for close
setup, dependency graph, sign-off matrix, late adjustments, blocker review, and period lock, showing
the backend readiness state, evidence count, blocker count, required action, and blocker issue
labels before the older task/dependency/matrix detail sections.
The cockpit also renders an end-to-end close workflow control sequence for setup retention,
checklist sign-off, late adjustments, blocker review, report package build, certification, export
manifest inspection, and period lock, with each step's action and disabled state derived from the
same shared close plan and selected report package state. Pending late-adjustment review and active
blocker review steps now surface their own review gates in the sequence, while row-level controls
retain the specific approval/rejection or issue-review commands required by the shared endpoints.
Its certification safeguard rows cover close checklist sign-off, period-lock posture,
late-adjustment review, report evidence, export-artifact certification, restatement workflow state,
blocker counts, and retained package evidence; older payloads still fall back to the browser's
display-only safeguard aggregation. Its package-build
command posts workflow, fund, period, package-seed, and evidence context
to the shared accounting report package endpoint, and its certify command posts the selected
retained package id, reviewer notes, and evidence links to the shared certification endpoint. Its
task sign-off administration lets operators select the ready checklist task, retained signer role,
Approved/Rejected decision, and reviewer notes before posting correlation id and evidence links to
the shared close-management endpoint. Its close setup editor lets operators adjust
materiality thresholds, currency, review role, late-adjustment approval requirement, and the primary
checklist task's owner, due date, required approval role/count, evidence requirement, multi-role
sign-off matrix rows, and dependencies
with retained dependency reasons before posting ledger-book evidence and correlation context to the shared close-plan configuration
endpoint, including the loaded configuration timestamp so stale browser setup edits fail closed instead of
overwriting newer retained setup. Dependency reason text can also use keyed predecessor entries such as
`task-pricing: Pricing package must clear` so each retained edge can carry its own audit rationale. The setup editor exposes retained checklist tasks, predecessor candidates, and sign-off
role candidates from required roles, retained sign-offs, task owners, and the materiality reviewer;
the matrix editor accepts retained `role | count | evidence` rows and preserves unedited task
matrices in the shared close-plan configuration request;
it blocks invalid materiality thresholds, malformed currencies, blank materiality review roles,
missing task ids, non-positive approval counts, blank approval roles, and blank evidence before
calling the shared endpoint. Its period-lock action posts the
current workflow version, selected report package, checklist-control approvals, close-package
manifest context, ledger-book-scoped evidence, and human-operator origin to the shared
close-management lock endpoint, rendering returned service blockers without attempting a local
close transition. The late-adjustment request form
posts the journal entry id, amount, currency, reason, controller actor, correlation id, and retained
late-adjustment/materiality evidence links to the shared close-management endpoint, then refreshes
the cockpit from the returned close plan. Late-adjustment approve/reject buttons post the retained
request id, decision, reviewer notes, correlation id, and evidence links to the shared
late-adjustment review endpoint; close transitions, dependency validation, materiality review
validation, certification state mutation, rendered statement artifacts, and restatement execution
remain service-owned.
Late-adjustment rows render retained approval or rejection actor, timestamp, evidence counts, and
materiality-threshold posture from the shared close-management DTO instead of deriving decision state
or approval blockers in the browser.
Close task sign-off rows likewise display retained approval counts, actor, notes, and evidence from
the shared close plan; React does not decide checklist completion locally, and dependency ordering
is enforced by the shared task sign-off endpoint.
The setup editor exposes a retained close-task catalog before the task authoring fields, so
operators select the task whose owner, due date, approval count/role, evidence requirement, and
dependency ids are being edited; unknown task ids are blocked in the browser before the shared
configuration endpoint is called. Dependency authoring also projects known predecessor candidates
from the loaded close plan as selectable rows, keeping dependency graph edits tied to retained
task ids while still submitting the shared close-plan configuration contract, and keyed entries in either the dependency-id text or reason text are preserved as per-edge reasons.
The close cockpit also projects dedicated dependency-graph, sign-off-matrix, and evidence/blocker
review rows from the loaded close plan and selected report package so operators can audit
predecessor coverage, required approval roles, retained evidence, and service-owned validation
blockers without re-deriving close state in React. Active blocker rows can now post a retained
close evidence-review command with workflow, period, issue, target, ledger-book, notes, and
close-review evidence; returned review rows are displayed beside the blocker while the underlying
validation issue remains service-owned.
Close-calendar rows display milestone due dates, owners, dependency counts, sign-off counts,
evidence counts, blockers, and period-lock state returned by the shared close plan instead of
rebuilding calendar posture from checklist rows in React.
The accounting production-certification editor also carries a separate close-plan setup control so
materiality policy, checklist/dependency, and sign-off configuration evidence is retained by
ledger book instead of being implied by generic close/reporting certification.
Accounting report package history rows also display the shared export artifact certification count
from the package bundle, while artifact generation, content hashes, and certification state remain
owned by Financial Operations. Operators can inspect the selected package's retained controlled
export manifest from the close cockpit; the browser displays artifact id, format, file name,
generation time, content hash, route, evidence count, certification state, and disabled external
posting posture while Financial Operations remains responsible for artifact generation and
certification mutation.
An empty holdings table on the Portfolio desk offers the step that actually fills it rather than
only stating that it is empty: a loaded workspace with no holdings routes to statement import, and
an empty paper session routes to the trading desk. The offered step follows the same branch as the
sentence printed beside it, so the two can never disagree, and a workspace that failed to load gets
no button — that is a load failure, not an empty desk, and routing elsewhere would hide it. The
multi-asset coverage presenters live in `portfolio-screen.multi-asset-coverage.ts`; the row and
group types stay owned by the view model and are imported type-only.
`FinancialRecordExplorerShell` is the browser presentation for the shared Financial Record Explorer
DTO. Accounting loads the `ledger` and Accounting-hosted `security-instrument` explorers from
`/api/workstation/financial-record-explorers/{explorerId}`, Portfolio loads `portfolio`, Reporting
renders the shared `report-line-provenance` explorer from the reporting payload, and Data links to
the existing Security Master lane instead of restoring the old static Data workbench. Reporting
renders the server-provided report-line chain for instrument, position or transaction,
reconciliation, journal, report line, evidence, and audit links; React must not rebuild that
lineage locally.
Saved views post back through the shared saved-view endpoint only when an operator supplies a
stable view name for the current filter/search state; browser code does not generate timestamp-only
saved-view labels. Shared explorer links also round-trip explicit `frexFilter` values alongside the
selected saved view, search text, and proof record, so a discussion URL can restore the exact
evidence state even when the filter is not encoded in a durable saved view. Blocked or empty DTOs
keep proof actions disabled with the server-provided reason.
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
private-capital close approvals, reviewed-automation outputs, workflow evidence-package readiness,
accounting-record evidence categories, close-cockpit evidence-package readiness, and non-ready Receive
Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close Support
command stages stay source-backed while React only groups the active work items for review. If the
close-calendar or private-capital close cockpit projection cannot load, the queue adds a blocked
unavailable item so workflow control fails closed.
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
For Trading, the hook and screen refresh paths pass the active account scope to the shared trading
workspace, trading-readiness, and operator-inbox endpoints only when it is a GUID, preserving
account-scoped brokerage sync and broker-execution reconciliation evidence without sending display
account labels into GUID-bound API parameters.
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
On the daily Reporting landing, React renders `starterKits` and `starterKitState` from the shared
Reporting payload as the "Set up your reporting desk" chooser. Selecting a kit posts to the shared
starter-kit provisioning endpoint, then shows the server-returned enabled template ids, layout id,
default period, and draft schedule ids; the browser does not locally decide which templates or
schedules belong to an archetype.
Report-pack delivery history rows link delivered packages to the shared `report-pack-delivery`
Evidence Workbench subject using the backend `reportId:attemptId` identity, so React does not build
delivery evidence packets or audit graph state locally. Publication review and delivery package
panels render retained line provenance with the server-provided Financial Record Explorer hrefs
rather than constructing ledger, portfolio, or Security & Instrument routes in React. The delivery
history also renders backend-owned access, channel, and download summaries for retained legacy
email-link, secure-portal, evidence-vault, and internal-route package labels instead of deriving
recipient-facing copy or transport capability from a label or URL shape. When the shared package
includes an access-expiry timestamp, React displays it
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
canonical artifact-vault downloads keep styled artifact generation in shared services. PDF output
applies the selected theme colors, firm, logo, footer, and disclaimer server-side, and XLSX packages
retain a Branding worksheet, so the browser exposes retained branding evidence without recreating
presentation rules in React. Email-link and secure-portal recipients use server-issued, scoped
access-grant exchange links with a single opaque fragment token. The browser rejects cross-origin,
query-credential, and legacy raw-package routes and does not turn retained manifest paths into
recipient downloads.
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
custom posture, color swatches, logo URI, footer text, and disclaimer copy. Operators can stage a
custom branding override for schedule or run configuration, but the browser does not call the
retired pack preview or generation endpoints. Preview, certified artifact production, lifecycle
creation, and release now proceed through the canonical governed-run parameter and run-detail
workflows, where readiness and retained evidence remain server-owned.
It also renders shared `scheduleDeliveryPlans` rows for scheduled report packs, including recipient,
channel, delivery mode, PDF/XLS/CSV formats, readiness, retained package links, access-expiry
timestamps, retained download summaries, latest artifact counts, checksum integrity summaries,
schedule/package branding theme, and version stamps from the backend payload. Each ready
delivery-plan row can also run its owning schedule through the shared schedule-run endpoint and
reports the returned run id, delivery count, recipient-specific delivery count, and target
delivery mode in the common schedule status lane.
Those rows render typed drilldown links and retained next-action references from the shared payload.
Browser-safe evidence routes remain inspectable, while legacy pack actions are presented only as
historical context and route operators to the canonical governed-run detail. React does not post
caller-supplied signers, hashes, manifest ids, retention paths, approvals, publication, archive,
restatement, or delivery commands to retired pack lifecycle endpoints.
The run cards also surface exact audit metadata from the shared run projection, including run id,
template id, as-of date, trigger, status, attempt count, section count, linked-lineage count, and
retained artifact names, so version-control review does not depend on parsing a prose lineage
summary.
The report-run parameter workspace mirrors the full optional `LedgerDimensionSet` through a JSON
editor. React validates supported scalar fields, UUID-shaped book/instrument/position identifiers,
fund and ledger-book consistency, and the `externalGlDimensions` string map before calling the
authoritative readiness endpoint. The run command then posts the server-returned normalized
parameters, including non-empty dimensions, so readiness and generation operate on the same exact
projection. A code-only ledger selection may omit the dimension `bookId` for server resolution.
Governed run responses consumed by React require canonical `normalizedParameters` and
version-bound `actionAvailability` entries with `expectedVersion`. Any retained legacy parameter
or action aliases are adapted only at the reporting-governance API boundary; screen components do
not infer permissions or versions from those aliases. The same run detail requires the immutable
access snapshot as `allowOwnerAccess` plus typed `User`, `Group`, or `Company` principals. React
renders those values as policy evidence only; flattened principal ids are rejected at the API
boundary rather than assigned a client-inferred principal kind or used to infer authorization.
The Reporting workspace also renders operator-managed schedule rows from the shared schedule
payload and wires schedule save/upsert, run-now, pause, and resume controls through the shared
schedule endpoints. Due schedules are leased and executed only by the hosted reporting worker; the
browser has no public batch `run-due` control or API helper. Schedule drafts default from the current schedule, approved template, and
distribution payload before posting back to the server. Retained delivery attempts render from the
shared delivery-history payload, so browser Reporting shows actual
recipient delivery state and retry history instead of static status chips. The delivery panel is
read-only: durable dispatch state and provider failure receipts are produced by the server-owned
distribution worker, not a client-recorded failure mutation. Delivered attempts retain package
mode, requested formats, manifest and publication evidence, canonical opaque-fragment access-grant
links, notification proof, artifact integrity, and governed run ids. Runs with retained ids link to
the governed run detail for release and distribution controls. Query-token package builders and
the retired legacy package/delivery API helpers are not exposed by the browser. When a schedule targets a custom report-writer
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
delivery mode, PDF/XLSX/CSV formats, and an explicit recipient principal id plus User/Group/Company
kind, then staging multiple delivery targets before save so a single governed schedule can retain
separate typed recipient targets. The browser blocks staging or saving a delivery target until both
recipient fields are present; the server validates the target against immutable access and the
recipient directory, then binds the ordered declarations under `deliveryTargetsSnapshotHash`.
Email Link uses the configured
`http-relay`; retained Evidence Vault and Internal Route mode labels use the local `secure-portal`
path and do not imply separate transport adapters. The browser renders the caller-specific server
transport catalog as the availability authority. It posts the shared
`ReportingScheduleUpsertRequestDto` instead of maintaining local schedule rules, including a
server-owned `datasetSourceId` and the current custom branding override. It never posts governed
dataset rows; the server resolves certified report-writer input. Recurring generated-run packages
therefore retain the selected firm styling without accepting client data as accounting evidence.
Schedule cards also show configured delivery targets
and the run-now status reports returned delivery counts or warnings from
`ReportingScheduleRunResultDto`, keeping generated report runs distinct from packaged
client/internal distributions. The backend applies the same governed template access policy to
schedule saves and manual schedule runs as it does to ad-hoc report runs, so browser controls cannot
schedule or execute user-locked custom templates outside the authorized owner, group, or company.
Schedule records also mirror `releaseDeliveryHandoffs` and `accessPolicySnapshotHash`. The browser
uses those server-owned fields to show whether post-generation delivery is blocked, awaiting
governance release, or enqueued, plus a compact token-free handoff history of durable identifiers,
formats, states, and timestamps. Package creation and bearer-grant issuance remain server-owned and
occur only after release; React does not enqueue a handoff or render retained destination, subject,
or body fields from this schedule history.
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
valuation projections instead of carrying a separate graph fixture. The route is **deliberately
withheld from discovery**: it is listed in `UNWIRED_WORKSTATION_ROUTES`, so neither the workspace
navigation nor the command palette offers it while the screen still renders a permanent
not-connected state. It stays mounted, so existing deep links and bookmarks resolve rather than
404. Restore it to both seams — which still read their labels from the route-catalog/view-model
seams, so discovery labels remain centralized — when the family-office endpoints are wired.
Strategy workspace navigation uses canonical Strategy labels for subroutes, including the retained
`/strategy/lab` route, so browser discovery does not expose `Research` as a visible root or
lane name while compatibility routes continue to resolve.
Strategy run-library live-region announcements and command failure messages also use canonical
Strategy wording while retained `Research*` DTO and component names remain compatibility seams.
Strategy Builder promotion-review warnings use risk/control wording in the browser view model,
matching the shared strategy-service validation copy while retained cell kinds remain compatibility
inputs.
The host-composed W6 browser path is the Covered Call form in
`covered-call-screen.view-model.ts`. Before calling the Covered Call API it requires operator
acceptance text as a future review requirement and at least one bounded, strict
`evidence://evidence-vault/{vaultId}` reference. The server resolves the retained manifest inside
the authenticated tenant/company scope before queueing and records the pre-execution entry through
the shared strategy-run repository. Covered Call results deep-link to that exact strategy run and
Vault artifact. The browser treats `PersistenceDegraded` as a terminal run phase, stops status
polling, and tells the operator that no Completed, Failed, or Cancelled lifecycle outcome is
authoritative when a durable lifecycle append fails.

The Strategy screen sends Paper promotion through the governed promotion endpoint. Its four
read-only acceptance checks are ready only when the server projects a durable operator/audit
decision, keyed evidence matching the source run, and the exact same-scope Paper child lineage;
metric eligibility, acknowledgement, or a caller-created paper session cannot satisfy them. The
corresponding UI contract proof lives in `covered-call-screen.view-model.test.ts`,
`covered-call-screen.test.tsx`, `strategy-screen.view-model.test.ts`, and
`strategy-screen.test.tsx`;
Strategy Designer and the uncomposed Backtesting Studio orchestrator are not evidence for this
bounded browser path.
The browser `DataScreen` owns the canonical Data workspace module under `src/screens/data-screen*`.
Retained `DataOperations*` DTO, endpoint, and fixture names are compatibility seams only. Data
workspace navigation and command-palette discovery surface `/data/providers` as the canonical
provider catalog and onboarding lane. `/data/operations` is the Ingestion Operations Center for
durable job state, checkpoints, retries, failures, transitions, and Evidence Vault receipts;
`/data/backfills` is a compatibility redirect. `/data/assurance` combines storage health, quality,
canonicalization parity, capacity, and guarded maintenance. Maintenance stays shared-service owned:
React can request a short-lived preview and submit rationale plus exact typed confirmation, but it
cannot choose arbitrary paths or bypass candidate fingerprint revalidation.
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
Trial-balance rows also render and search retained dimensional scope when the shared payload carries
`LedgerDimensionSetDto` data, including fund, entity, sleeve, strategy, investor, capital account,
instrument, tax lot, cost center, counterparty, and external GL dimensions; dimensionless legacy
rows remain explicit rather than being inferred from account names.
The browser type mirror for retained ledger journal rows carries the same dimensional fields, and
the Accounting view-model exposes a reusable journal-evidence dimension projection for rows returned
by `/api/workstation/runs/{runId}/ledger/journal` so journal review surfaces do not discard scoped
fund/entity/sleeve/cost-center or external-GL evidence.
The browser API helpers for run ledger trial-balance and retained journal reads also accept the
canonical ledger dimension filter set plus external GL dimension keys, so Accounting workstreams can
request server-scoped fund, entity, sleeve, strategy, investor, capital-account, instrument, tax-lot,
cost-center, counterparty, and external-GL results instead of relying on client-only filtering.
The same `/accounting/ledger` route is the first browser implementation of the shared Financial
Record Explorer pattern from the design document. It wraps the existing shared trial-balance,
ledger-line, reconciliation, evidence-packet, audit-packet, and report-usage read models with
explorer scope, saved-view labels, filter chips, summary signals, and proof drill-through actions
without creating a separate browser ledger state or adding new root navigation.
That shared explorer shell also wraps `/portfolio` and `/accounting/security-master`: Portfolio
anchors open holdings, selected run evidence, brokerage posture, and coverage proof to the existing
Portfolio view model, while Security Master anchors instrument search, identity evidence, conflicts,
schedules, lots, and trading controls to the Accounting-owned Security Master view model.

The Accounting Security Master workstream also renders the shared Instrument Passport provider-confidence evidence from /api/workstation/security-master/securities/{securityId}/passport, keeping provider mapping confidence, pricing posture, trust summary, and downstream usage endpoint-owned. The same passport now renders the endpoint-owned operations workbench panels for identity, provider evidence, terms, readiness, and handoff so browser code does not calculate valuation, ledger, reconciliation, close, or report readiness locally.
The Accounting journal-entry workstream at `/accounting/journal-entries` is a thin browser surface
over the shared manual journal entry workbench endpoints. React renders draft headers, GL account
selection, selected-line Security Master search/picker results, line validation badges, typed source
evidence attachments, treasury-context readiness, save draft, validate, attach-evidence API wiring,
and submit approval commands
from the shared DTOs while versioning, validation, persistence, private-capital fund-event context,
evidence gating, dimensional accounting normalization, period-lock enforcement, selected
ledger-book mutation checks, authenticated tenant/company scoping, and approval handoff remain
server-owned. Browser route `fundProfileId` and `ledgerBookId` values now flow into the shared
manual journal workbench query and are preserved on save, validate, submit, evidence attachment,
and lifecycle transition requests so scoped Accounting work cannot silently fall back to fund-level
manual JE drafts. The journal composer adds a sticky health and command bar, keyboard-oriented
line navigation with insert and duplicate actions, debounced governed draft autosave, and a scoped
local recovery snapshot for changes that have not reached the server. Recovery state is explicitly
labelled as non-authoritative and can be discarded back to the loaded server draft. Validation
issues retain their shared target identifiers and navigate operators to the affected header or line,
while the selected-line inspector edits the existing line entity, allocation, tax-lot, Security
Master, and shared journal dimension fields without moving accounting rules into React. The same
workstream renders the shared
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
rules. Shared FINOPS queue rows preserve server-owned status, owner, due/SLA, severity, blocker
type, close/report impact, evidence, action, and local route labels in the browser blocker panel.
External GL provider warnings compare read-only provider evidence against Meridian-owned
ledger truth; they must not make the external GL the source of ledger authority. When QuickBooks
Online local config is ready, the Accounting GL evidence panel previews the selected company
instead of the deterministic fixture; without that config it remains on `quickbooks-fixture`.
Route-provided `fundAccountId`, `ledgerBookId`, `periodId`, and `workflowStatus` now scope the close command center's
operations-continuity lookup before workflow detail is loaded, preventing a newer unrelated close
workflow from driving the accounting cockpit when operators open a scoped Accounting route.
During workstation bootstrap, the shell lets the Accounting route render its own actionable loading
workspace. That loading view shows the route/workstream being prepared, the Accounting payload groups
still loading, and links to continuity, entity setup, provider posture, and retained report evidence
so operators are not left with only generic shell diagnostics.
Workspace navigation and command-palette root commands canonicalize caller-provided workspace
metadata to the design-document root set: `Trading`, `Portfolio`, `Accounting`, `Reporting`,
`Strategy`, `Data`, and `Settings`. Legacy root labels such as `Research`, `Governance`, and
`Data Operations` remain route aliases and internal compatibility concepts only. App-shell overview
event labels also normalize retained source names before entering the visible evidence timeline.
The browser workstation root (`/`) now opens the Daily Control Tower, a read-only shell projection
of workflow continuity, trust posture, linked context, and timestamped evidence. Its landing model
is a shell-level Home location rather than an eighth workspace: none of the seven root workspace
items is marked current, and the operating-context copy identifies the Daily Control Tower instead
of falling back to Trading. Before the combined finance queue is shown, operators choose an
operating scope or explicitly opt into cross-scope review.
The landing model
now prioritizes the finance sequence Today, Exceptions, Close, Reconciliation, Ledger, Reports,
Evidence, and Data Health before non-finance surfaces. Decision drivers emphasize the finance
queue, trust posture, linked context, and evidence events before the queued work, so the first screen
explains why the operator should act, who owns the issue, what output is affected, which action is
next, and which retained evidence supports it. Scope, freshness, and provider-connectivity trust
cards expose their remediation actions in the card that reports the warning.
Queue evidence association is fail-closed on the stable item/evidence identifier. Similar route,
workspace, severity, or list position never attaches an unrelated proof event; missing correlation
is displayed as unavailable evidence and timestamp posture.
Legacy `/overview` links redirect to that root while suffixed overview routes continue through the
retained workspace alias path.
The app shell exposes the active route as a named workbench landmark and marks that landmark busy
during bootstrap or refresh, so skip-link and screen-reader users land on the current operator
workspace with explicit loading posture.
Shared workstation primitives render visual search context as read-only textboxes and shared tab
strips move focus and the active tab stop with Arrow, Home, and End keys, so route-owned filters and
inspector tabs do not need screen-local keyboard handling.
The first-10-minutes coach mark offers task journeys for Financial Operations, Trading and
Portfolio, Strategy and Research, and Administration. Journey progress is route-backed and retained
in the existing versioned onboarding storage record; switching journeys preserves completed work.
Supporting and metadata typography has a 12px floor in the dense setting. Global accessibility
styles retain visible focus, selected/current state, status boundaries, and data-table structure in
Windows forced-colors mode; reduced-motion mode suppresses non-essential animation and transitions.
The app shell keeps cross-workspace ranking and disclosure chrome centralized, while workspace
linked-context builders, workspace operator-focus candidate construction, workspace
evidence-timeline projection, workflow-continuity trail definitions, trail selection, active-route
matching, primary workflow routing, operating-scope route/query helpers, and workspace
workflow-continuity status builders live outside the shell so quote, exposure, close, break,
automation, reconciliation, provider, run, and report-pack recipient state can evolve with their
owning routes instead of accumulating in `app-shell.view-model.ts`.
The shell workflow-continuity view model now lives in
`app-shell.workflow-continuity-view-model.ts`; `app-shell.view-model.ts` imports it as a coordinator
boundary instead of importing each route-specific continuity, linked-context, operator-focus, and
evidence-timeline helper directly.
Workflow-continuity view-model contracts live in `app-shell.workflow-continuity-types.ts`, so the
route coordinator no longer owns the cross-workspace workflow, decision brief, focus, linked
context, and evidence timeline type definitions that are implemented by the workflow-continuity
builder.
The shell command-palette trigger, route-focus announcement model, no-host demo-data notice, status
panel, and trust strip live in `app-shell.command-palette.ts`, `app-shell.route-focus.ts`,
`app-shell.development-fixture-notice.ts`, `app-shell.status-panel.ts`, and
`app-shell.trust-strip.ts`, keeping keyboard shortcut semantics, active-route focus copy, demo
evidence-path steps, bootstrap recovery copy, failed-workspace items, build/mode posture, source
posture, and provider posture out of the route coordinator while preserving the same app-shell
view-state contract for React.
The rendered workflow-continuity dock lives in
`components/meridian/workflow-continuity-dock.tsx` with its stylesheet in
`src/styles/workflow-continuity-dock.css`, leaving `app.tsx` to compose routes, shell chrome, route
recovery, and the global workstation stylesheet while the dock owns its accessible links,
operating-scope chips, and primary-operator-flow disclosure.
Workspace navigation rail and drawer styles live in `src/styles/workspace-nav.css`, imported by
`components/meridian/workspace-nav.tsx`, so root-workspace routing, preserved operating-scope chips,
expand/collapse controls, status badges, and responsive drawer variants stay with the navigation
component instead of the global workstation stylesheet.
App-shell frame, skip-link, masthead, command-search trigger, trust-strip, session card, startup
status, status-strip, and workbench scroll styles live in `src/styles/app-shell.css`, imported by
`app.tsx`, so the global workstation stylesheet no longer owns root shell chrome.
The final light-first workspace surface cascade now lives in `src/styles/workspace-surface.css`,
imported immediately after `src/styles/index.css` in `main.tsx`, so `index.css` stays focused on
global tokens, Tailwind layers, and legacy shared rules while the workspace surface overrides remain
order-pinned and reviewable.
The root `Meridian Design System/` package is vendored as the visual source bundle for tokens,
component references, patterns, templates, and governance scripts. The browser workstation consumes
the package through copied `src/assets/` files and the `src/design-system/assets.ts` bridge, while
`src/design-system-contract.test.ts` keeps the package manifest, canonical token values, asset bridge,
and runtime CSS alignment under test.
Live shell chrome and Accounting adapters remain dashboard-native TypeScript: `WorkstationTopbar`,
`WorkstationStatusBar`, `TrialBalanceTable`, `AgingTable`, and `ReconciliationComparisonPanel`
adapt the manifest-backed design-system references without importing root JSX or runtime-injected
package CSS into the dashboard build.
Accounting uses one local navigation model in the workspace sidebar. Unique destinations are grouped
under Close, Records, Reconciliation, Review, and Administration; Accounting screens do not repeat
those routes in a horizontal tab strip or task-mode launcher. Governed delivery evidence remains a
Reporting handoff at `/reporting/evidence`, while `/accounting/reporting` is retained only as a
non-navigable compatibility route.
Accounting-specific split-pane, reference-panel, and journal-entry workstation styles live in
`src/styles/accounting-screen.css`, imported by `accounting-screen.tsx`, keeping route styling out of
the shared workstation stylesheet.
Command palette shell, chip, status, and group styles live in `src/styles/command-palette.css`,
imported after shared tokens in `main.tsx`, so the global workstation stylesheet no longer owns the
palette overlay rules.
Workspace filter bar, tab strip, inspector host, and document canvas primitive styles live in
`src/styles/workspace-primitives.css`, imported by `workspace-primitives.tsx`, so the global
workstation stylesheet does not own primitive-specific accessibility surface styling.
`WorkspaceTabStrip` emits stable tab ids from each panel id or an explicit `tabId`, and
`WorkspaceTabPanel` centralizes `role="tabpanel"`, `aria-labelledby`, focus, and hidden-state
semantics for route tabs that expose richer keyboard behavior.
Shared toolbar strip, dense data table, and entity-summary primitive styles live in
`src/styles/ui-kit-primitives.css`, imported by `ui-kit-primitives.tsx`, keeping dense table
keyboard/accessibility behavior and visual ownership in the same component module.
Dense row-detail panel styles live in `src/styles/dense-row-detail-accessibility.css`, imported by
`dense-row-detail-accessibility.tsx`, so row/detail focus handoff, labelled regions, selected-source
badges, and panel chrome stay owned by the accessibility primitive instead of the global stylesheet.
Reporting exposes route-owned task modes as Daily Reporting Cockpit, Report Builder, Run Status,
Delivery Evidence, Exports, and Governance. `/reporting` is the Daily Reporting Cockpit landing
route and stops at the daily decision queue plus focused task-mode links instead of rendering the
full builder surface. `/reporting/report-builder` owns governed output design and schedules,
`/reporting/run-status` owns queue posture, `/reporting/report-packs` keeps the report-pack
approval workflow panel while presenting as Delivery Evidence, `/reporting/exports` owns governed
export artifacts, and `/reporting/governance` owns access, approval, lifecycle, and audit controls.
Each queued daily item exposes blocked status, owner, affected output, next action, and proof or
evidence posture so browser Reporting matches the WPF cockpit decision model.
The Reporting task-mode resolver, report-pack route detector, and launcher links live in
`reporting-screen.task-mode-view-model.ts`, keeping the daily-cockpit IA out of the broader
reporting view model.
Financial Record Explorer drawers now render the shared Number Passport component for the selected
record, carrying source, freshness, reconciliation, approvals, report usage, blockers, evidence
packet, and audit-trail facts through the Accounting, Portfolio, Security Instrument, and Reporting
provenance surfaces. DTO-backed explorers also restore `frexExplorer`, `frexView`, `frexSearch`,
`frexFilter`, and `frexRecord` query state, emit a `Share state` link whose accessible name names
the selected saved view, search text, explicit filters, and proof record when present, and require
operators to supply stable saved-view names instead of timestamp-only labels, keeping browser FREX
review URLs portable without duplicating saved-view storage in React.
FREX route-query parsing, filter restoration, share-link serialization, and accessible share-state
summaries live in `financial-record-explorer.view-state.ts` so the React shell stays focused on DTO
rendering and Number Passport proof presentation.

The browser type mirrors include additive instrument-role, book-position, economic-state,
economic-event, projection-lineage, authoritative book-context assertion, and existing rule-pack
reference shapes. Candidate, posting-command, and Asset Operations payload fields remain optional
for older JSON. The browser submits posting intent and assertions only; server services resolve
authority, and the same shared contracts remain available to WPF without browser-owned accounting
logic or a new route.

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
| `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `W5X-EVIDENCE-001` | Evidence Vault productization |
| `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
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
npm --prefix src/Meridian.Ui/dashboard run lint
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
npm --prefix src/Meridian.Ui/dashboard run smoke:workstation
```

Linting is a correctness-only ESLint flat-config baseline (`eslint.config.mjs`): typescript-eslint
recommended, react-hooks rules, and a local kebab-case filename rule with grandfathered
PascalCase/camelCase directories (`components/accounting`, `components/charts`,
`features/accounting`). No stylistic or formatting rules are enforced. `react-hooks/exhaustive-deps`
stays a warning; treat new warnings in touched files as part of the change. Screen-level
accessibility is exercised by the `src/screens/*.a11y.test.tsx` suites plus axe assertions embedded
in the larger screen tests; keep new screens covered by at least one axe render.

## Change rules

Do not create mobile-first workflows or native mobile clients. Prefer shared read models and
endpoint contracts for behavior also consumed by WPF or host workflows.

## Related docs

- `src/Meridian.Ui/README.md`
- `docs/product/meridian-design-document.md`
- `docs/architecture/desktop-layers.md`
- `docs/source/generated/source-module-index.md`
- `docs/reference/accounting-report-packs.md`
- `docs/operators/governed-reporting-operations.md`

Browser reconciliation route helpers include the shared Accounting casework family for assignment, lifecycle transitions, comments, taxonomy, sign-off, reopen, audit, bulk triage, and bulk status/result lookup; keep these helpers aligned with `UiApiRoutes` and WPF consumers.

Browser extensibility route helpers expose the shared core extensibility catalog plus tenant-template activation-readiness, activation, and activation-history endpoints; keep `src/types.ts` aligned with `Meridian.Contracts.Extensibility` instead of adding UI-local workflow or rule shapes.

## Accounting close browser surface

The Accounting route reuses fund-operations ledger views and now includes trial-balance source-event and approval drill-through affordances. Keep browser-only rendering in `src/screens/accounting-screen.tsx` and shared accounting close contracts in `src/features/accounting/accountingCloseModels.ts`.
