---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CONTRACTS
path: src/Meridian.Contracts
status: active
owner_lane: Contract Compatibility
last_reviewed: 2026-06-06
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
- `Backfill/` - shared historical backfill run-result and per-symbol completeness signal payloads.
- `FundStructure/` - fund-structure command, query, DTO, ownership lifecycle, and graph-validation payloads.
- `Plaid/` - Plaid provider, account-link, transaction, investment, identity, webhook, and transfer DTOs.
- `Services/` - cross-module service contracts such as backtest preflight and Security Master
  validation gates, fund-structure graph/query orchestration, operational scheduling/trading
  calendar coordination, plus Environment Design draft/publish/runtime projection contracts that
  must be injectable without depending on Application implementation types.
- `Etl/` - shared ETL DTOs, the job-definition store contract, and the SFTP publisher port used by
  Application orchestration, Data Integration ETL services, Infrastructure adapters, and
  Storage-backed persistence.
- `Monitoring/` - shared event-pipeline metrics contracts and snapshot payloads consumed by
  Application, Platform tracing, diagnostics endpoints, WPF, and browser workstation services.
- Contract DTO files - shared payloads consumed across host, UI services, desktop, and dashboard.
- Project metadata - serialization and package references for contract consumers.

## Important workflows

Treat additive and breaking changes as cross-module compatibility work. Operations Continuity
workflow DTOs publish the shared broker intake, Security Master, ledger posting, reconciliation,
approval, close, and audit vocabulary consumed by both browser and WPF workstation clients. Keep
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
The shared audit-pack readiness model exposes completeness, missing category keys, warnings,
evidence-category summaries, measured generation seconds, a 60-second SLA target, and SLA pass/fail
posture. Older report-pack manifests may omit readiness; clients must treat that as unknown or
incomplete rather than invalid. Each category also carries contract-owned required evidence labels
so browser and WPF clients can display the source, normalized activity, reconciliation, ledger,
approval, document, export, and restatement requirements without parsing status prose.
Evidence workflow linkage and vault lookup DTOs include `AccountingRecordId` so retained
accounting-record manifests can be indexed and queried as first-class audit records, not only by a
generic evidence subject string.
`FundOperationsNavigationContext` also carries optional evidence subject metadata for shared
evidence routes such as `EvidenceWorkbench:accounting-record/{recordId}`, allowing browser and WPF
clients to preserve the subject and source target while resolving to their local audit surfaces.

Historical backfill contracts include the shared run outcome and per-symbol validation/completeness
signals consumed by Application orchestration, Storage status persistence, endpoints, tests, and
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

Report-pack workflow contracts carry the W4 governed lifecycle states `Draft`, `InReview`,
`Approved`, and `Published` plus governed publication metadata: sign-off actor, evidence hash,
retained manifest path, retained evidence links, report-line provenance, create requests, publish
requests, explicit `Rejected` state support, explicit review-state rejection requests with reason,
actor/role, and optional evidence-link metadata, and restatement requests with approver,
prior-version, changed-line, and evidence-link metadata.
Workstation Reporting run rows carry typed drilldown links and next-action references so browser
and WPF clients can distinguish open evidence routes from reference-only approval, publication, and
restatement actions without parsing artifact strings.
Workstation and fund-operations reporting summaries expose `reportPackDistributions` as
recipient-level distribution records instead of static `reportPackTargets` strings. Clients should
render recipient, role, channel, owner, state, due time, pending item count, and pending summary so
operators can see who receives each package and what is still waiting on approval, publication, or
delivery.
Report template contracts now also carry the governed authoring lifecycle for built-in and custom
template versions: draft requests, review submission, approval/rejection decisions, immutable
built-in markers, latest-approved posture, validation issues, approval references, and audit events.
Keep those template lifecycle fields shared so Reporting, browser, WPF, and endpoint tests use the
same version-approval vocabulary instead of maintaining client-local template state.
Pilot readiness contracts also carry W4 acceptance evidence categories and roles so acceptance proof
can be distinguished from evidence-vault manifest/export support in serialized artifacts.
Report-line provenance carries the reported value plus run, source-session, ledger-entry,
provider-event, Security Master definition, reconciliation-case, reconciliation-run,
reconciliation-outcome, and approval pointers so each retained line can be traced back to the source
workflow evidence before publication. Generated report-pack lineage pointers also carry optional display labels, source-system
tags, related ledger or journal evidence IDs, line amounts, latest evidence timestamps, and API
routes back to run continuity, ledger trial-balance, reconciliation, and Security Master search
evidence. Keep these fields shared so browser, WPF, and service tests enforce the same publication,
drilldown, and no-orphan-evidence rules.

Fund-structure contracts include the shared entity setup draft, validation summary, graph preview, and create-result payloads used by WPF, browser, and `/api/fund-structure` to create organization, business-lane, client/fund, legal-entity, vehicle, investment-portfolio, ownership, and account-handoff records without UI-local command vocabulary. The shared `IFundStructureService` contract lives in `Services/` so browser, WPF, Identity scoped-access lineage, endpoint, and composition consumers can depend on the fund-structure orchestration contract without depending on Application implementation types. The shared `IFundAccountTraversalQueryService` contract also lives in `Services/` so fund-account endpoints can use the same authoritative Fund -> Owns -> Account traversal contract while Application keeps the current cached implementation. `IGovernanceSharedDataAccessService` is contract-owned for the same reason: governance structure views should consume a shared Security Master, price, and backfill accessibility summary shape while Application keeps the current implementation. Fund-structure contracts include the ledger mapping workbench payload used by Accounting
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

Event-pipeline metrics contracts live in `Monitoring/`. Keep `IEventMetrics` and
`MetricsSnapshot` contract-owned so the Application default metrics implementation, Platform
tracing decorator, diagnostics endpoints, WPF shell, and shared browser endpoints can depend on the
same metric shape without introducing Application-layer dependencies.
Operational scheduler contracts live in `Services/`. Keep `IOperationalScheduler`,
`ITradingCalendarProvider`, operation types, resource requirements, scheduling decisions, slots,
trading sessions, and maintenance-window records contract-owned so scheduling behavior can be
implemented in Platform while tests, future hosts, and operator surfaces consume the same
scheduler shape.
Evidence Vault identities also expose retained artifact metadata for file-backed evidence bundles:
storage kind, artifact id, kind, relative vault path, content hash, retained size, source route, and
canonical subject linkage. Keep that metadata shared so packet, report, approval, screenshot,
statement, and validation producers can enforce the same retained-artifact vocabulary.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Prefer additive DTO changes when possible. Update shared compatibility tests and generated docs when
contract shape, blocker vocabulary, or route-visible payloads change.

## Related docs

- `docs/status/contract-compatibility-matrix.md`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
