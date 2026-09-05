---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-FINANCIAL-OPERATIONS
path: src/Meridian.FinancialOperations
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-07-27
---

# src/Meridian.FinancialOperations

## Shared close and lot convergence

The Financial Operations command center owns the shared close decision. It requires an explicit fund profile, ledger book, fund account, entity, and period; validates book/profile binding and exact workflow identity; and includes workflow, calendar, version-matched close-plan, and private-capital contributors. Missing, ambiguous, failing, or older-than-five-minute contributor evaluations block. Asset coverage and fund-wide diagnostic metrics do not establish readiness. Focused proof: `FinancialOperationsCommandCenterReadServiceTests`.

Close acceptance additionally proves account/entity/book subject ownership independently of workflow selection. The real close-plan reader stamps workflow, account, and retained evidence versions from one state snapshot; final projection rechecks those stamps so concurrent sign-off or configuration changes block instead of mixing snapshots. The closing-entry gate is mandatory. Repairing the underlying scope/evidence issue allows a fresh assessment to restore readiness.

Hard close and workflow publication re-evaluate shared readiness before mutation, including callers outside the workstation HTTP route. Complete subject scope, authenticated tenant/company, exact workflow revision, and current retained prerequisites are required. Close packages, locks, and published exports are outputs of that transition; they do not create circular prerequisites. Historical approval decisions stay visible while the current decision controls readiness. The retained close plan proves each task's required sign-offs; calendar reviewer totals describe a different approval dimension.

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
  scoped close-control evidence checks, governed close-plan configuration for materiality, task
  ownership, due dates, role-scoped sign-off matrix rows, evidence requirements, dependency predecessors, and
  dependency reasons, including independent-review enforcement for material late-adjustment
  decisions, service-owned operating coverage rows for close setup, dependency graph, sign-off
  matrix, late adjustments, blocker review, and period lock, plus a governed
  close-period lock bridge that fails closed on unresolved plan blockers before delegating to the
  Operations Continuity close-package publication gate. Hard close requires explicit Controller or
  Fund Controller authority; preparation-only closing-entry requests do not acquire that authority
  or seal the reconciliation queue.
- `AccountingClose/AccountingReportPackageService.cs` - accounting report package assembly for
  financial statements, investor capital statements, realized gain/loss, NAV packages,
  dimension-scoped package requests, certification state, validation issues, retained package
  history, evidence-backed package certification, independent certifier enforcement, and
  restatement workflow metadata.
- Ledger/AccountingPolicyService.cs - accounting-basis policy creation, resolution, listing,
  and projection metadata stamping for ledger writes.
- Ledger/AccountingJournalDraftService.cs - source-backed journal draft construction, ledger-book scope propagation, treasury-context validation, typed evidence metadata, and posting-command preparation before durable ledger append.
- `Ledger/AccountingPostingCandidateService.cs` - non-posting Rules Studio candidate construction,
  authoritative ledger-book and accounting-policy resolution, and fail-closed typed book,
  economic-event, book-position, projection-lineage, and rule-pack assertion validation.
- `Ledger/TextJournal/` - ledger-compatible text-journal parsing, validation, report rendering,
  and CLI-facing report service backed by the Meridian double-entry ledger engine.
- `AccountingSystem/AccountingSystemIntegrationService.cs` - provider-neutral external GL import, latest-import retention, ledger-truth reconciliation, provider availability projection, and read-only posting posture.
- `Reconciliation/StatementRunWorkflowService.cs` - statement-run workflow that imports canonical statements, matches rows against Meridian's internal book through the shared sided `StatementMatchingEngine`, and persists linked breaks and case materialization for shared UI consumers. Rows with no internal counterpart — and internal records missing from the statement — surface as genuine breaks instead of self-matches.
- `Reconciliation/StatementRunMatchingService.cs` - normalizes imported statement rows and projects the sided `StatementMatchingEngine` results into break records and per-row match outcomes for the live workflow; `ToleranceBreached` is computed from the actual variance.
- `Reconciliation/InternalReconciliationBook.cs` - the internal-book seam (`IInternalReconciliationBookSource`) supplying the positions, cash balances, and ledger transactions a statement run is reconciled against; the default `EmptyInternalReconciliationBookSource` yields honest unmatched breaks until a real source is registered.
- `Reconciliation/Connectors/StatementIngressLimits.cs` - the single PRD-010 ingress bound shared by
  every statement connector and by `StatementImportService`, with the named diagnostic codes each
  refusal carries. Registered once in `Reconciliation/ReconciliationServiceRegistration.cs`.
- `Reconciliation/Connectors/StatementImportService.cs` - preview and authoritative import-commit
  boundary used by the persisted statement reconciliation report coordinator; a committed import is
  checkpointed before Evidence Vault linkage or JSON/CSV reconciliation artifact retention so
  recovery cannot silently repeat the import. Commit rejects any parsed row or uploaded-document
  account identity that differs from the authorized external account.
- `Reconciliation/StatementReconciliationService.cs` - broker/custodian statement intake, mapping-profile validation, duplicate detection, normalization, matching, and reconciliation result projection. Position rows match through the shared `StatementMatchingEngine` against internal portfolio positions; rows without internal evidence surface as break cases instead of auto-matching.
- `Reconciliation/StatementReconciliationOrchestrator.cs` - staged reconciliation orchestration, checkpoint persistence, failure recovery, and case intake coordination.
- `Reconciliation/StatementRepositories.cs` - statement-run, validation, match, break, and case-link repository contracts and file-backed implementations.
- `Reconciliation/StatementMatchingEngine.cs` and `Reconciliation/ReconciliationMatchingEngine.cs` - deterministic match, tolerance, candidate, and true-break evaluation. The canonical daily pipeline is split across `ReconciliationIngestionContracts.cs`, `ReconciliationNormalizationService.cs`, `MatchingTolerances.cs`, `ReconciliationMatchingEngine.cs`, `DefaultReconciliationIngestionScheduler.cs`, and `ReconciliationRunOrchestrator.cs` (one type per file).
- `Reconciliation/ReconciliationMatchKernel.cs` - deterministic matching primitives under the canonical floor (and the seam the W9-INGEST-009 statement/ledger sided matcher inherits): stable best-first assignment over scored candidate pairs, bounded same-sign one-to-many/many-to-one split discovery, and content-derived identifiers. The floor itself is sided (candidate pairs must span two source snapshots; rows never match their own source), scores tolerance candidates instead of taking the first near-tolerance hit, records split-group shape (anchor, legs, residual) in `MatchEvidence` attributes, requires currency identity before comparing amounts (fail-closed FX surfaces as breaks), and derives match/break/evidence ids from run content so re-evaluating the same run is idempotent.
- `Reconciliation/BusinessDayAccountingCalendar.cs` and `Reconciliation/FileAccountingCalendar.cs` - the production `IAccountingCalendar`: weekend/holiday classification, roll-forward period resolution for postings, and signed business-day distance used by cash staging evidence; deployments load market calendars from `reconciliation/business-calendar.json` with a fail-safe weekends-only default. Canonical-pipeline normalization (`ReconciliationNormalizationService.cs`) converts through the same fail-closed `IReconciliationFxRateProvider` seam the statement lane uses and resolves accounting periods from each entry's own posting timestamp.
- `Reconciliation/ReconciliationIngestionOptions.cs` - capture policy for `DefaultReconciliationIngestionScheduler.cs`: bounded concurrent snapshot capture, per-source attempt timeout, and exponential-backoff retries; capture failures rethrow the final attempt's exception with its original type, and results return in deterministic source-type order regardless of completion order.
- `Reconciliation/StatementBreakClassifier.cs`, `StatementMappingProfiles.cs`, and `StatementToleranceProfiles.cs` - canonical break taxonomy, broker mapping profiles, and tolerance governance.
- `Reconciliation/ReconciliationEngineService.cs` - Security Master-enriched portfolio-vs-ledger
  reconciliation engine that joins positions, ledger balances, and the F# ledger reconciliation
  kernel.
- `Reconciliation/FileReconciliationDecisionJournal.cs` - crash-safe copy-on-write JSONL decision and resolution history persistence.
- `Reconciliation/Connectors/` - custodian/broker statement connector library (ADR-018):
  declarative versioned CSV/OFX mapping-profile documents with a file-backed store, live catalog,
  and format-drift detection; per-column mapping-confidence scoring; connectors for
  profile-driven CSV, OFX 1.x/2.x bank + investment statements, remotely fetched or uploaded IB
  Flex Report XML, fetch-capable Alpaca activity + portfolio snapshots with bounded complete
  pagination, and profile-less ISO 20022 camt.053 and BAI2 institutional bank cash statements
  (content-sniffed, mapped directly to canonical records); `StatementImportService` preview/commit
  orchestration that renders deterministic canonical-CSV artifacts into the existing
  statement-run workflow (positions, transactions, cash balances, fees, and dividends all
  classify per kind), retains a structured canonical-evidence sidecar for account margin, complete
  activity cursors, option lifecycle, tax-lot, and borrow evidence, and returns retained break ids
  plus structured reconciliation case links for the opened reconciliation work while retaining
  legacy case id/route arrays for compatibility; and
  persisted broker/custodian-classified fetch schedules with an idempotent schedule runner whose
  transient failures retain a stable non-sensitive status without advancing the last-successful-fetch
  watermark. Scheduled fetches reauthorize the retained tenant, company, fund, book, period, and
  external-account scope before provider access.
- `Banking/` - payment initiation, approval/rejection workflow, bank-side transaction records,
  deterministic transaction seeding, and PostgreSQL-backed banking persistence adapter.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes. Operations Continuity workflow state, command transitions, status derivation, persistence, audit hashing, reconciliation-break assignment/escalation, approval-policy rules, and close-calendar configuration live here so close, approval, report-pack, checklist-control, reviewer-independence, due-date, owner override, and retained audit policy remains part of Financial Operations rather than application orchestration or UI endpoints. The Financial Operations command-center read service also owns unified queue-row composition and deterministic status, owner, due/SLA, severity, blocker type, close/report impact, evidence, action, and route labels for browser and WPF clients.

Statement reconciliation also lives here. Broker/custodian statement intake, mapping profiles, validation, duplicate detection, matching, break classification, reconciliation decision journals, statement-run persistence, and durable case materialization are Financial Operations behavior. Application commands and shared UI services invoke the module workflow, but they do not own reconciliation state, matching rules, or statement-run persistence.

The statement connector library (`Reconciliation/Connectors/`, ADR-018) extends that intake seam: connectors parse CSV, OFX, uploaded or Web-Service-fetched IB Flex XML, and Alpaca snapshot sources into canonical records classified per kind (position, transaction, cash balance, fee, dividend), driven by declarative, operator-editable mapping-profile documents rather than code. Institutional bank cash statements are also ingested directly by the profile-less ISO 20022 camt.053 and BAI2 connectors (content-sniffed, closing-balance and signed entries mapped straight to canonical records) so most bank statements reconcile without hand-conversion. Commit renders a deterministic canonical-CSV artifact and hands it to `IStatementRunWorkflowService`, so the downstream matching, break, and case pipeline is unchanged and duplicate-key idempotency is preserved. A sibling `canonical-evidence.json` retains provider account margin, activity subtype and cursor completeness, option lifecycle, tax-lot, and securities-borrow evidence without widening the legacy reconciliation CSV seam. Profiles record the last accepted column layout for format-drift warnings, and fetch-capable connectors reuse the existing brokerage gateways and provider credential store — never a new secret store. Alpaca activity retrieval pages to a bounded complete cursor and fails closed if the provider cannot prove continuity. IB Flex uses the documented v3 request/retrieve flow with bounded polling and trusted-host enforcement. Persisted schedules retain an explicit broker/custodian source classification, support operator run-now and background cadence, and default legacy snapshots to broker; a failed fetch records only the exception type and advances a separate attempt/cadence watermark so provider or configuration failures do not retry every scheduler tick while the last-successful-fetch cursor remains available for recovery.

Statement ingress is bounded (PRD-010). Before this bound a caller-supplied `StatementSourceDocument`
sized the parse rather than the operator: `Camt053StatementConnector` built a whole-document
`XDocument` and `Bai2StatementConnector` split the entire payload on newlines, neither enforced a
record limit, and `StatementImportService` copied the source bytes before a connector was even
resolved — so the transport-level upload and CLI caps never covered that seam. `StatementIngressLimits`
is now one record shared by every connector and by the import service, so both refuse the same payload
and a deployment raises a cap in one place instead of per seam. Connectors refuse mid-parse, before
the allocation; the import service re-checks on preview, validate, and commit as the backstop no
connector can leave open — that check counts `StatementParseResult.TotalRetainedRows`, not
`Records.Count`, because the five evidence-only collections (account snapshots, activity events,
activity cursors, tax lots, borrow positions) are retained just as durably as canonical records.

`StatementIngressLimits.Default` bounds a document at `StatementConnectorLimits.MaxFileBytes`
(20 MiB — the statement-specific cap the workstation endpoint and CLI already enforce, deliberately
not the general 5 MiB data-upload cap, because IB Flex XML exports routinely exceed 5 MiB), 250,000
retained rows, 64 KiB per line, 64 levels of XML nesting, 50,000 nodes in any one materialized XML
subtree, 500,000 parsed nodes per document, 25,000 retained parse issues, and 500,000 flattened OFX
aggregates. Every bound refuses with a named code:

| Code | Bound |
| --- | --- |
| `STATEMENT_DOCUMENT_TOO_LARGE` | `MaxDocumentBytes`, checked by the import service and by every connector before it decodes — IB Flex included, which reported a private `STATEMENT_TOO_LARGE` until it took the shared limits, so a caller routing on this code missed Flex refusals alone |
| `STATEMENT_TOO_MANY_RECORDS` | `MaxRecords`, against total retained rows — charged on the append by every connector, and by the import service as the backstop. Nothing is charged against a prediction of what a payload will yield. camt.053, BAI2 and OFX previously charged *candidates* — an entry, detail line, or aggregate about to be attempted — so a pending camt entry, a malformed BAI2 amount, or an aggregate the mapper rejects consumed a record allowance it never drew on, and refused documents whose canonical rows sat well inside the bound. Work that retains no record is bounded by the budget that owns it instead. Alpaca charges its five evidence collections up front, since `Deserialize` has already materialized them, then one row per canonical append; a rich activity is retained twice (record and activity event) while a corporate action with no amount is not retained at all |
| `STATEMENT_TOO_MANY_ENTRIES` | `MaxDocumentEntries`, an UPPER bound on the objects a document could materialize before anything is mapped — raw OFX aggregates `OfxDocumentParser` flattens into entry dictionaries, and JSON objects the Alpaca pre-scan counts. Upper rather than exact: a deserializer skips unknown properties, so objects beneath a forward-compatible extension are charged here and never allocated. That is structural — a pre-scan must refuse before the allocation it prevents, so it can only bound what the payload contains, not what the deserializer keeps. Distinct from the record cap because the mapper rejects some aggregates, so aggregates and retained records are different counts; set above `MaxRecords` so an aggregate that maps to nothing does not consume a record's worth of the allowance. At the shipped defaults it cannot fire before `MaxParseNodes`: every object costs at least two tokens, so a 500,000-node budget is reached at roughly 250,000 objects. It is therefore an operator knob for deployments wanting a materialization ceiling stricter than the traversal budget, and only bites when set below about half of `MaxParseNodes` |
| `STATEMENT_LINE_TOO_LONG` | `MaxLineBytes`, measured in UTF-8 bytes |
| `STATEMENT_TOO_MANY_LINES` | `MaxDocumentLines`, the raw lines a line-oriented parser may walk, in both CSV and BAI2; refused before mapping. Blank lines count in both — they produce no canonical row but still cost an iteration to discover, and the byte cap alone permits twenty million of them. Only the synthetic final segment a terminating newline leaves is exempt, so acceptance does not depend on newline convention. CSV derived this from `MaxRecords` until 2026-08-30, which charged rows the mapper rejects to the record allowance one step removed |
| `STATEMENT_NESTING_TOO_DEEP` | `MaxNestingDepth`, inclusive — a document nested at exactly the limit is accepted and one level deeper is refused, identically in every connector that reads it |
| `STATEMENT_SUBTREE_TOO_LARGE` | `MaxSubtreeNodes`, one materialized XML subtree |
| `STATEMENT_TOO_MANY_NODES` | `MaxParseNodes`, the whole-document node budget, charged by the camt.053, OFX and IB Flex parsers, and by the Alpaca JSON pre-scan — one activity's `Metadata` dictionary is open-ended, so members have to be counted before `Deserialize` materializes them |
| `STATEMENT_TOO_MANY_DIAGNOSTICS` | `MaxDiagnostics`, retained parse issues; charged by every connector — the CSV, OFX, IB Flex and Alpaca row mappers, and the camt.053 and BAI2 per-row candidate charges, which also re-check after their parse loop so the final row's diagnostic cannot slip past |
| `ROW_LIMIT_EXCEEDED` | `MaxRecords`, reported by the IB Flex connector against its retained rows |

Preview returns these as issue objects, so a caller can branch on `issue.Code` directly. The other two
paths report as text — commit throws `InvalidDataException`, and `ValidateAsync` returns a
`StatementImportValidationResult` whose `Errors` is a list of strings — so both carry the code in
brackets ahead of the prose. Otherwise the same document yields an actionable code from one path and an
unclassifiable sentence from the others.

These messages advise raising the configured limit deliberately. A deployment does that by registering
its own `StatementIngressLimits` before `AddReconciliationServices`, since registration uses
`TryAddSingleton(StatementIngressLimits.Default)` and takes the first registration that wins:

```csharp
services.AddSingleton(StatementIngressLimits.Default with { MaxRecords = 1_000_000 });
services.AddStatementReconciliationServices();
```

Raise only the bound that actually refused, and record why: the defaults sit well above any real bank
statement, so a breach is far more often a malformed or hostile payload than a large one.

`MaxParseNodes` bounds how many nodes a parse walks, not how deep it goes or how large one subtree
is. It was defined for the XML connectors and, until this change, only OFX charged it: camt.053 could
walk hundreds of thousands of uniquely named shallow elements outside the single valid statement, with
the reader's name table retaining every distinct name string, and no bound fired. Both camt passes now
charge it, and IB Flex charges it in a streaming pre-scan ahead of the `XDocument` it still builds:
`MaxCharactersInDocument` bounds the characters read, not the object graph built from them, so a
permitted payload of many tiny elements could expand well past its own byte size before any row counter
existed. The pre-scan allocates nothing and refuses first.

`IbFlexStatementConnector` reads `MaxRecords` and `MaxDocumentBytes` like every other connector. It previously held a private
100,000-row ceiling, which made the paragraph above false for Flex imports: a deployment could raise
`MaxRecords` and still have a legitimate Flex report refused at row 100,001 by a number it had no way
to configure. Both bounds counted the same thing - retained rows - so they are now one bound. This
raises the default Flex ceiling from 100,000 to the shared 250,000; a deployment that wants the old
ceiling sets `MaxRecords` to 100,000. The same applied to document size, which kept a private 32 MiB
ceiling for one more round: it now reads `MaxDocumentBytes`, moving the Flex default the other way,
32 MiB down to 20 MiB. On the import path the service already refused above 20 MiB before the connector
saw the document, so that tightening binds only the direct fetch path.

The shared Margin Control Center reads retained canonical evidence across providers, accounts, and
prime brokers. Provider-reported buying power, maintenance margin, excess liquidity, and restriction
flags remain authoritative. Meridian displays a clearly labelled Reg T or portfolio-margin shadow
estimate only as a diagnostic comparison, never as liquidation or posting authority. Intraday
snapshots are provisional; end-of-day certification is permission checked and blocked for stale,
incomplete, or critical evidence.

The UI Shared Statement Reconciliation Report intake adapter binds retained imports to an exact
fund, ledger-book, open accounting-period, and as-of scope, starts or reuses the matching Operations
Continuity workflow, and projects source obligations into the existing canonical reconciliation
queue. Those are adapter actions into existing authorities. This module's statement-run, source-case,
and Operations Continuity services continue to own reconciliation state and
posting/approval/close gates; `IReconciliationBreakQueueRepository` and
`IStatementReconciliationCaseworkHandoffService` own governed queue mutation and evidence
synchronization. The adapter and its casework handoff may attach retained evidence, but they do not
post, approve, or close on an operator's behalf.

The statement-run workflow reconciles each imported statement against Meridian's own book rather than against itself: `StatementRunWorkflowService` resolves internal positions, cash, and ledger transactions through `IInternalReconciliationPopulationProvider` (default: an empty book, so every row is a genuine unmatched break) and runs the shared `StatementMatchingEngine` across positions, cash, and transactions in exact / tolerance / candidate / unmatched tiers. Foreign-currency amounts normalize to the reporting base currency through a fail-closed `IReconciliationFxRateProvider` (identity-only by default, so cross-currency lines break unless a rate is configured); cash matching retains its original currency identity after conversion so distinct per-currency balances cannot cross-match. Both statement-only and internal-only records surface as breaks with a truthful tolerance-breached flag and engine-sourced confidence. A real `IFxRateProvider` implementation (`InMemoryFxRateProvider`, identity/inverse/triangulation with as-of selection) is available to the execution and ledger layers.
Import services capture bounded raw and canonical bytes once, compute authoritative SHA-256 values
from those snapshots, validate any caller-supplied hash only as an assertion, and parse the same
captured bytes. Canonical CSV rendering uses reversible RFC-style quoting for commas, quotes, and
line breaks rather than replacing source characters. Raw uploads are retained under a portable,
single-segment filename; traversal-shaped names keep only a valid basename, while dot segments and
reserved device names use a deterministic safe fallback. Every retained path is resolved beneath
the configured data root and refuses existing symbolic-link or reparse-point traversal. The
retained duplicate key binds the raw and canonical hashes when they differ. Upgrade duplicate
detection also checks the prior canonical-only identity and returns that retained run id, preventing
the first post-upgrade retry from creating a second run for an already imported artifact.
The commit result also carries the specific break ids and structured reconciliation case links
created by the Financial Operations workflow, including each case route, status, priority, reason,
and suggested next action, allowing Evidence Vault and browser clients to point operators directly
at the retained casework instead of only showing aggregate break/case counts.

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
Operations Continuity transition commands compose the shared four-state
`VerifiedOperationOutcome`; the compatibility `Success` property is derived from that receipt and
cannot contradict it. Accepted aggregate changes and their outcome-bearing audit event use one
authoritative transition commit: PostgreSQL uses a serializable database transaction, the local
file store atomically replaces a workflow-plus-timeline commit envelope, and the in-memory store
coordinates both structures under one admitted commit. Precondition and policy blocks retain an
unchanged-state `workflow-transition-blocked` audit event with structured issues, source references,
and recovery actions. A commit exception returns `Failed`, retains no succeeded workflow snapshot
or succeeded audit receipt, and attempts a separate truthful failure receipt without upgrading the
result when that secondary retention is unavailable. Workflow snapshots remain rebuildable current
state; the append-only, hash-chained audit and embedded terminal receipts are the durable decision
evidence.
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
mutated after closure. Terminal casework distinguishes resolved, waived, and
superseded dispositions; material waivers and supersessions require independent approval evidence,
and supersessions retain the successor break identifier. Value, quantity, and cost-basis measures
remain attached to the case and its terminal evidence hash rather than collapsing into one amount.
Break assignment, escalation, and resolution commands also refresh the
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
retained evidence links, frozen Evidence Vault document snapshots, document object links, immutable
document source hashes, and checklist-control approvals; request-supplied hashes are compatibility
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
Malformed, null, or incomplete close-management snapshots fail closed and remain untouched for
recovery; a later sign-off cannot reinterpret missing slices as empty and overwrite retained close
evidence. Once a service has observed or written the durable snapshot, disappearance of that file
also fails closed instead of being treated as first-time initialization.
The final close-plan control is the shared `Post closing entries` gate. After the existing task,
sign-off, evidence, and version checks pass, the management service projects the current scoped
revenue/expense residual, queues the deterministic closing-entry draft into the governed workbench,
and waits for independent submit, approval, and posting. It rechecks that gate at the hard-close
mutation boundary and finalizes the actual ledger period before publishing the workflow close
package; an unavailable workbench, an unapproved current draft, a pending reversal, or any residual
temporary-account balance fails closed. The projection fingerprint makes retries reuse the same
draft while a late approved adjustment queues only its new closing delta. Governed reopen requires
human Controller authority plus retained restatement approval evidence, creates or reuses every
source-linked closing-batch reversal draft before moving the ledger period back to soft close, and
supports deterministic retry if the first reopen attempt stopped after draft creation.
Close blocker/evidence reviews are retained in the same close-management snapshot as explicit
operator review records; they require human origin, notes, scoped close-review/blocker evidence
that identifies the active issue, target, workflow or period, and selected ledger book, and they do
not clear the underlying validation blocker or satisfy period-lock gates.
Close-period plan configuration is also governed through this service: human-origin commands can
retain materiality policy setup, task display/owner/due-date overrides, role-scoped approval counts,
required evidence text, role-scoped sign-off matrix rows, and explicit task dependencies only when retained close-plan setup evidence
names the workflow or exact period and selected ledger book on the same artifact; requests that carry
the loaded configuration timestamp are rejected when a newer retained setup version already exists.
Task sign-off decisions retain authenticated actor, role, notes, and evidence, reject duplicate
actor-role decisions, actors who acknowledged the task, roles outside the task's sign-off matrix,
or incomplete prerequisite tasks, require retained approval/sign-off/control/review evidence that
identifies the close task, workflow, sign-off role, and workflow or exact close period on the same
artifact, count only
approved decisions toward the configured role-scoped approval cap, and promote the close task only when
retained approved decisions satisfy the configured role-scoped task approval count. Rejected retained sign-off
decisions block the close task, close-calendar milestone, and close-plan validation until operators
remediate the failed control; additional role decisions are rejected while that retained rejection
is still active. Each projected close
task now carries sign-off requirement rows so browser, WPF, report
certification, and export workflows can inspect required role, required approval count, approved
count, satisfaction state, and required evidence without rebuilding close matrix rules locally.
Unsatisfied close sign-off requirements also emit critical close-plan validation blockers even when
the upstream checklist row is marked done, so close cockpit, report certification, and period-lock
consumers cannot treat workflow task completion as retained approval evidence.
The same close plan carries close-calendar milestone rows derived from checklist due dates,
dependencies, sign-off counts, evidence, blockers, and period-lock state so accounting/reporting
surfaces can render calendar posture without reinterpreting workflow checklist rows. It also carries
service-owned operating coverage rows that summarize close-plan setup, dependency graph,
sign-off-matrix, late-adjustment, blocker-review, and period-lock readiness from the same validation
issues and retained evidence used by the lock gate, so browser and WPF clients do not infer close
operating coverage from scattered task or validation rows. The late-adjustment command remains a governed close review artifact; it does not
mutate posted journal entries and material adjustments require controller approval before final
close certification. Late-adjustment requests require retained late-adjustment evidence that
identifies the journal entry, workflow, or exact close period on the same artifact before a row is
stored, and review decisions are retained with authenticated actor, decision notes, and approval,
rejection, decision, or review evidence that identifies the retained request, journal entry,
workflow, or exact close period on the same artifact; generic close support evidence and split
support/provenance links are rejected for the governed request/review gates. Duplicate retained requests for the same journal entry within a
close workflow, duplicate decisions, and decisions after close-package period lock fail closed.
Pending material late adjustments now emit critical close-plan validation blockers until a
controller review approves or rejects the retained request, keeping period-lock and report-package
certification consumers from treating unresolved material adjustments as advisory close notes.
The same service exposes a governed close-period lock command that requires human-operator origin,
current workflow version, scoped close-package/report-pack/period-lock evidence, linked report
package id, and a close plan with no critical blockers before delegating to
`IOperationsContinuityWorkflowService.CloseWorkflowAsync`. The result carries the updated close
plan, the underlying operations transition, and service-owned blocking issues so browser, WPF, and
shared endpoints can show the same lock readiness without issuing the operations close command
directly.
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
a fund-period aggregate. Missing retained report evidence is a critical package blocker and is
carried into the report-evidence readiness row, so financial statements, NAV, restatement, and
export artifacts cannot appear ready for review without retained ledger, reconciliation,
rendered-report, and NAV support evidence. Standalone package evidence that names a different
ledger book is also carried into that readiness row so operator review surfaces see wrong-book
support evidence instead of only a package-level validation issue. Package dimension mismatches are
also projected through a dedicated report-dimension-scope readiness row, covering fund, ledger-book,
investor, capital-account, and explicit dimension-scope blockers before certification.
Report export readiness also fails closed when retained export artifacts are missing evidence,
content hashes, ledger-book alignment, or package dimension-scope alignment, so downstream
certification surfaces do not treat artifact retention as complete from certification state alone.
It blocks certification when close-plan evidence is missing, close checklist dependencies are
incomplete, the attached close workflow has not reached period-lock, approved sign-offs are missing,
or material late adjustments are still unapproved. It also blocks restatement certification when
retained certified prior-package lineage or retained restatement evidence is missing, requires
restatement lineage evidence to name the exact prior package or certification id being restated, and
retains package history through an atomic JSON snapshot when `StorageOptions` is registered.
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
Typed instrument-to-journal fields are additive assertions on that same candidate path.
`AccountingBookContextDto` is re-resolved through `ILedgerBookService`, including book owner,
period, basis, policy/version, currency, fund, and dimension scope; the client snapshot never grants
posting authority. Economic-event and projection references must agree with retained source-event
identity, evidence, effective date, instrument identity, and `BookPositionId`, while candidate and
generated-line `PositionId` dimensions must match. `AccountingRulePackReferenceDto` is validated
against the existing accounting policy rule pack and selected Rules Studio rule/version rather than
creating another rule authority. Any mismatch remains a blocking candidate issue before draft/write
creation.
Security Master remains the canonical source of instrument identity, Instruments/Asset Operations
own economic projections, and this module owns the governed candidate/approval handoff. These
optional fields add no Financial Operations persistence, direct ledger-entry input, or alternate
posting route; the approved immutable `JournalEntry` remains the accounting aggregate.
`AssetAccountingEventSpineService` generalizes that authority boundary across Acquisition,
Capitalization, Valuation, Income, Corporate Action, Impairment, Depreciation/Amortization, and
Disposal. It re-reads the immutable Projected spine version, authoritative book position, ledger
book, period version, accounting policy, and promoted rule pack, rejects any client assertion drift,
and appends Drafted only after Rules Studio returns a balanced approval-gated candidate. Generic
`AssetAccounting.*` candidate requests are rejected so callers cannot bypass this server-owned
authority path.
For the MBS factor-paydown model, candidate creation re-resolves the persisted holder role, book
position, factor economic state, and projection lineage, reruns the Instruments projector, and uses
the server amount for Rules Studio. Missing or stale projection state, cross-book identity, evidence
drift, event/lineage drift, a missing or mismatched authoritative rule-pack reference, or a
client-supplied amount mismatch blocks the candidate before approval. Factor detection is anchored
to the canonical event type as well as projection lineage, so omitting or relabeling a client field
cannot bypass server recalculation.
`AccountingPostingCandidatePostService` is the separate append gate for approved generated
candidates. It requires a configured Postgres-backed `ILedgerJournalStore`, a human-operator action
origin, retained source-event identity, approval evidence, an aggregate id equal to the target
ledger book, a pending approval-gated posting command, a matching ledger book/accounting basis,
journal metadata that names the approved ledger book, retained line dimensions whose book scope
matches that ledger book, and a period owned by that book before calling the journal store. Replays
for the same `(ledger book aggregate, source event)` return the existing journal, while the same
economic event may still produce separate GAAP, cash, tax, statutory, or primary postings because
each basis uses its own ledger-book aggregate.
Canonical asset acquisition and disposal candidates additionally require the Postgres atomic
tax-lot journal store. Acquisition creates the new lot in the journal transaction; disposal consumes
explicit selected lot ids under exact expected-version/open-quantity CAS. Both paths retain the
mutation fingerprint, evidence, relief policy, before/after snapshots, correction lineage, and
idempotent replay result. The event spine records Approved and Posted only from the durable journal
identity and balanced posted amounts returned by that boundary.
For this spine, a same-source journal is a replay only when its deterministic journal identity,
complete Drafted candidate/result fingerprints, policy/rule pack, approval evidence, amounts,
lines, currencies, and dimensions all match. Lots retain Security Master and book-position scope;
disposal rechecks selected unit cost and aggregate cost basis against the exact asset-relief journal
line under the same serializable transaction. A mismatch is a collision and blocks posting.
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
the selected tenant, company, fund, ledger book, and the specific certified control family. Each
positive control requires a complete typed retained-evidence identity; boolean flags, service or
endpoint availability, legacy full-token links, and synthesized profile/report references cannot
certify the profile.

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
| `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `W10-RECON-003` | Unified tolerance model and what-if replay workbench |
| `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `W10-PERF-001` | Portfolio and investor return measurement |
| `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~Reconciliation.Connectors" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

`IOperationsContinuityWorkflowService` publishes account-period close workflow commands and reads. `IOperationsContinuityRepository`, `IOperationsWorkflowAuditStore`, `IOperationsContinuityTransitionCommitStore`, `IOperationsContinuityWorkflowStartCommitStore`, and `IOperationsContinuityTransactionalCommitStore` publish workflow persistence and atomic transition/audit/ledger commit contracts. `IOperationsApprovalPolicyMatrixService` publishes the policy matrix consumed by shared workstation endpoints. `IOperationsCloseCalendarService` publishes close-calendar reads and governed item upserts. `IPrivateCapitalCloseCockpitService` is implemented here to publish the contract-owned close cockpit projection while endpoints remain in UI Shared. Accounting-close services publish journal posting, FX translation, trial-balance, roll-forward, and evidence-gate projections. `IAccountingPolicyService`, `IAccountingBasisProjectionService`, and `IAccountingBasisProjectionSetService` publish accounting-basis policy lookup, ledger write metadata projection, and one-source-event-to-many-book projection candidates for application workflows. `LedgerTextJournalReportService` publishes CLI-facing text-journal parsing and report rendering. `AccountingSystemIntegrationService` publishes provider listing, import preview/latest import, and latest external-GL reconciliation reads over `IAccountingSystemProvider` contracts. `IBankingService` publishes payment approval records, direct payment lookup, explicit bank-evidence recording, and bank-transaction evidence workflows over `Meridian.Contracts.Banking` DTOs. `IStatementRunWorkflowService`, `IStatementReconciliationService`, `IStatementReconciliationOrchestrator`, `IStatementValidationService`, and reconciliation repository contracts publish statement intake, validation, matching, persistence, and casework orchestration for commands and UI services. DTOs remain in `Meridian.Contracts.Workstation`, `Meridian.Contracts.AccountingSystem`, `Meridian.Contracts.Banking`, and `Meridian.Contracts.Ledger`; authorization roles and permissions come from `Meridian.Identity.Auth`; durable local writes use `Meridian.Storage.Archival.AtomicFileWriter` and banking persistence uses `Meridian.Storage.Banking`.
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
