# Desktop Workstation Screen Checklist Reference

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-15

This reference retains the twenty screen scopes consumed by the
[desktop checklist](../../plans/desktop-workstation-screen-blueprint.checklist.json) and
[desktop workflow catalog](../../../scripts/dev/desktop-workflows.json). The original design plan
is [historical source material](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/desktop-workstation-screen-blueprint.md).
Its archive redirect cannot supply the headings required by the active checklist validator.

The checklist owns the stable IDs, retained workspace groupings, evidence paths, maturity labels,
and focused validation commands. The sections below explain each tracked scope; they do not
re-certify its maturity or delivery status. Screen names and groupings describe the original
checklist scopes, not additional top-level navigation. Current shell routes belong to
[`src/Meridian.Wpf/Features/`](../../../src/Meridian.Wpf/Features/) and
[`ShellNavigationCatalog`](../../../src/Meridian.Wpf/Models/ShellNavigationCatalog.cs).
Use the [WPF / Web-UI Alignment Plan](wpf-web-ui-alignment-plan.md) for current parity work and the
[roadmap registry](../../roadmap/README.md) for delivery state.

Validate the checklist from the repository root:

```bash
python scripts/dev/desktop_screen_blueprint_checklist.py --summary
```

The validator checks every heading, source/evidence path, controlled maturity label, and reciprocal
workflow reference. A missing or empty blueprint, or an archive redirect, must fail validation.
When changing a screen's implementation, review its checklist evidence and run its recorded focused
command; the existence of a page or a passing catalog check alone does not establish readiness.

## Tracked screen scopes

### 1. Security Master Workspace

Reference-data review and security lifecycle readiness. Checklist evidence includes
`SecurityMasterViewModelTests`; current shell navigation places Security Master in Data.

### 2. Live Market Data Workspace

Live quote/feed presentation through `LiveDataViewerPage` and `LiveDataViewerViewModel`.
Fixture/demo evidence must remain distinguishable from live-provider acceptance.

### 3. Watchlist Workspace

Watchlist review and updates, with focused behavior covered by `WatchlistViewModelTests`.

### 4. Historical Replay Workspace

Historical replay/backtest review. The checklist currently cites `BatchBacktestViewModelTests`
and Backtest workflow captures; those do not establish all event-replay capabilities.

### 5. Strategy Workspace

Strategy run browsing and workspace composition, backed by `StrategyRunBrowserViewModelTests`
and `StrategyWorkspaceShellPageTests`.

### 6. Strategy Builder Workspace

Strategy authoring and construction scope. Consult the checklist's evidence and the parity plan
before treating adjacent authoring tools as a completed visual strategy builder.

### 7. Paper Trading Workspace

Paper-trading cockpit behavior composed through the Trading shell. The checklist records
`TradingWorkspaceShellViewModelTests` as its focused evidence.

### 8. Live Trading Workspace

Live-trading review and execution context. Shared Trading-shell and position-blotter evidence
does not replace live-trading-specific acceptance.

### 9. Portfolio Workspace

Account and aggregate portfolio review, including the `AccountPortfolioPage` and its view-model
tests. Paper/live history continuity remains a separate acceptance concern.

### 10. Position & Exposure Workspace

Position and exposure review. The checklist uses `PositionBlotterViewModelTests` for its current
evidence; active navigation and broader portfolio exposure belong to their owning feature modules.

### 11. Risk Management Workspace

Run risk, limits, and drawdown review. Checklist evidence includes `RunMatViewModelTests`;
the active `RunRisk` route belongs to Trading, independently of the retained checklist grouping.

### 12. Reconciliation Workspace

Accounting reconciliation workbench behavior, backed by `FundLedgerViewModelTests` and
`FundReconciliationWorkbenchServiceTests`.

### 13. Export & Reporting Workspace

Report/export preparation and presets, with focused `AnalysisExportViewModelTests`,
`DataExportViewModelTests`, and `ExportPresetsViewModelTests`.

### 14. Backfill & Data Repair Workspace

Backfill operations and data-quality review. The checklist links Backfill/Data Quality tests
and the corresponding desktop workflow captures.

### 15. System Health & Diagnostics Workspace

System health and diagnostics presentation, backed by `SystemHealthViewModelTests` and
`SystemHealthPageSmokeTests`.

### 16. Workflow Automation Workspace

Workflow library and workspace composition. `WorkflowLibraryViewModelTests` support the shell/list
scope; full automation execution needs its own acceptance evidence.

### 17. Audit & Activity Workspace

Audit/activity and messaging review, with `ActivityLogViewModelTests` and
`MessagingHubViewModelTests` as the checklist's focused evidence.

### 18. Notification & Alert Center

Notification history and filter recovery, backed by `NotificationCenterViewModelTests`.
Broader alert authoring and execution remain separate acceptance scopes.

### 19. Order Management Workspace

Order-management review across the current position-blotter and Trading-shell evidence.
A dedicated order-management acceptance claim requires dedicated proof.

### 20. Market Depth / Order Book Workspace

Order-book and market-depth presentation, with `OrderBookViewModelTests` as the focused
checklist evidence.
