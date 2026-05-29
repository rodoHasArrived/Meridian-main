# Meridian Desktop Workstation Screen Blueprint

**Date:** 2026-05-20  
**Status:** Draft blueprint  
**Audience:** WPF workstation implementation, shell/navigation, shared contract, and operator workflow owners

## Summary

This blueprint defines the next desktop-native workstation screens for Meridian's WPF application.
It assumes an explicit desktop-active direction for this workstream while preserving shared
contracts, read models, and workstation APIs so browser and desktop surfaces do not fork core
business behavior.

Programmatic tracking lives beside this plan in
`docs/plans/desktop-workstation-screen-blueprint.checklist.json`. Validate and summarize it with:

```powershell
python ./scripts/dev/desktop_screen_blueprint_checklist.py --summary
```

Each checklist row has a stable `desktop-screen-*` ID, root workspace, implementation status,
maturity label, source evidence paths, workflow coverage, and the narrow validation command to run
when that screen changes. Desktop workflow steps can reference those IDs with
`blueprintChecklistIds` so screenshot and manual automation stay traceable back to the blueprint.

Maturity labels use a controlled vocabulary: `Ready`, `Active but partial`,
`Fixture/demo-backed`, `UI shell only`, `TBI`, and `Needs verification`. Every maturity label must
name evidence from at least one permitted source: WPF view-model/service implementation under
`src/Meridian.Wpf/`, shared endpoint/read-model contracts under `src/Meridian.Ui.Services/` or
`src/Meridian.Ui.Shared/`, focused tests under `tests/`, acceptance evidence under `docs/status/`
or `artifacts/`, or screenshots under `docs/screenshots/desktop/`. Do not treat the existence of a
XAML page alone as maturity evidence.

The design target is an institutional workstation:

- workflow-centric
- operationally dense
- fast to scan
- keyboard-first
- audit-aware
- multi-workspace oriented
- ready for docking and future multi-monitor evolution

## Blueprint-to-Shell Reconciliation

Use this table before implementing any screen below. It reconciles the blueprint sections with the
current active WPF shell registry in `src/Meridian.Wpf/Features/` and
`src/Meridian.Wpf/Models/ShellNavigationCatalog*.cs`; those files remain the source of truth for
active routes and workspace ownership. When this table disagrees with the older shell-placement
summary below, prefer the current registry mapping here.

| Blueprint section | Active root workspace | Reconciliation status | Registry evidence / implementation note |
| --- | --- | --- | --- |
| 1. Security Master Workspace | `Data` | Active screen | Registered as `SecurityMaster` in the Data assurance lane. |
| 2. Live Market Data Workspace | `Trading` | Active screen | Registered as `LiveData` in the Trading market-feed lane. |
| 3. Watchlist Workspace | `Strategy` | Active screen | Registered as `Watchlist` under Strategy; current taxonomy keeps it out of the Data root. |
| 4. Historical Replay Workspace | `Strategy` | Active screen | Registered as `EventReplay` under Strategy analysis. |
| 5. Strategy Research Workspace | `Strategy` | Active capability inside another screen | Covered by the active Strategy shell plus `StrategyRuns`, `Charts`, `AdvancedAnalytics`, and related research pages rather than one page named Strategy Research. |
| 6. Strategy Builder Workspace | `Strategy` | Planned/TBI | No dedicated strategy-builder route is registered; adjacent active pages include `Backtest`, `QuantScript`, and `RunMat`. |
| 7. Paper Trading Workspace | `Trading` | Planned/TBI | No dedicated paper-trading screen is registered in the current Trading feature module. |
| 8. Live Trading Workspace | `Trading` | Planned/TBI | No dedicated live-trading execution ticket screen is registered; Trading currently exposes market feed, order book, position blotter, run risk, and hours. |
| 9. Portfolio Workspace | `Portfolio` | Active screen | Registered as `PortfolioShell` with account, aggregate, run, fund, accounts, import, and lending pages. |
| 10. Position & Exposure Workspace | `Portfolio` | Active capability inside another screen | Exposure review is covered by Portfolio pages (`AggregatePortfolio`, `RunPortfolio`, `FundPortfolio`); live position blotter is registered under Trading as `PositionBlotter`. |
| 11. Risk Management Workspace | `Trading` | Superseded by current shell taxonomy | The active registered route is `RunRisk` in the Trading execution lane, not a Settings-root risk workspace. |
| 12. Reconciliation Workspace | `Accounting` | Active screen | Registered as `FundReconciliation` in the Accounting fund-ops lane. |
| 13. Export & Reporting Workspace | `Reporting` | Active screen | Reporting owns `ReportingShell`, `FundReportPack`, `ReportRunStatus`, `AnalysisExport`, and `ExportPresets`; dataset export also exists as `DataExport` in Data. |
| 14. Backfill & Data Repair Workspace | `Data` | Active screen | Registered as `Backfill`; related repair/assurance work is covered by `DataQuality`, collection sessions, storage, and archive-health screens. |
| 15. System Health & Diagnostics Workspace | `Settings` | Active screen | Registered as `SystemHealth` and `Diagnostics` in Settings operations. |
| 16. Workflow Automation Workspace | `Settings` | Active screen | Registered as `WorkflowLibrary` in Settings workspace-layout support. |
| 17. Audit & Activity Workspace | `Accounting` | Active screen | Registered as `FundAuditTrail` in Accounting; workstation activity history is also registered as `ActivityLog` in Settings. |
| 18. Notification & Alert Center | `Settings` | Active screen | Registered as `NotificationCenter`, with `MessagingHub` as the related Settings notifications surface. |
| 19. Order Management Workspace | `Trading` | Planned/TBI | No dedicated order-management route is registered; current Trading routes stop at market feed, order book, position blotter, run risk, and trading hours. |
| 20. Market Depth / Order Book Workspace | `Trading` | Active screen | Registered as `OrderBook` in the Trading market-feed lane. |

## Scope

### In scope

- WPF-native workspace screens and deep pages
- MVVM shape for each screen
- reusable workstation controls and shell composition guidance
- shared service and contract seams that desktop should consume
- loading, empty, error, accessibility, and performance guidance
- phased implementation guidance for all requested screens

### Out of scope

- browser dashboard implementation
- provider adapter internals
- new execution/risk/accounting business rules
- persistence schema redesign
- full visual redesign of the existing WPF shell

### Assumptions

- Desktop remains grounded in `src/Meridian.Wpf/`, `src/Meridian.Ui.Services/`,
  `src/Meridian.Ui.Shared/`, and `src/Meridian.Contracts/`.
- `MainPage`, `ShellNavigationCatalog`, `PaneHostViewModel`, `WorkspaceShellPageBase`, and
  `NavigationService` remain the primary shell seams.
- New desktop work should be organized under `src/Meridian.Wpf/Features/<Workspace>/...` where
  possible, even if compatibility pages still live under the existing `Views/` and `ViewModels/`
  roots during transition.
- Shared posture, readiness, provider, reconciliation, execution, and reporting behavior belongs in
  shared contracts/services first, with WPF pages composing that state rather than inventing a
  second product logic lane.

## Common Desktop Architecture

### Shell placement

Map the requested screens to the current seven-workspace taxonomy:

| Root workspace | Primary screens |
| --- | --- |
| `Trading` | Live Market Data, Paper Trading, Live Trading, Order Management, Market Depth / Order Book |
| `Portfolio` | Portfolio, Position & Exposure |
| `Accounting` | Security Master, Reconciliation, Audit & Activity |
| `Reporting` | Export & Reporting |
| `Strategy` | Historical Replay, Strategy Research, Strategy Builder |
| `Data` | Watchlist, Backfill & Data Repair |
| `Settings` | Risk Management, System Health & Diagnostics, Workflow Automation, Notification & Alert Center |

Use compatibility aliases only for routing and migration, not as visible root labels.

### WPF implementation pattern

For every new desktop screen, prefer this shape:

- `Views/<Screen>Page.xaml`
- `Views/<Screen>Page.xaml.cs`
- `ViewModels/<Screen>ViewModel.cs`
- `Services/<Screen>PresentationService.cs`
- `Services/<Screen>WorkflowService.cs` when orchestration is screen-specific but still desktop-local
- `Features/<Workspace>/<Module>/<...>` when building a new workspace-owned module spine

Code-behind stays limited to:

- lifecycle (`Loaded`, `Unloaded`)
- AvalonDock / pane host wiring
- keyboard focus placement
- WPF-only resource application
- dispatcher-safe UI interop

Behavior belongs in:

- `BindableBase` view models
- shared read-model clients
- presentation services
- typed workstation API clients

### Shared reusable controls

Standardize these controls before adding many bespoke pages:

- `DenseDataTableControl`
- `SelectionInspectorPanel`
- `WorkspaceCommandToolbar`
- `StatusRibbonControl`
- `OperationalBadgeStrip`
- `AsyncContentHost`
- `SplitPaneWorkspaceHost`
- `TelemetrySparklineStrip`
- `LatencyBadgeControl`
- `AuditTrailPanel`
- `ProviderTrustStrip`
- `WorkflowStepStrip`
- `KeyValueInspectorGrid`
- `TimelinePanel`
- `OrderEntryTicketControl`
- `MarketDepthLadderControl`
- `DiagnosticsMetricTile`

### Common async/state model

Each screen view model should expose:

- `IsLoading`
- `IsRefreshing`
- `HasLoadedOnce`
- `ErrorState`
- `EmptyState`
- `StatusRibbon`
- `SelectedRowId`
- `ActiveInspectorTab`
- `FilterState`
- `CommandGroup`
- `KeyboardShortcuts`

Use explicit load states instead of null-driven rendering. Distinguish:

- first load
- refresh in place
- empty but valid
- degraded with stale data
- failed and retryable

### Common performance rules

- Use virtualization for all dense tables and tapes.
- Separate snapshot state from high-frequency delta state.
- Keep order-book, trade-tape, and quote updates allocation-light.
- Batch UI updates onto dispatcher-friendly intervals where visual fidelity allows it.
- Cap retained history per panel and expose operator-controlled time windows.
- Prefer immutable DTO snapshots plus VM-owned derived display models for scan-heavy panes.

### Common accessibility and keyboard rules

- `Ctrl+K`: command palette
- `Ctrl+1..7`: root workspace switch
- `F5`: refresh active screen
- `Alt+Left/Right`: inspector tab cycle
- `Alt+1..9`: toolbar actions
- arrow keys: dense-table navigation
- `Enter`: open details / default action
- `Space`: toggle selection / acknowledge action
- `Esc`: clear transient focus, search, or pending action

Every dense grid needs:

- stable row focus
- selected-row indicator
- keyboard-open detail panel
- visible shortcut hints for primary actions
- no hover-only state disclosure

## Cross-Screen Service and Contract Strategy

### Shared service lanes

- `IWorkstationOperatorInboxApiClient`
- `IWorkstationTradingReadinessApiClient`
- `ISecurityMasterOperatorWorkflowClient`
- `IWorkstationReconciliationApiClient`
- `IProviderRoutingApiClient`
- `IAccountManagementApiClient`
- `IWorkflowLibraryService`
- `IWorkstationTelemetryQueryService`
- `IReplaySessionService`
- `IOrderAuditQueryService`
- `IExportWorkflowService`

### Common DTO sources

Reuse before inventing:

- `Meridian.Contracts.Workstation.SecurityMaster*Dto`
- `Meridian.Contracts.Workstation.TradingOperatorReadiness*Dto`
- `Meridian.Contracts.Workstation.WorkflowLibrary*Dto`
- `Meridian.Contracts.Workstation.Reconciliation*Dto`
- `Meridian.Contracts.Workstation.StrategyRunReadModels.cs`
- `Meridian.Contracts.Api.LiveDataModels`
- `Meridian.Contracts.Api.StatusEndpointModels`
- `Meridian.Contracts.Api.ProviderRoutingApiModels`
- `Meridian.Contracts.Workstation.BrokerageSyncDtos`
- `Meridian.Contracts.FundStructure.*`
- `Meridian.Contracts.Ledger.*`

Where desktop needs richer display state, create WPF-local presentation models instead of mutating
contract DTOs.

## Workspace Blueprints

### 1. Security Master Workspace

- Screen purpose: Operate the institutional security master as a trust workbench for cross-provider identity, instrument definition, downstream impact, lots, schedules, and audit.
- Operator workflow: Search security -> filter results by asset class/status -> inspect trust posture -> review mappings/coverage/lineage -> apply override/review action -> drill into lots, cash flows, corporate actions, and downstream accounting or reconciliation impact.
- Layout structure: Three-column split. Left search/filter rail, center virtualized security grid, right inspector workspace with tabbed detail and lower lineage/timeline rail.
- Toolbar/actions: `Search`, `Clear`, `Open conflict queue`, `Refresh coverage`, `Validate metadata`, `Open downstream impact`, `Create override`, `Export packet`.
- Pane structure:
  - Left: search, asset class filter, provider filter, validation status, issue chips
  - Center: security grid
  - Right top: detail tabs
  - Right bottom: audit trail / lineage / provider coverage
- Tables/grids:
  - security result grid
  - provider mapping grid
  - open lots grid
  - corporate actions grid
  - cash flow schedule ladder
  - factor schedule grid
- Detail panels:
  - `Overview`
  - `Identity & Provenance`
  - `Economic Definition`
  - `Mappings & Coverage`
  - `Lots & Holdings`
  - `Cash Flows`
  - `Corporate Actions`
  - `Audit History`
- Suggested controls: `SecuritySearchStrip`, `AssetClassBadgeStrip`, `MappingHealthBadge`, `LotsInspectorGrid`, `CashFlowTimelineControl`, `DownstreamImpactPanel`.
- Suggested MVVM structure:
  - `SecurityMasterWorkspacePage`
  - `SecurityMasterWorkspaceViewModel`
  - `SecurityMasterOverviewViewModel`
  - `SecurityMasterIdentityPanelViewModel`
  - `SecurityMasterLineagePanelViewModel`
  - `SecurityMasterPresentationService`
- Suggested services:
  - existing `SecurityMasterOperatorWorkflowClient`
  - `ISecurityValidationGateService`
  - `SecurityMasterLineagePresentationService`
- Suggested DTO/domain usage:
  - `SecurityMasterWorkstationDto`
  - `SecurityIdentityDrillInDto`
  - `SecurityMasterTrustSnapshotDto`
  - `SecurityMasterProviderSymbolMappingDto`
  - `SecurityMasterConflictAssessmentDto`
  - `SecurityMasterDownstreamImpactDto`
- Loading/error/empty states:
  - loading: centered progress strip plus retained last result summary
  - empty: explain whether query empty, no match, or runtime unavailable
  - error: typed conflict/validation details with retry and diagnostics link
- Accessibility considerations: search should own initial focus, grid must expose selected security context to inspector header, override dialogs require visible rationale text.
- Performance considerations: virtualize security, lots, and corporate action grids; lazily load asset-class-specific tabs; batch schedule visualization updates.
- Future extensibility ideas: issuer hierarchy, covenant packs, reference-document attachments, pricing source waterfall, model eligibility badges.
- Suggested implementation phases:
  1. Search/grid/overview shell
  2. trust posture + mappings + lots
  3. schedules + corporate actions + lineage
  4. override/audit workflow integration
- Current implementation note (2026-05-20): Phase 1 has started in `src/Meridian.Wpf/Views/SecurityMasterPage.xaml` and `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs` with a dedicated filter rail, local asset-class/provider filtering, and mapping-gap projection over the loaded result set.

### 2. Live Market Data Workspace

- Screen purpose: Monitor real-time quotes, trades, BBO, spreads, provider posture, and latency for one or many symbols.
- Operator workflow: Enter symbol set -> pin symbols into watch panes -> inspect quote ladder and trade tape -> watch latency/throughput/connection quality -> branch to order book, replay, watchlist, or trade ticket.
- Layout structure: Top command ribbon, center multi-symbol quote matrix, right selected-symbol detail, lower split tape/latency panel.
- Toolbar/actions: `Add symbol`, `Remove symbol`, `Pin set`, `Open order book`, `Open replay`, `Open ticket`, `Pause updates`, `Reset layout`.
- Pane structure:
  - top: symbol entry and market status strip
  - center left: multi-symbol snapshot table
  - center right: selected symbol quote ladder / provider strip
  - bottom left: trade tape
  - bottom right: latency and throughput diagnostics
- Tables/grids:
  - live quote matrix
  - BBO ladder
  - trade tape
  - provider source detail
- Detail panels:
  - symbol summary
  - venue/source details
  - spread analytics
  - tick health and latency
- Suggested controls: `SymbolEntryBox`, `QuoteMatrixGrid`, `TradeTapeControl`, `ProviderLatencyStrip`, `MarketStatusBadge`, `TickRateBadge`.
- Suggested MVVM structure:
  - `LiveMarketDataWorkspacePage`
  - `LiveMarketDataWorkspaceViewModel`
  - `LiveQuoteRowViewModel`
  - `LiveMarketDataDiagnosticsViewModel`
  - `LiveMarketDataPresentationService`
- Suggested services:
  - `ILiveDataService`
  - `IProviderHealthSnapshotService`
  - `IMarketStatusService`
  - `ILiveDataBufferCoordinator`
- Suggested DTO/domain usage:
  - `SessionStatsDto`
  - quote/trade/depth models from `LiveDataModels`
  - `ProviderLatencySummaryDto`
  - `ConnectionHealthSnapshotDto`
- Loading/error/empty states:
  - loading: last symbol set placeholder with retained market-status ribbon
  - empty: no symbol selected, offer starter set/watchlist import
  - error: per-symbol degraded badge plus global connection diagnostic summary
- Accessibility considerations: arrow-key traversal across matrix cells, selected-symbol summary announced in detail panel header, no color-only spread state.
- Performance considerations: UI virtualization, ring-buffer-backed tape, throttled sparkline refresh, pooled row view models for high-frequency symbol sets.
- Future extensibility ideas: multi-monitor pop-out quote panes, venue-level routing visibility, micro-burst anomaly detection.
- Suggested implementation phases:
  1. single-symbol ladder+tape
  2. multi-symbol matrix
  3. provider and latency diagnostics
  4. replay/order-book/trading handoffs

### 3. Watchlist Workspace

- Screen purpose: Curate operator watch universes that drive live data, alerts, and research handoffs.
- Operator workflow: Load saved list -> filter/sort symbols -> inspect selected symbol posture -> bulk add/remove/import -> launch live quotes or alerts.
- Layout structure: Left list library, center symbol table, right symbol inspector and action deck.
- Toolbar/actions: `New list`, `Rename`, `Import`, `Export`, `Pin`, `Bulk add`, `Remove selected`, `Open live quotes`.
- Pane structure:
  - left: watchlist library and search
  - center: symbol membership grid
  - right: symbol detail, recent quote summary, alert eligibility
- Tables/grids:
  - watchlist library grid
  - member symbols grid
- Detail panels:
  - symbol overview
  - list metadata
  - related alerts and provider coverage
- Suggested controls: `PinnedListStrip`, `WatchlistLibraryTree`, `SymbolMembershipGrid`, `ListInspectorPanel`.
- Suggested MVVM structure:
  - `WatchlistWorkspacePage`
  - `WatchlistWorkspaceViewModel`
  - existing `WatchlistViewModel` can be adapted into a workspace-owned composition model
- Suggested services:
  - existing watchlist service
  - `IWatchlistImportExportService`
  - `IQuoteSnapshotLookupService`
- Suggested DTO/domain usage: existing watchlist display models plus live quote snapshot DTOs.
- Loading/error/empty states: explain empty library vs filtered miss; offer starter packs and recent live-data handoff.
- Accessibility considerations: keyboard reorder/pin actions, visible selected-list context, import/export status in live region.
- Performance considerations: lightweight in-memory filter, defer quote enrichment until row selected or visible.
- Future extensibility ideas: watchlist inheritance, role-shared lists, event-driven symbol packs.
- Suggested implementation phases:
  1. library + membership table
  2. symbol inspector + quote snapshot
  3. import/export/bulk workflows
  4. alerts/live-data integration

### 4. Historical Replay Workspace

- Screen purpose: Reconstruct historical market/session behavior for trust verification, strategy review, and execution analysis.
- Operator workflow: Select replay source/session -> define time window and symbols -> run replay -> inspect synchronized tape/depth/events -> jump to paper-trading or audit evidence.
- Layout structure: Left configuration pane, center replay canvas, lower timeline/events, right synchronized inspectors.
- Toolbar/actions: `Open replay`, `Run`, `Pause`, `Step`, `Speed`, `Jump to time`, `Export evidence`, `Open paper session`.
- Pane structure:
  - left: session/replay source, symbols, time controls
  - center: replay viewport
  - right: quote/depth/order/fill inspectors
  - bottom: event timeline and anomalies
- Tables/grids:
  - replay event grid
  - execution event grid
  - anomaly grid
- Detail panels:
  - selected timestamp snapshot
  - source health
  - replay verification posture
- Suggested controls: `ReplayTransportBar`, `TimeCursorStrip`, `EventTimelineControl`, `ReplaySnapshotInspector`.
- Suggested MVVM structure:
  - `HistoricalReplayWorkspacePage`
  - `HistoricalReplayWorkspaceViewModel`
  - `ReplayTransportViewModel`
  - `ReplayEventInspectorViewModel`
- Suggested services:
  - `IReplaySessionService`
  - `IExecutionReplayEvidenceService`
  - `IHistoricalDataQueryService`
- Suggested DTO/domain usage:
  - execution session replay DTOs
  - strategy run continuity/readiness DTOs
  - live data historical event models
- Loading/error/empty states: no replay selected, unavailable artifacts, verification mismatch, source gap.
- Accessibility considerations: full transport keyboard support; current replay cursor and speed visible without motion-only feedback.
- Performance considerations: paged event retrieval, decoupled transport clock from UI rendering, capped in-memory depth windows.
- Future extensibility ideas: multi-session comparison, synchronized strategy output overlay, trader training mode.
- Suggested implementation phases:
  1. transport + event timeline
  2. synchronized quote/trade/depth inspectors
  3. execution/paper-session correlation
  4. anomaly and evidence export

### 5. Strategy Research Workspace

- Screen purpose: Research and compare strategy runs, promotion candidates, and run evidence.
- Operator workflow: filter run library -> inspect selected run -> compare runs -> review preflight/promotability -> send to builder or paper-trading.
- Layout structure: Upper filter and summary strip, center run library table, right run detail, lower compare and evidence tabs.
- Toolbar/actions: `Refresh`, `Clear filters`, `Compare selected`, `Open builder`, `Open replay`, `Promote to paper`, `Export run packet`.
- Pane structure:
  - top: research cycle hero and filters
  - center: strategy run library
  - right: selected run summary
  - bottom: comparison, outputs, evidence, promotion history
- Tables/grids: run library, compare grid, signal/event output grid.
- Detail panels: run metadata, metrics, validation, linked portfolio, evidence.
- Suggested controls: `RunSummaryHero`, `RunComparisonGrid`, `PromotionHistoryPanel`, `ExperimentTagStrip`.
- Suggested MVVM structure:
  - `StrategyResearchWorkspacePage`
  - `StrategyResearchWorkspaceViewModel`
  - existing `StrategyRunBrowserViewModel` and `ResearchWorkspaceShellViewModel` feed composition
  - `StrategyRunComparisonPanelViewModel`
- Suggested services:
  - `StrategyRunReadService`
  - `BacktestPreflightService`
  - `IWorkflowLibraryService`
- Suggested DTO/domain usage:
  - `StrategyRunContinuityDto`
  - `RunComparisonDto`
  - `BacktestPreflightReportV2Dto`
  - `ResearchBriefingDto`
- Loading/error/empty states: empty library, filter-hidden runs, unavailable outputs, stale promotion evidence.
- Accessibility considerations: compare selection keyboard flow, visible current experiment context, no hidden compare locks.
- Performance considerations: lazy detail hydration, background compare precomputation, reuse shared chart/output controls.
- Future extensibility ideas: experiment notebooks, benchmark overlays, factor attribution views.
- Suggested implementation phases:
  1. run library + detail
  2. compare and promotion evidence
  3. builder and replay handoff
  4. advanced outputs and attribution

### 6. Strategy Builder Workspace

- Screen purpose: Provide a desktop-native quantitative workflow canvas for defining multi-cell strategy logic with sequential execution and inspectable runtime state.
- Operator workflow: create/open strategy -> add cells -> wire variables/dependencies -> validate -> preview outputs -> execute test run -> inspect diagnostics -> save reusable components/macros.
- Layout structure: Left component palette and variable explorer, center cell canvas, right validation/inspector, lower outputs/dependency graph/runtime diagnostics.
- Toolbar/actions: `New strategy`, `Open`, `Save`, `Validate`, `Run preview`, `Add cell`, `Extract macro`, `Graph view`, `Publish template`.
- Pane structure:
  - left: block palette, variables, data sources
  - center: ordered cell canvas
  - right: selected cell inspector and validation list
  - bottom: output preview, dependency graph, runtime diagnostics
- Tables/grids:
  - variable explorer
  - validation issue grid
  - execution result grid
- Detail panels:
  - cell properties
  - expression editor
  - dependency inspector
  - preview result panel
- Suggested controls: `StrategyCellCanvas`, `ExecutionOrderRail`, `VariableExplorerTree`, `DependencyGraphPanel`, `ExpressionEditorHost`, `ValidationIssueList`.
- Suggested MVVM structure:
  - `StrategyBuilderWorkspacePage`
  - `StrategyBuilderWorkspaceViewModel`
  - `StrategyCellViewModel`
  - `VariableExplorerViewModel`
  - `StrategyBuilderDiagnosticsViewModel`
  - `StrategyBuilderPresentationService`
- Suggested services:
  - `IStrategyDefinitionCompiler`
  - `IStrategyBuilderValidationService`
  - `IStrategyPreviewExecutionService`
  - `ISecurityMasterLookupService`
  - `IFundamentalDataBindingService`
- Suggested DTO/domain usage:
  - strategy metadata and parameter schema contracts
  - security master, market data, and factor DTOs
  - workflow library DTOs for reusable macros/presets
- Loading/error/empty states: no strategy loaded, invalid cell references, runtime unavailable, partial preview output.
- Accessibility considerations: keyboard cell creation and reordering, visible dependency/focus state, validation linked to selected cell.
- Performance considerations: diff-based graph recompute, deferred preview execution, editor virtualization for long strategies.
- Future extensibility ideas: collaborative review packets, version diff, strategy governance gating.
- Suggested implementation phases:
  1. ordered cell canvas + expression editor
  2. variables and dependency graph
  3. validation and preview execution
  4. macros, reusable templates, governance hooks

### 7. Paper Trading Workspace

- Screen purpose: Execute and supervise paper-session orders with institutional readiness and evidence posture.
- Operator workflow: restore/create paper session -> review readiness -> stage order -> acknowledge control warnings -> submit -> inspect fills, positions, replay verification, and promotion status.
- Layout structure: top readiness ribbon, left order entry + strategy linkage, center blotter/positions, right execution inspector, bottom audit and replay verification.
- Toolbar/actions: `Create session`, `Restore`, `Verify replay`, `Stage order`, `Cancel`, `Close session`, `Open audit`, `Promote review`.
- Pane structure:
  - top: readiness and trust gates
  - left: order ticket and strategy context
  - center top: orders blotter
  - center bottom: paper positions
  - right: selected order/fill/session inspector
  - bottom rail: audit/replay evidence
- Tables/grids: active orders, fills, positions, operator work items.
- Detail panels: order lifecycle, control evidence, strategy linkage, replay freshness.
- Suggested controls: `TradingReadinessRibbon`, `PaperOrderEntryTicket`, `ExecutionLifecycleTimeline`, `ReplayFreshnessPanel`.
- Suggested MVVM structure:
  - `PaperTradingWorkspacePage`
  - `PaperTradingWorkspaceViewModel`
  - `PaperOrderTicketViewModel`
  - `PaperSessionInspectorViewModel`
- Suggested services:
  - `IWorkstationTradingReadinessApiClient`
  - `IPaperOrderExecutionService`
  - `IExecutionSessionReplayService`
- Suggested DTO/domain usage:
  - `TradingOperatorReadinessDto`
  - `FundAccountBrokerageOrderDto`
  - `FundAccountBrokerageFillDto`
  - `StrategyRunReviewPacketDto`
- Loading/error/empty states: no active paper session, readiness blocked, stale replay verification, broker simulation unavailable.
- Accessibility considerations: explicit confirmation text for mutating actions, persistent disabled reasons, keyboard-first ticket submission.
- Performance considerations: incremental blotter refresh, bounded fill history, replay evidence loaded on selection.
- Future extensibility ideas: approval routing, team simulation mode, market replay join.
- Suggested implementation phases:
  1. readiness + session shell
  2. ticket + blotter + fills
  3. replay verification and audit
  4. promotion workflow integration

### 8. Live Trading Workspace

- Screen purpose: Supervise live execution with stricter trust, routing, broker, and override posture than paper.
- Operator workflow: confirm live posture -> inspect accounts/brokers -> stage or route strategy-driven orders -> supervise fills and overrides -> inspect circuit breakers and approvals.
- Layout structure: paper-trading layout plus stronger account/broker/risk rails and live-mode status band.
- Toolbar/actions: `Live mode review`, `Open broker links`, `Stage order`, `Pause strategy`, `Cancel all`, `Open circuit breakers`, `Export audit`.
- Pane structure:
  - top: live mode, environment, sign-off, and circuit breaker strip
  - left: account/broker/ticket stack
  - center: live orders and fills
  - right: execution/risk/audit inspector
  - bottom: manual overrides and approval history
- Tables/grids: live orders, live fills, broker sessions, override log.
- Detail panels: route diagnostics, exposure after order, approval evidence, failure diagnostics.
- Suggested controls: `EnvironmentModeRibbon`, `BrokerRouteInspector`, `CircuitBreakerPanel`, `ApprovalHistoryStrip`.
- Suggested MVVM structure:
  - `LiveTradingWorkspacePage`
  - `LiveTradingWorkspaceViewModel`
  - `LiveExecutionInspectorViewModel`
  - `BrokerRoutePanelViewModel`
- Suggested services:
  - `IOrderGateway`
  - `IRiskRuleEvaluationService`
  - `IBrokerageConnectionStatusService`
  - `IExecutionAuditQueryService`
- Suggested DTO/domain usage:
  - brokerage sync/order/fill DTOs
  - trading control readiness DTOs
  - provider trust and route preview DTOs
- Loading/error/empty states: live mode not enabled, no connected broker, blocked control gate, stale account sync.
- Accessibility considerations: visible live-mode acknowledgement, no hidden destructive actions, strong focus order between ticket and blotter.
- Performance considerations: incremental order updates, independent broker health poll cadence, bounded audit pane.
- Future extensibility ideas: cross-broker smart routing, execution quality scorecards, live surveillance hooks.
- Suggested implementation phases:
  1. live posture + broker/account strip
  2. live blotter and route diagnostics
  3. overrides, approvals, and circuit breakers
  4. execution quality analytics

### 9. Portfolio Workspace

- Screen purpose: View fund, account, household, and aggregate portfolio state with continuity to strategy, trading, accounting, and reporting.
- Operator workflow: select operating context -> review balances/positions/performance -> inspect account health -> drill into ledger or reconciliation -> export or branch to exposure analysis.
- Layout structure: left context/account tree, center portfolio summary and holdings, right account inspector, lower performance/cash-flow tabs.
- Toolbar/actions: `Refresh`, `Select context`, `Open brokerage sync`, `Open ledger`, `Open reconciliation`, `Export holdings`.
- Pane structure: context rail, holdings table, inspector, lower analytics tabs.
- Tables/grids: holdings, cash balances, brokerage households/accounts, performance points.
- Detail panels: selected account, linked strategy runs, readiness issues, last sync evidence.
- Suggested controls: `PortfolioContextTree`, `HoldingsGrid`, `AccountReadinessBadgeStrip`, `PerformanceCurvePanel`.
- Suggested MVVM structure:
  - `PortfolioWorkspacePage`
  - `PortfolioWorkspaceViewModel`
  - compose existing `AccountPortfolioViewModel`, `AggregatePortfolioViewModel`, `FundAccountsViewModel`
- Suggested services:
  - account management API clients
  - brokerage sync view service
  - portfolio performance presentation service
- Suggested DTO/domain usage:
  - `FundAccountsDto`
  - `FundAccountBrokerageSyncActivityDto`
  - `FundAccountBrokeragePositionDto`
  - `FundAccountBrokerageBalanceSnapshotDto`
- Loading/error/empty states: no context selected, disconnected account sync, no positions, stale balance snapshot.
- Accessibility considerations: selected account context should drive all lower panes; clear announce when switching between aggregate and account scopes.
- Performance considerations: cache summary cards, virtualize holdings, background performance series load.
- Future extensibility ideas: sleeve/vehicle switchers, collateral and margin overlays, funding ladder.
- Suggested implementation phases:
  1. context + holdings + inspector
  2. brokerage sync/readiness integration
  3. performance and cash-flow tabs
  4. accounting and reporting handoffs

### 10. Position & Exposure Workspace

- Screen purpose: Analyze open positions and desk/fund exposure across dimensions relevant to trading, risk, and portfolio review.
- Operator workflow: filter position set -> inspect aggregate or account exposure -> drill into instrument/sector/factor concentration -> open risk or order workflows.
- Layout structure: top aggregate exposure ribbon, center position blotter, right exposure inspector, lower heatmaps/distributions.
- Toolbar/actions: `Refresh`, `Reset filters`, `Aggregate by`, `Open risk`, `Open order history`, `Export exposure`.
- Pane structure: blotter, concentration panels, selected position inspector, lower factor or sector tabs.
- Tables/grids: position blotter, exposure summary grid, concentration grid.
- Detail panels: selected position, Greeks/sensitivity, linked orders/fills, financing/cash impact.
- Suggested controls: `ExposureSummaryRibbon`, `ConcentrationMatrix`, `PositionInspectorPanel`, `SensitivityChipStrip`.
- Suggested MVVM structure:
  - `PositionExposureWorkspacePage`
  - `PositionExposureWorkspaceViewModel`
  - existing `PositionBlotterViewModel` as basis
  - `ExposureAggregationPanelViewModel`
- Suggested services:
  - portfolio exposure calculation service
  - order history query service
  - factor exposure projection service
- Suggested DTO/domain usage: holdings/position DTOs, margin snapshots, option Greeks, run linkage models.
- Loading/error/empty states: no open positions, filtered miss, missing sensitivity data.
- Accessibility considerations: group changes announced, clear row-to-inspector linkage, keyboard aggregate switch.
- Performance considerations: incremental aggregation recompute, lazy drill-in expansion, no per-row synchronous lookups.
- Future extensibility ideas: scenario shock overlays, hedge proposals, historical exposure animation.
- Suggested implementation phases:
  1. blotter + aggregate exposure
  2. concentration and factor drill-ins
  3. strategy/order linkage
  4. scenario and hedge overlays

### 11. Risk Management Workspace

- Screen purpose: Present real-time and historical risk posture, rule violations, approvals, and escalation state.
- Operator workflow: review top-of-book risk -> inspect violations -> compare against history -> acknowledge or escalate -> trace to positions/orders/accounts.
- Layout structure: top risk status ribbon, left rule tree and filters, center violations and metrics, right approval/escalation inspector, lower historical comparison tabs.
- Toolbar/actions: `Refresh`, `Run stress`, `Open limit config`, `Acknowledge`, `Escalate`, `Export risk packet`.
- Pane structure: rule navigator, current risk summary, violations grid, inspector, historical comparison strip.
- Tables/grids: rule violations, limit utilization, drawdown ladder, stress scenario results.
- Detail panels: selected violation, approval chain, linked positions/orders, override rationale.
- Suggested controls: `RiskStatusRibbon`, `LimitUtilizationGrid`, `ViolationInspectorPanel`, `StressScenarioResultsGrid`.
- Suggested MVVM structure:
  - `RiskManagementWorkspacePage`
  - `RiskManagementWorkspaceViewModel`
  - `RiskViolationInspectorViewModel`
  - `RiskPresentationService`
- Suggested services:
  - `IRiskRuleEvaluationService`
  - `IRiskTelemetryService`
  - `IRiskApprovalWorkflowService`
- Suggested DTO/domain usage: risk rule projections, exposure and margin DTOs, operator work item DTOs.
- Loading/error/empty states: no risk feed yet, stale telemetry, no current violations, stress engine unavailable.
- Accessibility considerations: visible severity hierarchy, non-color violation emphasis, approval status text.
- Performance considerations: split fast summary from slower scenario panels, background historical load, capped stress result history.
- Future extensibility ideas: VaR engine, intraday shock presets, policy diff and approval archive.
- Suggested implementation phases:
  1. live status + violations
  2. approvals/escalation
  3. historical comparisons
  4. stress scenarios and config editing

### 12. Reconciliation Workspace

- Screen purpose: Manage accounting, position, cash, custodian, factor, and trade reconciliation through break queues and evidence packets.
- Operator workflow: load reconciliation run -> triage breaks -> inspect expected vs observed -> assign rationale or resolution -> approve or export packet.
- Layout structure: left run/filter rail, center break queue, right break inspector, lower evidence/journal preview tabs.
- Toolbar/actions: `Refresh queue`, `Reset filters`, `Open calibration`, `Assign`, `Resolve`, `Approve`, `Export packet`.
- Pane structure: reconciliation scope rail, break grid, selected break detail, evidence/journal tabs.
- Tables/grids: break queue, expected vs actual diff grid, supporting evidence grid, action history grid.
- Detail panels: variance explanation, security coverage issue, expected journal preview, owner/sign-off history.
- Suggested controls: `BreakQueueGrid`, `VarianceInspectorPanel`, `EvidencePacketPanel`, `JournalPreviewGrid`.
- Suggested MVVM structure:
  - `ReconciliationWorkspacePage`
  - `ReconciliationWorkspaceViewModel`
  - extend existing `FundLedgerViewModel` and reconciliation workbench seams
  - `ReconciliationEvidencePanelViewModel`
- Suggested services:
  - `IWorkstationReconciliationApiClient`
  - `FundReconciliationWorkbenchService`
  - `IReconciliationApprovalWorkflowService`
- Suggested DTO/domain usage:
  - `ReconciliationBreakDto`
  - `ReconciliationCalibrationSummaryDto`
  - `ExpectedJournalPreviewDto`
  - `SecurityMasterAccountingIssueDto`
  - account reconciliation DTOs
- Loading/error/empty states: no active run, no breaks, filtered miss, calibration unavailable.
- Accessibility considerations: selected break reflected in inspector heading, action buttons tied to visible rationale requirements.
- Performance considerations: paged break loads, lazy evidence detail, separate refresh cadence for summary vs detail.
- Future extensibility ideas: batch resolution suggestions, reconciliation packet signatures, close-lane integration.
- Suggested implementation phases:
  1. break queue + detail
  2. expected vs actual and journal preview
  3. approvals and packet export
  4. cross-domain reconciliation variants

### 13. Export & Reporting Workspace

- Screen purpose: Coordinate operational exports, governed report packs, presets, artifact readiness, and delivery status.
- Operator workflow: choose report or export preset -> validate scope -> preview output -> run export -> inspect artifact status -> hand off to governance or evidence.
- Layout structure: left preset/report navigator, center export job grid, right selected export inspector, lower preview/artifact tabs.
- Toolbar/actions: `New preset`, `Save`, `Validate`, `Run export`, `Cancel`, `Open artifact`, `Open evidence`, `Publish review`.
- Pane structure: preset tree, export jobs, preview panel, artifact and status panels.
- Tables/grids: export queue, artifact file grid, report-pack task grid.
- Detail panels: selected preset, export request scope, artifact manifest, report-pack readiness.
- Suggested controls: `ExportPresetLibrary`, `ExportJobQueueGrid`, `ArtifactManifestPanel`, `ReportPackStatusRibbon`.
- Suggested MVVM structure:
  - `ExportReportingWorkspacePage`
  - `ExportReportingWorkspaceViewModel`
  - compose existing `DataExportViewModel`, `AnalysisExportViewModel`, `ExportPresetsViewModel`
- Suggested services:
  - export workflow service
  - evidence artifact store client
  - report-pack validation service
- Suggested DTO/domain usage: export DTOs, artifact manifest DTOs, governed report pack readiness DTOs.
- Loading/error/empty states: no preset selected, no artifacts yet, export failed, report pack blocked by missing evidence.
- Accessibility considerations: export progress visible as text not spinner-only, queued/cancelled/completed statuses keyboard readable.
- Performance considerations: background job polling, artifact preview on demand, bounded manifest row count.
- Future extensibility ideas: publish approvals, recipient routing, schedule integration.
- Suggested implementation phases:
  1. preset and queue shell
  2. artifact status and preview
  3. report-pack readiness integration
  4. publish and scheduling

### 14. Backfill & Data Repair Workspace

- Screen purpose: Operate historical data recovery, gap repair, provider backfill, export repair, and package validation.
- Operator workflow: select provider/symbol/date scope -> preview gap -> start backfill -> inspect progress/errors -> validate repaired output -> branch to data browser or quality review.
- Layout structure: left request builder, center repair queue, right selected job inspector, lower preview/coverage tabs.
- Toolbar/actions: `Preview`, `Run backfill`, `Resume`, `Cancel`, `Validate package`, `Open coverage`, `Open data browser`.
- Pane structure: request form, queue grid, progress inspector, coverage preview and error detail.
- Tables/grids: backfill queue, provider availability grid, coverage gaps grid, repair output sample grid.
- Detail panels: selected job status, checkpoint history, provider trust, resulting files/packages.
- Suggested controls: `BackfillRequestPanel`, `BackfillQueueGrid`, `CoverageGapGrid`, `RepairDiagnosticsPanel`.
- Suggested MVVM structure:
  - `BackfillDataRepairWorkspacePage`
  - `BackfillDataRepairWorkspaceViewModel`
  - extend existing `BackfillViewModel`, `StorageViewModel`, `DataQualityViewModel`
- Suggested services:
  - `BackfillCoordinator`
  - `IHistoricalDataProvider`
  - package validation service
  - storage analytics service
- Suggested DTO/domain usage:
  - `BackfillRequestDto`
  - provider routing/provider trust DTOs
  - package validation DTOs
  - data quality/coverage projections
- Loading/error/empty states: no provider available, invalid date range, empty preview, interrupted repair with resume path.
- Accessibility considerations: status and progress must be text-visible, queue row selection stable during updates.
- Performance considerations: incremental progress publishing, sampled preview rows, no full tree scan on every refresh.
- Future extensibility ideas: repair recipes, scheduled backfill windows, dataset lineage diffs.
- Suggested implementation phases:
  1. request + queue
  2. preview and diagnostics
  3. package validation and storage linkage
  4. automation recipes

### 15. System Health & Diagnostics Workspace

- Screen purpose: Supervise provider, connectivity, queue, persistence, CPU, memory, thread, cache, replay, and export health with drill-down diagnostics.
- Operator workflow: scan top-line health -> inspect degraded subsystem -> correlate with workflow failures -> open dependency or historical trend views -> hand off to support/repair.
- Layout structure: top triage ribbon, center subsystem health matrix, right selected subsystem inspector, lower telemetry and dependency mapping tabs.
- Toolbar/actions: `Refresh`, `Open logs`, `Open provider health`, `Open queue metrics`, `Capture snapshot`, `Open dependency graph`.
- Pane structure: health summary, subsystem table, inspector, lower historical charts and event correlation.
- Tables/grids: subsystem status grid, recent incidents grid, dependency map table, metric history table.
- Detail panels: selected subsystem metrics, dependencies, recent correlated failures, recommended next action.
- Suggested controls: `SubsystemHealthMatrix`, `MetricTrendPanel`, `DependencyMapControl`, `IncidentCorrelationGrid`.
- Suggested MVVM structure:
  - `SystemHealthDiagnosticsWorkspacePage`
  - `SystemHealthDiagnosticsWorkspaceViewModel`
  - extend existing `SystemHealthViewModel` and `ProviderHealthViewModel`
  - `DiagnosticsDependencyPanelViewModel`
- Suggested services:
  - status API clients
  - `IHealthSnapshotAggregationService`
  - telemetry query service
  - log summary service
- Suggested DTO/domain usage:
  - `BackpressureStatusDto`
  - `ProviderLatencySummaryDto`
  - `ConnectionHealthSnapshotDto`
  - error stats and metrics DTOs
- Loading/error/empty states: pending first scan, partial subsystem outage, telemetry unavailable.
- Accessibility considerations: use structured labels for health states; dependency graph needs table fallback.
- Performance considerations: different polling intervals per subsystem; chart downsampling; preserve last healthy snapshot for degraded views.
- Future extensibility ideas: alert rules editor, anomaly detection, node-level service map.
- Suggested implementation phases:
  1. triage ribbon + subsystem matrix
  2. inspector and trend panels
  3. dependency and workflow correlation
  4. alert configuration

### 16. Workflow Automation Workspace

- Screen purpose: Expose named operator workflows, presets, command recipes, and multi-step operational playbooks.
- Operator workflow: browse workflow library -> inspect steps, prerequisites, and outputs -> launch workflow -> monitor progress -> reopen result packet.
- Layout structure: left workflow catalog, center selected workflow detail, right run history and active execution inspector.
- Toolbar/actions: `Run workflow`, `Dry run`, `Save preset`, `Duplicate`, `Archive`, `Open result`, `Open source workspace`.
- Pane structure: catalog tree, workflow detail, active run panel, run history.
- Tables/grids: workflow definitions, workflow steps, run history, artifact outputs.
- Detail panels: selected workflow overview, prerequisite checks, step diagnostics, output manifest.
- Suggested controls: `WorkflowCatalogTree`, `WorkflowStepList`, `WorkflowRunTimeline`, `PrerequisiteStatusPanel`.
- Suggested MVVM structure:
  - `WorkflowAutomationWorkspacePage`
  - `WorkflowAutomationWorkspaceViewModel`
  - leverage existing `WorkflowLibraryViewModel`
  - `WorkflowRunInspectorViewModel`
- Suggested services:
  - `IWorkflowLibraryService`
  - workflow preset store
  - runbook execution service
- Suggested DTO/domain usage:
  - `WorkflowLibraryDto`
  - `WorkflowDefinitionDto`
  - `WorkflowActionDto`
  - `WorkflowPresetLibraryDto`
- Loading/error/empty states: no workflows available, stale preset, unavailable dependency for selected workflow.
- Accessibility considerations: step-by-step keyboard activation; live run state announced incrementally.
- Performance considerations: lightweight catalog load, deferred artifact detail, bounded history.
- Future extensibility ideas: chained workflows, approvals, background scheduling, operator ownership.
- Suggested implementation phases:
  1. workflow catalog + detail
  2. run history and outputs
  3. dry-run and prerequisite checks
  4. scheduling and approvals

### 17. Audit & Activity Workspace

- Screen purpose: Provide an operator-facing consolidated history of actions, evidence, system events, and user activity.
- Operator workflow: filter activity stream -> inspect event -> trace actor/object/outcome -> export evidence -> hand off to notification, order, reconciliation, or diagnostics surfaces.
- Layout structure: top filter and severity strip, center activity grid, right detail inspector, lower raw payload and linked object tabs.
- Toolbar/actions: `Refresh`, `Clear filters`, `Export`, `Open linked object`, `Open diagnostics`, `Retain snapshot`.
- Pane structure: filter strip, activity grid, selected event inspector, lower payload and related-object tabs.
- Tables/grids: activity log, linked entities, event attachments.
- Detail panels: actor, timestamp, object, before/after, evidence links.
- Suggested controls: `ActivitySeverityStrip`, `ActivityEventGrid`, `EventPayloadInspector`, `LinkedEntityPanel`.
- Suggested MVVM structure:
  - `AuditActivityWorkspacePage`
  - `AuditActivityWorkspaceViewModel`
  - extend existing `ActivityLogViewModel`
  - `AuditEventInspectorViewModel`
- Suggested services:
  - activity log service
  - evidence link resolver
  - audit packet export service
- Suggested DTO/domain usage: existing activity/event models plus evidence manifest metadata.
- Loading/error/empty states: no retained activity, filters hide all, export unavailable.
- Accessibility considerations: chronological focus, severity and object labels explicit, payload inspector keyboard reachable.
- Performance considerations: virtualized log, capped retained rows, payload fetch on selection.
- Future extensibility ideas: actor-centric views, cross-workspace audit correlation, immutable signed packets.
- Suggested implementation phases:
  1. activity stream + inspector
  2. linked object and payload views
  3. export/evidence
  4. advanced correlation

### 18. Notification & Alert Center

- Screen purpose: Centralize operator notifications, delivery posture, alert definitions, unread state, escalation, and acknowledgment workflows.
- Operator workflow: scan critical alerts -> filter by severity/workspace -> inspect details -> acknowledge/escalate -> jump to owning workspace.
- Layout structure: top delivery posture strip, left alert categories, center notification grid, right selected notification inspector and action deck.
- Toolbar/actions: `Refresh`, `Mark read`, `Acknowledge`, `Escalate`, `Mute category`, `Open target`, `Clear resolved`.
- Pane structure: categories, notification history, inspector, lower delivery diagnostics.
- Tables/grids: notification grid, category summary grid, alert rule summary.
- Detail panels: selected message, related workflow, retry/escalation state, linked evidence.
- Suggested controls: `NotificationCategoryRail`, `AlertSeverityBadgeStrip`, `NotificationInspectorPanel`, `DeliveryPosturePanel`.
- Suggested MVVM structure:
  - `NotificationAlertCenterPage`
  - `NotificationAlertCenterViewModel`
  - extend existing `NotificationCenterViewModel` and `MessagingHubViewModel`
- Suggested services:
  - notification service
  - alert delivery posture service
  - escalation workflow service
- Suggested DTO/domain usage: notification history models, operator work item routing metadata, system-health event DTOs.
- Loading/error/empty states: no notifications yet, filter-hidden history, delivery outage.
- Accessibility considerations: unread state and severity visible via text and iconography; action deck keyboard-first.
- Performance considerations: incremental feed append, bounded history, per-category lazy expansion.
- Future extensibility ideas: alert subscriptions, alert correlation, incident room handoff.
- Suggested implementation phases:
  1. notification history + inspector
  2. categories and delivery posture
  3. escalation and mute workflows
  4. rule and subscription overlays

### 19. Order Management Workspace

- Screen purpose: Operate the institutional blotter for active, historical, partial, cancelled, and replaced orders with execution quality context.
- Operator workflow: filter blotter -> inspect lifecycle -> cancel/replace if eligible -> review fills and route quality -> trace strategy and position linkage.
- Layout structure: top blotter status strip, center order grid, right order inspector, lower fills/timeline/linked-position tabs.
- Toolbar/actions: `Refresh`, `Cancel`, `Replace`, `Bulk cancel`, `Open fills`, `Open position`, `Export blotter`.
- Pane structure: active vs historical tab strip, order grid, inspector, lower lifecycle panels.
- Tables/grids: order blotter, fills grid, route diagnostics grid, historical revisions.
- Detail panels: selected order summary, route, lifecycle timeline, linked strategy, linked position.
- Suggested controls: `OrderBlotterGrid`, `ExecutionTimelinePanel`, `CancelReplaceActionBar`, `RouteQualityBadgeStrip`.
- Suggested MVVM structure:
  - `OrderManagementWorkspacePage`
  - `OrderManagementWorkspaceViewModel`
  - `OrderLifecycleInspectorViewModel`
  - `ExecutionQualityPanelViewModel`
- Suggested services:
  - `IOrderAuditQueryService`
  - `IOrderGateway`
  - `IExecutionQualityAnalyticsService`
- Suggested DTO/domain usage:
  - `FundAccountBrokerageOrderDto`
  - `FundAccountBrokerageFillDto`
  - execution audit models
- Loading/error/empty states: no orders, filtered miss, route diagnostics unavailable, replace blocked.
- Accessibility considerations: explicit state labels for partial/cancel-replace, action availability described inline.
- Performance considerations: virtualized blotter, immutable revision snapshots, lazy fill timeline load.
- Future extensibility ideas: venue analytics, broker scorecards, child-order tree.
- Suggested implementation phases:
  1. blotter + inspector
  2. fills and timeline
  3. cancel/replace workflows
  4. execution quality analytics

### 20. Market Depth / Order Book Workspace

- Screen purpose: Display high-frequency order-book depth, imbalance, spread analytics, aggressor flow, and liquidity posture.
- Operator workflow: select symbol -> inspect depth ladder -> watch imbalance and flow -> zoom time window -> branch to live quotes or order entry.
- Layout structure: top symbol and depth controls, center depth ladder and heatmap, right liquidity inspector, lower recent trades and time-window analytics.
- Toolbar/actions: `Refresh`, `Depth levels`, `Zoom`, `Normalize`, `Open ticket`, `Open live quotes`, `Reset scale`.
- Pane structure:
  - top: selected symbol, venue, depth level, scale controls
  - center left: bid/ask ladder
  - center right: imbalance/liquidity summary
  - bottom left: aggressor tape
  - bottom right: spread and time-window analytics
- Tables/grids: depth grid, aggressor trade grid, venue contribution grid.
- Detail panels: selected level details, liquidity warnings, route relevance.
- Suggested controls: `MarketDepthLadderControl`, `OrderImbalancePanel`, `LiquidityHeatmapControl`, `AggressorTapeControl`.
- Suggested MVVM structure:
  - `MarketDepthWorkspacePage`
  - `MarketDepthWorkspaceViewModel`
  - existing `OrderBookViewModel` as primary base
  - `LiquidityAnalyticsPanelViewModel`
- Suggested services:
  - order book feed service
  - liquidity analytics service
  - venue contribution service
- Suggested DTO/domain usage:
  - `OrderBookLevelDto`
  - live trade and quote DTOs
  - provider latency and market status DTOs
- Loading/error/empty states: no symbol selected, depth feed unavailable, stale ladder, unsupported venue depth.
- Accessibility considerations: ladder selection keyboard support; imbalance and spread metrics exposed textually; zoom state visible.
- Performance considerations: highly optimized depth diff application, no full ladder redraw per tick, configurable heatmap cadence.
- Future extensibility ideas: queue position simulation, venue microstructure analytics, execution impact preview.
- Suggested implementation phases:
  1. ladder + spread posture
  2. aggressor tape and liquidity analytics
  3. heatmap and scaling
  4. execution impact and venue overlays

## Implementation Sequencing

Recommended wave order:

1. Stabilize shared desktop shell primitives and reusable controls.
2. Expand the highest-leverage existing seams first:
   - Security Master
   - System Health & Diagnostics
   - Reconciliation
   - Order Management / Market Depth
3. Build out trading and portfolio continuity:
   - Paper Trading
   - Live Trading
   - Portfolio
   - Position & Exposure
   - Risk Management
4. Build strategy and data operations:
   - Strategy Research
   - Historical Replay
   - Strategy Builder
   - Backfill & Data Repair
   - Watchlist
5. Build workstation governance and operator productivity:
   - Export & Reporting
   - Workflow Automation
   - Audit & Activity
   - Notification & Alert Center

## Validation Strategy

For desktop implementation work that follows this blueprint, prefer focused WPF slices:

```powershell
python ./scripts/dev/desktop_screen_blueprint_checklist.py --summary
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~SecurityMasterViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~OrderBookViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~MainPageUiWorkflowTests|FullyQualifiedName~WorkspaceShellContextStripControlTests|FullyQualifiedName~TradingWorkspaceShellPageTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup
```

When the work touches shared workstation contracts, add matching tests in:

- `tests/Meridian.Tests/`
- `tests/Meridian.Ui.Tests/`
- focused endpoint tests for new workstation read models

## Risks

- A desktop-only implementation drift would duplicate browser/shared logic unless every workspace is
  built on shared DTOs and services first.
- High-frequency screens can degrade the shell if delta application is not separated from
  presentation state.
- Workspace sprawl will become unmanageable unless reusable dense-table, inspector, and status-ribbon
  controls are standardized early.
- WPF validation in this repo is sensitive to concurrent build/file-lock noise; implementation
  slices should stay narrow and prove the touched surface only.
