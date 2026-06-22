---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-WPF
path: src/Meridian.Wpf
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-16
---

# src/Meridian.Wpf

## Purpose

WPF workstation is the active Windows desktop operator workstation sharing contracts and read models
with the browser workstation.

## Layer responsibility

This module owns the desktop shell, WPF pages, route hosting, and desktop workstation view models.
Keep shared contracts and read-model logic in shared UI services when browser and desktop both need
the behavior. The seven operator workspaces now register through feature modules under
`src/Meridian.Wpf/Features/` so new workspace-level navigation and shell ownership lands in the
matching module before it expands through the older flat page folders.

## Key folders and files

- `Features/` - workspace-owned module registration for Trading, Portfolio, Accounting, Reporting,
  Strategy, Data, and Settings.
- `ViewModels/` - desktop operator workflow view models.
- `Views/` - WPF pages and controls.
- `Shell/` and `Services/` - navigation, route, launch, and desktop service seams.

## Important workflows

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
through the shared accounting configuration service, renders the shared accounting production-readiness
assessment across ledger books, Rules Studio, posting execution, dimensions, external GL,
close/reporting, and tenant-admin blockers, renders tenant-admin control/evidence progress from the
shared readiness DTO, persists tenant-admin setup controls and retained evidence through the shared
accounting tenant administration profile store with WPF accounting admin-studio coverage, maps the
desktop aggregate setup checkbox into the shared enterprise configuration studio controls for chart
administration, rule-test/promotion setup, close setup, provider mapping, and tenant/company/report
group setup, plus separate enterprise-studio controls for ledger-book administration,
posting-rule authoring, approval queues, dimension mapping, and implementation sandbox validation,
and separate operational-hardening controls for audit review tooling, bulk import/export
safeguards, performance validation, and disaster-recovery runbooks, renders
the shared ledger-book-native workflow control count and retained ledger-book-scoped
workflow evidence for posting rules, JE lifecycle, close/reporting, external GL, reconciliation,
direct-lending projections, and strategy ledger reads, renders
dimensional ledger/query/report/export control counts with retained ledger-book-scoped evidence, renders
and saves retained tenant/company/fund/book-scoped production-certification controls through the
shared Accounting System profile store,
retained migration-run evidence plus generated migration rollout plan rows with ledger-book scope,
latest retained run, blocking issue codes, required actions, and canonical dimensions from the shared
production-readiness payload, renders the shared production-gap checklist for configurable
multi-ledger accounting, enterprise configuration studio coverage, guarded external GL integration,
dimensional ledger and reporting coverage, and production-control hardening, surfaces
shared ledger-book setup candidate guidance and can create the ledger book through the shared ledger-book service when book-scoped
configuration targets a missing registered book, renders a shared-workspace ledger-book
administration grid with selected/available books, fund-structure scope, basis, currency, policy,
description, and update timestamps, offers type-specific
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
Private-capital presets attach the shared
treasury ledger context expected by the approval service, including effective date, idempotency,
fund-event, capital-account, investor, payment, and settlement references.
Registration stays feature-owned in `Features/Accounting/AccountingFeatureModule.cs`; the
desktop fallback stores configuration/audit state in `workstation/accounting/accounting-configuration.json`
and manual journal drafts in `workstation/accounting/manual-journal-drafts.json` under the
configured workstation data root.

`FinancialRecordExplorerPage` is the generic WPF consumer for the shared Financial Record Explorer
DTO. `LedgerExplorer`, `PortfolioExplorer`, `SecurityInstrumentExplorer`, and
`ReportLineProvenanceExplorer` page tags resolve through the shell registry without adding new root
workspaces; the page maps shared columns and rows into `WorkstationTableInspectorControl` and
projects selected-record proof actions, `Used In`, and `Impacts` relationships into the inspector.
Empty or blocked source DTOs remain visible as disabled action states with server-provided reasons
rather than desktop-local placeholder balances.
Proof actions that carry shared Financial Record Explorer API hrefs map back to `LedgerExplorer`,
`PortfolioExplorer`, `SecurityInstrumentExplorer`, or `ReportLineProvenanceExplorer` page tags so
report-line drill-throughs stay route-compatible with the browser workstation. Report-line
provenance rows also carry shared instrument, position or transaction, reconciliation, journal,
report-line, evidence, and audit-link actions that WPF maps through the same view-model route
resolver instead of desktop-local lineage rules.

The desktop shell includes a first-launch and Settings entry point for a sample-data Demo / Sample Tour. Starting the tour enables `FixtureModeDetector` demo mode, selects the connected sample scenario, and walks operators through Data/provider status, Portfolio records, Accounting reconciliation, retained evidence/audit context, Reporting readiness, and Settings. The global demo banner and the tour banner label the workflow as demo/sample data only so sample records remain visually distinct from provider-backed operational data.

Keep desktop support aligned with shared contracts and governance posture.
Remote workstation calls should migrate through `IRemoteWorkstationClient`, which centralizes the
configured service URL, host health checks, and typed API calls for deployable WPF clients instead
of letting pages or services create their own HTTP clients or bind directly to the shared API
singleton. Watchlist backend synchronization now uses that seam for the optional `/api/watchlists`
probe while retaining local desktop persistence when the remote host does not provide a watchlist
payload. Activity Log also loads `/api/logs` through that seam and keeps the local offline
indicator path when the remote host is unavailable or returns a non-success response. Service
Manager health checks also use the same seam for deployable desktop clients; its graceful shutdown
path remains a local managed-process request because it uses the runtime-scoped shutdown token.
Setup Wizard backend readiness checks also use the remote seam, so first-run workstation setup
validates the configured remote host instead of issuing a page-local direct HTTP health probe.
The Symbols page Security Master bridge also resolves selected tickers through the same remote
client and shared workstation Security Master route instead of issuing page-local HTTP calls.
Ticker Strip quote polling also uses the remote client for `/api/live/{symbol}/quote`, preserving
the existing no-op offline behavior on non-success responses while keeping the service URL and HTTP
client lifecycle centralized for deployable desktop workstations.
After authentication and configuration initialization, WPF now starts the generic host lifecycle so
shared `IHostedService` registrations, including database-backed projection and outbox workers from
the shared composition graph, run under the desktop shell and stop through the existing host shutdown
path on exit.
Convention-based view-model wiring is handled by `Services/ViewModelViewResolver.cs`; shell pages
that follow the `*Page` to `*ViewModel` naming convention can receive a DI-constructed DataContext
without page-specific registration, while pages that set their own DataContext remain authoritative.
Runtime desktop capability toggles are declared by feature modules and surfaced in Settings through
the feature capability gate. The Security Master page projects the workstation trust
snapshot's `scheduleBook` and `openLotReadModel` payloads into operator-visible schedule, factor,
provenance, and open-lot review sections.

The same page now loads the shared Instrument Passport endpoint for the selected security so desktop operators see provider-confidence, pricing, trust, and downstream usage evidence in parity with the browser Accounting workstream.
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
Fund Ledger reconciliation actions call the shared workstation reconciliation endpoints, refresh the
queue from the shared break read model after review/resolve/dismiss, and keep the selected decision
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
Fund Audit Trail surface while the browser resolves the same shared tag to `/reporting/evidence`.
Parameterized desktop targets such as `EvidenceWorkbench:accounting-record/{recordId}` preserve the
canonical evidence subject for row/readiness metadata while resolving to the same Fund Audit Trail
route. Direct WPF navigation and embedded page-content creation canonicalize those parameterized
targets before resolving page content and carry the subject plus source target through
`FundOperationsNavigationContext`, so view models can use the same shared target string carried by
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
targets (`FundReportPack`, `ReportRunStatus`, `Dashboard`, `AnalysisExport`, `ExportPresets`,
`FundAuditTrail`, and `DataQuality`) rather than desktop-local reporting logic.
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
cost center, counterparty, account/portfolio scope, and external GL dimension values, with legacy
scope labels used only when canonical dimensions are absent. Run Risk
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
Analysis Export now opens on compact run/preset readiness instead of an embedded header. Recent
export history renders through `DenseDataGridControl`, selected history rows drive an inspector, and
run/save commands expose missing export name, destination, metric, or date-range requirements through
disabled-action tooltips and inspector facts.
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
workspace pages can migrate selected-row detail without changing route/page tags.
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

## Related docs

- `docs/status/wpf-screen-development-tracker.md` - generated WPF screen Gantt chart and automated per-screen TODO checklist derived from the shell registry, desktop screenshot index, and WPF test references.
- `docs/screenshots/desktop/README.md` - maintained desktop screenshot evidence index consumed by the generated screen tracker.
- `src/Meridian.Ui.Shared/README.md`
- `docs/development/wpf-implementation-notes.md`
- `docs/source/generated/source-module-index.md`
