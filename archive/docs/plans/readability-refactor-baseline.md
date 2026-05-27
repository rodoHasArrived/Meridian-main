# Readability refactor baseline

_Date: 2026-03-20_

This document records the Phase 0 baseline so later refactor work can show objective progress rather than relying on subjective impressions.

## Measurement method

- Largest files were measured by line count across `src/**/*.cs`.
- WPF direct transport/mapping usage was counted by scanning `src/Meridian.Wpf/Views/**/*.cs` and `src/Meridian.Wpf/ViewModels/**/*.cs` for `HttpClient`, `JsonSerializer`, or `System.Text.Json`.
- Large provider adapters were counted as adapter-layer `.cs` files under `src/Meridian.Infrastructure/Adapters` with 500 or more lines.
- Test coverage areas were approximated from the current automated test project layout because no line-coverage artifact is checked into the repository.

## Largest source files

| Lines | File | Notes |
| ---: | --- | --- |
| 1655 | `src/Meridian.Wpf/Views/DataQualityPage.xaml.cs` | WPF data-quality page remains the largest readability hotspot. |
| 1563 | `src/Meridian.Ui.Services/Services/StorageOptimizationAdvisorService.cs` | Large service with advisory and orchestration responsibilities. |
| 1396 | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` | Large provider adapter mixing connection and payload concerns. |
| 1395 | `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` | Central composition root with broad registration responsibilities. |
| 1290 | `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | Large desktop workflow service. |
| 1246 | `src/Meridian.Application/Services/ConfigurationWizard.cs` | Configuration workflow and decision logic are still dense. |
| 1226 | `src/Meridian.Application/Services/AutoConfigurationService.cs` | Auto-configuration policy surface is broad. |
| 1153 | `src/Meridian.Storage/Services/StorageCatalogService.cs` | Storage querying and catalog responsibilities remain large. |
| 1129 | `src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs` | Provider implementation is large and likely split-worthy. |
| 1076 | `src/Meridian.Wpf/ViewModels/DataQualityViewModel.cs` | View-model still contains transport, mapping, filtering, and presentation logic. |

## Startup path responsibilities baseline

`src/Meridian/Program.cs` currently acts as the startup traffic controller. The observable responsibilities in the current startup path are:

1. Parse CLI arguments and resolve the config path.
2. Initialize logging before full configuration load.
3. Build deployment context and determine host mode behavior.
4. Load and prepare configuration through `ConfigurationService`.
5. Dispatch command-mode handlers through `CommandDispatcher`.
6. Start and stop web UI hosting for web/desktop modes.
7. Validate configuration and gate process startup.
8. Validate file permissions and optional schema compatibility.
9. Build DI-backed runtime services through `HostStartupFactory.Create(...)`.
10. Recover the write-ahead log, initialize storage/status writing, and continue into runtime-specific collection/backfill flows.

This confirms that startup refactoring should begin by separating policy decisions from runtime orchestration instead of making ad hoc file splits.

## WPF pages with direct HTTP/JSON logic

### Page code-behind files with direct HTTP/JSON usage: 4
- `src/Meridian.Wpf/Views/SetupWizardPage.xaml.cs`
- `src/Meridian.Wpf/Views/DataQualityPage.xaml.cs`
- `src/Meridian.Wpf/Views/DataBrowserPage.xaml.cs`
- `src/Meridian.Wpf/Views/TradingHoursPage.xaml.cs`

### View-model files with direct HTTP/JSON usage: 4
- `src/Meridian.Wpf/ViewModels/LiveDataViewerViewModel.cs`
- `src/Meridian.Wpf/ViewModels/DataQualityViewModel.cs`
- `src/Meridian.Wpf/ViewModels/OrderBookViewModel.cs`
- `src/Meridian.Wpf/ViewModels/ActivityLogViewModel.cs`

### Combined WPF transport/mapping hotspots: 8 files
This is the initial desktop extraction backlog for moving raw endpoint and JSON work behind services.

## Large provider adapters baseline

Large provider adapter files (500+ lines): **18**.

Top candidates:
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` — 1396 lines.
- `src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs` — 1129 lines.
- `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` — 922 lines.
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaHistoricalDataProvider.cs` — 800 lines.
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs` — 747 lines.
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs` — 616 lines.
- `src/Meridian.Infrastructure/Adapters/Core/GapAnalysis/DataGapAnalyzer.cs` — 613 lines.
- `src/Meridian.Infrastructure/Adapters/Core/GapAnalysis/DataQualityMonitor.cs` — 608 lines.

This backlog is large enough that provider refactoring should be sequenced by risk and coverage, not by raw file size alone.

## Current test coverage areas baseline

Automated test inventory by project:

| Test project | Approximate focus | Test files |
| --- | --- | ---: |
| `tests/Meridian.Tests` | Core application, pipeline, providers, integration, storage, architecture, config, commands | 177 |
| `tests/Meridian.Ui.Tests` | Shared desktop UI services and service bases | 52 |
| `tests/Meridian.Wpf.Tests` | Windows-only WPF services and view-model behavior | 19 |
| `tests/Meridian.Backtesting.Tests` | Backtesting engine behavior | 4 |
| `tests/Meridian.McpServer.Tests` | MCP server tools | 2 |

### Coverage observations
- Startup guardrails already exist for command dispatch, CLI mode resolution, and config validation.
- Shared desktop service guardrails already exist for connection lifecycle and data-quality service abstractions.
- WPF-specific tests exist, but they are Windows-only and therefore need deliberate maintenance to avoid regression blind spots on non-Windows contributors.
- Provider coverage is broad, but the number of oversized adapters suggests more characterization is still needed before major file splits.

## Suggested progress metrics for future phases

- Reduce the number of 1,000+ line source files.
- Reduce the number of WPF code-behind/view-model files that perform raw HTTP/JSON work.
- Reduce the count of 500+ line provider adapter files.
- Increase characterization test coverage specifically around startup seams, desktop mapping seams, and provider lifecycle transitions.
