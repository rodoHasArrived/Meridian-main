---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-FINANCIAL-OPERATIONS
path: src/Meridian.FinancialOperations
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-16
---

# src/Meridian.FinancialOperations

## Purpose

Physical bounded-context module project for reconciliation, accounting records, payment approvals,
bank-transaction records, accounting-basis policy, ledger text-journal reporting, close workflows,
casework, and operational-record ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.FinancialOperations` - registered source module root.
- `OperationsContinuity/OperationsContinuityWorkflow.cs` - account-period close workflow aggregate, gates, checklist state, audit evidence, and close-readiness posture inputs.
- `OperationsContinuity/OperationsContinuityWorkflowService.cs` - command transitions, optimistic version checks, audit writes, ledger-post coordination, and DTO projection.
- `OperationsContinuity/OperationsContinuityRepositories.cs` - in-memory and file-backed workflow/audit stores plus transactional commit store contracts.
- `OperationsContinuity/PostgresOperationsContinuityStore.cs` - PostgreSQL workflow snapshot, audit timeline, and transactional ledger-post commit store.
- `OperationsContinuity/OperationsStatusDerivationService.cs` - deterministic status derivation from gate/sub-state posture through the F# operations rules.
- `OperationsContinuity/OperationsWorkflowAuditHashing.cs` - append-only workflow audit hash creation and chain validation.
- `OperationsContinuity/OperationsApprovalPolicyMatrixService.cs` - server-owned approval-policy matrix, governed rule upsert validation, audit-event construction, and file-backed policy persistence.
- `OperationsContinuity/OperationsCloseCalendarService.cs` - account-close calendar projection, governed due-date/owner overrides, and audit-event construction backed by Financial Operations policy.
- `PrivateCapital/PrivateCapitalActivityProjectionBuilder.cs` - Financial Operations-owned private-capital activity projection over manual-journal drafts, posted ledger events, report-pack workflow records, bank evidence, readiness, evidence categories, report-output posture, and payment-intent workflow status.
- `PrivateCapital/PrivateCapitalCloseCockpitService.cs` - private-capital close cockpit proof projection for partner capital tie-outs, expense/fee/allocation review, management-company operating records, NAV support packages, administrator-versus-Meridian shadow NAV tie-outs, close-control checklist evidence, close-package evidence, approval history, and period-lock readiness.
- `AccountingClose/` - deterministic journal posting, trial-balance projection, roll-forward,
  FX translation, source-linked audit rows, and period-close evidence gates.
- `AccountingClose/AccountingCloseManagementService.cs` - close-period plan projection over
  Operations Continuity workflow state, checklist dependencies, approval sign-offs, period-lock
  evidence, file-backed late-adjustment requests, materiality policy validation, and ledger-book
  scoped close-control evidence checks, including independent-review enforcement for material
  late-adjustment decisions.
- `AccountingClose/AccountingReportPackageService.cs` - accounting report package assembly for
  financial statements, investor capital statements, realized gain/loss, NAV packages,
  dimension-scoped package requests, certification state, validation issues, retained package
  history, evidence-backed package certification, independent certifier enforcement, and
  restatement workflow metadata.
- Ledger/AccountingPolicyService.cs - accounting-basis policy creation, resolution, listing,
  and projection metadata stamping for ledger writes.
- Ledger/AccountingJournalDraftService.cs - source-backed journal draft construction, ledger-book scope propagation, treasury-context validation, typed evidence metadata, and posting-command preparation before durable ledger append.
- `Ledger/TextJournal/` - ledger-compatible text-journal parsing, validation, report rendering,
  and CLI-facing report service backed by the Meridian double-entry ledger engine.
- `AccountingSystem/AccountingSystemIntegrationService.cs` - provider-neutral external GL import, latest-import retention, ledger-truth reconciliation, provider availability projection, and read-only posting posture.
- `Reconciliation/StatementRunWorkflowService.cs` - statement-run workflow that persists canonical imports, linked breaks, and case materialization for shared UI consumers.
- `Reconciliation/StatementReconciliationService.cs` - broker/custodian statement intake, mapping-profile validation, duplicate detection, normalization, matching, and reconciliation result projection.
- `Reconciliation/StatementReconciliationOrchestrator.cs` - staged reconciliation orchestration, checkpoint persistence, failure recovery, and case intake coordination.
- `Reconciliation/StatementRepositories.cs` - statement-run, validation, match, break, and case-link repository contracts and file-backed implementations.
- `Reconciliation/StatementMatchingEngine.cs` and `Reconciliation/CanonicalReconciliationEngine.cs` - deterministic match, tolerance, candidate, and true-break evaluation.
- `Reconciliation/StatementBreakClassifier.cs`, `StatementMappingProfiles.cs`, and `StatementToleranceProfiles.cs` - canonical break taxonomy, broker mapping profiles, and tolerance governance.
- `Reconciliation/ReconciliationEngineService.cs` - Security Master-enriched portfolio-vs-ledger
  reconciliation engine that joins positions, ledger balances, and the F# ledger reconciliation
  kernel.
- `Reconciliation/FileReconciliationDecisionJournal.cs` - crash-safe copy-on-write JSONL decision and resolution history persistence.
- `Banking/` - payment initiation, approval/rejection workflow, bank-side transaction records,
  deterministic transaction seeding, and PostgreSQL-backed banking persistence adapter.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes. Operations Continuity workflow state, command transitions, status derivation, persistence, audit hashing, reconciliation-break assignment/escalation, approval-policy rules, and close-calendar configuration live here so close, approval, report-pack, checklist-control, reviewer-independence, due-date, owner override, and retained audit policy remains part of Financial Operations rather than application orchestration or UI endpoints.

Statement reconciliation also lives here. Broker/custodian statement intake, mapping profiles, validation, duplicate detection, matching, break classification, reconciliation decision journals, statement-run persistence, and durable case materialization are Financial Operations behavior. Application commands and shared UI services invoke the module workflow, but they do not own reconciliation state, matching rules, or statement-run persistence.

Operations Continuity reconciliation runs retain the canonical Financial Operations lane coverage
for cash, position, trade, income, MBS factor, bank, and GL support. The workflow aggregate derives
ready/review/blocked posture from retained run evidence plus open reconciliation breaks so close
operators can review reconciliation completeness without browser or endpoint-local rules. Lane
classification uses structured break codes, sources, root-cause/output metadata, case correlation
metadata, and retained evidence labels/routes instead of depending only on display strings. The
bank reconciliation lane also recognizes retained payment confirmation, return, reversal, and
cash-evidence break language so approved payment cash evidence stays a reconciliation input rather
than live payment execution authority.
The same workflow service now derives the source-backed Financial Operations operational dashboard
from aggregate state. Its metrics cover Receive Activity, Match Records, Resolve Exceptions,
Approve Results, Produce Evidence, and Close Support, including retained evidence, route hints, and
required actions so UI surfaces consume a shared core-flow rollup instead of reconstructing
dashboard state locally.
The Financial Operations command-center read service also derives the shared close-support decision
from Operations Continuity, close-calendar, and private-capital close cockpit inputs. That decision
publishes period state, period-lock/reopen posture, NAV/report dependencies, unresolved exceptions,
approvals, and retained evidence gaps as one server-owned readiness posture so browser and WPF
surfaces cannot show synthetic completion while required evidence, approvals, lock state, or NAV
support remain blocked.
It also derives the reviewed-automation summary from aggregate state and enforces the action-origin
guard for material commands. Automation-origin and assistant-origin requests may carry suggestions,
summaries, drafts, flags, and retained review evidence, but Security Master override approval,
ledger posting, reconciliation break assignment, escalation, and resolution, approval submission or
decision, close-package publication, and governed reopen commands fail closed unless the request
origin is a human operator. Critical or material
reconciliation breaks also require retained resolution evidence before the aggregate can clear the
exception and advance approval posture. When report-pack evidence is ready but not yet submitted for
approval, the same summary surfaces report-commentary and audit-request-list drafts as review-only
work so publication remains behind human approval. Already closed reconciliation breaks reject
duplicate resolution or reassignment commands so retained case evidence and audit history cannot be
mutated after closure. Break assignment, escalation, and resolution commands also refresh the
derived reconciliation lane summaries so active-work queues, dashboards, and evidence tables do not
show stale break counts, required actions, or retained assignment/resolution evidence after
exception work. Lane required actions are derived from retained open break casework, including
source suggested actions, unassigned owner counts, escalation state, and blocked output names, so
MBS factor, income, bank, GL, and other reconciliation lanes keep exception-management guidance
without browser-local reconstruction. The dashboard Match Records and Resolve Exceptions metrics
roll those non-ready lane and open-break actions into the shared operational dashboard summary,
capped for scanability, so operators see specific cash, income, MBS factor, bank, GL, owner,
escalation, or blocked-output remediation work before approval instead of generic lane-completion
or exception prompts. Approve Results actions are likewise derived from report-pack readiness,
assigned reviewer state, approval history, and the same close checklist-control task IDs enforced by
the workflow aggregate, so submission and reviewer-decision work stays traceable without UI-local
approval rules. The same dashboard also derives Close Support actions from the close-readiness
blocker categories, so provider freshness, ledger posting, reconciliation, reporting, approval, and
period-lock work remain tied to the shared close checklist instead of a catch-all close prompt. The
Produce Evidence metric also rolls up incomplete evidence-package actions from accounting-record,
reconciliation-coverage, exception-management, report-pack, close-manifest, approval-history,
audit-support, and period-lock packages so retained evidence work stays source-backed through the
final dashboard stage. A governed
reopen retains the prior close-package manifest as evidence, but the operational dashboard no longer
treats that retained package as a current period lock; Produce Evidence remains in review until
incident remediation is closed again with a new retained period-lock package.
It also derives evidence-package summaries for accounting-record evidence, reconciliation coverage,
exception-management casework, report-pack readiness, close-package manifests, approval history,
audit-support packages, and period lock/reopen evidence from the same workflow, accounting-record,
close-package, lane, and retained timeline evidence. The reconciliation-coverage evidence package
makes cash, position, trade, income, MBS factor, bank, and GL support lane completeness visible as a
first-class audit package. The exception-management evidence package makes reconciliation-run case
inventory, open exception posture, assignment/escalation evidence, and resolution evidence visible
as a first-class audit package. The approval-history evidence package makes workflow
submission, reviewer decision, and retained checklist-control approvals visible as a first-class
evidence package before audit release. Package status and required actions remain Financial
Operations-owned instead of being recalculated by endpoint or browser tables. Close-package evidence
hashes are computed by the workflow aggregate from the published package identifiers, report pack,
retained evidence links, and checklist-control approvals; request-supplied hashes are compatibility
input only and are not trusted as retained audit evidence.
Private-capital close cockpit proof also lives here. `PrivateCapitalCloseCockpitService` composes
the shared private-capital activity projection with Operations Continuity workflow detail to derive
data receipt, reconciliation, journal posting, capital-account, partner-capital tie-out,
expense/fee/allocation, management-company operating records, NAV support, valuation, reporting,
delivery, close-control checklist, close-package, and period-lock lanes plus approval history and
NAV support package rows. The journal lane requires every source-backed fund-event record in the
close scope to be posted with ledger impact before it can pass. The close-control lane requires
retained checklist evidence and required control approvals for reversal approval, recurring-journal
completion, stale-mark resolution, and period lock or governed reopen proof before a closed workflow
can make the cockpit ready. Reporting, delivery, and partner-capital tie-out lanes require approved
report outputs and retained delivery manifests, so published but unapproved statements cannot make a
close package ready. Approval history includes workflow approvals, checklist-control approvals,
governed reopen approvals retained from the workflow timeline, fund-event approvals, and governed
report-output decisions so close reviewers can trace source, journal, report, NAV, period-reopen,
and administrator-tie-out approval evidence from one shared cockpit.
It also publishes explicit private-capital evidence package summaries for fund-event accounting,
expense/fee/allocation review, partner capital tie-outs, NAV support, and close approval/audit
evidence so operator surfaces can inspect package completeness without rebuilding lane rules
locally.
Private-capital activity projection semantics also live here. `PrivateCapitalActivityProjectionBuilder`
derives fund-event records, capital-account subledgers, evidence categories, report-output
readiness, and payment-intent workflow posture from contract DTO inputs while UI Shared only loads
stores, passes snapshots, and maps HTTP routes. Browser and WPF clients consume those projected DTOs
instead of recomputing accounting readiness outside Ledger or Financial Operations.
The management-company lane is read-only proof for retained expense allocation, management-fee,
intercompany, bank/card, budget or cash-plan, and reimbursement evidence; missing source support
keeps the lane in review instead of inventing ERP-like balances. The NAV support lane now requires
retained administrator NAV evidence tied against Meridian shadow NAV within tolerance before close
readiness can pass. UI Shared maps the route and WPF registers the contract, but Financial
Operations owns the readiness rules and retained evidence posture. The close cockpit consumes the
workflow-owned period-lock/reopen evidence package as authoritative period-lock proof, so a closed
workflow with a close package still remains in review when governed reopen remediation has not been
re-locked with retained evidence.

Portfolio-vs-ledger reconciliation engine behavior also lives here. The engine enriches
portfolio/ledger candidates with the contracts-owned Security Master query surface and classifies
matches and breaks through the F# ledger reconciliation kernel instead of Application-local
service/logging ownership.

Accounting-system GL evidence integration lives here as provider-neutral Financial Operations behavior. The integration service lists accounting-system providers, chooses configured QuickBooks Online evidence when available, falls back to read-only fixture providers when live company evidence is not configured, exposes available QuickBooks/Xero/NetSuite fixture import mappings, publishes provider-specific mapping requirements for account mapping, journal lineage, trial-balance tie-out, and dimension mapping, and keeps live Xero/NetSuite rows planned with posting disabled. It validates returned import scope, payload counts, and balanced journal evidence before retention, stamps a stable import content hash, retains latest imports by tenant/company/provider/fund/book, reconciles external trial-balance rows against Meridian-owned ledger totals for that same enterprise scope when a ledger store is available, and stores tenant/company-scoped external-GL mapping profiles for account and dimension mappings. Reconciliation rows retain both provider-side evidence refs and Meridian ledger-entry, journal-entry, period, and source refs; the summary also publishes ledger-book scope plus external-import, Meridian-ledger, and tie-out evidence package posture so close support can distinguish missing ledger proof from unresolved GL breaks. The tie-out evidence package classifies missing-external, missing-Meridian, and variance breaks into operator required actions for assignment, retained provider support, ledger remediation, and close approval evidence. Guarded export-package creation requires an explicit Meridian ledger book, a human-operator action origin, retained export-control evidence that identifies export-control intent plus the selected ledger book and the export fund, provider/fund scope, or exact export period on the same evidence artifact, a certified mapping profile retained for the same tenant/company/fund/provider/book with retained mapping approval, certification, sign-off, or review evidence that identifies the mapping profile or provider/fund scope, account mapping coverage, certified canonical accounting dimension mappings on both Meridian and external GL sides, import/reconciliation evidence for the exact export period and same ledger book, generated mapped export lines from Meridian-owned ledger totals, and no stale-period reconciliation reuse before it can reach ready-for-review certification state; unresolved GL breaks remain critical validation issues when balanced reconciliation is required. Guarded export review and certification require the selected mapping profile to be scoped to the export ledger book; fund-wide profiles can remain catalog/reference profiles but cannot make a scoped export ready for review. Export packages retain mapping profile, reconciliation id, reconciliation content fingerprint, and balanced-reconciliation lineage, and certification revalidates the current mapping profile, latest reconciliation, retained reconciliation id/fingerprint, tenant/company scope, and reconciliation ledger book before moving a retained artifact to Certified. Certified-looking mapping profiles with only generic support evidence or wrong-profile approval evidence are downgraded to Draft and cannot emit generated export lines, while certified-looking mapping profile upserts from reviewed automation are rejected before certification state is retained. Generated export lines are also suppressed when any retained dimension mapping is uncertified or missing canonical fund, entity, ledger-book, operating, investment, neutral account, or external-GL scope on either the Meridian or external GL side. Retained ready-for-review export packages can be certified with reviewer notes and evidence, duplicate or draft certification is rejected, and certification also fails closed if retained package state has live external GL posting enabled, lacks a posting-disabled reason, has current mapping/reconciliation blockers, was supplied by reviewed automation instead of a human operator, or the supplied certification evidence does not reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact. Live external GL posting remains disabled until a separately approved adapter and release gate publish Meridian-owned ledger entries. Controlled export-package manifests retain generated mapped lines, mapping/reconciliation lineage, evidence links, validation state, deterministic content hash, and `ExternalPostingAllowed = false` posture for review without creating a live posting path; manifest retrieval also revalidates current provider posting capability and retained posting-disabled state so tampered retained packages cannot emit a live-posting artifact. UI Shared maps endpoints and supplies credential-backed provider registration, but it does not own GL evidence reconciliation, mapping validation, export-package safeguards, or posting-disable posture.

The canonical external-export dimension mapping scope includes customer, vendor, and project
dimensions in addition to fund, entity, ledger-book, operating, investment, neutral account, and
external-GL scope, so generated guarded-export lines remain blocked until both Meridian and provider
dimension mappings cover the full relationship context.
Guarded external-GL export packages also preserve optional tenant/company scope in package identity,
manifest payloads, and content hashes. Manifest and certification lookup can be filtered by that
enterprise scope so one company's retained external-GL artifact cannot be retrieved or certified
through another company's session.

Guarded export validation also fails closed when the selected mapping profile targets a different
ledger book than the export package, and generated export lines are suppressed until the mapping
profile is certified for that selected book.
It also fails closed when a registered provider advertises live external-GL posting capability, so
the guarded export lane remains import-first and review-only even if an adapter exposes posting in
its capability metadata.

External GL export certification evidence must carry certification intent plus retained export
package id, certification id, export ledger book, and exact-period scope on the same evidence artifact; split support
and approval links are not enough for certification.
External GL mapping-profile certification evidence follows the same rule: retained mapping
approval, certification, sign-off, or review evidence must identify the mapping profile or
provider/fund scope on the same evidence artifact before the profile can feed generated export
lines. Retained guarded export packages can be listed by provider, fund, ledger book, certification
state, tenant, and company so operator/admin surfaces can review retained export history and
certification posture without knowing a package id in advance.

Accounting close projections live here as deterministic Financial Operations behavior. Journal
posting, FX translation, trial-balance, roll-forward, source-linked audit, and close evidence gates
are exposed to UI Services and WPF without making those surfaces own accounting-close state.
Trial-balance projection preserves `LedgerDimensionSetDto` on journal lines, buckets same-account
activity separately by dimensional scope, and supports scoped close/report filters for fund, entity,
sleeve, strategy, investor, capital account, instrument, tax lot, cost center, counterparty, neutral
organization/portfolio/book/account/customer/vendor/project dimensions, and external-GL dimensions
without inferring scope from account names. FX translation adjustments generated from those trial
balance rows retain the same dimensions and roll forward into the matching dimensional close row
instead of collapsing adjustments to account-only reporting buckets.
Production-readiness assessment now requires retained ledger-book-scoped evidence that period
reports, cross-period reports, journal dimension filters, and guarded external-export dimension
mappings preserve those canonical dimensions before dimensional reporting is treated as rollout-ready.
`AccountingCloseManagementService` now projects a `ClosePeriodPlanDto` from the Operations
Continuity workflow, converting workflow checklist tasks into dependency-aware close tasks,
Operations approvals into sign-off rows, close-package publication into period-lock posture, and
retained late-adjustment requests into materiality-policy validation issues. Workflows that are
started with a ledger-book scope retain that `LedgerBookId` through workflow summaries, workflow
detail, and the close plan so report-package, close, and ledger-book review surfaces do not lose
book context after handoff from Operations Continuity. Open workflow duplicate guards allow
distinct ledger books for the same fund period while still blocking same-book or ambiguous
fund-level duplicates. Close-backed accounting report packages inherit the close plan
book when the request omits one, block explicit ledger-book mismatches during package assembly, and
revalidate the current close-plan book before certification so certified exports cannot drift across
books. Report packages with ledger-book scope require retained ledger, reconciliation,
rendered-report, and NAV support evidence links that name the same ledger book before the package
can reach ready-for-review certification; close-backed packages also recheck that evidence against
the close plan book before certification. When `StorageOptions`
is registered, late-adjustment requests and task-level close sign-off decisions are retained
through an atomic JSON snapshot under the configured storage root and reproject after restart.
Task sign-off decisions retain authenticated actor, role, notes, and evidence, reject duplicate
actor-role decisions, roles outside the task's sign-off matrix, or incomplete prerequisite tasks,
require retained approval/sign-off/control/review evidence that identifies the close task, workflow,
sign-off role, and workflow or exact close period on the same artifact, count only
approved decisions toward the role-scoped approval cap, and promote the close task only when
retained approved decisions satisfy the role-scoped task approval count. Each projected close
task now carries sign-off requirement rows so browser, WPF, report
certification, and export workflows can inspect required role, required approval count, approved
count, satisfaction state, and required evidence without rebuilding close matrix rules locally.
The same close plan carries close-calendar milestone rows derived from checklist due dates,
dependencies, sign-off counts, evidence, blockers, and period-lock state so accounting/reporting
surfaces can render calendar posture without reinterpreting workflow checklist rows. The late-adjustment command remains a governed close review artifact; it does not
mutate posted journal entries and material adjustments require controller approval before final
close certification. Late-adjustment requests require retained late-adjustment evidence that
identifies the journal entry, workflow, or exact close period on the same artifact before a row is
stored, and review decisions are retained with authenticated actor, decision notes, and approval,
rejection, decision, or review evidence that identifies the retained request, journal entry,
workflow, or exact close period on the same artifact; generic close support evidence and split
support/provenance links are rejected for the governed request/review gates. Duplicate retained requests for the same journal entry within a
close workflow, duplicate decisions, and decisions after close-package period lock fail closed.
`AccountingReportPackageService` assembles the implementation-grade report package DTO family:
financial statement package, investor capital statement, realized gain/loss report, NAV package,
certification, validation issues, deterministic report-line provenance, deterministic export
artifact rows, service-owned close/report readiness rows, and optional restatement workflow metadata.
It accepts explicit canonical
`LedgerDimensionSetDto` scope on package requests, validates conflicting fund, ledger-book,
investor, and capital-account dimensions, preserves optional tenant/company scope, and stamps the
retained ledger book and dimension scope onto child financial statement, investor capital, realized
gain/loss, NAV, export, and provenance artifacts so report consumers do not have to infer
dimensional or tenant scope from package identifiers or parent rows. It carries close-plan
validation into the package certification state, keeps
standalone packages ready-for-review when non-blocking warnings remain, returns draft state when
ledger-book scope is missing, and uses ledger-book-scoped retained package identifiers so primary,
GAAP, tax, or other book packages for the same fund period do not overwrite one another. Explicit
dimension-scoped packages add a deterministic scope suffix so entity, strategy, capital-account, or
external-GL packages for the same book and period can coexist. Tenant/company-scoped packages add a
deterministic enterprise scope suffix so same fund/period/book packages cannot collide across
companies. Package history can also be filtered by ledger book, dimensions, tenant, and company so
close, reporting, and export review surfaces inspect the intended enterprise book/scope rather than
a fund-period aggregate.
blocking close-plan evidence is missing, close checklist dependencies are incomplete, the attached
close workflow has not reached period-lock, approved
sign-offs are missing, or material late adjustments are still unapproved, blocks restatement
certification when retained certified prior-package lineage or retained restatement evidence is
missing, requires restatement lineage evidence to name the exact prior package or certification id
being restated, and retains package history through an atomic JSON snapshot when `StorageOptions`
is registered.
  The service also owns the retained certification transition: only ready-for-review packages without
  critical validation issues and with a retained close workflow can move to `Certified`, duplicate
  certification is rejected, and reviewer notes plus evidence links are persisted back across the
  retained package and child report artifacts.
Close-backed packages retain the source workflow id and re-query the current close plan at
certification time, so a package assembled while ready-for-review cannot be certified after a new
period-lock blocker, incomplete checklist item, missing sign-off, or material late adjustment appears.
Certification evidence must be a retained approval, certification, sign-off, or review artifact
that references the retained package id, certification id, ledger book, exact package period,
tenant/company scope when the package is enterprise-scoped, and explicit dimension scope when the
package is dimension-scoped in the same artifact, so split generic support plus wrong-period or
wrong-scope approval evidence cannot certify a different
report package. Report package certification, close task sign-off, and late-adjustment request/review commands also reject
assistant or automation-origin requests before retaining approvals, sign-offs, decisions, or
certified report evidence.
Close task sign-off evidence must name the exact checklist task, sign-off role, workflow or close
period, and ledger book when scoped; extended role or period tokens cannot satisfy retained approval
provenance by prefix.
Late-adjustment request and review evidence uses the same exact-period provenance requirement, so
wrong or extended close-period tokens cannot request or approve material close adjustments for a
different period.
Child export artifacts retain ledger-book and canonical dimension scope, and receive certified
timestamps plus recomputed content hashes that include the book, dimensions, certified state, and
retained certification evidence. When the package is a restatement, final certification also
requires the approval evidence to name the exact prior package being restated, promotes the
retained restatement workflow metadata to approved, and merges the certification evidence into the
statement and NAV restatement records.
Certified accounting report packages are immutable at the retained package boundary; rebuilding the
same fund/period package after certification is rejected so corrections must use governed
restatement lineage instead of replacing certification evidence.
Provenance rows identify the statement, report line, amount, source kind,
fund/investor/capital-account dimensions, and retained evidence used for balance sheet, income
statement, statement of changes in capital, investor capital, NAV, and restatement lineage rows.
Close/report readiness rows classify checklist sign-off, period lock, late-adjustment review,
report evidence, export certification, and restatement workflow posture with blocker counts,
retained evidence, ledger-book scope, and canonical dimensions so operator surfaces consume the same
certification checklist that Financial Operations uses to gate package certification.
Export artifact rows identify the retained output kind, format, route, ledger book, dimensions,
certification-state-bound content hash, source statement id, evidence links, and certification state for financial statement PDFs/workbooks,
investor capital statements, realized gain/loss CSV, NAV packages, report-line provenance
manifests, and restatement manifests. The generated routes resolve to controlled JSON retrieval
manifests that preserve evidence, content hashes, certification state, and an explicit
`ExternalPostingAllowed = false` guard. Actual artifact byte rendering remains downstream report
renderer work; the accounting service owns the certification manifest state.

Accounting-basis policy and ledger text-journal reporting also live here. Application composition
registers the policy/projection services and the CLI command invokes the text-journal report service,
but Application no longer owns accounting policy resolution, ledger write projection metadata, or
text-journal parser/report semantics.
`AccountingJournalDraftService` accepts shared ledger-book scope and treasury ledger context, fails
closed before governed write projection when a draft is missing ledger-book scope or a retained
line-level book dimension conflicts with the draft ledger book, and stamps the resulting journal
metadata with effective date, idempotency, fund-event, capital-account, investor, payment-intent,
and settlement references before a governed ledger write is projected. Keep this behavior in
Financial Operations so private-capital and payment-linked drafts are validated once before browser,
WPF, storage, or reporting surfaces inspect them.
Operations Continuity ledger-posting candidates preserve `LedgerDimensionSetDto` on each candidate
line and map that scope into immutable ledger line dimensions before appending the governed journal
write, so close, reconciliation, report, and external-GL consumers do not have to infer line scope
from account names or journal-level metadata.
`AccountingPostingCandidateService` bridges Rules Studio posting-rule dry runs into that governed
journal draft path. It evaluates a source event through the shared accounting-configuration
service, passes tenant/company/fund/ledger-book scope into dry-run and workspace lookup, resolves
generated account paths through the active chart without guessing account type, preserves generated
dimensions and evidence on the returned candidate payload, carries generated line dimensions into
the draft request, and then calls the draft service to produce only an approval-gated posting
command candidate. The draft request keeps the selected Rules Studio posting rule id/version and
dry-run correlation separate from the accounting-policy rule id, then stamps that provenance onto
the governed journal metadata with source-event identity. The draft service also retains
line-entry keyed dimension tags on the governed write metadata so downstream ledger-book reports
and export mapping can recover line-specific
fund/entity/cost-center/counterparty/external-GL scope without adding a live posting path. It does
not append ledger entries or bypass the manual-journal lifecycle. Source-event posting candidates
now require explicit ledger-book scope and a ledger-book aggregate id that matches that scope, then
fail closed before draft/write creation when the request is unscoped or the aggregate boundary is a
source transaction instead of the target book. Tenant-scoped candidates cannot fall back to another
company's workspace, so Rules Studio dry-run output cannot become a governed posting candidate
through a fund-level fallback configuration.
The generated candidate path also preserves the neutral operational dimensions carried by
`LedgerDimensionSetDto` - organization, portfolio, book, account, customer, vendor, and project -
through generated posting lines, governed draft lines, and approved append writes so reporting and
external-GL mapping do not lose non-fund dimension scope at the rule-to-ledger boundary.
`AccountingPostingCandidatePostService` is the separate append gate for approved generated
candidates. It requires a configured Postgres-backed `ILedgerJournalStore`, a human-operator action
origin, retained source-event identity, approval evidence, an aggregate id equal to the target
ledger book, a pending approval-gated posting command, a matching ledger book/accounting basis,
journal metadata that names the approved ledger book, retained line dimensions whose book scope
matches that ledger book, and a period owned by that book before calling the journal store. Replays
for the same `(ledger book aggregate, source event)` return the existing journal, while the same
economic event may still produce separate GAAP, cash, tax, statutory, or primary postings because
each basis uses its own ledger-book aggregate.
External accounting-system providers remain read-only import, reconciliation, and export-package
surfaces; this service appends only Meridian-owned ledger facts.
The retained approval evidence for generated candidate append must name approval intent, fund,
ledger book, and source event on the same artifact, plus tenant and company when the request is
enterprise-scoped, so generic workpaper links cannot approve a different book or company by
association. The approving operator must also be independent from the source-event candidate
preparer before the append gate can move a generated candidate into the Meridian ledger.
Production certification profiles also fail closed before persistence when a retained profile marks
posting rules, journal lifecycle, close/reporting, external GL, reconciliation, direct lending,
strategy ledger reads, or dimensional reporting controls as certified without evidence that names
the selected tenant, company, fund, ledger book, and the specific certified control family. A full
production-certification evidence artifact can certify the full profile, but category-specific
evidence cannot be reused to bless unrelated controls.

Payment approval and bank-transaction records also live here. `IBankingService` publishes the
approval workflow and `IBankTransactionSource` evidence surface used by reconciliation, Plaid
workstation flows, and Direct Lending tests without making Direct Lending own bank-side
transaction state. Approval and rejection requests carry the reviewed-automation action origin so
assistant or automation-origin drafts can be rejected before payment approval state changes. Payment
approval no longer records bank-side transactions by itself; retained bank confirmation, return,
reversal, or failure evidence is recorded through an explicit bank-evidence command after approval,
and that bank-side transaction retains the operator that recorded the evidence. The bank-evidence
command also carries reviewed-automation action origin and rejects assistant or automation-origin
requests before the cash-evidence record is retained. This keeps payment work in the
request/approval/cash-evidence lane rather than treating approval as live payment execution.
Operations Continuity also projects reviewed-automation output artifacts for extraction, match
suggestion, journal draft, report commentary, audit request list, missing-support, and evidence
summary review stages. These artifacts are review rows backed by retained workflow evidence; they
do not create a path for assistant-origin posting, approval, payment release, report publication, or
evidence deletion.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-FINANCIAL-OPERATIONS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5X-FINOPS-001` | Financial operations control center |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-FINANCIAL-OPERATIONS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.FinancialOperations/Meridian.FinancialOperations.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityWorkflowServiceTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityEndpoints_ApprovalPolicy --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityEndpoints_CloseCalendar --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~OperationsContinuityEndpoints_ApprovalPolicy|FullyQualifiedName~OperationsContinuityEndpoints_CloseCalendar|FullyQualifiedName~StorageFeatureRegistrationTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StatementValidationServiceTests|FullyQualifiedName~StatementRepositoryTests|FullyQualifiedName~StatementReconciliationOrchestratorTests|FullyQualifiedName~StatementReconciliationContextAdapterTests|FullyQualifiedName~StatementMatchingEngineTests|FullyQualifiedName~CanonicalReconciliationMatchingEngineTests|FullyQualifiedName~StatementReconciliationServiceTests|FullyQualifiedName~StatementImportAndMatchingTests|FullyQualifiedName~StatementFixtureScenarioTests|FullyQualifiedName~StatementBreakClassifierTests|FullyQualifiedName~ReconciliationContractsTests|FullyQualifiedName~BrokerCustodianMatchingPipelineTests|FullyQualifiedName~ReconciliationApiServiceTests|FullyQualifiedName~StatementImportCommandsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ReconciliationEngineServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingSystemIntegrationServiceTests|FullyQualifiedName~ProviderConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~AccountingCloseServicesTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PaymentApprovalTests|FullyQualifiedName~BankTransactionSeedTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingPolicyServiceTests|FullyQualifiedName~LedgerCliCommandTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

`IOperationsContinuityWorkflowService` publishes account-period close workflow commands and reads. `IOperationsContinuityRepository`, `IOperationsWorkflowAuditStore`, `IOperationsContinuityWorkflowStartCommitStore`, and `IOperationsContinuityTransactionalCommitStore` publish workflow persistence and transactional audit/ledger commit contracts. `IOperationsApprovalPolicyMatrixService` publishes the policy matrix consumed by shared workstation endpoints. `IOperationsCloseCalendarService` publishes close-calendar reads and governed item upserts. `IPrivateCapitalCloseCockpitService` is implemented here to publish the contract-owned close cockpit projection while endpoints remain in UI Shared. Accounting-close services publish journal posting, FX translation, trial-balance, roll-forward, and evidence-gate projections. `IAccountingPolicyService`, `IAccountingBasisProjectionService`, and `IAccountingBasisProjectionSetService` publish accounting-basis policy lookup, ledger write metadata projection, and one-source-event-to-many-book projection candidates for application workflows. `LedgerTextJournalReportService` publishes CLI-facing text-journal parsing and report rendering. `AccountingSystemIntegrationService` publishes provider listing, import preview/latest import, and latest external-GL reconciliation reads over `IAccountingSystemProvider` contracts. `IBankingService` publishes payment approval records, direct payment lookup, explicit bank-evidence recording, and bank-transaction evidence workflows over `Meridian.Contracts.Banking` DTOs. `IStatementRunWorkflowService`, `IStatementReconciliationService`, `IStatementReconciliationOrchestrator`, `IStatementValidationService`, and reconciliation repository contracts publish statement intake, validation, matching, persistence, and casework orchestration for commands and UI services. DTOs remain in `Meridian.Contracts.Workstation`, `Meridian.Contracts.AccountingSystem`, `Meridian.Contracts.Banking`, and `Meridian.Contracts.Ledger`; authorization roles and permissions come from `Meridian.Identity.Auth`; durable local writes use `Meridian.Storage.Archival.AtomicFileWriter` and banking persistence uses `Meridian.Storage.Banking`.
`IAccountingPostingCandidateService` consumes `PostingRuleJournalCandidateRequestDto` and returns
`PostingRuleJournalCandidateResultDto` from the shared ledger contract surface so browser and WPF
can call the same source-event-to-draft candidate path without owning posting-rule execution or
ledger-posting semantics. `IAccountingPostingCandidatePostService` consumes the approved post
request and appends the candidate write through storage only after the ledger-book aggregate,
source-event, approval, period, and basis checks pass. Requests carry tenant/company/fund/ledger-book
scope through dry-run, chart resolution, candidate metadata, and post execution so the bridge follows
the same isolated configuration workspace as the Rules Studio store.

### Migration and archive notes

`OperationsContinuityWorkflow`, `OperationsContinuityWorkflowService`, workflow repository/store contracts and implementations, status derivation, audit hashing, `OperationsApprovalPolicyMatrixService`, `IOperationsApprovalPolicyMatrixService`, `OperationsCloseCalendarService`, and `IOperationsCloseCalendarService` moved from `src/Meridian.Application/OperationsContinuity` into this module. Statement reconciliation models, contracts, services, repositories, orchestration, mapping/tolerance profiles, matching engines, break classification, decision journals, and statement-run workflow services moved from `src/Meridian.Application/Reconciliation` into this module. `ReconciliationEngineService` moved from `src/Meridian.Application/Services` into this module and now consumes the contracts-owned Security Master query surface. Accounting close services moved out of the legacy Application accounting-close folder into `AccountingClose/`. Payment approval and bank-transaction services moved out of the legacy Application banking folder into `Banking/`. Accounting policy/projection services and ledger text-journal parser/reporting services moved out of the legacy Application ledger folder into `Ledger/`. `AccountingSystemIntegrationService` and `PrivateCapitalCloseCockpitService` moved from `src/Meridian.Ui.Shared/Services` into this module. Application composition, command handlers, and UI services consume these module services but do not own their workflow state, policy implementation, reconciliation state, matching rules, statement-run persistence, portfolio-vs-ledger reconciliation engine behavior, external-GL reconciliation, bank-side transaction state, accounting policy/projection behavior, ledger text-journal semantics, accounting-close projections, private-capital close proof, or posting-disable posture.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
