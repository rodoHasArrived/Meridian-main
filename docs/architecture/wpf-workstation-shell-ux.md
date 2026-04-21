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

### Research

Research keeps its dense run-comparison workspace but now uses the shared context strip and command bar for run scope, promotion posture, accounting impact, reconciliation preview, and workspace actions.

### Trading

Trading keeps the live-position, blotter, and capital-control surfaces while moving desk actions into the shared command bar, surfacing run or desk posture in the context strip, and exposing accounting and audit drill-ins from the cockpit.
The cockpit shell now also carries a dedicated promotion/status card that keeps promotion readiness,
audit linkage, and validation coverage visible above the KPI row, with direct `Run Review`,
`Event Replay`, and `Collection Sessions` actions when operators need deeper session context from
the same surface.

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

## Validation Expectations

Changes to workstation shells should continue to validate:

- workspace switching and command palette navigation
- keyboard-only navigation across sidebar, command bar, queue, and dock
- trust-state rendering for fixture mode, offline mode, stale data, unread alerts, currency, and ledger scope
- fund-linked empty states where governance actions depend on accounting-compatible scope
- operating-context switching and per-context layout restore
- bounded window-mode behavior for focused, dock-float, and workbench-preset shells

## Validation Commands

Use the workstation-shell checker for fast structural validation before running broader WPF build or test commands:

- `python scripts/wpf_finance_ux_checks.py --root . --paths src/Meridian.Wpf`
- `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj`
- `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -p:EnableFullWpfBuild=true -p:BuildProjectReferences=false --no-restore`

This complements the existing build and test validation for `Meridian.Wpf` by checking the shell-specific UX invariants introduced by the workstation-shell refactor.
