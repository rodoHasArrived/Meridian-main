---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SHARED
path: src/Meridian.Ui.Shared
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-08
---

# src/Meridian.Ui.Shared

## Purpose

UI shared contains shared UI read models, endpoint adapters, and compatibility shims for browser
and desktop surfaces.

## Layer responsibility

This module owns cross-surface operator-facing projection types and shared endpoint helpers. Preserve
compatibility across `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard`, and
`src/Meridian.Wpf`.

## Key folders and files

- `Endpoints/` - shared workstation endpoint mapping and projection helpers, including
  fund-structure ownership lifecycle, portable packaging, archive-maintenance, and data-quality
  monitoring routes.
- Shared read models - DTOs and compatibility shims consumed by browser and desktop clients.
- Project metadata - UI shared dependencies and build settings.

## Important workflows

`FundStructureSetupWorkflowService` backs `/api/fund-structure/setup-drafts/validate` and `/api/fund-structure/setup-drafts/create`, composing `IFundStructureService` commands once for browser and WPF entity setup instead of duplicating setup sequencing in clients.
Ownership lifecycle mutation routes under `/api/fund-structure/links/{id}` require the session-derived `ManageFundStructure` permission before updating, expiring, or replacing governance-impacting ownership links, and the underlying ownership/cash-flow policy is owned by `Meridian.Entities.FundStructure`.

Auth endpoints expose governed user-account administration, password reset, account disable, session
revocation, account audit, role-profile administration, and scoped access assignment administration from
the shared workstation host while delegating identity state to `Meridian.Identity`. `EndpointAuthorization`
keeps the existing global role checks for compatibility and adds scoped authorization helpers so
governance-core routes can require a permission on a specific organization, fund, portfolio, legal
entity, or account.

Preserve cross-surface compatibility when evolving shared read models. Keep ledger/reconciliation
source-of-truth services authoritative. `SecurityMasterWorkbenchQueryService` is published under
`Meridian.Ui.Shared.Services` and composes Application Security Master services into the shared
workstation drill-in projection. `FamilyOfficeReadService` composes the family-office
workstation overview from fund-structure, fund-account, reconciliation, and strategy-run read
services, and emits degraded guidance when linked accounts cannot provide balances, liabilities,
reconciliation state, or evidence completeness. Workstation endpoint registration is split by domain through
`WorkstationEndpoints.*.cs` partial files. Keep the root `WorkstationEndpoints.cs` file as the
coordinator, route new domain-specific endpoint edits to the matching partial file, and avoid
concurrent branches that both modify the root coordinator or the shared
`WorkstationEndpointsTests.cs` test body. For operations-continuity and reconciliation endpoint
changes, start with focused `MapWorkstationEndpoints_OperationsContinuity` /
`MapWorkstationEndpoints_Reconciliation` filters before broad workstation endpoint validation.
Reference-data endpoint groups for bonds, options, equity, futures, FX spot, crypto, deposits,
certificates of deposit, commodities, swaps, and money-market funds adapt `Meridian.Instruments` services
to shared browser/WPF routes. Keep those endpoints as permission and HTTP adapters; instrument
contract/reference logic belongs in the Instruments design module.
The root workstation bootstrap endpoints return canonical `WorkstationDataPayload` and
`WorkstationAccountingPayload` contract types for Data and Accounting. Retained
`/api/workstation/data-operations` and `/api/workstation/governance` routes remain compatibility
aliases only and must not drive new contract type names.
Data upload intake endpoints are registered under `/api/workstation/data/uploads/*`. The template
route serves the contract-owned catalog, and the preview route accepts bounded CSV uploads,
retains the source file under the resolved workstation upload root, and returns schema issues plus
preview rows without mutating trades, transactions, Security Master assets, entity structure, or
ledger/accounting records. Bank-statement CSV import uses
`/api/workstation/data/uploads/bank-statements/import` to validate a retained bank statement,
require a bank fund account, and apply the parsed lines through
`IFundAccountService.IngestBankStatementAsync`; the imported bank data remains reconciliation
evidence and does not post Meridian-owned ledger entries.
`BankFeedTransportService` reuses that same import boundary for scheduled local-file and SFTP
CSV pulls through `IEtlSourceReader`, and delegates Plaid API schedules to `IPlaidIngestionService`
so API feeds stay server-owned and ledger posting remains gated by Meridian approvals.
Plaid endpoints are registered as their own shared endpoint group from `UiApiRoutes`, with read
and mutation access resolved from the workstation session. The shared Plaid workstation service
keeps link-token creation, public-token exchange, item sync, webhook retention, and sandbox
transfer gating server-owned so browser and WPF clients do not handle Plaid access tokens or
duplicate bank evidence ingestion rules.
Provider connection and readiness services project provider setup metadata from the Data
Integration credential catalog into shared rows. Browser and WPF provider surfaces should render
credential fields, allowed environments, diagnostics, evidence, and recovery actions from those
rows instead of maintaining provider-specific local forms.
Accounting-system endpoints are also registered as a shared endpoint group from `UiApiRoutes`.
`Meridian.FinancialOperations.AccountingSystem.AccountingSystemIntegrationService` lists GL
providers, uses QuickBooks Online when local OAuth client id, client secret, refresh token, and
company realm id config are present, falls back to `quickbooks-fixture` otherwise, retains the
latest import in process, and compares external trial-balance evidence against Meridian-owned
ledger truth when the ledger store is available. UI Shared maps the endpoint group and registers the
Data Integration-owned credential-backed connection store; it does not own GL evidence reconciliation
or QuickBooks credential-persistence mapping.
The Data Integration-owned QuickBooks Online lane refreshes access through the server-side token
exchange seam and imports chart-of-accounts, journal-entry, and trial-balance evidence as read-only
reconciliation input.
Meridian remains the source of all ledger truth; external GL imports are evidence and
reconciliation inputs, not override authority. Posting/export remains disabled in the shared
service until an adapter capability explicitly supports publishing Meridian-owned ledger entries,
so browser and WPF clients inherit the same read-only reconciliation posture.
Shared accounting configuration and manual journal entry services also provide durable file-backed
fallback stores under the resolved workstation data root. `FileAccountingConfigurationStore`
persists chart accounts, templates, posting rules, and accounting action audit events at
`workstation/accounting/accounting-configuration.json`, while `FileManualJournalEntryDraftStore`
persists draft and submitted manual journal records at
`workstation/accounting/manual-journal-drafts.json`. Manual journal drafts carry a shared
`ManualJournalEntryTypeDto` so accrual, prepaid expense, expense, amortization, deferral,
reclassification, reversal, capital-call, distribution, subscription, redemption, LP-transfer,
management-fee, and general adjustment workflows persist as typed accounting records instead of
client-local labels. Private-capital entry types require shared treasury ledger context before
approval submission: effective date, idempotency key, fund-event type/id, and capital account
context, with optional investor, payment-intent, and settlement references. Stronger host
registrations can still replace those stores, but browser and WPF clients should consume the shared
services instead of keeping process-local accounting configuration, treasury-context validation, or
draft state. `ManualJournalEntryWorkbenchService` now derives a private-capital activity projection
from retained manual JE drafts and, when registered, `ILedgerJournalStore` posted journals plus
`ReportPackWorkflowService` workflow records. Posted ledger-backed fund events win over same-id
drafts, and the projection keeps fund-event rows, ordered capital-account subledger entries,
ledger-impact rows, capital-account aggregates, published report-output state, signed net activity,
and incomplete-context warnings server-owned for both browser and WPF consumers. When a posted
fund event matches a governed report-pack workflow, the projection maps report-pack id, workflow
state, retained publication manifest details, publication evidence hash, signer/timestamp, and
matched report-line provenance count into the private-capital report-output row.
`/api/ledger/private-capital/activity` can also be filtered by `fundEventId`, `capitalAccountId`,
and `investorId`; the endpoint returns a recomputed slice so report-package drill-throughs retain
matching events, subledger rows, ledger impacts, report outputs, fund-event ledger records, counts,
and net activity without leaking unrelated capital-account rows. Each fund-event ledger record is
rebuilt server-side through `PrivateCapitalFundEventLedgerRecordBuilder` from the filtered
projection rows so browser and desktop clients receive a
single event-level view containing event state, subledger impact, GL impact, evidence, approval,
and report-output posture. Those rows also carry top-level journal, memo, gross/net activity,
capital-account opening/ending net activity, payment/settlement, canonical activity route,
event evidence-packet route, approval id/route when an approval exists, child-count, primary
report-output route/workflow, publication manifest, provenance fields, server-derived
readiness label/reason, and next-action route from the grouped source rows so filtered
drill-throughs stay useful without client-side stitching.
Projections expose the event-level record collection as an empty list when
no fund events qualify, keeping browser and desktop consumers on the same non-null contract.
`/api/ledger/private-capital/fund-event-record` returns one of those shared event-level records
directly by `fundEventId`, including child rows and readiness posture, and returns 404 when the
fund-event id is absent instead of sending clients an empty aggregate to interpret.
The shared workflow library owns close-lane command routing as well: `AccountingReviewOperationsContinuity`
targets `OperationsContinuity` and `AccountingReviewCloseReadiness` targets `OperationsClose`, with
route metadata tied to the operations-continuity API. Browser and WPF clients should consume those
target tags instead of inventing client-local close-workflow routes.
The same shared library also exposes the design-document `Primary Operator Workflow` sequence:
`Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, and `Report`. Keep that sequence in
`BuiltInWorkflowDefinitionProvider` aligned with browser shell continuity and WPF launch targets so
client shells do not maintain separate primary workflow catalogs.
The built-in `accounting-records-evidence-review` workflow owns the v0.15 accounting-records
review path across retained source records, normalized activity, reconciliation cases, ledger
evidence, approvals, document attachments, export manifests, and report-pack/restatement lineage.
Browser and WPF command surfaces should consume that shared workflow instead of creating separate
accounting-record launch lists.
`WorkstationOperationsJsonContext` includes the accounting-record summary and evidence-category
DTOs so shared workstation endpoints can serialize the same accounting-record review payload that
desktop clients round-trip from `Meridian.Contracts.Workstation`.
The built-in `strategy-to-paper-review` workflow keeps its compatibility identifier while presenting
the design-document `Research to Paper Review` label and research-to-backtest market pattern, so
browser and WPF command surfaces share the same research-to-paper continuity language.
Portfolio is also a first-class shared workflow library lane. The built-in `portfolio-position-review`
workflow owns Portfolio workspace entry, aggregate exposure review, run-portfolio inspection,
brokerage-sync review, and snapshot import targets so Portfolio is not hidden inside Reporting.
Run comparison endpoints consume contract-owned compare and diff payloads from
`Meridian.Contracts.Workstation`; keep request/result schema additions in contracts and let this
layer focus on endpoint validation, dependency resolution, and service orchestration.
Single-family-office workstation contracts live in `Contracts/FamilyOfficeContracts.cs` with a
matching `Serialization/FamilyOfficeJsonContext.cs` source-generated JSON context. The shared
`FamilyOfficeReadService` assembles the workstation overview from fund-structure, fund-account,
portfolio, ledger, and reconciliation read seams and exposes `/api/workstation/family-office/*`
endpoint reads for overview, balance sheet, entities, and ownership graph. Family-office financial
summaries must carry source-system, source-document, as-of, valuation, evidence completeness,
reconciliation, review metadata, empty-state guidance, and degraded-state warnings so browser and
WPF operators can trace values back to governed evidence without client-local schema forks.
Diff responses now include strategy id/version metadata, lineage relation, compatibility level,
engine/mode context, artifact completeness, and warnings so operators can compare strategy
versions and execution engines without inferring risk from run names alone.
Promotion readiness projections also preserve retained checklist and evidence references from the
strategy promotion record. Live promotion remains review-required when those evidence references
are absent, which keeps browser and WPF operator states aligned with the server-owned promotion
gate.
Lean result ingestion uses `CanonicalBacktestResultNormalizer.FromLeanResult` from
`Meridian.Backtesting.Sdk` so imported QuantConnect runs enter the same `BacktestResult` storage
and comparison model as native Backtest Studio output. Summary-only Lean imports must retain their
coverage warnings so Strategy/Portfolio compare and diff views do not imply fill, cash-flow,
attribution, or ledger parity that was not imported.
The shared Investment Accounting Transaction Lab service previews Books Before Broker accounting
impact for trades, dividends, fees, accruals, corporate actions, and broker-reconciliation examples.
It emits balanced expected-journal candidates, trial-balance deltas, ledger-impact flags, and
reconciliation expectations through `/api/fund-structure/accounting/transaction-lab/preview`, so
browser and WPF clients can consume the same accounting lab contract instead of rebuilding
accounting rules locally. Requests can opt into explicit Books Before Broker mode to receive
broker-staging readiness, evidence/source blockers, required accounting and broker-routing
approvals, and the expected broker action before any paper/live movement is staged.
Report-pack workflow state is shared here as well: the W4 path moves `Draft` packs to `InReview`
through submission, then to `Approved`, and finally to `Published`; publication requires sign-off,
evidence hash, retained manifest metadata, and retained evidence links for every report-line
provenance pointer so browser and WPF clients do not invent local lifecycle or no-orphan-evidence
rules. The shared W4 acceptance filter keeps governed report-pack acceptance evidence separate from
evidence-vault manifest/export support so pilot readiness cannot mark W4 done from support
artifacts alone, and pilot artifacts should pass through that filter before serialization so the
`GovernedReportPack` stage gate reflects the shared acceptance/support split. The shared
report template registry now seeds built-in Reporting templates as approved immutable records and
exposes shared list, draft, submit, approve, reject, and render routes under
`/api/fund-structure/reporting/templates*`. Draft versions cannot render until approved, invalid
drafts cannot enter review, and approving a new version marks earlier approved records as no longer
latest without mutating built-in history. Custom draft and approval records are retained under the
resolved workstation data root at `workstation/reporting/report-templates.json`, so template
authoring state survives host restart. Approved custom templates can carry report-writer grid
definitions; the shared registry validates and renders those grids through `ReportWriterGridEngine`
instead of returning browser-local or WPF-local calculations. Render requests may include temporary
grid definitions for live no-code previews; the registry renders that request-scoped layout without
persisting it back to the approved template. Browser and WPF clients should render that shared
template state instead of treating built-in templates as the full authoring workflow.
Template definitions and report-pack workflow records now carry shared access policies for
user-locked, restricted user/group/company, and company-wide report audiences. `ReportAccessPolicyEvaluator`
normalizes and validates those policies, `/api/fund-structure/reporting/templates*` filters and
guards template reads/renders with the session actor plus admin override, and the workstation
Reporting payload filters restricted template and report-pack rows before distribution and recent-run
aggregates are projected.
`ReportPackRunReadService` uses the same registry list when it is registered, so Reporting payloads
include custom template drafts, in-review records, approvals, latest-approved status, and
report-writer grid metadata alongside built-in templates. For custom templates, that projection
keeps row fields, column fields, metrics, formula expressions, Top-N, and sort settings in the
shared payload so browser and WPF surfaces can render no-code report-writer canvases without
client-local template parsing or recalculation.
Generic Reporting orchestration runs and governed report-pack workflow records also share one
operator read model here. `FileReportingRunStore` persists `ReportingOutputManifest` plus audit
trail snapshots for scheduled/ad-hoc Reporting runs, `FileReportPackWorkflowRecordStore` persists
report-pack workflow records after create, submit, approve, publish, reject, restate, and archive
mutations, and `ReportPackRunReadService` projects both sources into the shared
`WorkstationReportingPayload`. Browser and WPF Reporting surfaces should consume those recent-run
rows for true template, schedule, attempt, approval, publication, evidence-bundle, restatement, and
drilldown status instead of reintroducing fixture rows in workstation bootstrap payloads.
Those recent-run rows also include typed drilldown links and next-action references for evidence,
approval submission/review, publication, release review, restatement, and archival work so clients
can render clickable routes while preserving reference-only POST/action metadata.
Fund-operations Reporting payloads now include portfolio reporting cuts derived from the same
shared cash/financing, strategy-run portfolio, account, and NAV attribution state used by
Accounting. `FundOperationsWorkspaceReadService` emits consolidated fund, strategy, and user-tag
rows with exposure, cash, P&L, shadow-NAV, variance, source-count, and version-stamp fields so
browser and WPF clients do not recalculate report cuts from separate portfolio APIs.
The read service also projects `livePortfolioViews` from those same cuts. Each row points to the
shared `/api/workstation/portfolio/summary` route, preserves source-backed freshness state, carries
liquidity text, and links single-run strategy cuts to `/api/portfolio/{runId}/cash-flows` for
cash-ladder evidence. Rows fail closed as `Blocked` when no fund account or portfolio run source
backs the reporting view.
It also projects `pnlSlices` for daily, weekly, monthly, and yearly P&L from retained portfolio run
timestamps. Each row carries realized/unrealized/current/prior/change values, source counts,
readiness text, a shared `/api/workstation/reporting?pnlSlice=...` route, and deterministic version
stamps; windows with no current source run fail closed as blocked instead of displaying synthetic
period P&L.
`FundOperationsWorkspaceReadService` also projects `crossFundConsolidations` from all active fund
accounts plus all fund-scoped strategy-run portfolio summaries. It emits company-wide, fund-level,
and legal-entity rows with exposure, cash, P&L, shadow-NAV, source counts, readiness text, and
deterministic version stamps; when no source-backed account or run data exists, the company row
fails closed with blocked readiness instead of synthetic consolidation values.
The same read service also emits structured export descriptors for regulatory trial-balance,
warehouse ledger-fact, investment portfolio-cut, and cross-fund consolidation outputs, and serves
`/api/fund-structure/reporting/structured-exports/{exportId}` from the same source-backed workspace
projection. The JSON payload includes stable column metadata, culture-invariant string row values,
readiness warnings, retained-path metadata, and deterministic version stamps so downstream
regulatory, warehousing, and investment-decision consumers can ingest governed data without
browser-local export shaping.
`FundOperationsWorkspaceReadService` also exposes built-in report branding themes through the
reporting summary, validates custom branding overrides with normalized theme ids and hex colors,
persists the selected theme on generated report-pack snapshots and manifests, and applies the same
theme to generated HTML, PDF text, and the XLSX `Branding` worksheet. That keeps logos, colors,
footer copy, disclaimers, and firm identity attached to the retained package artifact instead of the
browser view.
`ReportPackDeliveryService` persists delivery and delivery-failure attempts under the resolved
workstation data root at `workstation/reporting/report-pack-deliveries.json`; published and
restated workflow records can therefore show real delivery history, retry attempts, recipient
state, and last-sent timestamps instead of static distribution placeholders. Delivered attempts
also receive deterministic package metadata with default PDF/XLSX/CSV artifacts, retained-package
paths, secure email-link or portal URLs, and delivery-mode inference for portal, vault, and
internal-route channels; callers can override the mode and requested formats in
`ReportPackDeliveryRequestDto`. Package manifest reads are token-gated by
`ReportPackDeliveryService`, using constant-time token comparison and shared GET routes for the
email-link package URL and `/portal/reporting/packages/{packageId}`. `ReportingScheduleService`
persists operator-managed schedule records at `workstation/reporting/reporting-schedules.json`,
normalizes configured distribution targets, runs due schedules through `IReportingOrchestrationService`,
advances next due/as-of dates, and asks `ReportPackDeliveryService` to package the latest published or
restated report pack for each target. Schedule run results return delivery attempts and warnings so
operators can distinguish generated reports from actually packaged email-link or portal deliveries.
`ReportingRunCommandService` also runs approved built-in templates on demand through the same orchestration and run-store seam,
returning `WorkstationReportingRunPayload` rows with ad-hoc trigger metadata and review next
actions. The fund-structure
endpoint group exposes those delivery and schedule commands, while `FundOperationsWorkspaceReadService`
also writes rendered HTML and PDF statement artifacts alongside JSON, CSV, XLSX, and provenance
outputs so frozen report packs include inspectable document-format evidence.
The same read model emits `reportPackDistributions` recipient records instead of static
report-pack target strings. Browser and WPF clients should show recipient, role, channel, owner,
state, due date, and pending summary from those records so operators can see who receives each
package and what distribution work is pending.
The shared
fund-operations workspace read service carries the active Operations Continuity accounting-record
summary on the governance lifecycle projection so Fund Ledger report-pack handoff can render the
same evidence categories, readiness count, and route hints as the browser continuity detail without
recomputing close evidence locally. The category rows include required evidence labels, so browser
and WPF clients render document, export, restatement, approval, ledger, reconciliation, normalized
activity, and source-record requirements from shared contracts. The shared
fund-structure endpoints expose report-pack workflow creation,
validation, submission, approval, review rejection, publication, restatement, history, and archival
routes backed by shared contracts; review rejection is valid from `InReview`, records the reason,
actor/role metadata, and optional evidence links, and rejected packs must return through draft,
submission, and approval before publication. Restatement changed lines must carry evidence links before
the workflow can advance. Publication also rejects line provenance that omits the reported value or lacks a run,
source-session, ledger-entry, reconciliation-case, or reconciliation-run pointer, and each retained
line must carry ledger, provider-event, Security Master definition, reconciliation-outcome, and
approval references before publication. That keeps value-level report lineage enforceable in the
shared service instead of client code.
Generated governed report packs enrich line-level provenance with display labels,
source-system tags, related ledger and journal evidence IDs, line amounts, latest evidence
timestamps, and API routes back to run continuity, ledger trial-balance, reconciliation, and
Security Master search evidence so report consumers can drill into accounting support without
client-local route inference. The shared ledger amount provenance service exposes those retained
lineage pointers as a click-through drilldown for a report-pack ledger amount, combining the ledger
line, strategy/run evidence, Security Master pointer, reconciliation summary, durable case ids,
related case status/owner/sign-off posture, approval state, report usage, retained report-pack
artifacts, audit-pack readiness category evidence, export evidence, and restatement lineage. When a retained report
line carries a retained Security Master id, the drilldown uses that id to pull in open Security
Master exception cases for the same instrument. When a retained report line does not carry a direct
provider-event pointer, related provider-ledger cases can contribute provider-event evidence from
their upstream provider sync cursor and route metadata. Corporate-action and factor casework also
contributes structured provider event id/type, required feed, provider evidence source, and Security
Master id metadata to the drilldown. Provider-ledger
corporate-action/factor casework now also retains ledger-effect metadata, so the drilldown surfaces
the valuation or journal-support kind, principal/income amount, and journal
preview line count so report-line users can see how provider factor, amortization schedule, or cash
activity supports the ledger amount. Comma-separated required provider feeds, such as amortization
and factor schedules on the same provider event, are preserved as one structured evidence value
instead of being truncated at the first feed. Warnings remain only when neither retained lineage nor durable
casework can identify provider evidence for the amount. Ledger amount provenance also projects retained strategy/run
lineage into structured run links, including run id, display label, route, source system, capture
time, and whether the run pointer was captured at the selected line scope. Browser and WPF
drilldowns can therefore show the strategy/run origin directly instead of parsing generic evidence
rows.
Evidence packet validation also owns the shared SLA/freshness policy and Meridian Assurance Score
calculation for provider validation, replay, reconciliation, approval, and reporting evidence so
client surfaces consume the same readiness posture instead of recalculating it locally.
The workflow-summary endpoint also projects a cross-workflow Meridian Assurance Score from the
shared workspace postures, giving browser and WPF shells the same readiness indicator for Trading,
Portfolio, Accounting, Reporting, Strategy, Data, and Settings instead of client-local scoring.
Shared workstation fallback payloads keep retained `Research*` and `Governance*` contract names for
route and DTO compatibility, but visible session roles, strategy summaries, reconciliation
sign-off roles, and calibration summaries use canonical Strategy and Accounting wording.
Trading fallback guardrails and strategy promotion fallbacks also use Accounting wording so shared
bootstrap payloads do not expose legacy governance-lane copy while route aliases remain intact.
Workstation root endpoints are mapped from `UiApiRoutes` canonical constants for Strategy, Data,
Accounting, Reporting, Trading, and Portfolio, with Research, Data Operations, and Governance
routes retained as compatibility aliases that return the same shared payloads.
DK1 trust-gate readiness also normalizes retained owner labels from older automation packets, so
`Research`, `Data Operations`, and `Governance` become Strategy, Data, and Accounting before the
shared readiness payload reaches browser or WPF shells.
Run review packets also keep compatibility target tags such as `FundReconciliation` and
`SecurityMaster`, while their visible work-item workspace labels route reconciliation and coverage
attention through Accounting. Report-pack readiness and evidence warnings use neutral report-pack
wording instead of exposing the retained Governance repository type name to operators; repository
validation errors and Evidence Workbench node source labels follow the same wording while retaining
the contract-owned type names.
The file-backed Evidence Vault now stores more than manifest retention: retained local artifact
refs with file paths are copied into a vault bundle with content hash, size, source route, and
canonical subject metadata, while route-only artifacts stay as manifest references. The vault write
boundary rejects every retained artifact reference, copied or route-only, that omits canonical
subject linkage, lacks an addressable path/route, or uses unsupported subject kinds, so retained
statement/report/approval/screenshot artifacts cannot become orphan evidence. This keeps
packet/report/statement/screenshot/approval evidence retention server-owned instead of
client-local.
Retained vault bundles are also first-class Evidence Workbench subjects through the
`evidence-vault` subject kind: the shared contributor projects the retained manifest and each
copied artifact into the same packet graph, preserving hashes, source routes, and canonical subject
linkage for browser/WPF parity.
The shared Audit Trail Explorer service projects retained execution, promotion, order, control, and
Operations Continuity close/reconciliation/approval timeline records into contract-owned timeline rows and exposes `/api/execution/audit/search` with
server-side text, run, actor, symbol, action, outcome, correlation, normalized object, related
object, time-window, and limit filters. Timeline ordering is deterministic by occurrence time and
audit id, and text search includes related-object ids and evidence routes so close, reconciliation,
approval, promotion, and control evidence can be found through the same endpoint. Manual override
audit rows resolve to operator-action objects keyed by `overrideId`, while circuit breaker rows
resolve to execution-control objects with direct control routes, so operations review can
distinguish who staged a live override from who opened or closed a trading halt.
Use that shared service for browser and WPF audit search rather than client-local timeline
normalization.
The Security Master workstation workbench also exposes a shared Instrument Passport at
`/api/workstation/security-master/securities/{securityId}/passport`. The passport reuses the
server-owned trust snapshot to combine identity, provider mappings, lifecycle events, corporate
actions, pricing/trading-parameter readiness, downstream usage, and trust posture for browser and
WPF clients. Each provider mapping also carries a confidence row with source, freshness, confidence
score, related identifier-conflict IDs, conflict summaries, and override history so clients can show
provider-to-Security-Master trust without rebuilding mapping logic locally.
Security Master trust and conflict summaries use downstream Data, Accounting, and Reporting
workflow labels so browser and WPF clients do not surface retained Governance-era wording for
operator-facing review.
The shared Security Master endpoints also expose the ReferenceData-owned approved starter custom
asset profile catalog at `/api/security-master/asset-profiles` and allow `/api/security-master/search`
requests to filter profile-backed securities by custom profile id, pinned profile version, profile
field key, or profile field value without requiring a text query. Browser and WPF clients should
use those contract-owned filters instead of parsing profile-backed asset-specific JSON locally.
Certificate-of-deposit reference endpoints also consume the ReferenceData-owned
`ICertificateOfDepositReferenceService` rather than an Application-owned reference lookup.
Commodity reference endpoints follow the same pattern through ReferenceData-owned
`ICommodityReferenceService`.
The same endpoint group now exposes governed profile lineage plus admin-only draft, approve, and
rollback actions under `/api/security-master/asset-profiles/*`. These routes require
`AdminMaintenance` and server-resolved actor metadata, returning audit events with rationale,
correlation id, profile version, status, and approval reference so clients do not maintain local
profile governance state.
Portfolio multi-asset coverage is exposed through the shared workstation route
`/api/workstation/portfolio/multi-asset-coverage` and the `IMultiAssetCoverageReadService`
projection. It joins Security Master validation/profile posture, required provider evidence,
ledger classification, reconciliation evidence categories, and close-readiness blockers into a
single read model for browser and WPF clients. Coverage rows include contract-owned drill-through
targets for Security Master passport/profile, provider evidence, reconciliation break/case, ledger
mapping/evidence, Asset Operations detail, and close readiness so clients can navigate without
maintaining local routing rules. `/api/workstation/assets/{securityId}/operations` returns the
shared Security Master-keyed operations detail for loans, bonds, and later asset classes instead of
surface-specific read models. Missing retained provider inputs remain review-required or blocked rows; clients must not
mark asset classes ready with UI-local checks. The shared read model treats private-credit
commitments, unfunded commitments, paydowns, covenant notices, and obligation schedules as
`DirectLoan` provider evidence. Governed structured/private assets remain profile-backed
`CustomAsset` coverage rows and require retained servicer/trustee report, warehouse tape, NAV,
capital-call, distribution, obligation-schedule, and valuation evidence before the provider and
close-readiness targets can move to ready.
Provider-ledger reconciliation is shared service/API behavior: it reads the latest brokerage sync
projection, compares it with the internal fund-account balance snapshot, validates Security Master
coverage, and retains the latest detail under workstation data for browser and WPF clients to
consume later without adding client-specific reconciliation logic. Break records preserve stable
keys, owner assignment, tolerance, first/last-observed aging, and sign-off state from the previous
latest detail so repeated provider-ledger variances can be controlled as accounting casework. When
the shared reconciliation break queue is registered, those provider-ledger breaks are also seeded as
durable queue cases with route metadata, provider sync cursors, tolerance bands, required sign-off
role, structured "Explain the Break" summaries, and audit entries so controller workflows can
govern them with the same case lifecycle used by strategy and shadow-book breaks. The explanation
contract carries source systems, probable cause, ledger impact, suggested next action, and evidence
links for browser and WPF clients instead of leaving each client to reconstruct break narratives.
Provider-ledger Security Master identity gaps and stale resolved provider mappings are routed as
Security Master steward casework, with identity- or stale-mapping-specific routes, tolerance
profiles, team ownership, and sign-off roles instead of generic fund-accounting ownership.
Reconciliation details also emit provider-to-Security-Master confidence passports for every
provider position, preserving the resolution path, confidence score, validation issue codes, and
identifier-conflict evidence alongside the persisted accounting detail. Stale provider evidence now
caps passport confidence and adds a `PROVIDER_EVIDENCE_STALE` validation issue so controller review
can distinguish a resolved but stale provider mapping from a fresh resolved mapping. Amortization
schedule events are retained as distinct Security Master schedule feeds with principal/factor
metadata so fixed-income and structured-product close readiness can prove amortization support
without flattening it into generic factor evidence. When the shared
break queue is registered, those stale resolved mappings seed governed Security Master cases with
provider sync cursors and steward sign-off metadata. When the
Security Master operator override store is registered, the passport also carries the latest override
audit-history entries for the resolved instrument so controller review can see provider mapping
confidence and governed override context in the same retained reconciliation record.
When the Security Master conflict service is registered, resolved provider passports also include
open identifier conflicts for the resolved instrument, add `SM_IDENTIFIER_CONFLICT`, and cap mapping
confidence so controller review can distinguish clean resolution from unresolved provider identity
contention.
The same retained detail now includes a shadow-book comparison section for controller review. It
compares internal ledger cash, position market value, total equity, income/accrual amounts, and
unrealized P&L against provider balances, positions, and activity when both sides are available.
Realized P&L is also compared when the provider activity feed retains explicit realized P&L on
fills; the service does not infer realized P&L from fill notional. When retained custodian
position lines exist for the internal snapshot date, the same comparison adds per-symbol quantity
and market-value rows against provider positions so statement-level position breaks are visible
inside the retained provider-ledger detail. When retained bank statement lines exist for the
snapshot date, the detail also compares bank closing cash against internal ledger cash and compares
bank income cash flow against provider dividend/interest activity. Pending settlement remains an
explicit unavailable dimension until both the internal shadow book and provider projection retain
that amount. Non-primary shadow-book variances are promoted into reconciliation break records with
the same owner, tolerance, aging, sign-off, explanation, and optional durable casework metadata as
primary provider-ledger checks, while account cash, aggregate securities value, and total equity
continue to use their existing top-level break records to avoid duplicate controller tasks.
Provider-ledger details also include corporate-action and factor-schedule readiness. That section
uses provider capability routing, provider positions, provider income/principal cash activity, and
Security Master passport resolution to show whether split, dividend, coupon, amortization, paydown,
or factor evidence is ready for accounting support. Required balance, position, reconciliation,
corporate-action, and factor-schedule provider capabilities are checked at run time; fixed-income
and structured positions now degrade on the dedicated factor-schedule route instead of relying on
the generic corporate-action route. The brokerage sync projection can now retain explicit provider
corporate-action and factor events, so the persisted reconciliation detail distinguishes direct
provider event evidence from candidates inferred from positions or cash activity. The persisted
detail also retains provider evidence candidates for equity corporate-action exposure,
fixed-income factor/coupon schedules, provider corporate-action/factor/loan-schedule events,
dividend/interest cash activity, and principal/paydown cash activity, including provider event ids,
required feeds, Security Master attribution, amount/quantity context, and candidate status for controller handoff.
The retained readiness payload also projects ledger-effect rows: provider factor events become
factor-history valuation inputs, provider loan-schedule events become loan-schedule valuation
inputs, attributed dividend and interest cash activity carries cash/income journal-preview lines,
and principal/paydown cash activity carries cash/principal journal-preview lines for downstream
ledger review. The same payload now includes Security Master schedule feed rows that identify the
target feed kind, required provider feed, provider event, factor/cash amounts, Security Master
attribution, and whether the row can update Security Master schedule history and support ledger
valuation. When the shared break queue is registered, degraded or blocked
corporate-action and factor candidates are seeded as durable Security Master steward casework with
route, upstream cursor, required-feed, ledger-effect, amount, journal-preview count, and sign-off
metadata.
Security Master exception casework also routes open identifier conflicts and pending operator
overrides into the same durable break queue when that repository is available. The queued cases
carry Security Master routes, provider/conflict or override cursors, steward sign-off roles,
explainability summaries, and audit history so unresolved symbols, conflicting identifiers, stale
mapping reviews, and override requests can follow the governed reconciliation-case lifecycle.
Fund-account close readiness now links the latest provider-ledger Security Master passports back to
open Security Master queue items for the same held securities, so pending identifier-conflict or
operator-override cases can block the account close even when the case itself is not fund-account
scoped.
Evidence Workflow Fabric now exposes those open identifier conflicts as a first-class
`security-master-conflict` evidence subject. The packet contributor reads the shared conflict
service, links open conflicts to their durable case ids, and keeps route-only Security Master
artifacts in the manifest so browser and WPF clients can review conflict evidence without owning a
separate case model.
Evidence Workflow Fabric also exposes operations close approvals as the canonical `approval`
subject. The packet contributor reads `IOperationsContinuityWorkflowService`, resolves `current` to
the latest close workflow, and projects approval state, retained approval audit events, close
checklist control posture, report-pack readiness, accounting-record evidence categories, and
route-only approval/rejection links so browser and WPF clients do not duplicate close sign-off or
v0.15 audit-record evidence logic.
Accounting records are also first-class Evidence Workflow Fabric subjects under
`accounting-record`, mapped to the shared `accounting-records-evidence-review` workflow, so
operators and evidence-vault exports can address retained audit records directly instead of only
through an approval packet. The standard evidence endpoints serve packet, graph, validation, and
manifest export requests for `accounting-record/{workflowId}`, preserving the same vault identity
and retention path semantics as report-pack evidence. Vault search can rediscover retained
accounting-record manifests by their canonical `accounting-record/{workflowId}` subject even when
callers did not provide extra linkage metadata during export because manifest export defaults the
linkage `evidenceSubject` to the packet subject and stamps `accountingRecordId` for accounting
record packets.
The evidence packet validator applies assurance freshness policies to both the accounting-record
root and category nodes, so stale source-record, reconciliation, ledger, approval, document, export,
or restatement evidence lowers shared assurance before close approval or publication.
Statement reconciliation mutation endpoints trust the authenticated workstation session actor for
statement-run intake and reconcile commands. Client-supplied `ImportedBy` or reconcile actor values
are treated as untrusted payload hints and are replaced at the shared endpoint boundary before the
reconciliation API service persists durable cases, comments, attachments, SLA metadata, and audit
events.

Fund-structure endpoints expose `/api/fund-structure/ledger-mapping-view` as the shared accounting
control surface for account ledger mappings. The endpoint returns server-derived assignment source,
unmapped-account issue codes, and recommended action so browser and WPF surfaces do not invent
client-local mapping or posting readiness rules. Ledger group assignment validation and reference
normalization use the Entities-owned `LedgerGroupingRules` policy rather than endpoint-local rules.
Ledger mapping assignment mutations require an authenticated operator with `ManageDirectLending` or
`AdminMaintenance`, and audit attribution must come from the resolved session actor rather than
client-supplied request fields.
Auth endpoints expose `/api/auth/role-profiles` as the governed write path for custom authority
profiles. The Identity-owned file-backed role-profile store persists profile grants under the
storage root, merges custom profiles into `/api/auth/roles`, and feeds `UserProfileRegistry` so
configured `roleProfileName` accounts use the stored permissions after login. Keep this module as
the endpoint/read-model adapter; do not reintroduce session, profile, or role-profile persistence
state here.
Operations Continuity endpoints expose
`/api/workstation/operations/continuity/approval-policy-matrix` as the shared configuration read
model for approval governance. The endpoint is read-permission protected and returns the
server-owned approval actions, required permissions, reviewer independence, report-pack, checklist,
and audit-event metadata used by Settings, browser, and WPF surfaces. The policy service itself is
owned by `src/Meridian.FinancialOperations`; this module only adapts it to workstation HTTP routes.
`/api/workstation/operations/continuity/approval-policy-rules` is the admin-protected governed
write path for approval-policy rule edits. It trusts the authenticated session actor over the
browser payload, validates required approval counts and route shape, persists overrides through
the application service, and returns the updated matrix plus audit event, rationale, and
correlation evidence.
`/api/workstation/operations/continuity/close-calendar` exposes the account-close calendar read
model with optional fund-account and period filters. It returns server-derived next due task,
owner, readiness score, component breakdown, provider-freshness blocker, next-action,
approval-count, and workflow route metadata so close calendars stay aligned with Operations
Continuity rather than client-local date calculations. The calendar service itself is owned by
`src/Meridian.FinancialOperations`; this module only adapts it to workstation HTTP routes.
`/api/workstation/operations/continuity/close-calendar-items` is the admin-protected governed
write path for calendar owner and due-date configuration. It validates the target workflow and
checklist task, trusts the authenticated session actor, persists the override through the
Financial Operations calendar service, and returns the updated calendar item plus audit event and
correlation evidence.
`/api/workstation/operations/continuity/{workflowId}/close-readiness` exposes the same
controller-facing readiness score used by workflow detail and close calendar payloads. It keeps
provider freshness, Security Master completeness, ledger posting, reconciliation-break,
report-pack, and approval blockers server-owned so browser and WPF clients do not recalculate close
status.
`/api/ledger/periods/{periodId}/trial-balance`,
`/api/ledger/periods/{periodId}/trial-balance-report`, and
`/api/ledger/periods/{periodId}/pnl-summary` expose closed-period ledger reports from the shared
ledger book service. They keep trial balance, signed period-locked report totals, revenue,
expense, realized net income, accrual-basis adjustment impact, prior-period variance,
open-break count, and signoff posture server-derived for browser and WPF accounting surfaces.
`/api/ledger/reports/trial-balance` and `/api/ledger/reports/pnl-summary` aggregate those
closed-period summaries across a selected book, fund, node, accounting basis, and date range for
regulatory, investor, and internal reporting surfaces.
Manual journal entry workbench routes under `/api/ledger/journal-entry-workbench*` persist draft
and submitted approval records under the resolved workstation data root. The shared service
validates GL account, balance, currency, Security Master, typed evidence attachments, private-capital
treasury context, and version state before save or approval submission. Draft save remains
permissive for in-progress work, but approval submission requires retained source evidence and, for
private-capital entry types, retry-safe fund-event/capital-account context so browser and WPF
clients do not present process-local accounting work as durable ledger evidence. The workbench
response includes the shared private-capital activity projection, which skips incomplete fund-event
drafts and surfaces ledger-impact, projection, and report-output readiness warnings instead of
inventing capital-account, GL-impact, or stakeholder-package rows. The read-only
`/api/ledger/private-capital/activity` endpoint returns that same projection directly, giving
Reporting, browser diagnostics, WPF, and later LP/audit review surfaces a first-class activity
read model without loading the manual journal authoring payload.
Closed Operations Continuity workflow detail payloads include the governed close-package
publication manifest metadata produced by the close command: signer, sign-off rationale, retained
manifest id/route, evidence hash, report pack id, linked evidence, and checklist approvals.
`/api/fund-accounts/{accountId}/close-readiness` exposes the account-scoped close-readiness score
for provider-ledger workflows. It aggregates account readiness, provider freshness, Security Master
coverage from the latest provider-ledger reconciliation, internal ledger balance evidence,
corporate-action/factor-schedule readiness, internal ledger balance evidence, account-scoped break
queue casework, and pending sign-off state into weighted components, blockers, and next actions for
controller-facing close review. Components and blockers carry evidence links back to the retained
provider-ledger reconciliation or casework route so the score is directly auditable. Fixed-income
and structured provider positions now require direct retained provider factor-schedule,
loan-schedule, or matched principal/paydown ledger-effect evidence before the
corporate-action/factor component can be marked ready for close. Retained shadow-book comparison
breaks also keep the reconciliation
component in review-required state so provider/custodian statement variances cannot close merely
because the aggregate provider-ledger checks matched.
When provider-routing capability metadata is registered, the reconciliation service blocks runs
that cannot route account balances, account positions, or reconciliation-feed capability for the
fund account, so unsupported providers fail as accounting break evidence before ledger comparison.
The account close-readiness provider-data component now consumes those retained capability checks:
required capability misses block close readiness, while degraded quote-history,
corporate-action, factor-schedule, or asset-class capability checks require controller review.
Held-security Security Master exception cases also feed the close-readiness Security Master
component, so unresolved conflicts, stale mappings, or pending override approvals cannot hide
behind a green provider-ledger comparison.
Approved Security Master operator overrides now move their durable exception case through review and
resolution, then remain ready for an independent steward sign-off so close readiness and report-line
provenance can distinguish pending sign-off casework from approved definition evidence.
Ledger amount provenance drilldowns now preserve related reconciliation case materiality and aging
metadata, including severity, variance, tolerance band, sign-off actors, latest sign-off
actor/time/note, SLA state, age band, and business-age hours, so report-line click-throughs can show
whether a retained amount is still inside controller-owned break review.
The Security Master link in the same drilldown now carries the retained display label, source
system, and evidence id from the report-pack lineage pointer alongside the durable Security Master
id, so report-line consumers can show the exact definition evidence used for the amount.
Provider-ledger Security Master casework now retains provider-to-Security Master passport context
directly on the durable case: passport status, resolution source, confidence, freshness, provider
staleness, validation issue codes, identifier conflicts, and override-history count.
Resolved provider positions must also point to an active Security Master record; inactive
definitions are retained on the passport but block the Security Master reconciliation check and
close-readiness component instead of being treated as approved ledger-posting evidence.
Positioned accounts also require corporate-action capability as a valuation-readiness signal; if
that capability is unavailable, reconciliation can still run but records a degradation break for
controller review. Provider-ledger runs also check account-position and historical-quote routing
for each held asset class, so a provider that supports positions generally but lacks the held
asset-class or valuation-mark history records review-grade capability breaks before close
readiness treats the evidence as clean.

## Diagrams

See `DIA-BROWSER-WORKSTATION` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-SHARED -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SHARED -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-UI-SHARED-001` | Complete W5 shared read-model visibility for close-package and provenance timelines | done | high |
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj /p:EnableWindowsTargeting=true --logger "console;verbosity=normal"
```

## Change rules

Do not put browser-only or WPF-only product logic here. Keep shared read models compatible and route
domain-specific endpoint edits to the matching partial file.

## Related docs

- `src/Meridian.Ui.Services/README.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-module-index.md`
