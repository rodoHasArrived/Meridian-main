# UI Redesign Notes: Trading Workstation Migration

**Last Updated:** 2026-04-04
**Status:** Superseded and aligned with the active [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)

---

## Purpose

This document summarizes the target desktop and web information architecture for Meridian as it migrates from a page-centric utility suite to a workflow-centric **trading workstation**.

The canonical implementation plan lives in [`docs/plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md). This document focuses on the UI structure and component patterns implied by that plan.

---

## Design Principles

1. **Workflows over pages** — users should think in terms of research, trading, data operations, and governance, not a long list of disconnected tools.
2. **One run model everywhere** — backtest, paper, and live should feel like modes of the same strategy-run lifecycle.
3. **Portfolio + ledger visibility** — performance, costs, financing, and audit history should be visible without leaving the product.
4. **Progressive disclosure** — expert tools remain available, but primary actions stay obvious.
5. **Safety by design** — paper/live mode differences must be unmistakable.

---

## Target Workspaces

### 1) Research

**Purpose:** Data exploration, backtests, experiment comparison, and result analysis.

**Primary surfaces**
- Backtest Studio
- Engine selector (Meridian Native / Lean)
- Dataset and coverage validation
- Equity curve, fills, attribution, and ledger drill-in
- Saved scenarios and run comparison

### 2) Trading

**Purpose:** Strategy operation in paper mode now and live mode later.

**Primary surfaces**
- Active strategies
- Orders / fills blotter
- Positions and exposure
- Capital & controls posture (cash, financing, unsettled activity)
- Quote / order-book views
- Risk and alert panels
- Strategy controls (pause, stop, flatten)

### 3) Data Operations

**Purpose:** Provider, symbol, backfill, storage, and export management.

**Primary surfaces**
- Providers and health
- Symbols and watchlists
- Backfill jobs and schedules
- Storage, retention, and packaging
- Operational export flows

### 4) Governance

**Purpose:** Fund operations, ledger, diagnostics, retention assurance, audit, notifications, and settings.

**Primary surfaces**
- Fund Operations workspace
- Accounts, banking, and account portfolio drill-throughs
- Cash & financing, trial balance, journal, reconciliation, and audit trail
- Diagnostics and system status
- Notifications and audit history
- Retention and settings

---

## Key Composite Screens

### Backtest Studio

**Layout**
- Left: strategy, engine, parameters, dataset
- Center: progress + equity curve + benchmark comparison
- Right: metrics, fills, attribution, ledger summary

**Migration notes**
- Fold the current native backtest page and Lean backtest controls into one operator-facing experience.
- Add run comparison and “open portfolio / open ledger” actions.

### Trading Cockpit

**Layout**
- Left: strategy list / watchlist
- Center: market view + positions + actions
- Right: blotter + fills + risk + alerts
- Optional lower panel: execution audit / ledger activity

**Migration notes**
- Promote current live viewer and execution primitives into a cohesive paper-trading experience.
- Keep live trading hidden behind explicit mode guards and confirmation affordances.

### Fund Operations Workspace

**Tabs**
- Overview
- Accounts
- Banking
- Portfolio
- Cash & Financing
- Journal
- Trial Balance
- Reconciliation
- Audit Trail

---

## Navigation Rules

- Keep top-level navigation to **four workspaces**.
- Use workspace tabs/pivots for subviews.
- Ensure every major trading capability is reachable from both primary navigation and the command palette.
- Prefer “open related workflow” actions over orphan utility pages.

---

## Implementation Notes

### Near-term
- Register missing workflow pages consistently in WPF navigation.
- Add command palette entries for backtesting, trading, and governance fund-operations flows.
- Preserve existing pages where useful, but introduce workspace shells above them.

### Mid-term
- Add shared run, portfolio, and ledger view models / read models.
- Move page-local business logic into orchestration-backed view models.

### Long-term
- Treat web and desktop surfaces as two views over the same workflow model rather than separate conceptual products.

---

## Relationship to Other Docs

- Implementation plan: [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- Planning and sequencing: [ROADMAP](../status/ROADMAP.md)
- Current repository state: [FEATURE_INVENTORY](../status/FEATURE_INVENTORY.md)
