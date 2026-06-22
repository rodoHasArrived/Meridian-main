---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CONTRACTS
path: src/Meridian.Contracts
status: active
owner_lane: Contract Compatibility
last_reviewed: 2026-06-16
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
- `Integrations/` - provider integration template catalog entry, manifest, endpoint, mapping,
  validation, sync, OpenAPI import request/result, setup-save request/result,
  activation-readiness, activation request/result, manual CSV and REST dry-run request, raw
  payload, quarantine review/replay request/result, staging review, staging identity-resolution
  preview, promotion readiness, reconciliation handoff, sync-run summary/history, tenant-store factory
  seam, connection monitor, and run-due sync orchestration contracts for no-code read-only provider
  setup, monitoring, activation evidence, scheduled execution, and replayable ingestion. Quarantine
  review DTOs also carry pending, decisioned, replay-requested, ignored, and cash-position
  candidate counts so clients can render operator review posture without recounting decisions.
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
clients can inspect close-package publication without rebuilding package metadata locally.
Strategy-run trial-balance and journal DTOs expose the canonical `LedgerDimensionSetDto` beside
legacy account/entity/sleeve/vehicle scope fields so browser and WPF ledger drill-throughs can use
the same dimensional accounting vocabulary as rules, drafts, period reports, and external GL
mapping flows. Fund-level ledger trial-balance, journal, and reconciliation snapshot rows carry the
same dimension envelope so Accounting and Reporting exports can retain fund/entity/sleeve/account
scope instead of collapsing fund-ledger evidence to account-only rows. Ledger journal line DTOs carry optional Security Master identity, client-observed active status, approval
reference, provenance, and ledger-mapping evidence, and the shared blocker vocabulary includes the
posting gate failures used when an instrument-bearing posting lacks authoritative server-side
Security Master active-status proof or when journal/line provenance does not reference the resolved
Security Master id. The vocabulary also includes symbol
mismatch blockers for journal candidates whose instrument line symbol diverges from the
journal-level Security Master symbol and mapping mismatch blockers for generic ledger-mapping
references that do not name the resolved symbol or Security Master id. Operations Continuity
journal candidate lines also carry optional `LedgerDimensionSetDto` scope so reviewed close,
reconciliation, or accrual postings can preserve line-level fund/entity/cost-center/counterparty
and external-GL dimensions through the Financial Operations posting gate.
Reconciliation break queue items carry optional `LedgerBookId` scope so shared Accounting,
Reconciliation, and close-readiness surfaces can filter explicit book cases without inferring
accounting ownership from fund labels, routes, or exception text.
Accounting-system production-readiness contracts also expose retained migration run artifact list,
upsert payloads, and generated migration rollout plan rows so browser, WPF, and admin surfaces can
store ledger-book scope, historical journal backfill, dimensional backfill,
configuration-promotion, and close/reporting evidence migration proof under one shared route and
render the required migration lane actions instead of passing one-off request-only evidence.
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
`GovernanceLifecycleProjectionDto` also carries active Operations Continuity evidence packages,
reconciliation lane summaries, break cases, close-checklist tasks, and approval rows so Fund Ledger
and browser report-pack handoff can render package, lane, queue, and sign-off posture without a
UI-only DTO fork. The same projection carries reviewed-automation posture so browser and WPF
handoff surfaces can show allowed/prohibited automation guardrails and retained review evidence
without reading the workflow detail separately.
It also carries `OperationsDashboardSummaryDto`, a source-backed operational dashboard rollup for
Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce Evidence, and Close
Support. The dashboard metrics expose status, retained evidence, route hints, and required actions
so browser and WPF clients can present the Financial Operations core flow without recomputing it
from lower-level workflow fragments.
The workflow detail contract also carries `OperationsReviewedAutomationSummaryDto`, which lists
allowed suggestion/draft use cases, prohibited material actions, current review stage, required
human actions, retained evidence, and artifact rows for extraction, match suggestion, journal draft,
report commentary, audit request list, missing-support, and evidence-summary review outputs.
Guarded mutation request DTOs carry
`OperationsActionOriginDto` so Financial Operations can reject assistant or automation-origin
ledger posting, Security Master override approval, reconciliation break assignment, escalation, or
resolution, approval, close-package publication, and governed reopen commands before they mutate
the operating record. Report-pack-ready workflows can use the
same review-stage contract to surface report-commentary and audit-request-list drafts as
evidence-backed review artifacts rather than approved publications.
Payment approval/rejection, bank-evidence recording, manual-journal approval submission,
ledger period close, report-pack workflow approval/restatement, report-pack publication, and
report-pack delivery request contracts also carry the same action-origin metadata so assistant or
automation-origin calls cannot approve or reject payment requests, satisfy cash evidence, submit
journal drafts for approval, close accounting periods, approve report packs, restate published
report lines, publish reports, or create stakeholder delivery packages.
Banking contracts keep payment approval separate from bank-side evidence: approved payments remain
Meridian approval records, while `RecordPaymentBankEvidenceRequest` records retained confirmation,
return, reversal, or failure evidence under a human-operator origin before downstream cash,
reconciliation, or transfer surfaces can treat the payment as bank-supported. Bank transactions now carry the retaining operator so
confirmation and return evidence remain attributable in audit packages.
`OperationsEvidencePackageSummaryDto` publishes the same workflow's package posture for accounting
record evidence, report-pack evidence, close-package manifests, and audit-support packages. Package
rows carry status, category completeness, retained evidence counts, route hints, and required
actions so operator clients do not stitch package readiness together from report-pack,
close-package, accounting-record, and timeline fragments.
Operations reconciliation break cases also expose contract-owned SLA state/due time, materiality,
root-cause code, approval posture, and blocked downstream outputs beside owner, due date, variance,
supporting evidence, escalation, and correlation keys, so browser and WPF clients do not rebuild
v0.18 exception operations locally. Break-assignment/escalation and break-resolution requests carry
retained evidence and origin metadata so critical or material breaks cannot be cleared by
assistant/automation-origin commands or without resolution proof.
`PrivateCapitalCloseCockpitDto` publishes the v0.18 close cockpit as a shared read contract over
fund/book/period/entity scope, close workflow rows, close-readiness blockers, next actions, and
lane posture for data receipt, reconciliation, journals, capital accounts,
partner capital account tie-outs, expense/fee/allocation review, management-company operating
records, NAV support, valuation evidence, reporting, delivery, close-package, and period-lock
evidence. Journal posture is source-record strict: every retained fund-event record in scope must
be posted with ledger impact before the lane can report ready. The management-company lane keeps
expense allocation, management-fee, intercompany,
bank/card, budget or cash-plan, and reimbursement evidence in the shared lane contract so clients
render review-required states from source posture instead of local ERP-style calculations. It also
includes a close-control checklist lane for reversal approval, recurring journals, stale marks, and
period lock or governed reopen evidence. Reporting, delivery, and partner-capital statement
posture require approved report outputs before retained manifests can satisfy close readiness. It
also carries approval history rows for workflow
approval decisions, governed reopen approvals, and checklist-control approvals plus
NAV support package rows for positions, cash, pricing, shadow NAV, administrator NAV, the
administrator-versus-Meridian tie-out, and retained evidence links, so clients can inspect
approve-result and NAV-support evidence without rebuilding it from timeline, close-package, or
report-output fragments.
Financial Record Explorer DTOs under `Workstation/FinancialRecordExplorerDtos.cs` define the shared
ledger, portfolio, Security & Instrument, and report-line provenance explorer contract consumed by
both browser and WPF. The report-line provenance payload uses the same row, proof-action,
relationship, and graph DTOs to expose the retained instrument, position or transaction,
reconciliation, journal, report-line, evidence, and audit-link chain without adding browser- or
desktop-specific contract shapes.
Accounting configuration workspaces expose a computed `AccountingRulesStudioDto` beside the raw
posting-rule, version, approval, dry-run, and regression-test DTOs. Browser and WPF clients should
render rule counts, generated-posting coverage, effective-dated rule posture, saved-test coverage,
promotion queues, activation readiness, and server-owned required-action counters from this shared
studio read model instead of recomputing approval, regression-test, promotion, or validation state
locally.
Accounting-basis projection-set DTOs let shared clients request one retained source event across
multiple accounting bases, ledger books, periods, policies, and dimension scopes, returning the
governed posting candidate for each target without creating posted ledger facts. Approved generated
candidate posting uses a separate request/result contract so clients cannot confuse preview with
durable append.
The workspace can also carry a `LedgerBookSetupCandidateDto` when a selected ledger book is missing
but the server can derive a safe setup target from registered ledger-book scope. Clients should use
that candidate for ledger-book setup actions instead of guessing fund-structure node context.
Accounting-system contracts also publish `AccountingProductionReadinessDto`, a read-only control-plane
assessment for production rollout. The payload aggregates ledger-book rollout, Rules Studio,
posting-rule execution, journal lifecycle, dimensional accounting, external GL, close/reporting,
tenant-administration posture, explicit ledger-book-native workflow certification for posting
rules, journal lifecycle, close/reporting, external GL, reconciliation, direct-lending
projections, and strategy ledger reads with retained ledger-book-scoped
workflow evidence, and migration-rollout controls for
ledger-book migration, historical journal backfill, dimensional backfill, accounting configuration
promotion, and close/reporting evidence migration with shared blocker codes and retained
`AccountingMigrationRunArtifactDto` rows so browser, WPF, and admin setup surfaces can render the
same fail-closed readiness state instead of recomputing production gaps or treating certification
booleans as executable migration proof. Migration run artifacts can carry the canonical
`LedgerDimensionSetDto` plus tenant and company scope; dimensional backfill certification is
expected to retain fund, ledger-book, entity, sleeve, strategy, investor, capital-account,
instrument, tax-lot, cost-center, counterparty, and external-GL dimensions with the migration
evidence instead of relying on account or run names.
`AccountingLedgerBookWorkflowReadinessDto` counts posting rules, journal lifecycle, close/reporting,
external GL, reconciliation, direct-lending, and strategy-ledger-read controls as complete only when the certification flag is paired with retained
ledger-book-scoped evidence for that specific workflow lane or an explicit full workflow
certification packet. Ledger-book evidence must use an explicit `ledger-book:<id>`,
`ledger-book/<id>`, `book:<id>`, or `ledgerBookId=<id>` marker, with `ledgerBookId:<id>` and
`ledgerBookId/<id>` accepted for route-shaped evidence; incidental bare GUID references and generic
evidence links do not certify every workflow lane by implication.
`AccountingDimensionalReportingReadinessDto` separately certifies posted ledger-line dimension
persistence, trial-balance dimension filters, period reports, cross-period reports, journal
dimension filters, report-package provenance, and external-export dimension mappings with retained
ledger-book-scoped evidence before production readiness treats dimensional reporting as complete.
Each dimensional control requires evidence for that specific ledger/query/report/export lane,
unless the evidence is an explicit full dimensional or production certification packet, so one
generic ledger-book evidence link or incidental bare GUID reference cannot certify all dimensional
reporting controls by implication. Dimensional readiness also requires the retained evidence to
identify the explicit dimension scope with a `dimension-scope` or `ledger-dimension-set` marker,
keeping ledger-book proof separate from proof that fund, entity, sleeve, strategy, investor,
capital-account, instrument, tax-lot, cost-center, counterparty, and external-GL dimensions were
covered by the certified query/report/export path.
`AccountingTenantAdministrationReadinessDto` carries tenant, company, admin-role, scoped-access,
reporting-group, aggregate operator-surface, browser accounting admin-studio, WPF accounting
admin-studio, chart administration, rule-test and promotion, close setup, provider/external-GL
mapping setup, tenant/company/report-group setup, ledger-book administration, posting-rule
authoring, approval queues, dimension mapping, implementation sandbox validation, audit review
tooling, bulk import/export safeguards, performance validation, disaster-recovery runbooks, and retained-evidence readiness so
production accounting setup is blocked by shared contract state rather than workstation-local
assumptions.
Each configured tenant administration or enterprise configuration studio control requires retained
evidence for that setup lane, or an explicit `tenant-admin/full` or `tenant-administration/full`
certification packet, before readiness counts the control as complete. That evidence must identify
the selected tenant and company, so a generic or wrong-company setup packet is retained for audit
context but cannot certify every tenant-admin control by implication.
`AccountingTenantAdministrationProfileDto` is the retained setup profile shape for the same controls,
allowing shared endpoints, browser, and WPF to review or certify tenant administration posture
without passing transient request-only booleans.
`AccountingProductionCertificationProfileDto` is the retained tenant/company/fund/book certification
profile for ledger-book-native workflow controls and dimensional ledger/query/report/export controls, so
production readiness can load approved evidence from the shared Accounting System store only for the
active tenant, company, fund, and ledger-book scope instead of trusting request-time certification
flags alone.
`AccountingProductionGapDto` adds a stable, shared production-gap checklist to the readiness
payload for the five current productization lanes: configurable multi-ledger workflows,
enterprise accounting configuration studio, guarded external-GL integration, dimensional
ledger/reporting, and production controls hardening. Each row carries status, highest severity,
component areas, blocking issue codes, routes, summary, and required action so browser, WPF, and
admin setup surfaces can show the same remaining-work checklist without reverse-engineering it from
component labels or local heuristics.
Certified migration run artifacts must also be operationally clean: a retained certified run needs a
completion timestamp and zero retained issue count before the migration rollout plan can mark that
lane ready. This keeps ledger-book, historical journal, dimensional, configuration-promotion, and
close/reporting evidence migrations from becoming production proof while unresolved run issues
remain attached to the artifact.
Private-capital command-center DTOs in `Ledger/AccountingConfigurationDtos.cs` compose a single
fund event into evidence, workflow, ledger-impact, capital-account-impact, treasury expectation,
reconciliation, report-usage, delivery-record, tax-support, and audit-history lanes so clients can
navigate the v0.18 fund-event spine without deriving lane readiness locally. `SupportPackages`
carry operational evidence, payment intent, report output, delivery, tax-support, and audit-support
package posture so clients can reconstruct retained package readiness without mining lane details.
Manual journal lifecycle DTOs keep posted-entry correction provenance in the shared contract:
reversal and rebook actions retain typed original/correction links plus generated-draft transition
rows so browser, WPF, audit export, and report package surfaces do not infer corrections from memo
text or audit action names. Approval, rejection, and posting transitions also carry retained
evidence links so governed lifecycle state is not represented only by notes.
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
by a generic evidence subject string. Evidence nodes can also carry additive string metadata for
typed proof identifiers such as report-pack delivery attempt and package ids, allowing manifest
exporters to index delivery records without parsing operator summaries. Evidence Vault identities
also publish frozen request-list groups for event, close, audit, tax, and report-package support
beside the individual support requests so browser and WPF clients do not infer package checklists
from request ids. `EvidenceVaultRequestListQueryDto` and `EvidenceVaultRequestListEntryDto` expose
those retained request-list groups as a shared read index with vault id, manifest route, subject
linkage, support requests, open counts, severity, and blocked-output metadata.
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
payee, account scope, business purpose, approval policy, retained source evidence,
approval chain, bank/cash evidence with retaining-operator attribution, reconciliation linkage, audit history, and explicit
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
journal-entry, trial-balance, import-summary, reconciliation-preview evidence, row-level external
and Meridian ledger evidence references, and GL reconciliation evidence-package posture. Keep these
DTOs provider-neutral so QuickBooks-like adapters, shared endpoints, browser Accounting, Settings,
and future WPF surfaces consume the same read-only import, tie-out, and package vocabulary before
any posting/export workflow is enabled.
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
Provider integration manifest contracts under `Integrations/` define the versioned setup payloads
for no-code provider intake. They keep credentials as secret references, preserve raw payload,
quarantine, quarantine-review decisions, quarantine replay summaries, and staging identities, and
expose template catalog entries, OpenAPI import requests/results, setup-save requests/results,
manual CSV dry-run requests/results, schema-drift check requests/results, sync planning
requests/results, sync-run history payloads, run-due sync requests/results, promotion-readiness preview rows, durable
reconciliation handoff request/result/history records, activation state, mapping confidence,
validation issues, endpoint definitions, and sync schedules as shared contracts before browser or
WPF surfaces render setup, monitoring, reconciliation handoff, or scheduled execution state.
Handoff result payloads include duplicate-record counts so clients can show idempotent retry
failures from retained history rather than issuing another downstream reconciliation input.
`IProviderIntegrationTenantManifestStoreFactory`
lets workstation-hosted services resolve a tenant-partitioned manifest store while preserving the
existing global store contract for non-workstation callers. Activation-readiness
payloads carry operator-safe issue codes and required evidence labels so no-code setup can block
unresolved canonical mappings, missing approval evidence, and production-write capabilities that
are not backed by a certified provider adapter.

Report-pack workflow contracts carry the W4 governed lifecycle states `Draft`, `InReview`,
`Approved`, and `Published` plus governed publication metadata: sign-off actor, evidence hash,
retained manifest path, retained evidence links, report-line provenance, create requests, publish
requests, explicit `Rejected` state support, explicit review-state rejection requests with reason,
actor/role, and optional evidence-link metadata, workflow action-origin requests for material
report-pack state changes, and restatement requests with approver, prior-version, changed-line,
evidence-link, and action-origin metadata. The same shared contracts also carry
report-pack delivery attempts, delivery failure attempts, delivery history, operator-managed
reporting schedules, schedule delivery targets, due-schedule run results with delivery
attempts/warnings, ad-hoc report-run requests/results, and HTML/PDF rendered-statement artifact
formats so browser, WPF, endpoints, and host bootstrap payloads consume the same reporting command
and history shape.
Workflow action, restatement, publish, and delivery request DTOs carry `OperationsActionOriginDto`;
services must reject assistant or automation-origin approval, restatement, archival, publication,
delivery-package creation, and delivery-failure recording before any retained output, report-line
edit, or stakeholder package is written.
Delivered report-pack attempts can also carry `ReportPackDeliveryPackageDto`, including the
delivery mode, secure link or portal route, retained manifest path, requested PDF/XLSX/CSV
formats, retained artifact metadata, artifact SHA-256 checksums, artifact version stamps, the
publication evidence hash used for package integrity summaries, optional publication manifest
metadata, publication evidence links, retained line provenance, and publication-approved branding
metadata. Generated reporting-run packages also retain run id, template id, schedule id,
as-of date, trigger, status, attempt count, section count, lineage count, selected schedule
branding theme metadata, and source-artifact metadata so PDF/XLSX/CSV artifact bytes, checksums,
and byte sizes can be reconstructed consistently after package-store reloads. Delivery packages also carry contract-owned access,
channel, and download summaries plus `ReportPackDeliveryAccessLinkDto` rows for the primary
email-link/secure-portal/evidence-vault/internal route, operator route, retained manifest, and
token-gated artifact downloads, so clients can explain package access and retained outputs without
parsing token URLs locally. Delivery packages also carry `ReportPackDeliveryNotificationDto`
rows that preserve the recipient, channel, subject, body, token-gated href, status, and expiry for
email-link and secure-portal package notifications, so clients can show what was sent or published
without fabricating outbox state from the secure URL. Token-gated email-link and secure-portal
packages also carry an access-expiry timestamp plus access and channel summaries so clients can display and enforce
package availability from the shared contract. Keep those package fields shared so email-link, secure portal, evidence-vault, and
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
`WorkstationReportingPayload` and `FundReportingSummaryDto` also carry
`reportWriterDatasetSources` for no-code report-writer authoring. Each source includes a stable
source id, label, description, row count, field catalog, retained source rows, tags, certification
state, validation state, reconciliation state, refresh cadence, owner, version, release-approval
summary, lineage manifest, source run ids, permitted consumers, and optional row-lineage/evidence
index field names. Payloads publish a combined retained-reporting source plus dedicated
portfolio-cut, Top-N/contribution, cross-fund consolidation, and certified operational data-mart
sources so browser and WPF clients can preview pivot, Top-N, contribution, formula, and
row-lineage-index grids from governed portfolio, analytics, and consolidation data without
generating sample rows locally.
`ReportingRunRequestDto`, `ReportingScheduleUpsertRequestDto`, and `ReportingScheduleRecordDto`
can also carry `datasetSourceId` so ad-hoc and scheduled report-writer runs resolve one of those
retained sources server-side when callers do not embed explicit `datasetRows`. Generated reporting
run payloads and manifests retain the resolved report-writer dataset source id, label, and row
count so audit cards, delivery packages, and downstream evidence consumers can prove which governed
dataset powered a no-code report-writer output.
`ReportingScheduleUpsertRequestDto` and `ReportingScheduleRecordDto` can also retain a
`BrandingThemeId` or `BrandingThemeOverride`, and the generic Reporting manifest carries the
resolved theme into scheduled generated-run packages so recurring report-writer deliveries preserve
firm identity, colors, footer, and disclaimer metadata.
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
artifact, including grid title, kind, dimension count, metric count, formula count, and retained
validation pass/warning/failure summary counts, so clients can render generated no-code grid
evidence and fail-closed readiness without inferring it from artifact URIs. `UiApiRoutes`
also owns the retained grid artifact route at
`/api/fund-structure/reporting/runs/{runId}/report-writer-grids/{gridId}` so browser and desktop
clients build JSON/CSV/XLSX links from the same route contract.
`ReportPackDeliveryPackageDto` preserves the same generated grid rows and report-writer dataset
source evidence for reporting-run delivery packages plus optional `renderedReportWriterGrids` rows,
columns, warnings, lineage, data-dictionary fields, and validation checks so
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
source-evidence gap. `FreshnessPolicy` records the policy name, evaluation time, source age, live
and stale thresholds, classification booleans, and reason used by the server so clients can explain
why a reporting view is live-linked, source-backed, stale, or blocked without recomputing that
state. Browser clients may poll or auto-refresh the shared portfolio route for tick-linked
reporting views, but they must continue to render the server-owned state, tick age, provider,
policy, and blocker fields rather than recomputing freshness locally.
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
and cross-fund consolidation outputs. Each descriptor carries retained artifact path, retained
manifest path, deterministic SHA-256 integrity hash, integrity summary, schema/row/field/source
counts, row-lineage count, readiness, route, evidence, and version-stamp fields so downstream
regulatory, warehouse, and investment consumers can prove exactly which governed export artifact
was described.
`StructuredReportingExportPayloadDto` returns stable column metadata, data-dictionary fields,
validation checks, string-valued JSON rows, and optional row-lineage entries with one-based row
numbers, stable row keys, and schema-ordered SHA-256 row hashes for downstream export consumers. The same endpoint
accepts `format=json`, `format=csv`, or `format=xlsx` for schema-ordered file output; XLSX exports
also carry Metadata, DataDictionary, Validation, and RowLineage worksheets, including data-warehouse
descriptors whose default retained format is JSON. Payloads also carry the generated timestamp,
actor principal, company id, and report group principals used for the request so JSON, CSV, and
XLSX downloads can be bound to user-stamped export audit headers.
Reporting summaries and generated report-pack snapshots also carry shared `ReportBrandingThemeDto`
metadata for firm name, colors, logo URI, footer, disclaimer, and built-in/custom posture. Summary
payloads retain fund-profile context so clients can generate branded governed report packs without
hard-coded fund ids. `FundReportPackPreviewRequestDto` accepts the same saved-theme id or custom
branding override as generation, and `FundReportPackPreviewDto` echoes the normalized theme so
clients can verify styling before writing retained artifacts. Delivery artifacts apply the selected
theme to rendered HTML/PDF packet presentation and retain the same theme details in XLSX metadata,
keeping client-facing packet styling tied to the governed contract rather than browser-local copies.
`ReportingScheduleDeliveryPlanDto` carries schedule-to-recipient delivery plans with PDF/XLS/CSV
format sets, email-link or secure-portal mode, readiness text, due/as-of posture, last retained
delivery attempt metadata, latest package artifact counts, checksum integrity summaries, and
version stamps plus the schedule or last-package branding theme. It also carries the last retained
download summary, access-expiry timestamp, package access/channel summaries, notification proof,
report-writer dataset/grid summaries, and entitlement scope from the package evidence packet so clients can show whether the latest
scheduled no-code package was generated from a retained dataset and whether its pivot/Top-N/formula grids were rendered,
as well as whether it was company-wide, private, or restricted to specific user/group/company
principals. Delivery package `accessLinks` may include an `artifact-xls` compatibility link for
retained XLSX workbook artifacts; it is a token-gated `format=xls` route that returns the canonical
XLSX workbook bytes and MIME type rather than a separate legacy workbook artifact.
Email-link and secure-portal package links resolve to token-gated HTML views for recipient-facing
access, while `format=json` on the same routes returns the retained package manifest for API
clients.
`ReportingRunAuditTrailDto` exposes retained generic Reporting run lineage with run/template/as-of
metadata plus timestamped actor/action/notes rows, letting browser, WPF, and export consumers inspect
the same audit trail persisted by the orchestration store instead of relying on collapsed action
labels. For no-code report-writer runs, the audit trail also echoes the retained dataset source id,
label, and row count from the manifest so audit consumers can prove which governed dataset backed the
generated grids without reading package artifacts first.
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
Generic Reporting run manifests can retain the resolved policy from the approved template, allowing
scheduled generated-run delivery evidence to preserve restricted user, group, company, or private
entitlement scope instead of broadening no-code report packages to company-wide access.
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
payloads, including approval-limit and segregation-of-duties metadata, should stay Identity-owned
so browser, desktop, endpoint, and F# policy consumers share the same authority-configuration
vocabulary without keeping auth DTOs in the generic contracts project.

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
references, and validation support issues. Retained artifact references and copied vault artifacts
can also carry capture channel/source metadata plus extracted fields with confidence, review state,
expected value, validation status, and linked record identity. Keep that metadata shared so packet,
report, approval, screenshot, statement, audit, tax, close, and validation producers can enforce the
same retained-artifact, extraction, and request-list vocabulary. Request-list index DTOs should stay
contract-owned so close, audit, tax, report-package, browser, and WPF review surfaces can query the
same frozen support posture without parsing manifest JSON.
`EvidenceVaultIntakeRequestDto` and `EvidenceVaultIntakeResponseDto` extend that vocabulary to
API-backed document intake: callers provide the evidence subject, channel, file name, base64
payload, optional expected SHA-256 hash, source reference, extraction fields, lifecycle metadata,
and lookup linkage, while the response returns the retained artifact path, content hash, capture
metadata, extraction review fields, and vault identity. Non-ready extraction fields are part of the
shared support-request vocabulary so direct intake can freeze close, audit, tax, report-package, or
event request lists without browser or WPF clients parsing manifest JSON.

Audit Trail Explorer contracts live under `Workstation/AuditTrailExplorerDtos.cs` and normalize
retained audit records into cross-object timeline rows with object kind, object id, actor,
correlation, related-object ids, metadata, evidence routes, and action-ledger proof fields. Keep these query and result
payloads contract-owned, including object-kind/object-id and related-object filters, so browser and
WPF clients search the same audit vocabulary instead of inventing client-local timelines. Manual
override rows now use `OperatorAction` object kind keyed by override id, and circuit breaker rows
use `ExecutionControl` object kind with control-route evidence links for operations review.
Action-ledger proof fields publish the source ledger, source-local sequence, current event hash,
previous event hash when the source owns one, and status so v0.18 audit evidence can show the
immutable event trail without client-side reconstruction.
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
semantics locally. `CloseLedgerPeriodRequest` carries action-origin metadata because reviewed
automation can summarize close posture but cannot lock an accounting period for a human operator.
`LedgerPeriodPnlSummaryDto` also separates realized revenue/expense net income
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
custodian statement exceptions without rebuilding domain aggregates locally. Shared statement break
rows also carry retained case SLA due/warning/breach timestamps, SLA state, escalation label, and
escalation reason so statement-originated exceptions keep the same assignment and escalation posture
in browser, WPF, and evidence-package views. Keep these DTOs
additive and transport-safe so browser, WPF, retained evidence, and automation consumers can
reconcile custodian statements without referencing application, UI, or infrastructure types.

Direct lending command result codes distinguish validation failures, missing aggregates,
optimistic concurrency conflicts, and idempotency/command conflicts so persistence stores can return
operator-safe failure reasons without parsing exception text.

Accounting reconciliation casework contracts are shared here: break queue items carry assignee, priority, SLA policy/state/timestamps, age band, versioned taxonomy, threaded comments with mention/evidence/hash metadata, evidence counts, sign-off/reopen metadata, source-origin metadata, and optimistic concurrency versions. Resolve/dismiss, casework command, and bulk casework request payloads carry `ActionOrigin` so reviewed automation can draft triage but cannot resolve, sign off, dismiss, or reopen accounting exceptions. Casework command, bulk-triage, taxonomy, SLA policy, validation-problem, and sequenced audit-event payloads must remain additive so browser and WPF workstation clients use the same Accounting workflow.

Manual journal entry contracts include private-capital entry types for capital calls,
distributions, subscriptions, redemptions, LP transfers, and management fees. `TreasuryLedgerContextDto`
is the shared audit context for those drafts and carries effective date, idempotency key, fund
event, capital account, investor, payment intent, and settlement references so browser, WPF,
Financial Operations, and ledger metadata use the same retry-safe fund-event vocabulary. `AccountingPostingCommandDto` is the shared event-accounting posting envelope for source-backed journal impact; it carries command, aggregate, period, ledger-book scope, source/correlation/causation, idempotency, reviewer state, treasury context, correction lineage, action origin, and typed evidence references so posting services and durable storage validate the same intent.
Manual journal drafts, workbench reads, lifecycle requests, and evidence-attachment requests also
carry optional tenant/company scope so shared endpoints can stamp the authenticated accounting
context, resolve the tenant-scoped chart, and retain scoped lifecycle audit rows without trusting
browser- or WPF-supplied organization fields.
Accounting configuration contracts also carry the productized rules-studio vocabulary:
effective-dated posting rules, flat conditions, grouped `All`/`Any` condition sets, formulas,
allocation metadata, priority, dimensional scope through `LedgerDimensionSetDto`, generated posting lines, dry-run results,
persisted and ad-hoc rule/version-pinned test-case requests/results with expected generated posting-line assertions and saved-test evidence provenance, service-owned version history, promotion approvals, and
the dedicated promotion-approval request for approving a retained rule version with approver notes,
retained approval/review evidence that identifies the retained rule, rule version, and approval id in the same artifact,
human-operator action origin, and passing saved current-version regression tests whose retained evidence identifies the test case, expected rule, and expected version in the same artifact. Activation-readiness blockers for promotion-gated rules
remain contract-visible. Chart, template, posting-rule, regression-test, and promotion mutation
requests can carry `LedgerBookId`, `TenantId`, and `CompanyId`, and
`IAccountingConfigurationStore.GetAsync` accepts the same tenant/company/fund/book scope so clients
can address tenant-isolated fund-level and ledger-book-specific rule studios without sharing one
fund-wide workspace by accident. Accounting configuration audit events also retain optional
tenant/company scope so browser, WPF, and admin review surfaces can list mutation history without
blending same-company events across tenants. Dry-run generation expands positive static
or formula-backed allocation weights into generated posting lines, preserves balance through
residual rounding, and merges allocation target dimensions into the preview payload. Generated
posting-rule predicates can evaluate dimensional fields and external GL dimensions through the
same shared dry-run request, including explicit external GL field aliases such as
`externalGl.Department` and `gl.Department`.
`PostingRuleJournalCandidateRequestDto`, `PostingRuleJournalCandidateIssueDto`, and
`PostingRuleJournalCandidateResultDto` publish the next execution step after dry run: a source
event can be evaluated by the shared Rules Studio engine and returned as a governed journal draft
candidate with selected rule/version metadata, generated posting lines, retained evidence links,
blocking/non-blocking issues, and an approval-gated `AccountingPostingCommandDto` when validation
passes. Candidate requests also carry tenant/company scope alongside fund and ledger-book scope so
the dry-run and chart resolution path uses the same isolated Rules Studio workspace as accounting
configuration. `UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidates` exposes that shape
as the shared browser/WPF preview route. The candidate contract is non-posting by design; browser
and WPF clients must treat it as review input for the JE lifecycle, not as a ledger append.
`PostPostingRuleJournalCandidateRequestDto` and `PostedPostingRuleJournalCandidateResultDto` publish
the approved generated-candidate append contract behind
`UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts`; that route requires the
candidate aggregate to be the target `LedgerBookId` while `SourceEventId` remains the economic event
identity, allowing one event to post separately into GAAP, cash, tax, statutory, or primary books.
posting and allocation formula references are fail-closed contract concerns: duplicate formula ids,
missing formula references, and formula-backed allocation weights that resolve non-positive surface
as critical validation issues in workspace validation and dry-run previews. Rules sharing source
event, priority, effective-date window, and overlapping dimensions are reported as
critical priority conflicts unless required amount predicates make the rules mutually exclusive, so
operators must make dry-run selection deterministic before activation. Dry-run results also expose
`rule.no-candidate-match` when effective rules exist for a source event but all candidates are
rejected by dimensional scope or predicates, preventing clients from treating an empty generated
posting preview as a valid non-posting outcome. Manual journal lifecycle contracts carry
submit, approval, posting, rejection, close-lock, reversal, and rebook actions plus transition audit and
evidence attachment payloads so posted entries remain immutable and corrections move through
separate drafts. Approval/rejection evidence, posting evidence, close-lock evidence, and correction
evidence must identify the relevant reviewer, posting, correction, or close-lock intent plus the
journal entry or accounting period on the same retained artifact, giving browser, WPF, and service
consumers the same retained-evidence provenance contract. Reworked `Draft` and `NeedsFix` entries
must not carry stale submitted, approved, posted, or close-lock metadata from an earlier rejected
review cycle, but their lifecycle transition history remains retained for audit reconstruction.
Close and reporting contracts publish `ClosePeriodPlanDto`,
`SignOffCloseTaskRequestDto`, `LateAdjustmentRequestDto`, `ReviewLateAdjustmentRequestDto`, and
`AccountingReportPackageBundleDto` so browser and WPF consumers use the same dependency,
role-scoped sign-off requirement, close-calendar milestone, evidence-backed close task sign-off
whose retained artifact identifies the task, sign-off role, and workflow or period, materiality, retained
late-adjustment approval/rejection with request-, journal-, workflow-, or period-specific evidence
on the same retained artifact, financial statement, investor capital
statement, realized gain/loss, NAV, export-artifact manifest, certification, validation, and restatement vocabulary.
Operations Continuity workflow start, summary-list, and workflow projection DTOs can carry an
optional `LedgerBookId`, and close-plan DTOs preserve that book scope so close/reporting clients can
remain ledger-book-native after workflow handoff. Close-backed report package contracts treat ledger-book
scope as evidence scope as well: retained ledger, reconciliation, rendered-report, and NAV support
links are expected to name the selected book before the package is ready for certification.
Posting-rule promotion approval, close sign-off, late-adjustment, report-package certification, and external GL export certification
request DTOs also carry `OperationsActionOriginDto` so reviewed automation can draft support but
cannot approve, sign off, certify, or retain governed accounting evidence without a human operator.
Report export artifacts also expose a controlled retrieval manifest with content hash, evidence links,
certification state, and `ExternalPostingAllowed = false` so clients can prove what would be exported
without enabling live external posting.
External GL export package manifests expose the same controlled-review posture for guarded
QBO/Xero/NetSuite artifacts: generated mapped lines, validation state, retained evidence, content
hash, mapping/reconciliation lineage, and `ExternalPostingAllowed = false` remain visible without
enabling live posting.
Export packages also retain mapping profile, reconciliation, and balanced-reconciliation lineage so
Financial Operations can revalidate the current external-GL mapping and latest reconciliation state
before certification.
`SubmitManualJournalEntryApprovalRequest` carries action-origin metadata because reviewed
automation may draft journal content, but cannot submit the durable approval record on behalf of a
human operator. Manual-journal save, validation, submit, evidence-attachment, and lifecycle
requests can also carry requested `LedgerBookId` scope so the shared service rejects cross-book
draft mutations before normalization, persistence, approval, or correction state changes.
Manual-journal mutation requests also carry period-lock posture so save, submit, evidence
attachment, and lifecycle mutations can be rejected once close management locks the accounting
period; validation requests stay non-posting and return `manual-je.period-locked` as a critical
issue. Shared manual-journal validation also treats GUID-backed `PeriodId` values as ledger period
references when a ledger store is registered, blocking approval and lifecycle promotion if the
period is missing, closed, or scoped to a different `LedgerBookId`.
Evidence subjects can carry optional `LedgerBookId` scope so shared accounting evidence packets can
resolve the same ledger book as the initiating accounting surface instead of falling back to
fund-wide activity.
`LedgerDimensionSetDto` is the canonical dimensional accounting envelope for rules, generated
postings, manual JE headers/lines, mapping profiles, close checks, and reporting/export filters. It
now supports neutral operational-finance scope across organization, entity, portfolio, book,
account, customer, vendor, project, counterparty, instrument, cost center, and external GL
dimensions; fund, investor, and capital-account fields remain first-class specializations for
fund/private-capital workflows. Manual journal normalization trims and retains header dimensions,
merges deterministic external GL dimension keys, and propagates fund/entity scope to line
dimensions while allowing line-specific organization, entity, portfolio, account, instrument,
tax-lot, cost-center, and external GL overrides.
Generated posting candidates also carry those line dimensions into the governed draft request shape,
where retained ledger entries receive first-class line dimensions while compatibility metadata tags
remain available for downstream report and external-GL mapping recovery. This keeps dimensional
scope available without exposing a direct posting path.
Ledger period and cross-period trial-balance report lines carry that same dimension envelope so
closed-period accounting reports can distinguish account balances by retained fund/entity,
strategy, capital-account, instrument, cost-center, counterparty, and external GL context instead
of collapsing all activity to the account alone.
`LedgerJournalEntryDto` and `LedgerJournalEntryLineDto` expose raw period journal entries through
the same ledger-book and line-dimension contract so browser/WPF drill-through views can inspect the
underlying immutable postings without falling back to account-only report totals. The same payload
shape is used for aggregate-level journal drill-through, allowing clients to scope an operational
aggregate back to retained ledger-book postings and dimensional lines.
`OperationalFinanceScopeDto`, `OperationalEventCommandContextDto`,
`OperationalFinanceTraceNodeDto`, and `OperationalFinanceRecordTraceDto` provide the read-only
trace contract for the customer-neutral proof path from operational event through evidence,
posting candidate, journal lifecycle, ledger impact, report line, package, and audit event without
introducing a new posting command or bypassing manual journal lifecycle controls.
`AccountingReportPackageRequestDto` accepts an optional `LedgerDimensionSetDto` so report package
assembly can be scoped by fund, entity, sleeve, strategy, investor, capital account, instrument,
tax lot, cost center, counterparty, book, project, and external GL dimensions before certification.
It also carries optional tenant and company identifiers so retained package history, certification,
and export retrieval can be isolated by authenticated enterprise scope.
`AccountingReportPackageBundleDto` carries close-plan validation and optional retained close workflow
lineage into financial statement, investor capital, realized gain/loss, NAV, line-level provenance,
export-artifact manifest, certification, and restatement outputs. Financial statement, investor
capital, realized gain/loss, NAV, and provenance artifacts carry the retained ledger-book and
`LedgerDimensionSetDto` scope so report consumers can remain book- and dimension-native without
reconstructing package context from parent rows or retained-history routes. The retained bundle
also preserves tenant and company scope so browser/WPF history routes do not blend packages across
companies that share a fund, period, or ledger-book naming convention. The bundle also carries
service-owned close readiness rows for checklist/sign-off posture, period lock, late-adjustment
review, report evidence, export certification, and restatement workflow state so browser, WPF, and
API consumers do not rebuild certification safeguards locally. Package
certification remains `Draft` when close checklist dependencies are incomplete, approved sign-offs
are missing, or material late adjustments are not approved, and close-backed certification can
refresh the current close plan before moving a retained package to `Certified`.
`CertifyAccountingReportPackageRequestDto`
is the shared evidence-backed command payload for moving a retained ready-for-review package to
`Certified`; consumers supply the package id, reviewer notes, actor context, and evidence links that
carry approval, certification, sign-off, or review evidence and identify the retained package,
certification id, ledger book, package period, and explicit dimension scope when the package is
dimension-scoped on the same evidence artifact,
while Financial Operations owns the validation and state transition. Export artifact rows expose
artifact kind, format, route, content hash, source statement id, retained ledger-book scope,
`LedgerDimensionSetDto` scope, retained evidence, and certification state so browser and WPF
clients do not infer report output readiness or dimensional context from package ids or route
strings.
Guarded external GL export packages require certified account and dimension mapping coverage before
they can reach `ReadyForReview`: account mappings must be present, every dimension mapping must be
certified, both Meridian and external GL dimension sides must carry fund/entity scope, and generated
export lines must come from mapped Meridian-owned ledger totals rather than external-only evidence.
Reconciliation summaries carry `LedgerBookId`, and guarded export package requests must name the
target Meridian ledger book and use reconciliation evidence from the same book before review or
certification can proceed. Retained export package and manifest DTOs also carry a reconciliation
snapshot hash so certification can detect content drift in rows, totals, or evidence even when the
provider-facing reconciliation id is unchanged.
Certified mapping profiles must retain mapping approval, certification, sign-off, or review evidence
that identifies the mapping profile or provider/fund scope on the same evidence artifact, and the
certifying upsert must carry a human-operator action origin; split support and approval links leave
the profile in `Draft`.
The retained export-control evidence supplied at package creation must identify the export fund,
provider/fund scope, or exact export period before the package can move beyond Draft, and governed
export package retention also requires a human-operator action origin.
`CertifyAccountingSystemExportPackageRequestDto` carries the retained export package id, reviewer
notes, actor context, and evidence links for the service-owned transition to `Certified`; the
certification evidence artifact must itself identify the retained export package, certification id,
and exact export period, and the action origin must be a human operator. Export package request,
certification, package, and manifest DTOs also carry optional tenant and company scope so guarded
external-GL artifacts can be retained, certified, and retrieved without blending companies that
share provider, fund, period, or ledger-book naming conventions. Live
posting remains disabled even when those checks pass and the export artifact is certified. The
certification transition also fails closed if the retained package's current mapping profile or
latest reconciliation now has critical validation blockers.
`AttachManualJournalEntryEvidenceRequest` and
`UiApiRoutes.LedgerManualJournalEntryEvidence` expose a governed manual-journal evidence mutation
for mutable drafts and submitted/approved entries. The request carries the current journal version,
one typed evidence attachment, optional evidence links, and origin metadata; the shared service
validates line-scoped attachments, audits `manual-je.attach-evidence`, and rejects posted,
reversed, rebooked, or close-locked entries.
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
`PrivateCapitalActivityRoutes` helper publishes the route-shaping API used by Financial Operations
and UI adapters; it is a route contract helper only and does not own readiness or accounting
semantics.
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
