# Meridian Desktop User Manual

> Auto-generated on 2026-11-14 01:46:32 +00:00 by scripts/dev/generate-desktop-user-manual.ps1.

This manual is captured from the WPF desktop shell in fixture mode so the walkthrough stays deterministic, offline-friendly, and safe to reproduce while debugging UI workflows.

## Regenerate

```powershell
pwsh -File scripts/dev/generate-desktop-user-manual.ps1
```

To capture a single workflow, pass one or more workflow names:

```powershell
pwsh -File scripts/dev/generate-desktop-user-manual.ps1 -Workflow manual-overview,manual-data
```

## Platform Overview

A top-level walkthrough of the shell, workspace launcher, and the four major workstation shells.

### Step 1 - Strategy Workspace

Start on the Strategy workspace to review the shell status bar, demo data banner, and the first-launch landing experience.

![Strategy Workspace](../screenshots/desktop/manuals/manual-overview/01-strategy-workspace.png)

### Step 2 - Workspace Layouts

Use Workspace layouts to inspect saved layouts and workstation surface organization.

![Workspace Layouts](../screenshots/desktop/manuals/manual-overview/02-workspace-layouts.png)

### Step 3 - Strategy Workspace

The Strategy workspace now keeps the current workspace next action above the fold while secondary workspace actions stay behind expansion.

![Strategy Workspace](../screenshots/desktop/manuals/manual-overview/03-strategy-workspace.png)

### Step 4 - Trading Workspace

The Trading workspace now leads with active positions, workflow blocker/next action, and risk posture before KPI tiles and supporting desk lanes.

![Trading Workspace](../screenshots/desktop/manuals/manual-overview/04-trading-workspace.png)

### Step 5 - Data Workspace

The Data workspace centralizes providers, backfills, symbols, and storage operations.

![Data Workspace](../screenshots/desktop/manuals/manual-overview/05-data-workspace.png)

### Step 6 - Accounting Workspace

The Accounting workspace now exposes an explicit workbench identity so overview, accounting, reconciliation, and reporting modes stay obvious at a glance.

![Accounting Workspace](../screenshots/desktop/manuals/manual-overview/06-accounting-workspace.png)

### Step 7 - Help

The Help page is the starting point when an operator needs setup guidance or troubleshooting references.

![Help](../screenshots/desktop/manuals/manual-overview/07-help.png)

## Data Workflow

A practical walkthrough for operators who manage providers, symbols, storage, and backfills.

### Step 1 - Data Workspace

Begin in the workspace shell to orient around the operator dashboard and the available data tools.

![Data Workspace](../screenshots/desktop/manuals/manual-data/01-data-shell.png)

### Step 2 - Providers

Manage active providers, credentials, and connectivity from the Providers screen.

![Providers](../screenshots/desktop/manuals/manual-data/02-providers.png)

### Step 3 - Provider Health

Review feed health, current statuses, and degraded-state signals before changing collection behavior.

![Provider Health](../screenshots/desktop/manuals/manual-data/03-provider-health.png)

### Step 4 - Backfill

Use Backfill to queue or inspect historical data collection work.

![Backfill](../screenshots/desktop/manuals/manual-data/04-backfill.png)

### Step 5 - Data Sources

Use Data Sources to review provider routing, failover ordering, and feed configuration without leaving the Data workflow.

![Data Sources](../screenshots/desktop/manuals/manual-data/05-data-sources.png)

### Step 6 - Storage

Use Storage to inspect retention, layout, and data organization settings.

![Storage](../screenshots/desktop/manuals/manual-data/06-storage.png)

### Step 7 - Data Quality

Finish with Data Quality so anomalies and remediation work can be reviewed in one pass.

![Data Quality](../screenshots/desktop/manuals/manual-data/07-data-quality.png)

## Strategy and Trading Workflow

Walk through the core strategy surfaces and the trading cockpit used to inspect positions and risk.

### Step 1 - Strategy Workspace

The Strategy workspace is the umbrella view for strategy discovery, analysis, and run review.

![Strategy Workspace](../screenshots/desktop/manuals/manual-strategy-and-trading/01-strategy-shell.png)

### Step 2 - Backtest

Use Backtest to configure a strategy replay and inspect summary metrics.

![Backtest](../screenshots/desktop/manuals/manual-strategy-and-trading/02-backtest.png)

### Step 3 - Strategy Runs

Use Strategy Runs to browse completed runs and drill into the detail pages.

![Strategy Runs](../screenshots/desktop/manuals/manual-strategy-and-trading/03-strategy-runs.png)

### Step 4 - Quant Script

Use Quant Script for exploratory scripting and ad hoc analysis.

![Quant Script](../screenshots/desktop/manuals/manual-strategy-and-trading/04-quant-script.png)

### Step 5 - Trading Workspace

Switch to the Trading workspace to capture the action-first cockpit layout, with positions, current blocker, and primary desk action ahead of supporting narrative lanes.

![Trading Workspace](../screenshots/desktop/manuals/manual-strategy-and-trading/05-trading-shell.png)

### Step 6 - Position Blotter

Use the Position Blotter to capture the grid-first layout, batch-action readiness, and the simplified single-path blocked state.

![Position Blotter](../screenshots/desktop/manuals/manual-strategy-and-trading/06-position-blotter.png)

### Step 7 - Run Risk

Finish on Run Risk to validate the controls and risk views that govern a strategy run.

![Run Risk](../screenshots/desktop/manuals/manual-strategy-and-trading/07-run-risk.png)

## Accounting Workflow

Cover the oversight surfaces used for accounting, reconciliation, reporting, and reference-data governance.

### Step 1 - Accounting Workspace

Start in Accounting to orient around the oversight shell and confirm the shared workbench identity card.

![Accounting Workspace](../screenshots/desktop/manuals/manual-accounting/01-accounting-shell.png)

### Step 2 - Fund Ledger

Use Fund Ledger to capture the single Accounting workbench with explicit mode/workbench identity above the tabs.

![Fund Ledger](../screenshots/desktop/manuals/manual-accounting/02-fund-ledger.png)

### Step 3 - Fund Reconciliation

Use Fund Reconciliation to capture the deep-link route banner plus the prominent ownership and stale-snapshot posture.

![Fund Reconciliation](../screenshots/desktop/manuals/manual-accounting/03-fund-reconciliation.png)

### Step 4 - Fund Report Pack

Use Fund Report Pack to capture the reporting-mode route banner, handoff readiness, sign-off owner, and freshness warning.

![Fund Report Pack](../screenshots/desktop/manuals/manual-accounting/04-fund-report-pack.png)

### Step 5 - Security Master

Use Security Master to review reference data coverage and investigate security-level issues.

![Security Master](../screenshots/desktop/manuals/manual-accounting/05-security-master.png)

### Step 6 - Settings

Finish in Settings to capture the explicit shell-density preference that now controls shell verbosity independently from window mode.

![Settings](../screenshots/desktop/manuals/manual-accounting/06-settings.png)
