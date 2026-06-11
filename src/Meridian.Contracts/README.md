---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CONTRACTS
path: src/Meridian.Contracts
status: active
owner_lane: Contract Compatibility
last_reviewed: 2026-06-08
---

# src/Meridian.Contracts

## Purpose

Meridian contracts contains shared DTOs and cross-layer contracts used by host, services,
dashboard, and WPF.

## Layer responsibility

This module owns stable transport payloads, compatibility-safe DTOs, and shared schema objects.
Consumers depend on contracts; contracts should not depend on host, UI, application orchestration,
or provider implementations.

## Key folders and files

- `Workstation/` - workstation and operator workflow DTOs.
- `AssetOperations/` - shared Security Master-keyed asset operations DTOs, readiness payloads,
  and query/command service contracts.
- `Backfill/` - shared historical backfill run-result and per-symbol completeness signal payloads
  published under `Meridian.Contracts.Backfill`.
- `Coordination/` - shared cluster lease, leadership, scheduled-work ownership, subscription
  ownership, lease-record, and coordination snapshot contracts published under
  `Meridian.Contracts.Coordination`.
- `FundStructure/` - fund-structure command, query, DTO, ownership lifecycle, and graph-validation payloads.
- `Plaid/` - Plaid provider, account-link, transaction, investment, identity, webhook, and transfer DTOs.
- `Services/` - cross-module service contracts such as backtest preflight and Security Master
  validation gates, fund-structure graph/query orchestration, operational scheduling/trading
  calendar coordination, plus Environment Design draft/publish/runtime projection contracts that
  must be injectable without depending on Application implementation types.
- `Etl/` - shared ETL DTOs, the job-definition store contract, and the SFTP publisher port used by
  Application orchestration, Data Integration ETL services, Infrastructure adapters, and
  Storage-backed persistence.
- `Extensibility/` - stable financial operations core object, configurable layer, governed
  foundation, configuration envelope, tenant template, activation readiness/result, and
  extensibility catalog DTOs.
- `Monitoring/` - shared event-pipeline metrics contracts, snapshot payloads, and monitoring
  webhook sink contracts consumed by Application, Platform tracing/monitoring, diagnostics
  endpoints, WPF, and browser workstation services.
- `Pipeline/` - shared pipeline policy constants and runtime pipeline statistics DTOs consumed by
  Application pipelines, Platform monitoring, diagnostics endpoints, WPF, and browser workstation
  services.
- Contract DTO files - shared payloads consumed across host, UI services, desktop, and dashboard.
- Project metadata - serialization and package references for contract consumers.

## Important workflows

Treat additive and breaking changes as cross-module compatibility work. Operations Continuity
workflow DTOs publish the shared broker intake, Security Master, ledger posting, reconciliation,
reconciliation-break assignment/escalation, approval, close, and audit vocabulary consumed by both
browser and WPF workstation clients. Keep
returned workflow blocker codes in `OperationsWorkflowContractMatrix.BlockerCodes`, including
ledger journal context-validation failures, so clients can handle command failures without parsing
messages. Close-checklist control approval blockers are part of that shared vocabulary and must
remain contract-owned rather than browser-only or WPF-only state. Close readiness score payloads
are also contract-owned and include server-derived Security Master, position, cash, ledger,
pricing, reconciliation, corporate-action/factor-schedule, report, and approval components so UI
clients can render readiness without client-local scoring rules. Gate posture requests can carry
required provider capability gaps and degraded provider capability gaps from the shared provider
routing matrix; the server turns those into broker-ingest blocker/review states instead of asking
clients to infer close readiness from provider metadata. Closed operations workflows also
publish `OperationsClosePackagePublicationDto` with close-package id, retained manifest id/route,
evidence hash, sign-off actor/rationale, report pack id, evidence links, and checklist approvals so
clients can inspect close-package publication without rebuilding package metadata locally. Ledger
journal line DTOs carry optional Security Master identity, client-observed active status, approval
reference, provenance, and ledger-mapping evidence, and the shared blocker vocabulary includes the
posting gate failures used when an instrument-bearing posting lacks authoritative server-side
Security Master active-status proof or when journal/line provenance does not reference the resolved
Security Master id. The vocabulary also includes symbol
mismatch blockers for journal candidates whose instrument line symbol diverges from the
journal-level Security Master symbol and mapping mismatch blockers for generic ledger-mapping
references that do not name the resolved symbol or Security Master id.
Ledger draft requests also carry explicit approval and ledger-mapping evidence flags so controller
workflows can block the draft gate before post-time journal-line validation when Security Master
provenance exists but approved identity or accounting mapping proof is still missing.
Operations Continuity workflow DTOs also carry an optional accounting-record summary with eight
contract-owned evidence categories: retained source data, normalized transactions and positions,
reconciliation case history, journal and ledger evidence, approval history, report-pack lineage,
export evidence, and restatement lineage. Keep these categories shared so browser and WPF
accounting-record review surfaces do not derive audit readiness or evidence grouping independently.
The same workflow payload now carries canonical Financial Operations reconciliation lane summaries
for cash, position, trade, income, MBS factor, bank, and GL support coverage. Lane status, break
counts, required actions, route hints, and retained evidence links stay in the shared contract so
operator clients do not infer reconciliation completeness from local table state.
It also carries `OperationsDashboardSummaryDto`, a source-backed operational dashboard rollup for
Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close
Support. The dashboard metrics expose status, retained evidence, route hints, and required actions
so browser and WPF clients can present the Financial Operations core flow without recomputing it
from lower-level workflow fragments.
`OperationsEvidencePackageSummaryDto` publishes the same workflow's package posture for accounting
record evidence, report-pack evidence, close-package manifests, and audit-support packages. Package
rows carry status, category completeness, retained evidence counts, route hints, and required
actions so operator clients do not stitch package readiness together from report-pack,
close-package, accounting-record, and timeline fragments.
Operations reconciliation break cases also expose contract-owned SLA state/due time, materiality,
root-cause code, approval posture, and blocked downstream outputs beside owner, due date, variance,
supporting evidence, escalation, and correlation keys, so browser and WPF clients do not rebuild
v0.18 exception operations locally.
`PrivateCapitalCloseCockpitDto` publishes the v0.18 close cockpit as a shared read contract over
fund/book/period/entity scope, close workflow rows, close-readiness blockers, next actions, and
lane posture for data receipt, reconciliation, journals, capital accounts,
partner capital account tie-outs, expense/fee/allocation review, management-company operating
records, NAV support, valuation evidence, reporting, delivery, close-package, and period-lock
evidence. The management-company lane keeps expense allocation, management-fee, intercompany,
bank/card, budget or cash-plan, and reimbursement evidence in the shared lane contract so clients
render review-required states from source posture instead of local ERP-style calculations. It also
carries approval history rows for workflow approval decisions and checklist-control approvals plus
NAV support package rows for positions, cash, pricing, shadow NAV, administrator NAV, the
administrator-versus-Meridian tie-out, and retained evidence links, so clients can inspect
approve-result and NAV-support evidence without rebuilding it from timeline, close-package, or
report-output fragments.
Financial Record Explorer DTOs under `Workstation/FinancialRecordExplorerDtos.cs` define the shared
ledger, portfolio, Security & Instrument, and report-line provenance explorer contract consumed by
both browser and WPF.
Private-capital command-center DTOs in `Ledger/AccountingConfigurationDtos.cs` compose a single
fund event into evidence, workflow, ledger-impact, capital-account-impact, treasury expectation,
reconciliation, report-usage, delivery-record, tax-support, and audit-history lanes so clients can
navigate the v0.18 fund-event spine without deriving lane readiness locally.
The DTO owns scope, saved views, summary, filters, columns, rows, selected-record detail, proof
actions, record graph, `Used In`, and `Impacts` relationships; clients must render empty or blocked
states from the contract instead of fabricating totals when source-backed projections are missing.
Report-pack delivery packages also expose a contract-owned delivery evidence packet that binds the
stakeholder recipient, entitlement scope, approval chain, publication manifest, report-line
provenance, selected branding theme, delivery artifacts, request history, and restatement lineage
into one reconstructable proof object.
Workstation reporting run payloads carry the source as-of date beside run id, template, status,
trigger, attempts, section counts, linked-lineage counts, artifacts, and audit actions so browser
and WPF reporting surfaces can render version/audit metadata without inferring it from filenames or
delivery-package side channels.
The shared audit-pack readiness model exposes completeness, missing category keys, warnings,
evidence-category summaries, measured generation seconds, a 60-second SLA target, and SLA pass/fail
posture. Older report-pack manifests may omit readiness; clients must treat that as unknown or
incomplete rather than invalid. Each category also carries contract-owned required evidence labels
so browser and WPF clients can display the source, normalized activity, reconciliation, ledger,
approval, document, export, and restatement requirements without parsing status prose.
Evidence workflow linkage and vault lookup DTOs include `AccountingRecordId`,
`ReportPackDeliveryAttemptId`, and `ReportPackDeliveryPackageId` so retained accounting-record
and report-pack delivery manifests can be indexed and queried as first-class audit records, not only
by a generic evidence subject string. Evidence Vault identities also publish frozen request-list
groups for event, close, audit, tax, and report-package support beside the individual support
requests so browser and WPF clients do not infer package checklists from request ids.
`FundOperationsNavigationContext` also carries optional evidence subject metadata for shared
evidence routes such as `EvidenceWorkbench:accounting-record/{recordId}`, allowing browser and WPF
clients to preserve the subject and source target while resolving to their local audit surfaces.
`WorkstationAccountingPayload` can also carry the optional `ManualJournalWorkbench` projection so
Accounting and Reporting workstation surfaces can review fund events, ledger impact, capital-account
subledger impact, approval state, retained evidence, and report output from one contract-owned
private-capital activity model.
That private-capital contract now includes `PrivateCapitalFundEventLedgerRecordDto`,
`PrivateCapitalCapitalAccountSubledgerDto`, `PrivateCapitalReportOutputDto`, and
`PrivateCapitalEvidenceCategoryDto` so clients receive unified event, ledger-impact,
capital-account impact, evidence, approval, report-output, readiness reason, next-action, and
route metadata from the shared DTOs. `CapitalAccountWorkbenchDto` and
`ICapitalAccountWorkbenchService` extend that same contract-owned model into an investor-level
capital-account evidence surface with governed allocation policy trace fields, effective windows,
approval and replay inputs, statement publication, restatement changed-line lineage,
audit-support drill-through rows, and explicit live-versus-planned capability lists for browser
and WPF consumers. Payment-intent DTOs cover expected cash movement, requester,
approval chain, bank/cash evidence, reconciliation linkage, audit history, and explicit
execution-deferred posture for the v0.18 cash evidence layer. Keep broader cap-table
administration, broad LP portal, native live-payment execution, full forecasting, and Backtesting
Studio out of these contracts until the W1-W5 operational-record slice explicitly reopens those
lanes.

Cluster coordination contracts define the shared lease and ownership vocabulary for distributed
runtime work under `Meridian.Contracts.Coordination`. Keep `IClusterCoordinator`, `ILeaseManager`, `ICoordinationStore`,
`IScheduledWorkOwnershipService`, `ISubscriptionOwnershipService`, `LeaseRecord`,
`LeaseAcquireResult`, and `CoordinationSnapshot` contract-owned so Application orchestration,
Platform runtime coordination, Storage lease persistence, diagnostics, WPF, and browser endpoints
use the same leadership and ownership semantics without depending on Application implementation
types.

Historical backfill contracts include the shared run outcome and per-symbol validation/completeness
signals published under `Meridian.Contracts.Backfill` and consumed by Application orchestration,
Storage status persistence, endpoints, tests, and
operator surfaces. Keep these payloads transport-safe and additive so backfill status can move
between services without reintroducing Application-layer dependencies into durable storage.

Plaid contracts define the shared provider lane for bank account linking, transaction sync,
balance evidence, investment snapshots, identity verification, webhook retention, and sandbox
transfer gating. Keep Plaid DTOs contract-owned so Data provider onboarding, Treasury cash
movement, fund-account reconciliation, browser, WPF, and endpoint services consume the same
auditable account evidence rather than vendor-shaped payloads.
Accounting-system contracts define the shared GL provider lane for external chart-of-accounts,
journal-entry, trial-balance, import-summary, and reconciliation-preview evidence. Keep these DTOs
provider-neutral so QuickBooks-like adapters, shared endpoints, browser Accounting, Settings, and
future WPF surfaces consume the same read-only import and reconciliation vocabulary before any
posting/export workflow is enabled.
Provider connection and readiness DTOs also carry optional non-sensitive credential field and
environment metadata so browser and WPF provider setup surfaces can render from shared catalog
truth without receiving stored secrets or environment variable contents.

Core extensibility contracts define Meridian's stable financial operations object vocabulary,
configurable tenant layers, governed foundations, configuration envelopes, tenant templates,
activation readiness/results, and catalog registrations. Keep tenant-specific workflows, rules,
mappings, reports, permissions,
custom fields, source priority, ledger controls, notifications, and domain-extension descriptors
attached to these shared DTOs instead of creating browser-only, WPF-only, or provider-local
extension vocabularies. `UiApiRoutes.WorkstationExtensibilityCatalog` publishes the shared
workstation read endpoint for that registry, and the tenant-template route constants publish the
shared save, activation-readiness, activation, and activation-history endpoints under
`/api/workstation/extensibility/tenant-templates`. Activation readiness preserves the approval
evidence model by blocking unapproved configuration envelopes and envelopes that lack retained
approval actor/timestamp metadata.

Report-pack workflow contracts carry the W4 governed lifecycle states `Draft`, `InReview`,
`Approved`, and `Published` plus governed publication metadata: sign-off actor, evidence hash,
retained manifest path, retained evidence links, report-line provenance, create requests, publish
requests, explicit `Rejected` state support, explicit review-state rejection requests with reason,
actor/role, and optional evidence-link metadata, and restatement requests with approver,
prior-version, changed-line, and evidence-link metadata. The same shared contracts also carry
report-pack delivery attempts, delivery failure attempts, delivery history, operator-managed
reporting schedules, schedule delivery targets, due-schedule run results with delivery
attempts/warnings, ad-hoc report-run requests/results, and HTML/PDF rendered-statement artifact
formats so browser, WPF, endpoints, and host bootstrap payloads consume the same reporting command
and history shape.
Delivered report-pack attempts can also carry `ReportPackDeliveryPackageDto`, including the
delivery mode, secure link or portal route, retained manifest path, requested PDF/XLSX/CSV
formats, retained artifact metadata, artifact SHA-256 checksums, artifact version stamps, the
publication evidence hash used for package integrity summaries, optional publication manifest
metadata, publication evidence links, retained line provenance, and publication-approved branding
metadata. Generated reporting-run packages also retain run id, template id, schedule id,
as-of date, trigger, status, attempt count, section count, lineage count, and source-artifact
metadata so PDF/XLSX/CSV artifact bytes, checksums, and byte sizes can be reconstructed
consistently after package-store reloads. Delivery packages also carry contract-owned access,
channel, and download summaries plus `ReportPackDeliveryAccessLinkDto` rows for the primary
email-link/secure-portal/evidence-vault/internal route, operator route, retained manifest, and
token-gated artifact downloads, so clients can explain package access and retained outputs without
parsing token URLs locally. Token-gated email-link and secure-portal packages also carry an
access-expiry timestamp so clients can display and enforce package availability from the shared
contract. Keep those package fields shared so email-link, secure portal, evidence-vault, and
internal-route distribution clients do not infer report package output or integrity from
delivery-reference strings. The shared route catalog
includes both the token-gated package manifest URL under
`/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/package` and the
secure-portal package URL under `/portal/reporting/packages/{packageId}`. Package artifacts can
also carry token-gated `DownloadRoute` values under
`/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/artifacts/{artifactName}`
so clients download retained PDF/XLSX/CSV outputs from the shared package contract instead of
building artifact URLs locally; downloaded artifacts include package identity, publication manifest
fields, publication evidence links, and report-line provenance for downstream audit review.
Delivery attempts are also addressable as shared Evidence Workflow Fabric subjects by combining
`reportId:attemptId` under `report-pack-delivery`, allowing clients to open the canonical delivery
evidence graph without inventing browser- or WPF-local delivery identity rules.
`ReportingScheduleDeliveryPlanDto` also carries contract-owned `ReadinessBlockers` beside
`IsReady` and `ReadinessSummary`, so clients can fail closed when a scheduled recipient is inactive,
overdue without a retained package, configured with an incompatible email-link/portal/vault/internal
mode, or missing one of the requested artifact formats. Schedule plans also project the latest
delivery package access-link rows so operators can open the exact retained package, portal, manifest,
or artifact downloads produced by the last schedule run without inspecting delivery package internals.
Workstation Reporting run rows carry typed drilldown links and next-action references so browser
and WPF clients can distinguish open evidence routes from reference-only approval, publication, and
restatement actions without parsing artifact strings. Generic Reporting run rows also carry
`generatedReportWriterGrids` metadata for each retained `report-writer://.../grids/{gridId}`
artifact, including grid title, kind, dimension count, metric count, and formula count, so clients
can render generated no-code grid evidence without inferring it from artifact URIs. `UiApiRoutes`
also owns the retained grid artifact route at
`/api/fund-structure/reporting/runs/{runId}/report-writer-grids/{gridId}` so browser and desktop
clients build JSON/CSV/XLSX links from the same route contract.
`ReportPackDeliveryPackageDto` preserves the same generated grid rows for reporting-run delivery
packages plus optional `renderedReportWriterGrids` rows, columns, warnings, and lineage so
token-gated PDF/XLSX/CSV artifact downloads can be rebuilt byte-stably from the persisted package
manifest. `ReportingRunRequestDto` and `ReportingScheduleRecordDto` can also carry optional
dataset rows for approved report-writer grids; clients should only provide rows from a governed
dataset snapshot and should treat missing rows as an explicit no-data render, not as permission to
fabricate portfolio values. Shared services may resolve omitted ad-hoc or scheduled run rows from
retained portfolio cuts, Top-N/contribution analytics, and cross-fund consolidation rows before
calling the generic Reporting orchestration contract.
Workstation and fund-operations reporting summaries expose `reportPackDistributions` as
recipient-level distribution records instead of static `reportPackTargets` strings. Clients should
render recipient, role, channel, owner, state, due time, pending item count, and pending summary so
operators can see who receives each package and what is still waiting on approval, publication, or
delivery.
Fund-operations reporting summaries also expose `PortfolioReportingCutDto` rows for fund,
strategy, and user-tag views. These rows carry exposure, cash, P&L, shadow-NAV, variance,
source-count, evidence-route, and version-stamp fields so Reporting clients can show portfolio
cuts without recalculating portfolio or NAV state locally.
`PortfolioReportingLiveViewDto` extends those cuts with shared live-summary routes, source-backed
freshness state, liquidity summaries, optional run cash-ladder routes, and telemetry copy. It also
carries explicit market-tick telemetry: tick timestamp, safe numeric tick sequence, tick age,
provider label, tick freshness summary, and an `IsMarketTickLinked` flag. Clients must render the
`LiveLinked`, `SourceBacked`, `Stale`, or `Blocked` state from the contract instead of implying live
market ticks when the shared source evidence is missing or stale. `LiveLinked` is reserved for
source snapshots inside the server-owned live freshness window; older retained snapshots remain
`SourceBacked` until they cross the stale threshold. Blocked and stale live views also carry
`ReadinessBlockers`, allowing browser and WPF clients to fail closed with the same operator-facing
source-evidence gap.
`PortfolioReportingPnlSliceDto` carries daily, weekly, monthly, and yearly P&L rows derived from
retained portfolio run timestamps. Rows include current/prior period totals, realized/unrealized
P&L, change, source counts, readiness text, route, tags, and version stamp so clients can show
period P&L without inventing browser-local time-series logic.
`PortfolioReportingAnalyticsRowDto` carries source-backed Top-N winner, Top-N laggard, and
contribution rows for Reporting. Rows include security/strategy/asset-class scope, rank,
realized/unrealized/total P&L, contribution percent, heat-map intensity, source counts, route,
readiness text, tags, and version stamp so clients can render P&L contribution analytics without
recomputing retained portfolio positions.
`CrossFundReportingConsolidationDto` carries company, fund, and legal-entity rollups for
cross-fund reporting. Rows include fund/entity/account/run counts, gross and net exposure, cash,
P&L, shadow-NAV, variance, source counts, readiness text, route, tags, and version stamp so
Reporting clients can display consolidated portfolio metrics without deriving multi-fund state.
The same summary carries `StructuredReportingExportDto` descriptors for regulatory trial balance,
warehouse ledger facts, investment portfolio-cut outputs, Top-N/contribution analytics outputs,
and cross-fund consolidation outputs.
Each descriptor includes purpose, format, dataset, consumer, schema version, row/field/source
counts, retained path, API route, readiness summary, evidence route, and version stamp;
`StructuredReportingExportPayloadDto` returns stable column metadata, data-dictionary fields,
validation checks, and string-valued JSON rows for downstream export consumers. The same endpoint
accepts `format=json`, `format=csv`, or `format=xlsx` for schema-ordered file output; XLSX exports
also carry Metadata, DataDictionary, and Validation worksheets, including data-warehouse
descriptors whose default retained format is JSON.
Reporting summaries and generated report-pack snapshots also carry shared `ReportBrandingThemeDto`
metadata for firm name, colors, logo URI, footer, disclaimer, and built-in/custom posture. Summary
payloads retain fund-profile context so clients can generate branded governed report packs without
hard-coded fund ids.
`ReportingScheduleDeliveryPlanDto` carries schedule-to-recipient delivery plans with PDF/XLS/CSV
format sets, email-link or secure-portal mode, readiness text, due/as-of posture, last retained
delivery attempt metadata, latest package artifact counts, checksum integrity summaries, and
version stamps.
`ReportingRunAuditTrailDto` exposes retained generic Reporting run lineage with run/template/as-of
metadata plus timestamped actor/action/notes rows, letting browser, WPF, and export consumers inspect
the same audit trail persisted by the orchestration store instead of relying on collapsed action
labels.
`FundReportPackGenerateRequestDto` accepts either a `BrandingThemeId` or validated
`BrandingThemeOverride`, and `ReportPackPublishRequestDto` can retain that selected
`ReportBrandingThemeDto` on the publication manifest for downstream packages. Browser, WPF,
distribution, and retained-artifact readers therefore consume the same branding contract instead of
inventing presentation metadata locally.
Report template contracts now also carry the governed authoring lifecycle for built-in and custom
template versions: draft requests, review submission, approval/rejection decisions, immutable
built-in markers, latest-approved posture, validation issues, approval references, and audit events.
Template definitions can include report-writer grid definitions for detail, pivot, Top-N,
contribution, and formula-backed tables, and render responses carry structured grid rows, columns,
warnings, lineage metadata, optional data-dictionary fields, and validation checks alongside the
compatibility rendered-content string. The lineage payload includes input/output row counts,
filtered-input row counts, source fields, metric source mappings, formula dependencies, and saved-filter lineage so report previews and exports can retain
an auditable dataset trace. Render requests can also carry a temporary `Grids` override, allowing
no-code clients to preview an unsaved drag/drop layout through the shared renderer without mutating
the approved template record. Keep those template lifecycle and grid fields shared so Reporting,
browser, WPF, and endpoint tests use the same version-approval and no-code report-writer vocabulary
instead of maintaining client-local template state. Workstation
template metadata also carries report-writer row fields, column fields, metrics, formula
expressions, Top-N, sort settings, saved filters, and source-field catalog entries with field role,
data type, dataset, label, and description metadata. Operator clients can render no-code writer
canvases and drag/drop palettes without parsing template JSON, deriving grid composition from
count-only summaries, or guessing which source-backed portfolio, analytics, consolidation, and
generated contribution fields are available.
Workstation template metadata now projects the same audit/version-control fields used by the
governance record, including based-on template id, created/updated/submitted/approved/rejected
actors and timestamps, decision rationale, approval reference, validation issues, and retained
template audit events. Operator clients should render those fields directly instead of inferring
version lineage from display strings.
Template
definitions and report-pack workflow records also carry `ReportAccessPolicyDto` with private,
restricted user/group/company, and company-wide modes; workstation template payloads expose access
mode, summary, and accessibility posture so clients do not invent report audience rules locally.
`WorkstationReportAccessAuditSummaryDto` adds aggregate user/group/company access evidence to the
Reporting payloads, including visible and hidden template, report-pack, schedule, delivery, and
structured-export counts plus generic denial reasons, so browser and WPF clients can explain
locked report scope without leaking private report names.
Pilot readiness contracts also carry W4 acceptance evidence categories and roles so acceptance proof
can be distinguished from evidence-vault manifest/export support in serialized artifacts.
Report-line provenance carries the reported value plus run, source-session, ledger-entry,
provider-event, Security Master definition, reconciliation-case, reconciliation-run,
reconciliation-outcome, and approval pointers so each retained line can be traced back to the source
workflow evidence before publication. It also carries the Financial Record Explorer id and href used
by browser and WPF clients to open the shared ledger, portfolio, or Security & Instrument Explorer
for that retained line. Generated report-pack lineage pointers also carry optional display labels, source-system
tags, related ledger or journal evidence IDs, line amounts, latest evidence timestamps, and API
routes back to run continuity, ledger trial-balance, reconciliation, and Security Master search
evidence. Keep these fields shared so browser, WPF, and service tests enforce the same publication,
drilldown, and no-orphan-evidence rules.

Fund-structure contracts include the shared entity setup draft, validation summary, graph preview, and create-result payloads used by WPF, browser, and `/api/fund-structure` to create organization, business-lane, client/fund, legal-entity, vehicle, investment-portfolio, ownership, and account-handoff records without UI-local command vocabulary. The shared `IFundStructureService` contract lives in `Services/` so browser, WPF, Identity scoped-access lineage, endpoint, and composition consumers can depend on the fund-structure orchestration contract without depending on Application implementation types. The shared `IFundAccountTraversalQueryService` contract also lives in `Services/` so fund-account endpoints can use the same authoritative Fund -> Owns -> Account traversal contract while Identity owns the current cached implementation. `IGovernanceSharedDataAccessService` is contract-owned for the same reason: governance structure views should consume a shared Security Master, price, and backfill accessibility summary shape while Application keeps the current implementation. Fund-structure contracts include the ledger mapping workbench payload used by Accounting
surfaces to show account-to-ledger-group assignment source, unresolved mapping issues,
and recommended operator action without requiring clients to duplicate mapping precedence rules.
Investment Accounting Transaction Lab contracts carry shared preview payloads for trades,
dividends, fees, accruals, corporate actions, and broker-reconciliation examples. These payloads
reuse the balanced expected-journal preview contract and add trial-balance impact,
reconciliation expectation, source run/session, statement/case, and evidence ids so browser, WPF,
and endpoint services can reason about Books Before Broker accounting impact without client-local
accounting rules. Requests can now opt into `BooksBeforeBroker` preview mode, which returns
server-owned broker-staging readiness, required approvals, blockers, evidence ids, and expected
broker action before any paper/live movement is routed.

Identity auth contracts moved to `src/Meridian.Identity/Contracts/Auth` and are published under
`Meridian.Identity.Auth`. Role/profile, permission, scoped-access request/result, and audit-event
payloads should stay Identity-owned so browser, desktop, endpoint, and F# policy consumers share
the same authority-configuration vocabulary without keeping auth DTOs in the generic contracts
project.

Operations approval policy contracts include a shared approval policy matrix for close governance,
governed approval-policy rule upsert requests, result envelopes, and audit-event metadata.
Close-calendar contracts summarize workflow due posture from server-derived close checklists and
include governed item upsert requests, result envelopes, and audit events for owner/due-date
configuration.
These payloads describe server-owned approval actions, reviewer independence, required permissions,
report-pack requirements, checklist-control approvals, audit event types, rule-change rationale,
next due task, readiness, readiness components, blocker codes, next actions, due-date ownership,
and route contracts so Settings, browser, and WPF clients can render and configure the same policy
without duplicating enforcement rules.

Evidence workflow contracts now carry policy-owned SLA/freshness assessments and the Meridian
Assurance Score on packet completeness. Keep provider validation, replay checks, reconciliation,
approval, and report freshness policy output in shared DTOs so browser and WPF clients render the
same cross-workflow readiness signal without local scoring rules.
Evidence packets and graph responses also carry the v0.18 proof-chain coverage model for Source,
Normalization, Reconciliation, Ledger, Capital accounts, Close, Reporting, Delivery, and Audit
layers. Keep this contract-owned so client workbenches display Operational Evidence Graph coverage
without reclassifying packet nodes locally.
The shared `ISecurityValidationGateService` contract lives in `Services/` and returns
Security Master validation DTOs from `SecurityMaster/`; Backtesting, Execution, Strategies, browser,
and WPF consumers should depend on this contract rather than the Application-layer implementation.
Environment Design service contracts also live in `Services/` and return DTOs from
`EnvironmentDesign/`; browser, WPF, and host composition should depend on those contracts while the
Workflow module provides the current local-first `EnvironmentDesignerService` implementation.
ETL contracts include `EtlJobDefinition`, run/export result payloads, `IEtlJobDefinitionStore`,
and `ISftpFilePublisher`. Keep the SFTP publisher port contract-owned so the Data
Integration-owned export service can target SFTP without depending on Infrastructure, while the
Infrastructure adapter implements transport-specific publishing details.

Event-pipeline metrics and monitoring delivery contracts live in `Monitoring/` under
`Meridian.Contracts.Monitoring`. Keep `IEventMetrics`, `MetricsSnapshot`, and
`IMonitoringWebhookSink` contract-owned so Application
notification services, Platform tracing/monitoring, diagnostics endpoints, WPF shell, and shared
browser endpoints can depend on the same metric and alert-delivery shapes without introducing
Application-layer dependencies. Runtime pipeline statistics live in `Pipeline/` under
`Meridian.Contracts.Pipeline`; keep
`PipelineStatistics` contract-owned so Platform backpressure alerting can observe Application
pipeline state without referencing Application implementation assemblies.
Operational scheduler contracts live in `Services/`. Keep `IOperationalScheduler`,
`ITradingCalendarProvider`, operation types, resource requirements, scheduling decisions, slots,
trading sessions, and maintenance-window records contract-owned so scheduling behavior can be
implemented in Platform while tests, future hosts, and operator surfaces consume the same
scheduler shape.
Evidence Vault identities also expose retained artifact metadata, grouped request lists, and
support request rows for file-backed evidence bundles: storage kind, artifact id, kind, relative
vault path, content hash, retained size, source route, canonical subject linkage, request-list
target, highest severity, blocked outputs, missing/stale evidence requests, blocking work-item
references, and validation support issues. Keep that metadata shared so packet, report, approval,
screenshot, statement, audit, tax, close, and validation producers can enforce the same
retained-artifact and request-list vocabulary.

Audit Trail Explorer contracts live under `Workstation/AuditTrailExplorerDtos.cs` and normalize
retained audit records into cross-object timeline rows with object kind, object id, actor,
correlation, related-object ids, metadata, and evidence routes. Keep these query and result
payloads contract-owned, including object-kind/object-id and related-object filters, so browser and
WPF clients search the same audit vocabulary instead of inventing client-local timelines. Manual
override rows now use `OperatorAction` object kind keyed by override id, and circuit breaker rows
use `ExecutionControl` object kind with control-route evidence links for operations review.
`UiApiRoutes` also owns the canonical workstation root route constants for Strategy, Data,
Accounting, Reporting, Trading, and Portfolio while retaining Research, Data Operations, and
Governance constants as compatibility aliases. Shared endpoints and clients should consume the
canonical constants for new route references so visible workspace routing stays aligned across
browser and WPF surfaces. Contract XML documentation should describe visible workspaces as
Strategy, Data, and Accounting. Legacy Research, Data Operations, and Governance identifiers are
route or serialized payload compatibility concepts only, not active workstation contract type names.

Instrument Passport contracts are attached to the shared Security Master workstation trust
workbench payloads. `InstrumentPassportDto` carries identifier and provider mappings, lifecycle
events, corporate actions, pricing/trading-parameter readiness, downstream usage, and trust posture
so browser and WPF clients do not rebuild governed passport semantics locally. Provider-confidence
rows expose mapping source, freshness, confidence score, identifier-conflict links, and override
history for each provider symbol mapping.

Security Master custom asset profile contracts live under `SecurityMaster/` and define versioned
profile definitions, typed field schemas, identifier preferences, lifecycle states, accounting-impact
hints, pinned profile-backed terms, and approval metadata. Keep these contracts shared so future
Data, Settings, browser, WPF, and endpoint flows validate the same no-code custom asset model
instead of accepting client-local JSON shapes.
`SecurityAssetClassCatalog` also exposes `CustomAsset` so create workflows and projection consumers
can distinguish profile-backed alternative assets from generic `OtherSecurity` fallback records.
`SecuritySearchRequest` carries optional `customProfileId`, `profileVersion`, `profileFieldKey`,
and `profileFieldValue` filters so shared clients can find profile-backed alternative assets
through the canonical Security Master search route without depending on endpoint-local JSON.
Profile governance request/result DTOs carry draft, approval, rollback, lineage, audit event,
rationale, actor, approval-reference, and correlation metadata so Data, Settings, browser, WPF, and
endpoint tests share the same governed profile lifecycle contract.

Strategy run comparison and diff contracts live in `Workstation/StrategyRunReadModels.cs`.
Run diff payloads include base/target mode and engine plus final-equity, drawdown, Sharpe,
return, fill-count, net-P&L deltas, strategy id/version metadata, lineage relation,
compatibility level, artifact completeness, and warnings so browser, WPF, and service tests can
compare strategy versions and engines without endpoint-local DTOs.
Strategy promotion readiness contracts also carry retained approval checklist and evidence
references so paper-to-live promotion gates remain contract-owned and browser/WPF clients can show
the same human-approved evidence requirements without local promotion-state rules.
Strategy briefing contracts live in `Workstation/StrategyBriefingDtos.cs` and provide the
canonical Strategy-named payloads for run drill-ins, saved comparisons, alerts, "what changed"
items, workspace summary, and the full briefing DTO. Older `ResearchBriefing*` contracts are
retained as compatibility payloads while WPF and new consumers move to the Strategy names.
The workstation bootstrap contract in `Workstation/WorkstationBootstrapDtos.cs` now exposes
`WorkstationStrategyPayload` as the canonical payload for `/api/workstation/strategy`;
`/api/workstation/research` remains a compatibility alias for existing clients.
Data upload template, preview, and bank-statement import result contracts live under
`Workstation/DataUploadDtos.cs`. The Data bootstrap payload includes the upload-template catalog so
browser and WPF clients can render the same trade, transaction, bank-statement, asset-information,
and entity-configuration intake templates while upload preview results remain retained source
evidence, validation issues, and bounded sample rows only. Bank-statement import responses identify
the retained source path, imported batch, target bank account, statement date, and line count after
the shared endpoint applies evidence through the fund-account service.
The same workstation contract file owns the multi-asset operational coverage DTOs returned by
`/api/workstation/portfolio/multi-asset-coverage`: `MultiAssetCoverageSummaryDto`,
`MultiAssetClassCoverageDto`, `MultiAssetEvidenceRequirementDto`, and
`MultiAssetReadinessBlockerDto`. Each asset-class row also carries
`MultiAssetDrillThroughTargetDto` entries for Security Master passport/profile, provider evidence,
reconciliation casework, ledger mapping/evidence, Asset Operations detail, and close readiness.
Private-credit `DirectLoan` rows can also carry loan-schedule, commitment/covenant, and paydown/obligation targets, while
structured/private `CustomAsset` rows can carry profile-lineage, servicer/trustee, valuation/NAV,
and obligation close-readiness targets. Browser, WPF, and shared endpoint clients should render
those rows as supplied instead of recalculating asset-class readiness, ledger coverage,
reconciliation status, or close blockers locally.

Brokerage sync activity payloads are fund-account scoped under `Workstation/BrokerageSyncDtos.cs`.
Keep readiness and work-item decisions on `WorkstationBrokerageSyncStatusDto` and reserve
`FundAccountBrokerageSyncActivityDto` for durable account-level evidence, positions, orders, fills,
and cash-transaction details. Fill rows can carry explicit provider-reported realized P&L for
shadow-book review, but that value stays optional because many providers do not expose it in
activity feeds. Activity rows can also retain explicit provider corporate-action and factor events
for split, dividend, amortization, paydown, and factor-schedule evidence instead of forcing
reconciliation to infer every candidate from positions or cash activity. Provider-ledger
reconciliation payloads in the same file are also fund-account
scoped and compare the latest provider projection with Meridian's internal
account-balance snapshot plus Security Master coverage posture. Account-balance snapshots carry
optional realized and unrealized P&L fields so shadow-book comparisons can use retained internal
book values when those measures are available. The retained shadow-book comparison lines also carry
custodian-statement versus provider-position quantity and market-value rows when statement lines are
available for the snapshot date, plus bank-statement cash closing balance and income cash-flow rows
against retained ledger/provider activity. Non-primary shadow-book variances are promoted into the
same break payload stream so custodian, bank statement, income/accrual, realized P&L, unrealized
P&L, and pending-settlement availability issues can age, be assigned, require sign-off, and seed
casework like primary provider-ledger breaks. Reconciliation break payloads carry stable break keys, owner
assignment, tolerance, first/last-observed aging, sign-off state, and a structured
`ReconciliationBreakExplanationDto` with source systems, probable cause, ledger impact, suggested
next action, and evidence links so controller workflows can treat provider-ledger variances as
accounting-grade case records without rebuilding "Explain the Break" copy locally. Security Master
classification gaps and stale resolved provider mappings from provider-ledger reconciliation route
through steward-owned casework metadata instead of generic fund-accounting sign-off. The detail contract
also carries provider capability checks for account support, held asset-class position support,
historical quote/valuation-mark support, corporate actions, and factor schedules before controller
close readiness treats the provider evidence as clean. It also carries Security Master confidence passports for provider positions, including resolution
source, provider freshness, confidence score, validation issue codes, identifier-conflict evidence,
and retained Security Master override audit history when an operator override exists for the
resolved instrument. Stale provider evidence is represented directly on the passport with a capped
confidence score and `PROVIDER_EVIDENCE_STALE` validation issue code, not only in the account-level
provider freshness component. When break-queue storage is registered, stale resolved passports can
also be retained as Security Master steward cases with provider sync cursor and sign-off metadata.
Ledger amount provenance payloads expose structured strategy/run links in addition to generic
evidence rows so a report line drilldown can show the originating run id, label, route, source, and
whether the run pointer was captured at the selected line scope.
Corporate-action readiness now includes retained provider evidence candidate rows for equity
corporate actions, factor schedules, loan schedules, income cash activity, and principal/paydown
cash activity so downstream accounting and Security Master workflows can inspect the exact provider
event, required feed, attribution status, amount, quantity, and retained evidence source without
rebuilding the projection. The readiness contract also distinguishes generic provider corporate-action routing
from factor-schedule routing, so fixed-income and structured candidates can show whether
factor/coupon/principal feed support is actually routable. It also carries ledger-effect rows that
classify factor events as valuation inputs, loan-schedule events as valuation inputs with principal
context, attributed dividend/interest cash activity as cash/income journal-preview support, and
attributed principal/paydown cash activity as cash/principal journal-preview support. The readiness
contract now also exposes Security Master schedule feed rows that map each retained provider
candidate/effect to the target Security Master feed kind, required provider feed, factor/cash
amounts, attribution status, and whether the row can update Security Master history and support
ledger valuation. Degraded candidate rows can also be represented as durable
reconciliation casework with
Security Master steward sign-off metadata. Account close-readiness contracts consume the same
readiness evidence and require matched Security Master schedule feed rows that can update Security
Master history and support ledger valuation before fixed-income or structured positions can be
marked ready for close. Close readiness also treats retained
shadow-book comparison breaks as reconciliation review blockers and bridges retained Security Master
passports to open global Security Master casework for the same held securities, so pending
identifier-conflict or operator-override cases remain visible in the controller score.

Report-line provenance payloads live with the fund-operations workstation contracts.
Ledger period and cross-period reporting DTOs expose closed-period trial-balance rows and P&L
summary totals with accounting-basis, policy, prior-period variance, open-break count, and signoff
posture so clients can render period close and cross-period reports without recomputing ledger
semantics locally. `LedgerPeriodPnlSummaryDto` also separates realized revenue/expense net income
from accrual-basis adjustment impact and retains the accrual adjustment lines used for the split.
`LedgerTrialBalanceReportDto` wraps closed-period trial-balance detail rows with locked-period
status, aggregate totals, accounting-policy lineage, and a SHA256 report signature so browser, WPF,
export, and audit clients can verify the same period report payload.
`LedgerAmountProvenanceDetailDto` is the shared click-through contract for a retained report-pack
ledger amount: it carries the ledger amount, provider/source evidence pointers, Security Master
link, reconciliation run and case state, compact related-case owner/status/sign-off routing,
approval state, and report usage so browser and WPF clients do not reconstruct audit lineage from
report-pack internals. The Security Master link can now retain a durable `SecurityId`; the shared
service also carries the retained Security Master display label, source system, and evidence id from
the report lineage pointer, then uses retained security evidence ids to attach open Security Master
exception cases to the same report-line drilldown. Provider-event evidence can be direct report lineage or synthesized from
related provider-ledger casework that retains provider sync cursors and routes. Provider-event
evidence also carries optional provider event id/type, provider evidence source, required feed, and
Security Master id metadata when the source case came from provider-ledger corporate-action or
factor evidence. Provider-ledger corporate-action/factor casework also enriches the evidence row with ledger-effect kind,
principal/income amount, and journal-preview line count so report-line provenance can show valuation
or journal support without reconstructing provider-ledger detail payloads. Related reconciliation
case rows also retain materiality and aging context: severity, variance, tolerance band, reviewer and
resolver fields, sign-off count, latest sign-off actor/time/note, SLA policy/due-state, age band,
and business-age hours.

Statement reconciliation payloads live under `Workstation/StatementReconciliationDtos.cs` and keep
source-file evidence, mapping/tolerance profile versions, normalized positions, cash, transactions,
match summaries, breaks, operator cases, run create/reconcile commands, run validation envelopes,
and run-scoped break rows in the shared contract lane. Statement case payloads expose durable
casework metadata for owner/SLA disposition, aging, threaded comments, statement-row attachments,
Explain-the-Break summaries, and audit events so browser and WPF clients can inspect broker and
custodian statement exceptions without rebuilding domain aggregates locally. Keep these DTOs
additive and transport-safe so browser, WPF, retained evidence, and automation consumers can
reconcile custodian statements without referencing application, UI, or infrastructure types.

Direct lending command result codes distinguish validation failures, missing aggregates,
optimistic concurrency conflicts, and idempotency/command conflicts so persistence stores can return
operator-safe failure reasons without parsing exception text.

Accounting reconciliation casework contracts are shared here: break queue items carry assignee, priority, SLA policy/state/timestamps, age band, versioned taxonomy, threaded comments with mention/evidence/hash metadata, evidence counts, sign-off/reopen metadata, source-origin metadata, and optimistic concurrency versions. Casework command, bulk-triage, taxonomy, SLA policy, validation-problem, and sequenced audit-event payloads must remain additive so browser and WPF workstation clients use the same Accounting workflow.

Manual journal entry contracts include private-capital entry types for capital calls,
distributions, subscriptions, redemptions, LP transfers, and management fees. `TreasuryLedgerContextDto`
is the shared audit context for those drafts and carries effective date, idempotency key, fund
event, capital account, investor, payment intent, and settlement references so browser, WPF,
Financial Operations, and ledger metadata use the same retry-safe fund-event vocabulary.
`ManualJournalEntryWorkbenchDto` also carries an optional `PrivateCapitalActivityProjectionDto`
with server-owned fund-event rows, capital-account activity aggregates, ordered capital-account
subledger entries with running net activity, ledger-impact rows, report-output readiness candidates,
signed net activity, and projection validation issues so browser and WPF clients do not rebuild
private-capital ledger views from draft-local heuristics. Report-output rows also carry governed
report-pack identity, workflow state, publication manifest path/hash, signed-off publication
metadata, and report-line provenance counts when retained report-pack workflow records are linked
to the fund event. Posted report-output rows scope to the report-pack target capital account or
retained line provenance when either identifies one capital-account impact; unresolved multi-account
outputs remain `capital-account:unassigned` so clients do not display one investor's statement on
another investor's subledger. They also carry canonical report-output, fund-event record,
capital-account subledger, evidence-packet, and approval routes so report-package clients can drill
back into the same accounting record without reconstructing endpoint URLs. Report-output rows also
promote readiness label/reason and next action/route so clients can explain missing evidence,
pending approval, posting review, publication pending, missing governed packs, ready outputs, and
published outputs without parsing validation issue codes.
`PrivateCapitalFundEventLedgerRecordDto` groups each fund event with its
capital-account subledger movements, GL impacts, retained evidence links, approval state, and
report outputs so detail and drill-through clients can consume a single event-level accounting
record instead of stitching sibling arrays together. It also carries `PrivateCapitalEvidenceCategoryDto`
rows that classify source support, capital-account subledger support, ledger impact, approval state,
and report output readiness so clients can show operational evidence coverage without parsing child
arrays. The event-level record also promotes journal
entry id, gross and net capital activity, capital-account opening/ending net activity, memo,
payment and settlement references, canonical private-capital activity route, event evidence-packet
route, approval id/route when an approval exists, child-row counts, primary report-output
id/type/route, workflow state, publication manifest path, provenance counts, server-derived
readiness label/reason, and next-action route so operator tables and report-package
drill-throughs do not reopen nested arrays for basic record posture. The
projection also includes `PrivateCapitalCapitalAccountSubledgerDto`, which groups a capital
account's fund-event records, running subledger entries, GL impacts, retained evidence, approval
queue count, posted/published counts, report outputs, and validation issues into one
capital-account-level accounting record. It carries the same classified evidence categories at the
capital-account level so report and audit surfaces can compare source, ledger, approval, and report
coverage for one LP subledger without rebuilding coverage state. The account-level record also
promotes readiness, readiness reason, next action, and next-action route from the child fund-event
ledger records and evidence lanes, so clients do not infer subledger posture from counts. It normalizes omitted fund-event ledger records and
capital-account subledgers to empty collections
so clients can treat that event-level model as present even when there are no private-capital
events yet.
The projection is additive across draft and posted sources: posted ledger-backed fund events win
over same-id drafts, and it exposes posted fund-event and published report-output counts so clients
can distinguish retained ledger truth from unposted authoring state. Fund-event and subledger rows
also carry `IsPosted`, while report-output rows carry `IsPublished`, so drill-through clients do
not have to infer source state from approval labels. `IsPublished` remains a report-workflow fact;
`IsReportReady` is only true when the same fund event is posting-ready across approval, retained
evidence, balanced ledger impact, capital-account impact, and report-specific publication or
line-provenance evidence. `IsPublished` remains visible even when report-specific evidence is
missing so clients can show the retained output without treating it as ready. Fund-event and
capital-account report-output evidence categories require at least one report output, every linked
report output to be report-ready, and at least one retained report evidence link before marking the
evidence lane complete. Posted
private-capital rows inherit the owning ledger book's base currency across event, ledger-impact,
capital-account subledger, and report-output records instead of falling back to a client-local
currency default.
`IManualJournalEntryWorkbenchService.ListFundProfileIdsAsync` exposes retained manual-journal and
ledger-book fund scopes for evidence discovery without introducing investor portal or cap-table
surfaces.
`IManualJournalEntryWorkbenchService.GetPrivateCapitalActivityAsync`
and `UiApiRoutes.LedgerPrivateCapitalActivity` expose the same projection as a first-class review
endpoint for reporting, audit, and future LP support package consumers, without introducing an
investor portal or cap-table surface. The activity endpoint accepts optional `fundEventId`, `capitalAccountId`,
and `investorId` filters, retains posted fund events through matching child subledger, GL-impact,
or report-output rows, and recomputes account totals and net activity from the retained subledger
rows so multi-account posted events still drill into the selected capital account.
`UiApiRoutes.LedgerPrivateCapitalFundEventRecord` exposes one shared event-level record by
`fundEventId`, and
`UiApiRoutes.LedgerPrivateCapitalCapitalAccountSubledger` exposes one capital-account subledger by
`capitalAccountId`, `investorId`, and `currency` when those fields are needed to identify one
subledger, so review and report drill-through surfaces can load the needed accounting record
without interpreting an empty aggregate or accidentally selecting the wrong investor record.
`UiApiRoutes.LedgerPrivateCapitalReportOutput` exposes one private-capital report-output row by
`reportOutputId`, `reportPackId`, or `fundEventId` with optional capital-account and investor
filters, keeping report-to-ledger navigation on the same shared contract as activity, event-record,
and capital-account subledger review.
`UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench` exposes the narrow v0.18 Capital Account
Workbench slice by fund, ledger-book, fund-event, capital-account, investor, or currency scope. The
payload keeps investor-level account evidence, allocation rules, statement/restatement lineage, and
audit drill-throughs on shared DTOs while naming broader cap-table, LP portal, live-payment, and
forecasting behavior as planned rather than live.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-CONTRACTS -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-CONTRACTS -->
| TODO | Title | Status | Priority |
| --- | --- | --- | --- |
| `TODO-SRC-CONTRACTS-001` | Complete W5 accounting record evidence-chain contract coverage | done | high |
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Contracts/Meridian.Contracts.csproj /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~LeaseManagerTests|FullyQualifiedName~ClusterCoordinatorServiceTests|FullyQualifiedName~SplitBrainDetectorTests|FullyQualifiedName~SubscriptionOrchestratorCoordinationTests|FullyQualifiedName~IngestionJobServiceCoordinationTests|FullyQualifiedName~DiagnosticsEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Prefer additive DTO changes when possible. Update shared compatibility tests and generated docs when
contract shape, blocker vocabulary, or route-visible payloads change.

## Related docs

- `docs/status/contract-compatibility-matrix.md`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
