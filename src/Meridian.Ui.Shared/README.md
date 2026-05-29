---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SHARED
path: src/Meridian.Ui.Shared
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-05-28
---

# src/Meridian.Ui.Shared

## Purpose

UI shared contains shared UI read models and compatibility shims for browser and desktop surfaces.

## Layer responsibility

This module owns cross-surface operator-facing projection types and shared endpoint helpers. Preserve
compatibility across `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard`, and
`src/Meridian.Wpf`.

## Key folders and files

- `Endpoints/` - shared workstation endpoint mapping and projection helpers.
- Shared read models - DTOs and compatibility shims consumed by browser and desktop clients.
- Project metadata - UI shared dependencies and build settings.

## Important workflows

Preserve cross-surface compatibility when evolving shared read models. Keep ledger/reconciliation
source-of-truth services authoritative. Workstation endpoint registration is split by domain through
`WorkstationEndpoints.*.cs` partial files. Keep the root `WorkstationEndpoints.cs` file as the
coordinator, route new domain-specific endpoint edits to the matching partial file, and avoid
concurrent branches that both modify the root coordinator or the shared
`WorkstationEndpointsTests.cs` test body. For operations-continuity and reconciliation endpoint
changes, start with focused `MapWorkstationEndpoints_OperationsContinuity` /
`MapWorkstationEndpoints_Reconciliation` filters before broad workstation endpoint validation.
The shared workflow library owns close-lane command routing as well: `AccountingReviewOperationsContinuity`
targets `OperationsContinuity` and `AccountingReviewCloseReadiness` targets `OperationsClose`, with
route metadata tied to the operations-continuity API. Browser and WPF clients should consume those
target tags instead of inventing client-local close-workflow routes.
Run comparison endpoints consume contract-owned compare and diff payloads from
`Meridian.Contracts.Workstation`; keep request/result schema additions in contracts and let this
layer focus on endpoint validation, dependency resolution, and service orchestration.
Diff responses now include strategy id/version metadata, lineage relation, compatibility level,
engine/mode context, artifact completeness, and warnings so operators can compare strategy
versions and execution engines without inferring risk from run names alone.
Lean result ingestion uses `CanonicalBacktestResultNormalizer.FromLeanResult` from
`Meridian.Backtesting.Sdk` so imported QuantConnect runs enter the same `BacktestResult` storage
and comparison model as native Backtest Studio output. Summary-only Lean imports must retain their
coverage warnings so Research/Portfolio compare and diff views do not imply fill, cash-flow,
attribution, or ledger parity that was not imported.
The shared Investment Accounting Transaction Lab service previews Books Before Broker accounting
impact for trades, dividends, fees, accruals, corporate actions, and broker-reconciliation examples.
It emits balanced expected-journal candidates, trial-balance deltas, ledger-impact flags, and
reconciliation expectations through `/api/fund-structure/accounting/transaction-lab/preview`, so
browser and WPF clients can consume the same accounting lab contract instead of rebuilding
accounting rules locally.
<<<<<<< ours
Report-pack workflow state is shared here as well: the W4 path moves `Draft` packs to `InReview`
through submission, then to `Approved`, and finally to `Published`; publication requires sign-off,
evidence hash, retained manifest metadata, and retained evidence links for every report-line
provenance pointer so browser and WPF clients do not invent local lifecycle or no-orphan-evidence
rules. The shared fund-structure endpoints expose report-pack workflow creation, validation,
submission, approval, publication, restatement, history, and archival routes backed by shared
contracts; restatement
=======
Report-pack workflow state is shared here as well: publication requires sign-off, evidence hash,
retained manifest metadata, and retained evidence links for every report-line provenance pointer so
browser and WPF clients do not invent local lifecycle or no-orphan-evidence rules. The shared W4
acceptance filter keeps governed report-pack acceptance evidence separate from evidence-vault
manifest/export support so pilot readiness cannot mark W4 done from support artifacts alone. The shared
fund-structure endpoints expose report-pack workflow creation, validation, submission, approval,
publication, restatement, history, and archival routes backed by shared contracts; restatement
>>>>>>> theirs
changed lines must carry evidence links before the workflow can advance. Publication also rejects
line provenance that omits the reported value or lacks a run, source-session, ledger-entry,
reconciliation-case, or reconciliation-run pointer, keeping value-level report lineage enforceable
in the shared service instead of client code.
Generated governance report packs enrich line-level provenance with display labels,
source-system tags, related ledger and journal evidence IDs, line amounts, latest evidence
timestamps, and API routes back to run continuity, ledger trial-balance, reconciliation, and
Security Master search evidence so report consumers can drill into accounting support without
client-local route inference. The shared ledger amount provenance service exposes those retained
lineage pointers as a click-through drilldown for a report-pack ledger amount, combining the ledger
line, strategy/run evidence, Security Master pointer, reconciliation summary, durable case ids,
related case status/owner/sign-off posture, approval state, and report usage. When a retained report
line carries a retained Security Master id, the drilldown uses that id to pull in open Security
Master exception cases for the same instrument. When a retained report line does not carry a direct
provider-event pointer, related provider-ledger cases can contribute provider-event evidence from
their upstream provider sync cursor and route metadata. Corporate-action and factor casework also
contributes structured provider event id/type, required feed, provider evidence source, and Security
Master id metadata to the drilldown. Provider-ledger
corporate-action/factor casework now also retains ledger-effect metadata, so the drilldown surfaces
the valuation or journal-support kind, principal/income amount, and journal
preview line count so report-line users can see how provider factor or cash activity supports the
ledger amount. Warnings remain only when neither retained lineage nor durable casework can identify
provider evidence for the amount. Ledger amount provenance also projects retained strategy/run
lineage into structured run links, including run id, display label, route, source system, capture
time, and whether the run pointer was captured at the selected line scope. Browser and WPF
drilldowns can therefore show the strategy/run origin directly instead of parsing generic evidence
rows.
Evidence packet validation also owns the shared SLA/freshness policy and Meridian Assurance Score
calculation for provider validation, replay, reconciliation, approval, and reporting evidence so
client surfaces consume the same readiness posture instead of recalculating it locally.
The file-backed Evidence Vault now stores more than manifest retention: retained local artifact
refs with file paths are copied into a vault bundle with content hash, size, source route, and
canonical subject metadata, while route-only artifacts stay as manifest references. The vault write
boundary rejects retained payloads that omit canonical subject linkage or use unsupported subject
kinds, so copied statement/report/approval/screenshot artifacts cannot become orphan evidence. This keeps
packet/report/statement/screenshot/approval evidence retention server-owned instead of
client-local.
The shared Audit Trail Explorer service projects retained execution, promotion, order, control, and
Operations Continuity close/reconciliation/approval timeline records into contract-owned timeline rows and exposes `/api/execution/audit/search` with
server-side text, run, actor, symbol, action, outcome, correlation, time-window, and limit filters.
Use that shared service for browser and WPF audit search rather than client-local timeline
normalization.
The Security Master workstation workbench also exposes a shared Instrument Passport at
`/api/workstation/security-master/securities/{securityId}/passport`. The passport reuses the
server-owned trust snapshot to combine identity, provider mappings, lifecycle events, corporate
actions, pricing/trading-parameter readiness, downstream usage, and trust posture for browser and
WPF clients. Each provider mapping also carries a confidence row with source, freshness, confidence
score, related identifier-conflict IDs, conflict summaries, and override history so clients can show
provider-to-Security-Master trust without rebuilding mapping logic locally.
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
can distinguish a resolved but stale provider mapping from a fresh resolved mapping. When the shared
break queue is registered, those stale resolved mappings seed governed Security Master cases with
provider sync cursors and steward sign-off metadata. When the
Security Master operator override store is registered, the passport also carries the latest override
audit-history entries for the resolved instrument so controller review can see provider mapping
confidence and governed override context in the same retained reconciliation record.
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
checklist control posture, report-pack readiness, and route-only approval/rejection links so browser
and WPF clients do not duplicate close sign-off evidence logic.

Fund-structure endpoints expose `/api/fund-structure/ledger-mapping-view` as the shared accounting
control surface for account ledger mappings. The endpoint returns server-derived assignment source,
unmapped-account issue codes, and recommended action so browser and WPF surfaces do not invent
client-local mapping or posting readiness rules.
Auth endpoints expose `/api/auth/role-profiles` as the governed write path for custom authority
profiles. The shared file-backed role-profile store persists profile grants under the storage root,
merges custom profiles into `/api/auth/roles`, and feeds `UserProfileRegistry` so configured
`roleProfileName` accounts use the stored permissions after login.
Operations Continuity endpoints expose
`/api/workstation/operations/continuity/approval-policy-matrix` as the shared configuration read
model for approval governance. The endpoint is read-permission protected and returns the
server-owned approval actions, required permissions, reviewer independence, report-pack, checklist,
and audit-event metadata used by Settings, browser, and WPF surfaces.
`/api/workstation/operations/continuity/approval-policy-rules` is the admin-protected governed
write path for approval-policy rule edits. It trusts the authenticated session actor over the
browser payload, validates required approval counts and route shape, persists overrides through
the application service, and returns the updated matrix plus audit event, rationale, and
correlation evidence.
`/api/workstation/operations/continuity/close-calendar` exposes the account-close calendar read
model with optional fund-account and period filters. It returns server-derived next due task,
owner, readiness score, component breakdown, provider-freshness blocker, next-action,
approval-count, and workflow route metadata so close calendars stay aligned with Operations
Continuity rather than client-local date calculations.
`/api/workstation/operations/continuity/close-calendar-items` is the admin-protected governed
write path for calendar owner and due-date configuration. It validates the target workflow and
checklist task, trusts the authenticated session actor, persists the override through the
application calendar service, and returns the updated calendar item plus audit event and
correlation evidence.
`/api/workstation/operations/continuity/{workflowId}/close-readiness` exposes the same
controller-facing readiness score used by workflow detail and close calendar payloads. It keeps
provider freshness, Security Master completeness, ledger posting, reconciliation-break,
report-pack, and approval blockers server-owned so browser and WPF clients do not recalculate close
status.
Closed Operations Continuity workflow detail payloads include the governed close-package
publication manifest metadata produced by the close command: signer, sign-off rationale, retained
manifest id/route, evidence hash, report pack id, linked evidence, and checklist approvals.
`/api/fund-accounts/{accountId}/close-readiness` exposes the account-scoped close-readiness score
for provider-ledger workflows. It aggregates account readiness, provider freshness, Security Master
coverage from the latest provider-ledger reconciliation, internal ledger balance evidence,
corporate-action/factor-schedule readiness, internal ledger balance evidence, account-scoped break
queue casework, and pending sign-off state into weighted components, blockers, and next actions for
controller-facing close review. Fixed-income and structured provider positions now require direct
retained provider factor-schedule, loan-schedule, or matched principal/paydown ledger-effect evidence before the
corporate-action/factor component can be marked ready for close. Retained shadow-book comparison
breaks also keep the reconciliation
component in review-required state so provider/custodian statement variances cannot close merely
because the aggregate provider-ledger checks matched.
When provider-routing capability metadata is registered, the reconciliation service blocks runs
that cannot route account balances, account positions, or reconciliation-feed capability for the
fund account, so unsupported providers fail as accounting break evidence before ledger comparison.
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
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SHARED -->
- No registry-backed TODOs are open for this module.
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
