# Meridian Workflow Guide

This guide walks through the key workflows in the Meridian Terminal web dashboard and CLI. It covers setup, provider configuration, data collection, historical backfill, and operations.

> **How to launch the dashboard**
> ```bash
> MDC_AUTH_MODE=optional dotnet run --project src/Meridian/Meridian.csproj -- --ui --http-port 8200
> # open http://localhost:8200
> ```
> Or with Make:
> ```bash
> make run-ui
> ```

---

## Contents

1. [Dashboard Overview](#1-dashboard-overview)
2. [Step 1 – Choose a Data Provider](#2-step-1--choose-a-data-provider)
3. [Step 2 – Configure Storage](#3-step-2--configure-storage)
4. [Step 3 – Add a Data Source](#4-step-3--add-a-data-source)
5. [Step 4 – Add Symbols](#5-step-4--add-symbols)
6. [Step 5 – Run a Historical Backfill](#6-step-5--run-a-historical-backfill)
7. [Step 6 – Derivatives Tracking (Optional)](#7-step-6--derivatives-tracking-optional)
8. [CLI Workflows](#8-cli-workflows)
9. [Monitoring & Status](#9-monitoring--status)
10. [API Reference (Swagger)](#10-api-reference-swagger)

---

## 1. Dashboard Overview

When you open the Meridian Terminal at `http://localhost:8200` you land on the main dashboard. The page is a single scrollable configuration surface divided into sections:

| Section | Purpose |
|---------|---------|
| **Status bar** (top) | Live event counters, active provider badge, events-per-second |
| **Activity Log** | Real-time log stream showing connection events and configuration changes |
| **Data Provider** | Choose and configure the active market data provider |
| **Storage Configuration** | Set the data root path, naming convention, and compression |
| **Data Sources** | Add and prioritise data sources with automatic failover |
| **Historical Backfill** | Trigger one-off OHLCV backfill jobs via free providers |
| **Derivatives Tracking** | Enable options data collection with Greeks and chain snapshots |
| **Subscribed Symbols** | Add symbols to stream in real time |

The left navigation sidebar provides quick-jump links to each section. The header shows connection state (**Connected / LIVE** when the API server is reachable).

![Meridian Terminal – Main Dashboard](https://github.com/user-attachments/assets/314db7de-e5da-4f07-99c0-04156fdd6ad5)

---

## 2. Step 1 – Choose a Data Provider

The **Data Provider** section lets you select which market data source Meridian connects to. Two providers ship out of the box:

| Provider | Description |
|----------|-------------|
| **Interactive Brokers (IB)** | TWS/Gateway connection giving real-time L2 depth, tick-by-tick trades, and option chains |
| **Alpaca** | Cloud-based WebSocket stream for equities and crypto; no local install required |

**To switch providers:**

1. Open the **Active Provider** dropdown.
2. Select the desired provider.
3. The badge below the dropdown updates to show connection details for that provider.

> For Interactive Brokers, ensure TWS or IB Gateway is running on `127.0.0.1:7496` (live) or `127.0.0.1:7497` (paper). The port is configurable per data source.

![Provider & Storage Configuration](https://github.com/user-attachments/assets/48bc62b6-4906-4483-af23-2632524c9f79)

---

## 3. Step 2 – Configure Storage

The **Storage Configuration** card controls where and how Meridian persists market data to disk.

| Field | Options | Default |
|-------|---------|---------|
| **Data Root Path** | Any local path | `data` |
| **Compression** | Disabled / GZIP Enabled | Disabled |
| **Naming Convention** | Flat / By Symbol / By Date / By Type | By Symbol |
| **Date Partitioning** | None / Daily / Hourly / Monthly | Daily |
| **File Prefix** | Any string (optional) | *(empty)* |
| **Include Provider in Path** | Yes / No | No |

The **Preview Path** box at the bottom of the card shows a live example of the resulting file path so you can verify the convention before saving.

**Example path** with the defaults selected:
```
data/AAPL/Trade/2024-01-15.jsonl
```

Click **💾 Save Storage Settings** to persist changes to `appsettings.json`.

> **Tip:** Enable GZIP compression if you are collecting high-frequency tick data and disk space is a concern. It roughly halves the file size with negligible CPU overhead.

---

## 4. Step 3 – Add a Data Source

A *data source* is a named, prioritised connection to a provider. Multiple data sources can be active at once; Meridian fails over automatically when the top-priority source drops.

### 4.1 Configure failover

Before adding sources, set the failover policy:

1. Tick **Enable Automatic Failover** (enabled by default).
2. Set the **Failover Timeout** in seconds (default: `30`).
3. Click **⚙ Save**.

### 4.2 Add a source

Fill in the **Add/Edit Data Source** form:

| Field | Description |
|-------|-------------|
| **Name** | A friendly label (e.g. "IB Primary") |
| **Provider** | Interactive Brokers (IB) / Alpaca / Polygon.io |
| **Type** | Real-Time / Historical / Both |
| **Priority** | Lower number = higher priority (1 wins over 100) |
| **Symbols** | Comma-separated list scoped to this source (leave blank for all) |

For **Interactive Brokers** sources, expand the **IB Settings** panel:

| Field | Default | Notes |
|-------|---------|-------|
| Host | `127.0.0.1` | Change for remote TWS |
| Port | `7496` | `7497` for paper trading |
| Client ID | `0` | Must be unique per connection |
| Paper Trading | unchecked | Tick for paper account |
| Subscribe Depth | checked | L2 order book streaming |
| Tick-by-Tick | checked | Highest fidelity trade data |

Click **Save Data Source**. The source appears in the table with its current **Status** (Active / Inactive / Error).

---

## 5. Step 4 – Add Symbols

The **Subscribed Symbols** section manages the set of instruments Meridian streams in real time.

### Add a new symbol

| Field | Description |
|-------|-------------|
| **Symbol** | Ticker (e.g. `AAPL`) |
| **Security Type** | Stock, ETF, Option, Future, Forex, Crypto, etc. |
| **Trades Stream** | Enable / Disable trade tick streaming |
| **Depth Stream** | Enable / Disable L2 order book streaming |
| **Depth Levels** | Number of price levels to capture (default: 10) |

For Interactive Brokers, the **IB Options** panel lets you specify:
- `LocalSymbol` – IB-specific symbol override (e.g. `PCG PRA` for a preferred share)
- `Exchange` – Routing exchange (default: `SMART` for smart-routing)
- `Primary Exchange` – Listing exchange (e.g. `NYSE`, `NASDAQ`)

Click **➕ Add Symbol**. The symbol appears in the table and streaming begins immediately (assuming the provider connection is active).

> **Removing a symbol:** click the delete action in the symbol's table row.

---

## 6. Step 5 – Run a Historical Backfill

The **Historical Backfill** section lets you populate the local archive with OHLCV bar data from free public providers without needing an exchange subscription.

### Available free providers

| Provider | Coverage |
|----------|---------|
| **Stooq (free EOD)** | Daily OHLCV for US equities and ETFs (`.US` suffix applied automatically) |
| **Yahoo Finance (free)** | Daily adjusted OHLCV for global equities, ETFs, and indices |
| **Nasdaq Data Link (Quandl)** | Alternative and financial datasets; requires a free API key |

### Running a backfill

1. Select a **Data Provider** from the dropdown.
2. Enter a comma-separated list of **Symbols** (e.g. `AAPL, MSFT, GOOGL`).
3. Set **Start Date** and **End Date** in `YYYY-MM-DD` format (UTC).
4. Click **⏳ Start Backfill**.

The **Backfill Status** terminal at the bottom of the card streams progress in real time:

```
$ Ready to start backfill operation...
$ [13:45:02] Starting backfill for AAPL (2024-01-01 → 2024-12-31)
$ [13:45:04] AAPL: 252 bars downloaded
$ [13:45:04] Backfill complete
```

### Backfill via CLI

You can also trigger backfills from the command line without the dashboard:

```bash
# Backfill AAPL and MSFT for 2025 using Stooq
dotnet run --project src/Meridian/Meridian.csproj -- \
  --backfill \
  --backfill-provider stooq \
  --backfill-symbols AAPL,MSFT \
  --backfill-from 2025-01-01 \
  --backfill-to 2025-12-31

# Resume an interrupted backfill
dotnet run --project src/Meridian/Meridian.csproj -- \
  --backfill --resume --backfill-symbols QQQ

# Use Polygon.io for intraday data (requires API key)
dotnet run --project src/Meridian/Meridian.csproj -- \
  --backfill --backfill-provider polygon --backfill-symbols SPY
```

---

## 7. Step 6 – Derivatives Tracking (Optional)

The **Derivatives Tracking** panel enables continuous options data collection including Greeks, open interest, and full option chains.

### Enable tracking

1. Tick **Enable Derivatives Tracking**.
2. Enter **Underlying Symbols** (e.g. `SPY, QQQ, AAPL, MSFT`).
3. Set **Max Days to Expiry** (default: 90) and **Strike Range ±%** (default: 20).

### What is captured

| Setting | Default | Description |
|---------|---------|-------------|
| Capture Greeks | ✅ | delta, gamma, theta, vega, rho, IV per contract |
| Capture Open Interest | ✅ | Daily OI updates |
| Capture Chain Snapshots | ☐ | Full chain snapshot at each interval |
| Snapshot Interval | 300 s | How often to take chain snapshots |

### Expiration filter

Select which expiration cycles to include:

- **Weekly** (0–7 DTE)
- **Monthly** standard expirations
- **Quarterly** (March / June / September / December)
- **LEAPS** (12+ months)

### Index options

Enable the **Index Options** sub-panel for index-underlying options such as SPX, NDX, RUT, and VIX. You can choose AM-settled and/or PM-settled contracts independently.

Click **💾 Save Derivatives Settings** to apply.

The **Options Live Data** panel on the right shows a live summary: number of tracked contracts, chains, underlyings, and contracts with Greeks.

---

## 8. CLI Workflows

The full command surface is documented in `docs/HELP.md`. Common day-to-day commands:

### Configuration

```bash
# Interactive setup wizard
dotnet run --project src/Meridian/Meridian.csproj -- --wizard

# Validate the current config file
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config

# Show resolved configuration
dotnet run --project src/Meridian/Meridian.csproj -- --show-config

# Reload config without restarting (watch mode)
dotnet run --project src/Meridian/Meridian.csproj -- --watch-config
```

### Diagnostics

```bash
# Quick connectivity check
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check

# Validate provider credentials
dotnet run --project src/Meridian/Meridian.csproj -- --validate-credentials

# Full diagnostic bundle (logs, metrics, config)
dotnet run --project src/Meridian/Meridian.csproj -- --test-connectivity
```

### Symbol management

```bash
# List all subscribed symbols
dotnet run --project src/Meridian/Meridian.csproj -- --symbols

# Add symbols with depth
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add AAPL,MSFT
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-add ES --depth-levels 20

# Remove a symbol
dotnet run --project src/Meridian/Meridian.csproj -- --symbols-remove TSLA
```

### Data packages

```bash
# Create a portable data package
dotnet run --project src/Meridian/Meridian.csproj -- --package --package-name market-data-archive

# Inspect or validate a package
dotnet run --project src/Meridian/Meridian.csproj -- --list-package ./packages/data.zip
dotnet run --project src/Meridian/Meridian.csproj -- --validate-package ./packages/data.zip

# Import a package into the local archive
dotnet run --project src/Meridian/Meridian.csproj -- --import-package ./packages/data.zip
```

---

## 9. Monitoring & Status

### Live event counters

The header bar on the dashboard always shows:

- **Published Events** – total tick events written to the pipeline
- **Dropped Events** – events discarded due to backpressure (should be 0 in normal operation)
- **Integrity Events** – sequence anomalies detected (gap or out-of-order ticks)
- **Historical Bars** – total OHLCV bars persisted by backfill jobs
- **Events/s** – current throughput

### REST API status endpoint

The status JSON is available without authentication:

```bash
curl http://localhost:8200/api/status
```

Sample response:

```json
{
  "isConnected": true,
  "uptime": "00:01:01",
  "metrics": {
    "published": 0,
    "dropped": 0,
    "integrity": 0,
    "historicalBars": 0,
    "eventsPerSecond": 0
  },
  "pipeline": {
    "publishedCount": 0,
    "droppedCount": 0,
    "currentQueueSize": 0,
    "queueCapacity": 50000,
    "queueUtilization": 0
  }
}
```

### Activity log

The **Activity Log** terminal on the dashboard page streams server-sent events (SSE) from the backend. It shows:

- Connection events (`SSE connection established`)
- Configuration load/save events
- Provider connect / disconnect
- Backfill start / progress / completion
- Error events with timestamps

---

## 10. API Reference (Swagger)

Interactive REST API documentation is served at:

```
http://localhost:8200/swagger/index.html
```

The API covers 300+ routes across:

| Category | Example routes |
|----------|---------------|
| Status & health | `GET /api/status`, `GET /api/health` |
| Providers | `GET /api/providers`, `POST /api/providers/connect` |
| Symbols | `GET /api/symbols`, `POST /api/symbols`, `DELETE /api/symbols/{symbol}` |
| Backfill | `POST /api/backfill/start`, `GET /api/backfill/status` |
| Storage | `GET /api/storage/catalog`, `GET /api/storage/search` |
| Config | `GET /api/config`, `PUT /api/config` |
| Security Master | `GET /api/security-master/search` |
| Execution | `POST /api/execution/orders`, `GET /api/execution/positions` |

> The Swagger endpoint requires the API key header `X-Api-Key` when `MDC_AUTH_MODE` is set to `required`. Set `MDC_AUTH_MODE=optional` for local development to bypass authentication.

---

## Quick-reference: workflow checklist

```
□ 1. Launch the server          make run-ui  (or dotnet run -- --ui)
□ 2. Open the dashboard         http://localhost:8200
□ 3. Select provider            Data Provider → choose IB or Alpaca
□ 4. Set storage path           Storage Configuration → Data Root Path
□ 5. Add a data source          Data Sources → Add/Edit form → Save
□ 6. Add symbols                Subscribed Symbols → Add New Symbol
□ 7. Run backfill               Historical Backfill → select dates → Start
□ 8. (Optional) derivatives     Derivatives Tracking → Enable → Save
□ 9. Monitor                    Header counters + Activity Log
```
