# WPF ↔ Web-UI Alignment Plan

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-05

## Browser UX program delta — 2026-11-11

The [browser improvement program](browser-workstation-ux-program.md) retains shared domain
authority and records these browser-first presentation changes. WPF source is unchanged.

| Capability | Browser delivery | WPF parity disposition |
| --- | --- | --- |
| Compact shell and context | Integrated provenance/status, one route H1, combined Context | Presentation follow-up; preserve desktop fail-closed provenance |
| Browse tables and inspection | Shared workbench, typed entity inspector, compact detail rail | Shared read models and personal views available; desktop presentation follow-up |
| Operator triage | Durable shared assignment/acknowledgement/snooze and activity; default combined inbox | Shared API available; desktop composition follow-up; no desktop-local state |
| Approval collaboration | Discussion and reviewer reassignment through Operations Continuity | Shared versioned authority available; desktop controls follow-up |
| Accounting workflow boundaries | Factor Paydown and Consolidation have scoped child routes | Existing desktop workflow placement retained; route split is browser presentation |
| Prior-period figures | Posted-period comparison through shared contracts | Shared query available; desktop controls follow-up |
| Operational charts | Retained provider/account/backfill/break/close evidence | Shared sources available; desktop visualizations follow-up |

These entries describe implementation scope, not operator acceptance or live certification.

## Browser UX consolidation delta — 2026-11-16

| Browser change | WPF state |
| --- | --- |
| One refresh control | Ported: shell Refresh and F5 reload the active page, docked panes and visible split panes in place (`IPageRefreshTarget`, `PageRefreshResolver`); page-level Refresh buttons bound to the page's main command are removed. Sub-panel Refresh buttons remain. |
| One plain page purpose, consistent titles | Ported for the seven workspace shell pages (`WorkspaceCopyCatalog`). Deeper pages still use "posture"/"handoff" wording. |
| Scope stated one way | Ported as wording: "All authorized scopes" / "No scope selected" (`WorkspaceCopyCatalog.Scope`). The Trading and Accounting shells still show their own context cards. |
| Consistent holdings figures | Partial: the position blotter shows the market value the execution source reports. No currency or weight columns; Account and Aggregate portfolio totals still come from separate endpoints. |
| Trust stated once, overview layout order | Not ported. |

WPF changes in this delta were compile-checked with the full WPF build; WPF tests run on Windows CI.

## Browser UX Phase A delta — figures and status — 2026-11-16

| Browser change | WPF state |
| --- | --- |
| One definition (label, population, comparison basis) per headline figure | Shared seam ready, not consumed: `GET /api/workstation/kpi-definitions` serves `WorkstationKpiDefinitions` (`src/Meridian.Ui.Shared/Services/WorkstationKpiDefinitions.cs`). WPF headline strips should read their labels and populations from it before composing presentation. |
| One provider-health count on every screen | Shared seam exists (`/api/workstation/cross-screen-counts`); WPF Data and Settings parity pages should show that count, not a page-local one. |
| Position surfaces name their source | Not ported. The WPF position blotter should state paper session vs live holdings vs retained run. |
| Status bar freshness never says "up to date" over a stale source | Not ported. |

## Browser UX Phase E delta — workflow features — 2026-11-17

| Browser change | WPF state |
| --- | --- |
| Prior-period comparisons for NAV, run attribution and the cash ladder | Ported (2026-11-17). All three read `PeriodComparisonDto` and render through `PeriodComparisonProjection` (`src/Meridian.Ui.Services/Services/Comparisons/`), which mirrors the browser panel and is tested in `tests/Meridian.Ui.Tests`. NAV: Fund Operations → Portfolio tab, in process through `FundOperationsWorkspaceReadService`, with a prior-date picker. Run attribution: Run Risk page, through `StrategyRunReadService`. Cash ladder: Run Cash Flow page, "Vs end of last month", over `GET /api/portfolio/cash-ladder/comparison` (`PeriodComparisonApiClient`), because the portfolio ladder's providers are host-composed; a 501 says "not available in this host". The shared section is `PeriodComparisonSectionViewModel` with `Controls/PeriodComparisonPanel.xaml`. |
| NAV trend and exposure trend (last 12 month-ends) on `/portfolio/nav` | Ported (2026-11-17): Fund Operations → Portfolio tab, `NavTrendSectionViewModel` with `Controls/NavTrendPanel.xaml` (ScottPlot NAV line, long/short/gross exposure, and a table), from `GetNavTrendAsync`. |
| Report P&L slices with a prior published period, "—" when there is none | Shared in the Reporting payload (`PriorTotalPnl`, `PnlChange`, tag `no-prior`); WPF Reporting should show the dash for `no-prior`. |
| One variance rule (`PeriodChange`: change ÷ \|prior\| × 100, none for a zero prior) | Shared in `Meridian.Contracts`; the posted-ledger comparison already uses it. WPF variance columns should show the server's percent. |
| Export of visible columns as shown (CSV and XLSX) with totals | Not ported. |
| Due-date urgency chips, overdue first | Not ported. |
| "New since you last looked" on Today, Approvals and Reporting triage | Shared seam exists (`/api/workstation/operator/visits/{surface}`, surfaces `today-queue`, `approvals`, `reporting-triage`). Not consumed by WPF. |
| Bulk Acknowledge and Snooze with a confirmation per item | Not ported; WPF can loop the same per-item triage command. |
| Strategy comparison of up to four runs with overlaid curves | Not ported. |

## Browser UX Phase 1 delta — trustworthy queue — 2026-11-18

| Browser change | WPF state |
| --- | --- |
| Reconciliation inspector offers root cause, resolution, waive, sign-off and reopen, with an eligibility reason, a before/after preview and plain refusal text | Shared seam exists: the break-queue casework endpoints (`/api/workstation/reconciliation/break-queue/{breakId}/root-cause`, `/resolution`, `/waive`, `/sign-off`, `/reopen`) and the taxonomy endpoint. Not consumed by WPF. WPF must read `transitionStatus` and `errorCode`, because refusals answer HTTP 200. |
| Activity (audit history) for the selected break | Shared seam exists (`GET .../break-queue/{breakId}/audit`, 404 when empty). Not consumed by WPF. |
| The fund scope (entity, fund, fund account) is added to any in-app link that lacks it; scope is kept per tab | Browser-only. WPF has one window-level scope and no URL. |
| Fund-account picker for the three-way comparison; fund-profile picker for NAV vs prior period | Not ported. WPF NAV comparison still takes the fund profile from the Fund Operations selection. |
| Exceptions page lists each break once, in the break queue | Not ported; no WPF equivalent page duplicates it today. |

## Workspace, layout and saved-view fixes — 2026-11-18

| Change | Browser | WPF |
| --- | --- | --- |
| Workspace export then import keeps the workspace | — | Fixed: `WorkspaceService.ImportWorkspaceAsync` reads with the same `DesktopJsonOptions.PrettyPrint` options the export writes (camelCase, case-insensitive), so older PascalCase files still import. A file without a workspace name is refused instead of importing an empty workspace. |
| Reset layout in every workspace shell | — | Ported: the command bar's **More** menu ends with **Reset layout** (`WorkspaceCommandBarControl.ResetLayoutCommand`, bound to `WorkspaceShellPageBase.ResetLayoutCommand`) in Trading, Portfolio, Accounting, Reporting, Data and Settings. Strategy keeps its own Reset action. All seven save the default panes for the current workspace and layout scope, so the reset survives a restart. Other workspaces keep their layouts. |
| Restoring a saved layout leaves global density alone | Fixed: a layout's density applies to the session only (`applySessionDensity`). It is not stored or synced as the operator's preference. Choosing a density in Settings replaces it. | Not applicable (WPF dock layouts carry no density). |
| Saving a table view under an existing name replaces it | Fixed: the button reads "Replace “Name”", and the list keeps one entry. | Same host behaviour, because the personal-view store saves by name (tenant, operator and table). |
| Restore says which saved columns were skipped, and refuses a view with none left | Fixed in `TableWorkbench`. | Not ported. |
| Saved views on `execution-journal`, `portfolio-nav-comparison`, `portfolio-cash-ladder-comparison`, `portfolio-attribution-period-comparison` and `accounting-period-closing-balance-comparison` | Fixed: registered in the host's table permission map (`WorkstationEndpoints.WorkstationTablePermissions`), each mirroring its data endpoint's permission. A ratchet test (`WorkstationTableViewRegistrationTests`) fails if a dashboard `workbenchId` is added without an entry. A 403 or 501 now disables saving with the reason. | Shared host change. |

Deferred, not done:
- rename, delete and default table views (new endpoints);
- host-stored layouts that follow the operator, with a "keep as my default" density;
- team-published presets;
- a versioned workspace file format with an import preview.

## Security Master review queues delta — 2026-11-25

Follow-up to the Security Master architecture-review backlog (PR #3736, review items P5/D1, A2, D2 and D3).
Both lanes read the shared contracts in `Meridian.Contracts/Workstation/SecurityMasterReviewQueueDtos.cs`
and the routes in `UiApiRoutes`. The desktop transport is
`Meridian.Ui.Services/Services/SecurityMaster/SecurityMasterReviewQueueApiClient`. Server refusals are parsed by
`SecurityMasterWorkbenchFailure`, which mirrors the browser `classifyWorkbenchWriteError`.

| Shared seam | Browser | WPF |
| --- | --- | --- |
| Review inbox: `GET /api/security-master/workbench/revisions?state=Draft,Submitted,Approved`, `POST .../{securityId}/workbench/discard` | `security-master-review-inbox.tsx` (open, discard) | Ported. A "Review inbox" tab on `Views/SecurityMasterPage.xaml` (`ViewModels/SecurityMaster/SecurityMasterReviewInboxViewModel`) loads when the page opens. It shows the same states, ages ("under 1h", hours, then days) and discard eligibility (Draft or Submitted only), and sends the same discard reason. **Open** selects the security and opens the passport editor on that revision, so a reviewer who is not the drafter can approve, publish or discard it. |
| Staging conflict: 409 `field-already-staged` with `stagedBy`, `fieldPath`, `revisionId` | Passport editor banner names the first stager | Ported. `SecurityPassportEditorViewModel` shows "<stager> already has an open revision on <field>…". The editor also has a **Discard revision** command for its working Draft or Submitted revision. |
| Conflict resolution `pendingRevisionId` (review D3 "Use B") | `security-passport-editor.tsx` adopts the staged draft as the working revision, shows a "Draft staged" status, and marks the conflict "Awaiting draft …" with Accept and Override disabled (`classifyConflictResolution`) | Ported. The editor adopts the staged draft as its working revision (Draft), so it goes through the ordinary submit, approve and publish path. The conflict resolves to the chosen source when that draft publishes. |
| Held amendments: `GET /api/security-master/held-amendments`, `POST .../{heldAmendmentId}/discard` | `security-master-held-amendments.tsx` below the review inbox (status filter, open security, discard) | Ported: a "Held amendments" tab (`SecurityMasterHeldAmendmentsViewModel`) with a status filter, **Open security** (to resolve the named conflict) and **Discard** for Held rows. |
| Exception queue: `GET /api/security-master/exception-cases`, `POST .../sync`, `POST .../{caseId}/assign` | `security-master-exception-queue.tsx` below the held amendments (server SLA verdict, breach count, sync, assign) | Ported: an "Exception queue" tab (`SecurityMasterExceptionQueueViewModel`). It shows the server's SLA verdict and age without recomputing them, plus a breach count, **Sync cases**, and **Assign** (pre-filled with the session actor). |

Provider-integration actor flags (`GET/POST /api/workstation/provider-integrations/actor-flags[/scan]`,
`ProviderIntegrationActorFlagDto`): browser panel `settings-provider-integration-actor-flags.tsx` on the advanced
provider route. **WPF gap:** no desktop consumer yet; a desktop panel should read the same routes.

Host change: the held-amendment and exception-case routes now serialize those shared DTOs through
`Meridian.Ui.Shared/Endpoints/SecurityMasterReviewQueueProjection`. Previously they returned an Application record and an
anonymous wrapper. The JSON is unchanged; `SecurityMasterReviewQueueApiClientTests.ServerProjectionRoundTripsIntoTheSharedContracts`
pins it.

Both lanes now show the same rows, SLA text and refusal sentences. The browser panels read through
`src/Meridian.Ui/dashboard/src/lib/api/security-master-review-queues.api.ts`, whose types mirror the shared DTOs.
Both lanes pre-fill the assignee with the signed-in operator's username: the desktop from the session actor, the browser
from `/api/auth/me` (`getSignedInUsername`). Neither overwrites a typed name. The browser does not use the workstation
session's `displayName`, which the server fills with a desk label rather than the person. Before this change the browser marked a "Use B" conflict resolved even though it stays open until the staged draft
publishes; it now shows it as awaiting that draft.

The browser passport editor's conflict list now comes from the trust snapshot's open `conflictAssessments`;
`SecurityPassportEditorLauncher` had not passed any conflicts before. The
launcher also reads `GET /api/security-master/workbench/revisions?securityId=…`. That route now takes an optional
`securityId`, and each row carries `conflictId` and `conflictCandidateSource` for a "Use B" draft, read server-side
from the draft's marker by `SecurityMasterWorkbenchCommandService.TryReadConflictCandidate`. A conflict therefore stays
marked "Awaiting draft …" across reloads, and **Continue draft** makes that draft the editor's working revision. The
WPF editor takes a conflict id as input rather than listing conflicts, so it has no equivalent list to mark.

Validation: the `Meridian.Ui.Tests` client, parser and presentation tests ran on Linux. The new and existing WPF
view-model tests (`SecurityMasterReviewQueueViewModelTests`, `SecurityPassportEditorViewModelTests`) ran in a Linux
harness that compiles the view-model sources directly. The full WPF build (XAML included) and the WPF test project run
on Windows CI only.

This plan operationalizes the v0.25 design-charter decision to reactivate the WPF desktop
workstation as an active, co-equal operator UI lane and bring it up to parity with the browser
workstation. It is the working companion to roadmap item `W8-WPF-PARITY-001`.

- Charter decision: [current design charter](../../product/meridian-design-document.md) (decision originated in the Version 0.25 reactivation statement; consult the charter's active-surfaces policy and version history for the current wording).
- Roadmap item: `W8-WPF-PARITY-001` in [`../roadmap/data/roadmap-items.yml`](../../roadmap/data/roadmap-items.yml).
- Desktop architecture: [`wpf-implementation-notes.md`](../../development/wpf-implementation-notes.md), [`../architecture/desktop-layers.md`](../../architecture/desktop-layers.md).

**Routing maintenance (2026-10-05):** this plan retains its dated parity assessment and implementation
evidence. Use the roadmap registry for current delivery state and the
[desktop testing guide](../../development/desktop-testing-guide.md) for current validation commands;
this routing update does not re-certify the matrix below.

## Reactivation Summary

- The WPF app (`src/Meridian.Wpf/`) already projects the seven canonical workspaces — Trading,
  Portfolio, Accounting, Reporting, Strategy, Data, Settings — through a workspace shell, command
  palette (~55 nav + 8 action commands), 50+ pages, and MVVM view models backed by shared services.
- Reactivation therefore does **not** rebuild the shell. It closes the parity gap that opened while
  the desktop lane was deferred (v0.24), i.e. browser-first screens that never received a WPF
  equivalent.
- **Shared-first rule (unchanged):** both workstations consume `Meridian.Ui.Services`,
  `Meridian.Ui.Shared`, and `Meridian.Contracts`. No parity page may invent desktop-local product
  state, readiness rules, or DTOs. Presentation can differ; business state cannot.

## Parity Matrix

Web screens live in `src/Meridian.Ui/dashboard/src/screens/` (grouped by logical screen). WPF paths
are under `src/Meridian.Wpf/`. Assessment date: 2026-07-06; bond, cash-flow and credential-ownership rows added 2026-10-31 from the PR #3328 review. The intercompany operator-surfaces row was added 2026-11-14.

| Web screen (logical) | Closest WPF equivalent | Parity |
| --- | --- | --- |
| accounting-screen | `Views/AccountingWorkspaceShellPage.xaml` + `Views/AccountingClosePage.xaml` + `Views/FundLedgerPage.xaml` | Full |
| data-screen | `Features/Data/Shell/DataWorkspaceShellPage.xaml` + `Views/DataBrowserPage.xaml` + `Views/SecurityMasterPage.xaml` | Full |
| data-operations-assurance-workstreams | shared contracts/endpoints only | **Gap** — browser Ingestion Operations Center and Storage & Data Assurance shipped first |
| trading-screen | `Features/Trading/Shell/TradingWorkspaceShellPage.xaml` + `Views/PositionBlotterPage.xaml` + `Views/OrderBookPage.xaml` | Full |
| trading-screen order authority (governed order command boundary, `W13-OMS-001`): working-order Origin column, order-ticket authority refusals (`OrderResult.authorityDecision`), and the live manual-entry verdict (`TradingWorkspaceResponse.manualOrderEntry`) | `Views/OrderBookPage.xaml` (no WPF order ticket exists) | **Gap** (2026-11-17) — the shared seams are in place: `WorkstationTradingOrderRow.Origin`, `WorkstationTradingPayload.ManualOrderEntry` and `OrderResult.AuthorityDecision`. Remaining: an Origin column on the desktop order book. A future WPF ticket must post to the shared `orders/submit` route, render `authorityDecision`, and disable itself from `manualOrderEntry`; it must not decide live availability locally |
| strategy-screen | `Views/StrategyWorkspaceShellPage.xaml` + `Views/StrategyRunsPage.xaml` | Full |
| reporting-screen | `Features/Reporting/Shell/ReportingWorkspaceShellPage.xaml` + `Views/AnalysisExportPage.xaml` + `Views/ScheduleManagerPage.xaml` | Full |
| settings-screen | `Views/SettingsPage.xaml` + `Features/Settings/Shell/SettingsWorkspaceShellPage.xaml` | Full |
| portfolio-screen | `Features/Portfolio/Shell/PortfolioWorkspaceShellPage.xaml` + `Views/AccountPortfolioPage.xaml` | Full |
| market-data-screen (Market Data desk at `/data/quotes`, consolidated per `W8-UX-CONSOL-001`; live-quotes, watchlist, and price-alerts panels as `?view=` tabs) | `Views/LiveDataViewerPage.xaml` + `Views/WatchlistPage.xaml` + `Views/NotificationCenterPage.xaml` | Full for quotes/watchlist; alerts view Partial — alert surface exists, less rule-authoring focus |
| quant-lab-screen | `Views/QuantScriptPage.xaml` | Full |
| fund-structure (entity setup) | `Views/FundStructureSetupPage.xaml` + `Views/FundProfileSelectionPage.xaml` | Full |
| operating-scope picker entity/fund/account options (browser `scope-picker.tsx` over `GET /api/workstation/scope-options`, 404 fallback to the organization graph) | `Views/FundProfileSelectionPage.xaml` Scope Options card (`ViewModels/WorkstationScopeOptionsViewModel`) over `WorkstationScopeOptionsApiClient` | Partial (2026-11-14) — both lanes read the same grant-filtered `WorkstationScopeOptionsDto` and narrow by the same rule (entity → holding funds and booked/held accounts; fund → its accounts; unplaced accounts left out and counted); a 403 or other failure is reported, never retried against the graph. Remaining: the desktop 404 fallback reads the in-process (desktop-local, ungated) fund-structure graph rather than the host's `/api/fund-structure/graph`; picks select an existing desktop operating context instead of setting a persistent `entity › fund › account › period` scope bar; no period picker, URL-carried scope, saved default (`OperatingScopeValuesDto`), household-account merge, or screen-level fund narrowing on the desktop yet |
| asset-detail-screen | `Views/SecurityPassportEditorView.xaml` (drill-in from `SecurityMasterPage`) | Partial — less unified than the web tabbed detail |
| family-office-screen | `Views/AggregatePortfolioPage.xaml` | Partial — aggregation exists, less family-office framing |
| cash-ladder-screen | `Views/RunCashFlowPage.xaml` | Partial — WPF ladder is per-strategy-run; web is portfolio-wide liquidity ladder. The portfolio-wide comparison with the end of last month is available on the page (Phase E). |
| trial-balance (tab of finance-standard-pages ledger explorer at `/accounting/ledger?view=trial-balance`; consolidated per `W8-UX-CONSOL-001`) | `Views/PostedLedgerPage.xaml` | Aligned — both lanes now read the posted journal by ledger period over `/api/ledger/periods/*`; the shared projection lives in `Meridian.Ui.Services` (`PostedLedgerProjection`) so neither lane can drift on default-period choice, missing-summary handling, or balance integrity |
| accounting-screen posted-ledger panel (`/accounting/ledger`) | `Views/PostedLedgerPage.xaml` | Aligned — desktop reads the governed book over the shared API seam; no desktop-local ledger state |
| accounting-screen audit chain integrity panel (Configuration; `GET /api/ledger/audit-integrity`) | `Views/AccountingConfigurePage.xaml` Audit Chain Integrity card (`AuditChainIntegrityViewModel`) | Aligned — both lanes call the same `AdminMaintenance`-gated endpoint on demand and present each chain's server verdict without recomputing it; 403/409/429 read as distinct refusals, not failures, and leaving the page cancels a running check |
| accounting-screen onboarding shadow-mode panel (External GL; `/api/accounting/onboarding/workspaces/{id}/shadow-designation`, `/opening-balances`, `/opening-balances/report`, `/differences/{key}/explanation`, `/certificates`, `/promotion`) | `Views/AccountingConfigurePage.xaml` Onboarding parallel run tab (`Views/OnboardingShadowControl.xaml`) over `OnboardingShadowWorkspace` + `WorkstationOnboardingApiClient` in `Meridian.Ui.Services` | Partial (2026-11-14) — the shadow panel is aligned: same routes and contracts, the same explainable set (opening breaks plus period differences, never missing lines), uncovered-kind and source summaries, certificate and promotion gating, every mutation carrying the workspace version, a 409 that reloads the server copy and keeps the operator's inputs. Remaining: creating workspaces, editing criteria, capturing comparisons, reviews and packet export are browser-only |
| journal-entry-detail-screen | `Views/FundLedgerPage.xaml` (journal entries) | Partial — no standalone JE detail page |
| statement-import-screen | `ViewModels/FundLedgerViewModel.StatementReconciliation.cs` + `Views/PortfolioImportPage.xaml` | Partial — recon exists, no guided import screen |
| daily-control-tower-screen | `Views/DashboardPage.xaml` + `Views/WorkspaceDecisionQueueControl.xaml` | Partial — dashboard/decision-queue overlap, not a triage tower |
| report-library-screen | `Views/WorkflowLibraryPage.xaml` + Reporting shell | Partial |
| report-run-parameters-screen | `Views/AnalysisExportWizardPage.xaml` + Reporting shell | Partial |
| finance-standard-pages-screen (evidence-detail stub retired per `W8-UX-CONSOL-001`; `/accounting/evidence/detail` redirects into the reporting evidence workbench) | `Views/AnalysisExportPage.xaml` + Reporting shell | Partial |
| strategy-designer-screen | `Views/QuantScriptPage.xaml` + `Views/BacktestPage.xaml` | Partial — authoring present, no designer canvas |
| accounting-screen bond analytics and migration-preview panels (Evidence tab for Bond/CD assets; `GET /api/reference-data/bonds/{id}/analytics`, `POST .../migration-preview`, `.../migration-submit`) | `Views/SecurityPassportEditorView.xaml` bond section (`ViewModels/SecurityMaster/BondAnalyticsViewModel`, `BondMigrationPreviewViewModel`) over `WorkstationSecurityMasterApiClient` | Aligned — both lanes call the same analytics and hash-bound migration routes, show the server's exact decimals, and stage drafts with digests rather than amounts; the desktop section appears only for asset classes that carry a bond coupon structure |
| bond calculation cutover console (`GET .../bonds/cutover-readiness`, `POST .../bonds/cutover`; browser `accounting-screen.bond-cutover.tsx` in the Accounting configuration section) | `Views/AdminMaintenancePage.xaml` "Bond calculation cutover" card (`ViewModels/Settings/BondCutoverConsoleViewModel`) over `WorkstationBondCutoverApiClient` | Aligned — both lanes read the complete inventory on demand, require a reviewed block for every undeclared bond bound to its retained version and content hash, confirm the review, submit the fresh inventory hash, echo the current receipt hash on amendment and show the retained receipt; both also run the read-only `.../migration-preview/batch` comparison of one candidate convention set across up to 100 undeclared bonds (staging stays per bond); `.../settlement-consideration` still has no UI consumer in either lane |
| contractual cash-flow scenario preview and retained runs (`POST /api/security-master/{id}/cashflow-projections/preview`, `.../runs[/{runId}]`; `POST /api/loans/{id}/projections/preview`, `.../contractual-runs`; browser `contractual-cash-flow-workbench.tsx` on the asset-detail "Cash-flow scenarios" tab and below the loan book) | `Views/SecurityPassportEditorView.xaml` and `Views/DirectLendingPage.xaml` Evidence tab "Cash-flow scenarios" expanders (`ViewModels/CashFlows/ContractualCashFlowScenarioViewModel`) over `WorkstationContractualCashFlowApiClient` | Aligned — both lanes send valuation date, knowledge cutoff, horizon, rate shock, eligibility-driven exercise selections and one prepayment assumption; show the event ladder, blockers, exercise calculations and evidence hashes as exact server decimals; retain runs under a client-generated identity that any edit renews; verify a loaded retained run's replay by output hash; name an approved exercise notice on an exercise selection so it is retained rather than hypothetical; and compare two retained runs of the subject (`GET .../runs/{runId}/compare/{otherRunId}`: per-date net cash deltas, blocker differences, replay verification of both packets); curve pins have no UI yet in either lane |
| provider credential ownership commands (`POST /api/provider-routing/connections`, `PUT .../runtime-credentials`, `GET .../credentials/migration-preview`, `POST .../credentials/migrate`, `.../rollback`; browser `settings-provider-ownership-panel.tsx` inside the owned-credentials editor) | `Views/CredentialManagementPage.xaml` "Ownership administration" expander (`ViewModels/Settings/ProviderOwnershipAdministrationViewModel`) over `WorkstationProviderOwnershipApiClient` | Aligned — both lanes create owned connections, preview the non-secret vault status, migrate only with the previewed generation, roll back the exact retained receipt, finalize a verified migration (`POST .../credentials/finalize`, which ends rollback and releases the provider-wide record) and bind the host runtime credential; tenant and actor stay server-resolved and both read the binding's running-instance status (`GET .../runtime-credentials/status`) and offer the opt-in `POST .../runtime-credentials/apply`, which the default host answers with pending-restart because it has no in-process provider recycle |
| bond reference keyset pages (`GET .../bonds/issuer-ladder/page`, `.../maturity-ladder/page`; browser `bond-reference-ladder-panel.tsx` on the Security Master Reference data tab) and cash-ladder `engineVersion` replay | `Views/SecurityMasterPage.xaml` "Bond Ladders" tab (`ViewModels/SecurityMaster/BondReferenceLadderViewModel`) over `WorkstationSecurityMasterApiClient` | Partial — both lanes page issuer and maturity ladders by opaque cursor behind an explicit "Load more", never mixing pages across queries; the array ladder routes remain for the endpoint catalog and are scheduled for deprecation in `bond-capability-expansion.md`; the explicit `engineVersion` v1 cash-ladder replay still has no UI consumer in either lane |
| security-master review queues (browser `security-master-review-inbox.tsx`; held-amendment and exception-case routes; passport `field-already-staged` and "Use B" `pendingRevisionId`) | `Views/SecurityMasterPage.xaml` "Review queues" card (`ViewModels/SecurityMaster/SecurityMasterReviewInboxViewModel`, `SecurityMasterHeldAmendmentsViewModel`, `SecurityMasterExceptionQueueViewModel`) and `SecurityPassportEditorViewModel` over `SecurityMasterReviewQueueApiClient` | Aligned for the review inbox, held amendments, the exception queue, the passport staging conflict and "Use B" draft adoption (2026-11-25); see the 2026-11-25 delta |
| operations-continuity-screen | `Views/OperationsContinuityPage.xaml` + `ViewModels/OperationsContinuityViewModel.cs` (Wave P3, delivered 2026-08-05) | Partial — workflow queue with detail (gates, blockers, checklist, next action), the unified open-item operator queue, and the promoted close-calendar and approval-policy cards ship read-only over the widened `IOperationsControlCenterClient`; approval/close/reopen commands and the dashboard, cockpit, timeline, and reviewed-automation panels remain browser-first |
| evidence-workbench-screen (canonical mount `/reporting/evidence` per `W8-UX-CONSOL-001`; former `/accounting/evidence` and `/data/evidence` mounts redirect there) | `Views/EvidenceWorkbenchPage.xaml` + `ViewModels/EvidenceWorkbenchViewModel.cs` (Wave P1, delivered 2026-08-05) | Partial — subjects, packet completeness, proof chain, lineage, vault request lists/documents, validate, and manifest export ship over the shared evidence endpoints; document intake and reviewer accept/reject remain browser-first |
| operator-readiness-console | `Views/OperatorReadinessConsolePage.xaml` + `ViewModels/OperatorReadinessConsoleViewModel.cs` (Wave P2, delivered 2026-08-05) | Partial — cross-lane gates, session/trust/promotion/run/break panels, and the prioritized operator work-item queue ship over the shared readiness, inbox, reconciliation, and run-summary seams; browser-only extras (per-endpoint API source strip, fund-account switcher, next-action hero) remain browser-first |
| operations-record-release-screen | `Views/OperationsRecordReleasePage.xaml` + `ViewModels/OperationsRecordReleaseViewModel.cs` (Wave P3, delivered 2026-08-05) | Partial — the six-step release path composes the continuity workflow's gates, accounting-record summary, and close package with the browser's tone-combination gating; the source-data step and reporting recipients/distribution columns remain browser-first |
| accounting-screen intercompany operator surfaces (browser `accounting-screen.entity-agreements.tsx`, `.entity-obligations.tsx`, `.intercompany-close-advisories.tsx`, `.entity-proposals.tsx` + `.entity-counterpart-repricing.tsx`, `.entity-interest-accrual.tsx`, `.entity-accrual-schedules.tsx`, `.entity-management-fee.tsx`, `.entity-confirmations.tsx`, `.entity-rate-fixings.tsx`; `/api/fund-structure/intercompany-agreements*`, `/api/ledger/intercompany/obligations`, `/proposals*`, `/breaks`, `/accruals/preview`, `/accruals/drafts`, `/accrual-schedules*`, `/fees/preview`, `/fees/drafts`, `/confirmations*`, `/rate-fixings*`) | `Views/FundLedgerPage.xaml` Agreements tab (`Views/IntercompanyAgreementsControl.xaml` over `IntercompanyAgreementWorkspace`), Counterpart proposals tab (`Views/IntercompanyCounterpartProposalsControl.xaml` over `IntercompanyCounterpartQueueWorkspace`), Interest accrual tab (`Views/IntercompanyInterestAccrualControl.xaml` over `IntercompanyInterestAccrualWorkspace`, added 2026-11-16), Accrual schedules tab (`Views/IntercompanyAccrualSchedulesControl.xaml` over `IntercompanyAccrualScheduleWorkspace`, added 2026-11-17), Management fee tab (`Views/IntercompanyManagementFeeControl.xaml` over `IntercompanyManagementFeeWorkspace`, added 2026-11-17), Balance confirmation tab (`Views/IntercompanyBalanceConfirmationControl.xaml` over `IntercompanyBalanceConfirmationWorkspace`, added 2026-11-17), Rate fixings tab (`Views/IntercompanyRateFixingsControl.xaml` over `IntercompanyRateFixingWorkspace`, added 2026-11-17), and the close-readiness intercompany advisory (`ViewModels/FundLedgerViewModel.CloseReadiness.cs`) | Aligned (2026-11-14) for agreements with the structured-terms summary and editor, external-review attestation, the advisory obligation monitor, close advisories, and the counterpart proposal queue (delivery records, aged booking breaks with their stated basis, reasoned retry, explicit FX repricing preview/apply, and linking an existing counterpart). Both lanes call the same routes, take `canManageLedger` from `/api/ledger/intercompany/capabilities`, reuse a request identity for an identical retry, and show the server's exact decimals; the desktop keeps no proposal state. The loan-interest accrual request (2026-11-16) previews and drafts over the same two routes in both lanes and binds the draft to the previewed evidence digest. Periodic compounding (2026-11-17) uses the same routes; both lanes show each slice's compounded interest. Fixed management fees (2026-11-17) declare their per-period or per-annum basis in both agreement editors and preview and draft over `/api/ledger/intercompany/fees/*` in both lanes, binding the draft to the previewed evidence digest. Advisory reciprocal balance confirmation (2026-11-17) loads, proposes, confirms, disputes and resolves over the same `/api/ledger/intercompany/confirmations` routes in both lanes against the exact version and balance digest shown. Floating-rate accrual (2026-11-17): both lanes declare the fixing lag in the agreement editor, list, propose, approve and reject reviewed fixings over `/api/ledger/intercompany/rate-fixings`, and show each slice's pinned fixing. Since 2026-11-17 the desktop picks books and profiles from lists as the browser does: the accrual source book, its LoanInterest profile and the Loan principal profile of the same legal agreement, and the counterpart queue's source book and the counterparty entity's existing-counterpart book. Opt-in accrual schedules (2026-11-17) use the same `/accrual-schedules*` routes in both lanes: propose with a reason, maker-checker approval of the exact version, reasoned reject/disable, a dated due-period preview and run-now for active schedules; runs only draft for ordinary review and nothing posts. Since 2026-11-18 both lanes pick the existing counterpart journal from `GET /api/ledger/intercompany/proposals/{id}/counterpart-candidates`, which lists only posted journals in the chosen approved book that the server's link check would accept; linking still re-checks the choice. The desktop's "Open journal" action opens the Fund Ledger Journal tab for the book's fund profile with that entry selected. Remaining: native interaction is proven only by the Windows `IntercompanyNativeControlSmokeTests` and `FundLedgerViewModelTests` lane, not by the Linux workspace tests |
| margin-control-center-screen (`/accounting/margin-control`; `GET /api/workstation/reconciliation/margin-control` plus snapshot certification) | none | **Gap** — no desktop surface for provider margin evidence, shadow-model comparison, or end-of-day margin certification |
| covered-call-screen | `Views/OptionsPage.xaml` (chain viewer only) | **Gap** — no covered-call writing/roll workflow |
| strategy-formula-workbench (withdrawn; `/strategy/quant-lab?view=formulas` now canonicalizes to the Script Lab) | `Views/QuantScriptPage.xaml` | Not a WPF parity gap, but for a different reason than before — the browser tab was a permanent "Formula catalog is not connected" placeholder and has been withdrawn, so neither lane ships the surface. Do not treat this row as delivered capability: the built `strategy-formula-workbench` component is still unmounted and still has no formula-catalog endpoint behind it. When that endpoint lands, the browser mounts first and this row becomes a real parity gap to schedule. |

## True Gaps (ranked by operator centrality)

1. **covered-call-screen** — a staged covered-call income-strategy workflow (chain preview, trade
   timeline, run history). WPF `OptionsPage` is a read-only chain viewer. Web source:
   `screens/covered-call-screen.tsx` + `.view-model.ts`.
2. **data-operations-assurance-workstreams** — add Data workspace pages for the browser-first
   Ingestion Operations Center and Storage & Data Assurance. Consume
   `DataOperationsAssuranceDtos` and the shared workstation endpoints; do not reconstruct job
   transitions, maintenance permissions, preview fingerprints, or assurance summaries in WPF.
3. **margin-control-center-screen** — a desktop view of the browser's Margin Control Center:
   provider margin evidence, the shadow-model comparison, staleness alerts, and end-of-day
   certification. Consume the shared `MarginControlCenterDtos` and the
   `/api/workstation/reconciliation/margin-control` endpoints; do not recompute certification
   eligibility or shadow margin in WPF.
4. **strategy-designer-screen** (borderline) — a visual strategy builder (cells, legs, payoff chart).
   WPF covers authoring via `QuantScriptPage`/`BacktestPage` but has no designer canvas.

Closed gaps: **evidence-workbench-screen** (Wave P1), **operator-readiness-console** (Wave P2), and **operations-continuity-screen** + **operations-record-release-screen** (Wave P3)
shipped 2026-08-05 — see the matrix rows above and the delivery notes under each wave.

## Closure Sequence

Each wave follows the established desktop contribution pattern (see
[`wpf-implementation-notes.md`](../../development/wpf-implementation-notes.md) → *Contributing*): add the page + view
model, register one `ShellNavigationCatalog` entry, add a `CommandPaletteService` entry with the
workspace label, and add a workspace default-pane entry only if it belongs in a dock layout. Every
view model must consume the same shared read model the browser screen consumes.

### Wave P1 — Wire the already-referenced Evidence Workbench (highest leverage, lowest ambiguity)

**Delivered 2026-08-05.**

- `Views/EvidenceWorkbenchPage.xaml` + `ViewModels/EvidenceWorkbenchViewModel.cs` bind the shared
  evidence endpoints (`/api/workstation/evidence/*`) through the thin
  `Services/EvidenceWorkbenchApiClient.cs`, and reuse the Evidence Vault read models
  (`Workstation/Models/EvidenceVaultPresentationModels.cs`) for the retained-document queue plus the
  new packet projections in `Workstation/Models/EvidenceWorkbenchPresentationModels.cs`.
- The `EvidenceWorkbench` tag is registered in the Reporting workspace ("Report Packs" section), so
  existing `EvidenceWorkbench:{subject}` deep links from `FundLedgerViewModel` and
  `WorkflowLibraryViewModel` resolve to the real page; `NavigationService` passes the
  `{subjectKind}/{subjectId}` subject through the page's navigation `Parameter`.
- Tests: `Meridian.Wpf.Tests/ViewModels/EvidenceWorkbenchViewModelTests.cs` (projection, deep-link
  parsing, validate/export flows) plus updated shell routing tests.
- Follow-up (tracked, not blocking P1): document intake and reviewer accept/reject actions remain
  browser-first; add them when the desktop lane needs write-side vault parity.

### Wave P2 — Operator Readiness Console

**Delivered 2026-08-05.**

- `Views/OperatorReadinessConsolePage.xaml` + `ViewModels/OperatorReadinessConsoleViewModel.cs`
  aggregate the shared server-owned readiness contracts — `TradingOperatorReadinessService`
  (in-process, same contract as `/api/workstation/trading/readiness`), the operator-inbox client,
  the reconciliation break-queue client, and `StrategyRunWorkspaceService` run summaries — into one
  cross-lane console matching `operator-readiness-console.view-model.ts`. Sources that are absent or
  fail surface per-panel error text instead of empty-looking rows, and refreshes use the same
  supersede-revision cancellation pattern as the Wave P1 Evidence Workbench.
- Registered on the Home/Launchpad surface (`HomeFeatureModule`, strategy workspace, order -5) as
  "Operator readiness" with a default command-palette entry; each work item routes to its owning
  workspace via registered target tags with kind- and workspace-based fallbacks.
- Tests: `Meridian.Wpf.Tests/ViewModels/OperatorReadinessConsoleViewModelTests.cs` (readiness
  aggregation, per-panel degradation, work-item route resolution, supersede race) plus updated
  `HomeFeatureModuleTests`.
- Follow-up (tracked, not blocking P2): fund-account scoping via the operating-context resolver,
  the per-endpoint API source strip, and the next-action hero remain browser-first. Three browser
  panel compositions are also still browser-first: the Data-workspace provider posture rows inside
  the provider-trust panel, the "Promotion blockers" composite (promotion-under-review plus
  non-Ready gates plus PromotionReview work items), and the report-pack facts panel; the desktop
  console surfaces the equivalent truth through its gates, promotion, and work-item panels until
  those land.

### Wave P3 — Operations Continuity + Record Release

**Delivered 2026-08-05.**

- `Views/OperationsContinuityPage.xaml` + `ViewModels/OperationsContinuityViewModel.cs` promote the
  former Settings "Operations Control" tab into a dedicated accounting page: the continuity workflow
  queue with selected-workflow detail (gates, blockers, close checklist, and the server-recommended
  next action picked by gate-status priority), the unified open-item operator queue with its
  blocked/review/clear rollup, and the promoted close-calendar and approval-policy cards.
  `IOperationsControlCenterClient` was widened with the workflow list/detail read routes (null on
  API failure, so an outage never renders as an empty queue).
- `Views/OperationsRecordReleasePage.xaml` + `ViewModels/OperationsRecordReleaseViewModel.cs` follow
  the browser record-release screen's six-step release path over the most recently updated
  continuity workflow, with the browser's tone-combination gating (blocked outranks review outranks
  neutral outranks ready, and an unknown step keeps the release out of Ready).
- The `OperationsContinuity` tag was promoted from a `FundLedger` alias to a real page, so the
  shared inbox route map's continuity entry now lands on the dedicated surface; `OperationsClose`
  keeps its ledger alias.
- Tests: `OperationsContinuityViewModelTests` (workflow/queue projection, next-action priority,
  per-panel degradation) and `OperationsRecordReleaseViewModelTests` (step gating, tone
  combination, fail-closed accounting record, publication readiness) plus updated shell routing and
  feature-module tests.
- Follow-up (tracked, not blocking P3): the continuity mutations (assign/resolve breaks, checklist
  acknowledge, approval submit/approve/reject, close, governed reopen) and the dashboard, cockpit,
  timeline, evidence-package, and reviewed-automation panels remain browser-first, as do the
  record-release source-data step (Data-workspace payload) and the reporting recipients/
  distribution columns.

### Wave P4 — Covered Call workflow + partial-parity polish

- Extend `OptionsPage`/add `Views/CoveredCallPage.xaml` (Strategy/Trading) for the staged
  covered-call workflow (chain preview, trade timeline, run history) matching `covered-call-screen`.
- Close the partial-parity items where the browser screen has clearly moved ahead: trial-balance
  depth inside the ledger surface (the web lane now folds trial balance into its ledger explorer per
  `W8-UX-CONSOL-001`), a dedicated journal-entry-detail page, portfolio-wide cash ladder, and a daily
  control tower view over the existing dashboard/decision-queue state.
- `strategy-designer-screen` remains optional pending a decision on whether the desktop lane needs a
  visual designer canvas beyond `QuantScriptPage`/`BacktestPage`.
- Browser shell note (Phase 1 hierarchy pass, 2026-11-05): the web sidebar now pins a `Today` entry
  for the Daily Control Tower and shows per-workspace attention counts. No shared contract changed:
  the counts come from `selectDailyControlTowerQueueItems` in the dashboard's `lib/`, a projection of
  the existing decision queue. When WPF builds its control tower view, derive any counts from the
  same queue, and move that selector into `Meridian.Ui.Services` first if the desktop needs it, so
  the two shells cannot disagree about what needs an operator.

### Wave P5 — Data operations and assurance parity

- Add Ingestion Operations and Storage Assurance pages under the WPF Data workspace using the
  shared DTOs and `/api/workstation/data/*` endpoints introduced by the browser slice.
- Preserve the same permission, preview/execute, typed-confirmation, evidence-link, and
  copy-only-migration semantics; WPF owns presentation only.
- Tests: route registration, job-action availability, permission-disabled maintenance actions,
  and preview confirmation projection.

## Validation

WPF targets `net10.0-windows`; validate on Windows or with `/p:EnableWindowsTargeting=true`, and rely
on GitHub Actions `Meridian CI / quality-gate` and the Windows desktop build workflow as the
authoritative gates.

```bash
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableWindowsTargeting=true -c Release
dotnet test tests/Meridian.Wpf.Tests /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet test tests/Meridian.Ui.Tests /p:EnableWindowsTargeting=true
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/validate-docs-structure.py --summary
```

### Data workspace alignment parity

WPF Time Series Alignment uses the shared `/api/alignment/preview` and `/api/alignment/create`
routes. Browser adoption is sequenced under this plan and will consume the same routes.

## Definition of Done for W8-WPF-PARITY-001

- Every browser workstation screen has a WPF equivalent page/view model or an explicitly sequenced
  wave item in this plan (matrix above is the tracker).
- The `EvidenceWorkbench` navigation target resolves to a real page (Wave P1 — delivered 2026-08-05).
- New parity surfaces consume shared contracts, read models, and workstation endpoints; no
  desktop-local product state, DTOs, or readiness rules are introduced.
- Desktop build and desktop-focused service tests pass on the authoritative CI gate.


## W10 shared-close sequencing receipt - 2026-09-04

Fund Ledger close headlines and the queue consume `FinancialOperationsCommandCenterDto.CloseReadiness`. Desktop requests reuse explicitly selected tenant/company/fund/book/account/entity/period context. Browser Accounting forwards the same business scope, including entity. Neither workstation may substitute a local ready calculation when the shared decision is missing. Fund-wide metrics remain diagnostic; scoped selection and recovery journeys require validation before W10-SEAM-001 acceptance.
