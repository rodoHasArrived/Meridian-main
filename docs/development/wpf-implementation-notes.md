# WPF Desktop Application — Implementation Notes

**Version**: 1.7.x | **Last updated**: 2026-04-20 | **Status**: Authored / Included in solution build

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
| `Meridian.Wpf.Tests` | 104 unit tests for WPF-specific services and shell projections |
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
├── Fixture Mode Banner (orange, conditional on non-live mode)
└── WPF Frame → page navigation
```

**Workspace-aware navigation** — `ResolveWorkspaceIdForPage()` maps a page tag to its home workspace so that clicking a sidebar item or executing a command palette entry also activates the correct workspace session state. `WorkspacePrimaryNavList`, `WorkspaceSecondaryNavList`, `WorkspaceOverflowNavList`, and `RelatedWorkflowNavList` all dispatch through the same `NavigateToPageCommand` contract when the operator changes selection.

**Canonical sidebar buckets** — the shell now standardizes the left-rail group labels as `Home`, `Active Work`, `Review / Alerts`, and `Admin / Support`. The workspace selector tiles expose the same grouping model in their hover help so operators can see the shell structure before they switch workspaces.

**Welcome landing next-action panel** — `WelcomePage` now turns the system-overview snapshot into three readiness checks (provider session, symbol inventory, storage target) plus a primary next-step recommendation. Provider, symbol, storage, and freshness blockers route the operator back into Data Operations before the landing page suggests Research, Trading, or Governance shells.

**Shared context-strip attention rail** — `WorkspaceShellContextStripControl` now promotes the highest-priority `Warning` or `Danger` badge into a dedicated second-row attention rail before the rest of the badge wall. The rail collapses when the shell context is healthy and prioritizes `Critical` / `Attention`, then `Environment`, `Freshness`, and `Alerts` so trust-state regressions do not get buried inside dense shell chrome.

**Main shell context strip** — `MainPage` now renders the shared context strip between the workflow summary rail and the split-pane host. The shell publishes an immediate fallback context before the async `WorkspaceShellContextService` refresh completes, so page title/subtitle and warning badges stay visible even when the richer context composition is delayed or unavailable.

**Page header visibility refinement** — `MainPage` now keeps the current page title visible in the primary shell header instead of leaving the bound title/subtitle collapsed. Standard density shows both title and subtitle, while compact density keeps the title visible and collapses the subtitle so the context switcher and next-action strip stay above the fold.

**Research desk briefing hero** — `ResearchWorkspaceShellPage` now keeps the current research cycle, blocker, and next handoff visible above the market briefing. The hero reuses existing workflow-summary and active-run state so empty queues route into `Backtest`, queued promotion candidates route into `StrategyRuns`, and promotable active runs expose trading-review plus direct promotion actions without introducing a separate fetch path.

**Governance lane briefing card** — `GovernanceWorkspaceShellPage` now keeps the selected governance lane, blocker summary, and next handoff visible above the lane buttons. The hero state reuses the same fund-context, workflow-summary, reconciliation, reporting, and notification inputs already loaded for the shell, so lane switches update immediately without another service round-trip.

**Trading desk briefing hero** — `TradingWorkspaceShellPage` now keeps the current desk focus, readiness tone, and next handoff visible above the workbench. The hero state reuses the existing active-run, workflow-summary, and shared operator-readiness inputs so context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight states update without adding another service fetch path.

**Data Operations next-handoff card** — `DataOperationsWorkspaceShellPage` now turns the previously static right-side hero card into a priority handoff surface. Provider outages, storage blockers, resumable backfills, active exports, collection sessions, and steady-state readiness each project one explicit CTA with a target label, so operators can move straight into the next queue action without scanning the full workbench first.

**Security Master runtime fallback** — `SecurityMasterViewModel.SearchAsync()` now checks `ISecurityMasterRuntimeStatus.IsAvailable` before issuing workstation search calls so an unconfigured desktop shows the runtime guidance text instead of a misleading zero-results message.

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
| `data-operations` | Provider, Symbols, Backfill, Storage, DataExport, PackageManager, Schedules |
| `governance` | DataQuality, ProviderHealth, SystemHealth, Diagnostics, Settings, AdminMaintenance |

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
| `ProviderHealth` | `ProviderHealthPage` |
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
| `StrategyRunBrowserViewModel` | `Runs`, `SearchText`, `SelectedModeFilter` | `RefreshCommand`, `OpenDetailCommand`, `OpenPortfolioCommand`, `OpenLedgerCommand` |
| `StrategyRunDetailViewModel` | Execution summary, mode, timing, P&L, parameters | Cross-nav to Browser / Portfolio / Ledger |
| `StrategyRunPortfolioViewModel` | `TotalEquity`, `Cash`, exposure, `Positions` | Security Master resolve count |
| `StrategyRunLedgerViewModel` | `TrialBalance`, `Journal`, account balances | Security resolve count |

Parameter passing follows the standard MVVM drill-in pattern: `NavigationService.NavigateTo(tag, runId)` → `page.DataContext.Parameter = runId` → `LoadFromParameterAsync()`.

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

Research and Trading workspace shells remain lightweight presenter pages that surface the workspace's key metrics and provide quick navigation entry points to drill-in pages. Data Operations now uses a service-backed projection layer that folds provider, backfill, storage, session, notification, and export-job telemetry into a single operator shell.

Shell implementation now shares descriptor-driven infrastructure:

- `WorkspaceShellPageBase<TStateProvider, TViewModel>` owns dock restore/save, fallback content, and pane opening
- `WorkspaceShellViewModelBase` carries shell command state
- `IWorkspaceShellStateProvider` and `WorkspaceShellState` translate active run, operating-context, and preset state into declarative default panes
- `ShellNavigationCatalog.Workspaces.cs` is the source of truth for default panes and preset layouts across `Research`, `Trading`, `Data Operations`, and `Governance`

### `ResearchWorkspaceShellPage` (`Views/ResearchWorkspaceShellPage.xaml`)

**Purpose**: Single-page landing for the Research workspace. Shows the current research-cycle handoff, recent strategy runs, performance at a glance, and quick-links to Backtest, RunMat, Charts, and the run browser.

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
| `Meridian.Wpf.Tests` | 104 | WPF-specific services and shell projections: Navigation, Config, Connection, InfoBar, Keyboard, RunMat, Data Operations shell projection, etc. |
| `Meridian.Ui.Tests` | 171 | Shared services: ApiClient, Backfill, Charting, Watchlist, DataQuality, StrategyRun drill-ins |

Run with:

```bash
make test-desktop-services
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
