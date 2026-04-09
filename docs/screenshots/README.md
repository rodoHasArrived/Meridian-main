# Meridian UI Screenshots

Screenshots of the Meridian WPF desktop application. Desktop screenshots run on `windows-latest` in fixture mode.

## How to run (WPF desktop)

```powershell
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -p:TargetFramework=net9.0-windows
$env:MDC_FIXTURE_MODE = '1'
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj --no-build -c Release
```

Desktop screenshots are stored under [`desktop/`](desktop/) and are captured using Windows UI Automation and the built-in Command
Palette to navigate between pages.

### Local automation script (Windows)

For local/runner use on Windows, run:

```powershell
pwsh -File scripts/dev/capture-desktop-screenshots.ps1
```

Optional flags:

```powershell
# Reuse an existing build
pwsh -File scripts/dev/capture-desktop-screenshots.ps1 -SkipBuild

# Keep the app open after capture for manual inspection
pwsh -File scripts/dev/capture-desktop-screenshots.ps1 -KeepAppOpen
```

The script builds `src/Meridian.Wpf`, launches fixture mode (`MDC_FIXTURE_MODE=1`),
navigates each page via the shared desktop workflow runner, and writes PNGs to `docs/screenshots/desktop/`.

For other scripted desktop walkthroughs, use:

```powershell
pwsh -File scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup
pwsh -File scripts/dev/generate-desktop-user-manual.ps1
```

`Symbols` is currently excluded from the automated screenshot refresh flow because the page exits the desktop shell in fixture-mode automation; the committed `desktop/wpf-symbols.png` remains as the last known-good capture until that crash is fixed.

---

## 01 – Main Dashboard

Full-page view of the Meridian Terminal dashboard (`/`). Shows the overview panel, activity log, data provider selector, storage configuration, data sources, historical backfill controls, derivatives tracking, and subscribed symbols table.

![Meridian Terminal – Main Dashboard](https://github.com/user-attachments/assets/44f8822e-c141-4105-80bf-81fe2a9de7da)

---

## 02 – React Workstation Shell

The React-based trading workstation is served at `/workstation/` and provides the modern portfolio/trading workspace UI.

![Workstation Shell](02-workstation.png)

---

## 03 – Swagger API Docs

Interactive REST API documentation is available at `/swagger/index.html` and covers all 300+ API routes (backfill, providers, storage, security master, execution, etc.).

![Swagger API Docs](03-swagger.png)

---

## 04 – Storage Configuration & Data Sources

Mid-page view showing the **Storage Configuration** card (data root path, naming convention, date partitioning, preview path) and the top of the **Data Sources** panel with automatic failover toggle.

![Storage Configuration & Data Sources](https://github.com/user-attachments/assets/48bc62b6-4906-4483-af23-2632524c9f79)

---

## 05 – Data Provider Selector

The **Data Provider** card at the top of the dashboard, showing the live-connection provider dropdown and per-provider credential/settings panel.

![Data Provider Selector](05-data-source.png)

---

## 06 – Data Sources Panel

The **Data Sources** panel listing all registered providers, their failover priority order, and the automatic-failover toggle.

![Data Sources Panel](06-data-sources.png)

---

## 07 – Historical Backfill

The **Historical Backfill** section, showing the provider selector, symbol and date-range inputs, and the rolling status terminal for in-progress backfill jobs.

![Historical Backfill](07-backfill.png)

---

## 08 – Derivatives Tracking

The **Derivatives** panel for configuring options / futures data collection, including underlying symbol entry and options-chain provider status.

![Derivatives Tracking](08-derivatives.png)

---

## 09 – Subscribed Symbols

The **Subscribed Symbols** table showing the active symbol list with data-type columns and the add/remove controls.

![Subscribed Symbols](09-symbols.png)

---

## 10 – System Status Panel

The **System Status** section at the top of the dashboard showing the live metrics grid (published events, dropped events, integrity events, historical bars) and the scrolling activity log terminal.

![System Status Panel](10-status.png)

---

## 11 – Login Page

The **Sign In** page served at `/login`, used when Meridian Terminal is running in authenticated mode (`MDC_AUTH_MODE=required`). Shows the username/password form with the same dark terminal theme as the main dashboard.

![Login Page](11-login.png)

---

## 12 – Workstation: Research

The **Research** workspace of the React workstation shell, covering backtests, strategy run comparisons, QuantScript execution results, and experiment tracking.

![Workstation – Research](12-workstation-research.png)

---

## 13 – Workstation: Trading

The **Trading** workspace of the React workstation shell, showing the paper-trading cockpit, live positions blotter, open orders, fills history, and risk guardrails.

![Workstation – Trading](13-workstation-trading.png)

---

## 14 – Workstation: Data Operations

The **Data Operations** workspace of the React workstation shell, covering provider health, active backfills, storage tiers, exports, and symbol-management workflows.

![Workstation – Data Operations](14-workstation-data-operations.png)

---

## 15 – Workstation: Governance

The **Governance** workspace of the React workstation shell, showing the fund ledger overview, risk audit history, reconciliation breaks, diagnostics, and operational settings.

![Workstation – Governance](15-workstation-governance.png)
![Workstation – Governance](13-workstation-governance.png)

---

## 14 – Workstation: Trading – Orders

The **Orders blotter** deep-link within the Trading workspace, showing working and partially filled trading orders with status, fill quantity, and execution detail.

![Workstation – Trading: Orders](14-workstation-trading-orders.png)

---

## 15 – Workstation: Trading – Positions

The **Positions** deep-link within the Trading workspace, showing live positions, exposure, marks, and unrealized P&L.

![Workstation – Trading: Positions](15-workstation-trading-positions.png)

---

## 16 – Workstation: Trading – Risk

The **Risk guardrails** deep-link within the Trading workspace, showing the trading risk cockpit, position limits, drawdown stops, and order-rate throttle state.

![Workstation – Trading: Risk](16-workstation-trading-risk.png)

---

## 17 – Workstation: Data Operations – Providers

The **Provider health** deep-link within the Data Operations workspace, showing feed status, latency metrics, and operational notes for each registered provider.

![Workstation – Data Operations: Providers](17-workstation-data-operations-providers.png)

---

## 18 – Workstation: Data Operations – Backfills

The **Backfill queue** deep-link within the Data Operations workspace, showing active backfill jobs, progress, and review items.

![Workstation – Data Operations: Backfills](18-workstation-data-operations-backfills.png)

---

## 19 – Workstation: Data Operations – Exports

The **Storage exports** deep-link within the Data Operations workspace, showing export profiles and recent delivery targets.

![Workstation – Data Operations: Exports](19-workstation-data-operations-exports.png)

---

## 20 – Workstation: Governance – Ledger

The **Ledger overview** deep-link within the Governance workspace, showing cash flow summaries and audit-facing ledger details.

![Workstation – Governance: Ledger](20-workstation-governance-ledger.png)

---

## 21 – Workstation: Governance – Reconciliation

The **Reconciliation history** deep-link within the Governance workspace, showing open breaks, balanced runs, and reconciliation detail.

![Workstation – Governance: Reconciliation](21-workstation-governance-reconciliation.png)

---

## 22 – Workstation: Governance – Security Master

The **Security master coverage** deep-link within the Governance workspace, showing unresolved references and coverage risk across the instrument universe.

![Workstation – Governance: Security Master](22-workstation-governance-security-master.png)

---

## WPF Desktop Application

The following screenshots are captured from the WPF desktop application running in
fixture mode (`MDC_FIXTURE_MODE=1`). They live under the [`desktop/`](desktop/) subdirectory.

| # | Page | File |
|---|------|------|
| D01 | Dashboard | [`desktop/wpf-dashboard.png`](desktop/wpf-dashboard.png) |
| D02 | Providers | [`desktop/wpf-providers.png`](desktop/wpf-providers.png) |
| D03 | Provider Health | [`desktop/wpf-provider-health.png`](desktop/wpf-provider-health.png) |
| D04 | Backfill | [`desktop/wpf-backfill.png`](desktop/wpf-backfill.png) |
| D05 | Symbols | [`desktop/wpf-symbols.png`](desktop/wpf-symbols.png) |
| D06 | Live Data | [`desktop/wpf-live-data.png`](desktop/wpf-live-data.png) |
| D07 | Storage | [`desktop/wpf-storage.png`](desktop/wpf-storage.png) |
| D08 | Data Quality | [`desktop/wpf-data-quality.png`](desktop/wpf-data-quality.png) |
| D09 | Data Browser | [`desktop/wpf-data-browser.png`](desktop/wpf-data-browser.png) |
| D10 | Strategy Runs | [`desktop/wpf-strategy-runs.png`](desktop/wpf-strategy-runs.png) |
| D11 | Backtest | [`desktop/wpf-backtest.png`](desktop/wpf-backtest.png) |
| D12 | Quant Script | [`desktop/wpf-quant-script.png`](desktop/wpf-quant-script.png) |
| D13 | Security Master | [`desktop/wpf-security-master.png`](desktop/wpf-security-master.png) |
| D14 | Diagnostics | [`desktop/wpf-diagnostics.png`](desktop/wpf-diagnostics.png) |
| D15 | Settings | [`desktop/wpf-settings.png`](desktop/wpf-settings.png) |
