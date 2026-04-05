# WPF Desktop Application — Implementation Notes

**Version**: 1.7.x | **Last updated**: 2026-03-26 | **Status**: Authored / Included in solution build

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
|---------|------|
| `Meridian.Wpf` | Views, code-behind, WPF-specific services |
| `Meridian.Wpf.Tests` | 101 unit tests for WPF-specific services |
| `Meridian.Ui.Services` | Shared service layer (CommandPaletteService, WorkspaceService, NavigationServiceBase, etc.) |
| `Meridian.Ui.Tests` | 171 tests for shared UI services |
| `Meridian.Ui.Shared` | Endpoint helpers, DTO extensions, HTML template generator |
| `Meridian.Contracts` | Shared domain contracts, including `Workstation/StrategyRunReadModels.cs` |

---

## Shell Architecture

### MainPage — four-section sidebar + command palette

```
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

Content Frame
├── Fixture Mode Banner (orange, conditional on non-live mode)
└── WPF Frame → page navigation
```

**Workspace-aware navigation** — `ResolveWorkspaceIdForPage()` maps a page tag to its home workspace so that clicking a sidebar item or executing a command palette entry also activates the correct workspace session state.

**Selection suppression** — `_suppressNavSelection` prevents feedback loops when the NavigationService drives sidebar selection changes programmatically.

### Workspace system (`WorkspaceService`, `WorkspacePage`)

Four built-in workspace templates:

| Workspace ID | Pages |
|---|---|
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
- **Workspace labels** in descriptions: `Research workspace — Dashboard`, `Trading workspace — LiveData`, etc.

---

## Page Registry

All pages registered in `NavigationService.RegisterAllPages()` and declared in `Views/Pages.cs`.

### Research workspace pages
| Tag | Class | Notes |
|-----|-------|-------|
| `Dashboard` | `DashboardPage` | Default landing page |
| `Watchlist` | `WatchlistPage` | |
| `RunMat` | `RunMatPage` | Quant / script lab |
| `QuantScript` | `QuantScriptPage` | Notebook-style research surface with sticky document state, inline parameter validation, and session-preserved parameter inputs |
| `Charts` | `ChartingPage` | Candlestick / time-series |
| `OrderBook` | `OrderBookPage` | Live L2 depth |
| `StrategyRuns` | `StrategyRunsPage` | Strategy run browser |
| `RunDetail` | `RunDetailPage` | Run execution summary drill-in |
| `AdvancedAnalytics` | `AdvancedAnalyticsPage` | |

### Trading workspace pages
| Tag | Class | Notes |
|-----|-------|-------|
| `Backtest` | `BacktestPage` | |
| `LiveData` | `LiveDataViewerPage` | Real-time feed viewer |
| `RunPortfolio` | `RunPortfolioPage` | Positions, exposure, P&L drill-in |
| `LeanIntegration` | `LeanIntegrationPage` | QuantConnect Lean engine |
| `PortfolioImport` | `PortfolioImportPage` | CSV/bulk import |
| `TradingHours` | `TradingHoursPage` | Market calendar |

### Data Operations workspace pages
| Tag | Class |
|-----|-------|
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
|-----|-------|
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
|-----|-------|
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

```
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

```
ReconciliationRunRequest  — RunId, tolerance thresholds
ReconciliationBreakDto    — check ID, category, status, variance, source metadata
ReconciliationBreakCategory — AmountMismatch | MissingLedgerCoverage | MissingPortfolioCoverage | ...
```

---

## Strategy Run Workstation — ViewModel Layer

| ViewModel | Key properties | Commands |
|-----------|----------------|----------|
| `StrategyRunBrowserViewModel` | `Runs`, `SearchText`, `SelectedModeFilter` | `RefreshCommand`, `OpenDetailCommand`, `OpenPortfolioCommand`, `OpenLedgerCommand` |
| `StrategyRunDetailViewModel` | Execution summary, mode, timing, P&L, parameters | Cross-nav to Browser / Portfolio / Ledger |
| `StrategyRunPortfolioViewModel` | `TotalEquity`, `Cash`, exposure, `Positions` | Security Master resolve count |
| `StrategyRunLedgerViewModel` | `TrialBalance`, `Journal`, account balances | Security resolve count |

Parameter passing follows the standard MVVM drill-in pattern: `NavigationService.NavigateTo(tag, runId)` → `page.DataContext.Parameter = runId` → `LoadFromParameterAsync()`.

---

## Services Layer

### WPF-specific services (`Meridian.Wpf.Services`)
| Service | Responsibility |
|---------|----------------|
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
|----------|--------|
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
|------|----------|
| `ThemeTokens.xaml` | Semantic color tokens (`ConsoleTextPrimaryBrush`, `InfoColorBrush`, etc.) |
| `ThemeSurfaces.xaml` | Surface-level brushes (`ShellWindowBackgroundBrush`, `ShellRailBackgroundBrush`) |
| `ThemeControls.xaml` | Control styles (`NavItemStyle`, `CardStyle`, `PrimaryButtonStyle`, etc.) |
| `ThemeTypography.xaml` | Text styles (`PageTitleStyle`, `CardHeaderStyle`, `CardDescriptionStyle`) |
| `AppStyles.xaml` | Root merge dictionary |
| `Animations.xaml` | Shared transition animations |
| `IconResources.xaml` | Segoe Fluent Icons aliases |

---

## Research and Trading Workspace Shells

Two dedicated workspace shell pages now provide the primary operator surface for the Research and Trading workspaces. They are no longer lightweight landing pages. Each shell owns a workstation-grade dock surface, restores pane layout per workspace and fund, and keeps a shared selected-run context alive between research, portfolio, ledger, and trading views.

### `ResearchWorkspaceShellPage` (`Views/ResearchWorkspaceShellPage.xaml`)

**Purpose**: Backtest Studio for the Research workspace. Keeps strategy configuration, run context, run history, promotion actions, and embedded drill-ins in one screen.

**Design zones**:
1. **Header** — Studio title, selected-run context, KPI strip, and sticky fund scope
2. **Action bar** — Reset studio, promote to paper, open trading cockpit, run browser, run detail, portfolio inspector, ledger inspector
3. **Workbench panels** — Scenario/session rail, central run studio summary, right-side inspector summary
4. **History + promotion rail** — Recent run queue with direct open actions and promotion candidates
5. **Dock surface** — `MeridianDockingManager` hosting `Backtest`, `StrategyRuns`, `RunDetail`, `RunPortfolio`, `RunLedger`, `Charts`, and `LeanIntegration` as docked, tabbed, or floating panes

### `TradingWorkspaceShellPage` (`Views/TradingWorkspaceShellPage.xaml`)

**Purpose**: Trading Cockpit for the Trading workspace. Keeps live posture, active run context, blotter/order-book/risk panes, and capital posture in one workstation layout.

**Design zones**:
1. **Header** — Cockpit title, fund scope, selected-run context, KPI strip
2. **Desk action bar** — Pause, stop, flatten, cancel-all, acknowledge-risk
3. **Workbench panels** — Strategy/watchlist rail, market core, and blotter/alerts/risk summary
4. **Position + capital rail** — Active position list, cash/gross/net/financing posture, risk snapshot
5. **Dock surface** — `MeridianDockingManager` hosting `LiveData`, `RunPortfolio`, `PositionBlotter`, `OrderBook`, `RunRisk`, `RunLedger`, and `NotificationCenter`

### Main Shell Split/Float Workflow

`MainPage` now exposes operator-facing pane actions in the shell command bar:

- Preset workstation layouts
- `Split Right` and `Split Below`
- `Float`
- `Save Layout`
- `Reset`

Keyboard shortcuts implemented in the shell:

- `Ctrl+\` split right
- `Ctrl+Shift+\` split below
- `Ctrl+Alt+N` float the active pane
- `Ctrl+1..4` focus panes
- `Ctrl+Shift+R` reset the split layout

Navigation list items, command palette results, and recent-page chips can now be dragged into the split host or workspace dock surfaces using the shared `Meridian.PageTag` drag payload.

### Layout Persistence

`WorkspaceService` persists workstation layout state additively alongside session restore:

- preferred shell-first routes (`ResearchShell`, `TradingShell`)
- dock layout XML keyed by workspace and optional fund profile
- pane metadata (`PageTag`, dock zone, active pane, tool/document state)
- floating window metadata
- user-saveable layout presets in `SessionState` / `WorkspaceTemplate`

`StrategyRunWorkspaceService` now owns the shared `ActiveRunContext` used by both shells for:

- selected run ID and strategy identity
- fund scope label
- portfolio preview
- ledger preview
- trading handoff state
- promote-to-paper eligibility

---

## Build

```bash
# Standalone WPF build (Windows or cross-platform with Windows targeting)
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableWindowsTargeting=true -c Release

# WPF + shared UI services tests
dotnet test tests/Meridian.Wpf.Tests /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Ui.Tests /p:EnableWindowsTargeting=true
```

### Common errors

| Error | Fix |
|-------|-----|
| NETSDK1100 | Add `/p:EnableWindowsTargeting=true` on non-Windows hosts |
| `NU1008` | Remove `Version="..."` from any `<PackageReference>` — versions live in `Directory.Packages.props` |
| Page not found at runtime | Ensure page is declared in `Views/Pages.cs` **and** registered in `NavigationService.RegisterAllPages()` |

---

## Testing

| Test project | Count | Covers |
|---|---|---|
| `Meridian.Wpf.Tests` | 101 | WPF-specific services: Navigation, Config, Connection, InfoBar, Keyboard, RunMat, etc. |
| `Meridian.Ui.Tests` | 171 | Shared services: ApiClient, Backfill, Charting, Watchlist, DataQuality, StrategyRun drill-ins |

Run with:
```bash
make test-desktop-services
```

---

## Contributing

1. **Register new pages** in both `Views/Pages.cs` (partial class) and `NavigationService.RegisterAllPages()`
2. **Add command palette entry** in `CommandPaletteService.RegisterDefaultCommands()` — include workspace label in the `pageTag` argument so `BuildNavigationDescription` resolves correctly
3. **Follow MVVM patterns** — all data logic in ViewModels; code-behind restricted to UI event wiring
4. **Event cleanup** — always unsubscribe in `OnPageUnloaded` / `OnNavigatedFrom`
5. **Use shared contracts** — workstation read models live in `Meridian.Contracts.Workstation`; never duplicate DTO types in the WPF project

---

## Related Documentation

- [`docs/architecture/desktop-layers.md`](../architecture/desktop-layers.md) — Layer boundaries
- [`docs/development/desktop-testing-guide.md`](./desktop-testing-guide.md) — Testing procedures
- [`docs/evaluations/desktop-improvements-executive-summary.md`](../evaluations/desktop-improvements-executive-summary.md) — Platform improvement roadmap
- [`docs/development/ui-fixture-mode-guide.md`](./ui-fixture-mode-guide.md) — Offline / fixture mode development
- [`docs/status/ROADMAP.md`](../status/ROADMAP.md) — Desktop items in the project roadmap
- [`docs/development/policies/desktop-support-policy.md`](./policies/desktop-support-policy.md) — Contribution requirements
