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
- `Extensibility/` - shared extensibility catalog service, tenant-template activation service, and
  provider adapters that expose configurable workflow registrations through
  `Meridian.Contracts.Extensibility`.
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
`LoginSessionMiddleware` now also attaches a request tenant scope through
`CurrentTenantIdKey`, currently derived from the authenticated company id until tenant ids diverge
from company ids. `IWorkstationTenantContextAccessor` is the shared endpoint/service seam for
resolving actor, company, tenant, role profile, and permission context; production endpoint work
should use that accessor instead of reparsing `HttpContext.Items` or trusting client-supplied actor
and company fields. The `/api/workstation` route group also requires that tenant scope before any
workstation endpoint handler runs, so browser and WPF clients must operate through an authenticated
tenant-scoped session rather than relying on client-supplied organization fields.

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
Operations Continuity workflow list, detail, timeline, break-list, ledger-preview, close-readiness,
approval-policy, and close-calendar reads require the shared operations-continuity read permission
because those payloads expose Financial Operations evidence, blockers, assignments, and period-close
posture.
`FundOperationsWorkspaceReadService` passes Operations Continuity reviewed-automation posture through
the shared governance lifecycle projection so browser and WPF report-pack handoff surfaces render
the same automation guardrails and retained review evidence.
Financial Record Explorer endpoints are registered from `WorkstationEndpoints.FinancialRecordExplorers.cs`
under `/api/workstation/financial-record-explorers/{explorerId}` for `ledger`, `portfolio`, and
`security-instrument`, and `report-line-provenance`. `FinancialRecordExplorerReadService` composes
source-backed strategy run ledger, portfolio, Security Master, report-pack line provenance,
delivery history, evidence, reconciliation, reporting, and audit projections into the shared DTO.
The ledger explorer carries canonical `LedgerDimensionSetDto` scope into row cells, drill-in fields,
and dimension filter chips so browser and WPF users can inspect fund, entity, sleeve, strategy,
portfolio, book, account, investor, capital-account, instrument, tax-lot, cost-center,
counterparty, and external-GL context without re-inferring accounting scope from display text.
The report-line provenance builder emits an explicit instrument -> position or transaction ->
reconciliation -> journal -> report-line -> evidence/audit chain using retained provenance fields,
while `FileFinancialRecordExplorerSavedViewStore` persists operator-created views under the
workstation data root. Saved views are keyed by the authenticated workstation tenant and explorer
id, so operator filters created in one tenant do not appear in another tenant's Financial Record
Explorer session. Missing projections return empty or blocked DTO state with disabled actions and
reasons, not synthetic operational balances.
Reference-data endpoint groups for bonds, options, equity, futures, FX spot, crypto, deposits,
certificates of deposit, commodities, swaps, and money-market funds adapt `Meridian.Instruments` services
to shared browser/WPF routes. Keep those endpoints as permission and HTTP adapters; instrument
contract/reference logic belongs in the Instruments design module.
The root workstation bootstrap endpoints return canonical `WorkstationDataPayload` and
`WorkstationAccountingPayload` contract types for Data and Accounting. Retained
`/api/workstation/data-operations` and `/api/workstation/governance` routes remain compatibility
aliases only and must not drive new contract type names.
Accounting and Reporting workstation payloads forward `fundProfileId` and `ledgerBookId` query scope
into the shared manual-journal workbench when that service is registered, allowing the browser and
desktop reporting surfaces to render the same private-capital fund-event ledger, capital-account
subledger, evidence, approval, and report-output projection without a UI-local read model.
Book-scoped Accounting payloads also apply `ledgerBookId` to reconciliation break queues,
calibration summaries, open-break metrics, and the accounting control center. Queue items without
explicit book scope are excluded from book-scoped responses instead of being inferred from fund,
route, or exception text.
Accounting configuration workspace responses also include the computed Rules Studio read model from
`AccountingConfigurationService`, covering rule rows, effective-dated/generated-posting coverage,
saved regression-test coverage, validation counts, promotion queues, and activation readiness so
browser and WPF accounting screens can behave like a configuration studio without duplicating rule
approval logic in clients. Book-scoped configuration reads and activation readiness now also verify
the selected ledger book against the registered ledger-book service when that service is available,
returning a critical `configuration.ledger-book-missing` issue instead of letting operators activate
rules for an unconfigured book. When the requested book is missing but the fund has registered
ledger-book scope, the same workspace carries a server-derived ledger-book setup candidate so
clients can call the shared ledger-book endpoint without reconstructing fund-structure node details
locally.
Shared reporting run projections also carry the manifest or workflow as-of date with run id,
template, status, trigger, retry attempts, section counts, linked lineage, artifacts, and audit
actions, plus structured generated report-writer grid metadata when a run retained
`report-writer://.../grids/{gridId}` artifacts. This keeps report-run audit/version cards and
generated no-code grid evidence source-backed across browser and desktop clients.
Strategy-run ledger trial-balance and journal endpoints accept canonical accounting dimension
filters for fund, entity, sleeve, strategy, portfolio, account, investor, capital account,
instrument, tax lot, cost center, counterparty, and external GL scope, including
`externalGl.<name>` and `externalGlDimensionKey`/`externalGlDimensionValue` query forms. They
return the shared `LedgerDimensionSetDto` on rows where the source run provides that scope. Filters
for unavailable dimensions fail closed rather than inferring accounting scope from display text.
Report-pack delivery package access stays server-owned: email-link manifests use
`/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/package`, secure-portal
manifests use `/portal/reporting/packages/{packageId}`, and retained artifact downloads use the
delivery artifact route. Those routes validate the retained package token and expiry before
returning package manifests or PDF/XLSX/CSV bytes, so browser and WPF clients should render the
provided access links instead of reconstructing or bypassing delivery access.
Report-pack evidence packets also summarize matching delivery attempts as delivery-record and
delivery-evidence-packet nodes that route to the canonical `report-pack-delivery` packet, so the
parent Operational Evidence Graph shows delivery coverage without duplicating package internals in
browser or WPF clients. Delivery package nodes carry typed attempt/package metadata, and Evidence
Vault manifest export uses that metadata for `ReportPackDeliveryAttemptId` and
`ReportPackDeliveryPackageId` linkage before falling back to older summary parsing, keeping vault
search resilient when operator copy changes.
Data upload intake endpoints are registered under `/api/workstation/data/uploads/*`. The template
route serves the contract-owned catalog, and the preview route accepts bounded CSV uploads,
retains the source file under the resolved workstation upload root, and returns schema issues plus
preview rows without mutating trades, transactions, Security Master assets, entity structure, or
ledger/accounting records. Bank-statement CSV import uses
`/api/workstation/data/uploads/bank-statements/import` to validate a retained bank statement,
require a bank fund account, and apply the parsed lines through
`IFundAccountService.IngestBankStatementAsync`; the imported bank data remains reconciliation
evidence and does not post Meridian-owned ledger entries.

Provider integration endpoints are registered under `/api/workstation/provider-integrations/*`.
Template routes expose the Application-owned starter manifest pack, the setup-save command persists
draft manifests and connection instances, activation-readiness routes surface fail-closed readiness
blockers, the OpenAPI import route seeds tenant-scoped draft manifests from provider specs, dry-run
command routes execute manual CSV and REST validation through Application-owned services, the
schema-drift check route compares retained raw payloads with manifest response and mapping paths,
the sync-plan route reports due/manual/blocked capability state from schedule and retained run
history, the sync-run history route returns durable run evidence with staging and quarantine counts,
the run-due sync route starts due read-only REST/OpenAPI/hybrid capabilities through the
staging-first dry-run runtime, resolves configured endpoint dependencies from retained raw payload
evidence, and the connection monitor route adapts durable sync-run, staging, quarantine, and
validation evidence into browser/WPF-compatible payloads. The staging review route exposes accepted
staging records, reconciliation-ready counts, warning groups, and capability summaries without
promoting those records to Portfolio, Security Master, Ledger, or Accounting stores. The identity-resolution
route previews provider account and Security Master match posture for staged records, including
missing identifiers, unresolved securities, and review-required account mappings before
reconciliation promotion. The promotion-readiness route composes that posture into ready,
review-required, and blocked rows for the reconciliation-staging handoff while remaining read-only.
The reconciliation-handoff route requires provider configuration permission, persists only
operator-approved ready rows as handoff evidence, and exposes handoff history for the same
connection without writing Portfolio, Security Master, Ledger, or Accounting stores. Duplicate
handoff attempts for the same staged record are blocked and returned with operator-safe issue rows.
Quarantine review routes expose grouped validation issues and persist operator review decisions without
changing the retained rejected raw records. Quarantine replay routes remap records approved for
replay after mapping changes and write accepted records back into integration staging or
re-quarantine unresolved records. Setup, OpenAPI import, readiness, dry-run, activation, sync-plan,
sync-run history, run-due sync, schema-drift, staging review, identity-resolution preview, promotion-readiness,
reconciliation handoff, quarantine review/replay, and monitor endpoints resolve the
authenticated workstation tenant before reading or writing stored manifests, connections, or
retained run evidence. Import, dry-run, and run-due commands require provider/configuration permissions
because they create manifests or retain raw payload, staging, quarantine, and sync-run evidence.
The activation command persists active manifest and connection state only after Application
readiness passes with retained approval evidence. Richer setup-editing screens still need to bind
draft editing, endpoint tests, mapping preview, and corrective mapping/data-rule actions through the
shared workstation API.
`BankFeedTransportService` reuses that same import boundary for scheduled local-file and SFTP
CSV pulls through `IEtlSourceReader`, and delegates Plaid API schedules to `IPlaidIngestionService`
so API feeds stay server-owned and ledger posting remains gated by Meridian approvals.
Plaid endpoints are registered as their own shared endpoint group from `UiApiRoutes`, with read
and mutation access resolved from the workstation session. The shared Plaid workstation service
keeps link-token creation, public-token exchange, item sync, webhook retention, and sandbox
transfer gating server-owned so browser and WPF clients do not handle Plaid access tokens or
duplicate bank evidence ingestion rules. Sandbox transfer authorization now verifies the approved
Meridian payment record directly through `IBankingService.GetPaymentAsync`; retained bank
confirmation, return, reversal, or failure evidence is recorded separately through the Banking
endpoint instead of being fabricated by approval itself.
Provider connection and readiness services project provider setup metadata from the Data
Integration credential catalog into shared rows. Browser and WPF provider surfaces should render
credential fields, allowed environments, diagnostics, evidence, and recovery actions from those
rows instead of maintaining provider-specific local forms. Provider connection routes require the
authenticated workstation tenant scope before listing, saving, verifying, or deleting credential
state, and credential mutations still require `ManageCredentials`.
Symbol mapping endpoints under `/api/symbols/mappings` are tenant-scoped shared configuration
routes: reads require `ViewConfig` or `ModifyConfig`, and upsert, delete, and CSV import mutations
also require `ModifyConfig` before writing the shared symbol mapping configuration.
Accounting-system endpoints are also registered as a shared endpoint group from `UiApiRoutes`.
`/api/accounting-system/production-readiness` exposes a read-only accounting rollout control-plane
assessment. `AccountingProductionReadinessService` composes ledger-book rollout, accounting
configuration and Rules Studio, generated posting and dimensional coverage, manual journal
lifecycle registration, close/report service registration, external-GL provider and certified
mapping posture, migration-rollout certification for ledger-book scoping, historical journal
backfill, dimensional backfill, accounting configuration promotion, close/reporting evidence
migration, and tenant-admin rollout guidance into one shared fail-closed payload for browser, WPF,
and admin setup surfaces. The route does not create ledger books, import external GL data, post
journals, certify exports, or close periods.
`Meridian.FinancialOperations.AccountingSystem.AccountingSystemIntegrationService` lists GL
providers, uses QuickBooks Online when local OAuth client id, client secret, refresh token, and
company realm id config are present, falls back to `quickbooks-fixture` otherwise, registers
read-only `xero-fixture` and `netsuite-fixture` import mappings, exposes planned live Xero and
NetSuite rows with posting disabled, retains the latest import in process,
and compares external trial-balance evidence against Meridian-owned ledger truth when the ledger
store is available. The reconciliation response carries provider-side refs, Meridian ledger refs,
and package posture for external import, Meridian ledger support, and the GL tie-out. The same
endpoint group now lists and upserts scoped external-GL mapping profiles and creates guarded export
packages that require certified mapping and reconciliation evidence before reaching ready-for-review
state. Export package creation also requires retained export-control evidence that identifies the
export fund, provider/fund scope, or exact export period. Export packages carry generated mapped
lines from Meridian-owned ledger totals so
reviewers inspect the exact artifact that would be exported while external-only evidence remains
reconciliation support. It also exposes a certification route for retained ready-for-review export
artifacts so reviewer notes and evidence are handled by Financial Operations while live posting
stays disabled. Retained export packages include mapping profile and reconciliation lineage, and
the Financial Operations service revalidates the current mapping and latest reconciliation before
the shared certification endpoint can move the artifact to Certified.
Export-package certification is a governed release gate and requires `AdminMaintenance`;
fund-structure operators may stage mapping and guarded export artifacts for review, but they cannot
certify the retained export package.
Ledger period trial-balance, signed trial-balance report, P&L summary, and cross-period report
routes share the same dimensional filter query contract for fund, entity, sleeve, strategy,
investor, capital account, instrument, tax lot, cost center, counterparty, book/account/customer/
vendor/project, and external GL dimensions so browser and WPF accounting views do not implement
separate filtering rules. Signed trial-balance report checksums include the normalized dimensional
filter scope as well as the retained report lines, so two different empty fund/entity/cost-center
or external-GL scoped reports cannot certify to the same payload hash.
`/api/ledger/periods/{periodId}/journal-entries` exposes the retained journal entries for one
ledger-book period through the same dimension filter query contract. The route returns
ledger-book-scoped journal and line DTOs, and filtered requests return only matching dimensional
lines so drill-through clients do not show account-only journal evidence beside dimension-filtered
reports.
`/api/ledger/aggregates/{aggregateId}/journal-entries` exposes the same retained journal DTOs for
one operational aggregate, with optional `ledgerBookId` plus the same dimensional filters, so
operator drill-through can stay book-native even when the entry point is an event or aggregate id
rather than a closed period.
Accounting report-package certification follows the same release-gate posture: direct-lending
operators may build retained ready-for-review report packages, while final certification requires
`AdminMaintenance`.
The controlled export-package manifest route returns generated mapped lines, retained evidence,
validation state, mapping/reconciliation lineage, a deterministic content hash, and explicit
posting-disabled posture without creating a live external posting path.
Export certification evidence must reference the retained export package id, certification id, and
exact export period on the same retained artifact, so a generic approval packet cannot certify a
different guarded GL artifact. Assistant or automation-origin export certification requests are
rejected before the service certifies the artifact.
UI Shared maps these routes and registers the Data Integration-owned credential-backed
connection store, but it does not own GL evidence reconciliation, mapping-profile validation,
export-package certification safeguards, or QuickBooks credential-persistence mapping.
The Data Integration-owned QuickBooks Online lane refreshes access through the server-side token
exchange seam and imports chart-of-accounts, journal-entry, and trial-balance evidence as read-only
reconciliation input.
Meridian remains the source of all ledger truth; external GL imports are evidence and
reconciliation inputs, not override authority. Live external GL posting remains disabled in the
shared service until a separately approved adapter and release gate explicitly supports publishing
Meridian-owned ledger entries, so browser and WPF clients inherit the same guarded export and
read-only reconciliation posture.
Shared accounting configuration and manual journal entry services also provide durable file-backed
fallback stores under the resolved workstation data root. `FileAccountingConfigurationStore`
persists chart accounts, templates, posting rules, saved rule test cases, and accounting action audit events at
`workstation/accounting/accounting-configuration.json`, while `FileManualJournalEntryDraftStore`
persists draft and submitted manual journal records at
`workstation/accounting/manual-journal-drafts.json`. Manual journal drafts carry a shared
`ManualJournalEntryTypeDto` so accrual, prepaid expense, expense, amortization, deferral,
reclassification, reversal, capital-call, distribution, subscription, redemption, LP-transfer,
management-fee, and general adjustment workflows persist as typed accounting records instead of
client-local labels. Accounting configuration mutation requests can carry `LedgerBookId`, and the
shared in-memory/file-backed stores key workspaces by fund profile plus ledger book so book-scoped
charts, templates, posting rules, saved tests, promotion approvals, and audit rows do not overwrite
or leak into another book under the same fund. Accounting Rules Studio dry-runs merge rule scope, event dimensions,
counterparty, generated-line dimensions, allocation target dimensions, and external GL keys into
generated posting lines before returning preview results, so browser and WPF clients do not
reconstruct dimensional accounting context locally. Text rule predicates without retained
comparison values fail closed as validation issues during dry-run preview and activation so
incomplete operator predicates cannot accidentally select a posting rule. Rule staging, dry-run
preview, and saved regression execution remain available to ledger operators, while posting-rule
promotion approval is a governed release gate that requires `AdminMaintenance`. Private-capital entry types require shared treasury ledger context before
approval submission: effective date, idempotency key, fund-event type/id, and capital account
context, with optional investor, payment-intent, and settlement references. Stronger host
registrations can still replace those stores, but browser and WPF clients should consume the shared
services instead of keeping process-local accounting configuration, treasury-context validation, or
draft state. Manual journal save, validation, submission, evidence attachment, and lifecycle
requests can carry the selected `LedgerBookId`; the shared service rejects a requested book that
does not match the retained draft before normalizing, saving, approving, attaching evidence, or
applying correction transitions. Manual journal evidence attachment is exposed through
`/api/ledger/journal-entry-workbench/evidence`; it requires the current draft version, validates
line-scoped attachments, writes `manual-je.attach-evidence` audit, and refuses posted, reversed,
rebooked, or close-locked entries so evidence changes happen before posting or through correction
drafts. The same workbench service now enforces request-level period-lock posture across save,
submit, evidence attachment, and lifecycle mutations; validation remains read-only and returns a
critical `manual-je.period-locked` issue for locked periods. When a manual journal uses a
GUID-backed ledger period id and the ledger journal store is registered, validation also verifies
that the period exists, remains open, and belongs to the selected ledger book before approval or
lifecycle promotion can proceed. It also normalizes
`LedgerDimensionSetDto` on manual journal headers and lines, carrying fund/entity scope and
external GL dimensions from the header while enriching line dimensions from entity, instrument,
tax-lot, and cost-center fields. `ManualJournalEntryWorkbenchService` now loads retained manual JE
drafts and, when registered, `ILedgerJournalStore` posted journals plus `ReportPackWorkflowService`
workflow records, then delegates private-capital activity projection semantics to Financial
Operations. Posted ledger-backed fund events win over same-id drafts, and the projection keeps
fund-event rows, ordered capital-account subledger entries, ledger-impact rows, capital-account
aggregates, published report-output state, signed net activity, and incomplete-context warnings
Financial Operations-owned for both browser and WPF consumers. Posted
capital-account subledger rows and ledger-impact account scopes are reconstructed from the
shared ledger journal evidence. Close-management endpoints under `/api/ledger/close-management/*`
adapt Financial Operations close-plan behavior for browser and WPF consumers: the period-plan route
projects checklist dependencies, approval sign-offs, materiality policy, late adjustments, period
  lock posture, and validation issues from Operations Continuity, while the task sign-off and
  late-adjustment routes retain evidence-backed task, review request, approval, or rejection
  decisions without mutating posted journal entries. Assistant or automation-origin close sign-off
  and late-adjustment commands are rejected before retaining decisions. Task sign-off evidence must identify
the close task, sign-off role, and workflow or close period on the same retained artifact. Late-adjustment request evidence must identify
the journal entry, workflow, or close period, and late-adjustment review evidence must identify the
retained request, journal entry, workflow, or close period. Task sign-off requests are rejected when a
projected dependency task has not been signed off or when the requested role is outside the
projected role-scoped sign-off requirements, so browser and WPF clients cannot bypass close
checklist order or approval-count governance.
The `/api/ledger/reports/accounting-package` route adapts the Financial Operations report-package
service, returning the shared financial statement package, investor capital statement, realized
gain/loss report, NAV package, line-level provenance rows, certification, validation issues, and restatement workflow metadata
without requiring browser or WPF clients to reconstruct accounting report state locally. The
  companion `/api/ledger/reports/accounting-packages` route lists retained package history by optional
  fund profile, period, and `ledgerBookId` filters from the same shared service; retained package
  identifiers include ledger-book scope when available so book-native report packages do not
  overwrite each other for the same fund period. The
`/api/ledger/reports/accounting-packages/{packageId}/exports/{artifactId}` route returns the
retained controlled export-artifact manifest with evidence, content hash, certification state, and
live external posting disabled. The
`/api/ledger/reports/accounting-package/certification` route requires ledger mutation permission
and forwards the shared certification request to the same service so endpoint adapters resolve the
authenticated actor, reject assistant or automation-origin certification, return 404 for missing
packages, require one retained approval artifact that references the package id, certification id,
and exact period, and fail closed on draft, duplicate, stale close-plan, or critically blocked
retained packages.
ledger-owned capital-account impacts, so a posted fund event that touches multiple capital accounts
keeps those account/investor identities visible in shared Accounting, browser, and WPF projections
instead of collapsing every row to the event-level fallback account. When a posted fund event
matches a governed report-pack workflow, the projection maps report-pack id, workflow state,
retained publication manifest details, publication evidence hash, signer/timestamp, and matched
report-line provenance count into the private-capital report-output row.
Posted report-output rows resolve capital-account identity from the report-pack target account
first, then retained line provenance that points to one capital-account impact; unresolved
multi-account outputs stay explicit as `capital-account:unassigned` instead of being attached to
the event-level fallback account. Report-output rows also carry server-built report-output,
fund-event record, capital-account subledger, evidence-packet, and approval routes, so reporting, browser, and WPF
clients can move from a statement line back to the same fund-event ledger record without rebuilding
private-capital URLs locally.
Report-output rows also emit server-owned readiness label, reason, next action, and next-action
route from the same validation and publication state that drives `IsReportReady`, so clients can
explain report-output posture without inspecting issue codes.
Published workflow records can also match through retained publication/restatement/rejection
evidence pointers to the fund-event id, journal id, or ledger-entry id, so an event-level evidence
packet can keep the report output attached even before line provenance is populated.
Accounting configuration posting rules now carry effective dates, priority, dimensional scope,
flat and grouped `All`/`Any` event conditions, formula/allocation metadata, generated posting lines, version history, and
promotion-approval metadata in the shared ledger contracts. `/api/ledger/accounting-configuration/posting-rules/dry-run`
evaluates those rules without posting, returning the selected rule, generated lines, explanations,
and validation issues for browser and WPF clients. Dry-run previews also fail closed with the shared
`posting-rule.priority-conflict` critical issue and no selected generated posting preview when
multiple matched rules share the selected top priority, so ambiguous priority resolution is visible
before activation or promotion. If effective candidates exist for the source event but dimensional
scope or rule predicates reject every candidate, the dry-run returns `rule.no-candidate-match` and
no generated posting preview so operators can repair event data, thresholds, or scopes before
promotion or posting. Allocation rules now split generated/template
posting lines by static or formula-backed positive weights, round each allocation to cents, place
any residual on the final allocation line, and merge allocation target dimensions into the generated
line preview. Rule scope matching includes external GL dimension key/value pairs, and rule
conditions can address those same values with bare keys or explicit aliases such as
`externalGl.Department` and `gl.Department`, so department, class, book, or other external GL scoped
rules do not match dry-run events from a different external dimension. Dry-run and workspace
validation fail closed on duplicate active chart account paths,
missing or duplicate generated posting
line ids, missing or archived generated posting account references, missing or duplicate allocation ids, event-specific non-positive allocation weights, missing or duplicate condition ids, malformed amount-threshold predicate values,
inverted amount-between ranges, duplicate formula ids, missing generated-posting formula references, missing allocation
formula references, and formula-backed allocation weights that resolve non-positive while surfacing
rule-match validation issues for operator repair. Condition groups evaluate as required
`All` or `Any` predicate sets and surface service-owned explanations when a required group does not
match.
`/api/ledger/accounting-configuration/posting-rules/candidates` turns the same dry-run result into a
non-posting governed journal draft candidate by delegating to Financial Operations. The endpoint
returns selected rule/version metadata, generated posting lines with dimensions, retained evidence
links, blocking/non-blocking issues, and an approval-gated posting command when validation passes;
browser and WPF clients must still route posting through the JE lifecycle.
`/api/ledger/accounting-configuration/posting-rules/tests`
executes ad-hoc or saved non-posting regression test cases through the same dry-run engine and returns
per-case pass/fail assertion evidence for selected rule, selected rule version, balanced posting, expected generated posting lines,
generated line dimensions, and expected issue-code
checks. `/api/ledger/accounting-configuration/posting-rules/test-cases` saves workspace-owned
regression cases with mutation permission, audit evidence, and retained evidence links on the test
case itself; activation and promotion readiness validate that saved-case evidence references the
test case id, expected rule id, or expected rule version. Posting-rule upserts retain
service-owned version rows when a rule is created or its `RuleVersion` changes, capturing actor,
timestamp, mutation evidence, and the current promotion approval snapshot.
`/api/ledger/accounting-configuration/posting-rules/promotion-approvals` approves only the current
retained rule version, rejects stale-version approvals, requires approver notes plus retained
approval/review evidence that references the retained rule, current version, and approval id in
the same retained artifact,
requires retained saved regression coverage for that current version, runs those saved tests through
the dry-run assertion engine, rejects assistant or automation-origin approval requests before
mutation, updates the matching version snapshot, and appends
`posting-rule.promotion-approve` audit evidence. Replaying the same approved promotion id is
idempotent and does not append a second audit event; submitting a different approval id for an
already approved rule version is rejected so the retained approval lineage cannot be overwritten.
  Configuration activation
  now evaluates the same readiness path: the activation endpoint is an `AdminMaintenance` release
  gate, rejects assistant or automation-origin release requests, and requires retained activation,
  approval, certification, sign-off, or review evidence before `configuration.activate` can be
  audited; promotion-gated posting rules require approved promotion
  evidence plus saved current-version test coverage with retained regression evidence that identifies
  the test case, expected rule, and expected version in the same artifact, same-priority overlapping
  effective rules are rejected as deterministic selection conflicts, and any failing or evidence-weak
  saved regression case blocks activation before a
`configuration.activate` audit event is appended.
Manual journal lifecycle actions are exposed
through `/api/ledger/journal-entry-workbench/lifecycle-action`; submit, approve, post, reject,
close-lock, reverse-draft, and rebook-draft transitions stay server-owned, require human action
origin, retain `JournalEntryLifecycleTransitionDto` rows, and append accounting action audit
evidence. Direct submit-approval requests use the same retained submit transition. Lifecycle validation remains a non-release check, while
review/release transitions require `AdminMaintenance` at the endpoint boundary. Approval and
rejection requests require reviewer notes plus retained approval, rejection, sign-off, or review
evidence that identifies the journal entry or accounting period. Posting requests require operator
notes plus retained posting, approval, certification, sign-off, or review evidence with the same
journal or period provenance. Close-lock requests also require retained close, period-lock,
sign-off, certification, approval, or review evidence that identifies the journal entry or
accounting period.
Draft save and approval submission remain mutable only for `Draft`, `NeedsFix`, and `Rejected`
entries; submitted, approved, posted, reversed, rebooked, and close-locked entries reject direct
draft edits or duplicate submission and must use lifecycle actions or correction workflows.
Rejected and needs-fix entries that are reworked through draft save clear stale submission,
approval, posting, and close-lock fields before returning to editable draft state, while retaining
their lifecycle transition rows and audit trail.
Reversal and rebook requests create separate correction drafts instead of mutating the posted entry,
and require retained reversal, rebook, correction, approval, or review evidence before the original
posted entry transitions; that evidence must identify the posted journal entry or accounting
period. Direct reversal/rebook is available only while the entry is `Posted`;
`CloseLocked` entries reject direct correction actions and must use governed late-adjustment or
restatement workflows.
`/api/ledger/private-capital/activity` can also be filtered by `fundEventId`, `capitalAccountId`,
`investorId`, and `paymentIntentId`; the endpoint returns a recomputed slice so report-package and
payment-intent drill-throughs retain matching events, subledger rows, ledger impacts, report
outputs, fund-event ledger records, payment-intent workflows, counts, and net activity without
leaking unrelated capital-account rows. Account, investor, and payment-intent filters retain a
posted fund event when any child capital-account subledger row, GL impact, report-output row, or
payment-intent workflow matches, then derive filtered account totals and net activity from the
retained subledger rows before falling back to event-level rows. Each fund-event ledger record is
rebuilt server-side through the Financial Operations-owned `PrivateCapitalFundEventLedgerRecordBuilder` from the filtered
projection rows so browser and desktop clients receive a
single event-level view containing event state, subledger impact, GL impact, evidence, approval,
and report-output posture. Those rows also carry top-level journal, memo, gross/net activity,
capital-account opening/ending net activity aggregated across all matching child subledger rows,
payment/settlement, canonical activity route,
event evidence-packet route, approval id/route when an approval exists, child-count, primary
report-output route/workflow, publication manifest, provenance fields, server-derived
readiness label/reason, and next-action route from the grouped source rows so filtered
drill-throughs stay useful without client-side stitching. Event and capital-account subledger
records also include classified evidence categories for source support, capital-account subledger
support, ledger impact, approval state, and report output readiness, giving browser and desktop
clients a single server-owned evidence-coverage model instead of a flat link list that every client
must reinterpret.
Payment-intent evidence is also server-owned: fund-event and capital-account projections surface
payment intent readiness, retained cash evidence, settlement references, and a dedicated
`payment-intent` evidence packet with requester, payee, account scope, business purpose,
approval policy, retained source evidence, expected-cash, bank/cash evidence with retaining
operator attribution, reconciliation,
audit-history, and execution-deferred nodes. Bank/cash artifact refs carry capture channel/source
reference plus extracted amount, currency, date, settlement reference, transaction id, and status
fields validated against the expected cash movement, and explicit payment/settlement evidence links
must match the current intent before they satisfy readiness or appear as workflow bank/reconciliation
proof. Retained return, reversal, rejection, void, or failed-payment evidence is classified as
cash-evidence when it references the current intent. This layer documents cash-movement intent and
proof; live treasury payment execution remains outside the shared UI service boundary.
Projections expose the event-level record collection as an empty list when
no fund events qualify, keeping browser and desktop consumers on the same non-null contract.
Posted private-capital fund events inherit the owning ledger book's base currency through
fund-event rows, ledger impacts, capital-account subledgers, and report outputs so multi-currency
fund books do not appear as USD-only activity after posting.
Report-output rows keep `IsPublished` separate from `IsReportReady`: a retained publication can
remain visible for audit, but readiness stays false until the linked fund event is posting-ready
across approval, retained evidence, GL impact, capital-account impact, and report-specific
publication or line-provenance evidence. Fund-event source evidence remains visible on the row for
audit context, but it does not satisfy report-output readiness by itself. Fund-event and
capital-account report-output evidence categories require at least one report output, every linked
report output to be report-ready, and at least one retained report evidence link before the
evidence lane is complete.
`/api/ledger/private-capital/fund-event-record` returns one of those shared event-level records
directly by `fundEventId`, including child rows and readiness posture, and returns 404 when the
fund-event id is absent instead of sending clients an empty aggregate to interpret.
`/api/ledger/private-capital/fund-event-command-center` wraps the same source-backed event record
with command-center lanes for evidence, workflow, ledger impact, capital-account impact, treasury
expectation, reconciliation, report usage, delivery, tax support, and audit history plus retained
support packages. The support package rows cover operational evidence, payment intent, report
output, delivery, tax support, and audit support readiness, with review-required actions when
retained package evidence is missing. It requires `fundEventId`, returns 400 when the selector is
missing, and returns 404 when the event is absent.
The Financial Operations-owned `PrivateCapitalCapitalAccountSubledgerBuilder` also groups those event-level records with the
running capital-account subledger, ledger impacts, report outputs, retained evidence, approval
queue, posted/published counts, and validation issues into a capital-account-level record.
The subledger builder now emits the same readiness enum, label, reason, next action, and
next-action route shape used by fund-event ledger records, rolling up blocked, evidence-missing,
approval-pending, posting-review, report-review, ready, and published states without client-side
recalculation.
`/api/ledger/private-capital/capital-account-subledger` returns that grouped subledger directly by
`capitalAccountId`, returning 404 when the capital account is absent and 400 when multiple investor
or currency subledgers match until the caller also provides `investorId` and `currency`.
`/api/ledger/private-capital/report-output` returns one private-capital report-output row by
`reportOutputId`, `reportPackId`, or `fundEventId` with optional capital-account and investor
filters, returning 404 when no report output matches and 400 when the selector is missing or still
matches multiple outputs.
`CapitalAccountWorkbenchService` composes the shared private-capital activity projection with
retained report-pack workflow records to produce the narrow `/api/ledger/private-capital/capital-account-workbench`
surface. It exposes investor-level capital-account evidence, governed allocation policy trace
fields with effective windows, approval references, replay inputs, statement publication,
restatement changed-line lineage, and audit-support drill-through rows without creating a
browser-only or WPF-only read model.
This private-capital slice is intentionally limited to the unified fund-event ledger,
capital-account subledger, retained evidence, approval, governed report-output, and readiness
reason/action projection. Do not expand UI Shared into cap-table administration, broad LP portal,
native live-payment execution, full forecasting, or Backtesting Studio behavior unless a later
roadmap item reopens those lanes.
The evidence fabric also resolves `private-capital-fund-event` subjects from the shared manual
journal entry workbench projection. Subject discovery enumerates retained workbench draft scopes
and posted ledger-book fund scopes first, then loads each fund-scoped projection so non-default
funds such as `fund-alpha` appear in the evidence subject list instead of only resolving by direct
event id. Its packet uses the `private-capital-fund-event-review` template to require linked
fund-event state, retained evidence, approval state, capital-account subledger impact, GL impact,
and report output before the event-level evidence graph can be treated as complete. Report-output evidence artifacts prefer the direct
`/api/ledger/private-capital/report-output` route when the shared row provides it, keeping
evidence graph drill-through aligned with the endpoint and workstation review surfaces.
Evidence packets and graph responses also calculate the v0.18 Operational Evidence Graph
proof-chain layers server-side, mapping packet nodes to Source, Normalization, Reconciliation,
Ledger, Capital accounts, Close, Reporting, Delivery, and Audit coverage. Browser and WPF clients
should render that shared coverage instead of deriving their own lifecycle labels.
Private-capital fund-event and payment-intent evidence packet, graph, validation, and manifest
routes honor `ledgerBookId` query scope and propagate it through `EvidenceSubjectDto`, so
book-scoped accounting surfaces do not fall back to fund-wide activity when loading evidence.
Generated reporting-run deliveries also retain a `ReportingRunDelivery` evidence packet with
recipient scope, source report-writer artifacts, delivery artifact checksums, dataset/template
version, and request history so scheduled no-code report packs have the same audit-facing delivery
lineage as published governed report packs. When the generated run manifest carries a resolved
`ReportAccessPolicyDto`, the evidence packet uses that policy for its entitlement scope so
restricted group/company and private scheduled report-writer packs do not lose their audience lock
at delivery time. Delivery packages expose structured access links for
the token-gated email link or secure portal route, operator route, retained manifest path, and each
artifact download; schedule delivery-plan projections carry the latest package links forward so
operator clients can render the actual retained delivery options without parsing secure-link strings.
Their generated CSV/HTML/PDF artifacts include
manifest-owned report-writer grid metadata rows plus any manifest-owned rendered grid columns,
rows, warnings, lineage, data-dictionary fields, and validation checks. Generated XLSX packages include a
`ReportWriterGrids` worksheet with grid id, title, kind, artifact URI, dimension count, metric
count, formula count, and validation summary counts plus `ReportWriterGridRows`, `ReportWriterDictionary`, and
`ReportWriterValidation` worksheets for rendered row values, output field lineage, generated field
flags, and row-count/source-lineage checks. Reporting
schedules can persist optional dataset rows and pass them into scheduled runs, allowing scheduled
no-code packs to deliver source-backed pivot, Top-N, contribution, and formula output when a
governed dataset snapshot is supplied. Individual retained grids can also be exported as JSON, CSV,
XLS/XLSX, or PDF from the same policy-aware run artifact. `ReportWriterDatasetSourceService` also resolves retained
portfolio cuts, Top-N/contribution analytics, and cross-fund consolidation rows when ad-hoc or
scheduled report-writer execution omits dataset rows, so approved templates can run from retained
Meridian evidence instead of requiring pasted operator data.
The shared workflow library owns close-lane command routing as well: `AccountingReviewOperationsContinuity`
targets `OperationsContinuity` and `AccountingReviewCloseReadiness` targets `OperationsClose`, with
route metadata tied to the operations-continuity API. Browser and WPF clients should consume those
target tags instead of inventing client-local close-workflow routes.
The same shared library also exposes the design-document `Primary Operator Workflow` sequence:
`Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, and `Report`. Keep that sequence in
`BuiltInWorkflowDefinitionProvider` aligned with browser shell continuity and WPF launch targets so
client shells do not maintain separate primary workflow catalogs.
`ExtensibilityCatalogService` aggregates shared extensibility registrations. Built-in providers
adapt workstation workflows and actions, governed reporting templates, Identity role/permission
profiles, accounting configuration, posting-rule mappings, provider connection lifecycle, and draft
tenant-template/domain-extension seams into `ExtensibilityRegistrationDto` records. Browser and WPF
clients should consume that catalog instead of inventing local extension vocabularies; registrations
configure routing, review sequence, evidence expectations, scoped authority, templates, mappings,
and ledger controls without owning domain writes.
`ExtensibilityConfigurationService` persists tenant-template bundles and activation attempts through
`IExtensibilityConfigurationStore`. The default workstation registration uses
`FileExtensibilityConfigurationStore` under tenant-specific
`workstation/extensibility/tenants/{tenantId}/configuration-bundles.json` snapshots resolved from
the session-derived workstation tenant context.
Activation fails closed when a tenant template or domain extension attempts to override core object
identity, audit trail, or financial calculation integrity, and when bundled configuration envelopes
are not already approved with retained approval actor/timestamp evidence. Successful activation marks
bundled configuration envelopes `Active` with the session-derived actor and linked audit event
metadata.
The workstation API exposes that catalog at `/api/workstation/extensibility/catalog` so browser and
WPF clients can consume one shared stable-core and configurable-layer registry. Tenant-template
bundles are listed and saved under `/api/workstation/extensibility/tenant-templates`, activation
runs through `/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activate`, readiness
is exposed at `/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/readiness`, and
activation history remains readable at
`/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activations`.
The built-in `accounting-records-evidence-review` workflow owns the v0.15 accounting-records
review path across retained source records, normalized activity, reconciliation cases, ledger
evidence, approvals, document attachments, export manifests, and report-pack/restatement lineage.
Browser and WPF command surfaces should consume that shared workflow instead of creating separate
accounting-record launch lists.
`WorkstationOperationsJsonContext` includes the accounting-record summary, evidence-category, and
private-capital shadow NAV tie-out DTOs so shared workstation endpoints can serialize the same
Financial Operations payloads that desktop clients round-trip from `Meridian.Contracts.Workstation`.
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
persisting it back to the approved template. Registry normalization preserves the authored
drag-and-drop order for grids, row fields, column fields, metrics, formulas, and filters while
trimming duplicates, so preview and approved runs use the same layout an operator saved. Rendered
grid responses expand pivot column fields into first-observed cross-tab metric columns instead of
flattening them into extra row groups, while formulas continue to evaluate against each row's
aggregate metric totals. They also carry input/output row counts, filtered-input row counts, source
fields, metric source mappings, formula dependencies, and saved filter lineage so browser and WPF
previews can display the same audit trace as retained exports. Template governance validation now
blocks report-writer formulas that reference unknown metrics/formulas, unsupported `total(...)`
fields, or self/forward/circular formula dependencies before those templates can enter review or
approval, while recognizing supported helpers such as `safeDivide(...)`, `percent(...)`,
`basisPoints(...)`, and `round(...)` as functions instead of missing row fields.
Browser and WPF clients should render that shared
template state instead of treating built-in templates as the full authoring workflow.
Template definitions and report-pack workflow records now carry shared access policies for
user-locked, restricted user/group/company, and company-wide report audiences. `ReportAccessPolicyEvaluator`
normalizes and validates those policies, `/api/fund-structure/reporting/templates*` filters and
guards template reads/renders with the session actor, role, role-profile group, company id, and
admin override, report-pack lifecycle and delivery-history/attempt endpoints enforce the same policy
before mutating or exposing package state, and the workstation Reporting payload filters restricted
template and report-pack rows before distribution and recent-run aggregates are projected.
`GovernedReportingTemplateCatalog` adapts the latest approved registry template versions into
`IReportingTemplateCatalog`, allowing ad-hoc runs and due-schedule orchestration to execute approved
custom report-writer templates through the same run-store path as built-in reports. The
`/api/fund-structure/reporting/runs` command also evaluates the template access policy before
orchestration, so a direct run request cannot bypass the list/render filters for private or
restricted report templates. Reporting schedule upserts and manual schedule runs apply the same
governed template access check, so locked custom templates cannot be scheduled or run from an
existing schedule by callers outside the owner, user, group, or company policy. Reporting payload
reads, including the embedded `FundOperationsWorkspaceReadService` reporting summary behind the
fund workspace view, also filter schedule rows, `scheduleDeliveryPlans`, and `DeliveryAttempts`
through the visible template/workflow set for the current `ReportAccessQueryContext`, so
unauthorized users cannot infer locked schedule recipients, delivery modes, due dates, package
links, or delivery status from the read model.
Approved custom report-writer templates carry their saved grid definitions into the reporting
catalog as well. Generic ad-hoc and scheduled runs now retain `report-writer://.../grids/{gridId}`
artifacts and audit the grid count, so pivot, Top-N, contribution, and custom-formula grids remain
visible in run evidence after publication or schedule execution instead of existing only in the
template preview response. Reporting manifests and workstation run projections also retain the
resolved report-writer dataset source id, label, row count, and generated-grid validation summaries
when ad-hoc or scheduled automation uses a governed source-backed dataset. `ReportWriterGridArtifactService` serves those retained grids through
`/api/fund-structure/reporting/runs/{runId}/report-writer-grids/{gridId}` as JSON by default, with
`format=csv`, `format=pdf`, and `format=xlsx` downloads for operators that need direct grid extracts
or allocator-ready grid previews from a governed run artifact; `format=xls` and `format=excel` are
compatibility aliases for the same canonical `.xlsx` workbook. JSON, PDF, and XLSX downloads enrich
the retained render with a data dictionary or lineage summary plus validation checks for row-count,
column coverage, source-field lineage, and render warnings, while CSV stays a flat grid extract for
downstream ingestion. Those retained
grid downloads evaluate the source template access policy at read time, so private, restricted
user/group/company, and company-wide report audiences are enforced for JSON, CSV, and XLSX artifact
retrieval.
`ReportPackRunReadService` uses the same registry list when it is registered, so Reporting payloads
include custom template drafts, in-review records, approvals, latest-approved status, and
report-writer grid metadata alongside built-in templates. For custom templates, that projection
keeps row fields, column fields, metrics, formula expressions, Top-N, sort settings, and saved
filters in the shared payload so browser and WPF surfaces can render no-code report-writer canvases
without client-local template parsing or recalculation. The projection also attaches a
source-backed report-writer field catalog from `ReportWriterDatasetSourceService`, including
dimension, metric, and generated-field entries with dataset and data-type metadata, so no-code
authoring palettes can offer the full retained portfolio, analytics, and consolidation dataset
instead of only fields already placed in a saved grid. Contribution grid renders include the
server-generated `contributionPercent` and `contributionAbsPercent` columns, allowing clients to show
signed and absolute percentage-of-P&L breakdowns for offsetting winners and laggards without
recalculating contribution math.
`ReportWriterDatasetSourceService` also projects those retained report-writer datasets as
`reportWriterDatasetSources` for both `ReportPackRunReadService` and
`FundOperationsWorkspaceReadService` Reporting summaries. The projection includes a combined source,
dedicated portfolio-cut, Top-N/contribution analytics, cross-fund consolidation sources, and a
certified operational data-mart source that enriches retained rows with row-lineage keys, lineage
manifest pointers, evidence-index links, source run ids, validation state, reconciliation state,
certification posture, and permitted consumers. Each source carries its own field catalog plus
source-backed rows that ad-hoc previews can post to the shared render endpoint, so clients only
fall back to synthetic preview rows when the operator intentionally selects a fixture or pasted
dataset.
`ReportingRunCommandService` and `ReportingScheduleService` honor the same source ids through
optional `datasetSourceId` request fields, resolving retained portfolio-cut, Top-N/contribution, or
cross-fund rows server-side for approved report-writer templates when explicit `datasetRows` are not
provided.
The template projection also carries registry-owned audit and version-control metadata, including
based-on version, created/updated/submitted/approved/rejected actors and timestamps, decision
rationale, approval reference, validation issues, and retained template audit events, so clients do
not reconstruct governance lineage from display labels.
Generic Reporting orchestration runs and governed report-pack workflow records also share one
operator read model here. `FileReportingRunStore` persists `ReportingOutputManifest` plus audit
trail snapshots for scheduled/ad-hoc Reporting runs, `FileReportPackWorkflowRecordStore` persists
report-pack workflow records after create, submit, approve, publish, reject, restate, and archive
mutations, and `ReportPackRunReadService` projects both sources into the shared
`WorkstationReportingPayload`. Browser and WPF Reporting surfaces should consume those recent-run
rows for true template, schedule, attempt, approval, publication, evidence-bundle, restatement, and
drilldown status instead of reintroducing fixture rows in workstation bootstrap payloads. Generic
run audit trails are exposed through `/api/fund-structure/reporting/runs/{runId}/audit` with the
same governed template access policy used by retained grid artifacts, so private or restricted run
actors, timestamps, notes, and report-writer dataset source evidence do not leak through audit
drilldowns.
The same payload also carries `AccessAudit`, an aggregate user/group/company access summary with
visible and hidden counts for templates, report packs, schedules, delivery attempts, and structured
exports plus generic denial reasons. Clients should render that service-owned proof when explaining
user-locked or restricted report visibility instead of probing hidden report identifiers.
When retained workflow records are present, the same payload exposes `SelectedFundProfileId` so
clients can post governed report-pack commands against an explicit fund context.
Those recent-run rows also include typed drilldown links and next-action references for evidence,
approval submission/review, publication, release review, restatement, and archival work so clients
can render clickable routes while preserving reference-only POST/action metadata.
When that retained fund context is available, `ReportPackRunReadService` also projects
`structuredExports` descriptors for regulatory trial-balance, warehouse ledger-fact, investment
portfolio-cut, Top-N/contribution analytics, and cross-fund consolidation outputs. Those descriptors
reuse the governed `/api/workstation/reporting/structured-exports/{exportId}` export route, carry
retained-path, schema, row, field, source-count, evidence, readiness, and version metadata, and stay
absent when no source-backed fund context can prove the downstream export target. The workstation
route returns the same Reporting-owned payload as JSON and can emit schema-ordered CSV or XLSX files
from retained report-pack runs, distributions, portfolio cuts, analytics rows, and consolidation
rows without requiring a separate Fund Operations workspace projection.
Fund-operations Reporting payloads now include portfolio reporting cuts derived from the same
shared cash/financing, strategy-run portfolio, account, and NAV attribution state used by
Accounting. `FundOperationsWorkspaceReadService` emits consolidated fund, strategy, and user-tag
rows with exposure, cash, P&L, shadow-NAV, variance, source-count, and version-stamp fields so
browser and WPF clients do not recalculate report cuts from separate portfolio APIs.
The read service also projects `livePortfolioViews` from those same cuts. Each row points to the
shared `/api/workstation/portfolio/summary` route, preserves source-backed freshness state, carries
liquidity text, and links single-run strategy cuts to `/api/portfolio/{runId}/cash-flows` for
cash-ladder evidence. Fresh source snapshots inside the server live freshness window are emitted as
`LiveLinked`; older retained snapshots remain `SourceBacked` until they cross the 24-hour stale
threshold. Rows fail closed as `Blocked` when no fund account or portfolio run source backs the
reporting view. Each row also includes `FreshnessPolicy` evidence with the evaluated-at timestamp,
source age, live/stale thresholds, classification booleans, and reason text so browser and desktop
clients can explain freshness decisions while keeping the classification owned by the shared read
service.
It also projects `pnlSlices` for daily, weekly, monthly, and yearly P&L from retained portfolio run
timestamps. Each row carries realized/unrealized/current/prior/change values, source counts,
readiness text, a shared `/api/workstation/reporting?pnlSlice=...` route, and deterministic version
stamps; windows with no current source run fail closed as blocked instead of displaying synthetic
period P&L.
It also projects `analyticsRows` for Top-N winner, Top-N laggard, and contribution reporting from
retained portfolio position P&L. Rows are grouped by security, strategy, and asset class, include
contribution percent plus heat-map intensity, and expose shared
`/api/workstation/reporting?analyticsId=...` routes; workspaces without position-level P&L emit a
blocked analytics row instead of synthetic winners or laggards.
`FundOperationsWorkspaceReadService` also projects `crossFundConsolidations` from all active fund
accounts plus all fund-scoped strategy-run portfolio summaries. It emits company-wide, fund-level,
and legal-entity rows with exposure, cash, P&L, shadow-NAV, source counts, readiness text, and
deterministic version stamps; when no source-backed account or run data exists, the company row
fails closed with blocked readiness instead of synthetic consolidation values.
The same read service also emits structured export descriptors for regulatory trial-balance,
warehouse ledger-fact, investment portfolio-cut, Top-N/contribution analytics, and cross-fund
consolidation outputs, and serves `/api/fund-structure/reporting/structured-exports/{exportId}`
from the same source-backed workspace projection. The JSON payload includes stable column metadata,
culture-invariant string row values, data-dictionary fields, validation checks, readiness warnings,
retained-path metadata, and deterministic version stamps so downstream regulatory, warehousing, and
investment-decision consumers can ingest governed data without browser-local export shaping. Export
descriptors also include a retained manifest path, deterministic SHA-256 integrity hash, integrity
summary, and row-lineage count so downstream consumers can bind JSON/CSV/XLSX downloads back to a
stable manifest without deriving checksums from browser state. The
endpoint also accepts `format=json`, `format=csv`, or `format=xlsx` for every structured export;
`format=xls` and `format=excel` are compatibility aliases that return the same canonical `.xlsx`
workbook and MIME type instead of legacy BIFF content. XLSX workbooks retain Metadata,
DataDictionary, Validation, and RowLineage worksheets beside the schema-ordered data sheet,
including request actor, company, report groups, generated-at audit metadata, stable row keys, and
schema-ordered SHA-256 row hashes. JSON, CSV, and XLSX responses also set
`X-Meridian-Export-*` headers so flat file downloads are user/timestamp stamped without changing
the row schema. This includes the
data-warehouse ledger-facts descriptor whose default retained format is JSON, so users and
downstream jobs can download schema-ordered row files directly.
`FundOperationsWorkspaceReadService` also exposes built-in report branding themes and fund-profile
context through the reporting summary, validates custom branding overrides with normalized theme ids
and hex colors, echoes the selected theme from report-pack previews, persists the selected theme on
generated report-pack snapshots and manifests, and applies the same theme to generated HTML, PDF
text, and the XLSX `Branding` worksheet. That keeps logos, colors, footer copy, disclaimers, and
firm identity attached to the retained package artifact instead of the browser view.
`ReportPackWorkflowService` can also retain that selected `ReportBrandingThemeDto` on the
publication manifest, so downstream delivery packages use publication-approved branding metadata
rather than accepting delivery-time restyling. Published report-pack delivery XLSX artifacts keep
the delivery metadata sheet and add a dedicated `Branding` worksheet with the approved firm identity,
palette, logo URI, footer, disclaimer, and built-in/custom flag so recipients can inspect the
styling metadata in the delivered workbook itself.
`ReportPackRunReadService` derives `scheduleDeliveryPlans` from retained reporting schedules,
distribution policies, and delivery attempts so browser and desktop clients can show which scheduled
packs will deliver by email link, secure portal, evidence vault, or internal route without
duplicating delivery-policy logic.
Delivery-package access links also include an `artifact-xls` compatibility link whenever a retained
XLSX workbook artifact exists; the route uses `format=xls` while returning the canonical XLSX bytes
and MIME type, keeping scheduled PDF/XLS/CSV packs source-owned without legacy BIFF generation.
`FundOperationsWorkspaceReadService` emits live portfolio reporting view readiness blockers for
blocked or stale source snapshots, so clients render the same fail-closed explanation when tick-linked
reporting cannot prove current source evidence. The live-view projection also emits market tick
timestamp, tick age, safe tick sequence, provider label, tick freshness summary, and a live-link flag
so browser and desktop reporting surfaces can distinguish true live-linked portfolio telemetry from
retained source-backed snapshots. It also emits the freshness policy evidence used to classify the
view, including source age and the live/stale threshold seconds. Browser auto-refresh should call
the shared portfolio refresh route and then render these emitted fields; freshness classification
remains owned by this service.
`ReportPackDeliveryService` persists delivery and delivery-failure attempts under the resolved
workstation data root at `workstation/reporting/report-pack-deliveries.json`; published and
restated workflow records can therefore show real delivery history, retry attempts, recipient
state, and last-sent timestamps instead of static distribution placeholders. Delivered attempts
also receive deterministic package metadata with default PDF/XLSX/CSV artifacts, retained-package
paths, artifact SHA-256 checksums, artifact version stamps, publication evidence hashes, integrity
summaries, publication manifest fields, publication evidence links, retained line provenance,
publication-approved branding metadata, stakeholder delivery evidence packets, secure email-link or
portal URLs, service-owned delivery access/channel/download summaries, and delivery-mode inference
for portal, vault, and internal-route channels; callers can override the mode and requested formats
in `ReportPackDeliveryRequestDto`.
`ReportPackWorkflowActionRequestDto`, `ReportPackRestateRequestDto`,
`ReportPackPublishRequestDto`, `ReportPackDeliveryRequestDto`, and
`ReportPackDeliveryFailureRequestDto` carry reviewed-automation action origin metadata, and the
shared workflow/delivery services reject assistant or automation-origin approval, restatement,
archival, publication, stakeholder package creation, and delivery-failure recording before retained
outputs, changed report lines, or delivery attempts are written.
Email-link and secure-portal packages also retain
`ReportPackDeliveryNotificationDto` rows with recipient, subject, body, package href, token posture,
status, creation time, and expiry so operator clients can display the notification/outbox proof
without reconstructing it from the package URL. Token-gated package and artifact retrieval also enforce the
package `AccessExpiresAtUtc` timestamp when present, keeping email-link and secure-portal delivery
windows shared-service owned.
Generated package downloads rebuild CSV, XLSX, HTML, and PDF artifacts from that retained package
metadata, so recipients receive report-line provenance, publication evidence, selected branding, and
restatement lineage in the downloaded files instead of package identifiers only. XLSX packages keep
the Branding worksheet, while HTML and PDF package renderers apply the selected theme colors plus
recipient-visible firm, logo, footer, and disclaimer text so styled client packets are not metadata-only.
The shared delivery
evidence packet carries recipient and entitlement scope, approval chain, request history,
publication manifest, report-line provenance, retained delivery evidence, branding-theme package
contents, and restatement lineage for the Version 0.18 operational proof layer.
Schedule delivery plan rows project explicit readiness
blockers, the latest retained package artifact count, download summary, access-expiry timestamp,
access/channel summary, integrity summary, and the schedule or last-package branding theme, so clients can show
recipient/package readiness without fetching package manifests or inferring failure reasons from prose. Schedule plan
rows also carry the latest retained notification count and summary from the package evidence so clients can show whether
email-link or secure-portal notification proof exists without opening the package manifest. The same projection carries
latest retained report-writer dataset and generated/rendered grid summaries, so scheduled custom reports expose their
source-backed no-code output from the delivery-plan row. The shared read model marks a scheduled target unready when the schedule is inactive,
due without a successful retained package, configured with an incompatible delivery mode for its
channel, or missing one of the requested artifact formats from the latest package. Package manifest reads are token-gated by
`ReportPackDeliveryService`, using constant-time token comparison and shared GET routes for the
email-link package URL and `/portal/reporting/packages/{packageId}`. Email-link and secure-portal
package URLs render a recipient-readable HTML package page by default, including branding, package
integrity, artifact downloads, and notification proof; `format=json` returns the same token-gated
package manifest for API clients. Per-artifact package downloads
reuse the same package token and expose generated PDF/XLSX/CSV content through
`/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/artifacts/{artifactName}`,
so clients open artifact `DownloadRoute` values from the retained package instead of synthesizing
delivery file routes. `ReportingScheduleService`
persists operator-managed schedule records at `workstation/reporting/reporting-schedules.json`,
normalizes configured distribution targets, runs due schedules through `IReportingOrchestrationService`,
advances next due/as-of dates, and asks `ReportPackDeliveryService` to package the generated
reporting run for each target instead of falling back to an older published pack with the same
template id. The delivery service packages the scheduled run itself with token-gated
PDF/XLSX/CSV artifacts, a deterministic reporting-run package id, retained source artifact
provenance, report-writer dataset-source evidence, and `ReportingRunId`/template/schedule metadata
on `ReportPackDeliveryPackageDto`.
Those schedule records can also persist a selected `BrandingThemeId` or custom
`BrandingThemeOverride`; scheduled generated-run manifests and delivery packages carry that
normalized theme forward so recurring no-code report-writer deliveries preserve the same firm
identity, colors, footer, and disclaimer metadata as one-off branded report-pack generation.
Retained report-writer grid artifact downloads also read the manifest branding theme: PDF grid
artifacts use the selected firm name, primary color, logo reference, footer, and disclaimer, while
XLSX grid artifacts include a Branding worksheet with the normalized theme fields.
Generated scheduled-run manifests also carry the approved template access policy resolved through
the governed catalog, and `ReportPackDeliveryService` stamps that policy into
`ReportingRunDelivery.EntitlementScope` before storing the email-link or portal package.
`ReportPackRunReadService` carries the latest retained package entitlement back onto
`scheduleDeliveryPlans`, letting browser and desktop clients show the delivery audience lock beside
artifact integrity, retained download summary, access expiry, access/channel summary, notification proof, report-writer
dataset/grid evidence, branding, and access-link rows without opening the package manifest first.
Schedule run results return delivery attempts and warnings so operators can distinguish generated
reports from actually packaged email-link or portal deliveries.
`ReportingRunCommandService` also runs approved built-in templates on demand through the same orchestration and run-store seam,
returning `WorkstationReportingRunPayload` rows with ad-hoc trigger metadata, source-backed
report-writer dataset fallback, and review next actions. The fund-structure
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
activity, and source-record requirements from shared contracts. The same projection now carries
active Operations Continuity evidence packages and the cash, position, trade, income, MBS factor,
bank, and GL reconciliation lane summaries for Fund Ledger handoff, keeping WPF package and lane
rows source-backed by `IOperationsContinuityWorkflowService`. It also forwards active
Operations Continuity break cases, close-checklist tasks, and approval rows so browser and WPF
Financial Operations queues can show exception, close-support, and sign-off work from the same
shared lifecycle projection. The shared
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
The same normalization assigns each retained line a Financial Record Explorer id and
`/api/workstation/financial-record-explorers/{explorerId}` href so browser and WPF clients can open
the source-backed ledger, portfolio, or Security & Instrument Explorer without deriving routes.
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
When the active summary request carries an explicit `fundAccountId` query value, the Accounting
workspace also projects Operations Continuity financial-operations state from
`IOperationsContinuityWorkflowService`; legacy callers can still supply the account identifier in
`fundProfileId` until their shell context is upgraded. Receive-activity start, reconciliation
exceptions, approval history, close readiness, and retained evidence package posture remain
server-derived.
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
canonical subject metadata, while route-only artifacts stay as manifest references. Copied vault
artifacts also preserve optional capture channel/source details and extracted fields with
confidence, reviewer state, expected value, validation status, and linked record identity so
retained document evidence can prove how upload/email/API/portal/SFTP intake was reviewed against
expected records. `/api/workstation/evidence/vault/intake` is the shared API intake route for that
same vault model: it accepts bounded base64 document payloads, validates optional SHA-256
expectations, stores the artifact under `_vault`, writes a searchable manifest and vault identity,
and returns the retained artifact hash, capture metadata, extraction fields, and manifest route.
The vault write boundary rejects every retained artifact reference, copied or
route-only, that omits canonical subject linkage, lacks an addressable path/route, or uses
unsupported subject kinds, so retained statement/report/approval/screenshot artifacts cannot become
orphan evidence. This keeps packet/report/statement/screenshot/approval evidence retention
server-owned instead of client-local.
Manifest export also freezes grouped request lists and the underlying support request rows from
packet completeness: missing and stale evidence, blocking work items, and unresolved validation
issues are written into both the retained manifest and `_vault` identity index with target kind,
target id, highest severity, evidence kinds, and blocked outputs. This gives close, audit, tax,
report-package, and operator review workflows a durable request-list surface without rebuilding it
in browser or WPF clients. `/api/workstation/evidence/vault/request-lists` lists those frozen
request-list groups from retained vault identities with request-list, target, status, subject, and
limit filters, returning vault/manifest metadata beside the matching support request rows.
Retained vault bundles are also first-class Evidence Workbench subjects through the
`evidence-vault` subject kind: the shared contributor projects the retained manifest and each
copied artifact into the same packet graph, preserving hashes, source routes, and canonical subject
linkage for browser/WPF parity.
The shared Audit Trail Explorer service projects retained execution, promotion, order, control, and
Operations Continuity close/reconciliation/approval timeline records into contract-owned timeline rows and exposes `/api/execution/audit/search` with
server-side text, run, actor, symbol, action, outcome, correlation, normalized object, related
object, time-window, and limit filters. Timeline ordering is deterministic by occurrence time and
audit id, and text search includes related-object ids, evidence routes, source ledger, hash, and
ledger-status fields so close, reconciliation, approval, promotion, and control evidence can be
found through the same endpoint. Manual override audit rows resolve to operator-action objects keyed
by `overrideId`, while circuit breaker rows resolve to execution-control objects with direct control
routes, so operations review can distinguish who staged a live override from who opened or closed a
trading halt. Execution audit rows publish WAL-backed event hashes, and Operations Continuity rows
publish their retained previous/current hash chain, giving the shared audit explorer a v0.18
operator action ledger posture without moving write ownership into UI clients.
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
Report-pack delivery attempts are first-class Evidence Workflow Fabric subjects under
`report-pack-delivery`. The resolver lists and resolves attempts from `ReportPackDeliveryService`
or the persisted delivery record store, and the contributor projects recipient, package, artifact,
delivery-packet, publication manifest, line provenance, approval chain, branding theme, restatement
lineage, reporting-run source, and audit-history nodes into the same packet, graph, validation,
vault lookup, and manifest-export endpoints used by the browser and WPF surfaces.
Direct Evidence Vault intake now also promotes non-ready extraction fields into the same frozen
support-request and request-list index used by manifest export. Uploaded documents that arrive with
missing, blocked, stale, or review-required extracted fields retain the file bytes, capture metadata,
field review posture, support requests, and close/audit/tax/report-package/event request-list
grouping in the vault identity, so operators can follow up without interpreting raw intake manifests.
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
Fund-structure graph, advisory, fund-operating, accounting, ledger-mapping, and cash-flow read
routes also resolve the authenticated session before returning scoped structure data. Non-admin
callers must provide an explicit organization, business, client, fund, sleeve, vehicle,
investment-portfolio, legal-entity, or account scope that grants `ManageFundStructure`; unscoped
structure reads are reserved for `AdminMaintenance` so shared browser and WPF clients cannot
enumerate fund-structure records across tenant or scoped-access boundaries.
Auth endpoints expose `/api/auth/role-profiles` as the governed write path for custom authority
profiles. The Identity-owned file-backed role-profile store persists profile grants under the
storage root, merges custom profiles into `/api/auth/roles`, and feeds `UserProfileRegistry` so
configured `roleProfileName` accounts use the stored permissions after login. Auth account payloads
also pass through the Identity-owned company id attached to user profiles so Reporting access
policies can evaluate company principals from session state. Keep this module as the
endpoint/read-model adapter; do not reintroduce session, profile, role-profile, or company
persistence state here. Scoped access grant and revoke endpoints preserve explicit action-origin
metadata and return bad-request responses for assistant or automation-origin authority changes
before the Identity service writes a new assignment, revokes an assignment, or advances an
assignment version.
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
`/api/workstation/operations/private-capital-close-cockpit` exposes the v0.18 private-capital close
cockpit read model by adapting the Financial Operations-owned `IPrivateCapitalCloseCockpitService`.
That service composes Operations Continuity workflow detail with the shared private-capital activity
projection so fund/book/period/entity close lanes for data receipt, reconciliation, journals,
capital accounts, partner capital account tie-outs, expense/fee/allocation review,
management-company operating records, NAV support, valuation evidence, reporting, delivery,
close-package, and period-lock evidence remain server-owned for browser and WPF consumers. The
endpoint maps query scope and serialization only; readiness rules, approval-history projection,
partner tie-out blocking, expense/fee/allocation and management-company evidence failure posture,
NAV support package rows, administrator-versus-Meridian shadow NAV tie-out status, and retained
evidence posture live in
`src/Meridian.FinancialOperations`.
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
`/api/workstation/operations/continuity/{workflowId}/reconciliation/breaks/{breakId}/assign`
is the shared assignment/escalation adapter for Operations Continuity break cases. It trusts the
authenticated workstation actor, forwards owner, due date, escalation metadata, rationale, and
evidence to Financial Operations, and leaves source-owned validation and audit writes in the
workflow service.
`/api/ledger/periods/{periodId}/trial-balance`,
`/api/ledger/periods/{periodId}/trial-balance-report`, and
`/api/ledger/periods/{periodId}/pnl-summary` expose closed-period ledger reports from the shared
ledger book service. They keep trial balance, signed period-locked report totals, revenue,
expense, realized net income, accrual-basis adjustment impact, prior-period variance,
open-break count, and signoff posture server-derived for browser and WPF accounting surfaces.
The close command preserves the authenticated workstation actor and explicit action-origin
metadata; assistant or automation-origin requests are rejected by the shared ledger service before a
period-lock state, close event, or operator inbox sign-off item is written.
`/api/ledger/reports/trial-balance` and `/api/ledger/reports/pnl-summary` aggregate those
closed-period summaries across a selected book, fund, node, accounting basis, and date range for
regulatory, investor, and internal reporting surfaces. The cross-period report routes also accept
line-dimension filters such as `entityId`, `costCenterId`, `instrumentId`, and `externalGl.<name>`
so browser and WPF reporting surfaces can request fund/entity/cost-center/external-GL scoped
ledger slices without recomputing dimensional accounting totals locally.
Manual journal entry workbench routes under `/api/ledger/journal-entry-workbench*` persist draft
and submitted approval records under the resolved workstation data root. The shared service
validates GL account, balance, currency, Security Master, typed evidence attachments, private-capital
treasury context, and version state before save or approval submission. Draft save remains
permissive for in-progress work, but approval submission requires retained source evidence and, for
private-capital entry types, retry-safe fund-event/capital-account context so browser and WPF
clients do not present process-local accounting work as durable ledger evidence. Approval
submission also rejects assistant or automation-origin requests, and duplicate submission of an
already submitted or later-status entry, before the submitted record and audit event are written,
preserving reviewed automation as a draft/suggestion lane. The workbench
response includes the shared private-capital activity projection, which skips incomplete fund-event
drafts and surfaces ledger-impact, projection, and report-output readiness warnings instead of
inventing capital-account, GL-impact, or stakeholder-package rows. The read-only
`/api/ledger/private-capital/activity` endpoint returns that same projection directly, giving
Reporting, browser diagnostics, WPF, and later LP/audit review surfaces a first-class activity
read model without loading the manual journal authoring payload.
Lifecycle correction actions return the same typed reversal/rebook link objects on the transitioned
source entry and generated correction draft, and generated drafts carry a lifecycle transition row
back to the posted source. Endpoint callers should render those shared correction links instead of
reconstructing reversal or rebook lineage from audit action strings. Correction and close-lock
evidence must identify the correction or close-lock intent plus the journal entry or accounting
period on the same retained evidence artifact; split support and approval links are rejected before
the posted source is mutated. The lifecycle service validates the generated correction draft before
mutating the posted source entry, so invalid custom rebook lines cannot leave the source entry in a
corrected terminal state without a valid correction draft. Once the source entry is close-locked,
direct lifecycle correction actions are blocked and post-close changes must flow through
late-adjustment or restatement governance.
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
Fund-account read and mutation routes require scoped authorization for explicit fund, account, and
legal-entity identifiers before returning or changing account records. Unscoped account lists are
reserved for admin maintenance so workstation callers cannot enumerate fund accounts across tenant
or scoped-access boundaries by omitting filters. Account-scoped operational evidence routes,
including close readiness, balances, sync history, statements, positions, bank lines, and
reconciliation runs/results, resolve the owning account before returning or mutating data; write
payloads must match the route account id so callers cannot authorize one account in the URL while
submitting another account in the request body.
Operations Continuity reconciliation bridge payloads now also populate the shared cash, position,
trade, income, MBS factor, bank, and GL support lane summaries from provider-ledger reconciliation
detail, retained evidence, and open break materialization. Browser, WPF, and host callers should
consume these shared lane rows instead of deriving lane readiness from provider-specific detail
tables.
Operations Continuity workflow detail payloads also include the shared Financial Operations
operational dashboard summary derived by `src/Meridian.FinancialOperations`. The endpoint adapter
serializes the core Receive Activity, Match Records, Resolve Exceptions, Approve Results, Produce
Evidence, and Close Support metrics with retained evidence, route hints, and required actions so
browser and WPF clients do not build dashboard posture from local fragments.
`WorkstationWorkflowSummaryService` also emits a Financial Operations `Reviewed automation`
evidence badge from the contract-owned `OperationsReviewedAutomationSummaryDto` when workflow
detail provides it, falling back to source-backed intake, reconciliation, ledger, approval, and
close-package states for older payloads. Shared callers may surface automation review,
classification, extraction, matching, summary, draft, and flag posture as reviewed-output artifacts,
but approval, posting, publication, payment release, and evidence-retention decisions remain server-owned governed
commands. Operations Continuity endpoints trust the authenticated session for actor, reviewer, and
governed-admin fields while preserving explicit `ActionOrigin` values on material commands, so
Financial Operations can reject assistant or automation origins before ledger posting, approval,
close-package publication, governed reopen, or reconciliation case resolve/sign-off/reopen commands
mutate the operating record.
`WorkstationWorkflowSummaryService` also consumes Operations Continuity evidence-package summaries
when building the Accounting workspace home state. A closed workflow with a non-ready package,
including period-lock and reopen evidence, remains in review-required posture instead of being
reported as fully evidence-produced, and the shared evidence badges roll up package readiness plus
period-lock status for browser and WPF callers. Close-readiness blockers and non-ready retained
packages move the shared `Core flow` badge to `Close Support` so clients do not infer that final
evidence production is complete while close support work remains.
The same detail payload serializes Financial Operations evidence-package summaries for
accounting-record evidence, report-pack evidence, close-package manifests, and audit-support
packages. Shared endpoint callers receive status, category completeness, retained evidence counts,
route hints, and required actions without recomputing package posture from lower-level workflow
fragments.
The bridge also enriches Operations Continuity break cases with queue-backed owner, SLA due/state,
materiality, root-cause, approval posture, and blocked-output context when a governed
reconciliation case exists, and otherwise derives a conservative review/approval-required posture
from the retained reconciliation run so v0.18 exception controls stay server-owned.
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
