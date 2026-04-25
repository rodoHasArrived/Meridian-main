# Meridian - WPF Desktop Application

This is the WPF (.NET 9) desktop application for Meridian. It is the primary desktop operator shell and the main host for the workstation migration.

## Overview

The current WPF application already spans research, trading-adjacent, data-operations, and governance-adjacent workflows:

- **Research workflows** - portfolio operations dashboard, backtests, charts, replay, run comparison, RunMat, and Lean integration
- **Trading-adjacent workflows** - live data, watchlists, order-book views, trading-hours awareness, and shared run drill-ins
- **Data operations** - symbols, providers, backfills, schedules, storage, packaging, and export flows
- **Governance-adjacent workflows** - portfolio and ledger drill-ins, diagnostics, provider health, retention, and settings
- **Shell ergonomics** - the workstation header exposes quick shell-density switching while the persisted preference continues to round-trip through Settings, and the recent-pages rail stays scoped to the active workspace so sidebar history matches the selected operator context

The repo now also includes persisted built-in workspace categories for `Research`, `Trading`, `Data Operations`, and `Governance`, plus shared run, portfolio, ledger, and early reconciliation seams that the desktop shell can grow into.

Recent governance work is also moving older utility pages into shell-native workbenches. `FundAccounts` now participates in the governance shell with page-body metrics, account inspectors, provider-routing previews, and Security Master / historical-price / backfill posture surfaced directly from the shared `FundStructureSharedDataAccessDto` baseline.
`NotificationCenter` now supports history triage with search, unread-only filtering, and per-item acknowledgement so governance operators can work events as a queue instead of a flat feed.
`DataOperationsWorkspaceShellPage` now opens with a scope-and-handoff briefing card so operators see the active operational focus before dropping into provider, backfill, storage, and export queues.

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
- `Research`: `Dashboard` portfolio operations overview, `Backtest`, `BatchBacktest`, `StrategyRuns`, `LeanIntegration`, `Charts`, `RunMat`, `EventReplay`
- `Trading`: `LiveData`, `StrategyRuns`, `RunPortfolio`, `RunLedger`, `PositionBlotter`, `OrderBook`, `PortfolioImport`, `TradingHours`, `Watchlist`
- `Data Operations`: `Provider`, `Symbols`, `Backfill`, `Schedules`, `Storage`, `PackageManager`, `DataExport`
- `Governance`: `GovernanceShell`, `FundAccounts`, `SecurityMaster`, `FundLedger`, `FundReconciliation`, `DataQuality`, `ProviderHealth`, `SystemHealth`, `Diagnostics`, `RetentionAssurance`, `AdminMaintenance`, `Settings`

`PositionBlotter` includes a selected-position review rail for action eligibility, long/short exposure totals, and compact selected-row previews before batch flatten or upsize actions are submitted.

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
