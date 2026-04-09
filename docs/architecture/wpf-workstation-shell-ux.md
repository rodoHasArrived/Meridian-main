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

## Trust Signals

The context strip is powered by `WorkspaceShellContextService` and standardizes:

- workspace title and subtitle
- active fund or scope
- environment state
- as-of and freshness posture
- approval or lock state
- unread alert pressure

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

- a fund-first review queue grouped into Fund Ops, Reconciliation, Diagnostics, and Alerts
- a locked empty state when no fund is selected
- a right rail for active fund summary, recent governance work, and audit access
- dock restore with fund-scoped layout persistence

### Research

Research keeps its dense run-comparison workspace but now uses the shared context strip and command bar for run scope, promotion posture, and workspace actions.

### Trading

Trading keeps the live-position, blotter, and capital-control surfaces while moving desk actions into the shared command bar and surfacing run or desk posture in the context strip.

### Data Operations

Data Operations adopts the same shell with provider, backfill, and storage queues plus a recent-operations rail and docked operational surfaces.

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
- trust-state rendering for fixture mode, offline mode, stale data, and unread alerts
- fund-required empty states where governance actions depend on active fund scope

## Validation Commands

Use the workstation-shell checker for fast structural validation before running broader WPF build or test commands:

- `python scripts/wpf_finance_ux_checks.py --root . --paths src/Meridian.Wpf`

This complements the existing build and test validation for `Meridian.Wpf` by checking the shell-specific UX invariants introduced by the workstation-shell refactor.
