# WPF Desktop Application — Implementation Notes

**Version**: 1.7.x | **Last updated**: 2026-04-27 | **Status**: Authored / Included in solution build

## Overview

Meridian's WPF desktop application (`src/Meridian.Wpf/`) is the sole native Windows desktop surface for the platform. It exposes the full Meridian capability set through a workspace-based shell with a command palette, four canonical workspaces (Research, Trading, Data Operations, Governance), and deep drill-in pages for strategy runs, portfolios, and ledger governance.

## Architecture

### Stack

- **.NET 9.0 + WPF** — Windows-only, `.csproj` targets `net9.0-windows`
- **MVVM** — `BindableBase` (from `Meridian.Ui.Services.Services`) + `INotifyPropertyChanged`
- **DI** — `Microsoft.Extensions.Hosting`; singleton services resolved via `IServiceProvider`
- **Shared services** — `Meridian.Ui.Services` and `Meridian.Ui.Shared` for cross-surface logic

### Project references

| Project | Role |
| --- | --- |
| `Meridian.Wpf` | Views, code-behind, WPF-specific services |
| `Meridian.Wpf.Tests` | 106 unit tests for WPF-specific services and shell projections |
| `Meridian.Ui.Services` | Shared service layer (CommandPaletteService, WorkspaceService, NavigationServiceBase, etc.) |
| `Meridian.Ui.Tests` | 171 tests for shared UI services |
| `Meridian.Ui.Shared` | Endpoint helpers, DTO extensions, HTML template generator |
| `Meridian.Contracts` | Shared domain contracts, including `Workstation/StrategyRunReadModels.cs` |

---

## Shell Architecture

### MainPage — workspace selector + grouped shell navigation + command palette

```text
Left Sidebar (288 px)
├── Header: logo, app title, version badge
├── RESEARCH section  (7 items)
├── TRADING section   (5 items)
├── DATA OPS section  (6 items)
├── GOVERNANCE section(6 items)
└── Footer: Ctrl+K command palette button, Help button

Top Header Bar (74 px)
├── Left: Back button, breadcrumb page title + description
└── Right: Connection status badge, Refresh, Notifications

Shell Summary Rail
├── Workflow summary strip
└── Shared context strip with page title, subtitle, badges, and attention rail

Content Frame
├── Single app-level fixture/offline indicator plus shell status badge
└── WPF Frame → page navigation
```

**Workspace-aware navigation** — `ResolveWorkspaceIdForPage()` maps a page tag to its home workspace so that clicking a sidebar item or executing a command palette entry also activates the correct workspace session state. `WorkspacePrimaryNavList`, `WorkspaceSecondaryNavList`, `WorkspaceOverflowNavList`, and `RelatedWorkflowNavList` all dispatch through the same `NavigateToPageCommand` contract when the operator changes selection.

**Canonical sidebar buckets** — the shell now standardizes the left-rail group labels as `Home`, `Active Work`, `Review / Alerts`, and `Admin / Support`. The workspace selector tiles expose the same grouping model in their hover help so operators can see the shell structure before they switch workspaces.

**Welcome landing next-action panel** — `WelcomePage` now turns the system-overview snapshot into three readiness checks (provider session, symbol inventory, storage target), a readiness progress strip, and a primary next-step recommendation. Provider, symbol, storage, and freshness blockers route the operator back into Data Operations before the landing page suggests Research, Trading, or Governance shells.

**Add Provider catalog filtering** — `AddProviderWizardPage` now keeps provider catalog filtering, active-filter state, result-scope copy, empty-state recovery, connection feedback, save feedback, credential guidance, and setup-progress fills bound to `AddProviderWizardViewModel`. The page still owns navigation and dynamic credential-field control creation, but it no longer mutates provider card item sources or filter button styles directly.

**Shared context-strip attention rail** — `WorkspaceShellContextStripControl` now promotes the highest-priority `Warning` or `Danger` badge into a dedicated attention rail after the page title and wrapped badge row. The rail collapses when the shell context is healthy and prioritizes `Critical` / `Attention`, then `Environment`, `Freshness`, and `Alerts` so trust-state regressions do not get buried inside dense shell chrome.

**Main shell context strip** — `MainPage` renders the shared context strip for workflow pages and suppresses it on workspace landing pages where the compact next-action row already carries the first operator handoff. The shell publishes an immediate fallback context before the async `WorkspaceShellContextService` refresh completes, so page title/subtitle and warning badges stay visible even when the richer context composition is delayed or unavailable.

**Operator queue action** — `MainPage` now consumes `GET /api/workstation/operator/inbox` through `IWorkstationOperatorInboxApiClient`, includes the selected account operating context as `fundAccountId` when one is active, colors the shell queue action from the inbox tone, and routes the first actionable work item by route metadata before falling back to its target page tag. Known shared routes open concrete workbenches such as `FundReconciliation`, `SecurityMaster`, or `AccountPortfolio` for brokerage-sync blockers instead of stopping on a workspace landing page. Queue attention text includes review count, severity, owner/source, and the concrete target page so the context-strip warning is actionable. The Trading shell uses the same account context when it requests shared operator readiness, so brokerage-sync blockers and account-scoped readiness stay aligned between the queue and the cockpit. Its desk briefing hero also resolves readiness work-item routes to concrete workbenches before broad shell tags when a warning or critical item blocks a ready active-run state. If the backend queue is unavailable, the action falls back to `NotificationCenter` instead of inventing shell-local readiness state.

**Page header visibility refinement** — `MainPage` now keeps the current page title visible in the primary shell header instead of leaving the bound title/subtitle collapsed. Standard density shows both title and subtitle, while compact density keeps the title visible and collapses the subtitle so the context switcher and next-action strip stay above the fold.

**Persistent status bar pipeline pressure** — `StatusBarControl` now shows a compact queue-pressure label beside throughput. `StatusBarViewModel` formats queue size/capacity or normalized utilization from the existing `StatusResponse.Pipeline` snapshot, colors the label by utilization thresholds, and includes the same queue state in the status tooltip without adding another timer, service call, or buffer.

**Research desk briefing hero** — `ResearchWorkspaceShellPage` now keeps the current research cycle, blocker, and next handoff visible above the market briefing. The hero reuses existing workflow-summary and active-run state so empty queues route into `Backtest`, queued promotion candidates route into `StrategyRuns`, and promotable active runs expose trading-review plus direct promotion actions without introducing a separate fetch path.

**Strategy run browser filter recovery** — `StrategyRunsPage` now shows visible-vs-recorded run scope beside search and overlays a dynamic empty state when no rows are visible. `StrategyRunBrowserViewModel` distinguishes a truly empty run library from search or mode filters that hide retained rows, and its `ClearRunFiltersCommand` restores the in-memory run list without another run-store read.

**RunMat output readiness** — `RunMatPage` now keeps the script output panel actionable before and during runs. `RunMatViewModel` projects output-line count, idle/streaming/empty-output guidance, and `StopRunCommand` availability from the existing in-memory output collection and run state, so the output list gains recovery context without adding another timer, process read, or persistence write.

**Batch Backtest result readiness** — `BatchBacktestPage` now replaces the blank results grid with a bound empty-state panel when no sweep rows are available. `BatchBacktestViewModel` projects idle, validation-blocked, running-before-first-result, failed-without-results, cancelled, and populated result states from the existing batch status counters and summaries without adding service calls, timers, or persistence writes.

**QuantScript run-history handoff** — `QuantScriptPage` now renders the execution-history state already maintained by `QuantScriptViewModel` as a dedicated `Run History` tab. The tab shows recorded executions, selected-run evidence, console preview, and existing Strategy Runs handoff commands without adding a new history read, timer, or execution-path side effect.

**Run Cash Flow empty-state guidance** — `RunCashFlowPage` now hides blank ladder and event grids when no retained cash-flow rows are available. `CashFlowViewModel` projects selected-run, missing-run, no-event, and loaded states from the already-loaded run cash-flow summary so the drill-in gives operators clear next steps without adding another run-store read.

**Governance lane briefing card** — `GovernanceWorkspaceShellPage` now keeps the selected governance lane, blocker summary, and next handoff visible above the lane buttons. The hero state reuses the same fund-context, workflow-summary, reconciliation, reporting, and notification inputs already loaded for the shell, so lane switches update immediately without another service round-trip.

**Fund Accounts operator briefing** — `FundAccountsPage` now turns the static operator brief into a stateful fund-account handoff. `FundAccountsViewModel` projects fund-context, empty account queue, missing route evidence, blocked provider routes, shared-data access gaps, ready-for-reconciliation states, and balance-evidence snapshot posture from the already-loaded account queue, route previews, provider bindings, balance history, and `FundStructureSharedDataAccessDto` baseline without another service call.

**Account Portfolio position readiness** — `AccountPortfolioPage` now replaces the blank positions grid with view-model-owned guidance for no account context, first load, unavailable account snapshots, and accounts with no open positions. `AccountPortfolioViewModel` owns the empty-state copy, visibility, and refresh eligibility while reusing the existing account-detail endpoint and timer path.

**Aggregate Portfolio position readiness** — `AggregatePortfolioPage` now replaces the blank desk-level positions grid with view-model-owned guidance for first load, active refresh, unavailable aggregate endpoints, and loaded snapshots with no netted positions. `AggregatePortfolioViewModel` owns the empty-state copy and grid visibility while reusing the existing aggregate/exposure endpoints, refresh command, and polling timer.

**Portfolio Import action readiness** — `PortfolioImportPage` now binds file, index, and manual-entry actions to `PortfolioImportViewModel` commands instead of page click handlers. The view model owns import readiness copy, command enablement, and manual symbol counts so invalid file/manual actions are disabled before service calls start, while the view stays focused on layout and rendering.

**Advanced Analytics action readiness** — `AdvancedAnalyticsPage` now binds refresh, report generation, gap analysis, repair confirmation, provider comparison, and status dismissal to `AdvancedAnalyticsViewModel` commands. The view model owns comparison readiness copy and the inline repair-confirmation state from the already-loaded gap analysis result, so repair no longer depends on a page-level `MessageBox` and no new service calls, timers, or persistence writes are introduced.

**Analysis Export action readiness** — `AnalysisExportPage` now binds export launch and preset save actions to `AnalysisExportViewModel` commands instead of page click handlers. The view model owns required-field, metric-selection, symbol-scope, date-scope, and recent-export presentation state so invalid exports are disabled with inline guidance before an operator queues work, without adding service calls, timers, or persistence writes.

**Analysis Export Wizard readiness** — `AnalysisExportWizardPage` now binds add-symbol, back, next/queue, and cancel actions to `AnalysisExportWizardViewModel` commands instead of page click handlers. The view model owns step title/detail copy, scope text, validation visibility, and action enablement for symbol, date, destination, metric, and pre-export checks without adding service calls, timers, or persistence writes.

**Export Presets readiness** — `ExportPresetsPage` now binds save/delete actions to `ExportPresetsViewModel` commands instead of page click handlers. The view model owns preset-library state, empty-state copy, save-readiness title/detail text, status visibility, and built-in preset delete gating so reporting operators see whether a preset can be saved or removed before acting, without adding service calls, timers, or persistence writes.

**Data Export quick-export and schedule readiness** — `DataExportPage` now shows compact readiness strips before the Quick Export and Scheduled Exports controls. `DataExportViewModel` owns export command enablement, selected-symbol count, date-scope copy, format/compression guidance, schedule toggle state, schedule destination validation, schedule action enablement, validation recovery, and progress state so invalid quick exports or scheduled export setups are disabled before an operator queues work, without adding service calls, timers, or persistence writes.

**Data Sampling readiness** — `DataSamplingPage` now binds symbol add, sample generation, and preset save actions to `DataSamplingViewModel` commands instead of page click handlers. The view model owns sample readiness title/detail text, symbol-scope copy, validation state, command enablement, and retained recent-sample state, so invalid sample requests are explained before launch without adding service calls, timers, or persistence writes.

**Time Series Alignment action readiness** — `TimeSeriesAlignmentPage` now binds alignment setup, symbol chips, preset application, validation, run progress, results, and recent alignment history through `TimeSeriesAlignmentViewModel`. The view model owns command enablement and inline readiness copy for missing symbols, dates, or selected fields, then maps the bound setup into the existing `TimeSeriesAlignmentService` request without adding backend behavior, polling, or persistence writes.

**Symbols filter recovery** — `SymbolsPage` now binds search text, subscription scope, exchange scope, visible-row copy, filter-aware empty-state copy, and the `Clear Filters` action through `SymbolsPageViewModel`. Bulk action buttons bind to the view-model-owned selection state, so filtered-out lists and selected rows recover without another backend read or page-owned filter logic.

**Fund Ledger reconciliation filter recovery** — the reconciliation workbench inside `FundLedgerPage` now exposes a bound `Reset Filters` action beside queue refresh. `FundLedgerViewModel` tracks active break-queue, scope, and local-search filters, restores the already-loaded open queue without another service read, and updates the empty-state copy when filters hide retained break rows.

**Fund Ledger reconciliation calibration posture** — `FundReconciliationWorkbenchService` now loads `GET /api/workstation/reconciliation/calibration-summary` through `IWorkstationReconciliationApiClient` while it loads the break queue. `FundLedgerViewModel` projects the returned status, pending sign-off count, missing metadata count, and tolerance-profile rollups into `FundLedgerPage` so governance operators can see calibration blockers before resolving or signing off reconciliation work. Use the focused `FundLedgerViewModelTests` and `FundReconciliationWorkbenchServiceTests` filters when changing this surface.

**Trading desk briefing hero** — `TradingWorkspaceShellViewModel` now owns the Trading shell presentation state, with `TradingWorkspaceShellPresentationService` composing active-run, workflow-summary, fund/account context, capital, and shared operator-readiness inputs into bound UI state. `TradingWorkspaceShellPage` is limited to WPF lifecycle, tone resources, dock hosting, and navigation forwarding. The hero and validation card still cover context-required, replay-mismatch, controls-blocked, paper-review, live-oversight, DK1 sign-off, and account-scoped brokerage-sync states without adding backend behavior or changing route tags.

**Trading Hours session briefing** — `TradingHoursPage` now turns the current market calendar state into a compact desk handoff beside the exchange status card. `TradingHoursViewModel` projects live-risk, pre-market staging, after-hours review, and closed-planning copy from the existing calendar status response or local fallback calculation, and the holiday card shows explicit missing-calendar or unavailable-calendar guidance instead of a blank table, without adding a service call, timer, or persistence write.

**Order Book order-flow posture** — `OrderBookPage` now places a compact posture strip between the page hero and depth ladder. `OrderBookViewModel` owns the selected symbol, selected depth level, depth-level options, and posture projection so the header selectors bind directly to VM state while the already-loaded ladder and recent-trade window continue to drive spread, cumulative-delta, tape-readiness, and bid/ask pressure copy without adding another polling path or changing the allocation-sensitive heatmap render loop.

**Position Blotter empty-state reset** — `PositionBlotterPage` now replaces a blank grid with a focused empty-state card when no rows are displayed. Filter/search misses explain that hidden rows can be restored with `Reset Filters`, while truly empty snapshots keep reset disabled and leave `Refresh` as the recovery action.

**Provider Health posture briefing** — `ProviderHealthPage` now places a compact briefing card ahead of the streaming and backfill provider grids. `ProviderHealthViewModel` projects stale snapshots, offline streaming sessions, mixed provider states, blocked backfill coverage, and ready posture from already-loaded provider counts so the page exposes one next handoff, a target surface, and automation IDs without adding another polling path.

**System Health triage briefing** — `SystemHealthPage` now places a compact system triage card between the hero and resource metrics. `SystemHealthViewModel` projects provider posture, storage pressure, corrupted/orphaned storage evidence, and retained event severity into one next handoff with provider, storage, and event chips, reusing the page's existing health snapshots without adding a backend call. Provider and recent-event empty states now distinguish a pending first scan from a completed empty snapshot, so support operators do not read "no providers" or "no events" as confirmed before the page has loaded that section.

**Notification Center history recovery** — `NotificationCenterPage` now exposes a `Reset Filters` action when search, unread-only, or severity filters hide retained notification history. `NotificationCenterViewModel` resets the history filters against the already-loaded notification window, owns the `All` severity toggle, and keeps the severity checkboxes two-way bound so the visible filter controls match the recovered history scope without a new service read or code-behind synchronization.

**Activity Log triage strip** — `ActivityLogPage` now keeps retained error count, warning count, visible entry count, latest entry time, and active filter scope above the virtualized list. `ActivityLogViewModel` projects this from the retained in-memory log window and owns the level, category, and search filter bindings; when filters hide retained entries, the empty state exposes a bound `Reset Filters` action that restores the in-memory activity window without another backend request or code-behind synchronization. The header actions now follow the same retained-window state: `Export` disables when no rows are visible, and `Clear` disables until there is retained activity to remove.

**Messaging Hub delivery posture** — `MessagingHubPage` now promotes message-flow state into a compact delivery posture panel with recent-activity scope text and an automation-addressable empty state. `MessagingHubViewModel` derives waiting, subscriber-ready, flowing, and failure-review copy from the page's existing session counters and subscription counts, keeps only the latest 50 activity rows, and exposes bound refresh and clear commands so the header shows the last statistics refresh while the Clear button disables itself when there is no retained activity to clear.

**Agent local-AI readiness** — `AgentPage` now surfaces a bound readiness card, model-scope chip, empty-conversation state, and input guidance before an operator sends a prompt. `AgentViewModel` owns Ollama availability, installed-model state, selected-model readiness, send/clear command enablement, and conversation empty-state copy, so missing local runtime or missing-model setup is explained without page workflow logic.

**Watchlist posture card** — `WatchlistPage` now summarizes saved watchlists, pinned lists, symbol coverage, current search scope, and the next operator action above the grid. `WatchlistViewModel` projects this from already-loaded local watchlist display models, so search misses and unpinned libraries get actionable copy without another service call; the search-miss empty state also exposes a bound `Clear Search` recovery action that resets the in-memory filter.

**Data Quality symbol search recovery** — `DataQualityPage` now distinguishes an empty monitored-symbol library from a symbol-filter search miss. The `Quality by Symbol` panel shows the active filter scope, search-miss copy, and a `Clear Filter` recovery action backed by `DataQualityViewModel` in-memory filtering, so operators can recover without refreshing or leaving the page.

**Data Browser filter recovery** — `DataBrowserPage` now refreshes its retained market-data window as search, data-type, venue, time-period, and date filters change. `DataBrowserViewModel` owns the time-period options, selected period, derived date range, scope copy, and `Reset Filters` command, so operators can scope the retained rows from the toolbar and recover hidden rows without leaving the page or issuing another backend request.

**Storage preview scope strip** — `StoragePage` now annotates the live file-structure preview with a bound scope strip that shows the selected root, naming convention, and compression mode. `StorageViewModel.RefreshPreview()` normalizes relative Windows and POSIX-style preview roots, updates the sample tree, and exposes automation IDs for the preview scope, guidance, tree, estimate, and selector controls without adding another storage scan or persistence write.

**Storage archive posture** — `StoragePage` now places an archive-posture card above the configuration and preview panels. `StorageViewModel` projects daily growth, capacity horizon, last scan, and one operator handoff from the already-loaded `StorageAnalytics` snapshot, so capacity pressure and stalled archive growth are visible without adding another storage scan, timer, or persistence write.

**Data Operations next-handoff card** — `DataOperationsWorkspaceShellPage` now turns the previously static right-side hero card into a priority handoff surface. Provider outages, storage blockers, resumable backfills, active exports, collection sessions, and steady-state readiness each project one explicit CTA with a target label, while the same hero shows compact provider, backfill, and storage health chips so operators can confirm the readiness posture before scanning the full workbench.

**Backfill start readiness** — `BackfillPage` now shows an automation-addressable start-readiness card above the run controls. `BackfillViewModel` owns symbol normalization, date-range validation, request scope text, validation label visibility, and Start enablement so empty-symbol and invalid-date states are visible before an operator launches a historical backfill; the page code-behind only refreshes the VM from existing WPF controls and delegates the launch to the existing backfill command path.

**Schedule Manager MVVM state** — `ScheduleManagerPage` now binds backfill, maintenance, template refresh, empty/error copy, and cron validation through `ScheduleManagerViewModel`. The page code-behind only wires construction and first-load initialization, while the view model owns command enablement and UTC next-run presentation without adding backend calls, timers, or persistence writes.

**Admin Maintenance cleanup readiness** — `AdminMaintenancePage` now renders a cleanup readiness card with preview, execution, and confirmation actions bound through `AdminMaintenanceViewModel`. The view model owns preview scope, empty/error copy, destructive-action gating, inline confirmation state, and cleanup execution reset behavior while the page keeps cleanup rendering and layout concerns in XAML.

**Security Master runtime fallback** — `SecurityMasterViewModel.SearchAsync()` now checks `ISecurityMasterRuntimeStatus.IsAvailable` before issuing workstation search calls so an unconfigured desktop shows the runtime guidance text instead of a misleading zero-results message.

**Security Master search recovery** — `SecurityMasterPage` now exposes a bound `SearchCommand` for the search button and `Enter` key plus a bound `Clear Search` action in the search strip and in the results empty-state card. `SecurityMasterViewModel` tracks attempted searches, unavailable-runtime recovery, result count, query scope, and search-command availability locally so operators can run or reset a search without code-behind workflow logic.

**Security Master conflict operator lane** — the workstation conflict queue now groups open mismatches by security, scores severity and auto-resolve confidence from the selected field mismatch, and turns fund-review, reconciliation, cash-flow, and report-pack jumps on only when the active conflict actually affects those downstream workflows.

**Security Master trust posture** — detail refresh now loads economic-definition provenance, latest history actor/timestamp, and trading-parameter coverage so the selected security surface can answer whether the instrument definition is ready for downstream portfolio, ledger, reconciliation, and reporting use.

**Security Master trust workbench** — the selected-security workspace is now organized as `Overview`, `Identity & Provenance`, `History`, `Conflict Queue`, and `Corporate Actions`. The overview surface adds trust summary, downstream impact, and recommended-action cards; the conflict queue adds selected-security filters plus low-risk bulk assist; the right rail now exposes `Trust Posture`, `Selected Security`, and `Next Best Actions`.

**Security Master trust workflow client** — `SecurityMasterViewModel` now consumes `SecurityMasterTrustSnapshotDto` and bulk conflict-resolution results through the typed workstation API client, keeps dispatcher hops explicit as `System.Windows.Application`, routes downstream fund-operations jumps with the active fund profile attached, and leaves global ingest polling independent from the selected-security trust snapshot refresh path.

**Selection suppression** — `_suppressNavSelection` prevents feedback loops when the NavigationService drives sidebar selection changes programmatically.

### Workspace system (`WorkspaceService`, `WorkspacePage`)

Four built-in workspace templates:

| Workspace ID | Pages |
| --- | --- |
| `research` | Dashboard, LiveData, Charts, RunMat, StrategyRuns, OrderBook, Watchlist |
| `trading` | Backtest, StrategyRuns, LeanIntegration, PortfolioImport, TradingHours |
| `data-operations` | Provider, ProviderHealth, Symbols, Backfill, Storage, DataExport, PackageManager, Schedules |
| `governance` | DataQuality, SystemHealth, Diagnostics, Settings, AdminMaintenance |

Each workspace persists:

- `ActivePageTag` — last active page within the workspace
- `OpenPages` — MRU list (max 8)
- `WidgetLayout`, `ActiveFilters`, `WorkspaceContext` — per-workspace state
- `WindowBounds` — restored on session resume

### Command Palette (`CommandPaletteService`, `Ctrl+K`)

- **~55 navigation commands** — one per registered page, each labelled with its workspace context
- **8 action commands** — Start/Stop collector, Run backfill, Refresh, Add symbol, Toggle theme, Save, Search
- **Fuzzy search** — exact → prefix → contains → character-sequence fuzzy, top-15 results
- **Recency tracking** — LRU 10, shown when palette opens empty
- **In-input result traversal** — when the shell palette is open, `Up` and `Down` move the selected result without leaving the search box; `Enter` opens the selection and `Esc` closes the overlay
- **Workspace labels** in descriptions: `Research workspace — Dashboard`, `Trading workspace — LiveData`, etc.

---

## Page Registry

Catalog-backed desktop pages now live in the `ShellNavigationCatalog` partials under
`src/Meridian.Wpf/Models/`. `NavigationService.RegisterAllPages()` and the WPF DI setup both loop
over that catalog, so adding a new shell page typically requires:

1. the page and its view model
2. one `ShellNavigationCatalog` entry
3. optionally, a workspace default-pane entry if the page should open in a shell layout

Only non-catalog utility pages still need direct manual registration in `App.xaml.cs`.

### Research workspace pages

| Tag | Class | Notes |
| --- | --- | --- |
| `Dashboard` | `DashboardPage` | Default landing page |
| `Watchlist` | `WatchlistPage` | |
| `RunMat` | `RunMatPage` | Quant / script lab |
| `Charts` | `ChartingPage` | Candlestick / time-series |
| `OrderBook` | `OrderBookPage` | Live L2 depth |
| `StrategyRuns` | `StrategyRunsPage` | Strategy run browser |
| `RunDetail` | `RunDetailPage` | Run execution summary drill-in |
| `AdvancedAnalytics` | `AdvancedAnalyticsPage` | |

### Trading workspace pages

| Tag | Class | Notes |
| --- | --- | --- |
| `Backtest` | `BacktestPage` | |
| `LiveData` | `LiveDataViewerPage` | Real-time feed viewer |
| `RunPortfolio` | `RunPortfolioPage` | Positions, exposure, P&L drill-in |
| `LeanIntegration` | `LeanIntegrationPage` | QuantConnect Lean engine |
| `PortfolioImport` | `PortfolioImportPage` | CSV/bulk import |
| `TradingHours` | `TradingHoursPage` | Market calendar |

### Data Operations workspace pages

| Tag | Class |
| --- | --- |
| `Provider` | `ProviderPage` |
| `ProviderHealth` | `ProviderHealthPage` |
| `DataSources` | `DataSourcesPage` |
| `Symbols` | `SymbolsPage` |
| `SymbolMapping` | `SymbolMappingPage` |
| `SymbolStorage` | `SymbolStoragePage` |
| `IndexSubscription` | `IndexSubscriptionPage` |
| `Options` | `OptionsPage` |
| `Backfill` | `BackfillPage` |
| `Storage` | `StoragePage` |
| `DataBrowser` | `DataBrowserPage` |
| `DataCalendar` | `DataCalendarPage` |
| `DataExport` | `DataExportPage` |
| `DataSampling` | `DataSamplingPage` |
| `TimeSeriesAlignment` | `TimeSeriesAlignmentPage` |
| `AnalysisExport` | `AnalysisExportPage` |
| `AnalysisExportWizard` | `AnalysisExportWizardPage` |
| `ExportPresets` | `ExportPresetsPage` |
| `EventReplay` | `EventReplayPage` |
| `PackageManager` | `PackageManagerPage` |
| `Schedules` | `ScheduleManagerPage` |

### Governance workspace pages

| Tag | Class |
| --- | --- |
| `DataQuality` | `DataQualityPage` |
| `CollectionSessions` | `CollectionSessionPage` |
| `ArchiveHealth` | `ArchiveHealthPage` |
| `ServiceManager` | `ServiceManagerPage` |
| `SystemHealth` | `SystemHealthPage` |
| `Diagnostics` | `DiagnosticsPage` |
| `StorageOptimization` | `StorageOptimizationPage` |
| `RetentionAssurance` | `RetentionAssurancePage` |
| `AdminMaintenance` | `AdminMaintenancePage` |
| `RunLedger` | `RunLedgerPage` |
| `ActivityLog` | `ActivityLogPage` |
| `MessagingHub` | `MessagingHubPage` |
| `NotificationCenter` | `NotificationCenterPage` |

### Support / cross-workspace pages

| Tag | Class |
| --- | --- |
| `Help` | `HelpPage` |
| `Welcome` | `WelcomePage` |
| `Settings` | `SettingsPage` |
| `KeyboardShortcuts` | `KeyboardShortcutsPage` |
| `SetupWizard` | `SetupWizardPage` |
| `AddProviderWizard` | `AddProviderWizardPage` |
| `Workspaces` | `WorkspacePage` |

---

## Shared Workstation Contracts

All workstation-facing read models live in `src/Meridian.Contracts/Workstation/`.

### `StrategyRunReadModels.cs`

```text
StrategyRunMode           — Backtest | Paper | Live
StrategyRunEngine         — MeridianNative | Lean | BrokerPaper | BrokerLive
StrategyRunStatus         — Pending | Running | Paused | Completed | Failed | Cancelled | Stopped
StrategyRunPromotionState — None → CandidateForPaper → CandidateForLive → LiveManaged

StrategyRunSummary        — summary row for browser / recent-run list
StrategyRunDetail         — expanded detail; embeds Portfolio + Ledger summaries
StrategyRunExecutionSummary — fills, commissions, margin, audit flags
StrategyRunPromotionSummary — promotion state + reasoning
StrategyRunGovernanceSummary — parameter/portfolio/ledger/audit coverage flags
StrategyRunComparison     — side-by-side multi-run comparison row (Sharpe, drawdown, XIRR)

PortfolioSummary          — equity, cash, gross/net exposure, realized/unrealized P&L, positions
PortfolioPositionSummary  — per-symbol: quantity, cost-basis, P&L, security ref
LedgerSummary             — asset/liability/equity/revenue/expense balances, trial-balance, journal
LedgerTrialBalanceLine    — account row with symbol and security resolution
LedgerJournalLine         — journal entry row (debits, credits, description)
WorkstationSecurityReference — lightweight Security Master ref used by portfolio + ledger surfaces
```

### `ReconciliationDtos.cs`

```text
ReconciliationRunRequest  — RunId, tolerance thresholds
ReconciliationBreakDto    — check ID, category, status, variance, source metadata
ReconciliationBreakCategory — AmountMismatch | MissingLedgerCoverage | MissingPortfolioCoverage | ...
```

### `SecurityMasterTrustWorkbenchDtos.cs`

```text
SecurityMasterTrustSnapshotDto            — selected-security workbench snapshot
SecurityMasterEconomicDefinitionDrillInDto — typed winning-source provenance fields
SecurityMasterTrustPostureDto            — trust tone, summary, conflict/trading-parameter posture
SecurityMasterSourceCandidateDto         — winning source plus challenger candidates
SecurityMasterConflictAssessmentDto      — preserve-winner / challenger / dismiss-equivalent recommendations
SecurityMasterDownstreamImpactDto        — workflow-summary portfolio / ledger / reconciliation / report-pack impact
SecurityMasterRecommendedActionDto       — ordered next-best operator actions
BulkResolveSecurityMasterConflictsRequest / Result — low-risk bulk-assist contract
```

Shared workstation routes now include:

- `GET /api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId={optional}`
- `POST /api/workstation/security-master/conflicts/bulk-resolve`

---

## Strategy Run Workstation — ViewModel Layer

| ViewModel | Key properties | Commands |
| --- | --- | --- |
| `StrategyRunBrowserViewModel` | `Runs`, `SearchText`, `SelectedModeFilter`, `RunScopeText`, `EmptyStateTitle`, `EmptyStateDetail`, `CanChooseComparisonRun`, `ComparisonGuidanceText` | `RefreshCommand`, `OpenDetailCommand`, `OpenPortfolioCommand`, `OpenLedgerCommand`, `CompareRunsCommand`, `ClearComparisonCommand`, `ClearRunFiltersCommand` |
| `StrategyRunDetailViewModel` | Execution summary, mode, timing, P&L, parameters | Cross-nav to Browser / Portfolio / Ledger |
| `StrategyRunPortfolioViewModel` | `TotalEquity`, `Cash`, exposure, `Positions` | Security Master resolve count |
| `StrategyRunLedgerViewModel` | `TrialBalance`, `Journal`, account balances | Security resolve count |

Parameter passing follows the standard MVVM drill-in pattern: `NavigationService.NavigateTo(tag, runId)` → `page.DataContext.Parameter = runId` → `LoadFromParameterAsync()`.

`StrategyRunsPage` keeps the comparison picker visible but now gates it from the already-loaded run list and explains the next step when there is no primary run, only one visible run, the same run is selected twice, or a distinct comparison is ready.

---

## Services Layer

### WPF-specific services (`Meridian.Wpf.Services`)

| Service | Responsibility |
| --- | --- |
| `NavigationService` | Frame-based navigation; 50+ pages; history, breadcrumb, onboarding tour hooks |
| `ConnectionService` | Provider connection state; latency tracking |
| `ConfigService` | App configuration with `ConfigPath` |
| `ThemeService` | Dark/Light; persisted; Windows accent integration |
| `NotificationService` | Toast notifications and alert routing |
| `LoggingService` | Structured log sink |
| `KeyboardShortcutService` | 20+ global shortcuts |
| `MessagingService` | Inter-component messaging bus |
| `FirstRunService` | Setup wizard gating |
| `WorkspaceService` | Workspace + session state persistence (re-export of shared base) |
| `BackgroundTaskSchedulerService` | Scheduled background execution |
| `OfflineTrackingPersistenceService` | Offline mode data tracking |
| `PendingOperationsQueueService` | Offline operation queue |

### Shared services (from `Meridian.Ui.Services`)

`CommandPaletteService`, `NavigationServiceBase`, `WorkspaceService` base, `SearchService`, `FixtureModeDetector`, `OnboardingTourService`, `ActivityFeedService`, `AlertService`, `DataQualityPresentationService`, and 40+ additional services used by both the WPF app and the web dashboard.

---

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+K` | Open command palette |
| `Ctrl+D` | Dashboard |
| `Ctrl+B` | Backfill |
| `Ctrl+W` | Watchlist |
| `Ctrl+Y` | Symbols |
| `Ctrl+Q` | Data Quality |
| `Ctrl+0` | Settings |
| `Ctrl+Shift+B` | Backtest |
| `Ctrl+Shift+S` | Start collector |
| `Ctrl+Shift+Q` | Stop collector |
| `Ctrl+Shift+T` | Toggle theme |
| `Ctrl+R` | Run backfill |
| `Ctrl+N` | Add symbol |
| `Ctrl+F` | Search symbols |
| `Ctrl+S` | Save |
| `F1` | Help |
| `F5` | Refresh |

---

## XAML Style System

Style resources in `Meridian.Wpf/Styles/`:

| File | Contains |
| --- | --- |
| `ThemeTokens.xaml` | Semantic color tokens (`ConsoleTextPrimaryBrush`, `InfoColorBrush`, etc.) |
| `ThemeSurfaces.xaml` | Surface-level brushes (`ShellWindowBackgroundBrush`, `ShellRailBackgroundBrush`) |
| `ThemeControls.xaml` | Control styles (`NavItemStyle`, `CardStyle`, `PrimaryButtonStyle`, etc.) |
| `ThemeTypography.xaml` | Text styles (`PageTitleStyle`, `CardHeaderStyle`, `CardDescriptionStyle`) |
| `AppStyles.xaml` | Root merge dictionary |
| `Animations.xaml` | Shared transition animations |
| `IconResources.xaml` | Segoe Fluent Icons aliases |

---

## Workspace Shells

The WPF shell now projects seven root workspace capabilities: Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings. Strategy and Trading shells keep presentation state in view models backed by WPF-scoped presentation services; their pages handle WPF lifecycle, docking, tone resources, and navigation forwarding. Data uses a service-backed projection layer that folds provider, backfill, storage, session, notification, and export-job telemetry into a single operator shell.

Shell implementation now shares descriptor-driven infrastructure:

- `WorkspaceShellPageBase<TStateProvider, TViewModel>` owns dock restore/save, fallback content, and pane opening
- `WorkspaceShellViewModelBase` carries shell command state
- `IWorkspaceShellStateProvider` and `WorkspaceShellState` translate active run, operating-context, and preset state into declarative default panes
- `ShellNavigationCatalog.Workspaces.cs` is the source of truth for default panes and preset layouts across `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`

### `ResearchWorkspaceShellPage` (`Views/ResearchWorkspaceShellPage.xaml`)

**Purpose**: Single-page landing for the Strategy workspace. Shows the current strategy-cycle handoff, recent strategy runs, performance at a glance, and quick-links to Backtest, RunMat, Charts, and the run browser. `ResearchShell` remains a hidden compatibility alias for this page.

`ResearchWorkspaceShellViewModel` owns the shell's loading/error state, KPI text, desk-briefing hero, workflow blocker/evidence, active-run summary, briefing collections, command group, and action requests. `ResearchWorkspaceShellPresentationService` composes the existing run workspace, research briefing, watchlist, fund/operating context, shell context, workflow summary, and optional promotion services into immutable UI state without adding backend behavior. `ResearchWorkspaceShellPage.xaml.cs` stays limited to lifecycle, AvalonDock pane hosting, tone resource application, and forwarding view-model action requests to `NavigationService` or `OpenWorkspacePage`.

**Design zones**:

1. **Header** — Active strategy count, cumulative P&L across completed runs, last-run timestamp
2. **Desk briefing hero** — Current cycle focus, blocker, and next handoff projected from workflow + active-run state
3. **Market Briefing** — Pinned insights, watchlists, change feed, and saved comparisons
4. **Run Studio + Recent Runs** — Run context, inspector guidance, and the run-history rail
5. **Promotion Pipeline** — Candidates for paper promotion (sourced from `StrategyRunPromotionState`)

### `TradingWorkspaceShellPage` (`Views/TradingWorkspaceShellPage.xaml`)

**Purpose**: Single-page landing for the Trading workspace. Shows live execution state, active paper/live positions, and a compact promotion/audit/validation status card for the active run or aggregate workspace posture.

**Design zones**:

1. **Header** — Active fund context and active run summary
2. **Desk briefing hero** — Current desk focus, readiness tone, and one next handoff projected from workflow + operator-readiness state
3. **Workbench rail** — Active positions plus capital and risk inspector cards for the current trading posture
4. **KPI strip and support lanes** — Active paper/live run counts, total equity, and supporting market-core / audit quick actions

Replay and collection-session review stay on their owning Data Operations and Governance pages until the trading shell has a proven need for another deep-review lane.

### `DataOperationsWorkspaceShellPage` (`Views/DataOperationsWorkspaceShellPage.xaml`)

**Purpose**: Operational cockpit for provider readiness, backfill pressure, storage posture, collection sessions, and export delivery.

**Data composition**:

1. `DataOperationsWorkspaceShellPage.xaml.cs` loads provider catalog/status, backfill health, resumable checkpoints, execution history, schedules, storage stats/health, active and recent collection sessions, persisted export jobs, and notification history.
2. `DataOperationsWorkspacePresentationBuilder` converts those service responses into shell context badges, a next-handoff hero card, queue cards, summary values, recent operations, and quick-action wiring.
3. Primary actions route directly to `ProviderHealth`, `Backfill`, and `DataExport`; secondary actions keep `Providers`, `Storage`, `CollectionSessions`, `Schedules`, and `PackageManager` in the same shell flow.

**Design zones**:

1. **Context strip** — Scope, freshness, backfill review state, and critical blockers derived from live operational state instead of static labels.
2. **Next-handoff card** — One priority CTA selected from provider, storage, backfill, export, or active-session posture, with a target label and optional secondary action.
3. **Queue boards** — Provider health, backfill queue/session state, storage posture, and export job visibility.
4. **Recent operations rail** — Latest session, resumable backfill, export run, or alert-linked notification with a deep link back into the owning page.
5. **Support surfaces** — Fast open buttons for Providers, Backfill, Symbols, Storage, Collection Sessions, Data Export, Schedules, and Package Manager.

---

## Build

```bash
# Standalone WPF build (Windows or cross-platform with Windows targeting)
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableWindowsTargeting=true -c Release

# WPF + shared UI services tests
dotnet test tests/Meridian.Wpf.Tests /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet test tests/Meridian.Ui.Tests /p:EnableWindowsTargeting=true
```

### Common errors

| Error | Fix |
| --- | --- |
| NETSDK1100 | Add `/p:EnableWindowsTargeting=true` on non-Windows hosts |
| `NU1008` | Remove `Version="..."` from any `<PackageReference>` — versions live in `Directory.Packages.props` |
| Page not found at runtime | Ensure the page has a `ShellNavigationCatalog` entry with the correct `PageType`, and that `AddMeridianWpfShell()` is active in `App.ConfigureServices()` |

---

## Testing

| Test project | Count | Covers |
| --- | --- | --- |
| `Meridian.Wpf.Tests` | 106 | WPF-specific services and shell projections: Navigation, Config, Connection, InfoBar, Keyboard, RunMat, Data Operations shell projection, operator inbox action, etc. |
| `Meridian.Ui.Tests` | 171 | Shared services: ApiClient, Backfill, Charting, Watchlist, DataQuality, StrategyRun drill-ins |

Run with:

```bash
make desktop-test
```

---

## Contributing

1. **Register new shell pages** by adding one `ShellNavigationCatalog` entry; add a workspace pane definition only if the page belongs in a default dock layout
2. **Add command palette entry** in `CommandPaletteService.RegisterDefaultCommands()` — include workspace label in the `pageTag` argument so `BuildNavigationDescription` resolves correctly
3. **Follow MVVM patterns** — all data logic in ViewModels; code-behind restricted to UI event wiring and shell-specific visual concerns
4. **Event cleanup** — always unsubscribe in `OnPageUnloaded` / `OnNavigatedFrom`
5. **Use shared contracts** — workstation read models live in `Meridian.Contracts.Workstation`; never duplicate DTO types in the WPF project

---

## Related Documentation

- [`docs/architecture/desktop-layers.md`](../architecture/desktop-layers.md) — Layer boundaries
- [`docs/development/desktop-testing-guide.md`](./desktop-testing-guide.md) — Testing procedures
- [`docs/evaluations/desktop-platform-improvements-implementation-guide.md`](../evaluations/desktop-platform-improvements-implementation-guide.md) — Platform improvement roadmap and implementation reference
- [`docs/development/ui-fixture-mode-guide.md`](./ui-fixture-mode-guide.md) — Offline / fixture mode development
- [`docs/status/ROADMAP.md`](../status/ROADMAP.md) — Desktop items in the project roadmap
- [`docs/development/policies/desktop-support-policy.md`](./policies/desktop-support-policy.md) — Contribution requirements
