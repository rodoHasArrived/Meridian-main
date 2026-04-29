# Meridian - WPF Desktop Application

This is the WPF (.NET 9) desktop application for Meridian. It is the primary desktop operator shell and the main host for the workstation migration.

## Overview

The current WPF application projects seven root workspace capabilities: Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.

- **Trading workflows** - live data, watchlists, order-book views, trading-hours awareness, execution risk, and position blotter handoffs
- **Portfolio workflows** - account and aggregate portfolios, fund accounts, imports, direct lending, and run portfolio drill-ins
- **Accounting workflows** - fund ledger, cash-flow, banking, financing, trial balance, reconciliation, and audit trail workflows
- **Reporting workflows** - report packs, dashboards, analysis export, export wizards, and export presets
- **Strategy workflows** - backtests, strategy runs, charts, replay, run comparison, RunMat, QuantScript, and Lean integration
- **Data workflows** - symbols, providers, provider health, backfills, schedules, storage, packaging, and export flows
- **Settings workflows** - credentials, diagnostics, system health, service management, activity, notifications, local AI agent, shortcuts, help, and setup
- **Shell ergonomics** - the workstation header exposes quick shell-density switching while the persisted preference continues to round-trip through Settings, the recent-pages rail stays scoped to the active workspace so sidebar history matches the selected operator context, the governance shell keeps the currently selected lane plus its next handoff visible above the queue wall, and the trading shell now keeps a desk-briefing hero above the workbench so context, replay/controls posture, and the next desk action stay explicit

The header operator queue consumes shared inbox route metadata and maps readiness work items into concrete workbenches: replay and DK1 trust items open `FundAuditTrail`, promotion review opens `StrategyRuns`, brokerage-sync blockers open `AccountPortfolio`, execution-control blockers open `RunRisk`, and governance items open `SecurityMaster`, `FundReconciliation`, or `FundReportPack`.

The repo now also includes persisted built-in workspace categories for `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, plus hidden compatibility aliases for the former `Research`, `Data Operations`, and `Governance` roots.

The persistent status bar now surfaces pipeline queue pressure from the existing status snapshot beside connection state, throughput, backfills, drop badges, and UTC time, so operators can spot queue saturation without opening a diagnostics page or triggering another service read.

Recent governance work is also moving older utility pages into shell-native workbenches. `FundAccounts` now participates in the governance shell with page-body metrics, a stateful operator brief, account inspectors, provider-routing previews, balance-evidence posture, and Security Master / historical-price / backfill posture surfaced directly from retained account evidence plus the shared `FundStructureSharedDataAccessDto` baseline.
`NotificationCenter` now supports history triage with search, unread-only filtering, directly bound severity filters, per-item acknowledgement, and a reset-filters recovery action so governance operators can work events as a queue instead of a flat feed.
`ProviderHealth` now opens with a compact provider-posture briefing that turns connected/disconnected streaming counts, backfill availability, and stale snapshots into one next handoff before the operator scans individual provider cards.
`Backfill` now surfaces a bound start-readiness card before launch, with symbol/date validation, request scope, and the Start button enablement projected by `BackfillViewModel` instead of page-owned validation label updates.
`ScheduleManager` now binds schedule refresh, empty/error status, template loading, and cron validation through `ScheduleManagerViewModel`, keeping the page code-behind limited to construction and initial load.
`SystemHealth` now opens with a triage briefing that folds provider health, storage pressure, corrupted/orphaned storage evidence, and retained event severity into one next handoff before the operator scans CPU, storage, and recent-event panels; its provider and recent-event empty states distinguish pending scans from confirmed empty snapshots.
`ActivityLog` now keeps a compact triage strip above the virtualized event list so visible entries, retained errors, retained warnings, latest event time, and active filters stay visible while operators export, clear, or reset filtered support traces.
`Watchlist` now opens with a posture card that summarizes saved lists, pinned lists, symbol coverage, and current search scope before operators load, pin, create, or import a list; pinned lists also surface first with compact card badges for quick desk loading.
`SecurityMaster` now binds the Search button and Enter key to `SearchCommand`, then pairs no-match or unavailable-runtime states with a recovery card and bound `Clear Search` action so query/results state can reset without another workstation read.
Accounting, Reporting, and Settings now split the former governance surface into capability-specific roots while preserving legacy governance deep-link compatibility.
`FundLedger` reconciliation now includes a reset-filters recovery action beside queue refresh and consumes the shared reconciliation calibration summary so tolerance-profile posture, pending sign-off, and missing calibration metadata are visible before operators resolve or sign off break queue work.
`DataOperationsWorkspaceShellPage` now backs the Data root with a scope-and-handoff briefing card plus compact provider, backfill, and storage health chips so operators see the active focus and readiness posture before dropping into the queue wall.
`AddProviderWizard` now binds provider-catalog filtering, result-scope copy, empty-state recovery, credential guidance, connection feedback, save feedback, and setup-progress fills through `AddProviderWizardViewModel` while the page stays limited to navigation and dynamic credential-field controls.
`Storage` now opens with an archive-posture card that summarizes daily growth, capacity horizon, and the last metrics scan before operators review the file-structure preview. The preview still annotates the selected root, layout, and compression scope so archive-path decisions are visible before operators run backfill, export, or package jobs.
`DataSources` now surfaces edit-readiness copy, save gating, provider/type/feed bindings, option checkbox bindings, symbol scope, and row commands through `DataSourcesViewModel` so provider setup persists through view-model state instead of page event relays.
`SymbolMapping` now binds provider lists, test results, add-mapping readiness, mapping counts, empty-state visibility, and inline remove confirmation through `SymbolMappingViewModel`; import/export file pickers remain view-owned while CSV content flows through the view model.
`CollectionSessions` now exposes session lifecycle readiness, loading state, empty-history recovery, and create/refresh/pause/stop actions through `CollectionSessionViewModel` commands so capture-session controls disable consistently during async work.
`DataBrowser` now refreshes its retained market-data window as filters change, binds the top time-period selector to view-model date-range state, and shows a reset-filters empty state when search, type, venue, or date filters hide every row.
`DataExport` now surfaces Quick Export and Scheduled Export readiness before launch, disables invalid export or schedule attempts through `DataExportViewModel`, and keeps symbol count, date scope, format, compression, schedule destination, and progress state bound to the view model.
`DataSampling` now binds symbol add, sample generation, and preset save actions through `DataSamplingViewModel` commands, with inline readiness, symbol-scope, validation, and recent-sample state before an operator queues a sample.
`TimeSeriesAlignment` now moves alignment setup, symbol parsing, preset application, validation, command enablement, run progress, result summary, and recent alignment state into `TimeSeriesAlignmentViewModel`, so invalid runs are explained before launch and the page code-behind stays focused on construction.
`AdvancedAnalytics` now binds diagnostics actions through `AdvancedAnalyticsViewModel` commands, shows comparison readiness guidance beside the provider-compare controls, and replaces the repair `MessageBox` with an inline confirmation panel driven by repairable-gap state.
`AnalysisExport` now binds run and preset actions through `AnalysisExportViewModel` commands, surfaces export readiness guidance before launch, and keeps recent-export state text with the retained in-session export list.
`AnalysisExportWizard` now binds add-symbol, step navigation, queue, and reset actions through `AnalysisExportWizardViewModel`, with step readiness, validation visibility, and scope text projected before an operator advances through the wizard.
`ExportPresets` now binds save/delete actions through `ExportPresetsViewModel` commands, shows preset-library and save-readiness copy before operators commit changes, and keeps built-in preset deletion disabled through view-model state.
`ResearchWorkspaceShellPage` now backs the Strategy root with a desk-briefing hero above the market briefing so operators can see the current cycle focus, the next handoff, and the primary blocker before dropping into run history or promotion candidates.
`Charting` now binds symbol, timeframe, date range, indicator toggles, and refresh readiness through `ChartingPageViewModel`, with a setup-readiness card that explains incomplete chart requests before the candlestick surface is refreshed.
`StrategyRuns` now distinguishes an empty run library from filters that hide retained runs, shows the visible/recorded run scope beside search, gates the comparison picker with next-step guidance, and exposes a reset-filters recovery action without reloading the run store.
`BatchBacktest` now gives the sweep results pane stateful empty guidance for idle, validation-blocked, running, failed-without-results, cancelled, and populated result states using only the existing batch counters and summaries.
`RunMat` now gives the output panel an empty/streaming state and disables Stop unless a script run is active, using only the current run state and retained output lines.
`QuantScript` now includes a dedicated Run History tab that surfaces recorded executions, selected-run evidence, console preview, and the existing Strategy Runs handoff actions.
`RunCashFlow` now shows bound empty-state guidance for missing run evidence and no-cash-flow runs instead of leaving the cash ladder and event grids blank.
`Welcome` now adds a readiness progress strip to the next-action panel so provider, symbol, and storage setup posture is visible before the operator opens a workspace shell.

## Why WPF?

This project was migrated from UWP/WinUI 3 to WPF for several reasons:

1. **Broader compatibility** - Works on standard Windows desktop environments without UWP packaging constraints.
2. **Mature and stable** - Strong tooling and long-lived desktop patterns.
3. **Simpler deployment** - Standard executable and publish flows.
4. **Cleaner .NET integration** - Avoids WinRT metadata and source-inclusion workarounds that complicated the prior stack.

## Migration Direction

The WPF application remains Meridian's primary desktop surface, but it is now the host for the **Trading Workstation Migration**. The current codebase already contains broad feature coverage, persisted built-in workspaces, and shared run drill-ins; the shell now organizes that breadth into seven workflow-centric workspaces:

- **Trading** - live monitoring, orders, positions, trading hours, execution risk, and position blotters
- **Portfolio** - account, aggregate, fund, import, direct-lending, and run portfolio workflows
- **Accounting** - ledger, cash-flow, banking, financing, trial-balance, reconciliation, and audit workflows
- **Reporting** - report packs, dashboards, analysis export, export wizards, and presets
- **Strategy** - backtesting, experiment comparison, charts, replay, QuantScript, RunMat, and strategy drill-ins
- **Data** - providers, symbols, backfill, storage, schedules, quality, package, and export workflows
- **Settings** - credentials, diagnostics, service management, health, activity, notifications, help, and setup

See [`docs/plans/trading-workstation-migration-blueprint.md`](../../docs/plans/trading-workstation-migration-blueprint.md) for the active migration blueprint, [`docs/status/ROADMAP.md`](../../docs/status/ROADMAP.md) for the active delivery waves, and [`docs/architecture/ui-redesign.md`](../../docs/architecture/ui-redesign.md) for the target information architecture.

## Architecture

### Technology Stack

- **.NET 9.0**
- **WPF**
- **MVVM pattern**
- **Microsoft.Extensions.DependencyInjection**
- **Async/await**

### Key implementation seams

- `Services/NavigationService.cs` - page registration and navigation orchestration
- `Services/WorkspaceService.cs` - persisted workspace templates and session restore
- `Services/StrategyRunWorkspaceService.cs` - shared run drill-in coordination
- `Views/MainPage.xaml` - workstation-oriented shell navigation
- `ViewModels/` - incremental MVVM extraction for richer surfaces

## Build and Run

### Requirements

- **.NET 9 SDK**
- **Windows** for a functional WPF build
- **Visual Studio 2022** optional, but useful for XAML work

### Commands

```bash
# Restore dependencies
dotnet restore src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true

# Build
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true

# Run
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -p:EnableFullWpfBuild=true

# Publish (self-contained)
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -r win-x64 --self-contained -p:EnableFullWpfBuild=true
```

### Platform behavior

- **On Windows**: builds as the full desktop application.
- **On Linux/macOS**: builds as a minimal stub for CI compatibility.

## Running the App

### Prerequisites

1. Start the backend API, normally at `http://localhost:8080`.
2. Ensure configuration exists in `appsettings.json` or the Meridian app-data location.

### First run behavior

On first run, the application will:

1. Check for existing configuration.
2. Create or copy a default config when needed.
3. Initialize services.
4. Restore the last saved workspace/session state when available.
5. Land on a workspace-first welcome surface that launches `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, or `Settings`, with a collection-focused quick start kept under Data.

## Current Surface Map

The application still contains many page-level screens, but the active desktop direction is to group them into workstation-native journeys.

Examples:

- `Welcome`: workspace launcher plus Data collection quick start and readiness progress
- `Trading`: `TradingShell`, `LiveData`, `OrderBook`, `PositionBlotter`, `RunRisk`, `TradingHours`
- `Portfolio`: `PortfolioShell`, `AccountPortfolio`, `AggregatePortfolio`, `RunPortfolio`, `FundPortfolio`, `FundAccounts`, `PortfolioImport`, `DirectLending`
- `Accounting`: `AccountingShell`, `FundLedger`, `RunLedger`, `RunCashFlow`, `FundBanking`, `FundCashFinancing`, `FundTrialBalance`, `FundReconciliation`, `FundAuditTrail`
- `Reporting`: `ReportingShell`, `FundReportPack`, `Dashboard`, `AnalysisExport`, `AnalysisExportWizard`, `ExportPresets`
- `Strategy`: `StrategyShell`, `Backtest`, `BatchBacktest`, `StrategyRuns`, `RunDetail`, `LeanIntegration`, `Charts`, `RunMat`, `QuantScript`, `EventReplay`, `Watchlist`
- `Data`: `DataShell`, `Provider`, `ProviderHealth`, `Symbols`, `Backfill`, `Schedules`, `Storage`, `PackageManager`, `DataExport`, `TimeSeriesAlignment`, `DataQuality`, `CollectionSessions`, `SecurityMaster`, `DataBrowser`
- `Settings`: `SettingsShell`, `Settings`, `CredentialManagement`, `SystemHealth`, `Diagnostics`, `ServiceManager`, `AdminMaintenance`, `MessagingHub`, `NotificationCenter`, `ActivityLog`, `KeyboardShortcuts`, `Help`, `SetupWizard`, `Workspaces`

`PositionBlotter` includes a selected-position review rail for action eligibility, long/short exposure totals, and compact selected-row previews before batch flatten or upsize actions are submitted.
`TradingHours` includes a session-briefing strip that translates the current calendar state into a live-risk, pre-market, after-hours, or closed-planning handoff beside the exchange schedule, and its holiday card now distinguishes loaded closures from missing or unavailable calendar rows.
`OrderBook` includes VM-bound symbol/depth selectors plus an order-flow posture strip that summarizes symbol scope, depth availability, spread, cumulative delta, and the next monitoring handoff above the depth ladder and tape.
`FundAccounts` includes a stateful operator brief that projects fund-context, account-queue, provider-routing, blocked-route, shared-data, balance-evidence, and ready-for-reconciliation states from already-loaded account and provider evidence.
`AccountPortfolio` hides blank account-position grids behind view-model-owned account-selection, loading, unavailable-snapshot, and no-open-position guidance, with refresh disabled until an account context exists.
`AggregatePortfolio` hides blank desk-level position grids behind view-model-owned first-load, loading, unavailable-snapshot, and no-netted-position guidance, with the existing refresh command exposed in the recovery card.
`PortfolioImport` now exposes file, index, and manual-entry actions through view-model commands, with readiness copy and manual symbol counts derived before operators start an import.
`AdvancedAnalytics` exposes refresh, report generation, gap analysis, repair confirmation, provider comparison, and status dismissal through view-model commands so diagnostic actions stay testable and code-behind remains limited to page-load initialization.
`ProviderHealth` includes a provider-posture briefing ahead of the individual provider grids so stale snapshots, offline streaming sessions, mixed-provider states, and blocked backfill coverage produce one visible next handoff.
`SystemHealth` includes a triage briefing ahead of the resource metrics so provider, storage, and event posture produce one visible support handoff without another health fetch.
`NotificationCenter` includes a reset-filters empty-state action and view-model-owned severity filter state so search, unread-only, and severity-filter misses can recover the retained history list without another service read or code-behind checkbox synchronization.
`ActivityLog` includes a triage strip and reset-filters empty-state action ahead of the virtualized log list so support workflows can see retained errors, warnings, the latest event, and active filter scope, then recover hidden retained entries without another backend request. Its header actions are state-aware: export is available only for visible rows, and clear is available only when retained activity exists.
`MessagingHub` includes a bound refresh action with header recency text, a delivery-posture card, retained-activity scope, and a disabled clear action when no messaging rows are retained.
`AdminMaintenance` includes view-model-owned schedule readiness, cleanup readiness, preview command, inline confirmation, and execute gating so enabled maintenance schedules require at least one selected operation and destructive cleanup actions stay disabled until a staged preview has files.
`Agent` includes a view-model-owned local-AI readiness card, model-scope text, empty-conversation guidance, and command-gated input/clear actions so Ollama availability and missing-model setup are visible before an operator tries to chat.
`Watchlist` includes a posture card, pinned-first card ordering, pinned badges, and dynamic empty-state copy so search misses, unpinned libraries, and ready pinned lists each give the operator a clear next step.
`SecurityMaster` includes a view-model-owned `SearchCommand`, search-recovery empty state, and `ClearSearchCommand` so failed or unavailable searches can run from the same button/keyboard path and clear the query, selected security, and retained results without issuing another service call.
`Symbols` includes view-model-owned search, subscription, and exchange filter state with visible-scope copy, a filter-aware empty state, a bound `Clear Filters` recovery action, and bulk-action enablement that follows the retained row selection.
`SymbolMapping` includes view-model-owned test/add/remove mapping state, inline setup guidance, mapping/test result visibility, and a remove-confirmation panel so provider symbol overrides can be checked and changed without page-owned workflow logic.
`CollectionSessions` includes a view-model-owned lifecycle readiness card, busy/loading projection, command-gated create/refresh/pause/stop actions, and an empty-history recovery path for starting the first daily capture session.
`DataQuality` distinguishes symbol search misses from an empty monitored-symbol library and exposes a `Clear Filter` recovery action in the Quality by Symbol panel.
`DataBrowser` includes a view-model-owned time-period selector, filter-aware empty state, and `Reset Filters` command so data operations users can scope or recover hidden retained market-data rows without a backend read.
`DataSampling` includes a view-model-owned readiness card, command-gated add/generate/save actions, symbol-scope copy, and recent-sample state so data operators can resolve missing name, symbols, date range, or data-type setup before queueing a sample.
`TimeSeriesAlignment` includes a view-model-owned readiness card, bound setup controls, command-gated run action, progress state, result summary, and recent-alignment empty state so data operators can fix missing symbols, dates, or fields before launching an alignment request.
`TradingWorkspaceShellPage` now adds a desk-briefing hero above the workbench so context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight states keep one primary handoff visible before the operator drops into blotter, risk, or audit surfaces.
`ResearchWorkspaceShellPage` now keeps the active research cycle explicit with a desk-briefing hero that upgrades no-op trading-review prompts into actionable run-browser, portfolio, or promotion handoffs based on the selected run state.
`StrategyRuns` includes a filter-aware empty state, comparison-picker guidance, and a `Reset Filters` action so search or mode misses can recover the already-loaded run browser rows without another service read.
`BatchBacktest` hides the empty result grid until rows exist and shows automation-addressable guidance for unresolved validation, active sweeps waiting on first rows, failed batches, and cancelled runs.
`RunMat` exposes output-line count plus idle, streaming, and no-output guidance beside the existing Last Run and resolved-executable panels.
`QuantScript` renders its retained execution history in the workbench so notebook users can inspect parameters, outputs, mirrored backtest evidence, and run-browser handoffs without leaving the page.
`RunCashFlow` hides empty ladder/event grids and explains whether the operator needs to select a run, recover missing run evidence, or accept that a retained run produced no cash-flow rows.

## Development Notes

### Adding a page

1. Add the XAML page under `Views/`.
2. Add its code-behind or view model.
3. Register it in `Services/NavigationService.cs`.
4. Place it in the appropriate workstation or supporting navigation surface.

### Adding a service

1. Add the interface and implementation under `Services/`.
2. Register it in `App.xaml.cs` or the relevant composition path.
3. Inject it into pages or view models through constructors.

### Implementation guidance

- Keep business logic in services or view models instead of code-behind where practical.
- Prefer extending shared run, portfolio, ledger, and workstation services before introducing parallel desktop-only models.
- Use `IBatchBacktestService` for `BatchBacktest` request sweeps so the desktop panel exercises the same backtesting engine path as non-desktop callers.
- Treat the workstation categories as the user-facing source of truth even when legacy pages still exist underneath.
- Keep workstation chrome aligned with the dark Meridian operator style: navy shell surfaces, cyan focus and selection accents, compact data cards, and dense panel layouts suited to repeated market-monitoring work.
- Avoid `.Result` / `.Wait()` in UI-facing async paths; use `await` + cancellation tokens to prevent desktop deadlocks.

## Deployment

### Single-file publish

```bash
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableFullWpfBuild=true
```

### Framework-dependent publish

```bash
dotnet publish src/Meridian.Wpf/Meridian.Wpf.csproj \
  -c Release \
  -r win-x64 \
  --no-self-contained \
  -p:EnableFullWpfBuild=true
```

## Configuration

The application uses `appsettings.json` for configuration. On first run, it will copy `appsettings.sample.json` if no configuration exists.

User-local configuration is stored under:

`%APPDATA%\\Meridian\\appsettings.json`

Workspace/session state is stored separately under the Meridian local app-data area managed by `WorkspaceService`.

## Known Gaps

- The workstation shell is real, but some flows still rely on older page-first composition.
- Shared run drill-ins are established, but broader paper/live and governance-grade reconciliation/reporting workflows are still in progress.
- Governance account operations now expose richer shell-native inspectors, but adjacent governance pages still need the same page-body harmonization pass.
- MVVM extraction is incremental rather than complete across every page.
