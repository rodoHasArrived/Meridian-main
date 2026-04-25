# WPF Workstation Shell UX

## Purpose

This document describes the workstation-shell pattern used by the Meridian WPF desktop client after the shell UX refactor. The goal is to keep dense financial workflows inside a stable operator workstation instead of rebuilding the page chrome around each landing surface.

## Shell Pattern

Each workspace shell follows the same structure:

1. A sticky context strip at the top of the content area.
2. A shared command bar with a small number of primary actions and a secondary overflow menu.
3. A fixed main work area with a center surface and a right-hand inspector rail.
4. A dock host at the bottom for drill-in pages and supporting tools.

Only queues, inspectors, and docked surfaces scroll. The page chrome itself remains stable.

The top-level shell remains fixed at four workspaces only:

- `Research`
- `Trading`
- `Data Operations`
- `Governance`

Accounting is now incorporated as a first-class Governance lane rather than a fifth workspace.

## Operating Contexts

The shell is no longer fund-profile-only. `WorkstationOperatingContextService` now centers the workstation on one active operating context that can represent:

- organization
- business
- client
- investment portfolio
- fund
- entity
- sleeve
- vehicle
- account
- ledger group

The header context switcher, shell badges, workspace session restore, and dock-layout persistence resolve through this operating context first, with fund-profile selection preserved as a compatibility bridge for existing fund-scoped pages.

## Trust Signals

The context strip is powered by `WorkspaceShellContextService` and standardizes:

- workspace title and subtitle
- active operating scope
- environment state
- as-of and freshness posture
- approval or lock state
- unread alert pressure
- base currency and ledger scope when available

This makes governance, trading, research, and data operations surfaces show the same trust vocabulary even when their working sets differ.

Data Operations provider trust now uses the DK1 rationale vocabulary directly. The workstation API provider rows expose `signalSource`, `reasonCode`, `recommendedAction`, `trustScore`, and `gateImpact`; the WPF provider queue shows the same source, code, and action in the visible provider-health detail when a provider route is degraded or disconnected.

## Workflow Summary Guidance

The shell now has a second shared seam for operator guidance: `WorkstationWorkflowSummaryService`.

- The shell now renders one current-workspace-first primary card and keeps the remaining workspace actions behind an explicit expansion affordance.
- The primary card still answers the same three questions: the current workspace state, the primary blocker, and the next operator action with an explicit target page tag.
- The WPF shell keeps `ShellNavigationCatalog`, the command palette, and the four-workspace model intact. The summary seam only changes what the shell emphasizes first.
- `WorkspaceShellContextService` remains responsible for chrome, trust badges, and scope cues. It is not overloaded with cross-workspace workflow rules.

The summary projection is shared with the workstation HTTP surface through `GET /api/workstation/workflow-summary`, so the next-action ordering stays consistent across shells and tests instead of being duplicated in WPF-specific page code.

## Shell Density

Shell density is now an explicit two-state preference persisted through `SettingsConfigurationService`.

- `Standard` keeps descriptive shell copy and secondary guidance visible.
- `Compact` suppresses duplicate chrome and keeps the current workspace action prominent above the fold.
- Density is separate from `BoundedWindowMode`; layout restore and pane behavior still belong to `Focused`, `Dock + Float`, and `Workbench Preset`.

## Shared Chrome Models

The shell chrome uses shared WPF-only models in `src/Meridian.Wpf/Models/WorkspaceShellChromeModels.cs`:

- `WorkspaceShellContext`
- `WorkspaceShellBadge`
- `WorkspaceCommandGroup`
- `WorkspaceCommandItem`
- `WorkspaceQueueItem`
- `WorkspaceRecentItem`

These types are intended for shell composition only. They do not introduce new backend or HTTP contracts.

## Workspace Rollout

### Governance

Governance is the pilot shell. It uses:

- explicit subareas for `Operations`, `Accounting`, `Reconciliation`, `Reporting`, and `Audit`
- a locked empty state when no fund-linked operating context is selected
- a right rail for active governance context, recent governance work, and audit access
- operating-context-scoped dock restore and bounded window-mode persistence
- lane summaries for `Accounting`, `Reconciliation`, `Reporting`, and `Audit` that become distinct as soon as a fund-linked context exists
- a persistent workbench identity card plus route banners for deep links such as `FundTrialBalance`, `FundReconciliation`, and `FundReportPack`
- explicit ownership, sign-off, and stale-snapshot copy on reconciliation and report-pack tabs so operators can tell whether they are in overview, accounting, reconciliation, or reporting mode without relying on navigation history

### Research

Research keeps its dense run-comparison workspace but now uses the shared context strip, command bar, and workflow-handoff card for run scope, promotion posture, accounting impact, reconciliation preview, and workspace actions.
The research handoff card exposes explicit `Start Backtest`, `Review Run`, and `Send to Trading Review` CTA states, backed by shared evidence badges from run, portfolio, ledger, and promotion seams.

### Trading

Trading keeps the live-position, blotter, and capital-control surfaces while moving desk actions into the shared command bar, surfacing run or desk posture in the context strip, and exposing accounting and audit drill-ins from the cockpit.
The cockpit shell now also carries a workflow-status card that replaces generic `Awaiting runs` copy with summary-driven handoff, blocker, and next-action labels. Active positions, risk posture, and the primary desk action now sit above KPI tiles and supporting narrative lanes, which keeps `no context selected`, `candidate awaiting paper review`, `active paper/live cockpit`, and `candidate awaiting governance review` visible without forcing operators to scroll past shell summaries.

### Data Operations

Data Operations adopts the same shell with provider, backfill, and storage queues plus a recent-operations rail and docked operational surfaces scoped to the active operating context.

## Window Modes

Docking remains bounded to one workstation shell. The supported modes are:

- `Focused`: docked panes only; floating panes are not restored
- `Dock + Float`: docked and floating panes restored inside one shell window
- `Workbench Preset`: dock/floating composition restored from a named preset such as `Research Compare`, `Trading Cockpit`, `Accounting Review`, or `Reconciliation Workbench`

Layouts persist per workspace and per operating context. Floating panes are normalized back to docked/tabbed behavior when the shell is in `Focused` mode.

## Sidebar Navigation

The main shell sidebar now groups each workspace navigation list into:

- Home
- Active Work
- Review / Alerts
- Admin / Support

This reduces flat navigation sprawl and makes keyboard-first scanning more predictable.

## Design System Baseline

The workstation shell treats the shared WPF resource dictionaries as the active design-system contract for operator pages:

- `src/Meridian.Wpf/Styles/ThemeTokens.xaml` owns the shell palette plus dedicated chart tokens for chart cards, plot areas, grid lines, axis labels, borders, crosshairs, equity/positive states, drawdown/negative states, and amber midpoint/warning emphasis.
- `src/Meridian.Wpf/Styles/ThemeSurfaces.xaml` provides `ChartCardStyle`, `ChartPlotAreaStyle`, `ChartCanvasPlotAreaStyle`, and `ChartLegendStripStyle` so chart-heavy pages do not duplicate panel chrome.
- `src/Meridian.Wpf/Styles/ThemeTypography.xaml` provides the shared display, body, data, and chart text styles used by chart labels, market-depth rows, and tabular trading values.
- `src/Meridian.Wpf/Assets/Brand/` and `src/Meridian.Wpf/Assets/Icons/` mirror the extracted Meridian design-system bundle so WPF navigation, brand marks, and page icons stay aligned with the shipped visual asset set.

When adding or changing charts, use the WPF chart tokens rather than raw hex values. ScottPlot and LiveCharts renderers should take colors from `Meridian.Ui.Services.Services.ColorPalette`, while XAML chart chrome should bind to the `Chart*` resource keys in `ThemeTokens.xaml`.

Market-depth and chart semantics should preserve the design-system previews: bid/positive = mint, ask/drawdown = coral, live/crosshair = signal cyan, midpoint/warning = amber. The broader light shell palette remains separate; pages opt into dark chart surfaces only for chart/trading panels.

## Validation Expectations

Changes to workstation shells should continue to validate:

- workspace switching and command palette navigation
- keyboard-only navigation across sidebar, command bar, queue, and dock
- trust-state rendering for fixture mode, offline mode, stale data, unread alerts, currency, and ledger scope
- workflow-summary rendering for shell-wide next actions, blockers, and target page tags
- fund-linked empty states where governance actions depend on accounting-compatible scope
- operating-context switching and per-context layout restore
- bounded window-mode behavior for focused, dock-float, and workbench-preset shells

## Validation Commands

Use the workstation-shell checker for fast structural validation before running broader WPF build or test commands:

- `python scripts/wpf_finance_ux_checks.py --root . --paths src/Meridian.Wpf`
- `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj`
- `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -p:EnableFullWpfBuild=true -p:BuildProjectReferences=false --no-restore`

This complements the existing build and test validation for `Meridian.Wpf` by checking the shell-specific UX invariants introduced by the workstation-shell refactor.
