# WPF ↔ Web-UI Alignment Plan

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-05

This plan operationalizes the v0.25 design-charter decision to reactivate the WPF desktop
workstation as an active, co-equal operator UI lane and bring it up to parity with the browser
workstation. It is the working companion to roadmap item `W8-WPF-PARITY-001`.

- Charter decision: [`../product/meridian-design-document.md`](../product/meridian-design-document.md) (decision originated in the Version 0.25 reactivation statement; carried forward by the Version 1.0 charter's active-surfaces policy and version history).
- Roadmap item: `W8-WPF-PARITY-001` in [`../roadmap/data/roadmap-items.yml`](../roadmap/data/roadmap-items.yml).
- Desktop architecture: [`wpf-implementation-notes.md`](./wpf-implementation-notes.md), [`../architecture/desktop-layers.md`](../architecture/desktop-layers.md).

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
are under `src/Meridian.Wpf/`. Assessment date: 2026-07-06.

| Web screen (logical) | Closest WPF equivalent | Parity |
| --- | --- | --- |
| accounting-screen | `Views/AccountingWorkspaceShellPage.xaml` + `Views/AccountingClosePage.xaml` + `Views/FundLedgerPage.xaml` | Full |
| data-screen | `Features/Data/Shell/DataWorkspaceShellPage.xaml` + `Views/DataBrowserPage.xaml` + `Views/SecurityMasterPage.xaml` | Full |
| data-operations-assurance-workstreams | shared contracts/endpoints only | **Gap** — browser Ingestion Operations Center and Storage & Data Assurance shipped first |
| trading-screen | `Features/Trading/Shell/TradingWorkspaceShellPage.xaml` + `Views/PositionBlotterPage.xaml` + `Views/OrderBookPage.xaml` | Full |
| strategy-screen | `Views/StrategyWorkspaceShellPage.xaml` + `Views/StrategyRunsPage.xaml` | Full |
| reporting-screen | `Features/Reporting/Shell/ReportingWorkspaceShellPage.xaml` + `Views/AnalysisExportPage.xaml` + `Views/ScheduleManagerPage.xaml` | Full |
| settings-screen | `Views/SettingsPage.xaml` + `Features/Settings/Shell/SettingsWorkspaceShellPage.xaml` | Full |
| portfolio-screen | `Features/Portfolio/Shell/PortfolioWorkspaceShellPage.xaml` + `Views/AccountPortfolioPage.xaml` | Full |
| market-data-screen (Market Data desk at `/data/quotes`, consolidated per `W8-UX-CONSOL-001`; live-quotes, watchlist, and price-alerts panels as `?view=` tabs) | `Views/LiveDataViewerPage.xaml` + `Views/WatchlistPage.xaml` + `Views/NotificationCenterPage.xaml` | Full for quotes/watchlist; alerts view Partial — alert surface exists, less rule-authoring focus |
| quant-lab-screen | `Views/QuantScriptPage.xaml` | Full |
| fund-structure (entity setup) | `Views/FundStructureSetupPage.xaml` + `Views/FundProfileSelectionPage.xaml` | Full |
| asset-detail-screen | `Views/SecurityPassportEditorView.xaml` (drill-in from `SecurityMasterPage`) | Partial — less unified than the web tabbed detail |
| family-office-screen | `Views/AggregatePortfolioPage.xaml` | Partial — aggregation exists, less family-office framing |
| cash-ladder-screen | `Views/RunCashFlowPage.xaml` | Partial — WPF ladder is per-strategy-run; web is portfolio-wide liquidity ladder |
| trial-balance (tab of finance-standard-pages ledger explorer at `/accounting/ledger?view=trial-balance`; consolidated per `W8-UX-CONSOL-001`) | `Views/PostedLedgerPage.xaml` | Aligned — both lanes now read the posted journal by ledger period over `/api/ledger/periods/*`; the shared projection lives in `Meridian.Ui.Services` (`PostedLedgerProjection`) so neither lane can drift on default-period choice, missing-summary handling, or balance integrity |
| accounting-screen posted-ledger panel (`/accounting/ledger`) | `Views/PostedLedgerPage.xaml` | Aligned — desktop reads the governed book over the shared API seam; no desktop-local ledger state |
| journal-entry-detail-screen | `Views/FundLedgerPage.xaml` (journal entries) | Partial — no standalone JE detail page |
| statement-import-screen | `ViewModels/FundLedgerViewModel.StatementReconciliation.cs` + `Views/PortfolioImportPage.xaml` | Partial — recon exists, no guided import screen |
| daily-control-tower-screen | `Views/DashboardPage.xaml` + `Views/WorkspaceDecisionQueueControl.xaml` | Partial — dashboard/decision-queue overlap, not a triage tower |
| report-library-screen | `Views/WorkflowLibraryPage.xaml` + Reporting shell | Partial |
| report-run-parameters-screen | `Views/AnalysisExportWizardPage.xaml` + Reporting shell | Partial |
| finance-standard-pages-screen (evidence-detail stub retired per `W8-UX-CONSOL-001`; `/accounting/evidence/detail` redirects into the reporting evidence workbench) | `Views/AnalysisExportPage.xaml` + Reporting shell | Partial |
| strategy-designer-screen | `Views/QuantScriptPage.xaml` + `Views/BacktestPage.xaml` | Partial — authoring present, no designer canvas |
| operations-continuity-screen | `Views/OperationsContinuityPage.xaml` + `ViewModels/OperationsContinuityViewModel.cs` (Wave P3, delivered 2026-08-05) | Partial — workflow queue with detail (gates, blockers, checklist, next action), the unified open-item operator queue, and the promoted close-calendar and approval-policy cards ship read-only over the widened `IOperationsControlCenterClient`; approval/close/reopen commands and the dashboard, cockpit, timeline, and reviewed-automation panels remain browser-first |
| evidence-workbench-screen (canonical mount `/reporting/evidence` per `W8-UX-CONSOL-001`; former `/accounting/evidence` and `/data/evidence` mounts redirect there) | `Views/EvidenceWorkbenchPage.xaml` + `ViewModels/EvidenceWorkbenchViewModel.cs` (Wave P1, delivered 2026-08-05) | Partial — subjects, packet completeness, proof chain, lineage, vault request lists/documents, validate, and manifest export ship over the shared evidence endpoints; document intake and reviewer accept/reject remain browser-first |
| operator-readiness-console | `Views/OperatorReadinessConsolePage.xaml` + `ViewModels/OperatorReadinessConsoleViewModel.cs` (Wave P2, delivered 2026-08-05) | Partial — cross-lane gates, session/trust/promotion/run/break panels, and the prioritized operator work-item queue ship over the shared readiness, inbox, reconciliation, and run-summary seams; browser-only extras (per-endpoint API source strip, fund-account switcher, next-action hero) remain browser-first |
| operations-record-release-screen | `Views/OperationsRecordReleasePage.xaml` + `ViewModels/OperationsRecordReleaseViewModel.cs` (Wave P3, delivered 2026-08-05) | Partial — the six-step release path composes the continuity workflow's gates, accounting-record summary, and close package with the browser's tone-combination gating; the source-data step and reporting recipients/distribution columns remain browser-first |
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
3. **strategy-designer-screen** (borderline) — a visual strategy builder (cells, legs, payoff chart).
   WPF covers authoring via `QuantScriptPage`/`BacktestPage` but has no designer canvas.

Closed gaps: **evidence-workbench-screen** (Wave P1), **operator-readiness-console** (Wave P2), and **operations-continuity-screen** + **operations-record-release-screen** (Wave P3)
shipped 2026-08-05 — see the matrix rows above and the delivery notes under each wave.

## Closure Sequence

Each wave follows the established desktop contribution pattern (see
[`wpf-implementation-notes.md`](./wpf-implementation-notes.md) → *Contributing*): add the page + view
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

## Definition of Done for W8-WPF-PARITY-001

- Every browser workstation screen has a WPF equivalent page/view model or an explicitly sequenced
  wave item in this plan (matrix above is the tracker).
- The `EvidenceWorkbench` navigation target resolves to a real page (Wave P1 — delivered 2026-08-05).
- New parity surfaces consume shared contracts, read models, and workstation endpoints; no
  desktop-local product state, DTOs, or readiness rules are introduced.
- Desktop build and desktop-focused service tests pass on the authoritative CI gate.


## W10 shared-close sequencing receipt - 2026-09-04

Fund Ledger close headlines and the queue consume `FinancialOperationsCommandCenterDto.CloseReadiness`. Desktop requests reuse explicitly selected tenant/company/fund/book/account/entity/period context. Browser Accounting forwards the same business scope, including entity. Neither workstation may substitute a local ready calculation when the shared decision is missing. Fund-wide metrics remain diagnostic; scoped selection and recovery journeys require validation before W10-SEAM-001 acceptance.
