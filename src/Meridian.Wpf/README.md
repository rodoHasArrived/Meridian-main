---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-WPF
path: src/Meridian.Wpf
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-09-05
---

# src/Meridian.Wpf

The desktop workstation is installed as part of the single Meridian product and opened
on demand from the browser workstation. It is not a separate end-user package or Start
Menu product.

## Shared close and lot convergence

Fund Ledger carries its explicitly selected book/account/entity/period context to the shared command-center service. Both the queue and private-capital close headline consume the shared decision; clear local lane inputs cannot establish close readiness. The browser and WPF use the same contributor manifest and blocking rules.

Account, aggregate, strategy-run, and trading position presentations use `MarkFreshnessPresentation` over the shared assessment. Observation date, age, and review reason remain visible in rows and inspectors. An absent mark date is unknown evidence, even when the enclosing position snapshot is recent. Close acceptance exercises recovery using the shared decision and authoritative subject scope.

Operations Continuity and Accounting Close require explicit fund, book, account, entity, and period selections for close evaluation. Preparation remains available without close scope. The desktop shared publication guard reads the current authenticated session; missing tenancy, sign-out, or unavailable authoritative evidence blocks publication. Changing the selected subject or workflow invalidates prior readiness and pending results.

Accounting Close resolves `IWorkstationAccountingCloseApiClient` to
`WorkstationAccountingCloseApiClient` in the Accounting feature module. Its plan reads and
governed commands use the server HTTP endpoints; the server resolves authenticated authority
and applies the shared close guard before locking or publication. The registered-screen
recovery scenarios in `AccountingCloseHttpRecoveryTests` retain the selected workflow across
evidence refusal and refresh after repair. Close-readiness acceptance remains in progress
pending the required hosted integration checks.

## Purpose

WPF workstation is an active Windows desktop operator workstation and a co-equal UI lane alongside
the browser workstation. It projects the seven canonical workspaces over shared contracts and read
models; its current lane focus is closing web-UI parity gaps (`W8-WPF-PARITY-001`, see
`docs/development/wpf-web-ui-alignment-plan.md`) without forking product state.

## Layer responsibility

This module owns the retained desktop shell, WPF pages, route hosting, and desktop workstation view models.
Keep shared contracts and read-model logic in shared UI services when browser and desktop both need
the behavior. The seven operator workspaces now register through feature modules under
`src/Meridian.Wpf/Features/` so new workspace-level navigation and shell ownership lands in the
matching module before it expands through the older flat page folders.
`ProviderDataProjectionViewModel` requires the signed-in tenant and company on every refresh and
uses the shared scoped provider projection. It has no unscoped fallback, so missing desktop session
scope fails before any provider rows are read.

## Key folders and files

- `Features/` - workspace-owned module registration for Trading, Portfolio, Accounting, Reporting,
  Strategy, Data, and Settings.
- `ViewModels/` - desktop operator workflow view models.
- `Views/` - WPF pages and controls.
- `Shell/` and `Services/` - navigation, route, launch, and desktop service seams.

## Important workflows

**Startup refusals are fatal.** `App.StartHostServicesAsync` deliberately tolerates a hosted service
that fails to start -- a database-backed projection or worker that cannot reach its store leaves the
desktop running with reduced processing rather than not running at all. That tolerance does not
extend to a governance guard. When `Meridian.Ui.Shared.Services.HostStartupEscalation.IsRefusal`
matches the fault -- any `Meridian.Application.Composition.StartupRefusedException`, including one
wrapped in an aggregate -- the shell reports it in a modal dialog carrying the guard's remediation
text and shuts down, because continuing is precisely what the guard forbade. A toast is not used: an
application that is closing does not show one. The guards that reach this path today are ADR-019's
`ProductionRegistrationGuardService` and W9-GOV-008's `InMemoryFundStructureTenancyGuard`; do not
reintroduce a blanket catch around host startup that swallows them.

Both are registered by `App.ConfigureServices`, and the ADR-019 one has to be: this desktop composes
its own graph and never calls `AddMarketDataServices`, the only other caller of
`AddProductionRegistrationGuard`, so leaving it out meant the lane whose tolerant catch this posture
exists to close had no final-graph guard at all. It is a no-op on an ordinary launch — without a
`MeridianDeploymentPostureDeclaration` or one of the posture environment variables, its `StartAsync`
takes neither the production nor the supported-local branch — so it costs nothing until a posture is
actually declared.

**Refusals are decided before the shell exists, and only refusals.**
`App.RunStartupRefusalPreflightAsync` runs every registered
`Meridian.Application.Composition.IStartupRefusalGuard` in `OnStartup` *before* `MainWindow` is
resolved or shown, and a refusal returns without constructing it. Keep that order. A guard that
*fails* rather than refuses counts as a refusal here: `StartupRefusalPreflight` converts it, because
"I cannot tell whether this composition is safe" is not "it is safe", and the ordinary
hosted-service tolerance behind the window would otherwise let the rejected posture serve.
`MainWindow.OnWindowLoaded` navigates to the fund-profile page, starts the shell view model, and
loads workspaces as soon as the window is shown, so showing first would leave the operator an
interactive shell backed by exactly the posture the guard rejects for as long as the guard and the
teardown behind it take. Checking the refusal flag afterwards only suppresses the later visibility
recovery; it cannot un-serve what was already on screen.

The preflight deliberately does **not** start the host. `IHost.StartAsync` returns only once every
hosted service has started, and this composition starts a symbol-registry initializer and a
canonical-registry migration that read the configured data root -- a slow or unreachable root would
then hold the shell back indefinitely, which is a worse outcome than the one the preflight exists to
prevent. Host startup therefore stays in `SafeOnStartupAsync`, behind the window, alongside theme,
tray, connection monitoring, and background services; that method keeps its own refusal catch as
defence in depth. The guards run in both places, which is why `IStartupRefusalGuard` requires
implementations to be safe to run twice: a guard must ask a question about the composition, never
act on it. It also requires them to answer without unbounded work, since they run with nothing on
screen -- which is why ADR-019's `ProductionRegistrationGuardService` is *not* marked, despite being
a refusal guard: in a production posture it resolves every factory-registered singleton, and that
belongs behind the window. Its *static* half is marked, as the separate
`StaticProductionRegistrationGuardService`: `ProductionServiceRegistrationPolicy` resolves nothing,
so the descriptor scan answers immediately, and postponing it too left a prohibited production graph
interactive until hosted-service startup shut it down. Register a new guard against the interface as well as `IHostedService`
-- mapping one singleton to both -- and the preflight picks it up without this shell being edited;
if the guard cannot answer cheaply, leave it an ordinary hosted service instead.

Application startup now shows `StartupWindow` before the main shell. After authentication, the main shell defaults to `HomeWorkspace`, a source-backed WPF launch checkpoint that groups provider health, data freshness, reconciliation, approvals, accounting/reporting readiness, and recent activity before operators enter deeper task workspaces. The startup view model validates
credentials through the Identity-owned `UserProfileRegistry` and `LoginSessionService`, keeps the
desktop session in `DesktopAuthenticationSession`, and opens `MainWindow` only after a configured
operator signs in or an unconfigured optional-development session is explicitly accepted. WPF reads
`MDC_USERS` password hashes first and then the legacy `MDC_USERNAME` / `MDC_PASSWORD_HASH` bootstrap
fallback through Identity; it does not persist login credentials in desktop config files. The main shell shows the active operator
session in its header and the `Log out` command signs out the in-memory session, hides the shell, and
returns to the startup login screen with a fresh startup view model.
Manual desktop secret entry uses `SecretInputControl`, which keeps values hidden by default, exposes
an explicit reveal toggle with non-secret automation names, and clears masked and revealed values
together when a flow resets the input.

Desktop configuration is preflighted before the generic host parses `appsettings.json`. Invalid
configuration is moved to a timestamped retained backup, a valid last-known-good copy is restored
when available (otherwise safe defaults are written), and a recovery receipt is retained beside the
configuration. The Data Sources page remains navigable, displays the recovery outcome and retained
artifact path, and exposes a retry command after the operator corrects file access or syntax; a
configuration failure no longer terminates the entire desktop process.

The Accounting workspace includes a dedicated `FundStructureSetupPage` and `FundStructureSetupViewModel` for operator entity setup. It uses the shared `FundStructureSetupWorkflowService` so desktop setup validation, graph preview, review-and-create, and account handoff behavior match `/api/fund-structure`.
`FundAccountingConfigure` now routes to `AccountingConfigurePage` and `AccountingConfigureViewModel`
instead of a generic ledger page. The page opens on compact action chrome instead of a duplicate
hero, preserving status, storage posture, active-fund context, and command readiness beside the
primary configure actions. The workbench reads and mutates shared accounting configuration DTOs,
surfaces selected ledger-book setup readiness, chart accounts, templates, posting rules, validation, and audit rows, saves manual journal
entry drafts through the shared workbench service with selected ledger-book scope on save,
validation, submit, and lifecycle actions, shows the shared Rules Studio projection for
effective-dated rule versions, generated posting readiness, saved regression tests, and promotion
approval queues, runs saved rule-test suites, and approves promotion-gated posting-rule versions
through the shared accounting configuration service, authors promotion-gated posting-rule drafts
with active ledger-book scope, template validation, priority, retained evidence, and audit rows
through that same shared configuration service, renders the shared accounting production-readiness
assessment across ledger books, Rules Studio, posting execution, dimensions, external GL,
close/reporting, and tenant-admin blockers, renders tenant-admin control/evidence progress from the
shared readiness DTO, persists tenant-admin setup controls and retained evidence through the shared
accounting tenant administration profile store with independent WPF accounting admin-studio, chart
administration, rule-test/promotion setup, close setup, provider mapping,
tenant/company/report-group setup, ledger-book administration, posting-rule authoring, approval
queue, dimension mapping, implementation sandbox, audit review, bulk import/export safeguard,
performance validation, and disaster-recovery runbook controls plus editable approval queue setup
for queue id, workflow kind, role/count, segregation policy, and evidence requirement plus editable
dimension mapping setup for mapping id, provider id, Meridian/provider dimension rows, and evidence
requirements. Desktop save readiness and command execution now block chart, posting-rule,
provider-mapping, production-certification, approval queue, and dimension mapping saves until
required operator evidence and typed payloads are complete, matching the shared stores'
fail-closed invariants, renders
the shared ledger-book-native workflow control count and retained ledger-book-scoped
workflow evidence for posting rules, JE lifecycle, close/reporting, external GL, reconciliation,
close-plan setup, direct-lending projections, and strategy ledger reads, renders
dimensional ledger/query/report/export control counts with retained ledger-book-scoped evidence, renders
and saves retained tenant/company/fund/book-scoped production-certification controls through the
shared Accounting System profile store, adding scoped retained evidence markers for checked
book-native workflow and dimensional controls before persistence,
authors retained external-GL provider mapping profiles through the shared Accounting System service
with provider/profile identifiers, account mappings, editable Meridian/provider dimension maps for
fund/book plus customer/vendor/project-style scope, human-origin certification evidence, retained
profile rows, and production-readiness refresh, authors retained chart account nodes through the
shared accounting configuration chart service with active ledger-book scope, parent path, financial
account id, and retained setup evidence,
retains selected-ledger-book implementation-sandbox, sandbox-validation, fixture-validation, and
implementation-fixture proof through the shared tenant administration profile store,
retained migration-run evidence plus generated migration rollout plan rows with ledger-book scope,
latest retained run, blocking issue codes, required actions, and canonical dimensions from the shared
production-readiness payload, renders retained migration worker plans from the shared accounting
system store with source/migrated row reconciliation, tenant/company/fund/book scope, evidence, and
canonical dimensions, renders the shared production-gap checklist for configurable
multi-ledger accounting, enterprise configuration studio coverage, guarded external GL integration,
dimensional ledger and reporting coverage, and production-control hardening with service-owned issue
messages beside stable blocker codes, surfaces
shared ledger-book setup candidate guidance, loads the active open ledger period through the shared ledger-book service for
manual journal draft validation, and can create the ledger book through the shared ledger-book service when book-scoped
configuration targets a missing registered book, renders a shared-workspace ledger-book
administration grid with selected/available books, fund-structure scope, basis, currency, policy,
description, and update timestamps, and adds selected-`ledgerBookId` tenant-admin evidence when
desktop operators save book-administration setup controls without already naming that book, and offers type-specific
draft presets for accrued
balances, accrued expenses, prepaid expenses, expenses, amortization, deferrals, reclassifications,
reversals, capital calls, distributions, subscriptions, redemptions, LP transfers, and management
fees, builds governed posting-rule journal draft candidate previews through the shared posting
candidate service without posting to the ledger, exposes approve, post, reverse, rebook, and
close-lock lifecycle buttons over the shared journal-entry lifecycle service, shows read-only
external GL evidence and retained package readiness from the shared QuickBooks, Xero, and NetSuite
fixture/import-first provider registrations, projects
close/evidence/reconciliation posture from shared operations continuity when available, and creates
fund-scoped accounting-basis policy records and multi-basis ledger-book projection candidates
through Financial Operations services.
`FundAccountingClose` routes to `AccountingClosePage`, a dedicated WPF close workbench over the
HTTP-backed `IWorkstationAccountingCloseApiClient`, which implements the shared
`IAccountingCloseManagementService` contract. Operators can load a close-period plan by workflow id,
edit desktop draft fields for materiality thresholds, currency, review role, late-adjustment
approval posture, select the retained checklist task being edited, and update task owner, due date,
required approval role/count, required evidence, role-scoped sign-off matrix rows, dependencies, and dependency reasons, then retain task/dependency, sign-off, required-evidence, and
late-adjustment evidence metadata through the same governed close-plan configuration request used by
browser close setup, including the loaded configuration timestamp so stale desktop setup edits fail closed
when a newer retained setup version exists. Dependency reason text can use keyed predecessor entries such as
`task-pricing: Pricing package must clear`, so desktop setup can preserve a distinct audit rationale for each retained dependency edge.
Sign-off matrix text uses retained `role | count | evidence` rows and carries unedited task matrices forward in the shared configuration request.
The workbench exposes governed close-task sign-off, selectable late-adjustment review, selectable blocker/evidence review, and
period-lock commands over the same shared service, carrying human actor, correlation, workflow,
period, ledger-book, task, close-package, report-pack, and retained evidence context while reloading
the returned plan instead of applying desktop-local approval rules. Desktop sign-off administration
now lets operators select the close task, sign-off role, Approved/Rejected decision, and retained
notes before calling the shared sign-off service, so WPF can administer matrix rejections as well as
approvals. Desktop late-adjustment review now lets operators select the retained request id,
Approved/Rejected decision, and review notes before retaining approval or rejection evidence.
Desktop blocker/evidence review now lets operators select the active issue code, target id, and
notes before calling the shared close-management evidence-review command with workflow, period,
issue, target, ledger-book, correlation id, and human-origin retained close-review evidence;
returned review rows are displayed beside the blocker without clearing the service-owned validation issue.
The workbench also renders an ordered close workflow control sequence over setup retention,
checklist sign-off, late-adjustment request, late-adjustment review, blocker review, and period
lock, with each step's command, status, evidence, and disabled reason derived from the same loaded
shared close plan and governed desktop command state.
The page binds close-plan
materiality, period-lock, task, dependency, sign-off matrix, late-adjustment, retained-evidence, and
validation-blocker rows directly from the shared close-plan DTO so desktop review uses the same
server-owned close-management state as browser. Desktop setup retention now also fails closed before
calling the shared service when materiality thresholds are negative, materiality currency is
malformed, review role is blank, the selected close task is missing or unknown, due-date text is not
`yyyy-MM-dd`, approval count is non-positive, approval role is blank, or required sign-off evidence
is blank. Desktop operators can now request retained late adjustments from the same close workbench
with journal-entry id, amount, currency, reason, human-origin stamping, and workflow/period/ledger-book
evidence before the shared service applies materiality approval rules.
Accounting Configure implementation-sandbox proof now uses the same retained tenant-administration
setup payload as the main save path: approval-queue and dimension-mapping configurations are
validated and preserved with the sandbox evidence before readiness refresh.
Desktop close-support queue projections consume
`FinancialOperationsCommandCenterDto.CloseSupportDecision` directly, so period state,
lock/reopen posture, NAV/report dependencies, unresolved exceptions, approvals, and retained
evidence gaps stay aligned with the browser command center and shared endpoint decisions. The
desktop Financial Operations queue grid also renders the shared queue-row severity, SLA, blocker
type, and close/report impact labels instead of deriving those operator fields locally.
Private-capital presets attach the shared
treasury ledger context expected by the approval service, including effective date, idempotency,
fund-event, capital-account, investor, payment, and settlement references.
Registration stays feature-owned in `Features/Accounting/AccountingFeatureModule.cs`; the
desktop fallback stores configuration/audit state in `workstation/accounting/accounting-configuration.json`
manual journal drafts in `workstation/accounting/manual-journal-drafts.json`, and retained migration
run evidence in `workstation/accounting/migration-run-artifacts.json` plus retained migration worker
plans in `workstation/accounting/migration-run-worker-plans.json` under the configured workstation
data root.

`FinancialRecordExplorerPage` is the generic WPF consumer for the shared Financial Record Explorer
DTO. `LedgerExplorer`, `PortfolioExplorer`, `SecurityInstrumentExplorer`, and
`ReportLineProvenanceExplorer` page tags resolve through the shell registry without adding new root
workspaces; the page maps shared columns and rows into `WorkstationTableInspectorControl` and
projects selected-record proof actions, `Used In`, and `Impacts` relationships into the inspector.
Empty or blocked source DTOs remain visible as disabled action states with server-provided reasons
rather than desktop-local placeholder balances.
Saved-view cards on the WPF explorer apply the shared Financial Record Explorer `viewId` query and
reload the source DTO from the workstation endpoint, so desktop ledger/report-line review uses the
same server-scoped rows, selected record, summary counts, and proof graph as the browser workstation.
Proof actions that carry shared Financial Record Explorer API hrefs map back to `LedgerExplorer`,
`PortfolioExplorer`, `SecurityInstrumentExplorer`, or `ReportLineProvenanceExplorer` page tags so
report-line drill-throughs stay route-compatible with the browser workstation. Report-line
provenance rows also carry shared instrument, position or transaction, reconciliation, journal,
report-line, evidence, and audit-link actions that WPF maps through the same view-model route
resolver instead of desktop-local lineage rules.
The generic selected-record field and relationship surfaces also carry factor evidence, holder
role/book position, economic projection, posting command, approval, immutable journal, and
ledger/report evidence identities resolved by UI Shared. WPF registers the shared factor projector
for independent desktop composition but does not calculate factor economics or query the journal in
the view model.

The desktop shell includes a first-launch and Settings entry point for a sample-data Demo / Sample Tour. Starting the tour enables `FixtureModeDetector` demo mode, selects the connected sample scenario, and walks operators through Data/provider status, Portfolio records, Accounting reconciliation, retained evidence/audit context, Reporting readiness, and Settings. The global demo banner and the tour banner label the workflow as demo/sample data only so sample records remain visually distinct from provider-backed operational data.

Keep desktop support aligned with shared contracts and governance posture.
Remote workstation calls should migrate through `IRemoteWorkstationClient`, which centralizes the
configured service URL, host health checks, and typed API calls for deployable WPF clients instead
of letting pages or services create their own HTTP clients or bind directly to the shared API
singleton. Watchlist backend synchronization now uses that seam for the optional `/api/watchlists`
probe while retaining local desktop persistence when the remote host does not provide a watchlist
payload. Activity Log also loads `/api/logs` through that seam and keeps the local offline
indicator path when the remote host is unavailable or returns a non-success response. Service
Manager health checks also use the same seam for deployable desktop clients. Lifecycle status,
readiness checks, latest receipts, restart, and shutdown use the typed `ILifecycleControlClient`;
the WPF process neither stores a raw shutdown token nor infers backend process ownership.
Setup Wizard backend readiness checks also use the remote seam, so first-run workstation setup
validates the configured remote host instead of issuing a page-local direct HTTP health probe.
The Symbols page Security Master bridge also resolves selected tickers through the same remote
client and shared workstation Security Master route instead of issuing page-local HTTP calls.
Ticker Strip quote polling also uses the remote client for `/api/live/{symbol}/quote`, preserving
the existing no-op offline behavior on non-success responses while keeping the service URL and HTTP
client lifecycle centralized for deployable desktop workstations.
Before login is enabled, the startup window queries the host lifecycle projection and requires a
Ready or Degraded snapshot that is accepting work. Closing WPF ends only the desktop client; it does
not implicitly stop the persistent installed host or its dedicated database. The compatibility
`BackendServiceManager` delegates start/stop/status operations to
`Meridian.LifecycleSupervisor.exe` and refuses direct process termination.
After local credential validation, WPF establishes a cookie-and-CSRF session with the host using the
same stored account; the desktop account store resolves the installed `MDC_DATA_ROOT` so the WPF and
browser workstations authenticate against one operator identity source.
Convention-based view-model wiring is handled by `Services/ViewModelViewResolver.cs`; shell pages
that follow the `*Page` to `*ViewModel` naming convention can receive a DI-constructed DataContext
without page-specific registration, while pages that set their own DataContext remain authoritative.
Runtime desktop capability toggles are declared by feature modules and surfaced in Settings through
the feature capability gate. The Security Master page projects the workstation trust
snapshot's `scheduleBook` and `openLotReadModel` payloads into operator-visible schedule, factor,
provenance, and open-lot review sections.

The same page now loads the shared Instrument Passport endpoint for the selected security so desktop operators see provider-confidence, pricing, trust, downstream usage, operations-readiness, and handoff evidence in parity with the browser Accounting workstream.
The Direct Lending page consumes the shared `DirectLendingOperationsReadModelDto` for servicer
statement batches as well as collateral, status, exceptions, evidence, and close blockers. The WPF
panel is read-only over imported position/remittance batches; preview, validation, evidence
retention, and apply decisions stay behind shared Direct Lending endpoints and services.
The Settings page also surfaces governed Security Master asset profiles for WPF operators. It lists
approved profile definitions from the shared `/api/security-master/asset-profiles` route, drafts and
approves profile variants through the shared governance endpoints, loads lineage, supports rollback,
and creates profile-backed `CustomAsset` records with `customProfileId`, `profileVersion`, typed
`profileFields`, and approval metadata. WPF stays thin here: profile governance, mutation
permissions, and custom-asset validation remain owned by the shared Security Master API.
Settings also includes a read-only Operations Control tab backed by the shared Operations Continuity
approval-policy matrix and close-calendar routes. The tab gives desktop operators visibility into
approval thresholds, evidence requirements, independent-review controls, close due dates, blockers,
checklist posture, and approval counts while leaving policy and calendar mutations owned by the
shared Operations Continuity endpoints and browser control-center workflow.
Run Cash Flow consumes `StrategyRunContinuityService` when the desktop shell provides it, so the
cash-flow drill-in presents the same run, portfolio, ledger, cash-flow, reconciliation, and warning
posture used by shared workstation continuity endpoints.
The drill-in uses compact action-strip chrome, shared dense cash-ladder and cash-flow event tables,
and right-side inspectors for the selected event, ladder bucket, continuity posture, and run actions;
Security Master remains disabled until a symbol-linked cash-flow event is selected.
Desktop backtest services register the Backtesting-owned `IBacktestPreflightService` implementation
and attach it to the singleton `BacktestService`, so WPF strategy runs use the same date-range,
replay-coverage, execution-model, and optional Security Master preflight checks as shared
Backtesting flows.
Fund Ledger trial-balance and journal grids project the canonical ledger dimension envelope from
shared DTOs, including fund, entity, sleeve, strategy, investor, capital-account, instrument,
tax-lot, cost-center, counterparty, organization, portfolio, book, account, customer, vendor, and
project scope, while detail inspectors continue to show external-GL dimensions for selected rows.
Fund Ledger reconciliation actions call the shared workstation reconciliation endpoints and inspect
the returned verified outcome before displaying
success. Assign, resolve, waive, and supersede commands therefore surface blocked prerequisites,
failed persistence, retained evidence, and recovery guidance instead of inferring completion from an
HTTP response or compatibility message. `CompletedWithWarnings` retains the successful mutation,
refreshes the shared queue, and keeps its issues and recovery guidance visible; only `Blocked` or
`Failed` suppresses the success path. Reconciliation reads distinguish a confirmed missing run
record from a failed detail request: partial reads render a degraded notice, a complete detail-read
outage renders an unavailable notice, and only a successful read with no known runs uses the
verified empty state. Break-queue and calibration failures are also retained as unavailable instead
of producing zero-count metrics or a synthesized `Ready` posture; overview and security-coverage
counts are suppressed or marked as lower bounds whenever their detail population is incomplete.
Strategy workspace composition resolves the durable
strategy-run store and operational case-history store; lifecycle state, attempts, input hashes,
artifacts, exceptions, and recovery events survive desktop restart rather than falling back to an
in-memory production history.

The desktop pending-operations store persists a versioned queue envelope. Unknown operation types
remain durable for a later handler, while the retired authentication-sensitive
`reconciliation.review-break` and `reconciliation.resolve-break` replay types move once into
payload-free quarantine so operator notes and evidence are not retained in an unsafe replay record.

After mutation, the desktop refreshes the queue from the shared break read model after
review/resolve/dismiss and keeps the selected decision
note, audit event, pending close sign-off posture, and contract-owned "Explain the Break" summary
visible in the retained detail panel. The WPF queue projection carries the same source systems,
probable cause, ledger impact, suggested next action, and evidence links as the browser Accounting
detail so desktop operators do not rebuild reconciliation narratives locally. Statement-originated
break rows also render the retained case SLA label and escalation posture from the shared
`StatementBreakDto`, keeping desktop exception triage aligned with source-backed case ownership.
The retained reconciliation detail also exposes a Match Items tab with side-by-side selectable
ledger-entry and source-data grids; the desktop view model keeps those checkbox selections as
presentation state and submits matched items through the same shared resolve endpoint with an
audit-note summary.
Shared close-workflow target tags stay explicit in desktop routing: `OperationsContinuity` and
`OperationsClose` are WPF aliases for the Fund Operations page, with navigation parameters that
land on the overview and report-pack readiness tabs while the browser resolves both tags to
`/accounting/operations-continuity`.
The browser `AccountingApprovals` approval route also resolves in WPF to the Fund Audit Trail
surface, so the design-document approval step has a route-compatible desktop target for approval
history, retained evidence, and accounting audit references.
Shared evidence workflow target routing is also explicit: `EvidenceWorkbench` resolves to the WPF
Evidence packets page (`EvidenceWorkbenchPage`, Reporting workspace) while the browser resolves the
same shared tag to `/reporting/evidence`. Parameterized desktop targets such as
`EvidenceWorkbench:accounting-record/{recordId}` preserve the canonical evidence subject: direct WPF
navigation and embedded page-content creation canonicalize those parameterized targets before
resolving page content and pass the `{subjectKind}/{subjectId}` subject string through the page's
navigation parameter, so the Evidence packets view model focuses the same shared subject carried by
browser routes, workflow rows, and saved presets.
The route-registry parity test covers all built-in workflow entry and action target tags so shared
workflow catalog updates cannot silently become browser-only or desktop-only.
The WPF workflow library also projects the shared v0.15 `Accounting Records Evidence Review`
workflow, preserving the source-record, normalized-activity, reconciliation-case, ledger-evidence,
approval-history, report-lineage, export-evidence, and restatement-lineage action sequence from the
shared catalog.
The Accounting Configure manual-journal tab consumes the shared private-capital activity projection
from `ManualJournalEntryWorkbenchDto`, exposing capital-account activity, fund-event, ledger-impact,
and report-output readiness grids plus subledger movement, posted fund-event, and published
report-output counts from server-owned treasury context instead of deriving private-capital ledger
rows, GL-impact rows, or stakeholder-package state in desktop code. Posted ledger-backed fund events
and published governed report outputs use the explicit shared source-state flags for desktop row
labels instead of inferring those states from approval or readiness text, and report-output rows
show shared report-pack workflow state, publication manifest identity, and provenance counts when
the shared projection provides them. The shared projection also carries event-level ledger records
that group each fund event with its subledger rows, GL impacts, evidence, approval state, and
report-output posture. The desktop grid reads promoted memo, gross/net activity, and child-count
fields plus capital-account opening/ending net activity, the canonical activity route,
evidence-packet route, approval id/route, the direct private-capital report-output route,
primary report-output workflow/provenance, readiness reason, and next action from that record
without introducing WPF-local accounting aggregation.
Manual journal lifecycle buttons build shared lifecycle requests with a distinct desktop controller
actor for approval, posting, close-lock, reversal, and rebook decisions, leaving the shared service
to enforce preparer-independence, evidence, period-lock, idempotency, and correction-draft controls
instead of letting the desktop view model promote entries with the draft preparer identity.
Report-output rows also consume the shared report-output readiness label, reason, next action, and
next-action route, so the desktop grid explains missing evidence, approval, posting, publication,
and published states without parsing validation issue codes.
Accounting Configure also renders account-level capital-account subledger rows from
`PrivateCapitalCapitalAccountSubledgerDto`, including the shared subledger route,
opening/ending roll-forward, net activity, contribution/distribution totals, approval queue,
posted/published counts, validation issues, contract-owned readiness label/reason, next action,
and evidence-category readiness.
The Capital Account Workbench tab consumes `ICapitalAccountWorkbenchService` and keeps investor
capital-account evidence, allocation-rule readiness, statement/restatement lineage, audit-support
drill-throughs, and live-versus-planned capability rows in the shared DTO shape used by the browser
workbench.
Manual Journal payment-intent rows now surface the shared payee, account scope, business purpose,
approval policy, and retained source-evidence count beside approval-chain, bank/cash evidence,
retaining-operator attribution, reconciliation, audit-history, and execution-deferred posture,
keeping the desktop cash-evidence view source-backed instead of deriving request metadata in WPF.
The Accounting feature module also registers the Financial Operations-owned
`IPrivateCapitalCloseCockpitService`, so desktop composition can resolve the same private-capital
close proof projection for partner capital tie-outs, NAV support packages, approval history,
close-package evidence, and period-lock readiness without introducing WPF-local rules.
Fund Ledger now consumes that shared close cockpit in its Report Pack side rail, projecting close
lanes, evidence package rows, NAV support packages, approval history, blocker text, retained
evidence counts, and readiness sign-off posture into read-only WPF rows that stay source-backed by
the Financial Operations service.
The Accounting shell's Financial Operations checkpoint now also renders the shared workflow
evidence badges and primary blocker detail from `WorkstationWorkflowSummaryService`, giving desktop
operators a source-backed compact view of core-flow stage, break pressure, approval posture,
retained evidence, and close-package state before they open the deeper Operations Continuity or
Fund Audit Trail surfaces.
That desktop surface remains a reviewer for the unified fund-event ledger and capital-account
subledger model: evidence-category readiness covers source support, capital-account subledger,
ledger impact, approval state, and report output, while readiness reason/action copy comes from the
shared DTOs. Do not add WPF-only cap-table administration, broad LP portal, native live-payment
execution, full forecasting, or Backtesting Studio behavior to this slice.
The desktop diagnostics surface reads colocation profile state through
`Meridian.Platform.Performance.ICoLocationProfileActivator`, keeping runtime-performance ownership
in Platform while WPF remains a presentation surface.
Cluster Status now uses `WorkstationTableInspectorControl` for coordination ownership review,
keeping the cluster summary above the selected-node inspector and the action inspector below it.
Mesh-disabled or missing lease-manager sessions fail closed with a blocked refresh posture, while
enabled sessions expose read-only node rows, selected-node lease freshness, and manual refresh
readiness without mutating coordination leases from WPF.
When shared workflow actions carry parameterized evidence targets such as
`EvidenceWorkbench:accounting-record/{recordId}`, the workflow library keeps the raw target tag for
navigation and filtering but presents the operator action target as `EvidenceWorkbench` so route
syntax does not leak into desktop workflow copy.
Shared workstation affordance primitives under `Workstation/Models` and `Workstation/Controls`
standardize action posture, readiness tone, evidence links, recovery actions, and sign-off
requirements for W4 close/report surfaces. Fund Ledger reconciliation and Report Pack handoff
surfaces consume these primitives so blocker, evidence, recovery, and sign-off signifiers stay
visible without creating desktop-only business rules.
Portfolio cockpit decision items include the shared multi-asset coverage route
`/api/workstation/portfolio/multi-asset-coverage` so desktop operators can review asset-class
readiness, provider evidence, ledger coverage, reconciliation posture, close blockers, and
contract-owned drill-through targets through the same read model used by the browser Portfolio and
Accounting screens.
The same cockpit now advertises the shared Asset Operations detail route
`/api/workstation/assets/{securityId:guid}/operations`, pointing operators to the contract-owned
`AssetOperationsDetailDto` subject, projected-cash-flow, reconciliation-result, and ledger-projection
sections for any Security Master asset instead of rebuilding direct-lending-only drill-ins in WPF.
The Reporting cockpit keeps the desktop entry point aligned with the shared reporting engine by
refreshing its decision queue and summary tiles from `FundOperationsWorkspaceReadService` reporting
telemetry. It surfaces report writer datasets and retained grids, branded report packs, scheduled
PDF/XLSX/CSV delivery, secure-portal and email-link distribution, Top-N/contribution analytics,
custom-formula grid validation, cross-fund consolidation roll-ups with shadow-NAV, regulatory and
warehouse exports, user/group/company access posture, and audit lineage through registered WPF
targets (`FundReportPack`, `ReportRunStatus`, `EvidenceWorkbench`, `Dashboard`, `AnalysisExport`,
`ExportPresets`, `ReportLineProvenanceExplorer`, `FundAuditTrail`, and `DataQuality`) rather than
desktop-local reporting logic. The Reporting shell default pane set and command surface now include
`ReportLineProvenanceExplorer`, and the Evidence packets page (`EvidenceWorkbench`) provides the
canonical desktop parity surface for the browser `/reporting/evidence` evidence workbench alongside
report-line evidence and provenance review. Its home chrome stays compact: the Daily Reporting Cockpit strip
puts the shared summary text, writer, approval, and delivery posture beside direct report-pack, run
status, evidence, and export routes before the decision queue instead of rendering a separate
page-level hero.
The same Reporting shell now hosts a thin canonical governance workbench over shared reporting
contracts and API routes. Desktop operators can round-trip exact template/version, fund/entity,
book, period, as-of, accounting-basis, currency, consolidation, output, finality, schedule,
evidence, dimension, and template-parameter inputs; inspect server-owned readiness blockers; and
advance retained runs through `Draft -> Validated -> InReview -> Approved -> Released`. The WPF
view model enables lifecycle commands only from caller-specific server `ActionAvailability` entries
and submits their server-owned expected versions. Secure delivery similarly uses the server transport
catalog and its explicit queue, grant-issuance, grant-revocation, and per-transport readiness decisions;
the desktop keeps no transport allow-list and fails closed when either projection is unavailable. The
one-time recipient link is accepted only when its bearer is fragment-scoped, is kept in memory only
until the next distribution or run action, and never appears in retained delivery or grant-history
rows. The
server continues to own tenant scope, maker-checker authorization, certified snapshot and access-policy
hashes, immutable artifact references, restatement-as-new-revision behavior, and release-gated secure
distribution receipts.
Fund Ledger Report Pack handoff also renders the shared Operations Continuity accounting-record
summary, including retained source records, normalized activity, reconciliation history, ledger
evidence, approvals, report-pack lineage, export evidence, restatement lineage, measured
audit-pack timing, and 60-second target status. The WPF view model maps contract-owned category
status, required evidence labels, evidence links, route hints, readiness warnings, and timing into
desktop rows and readiness state instead of deriving audit readiness in XAML or desktop-only
services. It also renders the shared Financial Operations reconciliation lane coverage for cash,
position, trade, income, MBS factor, bank, and GL support, preserving lane status, break counts,
required actions, route targets, and retained evidence subjects from the active Operations
Continuity workflow. The same side rail now renders a source-backed Financial Operations operator
queue from shared Operations Continuity break cases, close-checklist tasks, approvals, evidence
packages, reconciliation lanes, and private-capital close blockers, keeping exception management,
close support, and approval history visible without WPF-local workflow rules. It also surfaces the
shared reviewed-automation posture, allowed/prohibited automation guardrails, retained evidence, and
human-review requirement as a read-only WPF signifier before the queue. Each accounting-record
evidence row now carries both the desktop shell target and canonical `accounting-record/{recordId}`
subject target so WPF operators can reconcile the row with the same Evidence Workbench subject used
by the browser and shared evidence endpoints.
Desktop evidence read-model parity includes the shared Evidence Vault document queue contracts:
retained document entries carry classification, source hash, source channel, actor, tenant/scope,
extraction status, reviewer state, linked close/report/accounting objects, open support-request
count, retained manifest route, and the shared uploaded/local/imported intake-source descriptor
without giving document intake authority to post or approve accounting records. WPF serialization
also preserves `EvidenceVaultIdentityDto.ManifestSnapshot` so desktop close, report, tax, and audit
package views can consume the same frozen document/request/object-link snapshot as the browser.
`MainPage` remains the route-compatible desktop shell entry point, but shell chrome is now composed
from reusable WPF primitives: `InstitutionalShellFrameControl`, `ShellRailControl`,
`ShellMastheadControl`, `WorkspaceEvidenceStripControl`, `WorkspaceCommandSurfaceControl`,
`InstitutionalCommandPaletteControl`, and `WorkspaceInspectorHostControl`. Workspace shell posture
is WPF-only and resolved through `ShellNavigationCatalog.GetWorkspaceLayoutDescriptor`: Trading and
Data use `Terminal`, Portfolio, Accounting, Reporting, and Settings use `Cockpit`, and Strategy uses
`Workbench`. Legacy workspace names continue to resolve as aliases to the seven canonical roots.
Desktop launch surfaces, setup completion, environment starter lanes, and operating-context defaults
emit canonical page tags (`StrategyShell`, `DataShell`, and `AccountingShell`) so new persisted state
does not reintroduce legacy `Research`, `Data Operations`, or `Governance` root names.
Catalog-backed operator pages, including Accounting entity setup, are registered exactly once
through `AddMeridianWpfShell`; the App bootstrap keeps only non-catalog supplemental pages in its
extra page-registration block.
Shell coordinator, pane-host, and direct `NavigationService` paths accept compatibility aliases as
inbound route requests but store and execute canonical page tags in active pane state, restored
content creation, navigation history, navigation events, and pane-drop results.
Workspace id compatibility normalization and the canonical legacy-alias list are centralized through
`WorkstationNavigationDefaults` instead of repeated shell-local switch expressions.
The shared context-strip service normalizes legacy workspace titles before rendering owner copy, so
old callers cannot reintroduce `Research`, `Data Operations`, or `Governance` as visible workspace
labels.
The workspace layout page also renders built-in active workspace chips from canonical root names,
not from retained legacy category enum names, so Portfolio, Reporting, and Settings do not appear as
Accounting categories.
Operator-inbox attention badges use the same canonical workspace-title normalization before
rendering owner labels, while retained API payloads may still carry legacy workspace names for
compatibility.
The two-pane workstation layout keeps the retained `ResearchData` compatibility identifier but
renders the operator-facing label as `Strategy + Data`.
`WorkspaceLayoutManager` is the shared bridge from `WorkspaceShellDefinition` and preset pane
definitions into both the split-pane model and the AvalonDock host. `FloatWindow` opens detachable
pane windows inside the same single desktop shell, records floating-pane metadata in
`WorkstationLayoutState`, and docks the pane back when reopened with a dock action instead of
creating separate top-level workspace applications.
WPF icon resources and SVG source filenames keep retained identifiers such as `IconResearch`,
`IconDataOps`, `IconGovernance`, `research.svg`, `data-operations.svg`, and `governance.svg`, but
the icon asset documentation maps those compatibility names to canonical Strategy, Data,
Accounting, and Settings operator labels.
`WorkspaceService` uses that same seam for persisted session and layout restore while keeping only
older template aliases such as monitoring, storage-admin, and analysis-export local to the service.
Strategy shell presentation defaults also emit the canonical `strategy` workspace id while retaining
legacy research aliases for restored workflow summaries.
Workspace shell copy and automation names use canonical Strategy/Data shell constants instead of
legacy Research/Data Operations constant names.
Accounting shell workflow summary selection prefers the canonical `accounting` workspace id while
retaining `governance` as an inbound compatibility alias. Desktop workflow-summary calls forward the
active account-scoped operating context as `fundAccountId`, so the shared Financial Operations
summary can load source-backed reconciliation, approval, close, and evidence posture even when the
fund profile id is a human-readable desktop profile key.
Accounting shell visible copy, accessibility names, queue summaries, and presentation-service
handoff text use canonical `Accounting` wording. The shared presentation service now uses an
`Accounting*` name, and Accounting shell page types, state providers, view models, page bases, and
automation IDs use canonical `AccountingWorkspace*` names. Remaining `GovernanceShell` and
`GovernanceWorkspace` names are route aliases only.
The Accounting shell also projects the design-document Financial Operations workflow
(`Receive Activity`, `Match Records`, `Resolve Exceptions`, `Approve Results`, `Produce Evidence`)
from `AccountingWorkspacePresentationService` into a compact current-checkpoint row instead of a
multi-step strip, so operator workflow state is derived from shared fund, reconciliation, approval,
and audit posture without crowding the queue workbench.
Fund Ledger and Fund Accounts drill-in surfaces use Accounting wording for route banners,
report-pack preview, account queues, and reconciliation guidance while preserving compatibility
type names where needed. Fund Ledger now reduces its duplicate hero to a compact action strip and
uses a shared dense account table plus selected-account inspector for the Accounts tab, with account
portfolio drill-through blocked until a row is selected. Its local fund-ledger read service now
populates the shared `LedgerDimensionSetDto` on fund trial-balance, journal, and reconciliation
snapshot rows, and the Fund Ledger dense trial-balance and journal tables plus selected-row
inspectors surface fund, entity, sleeve, account-scope, portfolio, external-GL, and canonical
dimension facts so desktop Accounting review matches the shared reporting/export contract. Fund
Accounts now opens on a compact action strip and combined dense account table plus
selected-account inspector, with provider binding and route-preview evidence kept as supporting
workbench panels below the primary account queue.
Fund Ledger report-pack handoff preview copy also names the Accounting workspace so downstream
board, investor, compliance, and fund-ops packets do not surface the legacy Governance root label.
`WorkspaceCommandSurfaceControl` and `WorkspaceEvidenceStripControl` take explicit automation ID
properties from the active workspace layout descriptor so shell chrome can be reused without
depending on ambient `MainPage` bindings.
Modal surfaces should migrate through `WorkspaceDialogChromeControl`; provider API-key setup,
watchlist saving, and scheduled-job editing now use that shared dialog chrome with stable title,
subtitle, body, input, and action automation IDs. The control projects its chrome automation ID and
title onto the reusable control itself so UI automation can address both the control and template
parts consistently.
Standalone command-palette chrome should use the same shell tokens and stable automation IDs instead
of page-local colors or shadow effects.
High-value workbench pages should migrate through the shared workstation controls before broad
page sweeps; Strategy Runs now opens on compact action chrome instead of a duplicate hero, uses
`DenseDataGridControl` plus tabbed inspector panes for run, evidence, comparison, and artifact
context, and surfaces selected-run or comparison guidance as disabled-action tooltips while
preserving existing page tags and navigation commands. Account Portfolio and Aggregate Portfolio
now use `WorkstationTableInspectorControl` with custom empty-state content for recovery guidance;
Aggregate Portfolio also uses the table-header slot for its concentration strip. Both keep compact
action strips, shared dense position tables, selected-position inspectors, and disabled-action
tooltips for refresh/Security Master readiness while preserving their existing account and
aggregate API read paths. Run Portfolio follows the same drill-in
pattern: the duplicate hero is replaced by compact run actions, retained positions render through
`WorkstationTableInspectorControl`, the selected position drives an inspector rail, and Security
Master/run drill-in commands expose their disabled reasons through view-model-owned tooltips. Run
Detail now uses the same focused drill-in contract: compact run actions stay fail-closed until
retained detail loads, run evidence is projected through the shared inspector, and retained
parameters render through the shared dense table with selected-parameter inspection. Run Ledger now
uses the same compact drill-in treatment for Accounting review: retained trial-balance and journal
rows render through dense shared tables, the selected trial-balance line owns the inspector rail, and
Security Master actions fail closed unless the selected line carries a security or symbol. The Run
Ledger dense tables and selected-line inspector now surface the shared `LedgerDimensionSetDto`
scope, including fund, entity, sleeve, strategy, investor, capital account, instrument, tax lot,
cost center, counterparty, organization, portfolio, book, account, customer, vendor, project, and
external GL dimension values, with legacy scope labels used only when canonical dimensions are
absent. Run Risk
now follows that focused attribution pattern: the oversized hero is replaced by compact run actions,
retained symbol attribution uses the shared dense table, and the selected symbol owns the inspector
rail plus Security Master lookup readiness.
The desktop shell visual system now targets a light institutional workstation frame with a
near-black global app bar, paper page bands, compact filter bars, and dense table chrome. New
workspace overhauls should prefer `WorkstationPageBandStyle`, `WorkstationFilterBarStyle`,
`WorkstationFilterChipStyle`, `WorkstationTablePanelStyle`, `WorkstationInspectorRailStyle`,
`WorkstationDockStripStyle`, `DenseDataGridControl`, `WorkstationTableInspectorControl`, and
inspector host primitives before adding page-local cards or dark terminal styling. The Data shell is
the reference implementation for this professional table-plus-inspector composition, including
bounded dock height for stable repeated operator use.
Settings/Admin cockpit work uses `WorkstationStatePanelControl` for schedule and cleanup readiness
state so maintenance blockers, confirmation posture, and evidence summaries reuse the same
`WorkspaceTone` semantics as other operational pages.
Data Quality terminal work uses `DenseDataGridControl` for the symbol-quality table with
view-model-owned selected-row drilldown and provider-comparison command state, keeping the Data
workspace on shared dense-table behavior without changing data-quality service contracts.
Data terminal provider, backfill, and storage decision queues now use
`WorkspaceDecisionQueueControl` while retaining existing queue-region empty/loading/error state
templates and view-model-owned action resolution.
Data shell state providers and XAML page bases use canonical `DataWorkspace*` names; retained
`DataOperationsShell` and `DataOperationsWorkspace` tags are inbound route aliases only.
`DataWorkspacePresentationBuilder` projects the design-document Data Integration workflow
(`Connect Source`, `Acquire Data`, `Validate Data`, `Normalize Data`, `Store Data`, `Publish Data`)
into a compact current-checkpoint status in the filter bar and operations summary instead of a
dedicated workflow card, so desktop operators keep provider-to-publish progression visible without
crowding the queue workbench. Retained `DataOperations` route and smoke-test names are
compatibility shims only, not the canonical presentation-model taxonomy.
`MainPageViewModel` retains the design-document primary operator workflow route map
(`Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, `Report`), while the shared
`WorkspaceEvidenceStripControl` keeps compact next-action and evidence summaries on workspace home
pages only. Data terminal and deep workflow pages collapse that shared summary chrome and the
split-pane layout bar so the page-owned workbench remains the primary surface.
Provider Health now follows the same focused workstation composition: the page-level hero is reduced
to compact freshness controls, provider management centers the shared dense table plus inspector,
and provider-specific diagnostics are disabled until a row is selected. Affected-workflow labels use
canonical workspace names such as `Strategy` and `Data`; retained provider DTO and page-tag
compatibility names must not leak `Research` or `Data Operations` into operator-facing recovery
tables.
The legacy Provider page is limited to backfill-specific tuning: provider enablement, priority,
rate limits, fallback preview, dry-run planning, and audit history. It now uses compact action
chrome plus shared dense table/inspector workbench tabs for provider settings, fallback chain,
dry-run plan, and audit trail. Save/reset mutations are selected-provider commands with disabled
tooltips; provider status, credentials, routing, diagnostics, and recovery actions belong in
Provider Health and Settings via shared provider metadata instead of hand-authored desktop provider
cards.
System Health now uses the same compact support-console treatment: the duplicate hero is replaced by
a health action strip, provider and recent-event lists render through shared dense tables, the triage
briefing is projected through the shared inspector model, and refresh/diagnostic commands expose
their disabled reasons through view-model-owned tooltips.
Service Manager uses the same compact workstation treatment: the duplicate page-level hero is
removed, the first visible band is the service posture strip, and lifecycle controls remain paired
with runtime/action inspectors for service-dependent workflow recovery.
Data Export now opens on export readiness instead of a duplicate hero. Quick export and scheduled
export actions surface their existing view-model readiness details as disabled-action tooltips, so
operators see the missing symbol, date, time, or destination requirement at the command surface.
Data Sources now opens on configuration controls instead of a duplicate hero. The source edit
readiness card remains view-model-owned, and the disabled save action exposes the same readiness
detail as its tooltip so missing source names or invalid priorities are visible at the command.
Data Quality now uses a compact freshness strip plus the shared workspace command bar instead of a
duplicate hero. Refresh and quality-check actions stay in `WorkspaceCommandBarControl`, while the
symbol-quality table remains on the shared dense-grid surface with selection-owned drilldown state.
`WorkspaceCommandItem` carries `DisabledReason` separately from `Description`, so "what this command
does" and "why it cannot run right now" no longer share one string; commands that leave it blank
still fall back to `Description` when mapped to `WorkstationCommandModel`. Both command bars surface
that reason on disabled actions — `WorkspaceCommandBarControl` through a tooltip marked
`ShowOnDisabled`, and `WorkstationCommandBarControl` inline beneath the label for primary commands
and through a `ShowOnDisabled` tooltip for overflow commands. Both bars build their overflow
`ContextMenu` in code-behind, so each menu item carries the same tooltip and automation metadata as
the primary buttons. Strategy's `Promote to Paper` and `Open Trading Cockpit` publish state-specific
reasons when no eligible run is selected, rather than repeating their descriptions. Command buttons expose
`AutomationProperties.AutomationId` from the command's stable `Id`, falling back to a normalized
label via `WorkspaceCommandAutomation` only when a command ships without one, so UI automation stays
anchored to identity rather than to display copy.
Activity Log now uses compact action chrome instead of a duplicate hero. Export and clear remain
guarded by view-model state, and disabled-action tooltips explain when retained or visible log
entries are missing before support traces can be exported or cleared.
Order Book now keeps the depth ladder as the first-order screen element: symbol, depth, and
connection controls live in compact action chrome, and the empty ladder state reuses the
view-model-owned order-flow posture instead of static XAML instructions.
Watchlists now use the shared dense table plus inspector pattern instead of repeated cards. The
filtered library stays virtualized through `WorkstationTableInspectorControl` composing
`DenseDataGridControl`, selected-row actions are disabled until a watchlist is selected, and
disabled-action tooltips explain the missing selection at the command surface.
Direct Lending now follows the same focused portfolio workbench pattern: the header card is replaced
by a compact action strip, loan/accrual/cash evidence renders through shared dense tables, the
selected loan owns the inspector rail, and accrual posting fails closed until a retained loan row is
selected and detail loading is idle.
Analysis Export now opens on compact canonical-backend availability instead of an embedded header.
Recent export history renders through `DenseDataGridControl` but remains empty until backend-confirmed
history is connected. Run and preset-save commands fail closed because this desktop screen's
destination, metric, chart, summary, and preset options are not yet represented by the canonical
analysis-export service; disabled-action tooltips state that no export or preset was created.
RunMat Lab is a Strategy workspace tool; its visible page descriptions and code comments use
`Strategy` wording while retaining the existing `RunMat` page tag and automation IDs.
QuantScript run-history handoffs use `CompareInStrategyCommand` for Strategy Runs comparison
routing while preserving the existing button automation ID and run-detail navigation targets.
`StrategyWorkspaceShellPresentationService` owns Strategy shell briefing, workflow, degraded-state,
command, and promotion presentation state. Strategy shell page types, state providers, view models,
page bases, automation IDs, and hero/workflow bindings use canonical `StrategyWorkspace*` and
`Strategy*` names, while `ResearchShell` and `ResearchWorkspace` remain route aliases only.
Strategy shell fallback briefing summaries, deterministic workflow guidance, degraded-state recovery
copy, and promotion notes also use canonical `Strategy` wording while retaining legacy research
DTO/interface names for API compatibility. The desktop briefing client requests the canonical
`/api/workstation/strategy/briefing` route while the shared host still serves the retained research
briefing route as a compatibility alias.
Backfill terminal work opens on compact action chrome instead of a duplicate hero, and the disabled
start command surfaces the view-model readiness detail as its tooltip. Gap-analysis and
per-symbol-progress tables use `DenseDataGridControl`, with table descriptors owned by
`BackfillWorkbenchSectionViewModel` so long-running provider catch-up workflows reuse the shared
dense-table/empty-state surface.
Trading terminal work uses `DenseDataGridControl` for active positions and view-model-owned
selected-position inspector state so paper/live desk review keeps row selection, P&L, mode, and
next-action context in the shared dense table surface. `WorkspaceInspectorHostControl` owns
empty, selected, loading, and error inspector states with caller-supplied automation IDs so
workspace pages can migrate selected-row detail without changing route/page tags. The desktop
Trading hero now treats active live runs as ready only when the shared `ReadyForLiveOperation`
posture is true, so paper-ready evidence cannot present a live desk as operator-ready. When the
shared readiness payload includes `LiveOperationRequirements`, the desktop live-run hero uses the
first non-ready W7 requirement to name and route the missing evidence item, such as governance
sign-off, broker execution reconciliation, rollback/kill-switch, or audit-retention proof, instead
of falling back to a generic readiness review.
The desktop position blotter also stamps the active account-scoped operating context onto
close/upsize action requests when the context resolves to a fund-account GUID, so generated broker
orders evaluate the same account-scoped W7 readiness gate as browser trading actions.
Trading shell fallback and context-handoff copy routes reconciliation and kill-switch handoffs
through Accounting wording while preserving retained `GovernanceShell` target tags for route
compatibility.
Accounting, Portfolio, Reporting, and Settings/Admin cockpit home decisions bind to view-model-owned `WorkspaceQueueItem`
collections through `WorkspaceDecisionQueueControl`, preserving existing page tags while reusing
`WorkspaceTone` queue-card and badge semantics for summary, approval, exception, and delivery
decisions, including primary, secondary, and blocked cockpit actions.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-WPF -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `W8-WPF-PARITY-001` | WPF desktop workstation reactivation and web-UI parity |
| `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `W10-PROV-001` | Ledger-amount evidence subject and shared proof drawer |
| `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `W10-RECON-003` | Unified tolerance model and what-if replay workbench |
| `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `W10-PERF-001` | Portfolio and investor return measurement |
| `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-WPF -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```powershell
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~AppServiceRegistrationTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:NodeReuse=false
```

## Change rules

Keep WPF views declarative and move loading, disabled, preview, empty-state, and status-copy
behavior into view models. Do not duplicate product logic that belongs in shared UI services.
When telemetry, latency, order-flow, or preview data is unavailable, show an explicit unmeasured or
unavailable state rather than seeded sample numbers or plausible-looking derived metrics.
Use `Controls/EmptyStatePanel` for reusable missing-data states; it supports title, explanation, severity, and up to two actions for provider setup, import, selection, freshness, reconciliation, reporting, and fixture-data recovery paths.

## Related docs

- `docs/status/wpf-screen-development-tracker.md` - generated WPF screen Gantt chart and automated per-screen TODO checklist derived from the shell registry, desktop screenshot index, and WPF test references.
- `docs/screenshots/desktop/README.md` - maintained desktop screenshot evidence index consumed by the generated screen tracker.
- `src/Meridian.Ui.Shared/README.md`
- `docs/development/wpf-implementation-notes.md`
- `docs/reference/accounting-report-packs.md`
- `docs/operators/governed-reporting-operations.md`
- `docs/source/generated/source-module-index.md`
