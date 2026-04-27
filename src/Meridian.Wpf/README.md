# Meridian - WPF Desktop Application

This is the WPF (.NET 9) desktop application for Meridian. It is the primary desktop operator shell and the main host for the workstation migration.

## Overview

The current WPF application already spans research, trading-adjacent, data-operations, and governance-adjacent workflows:

- **Research workflows** - Research workspace, backtests, charts, replay, run comparison, RunMat, and Lean integration
- **Trading-adjacent workflows** - live data, watchlists, order-book views, trading-hours awareness, and shared run drill-ins
- **Data operations** - symbols, providers, backfills, schedules, storage, packaging, and export flows
- **Governance-adjacent workflows** - portfolio and ledger drill-ins, diagnostics, provider health, retention, and settings
- **Shell ergonomics** - the workstation header exposes quick shell-density switching while the persisted preference continues to round-trip through Settings, the recent-pages rail stays scoped to the active workspace so sidebar history matches the selected operator context, the governance shell keeps the currently selected lane plus its next handoff visible above the queue wall, and the trading shell now keeps a desk-briefing hero above the workbench so context, replay/controls posture, and the next desk action stay explicit

The repo now also includes persisted built-in workspace categories for `Research`, `Trading`, `Data Operations`, and `Governance`, plus shared run, portfolio, ledger, and early reconciliation seams that the desktop shell can grow into.

The persistent status bar now surfaces pipeline queue pressure from the existing status snapshot beside connection state, throughput, backfills, drop badges, and UTC time, so operators can spot queue saturation without opening a diagnostics page or triggering another service read.

Recent governance work is also moving older utility pages into shell-native workbenches. `FundAccounts` now participates in the governance shell with page-body metrics, a stateful operator brief, account inspectors, provider-routing previews, and Security Master / historical-price / backfill posture surfaced directly from the shared `FundStructureSharedDataAccessDto` baseline.
`NotificationCenter` now supports history triage with search, unread-only filtering, directly bound severity filters, per-item acknowledgement, and a reset-filters recovery action so governance operators can work events as a queue instead of a flat feed.
`ProviderHealth` now opens with a compact provider-posture briefing that turns connected/disconnected streaming counts, backfill availability, and stale snapshots into one next handoff before the operator scans individual provider cards.
`SystemHealth` now opens with a triage briefing that folds provider health, storage pressure, corrupted/orphaned storage evidence, and retained event severity into one next handoff before the operator scans CPU, storage, and recent-event panels; its provider and recent-event empty states distinguish pending scans from confirmed empty snapshots.
`ActivityLog` now keeps a compact triage strip above the virtualized event list so visible entries, retained errors, retained warnings, latest event time, and active filters stay visible while operators export, clear, or reset filtered support traces.
`Watchlist` now opens with a posture card that summarizes saved lists, pinned lists, symbol coverage, and current search scope before operators load, pin, create, or import a list.
`SecurityMaster` now adds a search-recovery card and bound `Clear Search` action so unavailable runtime checks and no-match searches can reset query/results state without another workstation read.
`GovernanceWorkspaceShellPage` now adds a selected-lane briefing card above the lane buttons so operators can keep the active queue, blocker, and next action visible before opening `Operations`, `Accounting`, `Reconciliation`, `Reporting`, or `Audit`.
`FundLedger` reconciliation now includes a reset-filters recovery action beside queue refresh so scope/search misses can restore the already-loaded open break queue without another service read.
`DataOperationsWorkspaceShellPage` now opens with a scope-and-handoff briefing card plus compact provider, backfill, and storage health chips so operators see the active focus and readiness posture before dropping into the queue wall.
`DataBrowser` now refreshes its retained market-data window as filters change and shows a reset-filters empty state when search, type, venue, or date filters hide every row.
`ResearchWorkspaceShellPage` now keeps a desk-briefing hero above the market briefing so operators can see the current cycle focus, the next handoff, and the primary blocker before dropping into run history or promotion candidates.
`StrategyRuns` now distinguishes an empty run library from filters that hide retained runs, shows the visible/recorded run scope beside search, and exposes a reset-filters recovery action without reloading the run store.
`BatchBacktest` now gives the sweep results pane stateful empty guidance for idle, validation-blocked, running, failed-without-results, cancelled, and populated result states using only the existing batch counters and summaries.
`RunMat` now gives the output panel an empty/streaming state and disables Stop unless a script run is active, using only the current run state and retained output lines.
`QuantScript` now includes a dedicated Run History tab that surfaces recorded executions, selected-run evidence, console preview, and the existing Strategy Runs handoff actions.

## Why WPF?

This project was migrated from UWP/WinUI 3 to WPF for several reasons:

1. **Broader compatibility** - Works on standard Windows desktop environments without UWP packaging constraints.
2. **Mature and stable** - Strong tooling and long-lived desktop patterns.
3. **Simpler deployment** - Standard executable and publish flows.
4. **Cleaner .NET integration** - Avoids WinRT metadata and source-inclusion workarounds that complicated the prior stack.

## Migration Direction

The WPF application remains Meridian's primary desktop surface, but it is now the host for the **Trading Workstation Migration**. The current codebase already contains broad feature coverage, persisted built-in workspaces, and shared run drill-ins; the next implementation phase reorganizes that breadth into more durable workflow-centric workspaces:

- **Research** - backtesting, experiment comparison, charts, replay, and research drill-ins
- **Trading** - live monitoring, orders, positions, paper/live strategy operation, and promotion-adjacent workflows
- **Data Operations** - providers, symbols, backfill, storage, schedules, and export
- **Governance** - portfolio, ledger, diagnostics, retention, and settings, with deeper reconciliation and reporting still to come

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
5. Land on a workspace-first welcome surface that launches `Research`, `Trading`, `Data Operations`, or `Governance`, with a collection-focused quick start kept under Data Operations.

## Current Surface Map

The application still contains many page-level screens, but the active desktop direction is to group them into workstation-native journeys.

Examples:

- `Welcome`: workspace launcher plus Data Operations collection quick start
- `Research`: `ResearchShell` workspace overview, `Backtest`, `BatchBacktest`, `StrategyRuns`, `LeanIntegration`, `Charts`, `RunMat`, `EventReplay`
- `Trading`: `LiveData`, `StrategyRuns`, `RunPortfolio`, `RunLedger`, `PositionBlotter`, `OrderBook`, `PortfolioImport`, `TradingHours`, `Watchlist`
- `Data Operations`: `Provider`, `Symbols`, `Backfill`, `Schedules`, `Storage`, `PackageManager`, `DataExport`
- `Governance`: `GovernanceShell`, `FundAccounts`, `SecurityMaster`, `FundLedger`, `FundReconciliation`, `DataQuality`, `ProviderHealth`, `SystemHealth`, `Diagnostics`, `RetentionAssurance`, `AdminMaintenance`, `Settings`

`PositionBlotter` includes a selected-position review rail for action eligibility, long/short exposure totals, and compact selected-row previews before batch flatten or upsize actions are submitted.
`TradingHours` includes a session-briefing strip that translates the current calendar state into a live-risk, pre-market, after-hours, or closed-planning handoff beside the exchange schedule.
`FundAccounts` includes a stateful operator brief that projects fund-context, account-queue, provider-routing, blocked-route, shared-data, and ready-for-reconciliation states from already-loaded account and provider evidence.
`ProviderHealth` includes a provider-posture briefing ahead of the individual provider grids so stale snapshots, offline streaming sessions, mixed-provider states, and blocked backfill coverage produce one visible next handoff.
`SystemHealth` includes a triage briefing ahead of the resource metrics so provider, storage, and event posture produce one visible support handoff without another health fetch.
`NotificationCenter` includes a reset-filters empty-state action and view-model-owned severity filter state so search, unread-only, and severity-filter misses can recover the retained history list without another service read or code-behind checkbox synchronization.
`ActivityLog` includes a triage strip and reset-filters empty-state action ahead of the virtualized log list so support workflows can see retained errors, warnings, the latest event, and active filter scope, then recover hidden retained entries without another backend request. Its header actions are state-aware: export is available only for visible rows, and clear is available only when retained activity exists.
`MessagingHub` includes a bound refresh action with header recency text, a delivery-posture card, retained-activity scope, and a disabled clear action when no messaging rows are retained.
`Watchlist` includes a posture card and dynamic empty-state copy so search misses, unpinned libraries, and ready pinned lists each give the operator a clear next step.
`SecurityMaster` includes a search-recovery empty state and view-model-owned `ClearSearchCommand` so failed or unavailable searches can clear the query, selected security, and retained results without issuing another service call.
`DataQuality` distinguishes symbol search misses from an empty monitored-symbol library and exposes a `Clear Filter` recovery action in the Quality by Symbol panel.
`DataBrowser` includes a filter-aware empty state and view-model-owned `Reset Filters` command so data operations users can recover hidden retained market-data rows without a backend read.
`TradingWorkspaceShellPage` now adds a desk-briefing hero above the workbench so context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight states keep one primary handoff visible before the operator drops into blotter, risk, or audit surfaces.
`ResearchWorkspaceShellPage` now keeps the active research cycle explicit with a desk-briefing hero that upgrades no-op trading-review prompts into actionable run-browser, portfolio, or promotion handoffs based on the selected run state.
`StrategyRuns` includes a filter-aware empty state and `Reset Filters` action so search or mode misses can recover the already-loaded run browser rows without another service read.
`BatchBacktest` hides the empty result grid until rows exist and shows automation-addressable guidance for unresolved validation, active sweeps waiting on first rows, failed batches, and cancelled runs.
`RunMat` exposes output-line count plus idle, streaming, and no-output guidance beside the existing Last Run and resolved-executable panels.
`QuantScript` renders its retained execution history in the workbench so notebook users can inspect parameters, outputs, mirrored backtest evidence, and run-browser handoffs without leaving the page.

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
