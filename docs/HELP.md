# Meridian - User Guide

Welcome to the Meridian! This tool helps you **build your own market data archive** by connecting to financial data providers, capturing market data in real-time, and storing everything locally for research, backtesting, and algorithmic trading.

**Why use this tool?**

- **Own your data** — Everything is stored locally in JSONL/Parquet files, not locked in a vendor's cloud
- **Save money** — Use free-tier APIs strategically, pay only for premium data you actually need
- **Stay reliable** — Automatic reconnection, provider failover, and data quality monitoring
- **Stay flexible** — Switch providers without losing data, collect exactly what you need

This guide covers everything from installation to advanced configuration.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Auto-Configuration](#auto-configuration)
  - [Configuration Wizard](#configuration-wizard)
  - [Quick Auto-Configuration](#quick-auto-configuration)
  - [Provider Detection](#provider-detection)
  - [Credential Validation](#credential-validation)
- [Installation](#installation)
- [Configuration](#configuration)
  - [Configuration File](#configuration-file-location)
  - [Environment Variables](#environment-variables)
  - [Hot Reload](#hot-reload)
- [Data Providers](#data-providers)
  - [Interactive Brokers](#interactive-brokers-ib)
  - [Alpaca](#alpaca)
  - [Polygon](#polygon)
  - [NYSE](#nyse)
  - [StockSharp](#stocksharp)
- [Multi-Provider Support](#multi-provider-support)
  - [Simultaneous Connections](#simultaneous-connections)
  - [Circuit Breaker Pattern](#circuit-breaker-pattern-v13)
  - [Concurrent Provider Executor](#concurrent-provider-executor-v13)
- [Historical Backfill](#historical-backfill)
  - [Backfill Providers](#backfill-providers)
  - [Priority Backfill Queue](#priority-backfill-queue-v13)
  - [Data Gap Detection & Repair](#data-gap-detection--repair-v13)
  - [Data Quality Monitoring](#data-quality-monitoring-v13)
- [Storage Settings](#storage-settings)
  - [Naming Conventions](#naming-conventions)
  - [Date Partitioning](#date-partitioning)
  - [Compression](#compression)
- [Symbol Management](#symbol-management)
- [Archival-First Storage](#archival-first-storage-v15)
- [Analysis-Ready Exports](#analysis-ready-exports-v15)
- [QuantConnect Lean Integration](#quantconnect-lean-integration)
- [Offline Storage & Archival](#offline-storage--archival)
- [Web Dashboard](#web-dashboard)
- [Windows Desktop App](#windows-desktop-app)
- [Command Line Usage](#command-line-usage)
- [Makefile Commands](#makefile-commands)
- [Health Endpoints & Monitoring](#health-endpoints--monitoring)
- [Security Best Practices](#security-best-practices)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Support and Resources](#support-and-resources)

---

## Overview

**Version:** 1.7.2 | **Status:** Development / Pilot Ready

### What You Can Do

| Task | How It Helps You |
|------|------------------|
| **Stream real-time data** | Capture live trades, quotes, and order book depth from Interactive Brokers, Alpaca, NYSE, Polygon, or StockSharp |
| **Download historical data** | Backfill years of price data from 10+ providers (Yahoo Finance, Tiingo, Polygon, etc.) with automatic failover |
| **Store data locally** | Own your data in structured JSONL or Parquet files—no vendor lock-in, full offline access |
| **Monitor data quality** | Automatic validation catches missing data, sequence gaps, and anomalies |
| **Export and package** | Create portable data packages for sharing, backup, or use in other tools |
| **Run backtests** | Feed your collected data into QuantConnect Lean for algorithmic strategy development |

### Technical Stack

Built on **.NET 9.0** using **C# 13** and **F# 8.0**. Supports deployment as a self-contained executable, Docker container, or systemd service. Includes a web dashboard. A WPF desktop app for Windows exists in `src/Meridian.Wpf/` and is included in the solution build (full WPF application on Windows; CI stub on Linux/macOS).

---

## Quick Start

### 1. Start the Application

If this is your first run, use the installer orchestrator first so dependencies and runtime mode are configured consistently:

```bash
# Interactive installer (Docker or Native)
./build/scripts/install/install.sh

# Or choose a mode explicitly
./build/scripts/install/install.sh --docker
./build/scripts/install/install.sh --native
```

On Windows, use the equivalent PowerShell installer:

```powershell
.\build\scripts\install\install.ps1
.\build\scripts\install\install.ps1 -Mode Docker
.\build\scripts\install\install.ps1 -Mode Native
```

**Option A: Web Dashboard (Cross-platform)**

The easiest way to get started is with the web dashboard:

```bash
# Using the compiled executable
./Meridian --ui

# Or using dotnet run
dotnet run --project src/Meridian/Meridian.csproj -- --ui --http-port 8080

# Or using Make
make run-ui
```

Then open your browser to `http://localhost:8080`

**Option B: Windows Desktop App (WPF)**

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj
```

### 2. Configure Your Data Provider

Choose your preferred real-time data provider:

- **Interactive Brokers**: Best for Level 2 market depth data
- **Alpaca**: Best for real-time US equities with free tier available
- **Polygon**: Comprehensive market data with tick-level granularity
- **NYSE**: Direct exchange feeds for NYSE-listed securities
- **StockSharp**: Multi-exchange connectivity with unified API

### 3. Add Symbols to Track

Add the stock symbols you want to collect data for (e.g., AAPL, MSFT, TSLA)

### 4. Configure Storage

Choose where and how you want data to be stored

### 5. Start Collecting

Run the collector in production mode:

```bash
./Meridian --http-port 8080 --watch-config
```

---

## Auto-Configuration

Meridian includes user-friendly auto-configuration features to help new users get started quickly.

### Configuration Wizard

The interactive configuration wizard is the recommended way for new users to set up the application:

```bash
./Meridian --wizard
```

The wizard guides you through a step-by-step process:

1. **Provider Detection** - Automatically detects available providers from environment variables
2. **Use Case Selection** - Choose your primary use case (research, trading, backtesting)
3. **Data Source Configuration** - Select and configure your data provider
4. **Symbol Setup** - Add symbols you want to track
5. **Storage Configuration** - Choose storage format and location
6. **Backfill Setup** - Configure historical data backfill preferences
7. **Review & Confirm** - Review your configuration before saving

The wizard generates a complete `appsettings.json` file ready for use.

### Quick Auto-Configuration

If you prefer a non-interactive setup and have environment variables configured:

```bash
./Meridian --auto-config
```

Auto-configuration:

- Detects available providers from environment variables
- Selects the highest-priority provider with valid credentials
- Generates a sensible default configuration
- Creates `config/appsettings.json` automatically

**Supported Environment Variables:**

```bash
# Alpaca
export ALPACA_KEY_ID=your-key-id
export ALPACA_SECRET_KEY=your-secret-key

# Polygon
export POLYGON_API_KEY=your-api-key

# Tiingo
export TIINGO_API_TOKEN=your-token

# Finnhub
export FINNHUB_API_KEY=your-api-key

# Alpha Vantage
export ALPHA_VANTAGE_API_KEY=your-api-key
```

### Provider Detection

Check which providers are available and their status:

```bash
./Meridian --detect-providers
```

This shows:

- Available providers and their display names
- Whether credentials are configured
- Missing credential information
- Provider capabilities (RealTime, Historical, L2Depth, etc.)
- Suggested priority order

### Credential Validation

Validate your API credentials without starting the collector:

```bash
./Meridian --validate-credentials
```

This:

- Tests connectivity to each configured provider
- Validates API key format and authentication
- Reports any credential issues with helpful error messages
- Suggests fixes for common problems

### Generate Configuration Template

Create a configuration template to customize manually:

```bash
./Meridian --generate-config
```

This creates a `config/appsettings.json` template with all available options commented and documented.

---

## Installation

### Golden Path (Recommended)

Use the installation orchestrator script for all setups. It keeps Docker and native installs consistent across platforms.

```bash
# Interactive installer (Docker or Native)
./build/scripts/install/install.sh

# Or choose a mode explicitly
./build/scripts/install/install.sh --docker
./build/scripts/install/install.sh --native
```

Access the dashboard at `http://localhost:8080`.

### Windows Installation

> **WSL 2 prerequisite (Docker mode):** The Docker-based installation requires Windows Subsystem for Linux 2. Run `wsl --status` to verify WSL 2 is active. If WSL is not installed or needs updating, see [Troubleshooting Issue #10](#10-catastrophic-failure-when-updating-wsl-on-windows) for setup steps and fixes.

The PowerShell installer mirrors the same workflow on Windows.

```powershell
# Interactive installation
.\build\scripts\install\install.ps1

# Or specify mode directly
.\build\scripts\install\install.ps1 -Mode Docker
.\build\scripts\install\install.ps1 -Mode Native
```

### Optional Make Wrappers

Make targets are available as thin wrappers around installation and runtime commands:

```bash
make help
make docker
make run-ui
make test
make doctor
```

### Manual Prerequisites (if not using installer)

- **Operating System**: Windows, Linux, or macOS
- **.NET Runtime**: .NET 9.0 (included in self-contained builds)
- **Disk Space**: Depends on the number of symbols and data retention
- **Network**: Internet connection for data providers

### Download and Install (Manual)

1. Download the appropriate executable for your platform:
   - Windows: `Meridian-win-x64.exe`
   - Linux: `Meridian-linux-x64`
   - macOS: `Meridian-osx-x64`

2. Make executable (Linux/macOS):

   ```bash
   chmod +x Meridian-linux-x64
   ```

3. Create configuration file:

   ```bash
   cp config/appsettings.sample.json config/appsettings.json
   ```

4. Edit `config/appsettings.json` with your settings

### Building from Source

```bash
# Clone the repository
git clone https://github.com/rodoHasArrived/Meridian.git
cd Meridian

# Build in Release mode
dotnet build -c Release

# Or using Make
make build

# Run tests
make test
```

---

## Configuration

### Configuration File Location

The application looks for `appsettings.json` in these locations (in order):

1. `./config/appsettings.json` (recommended)
2. `./appsettings.json`
3. Same directory as the executable

### Basic Configuration

```json
{
  "DataRoot": "data",
  "Compress": false,
  "DataSource": "IB",
  "Symbols": [
    {
      "Symbol": "AAPL",
      "SubscribeTrades": true,
      "SubscribeDepth": true,
      "DepthLevels": 10,
      "SecurityType": "STK",
      "Exchange": "SMART",
      "Currency": "USD"
    }
  ]
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `DataRoot` | string | "data" | Directory where market data will be stored |
| `Compress` | boolean | false | Enable gzip compression for storage files |
| `DataSource` | string | "IB" | Data provider: "IB", "Alpaca", "Polygon", "NYSE", or "StockSharp" |
| `Symbols` | array | [] | List of symbols to collect data for |

### Environment Variables

API credentials should be set via environment variables for security. Use double underscore (`__`) for nested configuration sections.

```bash
# Interactive Brokers
export IB_HOST=127.0.0.1
export IB_PORT=7497
export IB_CLIENT_ID=17

# Alpaca
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key

# Polygon
export POLYGON__APIKEY=your-api-key

# NYSE
export NYSE__APIKEY=your-api-key

# Tiingo (for historical backfill)
export TIINGO__TOKEN=your-token

# Alpha Vantage
export ALPHAVANTAGE__APIKEY=your-api-key

# Finnhub
export FINNHUB__APIKEY=your-api-key
```

**Note:** Environment variables override values in `appsettings.json`.

### Hot Reload

When running with `--watch-config`, the application will automatically reload configuration changes without restarting. This allows you to:

- Add or remove symbols
- Change storage settings
- Update provider credentials (when using file-based config)

---

## Data Providers

### Interactive Brokers (IB)

**Requirements:**

- TWS (Trader Workstation) or IB Gateway running
- API connections enabled in TWS/Gateway settings
- Valid IB account

**Setup:**

1. Start TWS or IB Gateway
2. Enable API connections:
   - Go to File > Global Configuration > API > Settings
   - Check "Enable ActiveX and Socket Clients"
   - Note the Socket Port (default: 7497 for TWS, 4001 for Gateway)

3. Configure in `appsettings.json`:

   ```json
   {
     "DataSource": "IB"
   }
   ```

4. Set environment variables:

   ```bash
   export IB_HOST=127.0.0.1
   export IB_PORT=7497
   export IB_CLIENT_ID=17
   ```

**Supported Data Types:**

- Tick-by-tick trades
- Level 2 market depth (order book)
- Best bid/offer quotes
- Market microstructure data

See [docs/providers/interactive-brokers-setup.md](providers/interactive-brokers-setup.md) for detailed setup instructions.

### Alpaca

**Requirements:**

- Alpaca account (free tier available)
- API credentials from Alpaca dashboard

**Setup:**

1. Sign up at https://alpaca.markets
2. Get your API Key ID and Secret Key from the dashboard
3. Configure in the web UI or `appsettings.json`:

```json
{
  "DataSource": "Alpaca",
  "Alpaca": {
    "KeyId": "YOUR_KEY_ID",
    "SecretKey": "YOUR_SECRET_KEY",
    "Feed": "iex",
    "UseSandbox": false,
    "SubscribeQuotes": true
  }
}
```

**Or via environment variables (recommended):**

```bash
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key
```

**Feed Options:**

- `iex`: Free IEX feed (delayed)
- `sip`: Paid SIP feed (real-time)
- `delayed_sip`: Delayed SIP feed

**Supported Data Types:**

- Real-time trades
- Real-time quotes
- Bar/candlestick data

See [docs/providers/alpaca-setup.md](providers/alpaca-setup.md) for detailed setup instructions.

### Polygon

**Requirements:**

- Polygon.io account
- API key from Polygon dashboard

**Setup:**

```json
{
  "DataSource": "Polygon",
  "Polygon": {
    "ApiKey": "YOUR_API_KEY",
    "WebSocketUrl": "wss://socket.polygon.io/stocks"
  }
}
```

**Or via environment variables:**

```bash
export POLYGON__APIKEY=your-api-key
```

**Supported Data Types:**

- Real-time trades
- Real-time quotes
- Aggregates (bars)
- Options data (with appropriate subscription)

### NYSE

**Requirements:**

- NYSE market data subscription
- API credentials from NYSE

**Setup:**

```json
{
  "DataSource": "NYSE",
  "NYSE": {
    "ApiKey": "YOUR_API_KEY",
    "Endpoint": "wss://feeds.nyse.com"
  }
}
```

**Or via environment variables:**

```bash
export NYSE__APIKEY=your-api-key
```

**Supported Data Types:**

- Direct exchange trades
- Order book snapshots
- Market depth

### StockSharp

**Requirements:**

- Build with `EnableStockSharp=true` to restore StockSharp packages (defaults to false).
- StockSharp core + connector packages installed (StockSharp.Algo, StockSharp.Messages, StockSharp.BusinessEntities, plus connector package)
- Broker/exchange credentials and transport endpoints (Rithmic/IQFeed/CQG/IB)

**Setup:**

```json
{
  "DataSource": "StockSharp",
  "StockSharp": {
    "Enabled": true,
    "ConnectorType": "Rithmic",
    "AdapterType": "",
    "AdapterAssembly": "",
    "EnableRealTime": true,
    "EnableHistorical": true,
    "UseBinaryStorage": false,
    "StoragePath": "data/stocksharp/{connector}",
    "Rithmic": {
      "Server": "Rithmic Test",
      "UserName": "${MDC_STOCKSHARP_RITHMIC_USERNAME}",
      "Password": "${MDC_STOCKSHARP_RITHMIC_PASSWORD}",
      "CertFile": "${MDC_STOCKSHARP_RITHMIC_CERTFILE}",
      "UsePaperTrading": true
    }
  }
}
```

**Environment Variables (recommended):**

- `MDC_STOCKSHARP_CONNECTOR` - Connector type (Rithmic, IQFeed, CQG, InteractiveBrokers, Custom)
- `MDC_STOCKSHARP_ADAPTER_TYPE` - Adapter type for custom connectors (full type name)
- `MDC_STOCKSHARP_ADAPTER_ASSEMBLY` - Adapter assembly name (if needed)
- `MDC_STOCKSHARP_RITHMIC_SERVER` / `MDC_STOCKSHARP_RITHMIC_USERNAME` / `MDC_STOCKSHARP_RITHMIC_PASSWORD`
- `MDC_STOCKSHARP_IQFEED_HOST` / `MDC_STOCKSHARP_IQFEED_LEVEL1_PORT`
- `MDC_STOCKSHARP_CQG_USERNAME` / `MDC_STOCKSHARP_CQG_PASSWORD`
- `MDC_STOCKSHARP_IB_HOST` / `MDC_STOCKSHARP_IB_PORT` / `MDC_STOCKSHARP_IB_CLIENT_ID`

**Supported Connectors:**

- Rithmic
- IQFeed
- CQG
- Interactive Brokers
- Custom adapters via `AdapterType` (StockSharp connector catalog)

**Notes:**

- Use `{connector}` in `StoragePath` to segment storage per connector (e.g., `data/stocksharp/rithmic`).
- Toggle `EnableRealTime`/`EnableHistorical` to run a single connector for both streaming and backfill.
- Use `ConnectionParams` to pass connector-specific settings (e.g., API keys, host overrides).

**Validation Checklist:**

1. Validate config + credentials:

   ```bash
   Meridian --validate-config
   Meridian --validate-credentials
   ```

2. Validate real-time flow:

   ```bash
   Meridian --dry-run
   ```

3. Validate backfill flow (independent of real-time provider):

   ```bash
   Meridian --backfill
   ```

---

## Multi-Provider Support

Meridian supports connecting to multiple data providers simultaneously for enhanced data quality and reliability.

### Simultaneous Connections

Connect to multiple providers at the same time to:

- Compare data quality across sources
- Implement automatic failover
- Collect from multiple sources for reconciliation

**Via Web Dashboard:**

1. Navigate to "Multi-Provider Connections" section
2. Click "Add Provider Connection"
3. Configure provider ID, type, and credentials
4. Repeat for additional providers

**Configuration Example:**

```json
{
  "DataSources": {
    "Sources": [
      {
        "Id": "ib_primary",
        "Name": "Interactive Brokers Primary",
        "Provider": "IB",
        "Priority": 1,
        "Enabled": true
      },
      {
        "Id": "alpaca_backup",
        "Name": "Alpaca Backup",
        "Provider": "Alpaca",
        "Priority": 2,
        "Enabled": true,
        "Alpaca": {
          "KeyId": "YOUR_KEY",
          "SecretKey": "YOUR_SECRET"
        }
      }
    ],
    "EnableFailover": true,
    "FailoverTimeoutSeconds": 30
  }
}
```

### Provider Comparison

Compare data quality metrics side-by-side across all connected providers:

| Metric | Description |
|--------|-------------|
| Data Quality Score | Overall score (0-100%) based on connection stability, latency, and drop rate |
| Trades Received | Total trade events received |
| Depth Updates | Total order book updates |
| Average Latency | Mean message processing latency |
| Messages Dropped | Events dropped due to backpressure |
| Connection Success Rate | Percentage of successful connection attempts |

Access via the "Provider Comparison" section in the web dashboard or `/api/multiprovider/comparison` endpoint.

### Automatic Failover

Configure automatic failover rules to maintain data collection when providers fail:

**Failover Rule Configuration:**

```json
{
  "FailoverRules": [
    {
      "Id": "primary_failover",
      "PrimaryProviderId": "ib_primary",
      "BackupProviderIds": ["alpaca_backup", "polygon_tertiary"],
      "FailoverThreshold": 3,
      "RecoveryThreshold": 5,
      "DataQualityThreshold": 70,
      "MaxLatencyMs": 1000
    }
  ]
}
```

**Failover Triggers:**

- **Consecutive Failures**: Failover after N consecutive connection/data failures
- **Data Quality**: Failover when quality score drops below threshold
- **Latency**: Failover when latency exceeds maximum acceptable value

**Auto-Recovery**: When the primary provider recovers, subscriptions automatically migrate back.

### Circuit Breaker Pattern

The circuit breaker pattern prevents cascading failures by temporarily disabling unhealthy providers:

**States:**

- **Closed**: Normal operation, requests flow through
- **Open**: Provider failed, requests immediately rejected (fast-fail)
- **Half-Open**: Testing recovery with single request

**Configuration:**

```json
{
  "CircuitBreaker": {
    "FailureThreshold": 5,
    "OpenDuration": "00:01:00",
    "SuccessThreshold": 1,
    "SlidingWindow": "00:05:00"
  }
}
```

The circuit opens after 5 consecutive failures, waits 1 minute, then allows a test request.

### Concurrent Provider Executor

Execute operations across multiple providers in parallel:

**Strategies:**

- `All`: Return all results from all providers
- `FirstSuccess`: Stop on first successful result
- `HighestPriority`: Return result from highest priority provider
- `Merge`: Combine results from all providers

**Configuration:**

```json
{
  "ConcurrentExecution": {
    "MaxConcurrency": 4,
    "PerProviderTimeout": "00:00:30",
    "ContinueOnError": true
  }
}
```

### Provider Symbol Mapping

Different providers may use different symbols for the same security. Configure mappings to normalize symbols:

**Via Web Dashboard:**

1. Navigate to "Provider Symbol Mapping" section
2. Add canonical symbol and provider-specific variants
3. Optionally include FIGI, ISIN, or CUSIP identifiers

**Example Mappings:**

| Canonical | IB | Alpaca | Polygon | FIGI |
|-----------|-----|--------|---------|------|
| BRK.B | BRK B | BRK.B | BRK.B | BBG000DWG505 |
| PCG.PRA | PCG PRA | PCG-A | PCG/A | BBG00123ABC |

**Import/Export:** Use CSV for bulk symbol mapping management.

---

## Historical Backfill

Download historical data to fill gaps or get initial dataset.

### Using the Web Dashboard

1. Navigate to "Historical Backfill" section
2. Select provider from dropdown
3. Enter comma-separated symbols: `AAPL,MSFT,TSLA`
4. Select date range
5. Click "Start Backfill"

### Using Command Line

```bash
./Meridian --backfill \
  --backfill-provider stooq \
  --backfill-symbols AAPL,MSFT \
  --backfill-from 2024-01-01 \
  --backfill-to 2024-12-31
```

### Using Makefile

```bash
make run-backfill SYMBOLS=SPY,AAPL PROVIDER=tiingo FROM=2024-01-01 TO=2024-12-31
```

### Backfill Providers

Available historical data providers (in priority order):

| Provider | Free Tier | Data Types | Rate Limits | Notes |
|----------|-----------|------------|-------------|-------|
| **Alpaca** | Yes (with account) | Bars, trades, quotes | 200/min | Best for recent data |
| **Polygon** | Limited | Bars, trades, quotes, aggregates | Varies | Comprehensive coverage |
| **Tiingo** | Yes | Daily bars | 500/hour | Good for daily OHLCV |
| **Yahoo Finance** | Yes | Daily bars | Unofficial | Unofficial API |
| **Stooq** | Yes | Daily bars | Low | US equities, indices, ETFs |
| **Finnhub** | Yes | Daily bars | 60/min | Wide coverage |
| **Alpha Vantage** | Yes | Daily bars | 5/min | Slow but reliable |
| **Nasdaq Data Link** | Limited | Various | Varies | Premium data available |

**Configure fallback chain:**

```json
{
  "Backfill": {
    "ProviderPriority": ["alpaca", "tiingo", "stooq", "yahoo"]
  }
}
```

### Backfill Data Format

Historical bars are converted to the same JSONL format as real-time data for consistency:

```json
{"timestamp":"2024-01-15T21:00:00Z","symbol":"AAPL","type":"HistoricalBar","open":150.0,"high":152.5,"low":149.5,"close":151.75,"volume":50000000}
```

### Priority Backfill Queue

Sophisticated job scheduling with priority levels and dependencies:

**Priority Levels:**

| Priority | Value | Use Case |
|----------|-------|----------|
| Critical | 0 | System-critical gaps |
| High | 10 | User-requested immediate |
| Normal | 50 | Standard backfill |
| Low | 100 | Background fill |
| Deferred | 200 | Fill when idle |

**Features:**

- Dependency chains between jobs
- Automatic retry with exponential backoff
- Pause/resume individual jobs
- Concurrent execution with limits

### Data Gap Detection & Repair

Automatically detect and repair gaps in historical data:

**Gap Detection:**

- Compares stored data against trading calendar
- Identifies missing dates and partial data
- Calculates coverage percentage

**Gap Repair:**

- Fetches missing data from alternate providers
- Configurable provider priority
- Continues on individual failures
- Rate-limit aware with request delays

**Gap Types:**

| Type | Severity | Description |
|------|----------|-------------|
| Missing | Critical | No data for date |
| Partial | Warning | Incomplete data |
| Holiday | Info | Expected market closure |

### Data Quality Monitoring

Multi-dimensional quality scoring for all stored data:

**Quality Dimensions:**

| Dimension | Weight | Checks |
|-----------|--------|--------|
| Completeness | 30% | Gap coverage |
| Accuracy | 25% | Price ranges, OHLCV validity |
| Timeliness | 20% | Data freshness |
| Consistency | 15% | Duplicate detection |
| Validity | 10% | Format, constraints |

**Quality Grades:** A+ (95%+) to F (<50%)

**Alerts:** Automatic alerts when quality drops below threshold (default: 80%)

See [docs/providers/backfill-guide.md](providers/backfill-guide.md) for detailed backfill documentation.

---

## Storage Settings

### Naming Conventions

The naming convention determines how files are organized in directories:

#### 1. Flat

All files in one directory: `{root}/{prefix}{symbol}_{type}_{date}.jsonl`

**Example:** `data/market_AAPL_Trade_2024-01-15.jsonl`

**Use When:** You have a small number of symbols and want simple organization

#### 2. By Symbol (Recommended)

Organized by symbol, then data type: `{root}/{symbol}/{type}/{prefix}{date}.jsonl`

**Example:** `data/AAPL/Trade/2024-01-15.jsonl`

**Use When:** You want to easily access all data for a specific symbol

#### 3. By Date

Organized by date, then symbol: `{root}/{date}/{symbol}/{prefix}{type}.jsonl`

**Example:** `data/2024-01-15/AAPL/Trade.jsonl`

**Use When:** You want to process data by time periods

#### 4. By Type

Organized by data type, then symbol: `{root}/{type}/{symbol}/{prefix}{date}.jsonl`

**Example:** `data/Trade/AAPL/2024-01-15.jsonl`

**Use When:** You want to analyze specific data types across all symbols

### Date Partitioning

Controls how data is split across time periods:

- **None**: Single file per symbol/type combination
- **Daily** (Recommended): New file each day
- **Hourly**: New file each hour (for high-frequency trading)
- **Monthly**: New file each month (for long-term storage)

### Compression

Enable gzip compression to reduce disk space usage:

```json
{
  "Compress": true
}
```

**Savings:** Typically 80-90% reduction in file size

**Trade-off:** Slightly slower write performance (usually negligible)

### Data Format

All data is stored in **JSON Lines (JSONL)** format:

- One JSON object per line
- Easy to stream and process
- Human-readable
- Compatible with many tools (pandas, jq, etc.)

**Example Trade Event:**

```json
{"timestamp":"2024-01-15T14:30:00.123Z","symbol":"AAPL","type":"Trade","price":150.25,"size":100,"aggressorSide":"Buy"}
```

See [docs/architecture/storage-design.md](architecture/storage-design.md) for detailed storage architecture.

---

## Symbol Management

### Adding Symbols

#### Via Web Dashboard

1. Navigate to the "Subscribed Symbols" section
2. Fill in symbol details
3. Click "Add Symbol"

#### Via Configuration File

```json
{
  "Symbols": [
    {
      "Symbol": "AAPL",
      "SubscribeTrades": true,
      "SubscribeDepth": false,
      "DepthLevels": 10,
      "SecurityType": "STK",
      "Exchange": "SMART",
      "Currency": "USD"
    }
  ]
}
```

### Symbol Options

| Option | Type | Description |
|--------|------|-------------|
| `Symbol` | string | Ticker symbol (e.g., "AAPL") |
| `SubscribeTrades` | boolean | Collect trade/tick data |
| `SubscribeDepth` | boolean | Collect Level 2 order book (IB only) |
| `DepthLevels` | integer | Number of price levels to track (5-20 typical) |
| `SecurityType` | string | "STK", "OPT", "IND_OPT", "FOP", "FUT", "SSF", "CASH", "CMDTY", "CRYPTO", "CFD", "BOND", "FUND", "WAR", "BAG", "MARGIN", etc. |
| `Exchange` | string | Exchange routing (IB: "SMART" recommended) |
| `Currency` | string | "USD", "EUR", etc. |
| `LocalSymbol` | string | IB local symbol for specific securities |
| `PrimaryExchange` | string | Primary listing exchange (e.g., "NYSE") |

### Removing Symbols

#### Via Web Dashboard

Click the "Delete" button next to the symbol

#### Via Configuration File

Remove the symbol object from the `Symbols` array

---

## Archival-First Storage

Meridian includes an archival-first storage pipeline designed for crash-safe, long-term data preservation.

### Write-Ahead Logging (WAL)

All market events are written to a Write-Ahead Log before being committed to primary storage:

**Features:**

- Crash-safe persistence with transaction semantics
- Per-record SHA256 checksums for integrity verification
- Configurable sync modes for durability vs. performance tradeoff
- Automatic recovery of uncommitted records after crash

**Configuration:**

```json
{
  "Archival": {
    "EnableWal": true,
    "SyncMode": "BatchedSync",
    "SyncBatchSize": 1000,
    "MaxFlushDelay": "00:00:05"
  }
}
```

**Sync Modes:**

- **NoSync**: Fastest, relies on OS buffering
- **BatchedSync**: Balanced performance and durability (default)
- **EveryWrite**: Maximum durability, slowest

### Compression Profiles

Optimize storage based on access patterns with tier-specific compression:

**Pre-built Profiles:**

| Profile | Codec | Level | Speed | Ratio | Use Case |
|---------|-------|-------|-------|-------|----------|
| Real-Time Collection | LZ4 | 1 | ~500 MB/s | 2.5x | Live data capture |
| Warm Archive | ZSTD | 6 | ~150 MB/s | 5x | Frequently accessed data |
| Cold Archive | ZSTD | 19 | ~20 MB/s | 10x | Long-term storage |
| High-Volume Symbols | ZSTD | 3 | ~300 MB/s | 3.5x | SPY, QQQ, AAPL, etc. |
| Portable Export | Gzip | 6 | ~100 MB/s | 6x | Maximum compatibility |

### Schema Versioning

Ensure long-term data compatibility with schema versioning:

**Features:**

- Semantic versioning for all event types (e.g., Trade v1.0.0, v2.0.0)
- Automatic migration between schema versions
- JSON Schema export for external tool integration
- Schema registry with version history

**Built-in Schemas:**

- Trade v1.0.0: Basic trade event
- Trade v2.0.0: Extended with TradeId and Conditions
- Quote v1.0.0: Best bid/offer quote

---

## Analysis-Ready Exports

Export collected data in formats optimized for external analysis tools.

### Export Profiles

**Python/Pandas:**

- Format: Parquet with datetime64[ns]
- Compression: Snappy
- Includes: `load_data.py` loader script

**R Statistics:**

- Format: CSV with proper NA handling
- Timestamps: ISO 8601
- Includes: `load_data.R` loader script

**QuantConnect Lean:**

- Format: Native Lean data format
- Structure: `/equity/usa/tick/{symbol}/{date}_{type}.zip`
- Resolution: Tick-level data

**PostgreSQL/TimescaleDB:**

- Format: CSV with COPY command
- Includes: `create_tables.sql` DDL script
- Includes: `load_data.sh` loader script

**Microsoft Excel:**

- Format: XLSX with multiple sheets
- Max records: 1,000,000 per file

### Data Quality Reports

Generate analysis-focused quality reports with each export:

**Generated Reports:**

- `quality_report.md` - Human-readable summary
- `quality_report.json` - Machine-readable data
- `outliers.csv` - Detected price outliers (>4σ)
- `gaps.csv` - Data gap inventory
- `quality_issues.csv` - Issue tracker

**Quality Metrics:**

- Completeness scoring (% of expected trading time)
- Outlier detection with Z-scores
- Gap classification (weekend, overnight, unexpected)
- Descriptive statistics (mean, median, percentiles)
- Quality grading (A+ to F)

**Recommendations:**

- Suitability assessment for backtesting, ML training, research
- Preprocessing suggestions for detected issues

### Running an Export

**Via Web Dashboard:**

1. Navigate to "Data Export" section
2. Select export profile (Python, R, Lean, etc.)
3. Choose symbols and date range
4. Click "Export"
5. Download data package with quality report

**Via Command Line:**

```bash
./Meridian --export \
  --profile python-pandas \
  --symbols AAPL,MSFT \
  --from 2026-01-01 \
  --to 2026-01-31 \
  --output ./exports
```

---

## QuantConnect Lean Integration

Meridian integrates with the QuantConnect Lean Engine for backtesting and algorithmic trading.

### Setup

1. Install QuantConnect Lean Engine
2. Configure Lean data path:

```json
{
  "Lean": {
    "DataPath": "/path/to/Lean/Data",
    "Resolution": "Tick",
    "Format": "Native"
  }
}
```

### Export to Lean Format

```bash
./Meridian --export \
  --profile quantconnect-lean \
  --symbols SPY,AAPL \
  --from 2024-01-01 \
  --to 2024-12-31 \
  --output /path/to/Lean/Data
```

### Data Structure

Lean expects data in this structure:

```text
Data/
├── equity/
│   └── usa/
│       ├── tick/
│       │   └── spy/
│       │       ├── 20240115_trade.zip
│       │       └── 20240115_quote.zip
│       └── daily/
│           └── spy.zip
```

See [docs/integrations/lean-integration.md](integrations/lean-integration.md) for detailed Lean integration documentation.

---

## Offline Storage & Archival

Meridian includes tools for managing archived data offline.

### Portable Data Packager

Create self-contained archive packages for data portability and backup:

**Features:**

- Package data by symbol, date range, or event type
- Include manifests and schemas for self-documentation
- SHA256 checksums for integrity verification
- Optional encryption for sensitive data
- Multiple formats: ZIP, TAR.GZ, 7Z

**Via Web Dashboard:**

1. Navigate to "Archive Browser" section
2. Select files/folders to package
3. Choose package format and options
4. Click "Create Package"

**Package Structure:**

```text
MarketData_2026-01.zip
├── manifest.json         # Package metadata
├── README.md             # Usage documentation
├── schemas/              # JSON schemas for event types
│   ├── Trade_schema.json
│   └── Quote_schema.json
├── data/                 # Market data files
│   ├── AAPL/
│   └── MSFT/
├── verification/
│   └── checksums.sha256  # File integrity checksums
```

### Data Completeness Calendar

Visualize data coverage and identify gaps across your archive:

**Features:**

- Calendar heatmap showing completeness by date
- Per-symbol completeness tracking
- Gap detection with trading calendar awareness
- One-click backfill for missing dates
- Completeness scoring (0-100%)

**Completeness Status Colors:**

- **Green (>99%)**: Complete data
- **Yellow (95-99%)**: Minor gaps
- **Orange (80-95%)**: Significant gaps
- **Red (<80%)**: Major issues
- **Gray**: Non-trading day (weekend/holiday)

**Via WPF Desktop App:**

1. Navigate to "Data Completeness" page
2. Select date range and symbols
3. View calendar heatmap
4. Click any day for drill-down details
5. Use "Backfill Gaps" to queue missing data

### Archive Browser

Browse and inspect archived data files:

**Navigation:**

- Tree view: Year → Month → Day → Symbol → Event Type
- File metadata: size, checksum, event count, timestamps
- Quick preview: first/last 100 events without full load

**File Operations:**

- **Preview**: View sample events
- **Verify**: Check file integrity
- **Compare**: Detect duplicates or changes
- **Export**: Copy files to another location
- **Search**: Find events by timestamp or content

**Via WPF Desktop App:**

1. Navigate to "Archive Browser" page
2. Browse the hierarchical tree
3. Right-click files for context menu
4. Use search bar for filtering

### Batch Export Scheduler

Automate recurring data exports:

**Job Configuration:**

```json
{
  "Name": "Daily Python Export",
  "SourcePath": "/data",
  "DestinationPath": "/exports/{year}/{month}/",
  "Symbols": ["AAPL", "MSFT", "GOOGL"],
  "EventTypes": ["Trade", "BboQuote"],
  "DateRange": "yesterday",
  "Format": "parquet",
  "Schedule": {
    "Frequency": "Daily",
    "TimeOfDay": "06:00"
  },
  "IncrementalMode": true
}
```

**Export Formats:**

- **Raw**: Original JSONL files (optionally decompressed)
- **CSV**: Comma-separated values for Excel/spreadsheets
- **Parquet**: Columnar format for Python/pandas
- **JSON Lines**: Decompressed JSONL

**Schedule Frequencies:**

- Hourly
- Daily (with specific time)
- Weekly (with day of week)
- Monthly (with day of month)

**Via Web Dashboard:**

1. Navigate to "Batch Export" section
2. Create new export job
3. Configure schedule and format
4. Monitor job status and history

---

## Web Dashboard

### Starting the Dashboard

```bash
./Meridian --ui
```

Access at: `http://localhost:8080`

### Custom Port

```bash
./Meridian --ui --http-port 9000
```

### Dashboard Features

1. **System Status**
   - Connection state
   - Real-time metrics
   - Last update timestamp

2. **Data Provider**
   - Switch between providers
   - Configure credentials
   - View provider-specific settings

3. **Storage Settings**
   - Configure data directory
   - Set naming convention
   - Enable compression
   - Preview file paths

4. **Historical Backfill**
   - Download historical data
   - View backfill status
   - Check progress

5. **Symbol Management**
   - Add/remove symbols
   - Configure subscription options
   - Manage IB-specific settings

### Dashboard Notifications

The dashboard shows toast notifications for:

- Successful operations
- Errors and failures
- Informational messages

---

## Windows Desktop App

The WPF desktop application provides a native Windows experience for configuring and monitoring Meridian.

### Starting the Desktop App

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj
```

### Desktop App Features

The application includes dedicated pages for all collector functions:

1. **Dashboard Page**
   - Real-time system status
   - Live metrics and statistics
   - Connection state indicator
   - Event throughput monitoring

2. **Provider Page**
   - Select data provider (IB, Alpaca, Polygon)
   - Configure provider-specific settings
   - Test connection status

3. **Storage Page**
   - Configure data directory
   - Select naming convention
   - Set date partitioning options
   - Enable/disable compression
   - Preview file path structure

4. **Symbols Page**
   - Add and remove symbols
   - Configure subscription options
   - Set security type and exchange
   - Manage depth levels

5. **Backfill Page**
   - Select backfill provider
   - Enter symbols to backfill
   - Configure date range
   - Start and monitor backfill progress

6. **Settings Page**
   - General application settings
   - Logging configuration
   - Advanced options

### Secure Credential Management

The desktop app uses Windows CredentialPicker for secure API key management:

**How it works:**

1. Navigate to the Provider page
2. Click "Set Credentials" for your chosen provider
3. Windows CredentialPicker dialog appears
4. Enter your API credentials securely
5. Credentials are stored in Windows Credential Manager

**Benefits:**

- Credentials never stored in plain text files
- Protected by Windows security
- Integrated with Windows Hello (biometric auth)
- Separate from application data

**Supported Credentials:**

- Interactive Brokers: User ID (no password required for API)
- Alpaca: API Key ID and Secret Key
- Polygon: API Key

### Desktop App vs Web Dashboard

| Feature | Desktop App | Web Dashboard |
|---------|-------------|---------------|
| Platform | Windows only | Any browser |
| Credential Storage | Windows Credential Manager | appsettings.json |
| UI Framework | WPF/XAML | HTML/CSS/JavaScript |
| Offline Use | Yes | Requires collector running |
| Native Integration | Full Windows features | Browser sandboxed |

**When to use Desktop App:**

- Windows-only deployment
- Secure credential management is critical
- Native Windows experience preferred
- Integration with Windows ecosystem

**When to use Web Dashboard:**

- Cross-platform access
- Remote monitoring
- Quick configuration changes
- Existing browser workflow

---

## Command Line Usage

### Basic Modes

#### First-Time Setup Mode (Recommended for New Users)

```bash
./Meridian --wizard
```

Interactive wizard that guides you through configuration step by step.

#### Quick Auto-Configuration Mode

```bash
./Meridian --auto-config
```

Automatically configures the application based on environment variables.

#### Monitoring Mode (Recommended)

```bash
./Meridian --http-port 8080 --watch-config
```

- `--http-port 8080`: Enable HTTP monitoring endpoints
- `--watch-config`: Auto-reload configuration changes

#### Web Dashboard Mode

```bash
./Meridian --ui
```

#### Backfill Mode

```bash
./Meridian --backfill
```

#### Replay Mode (Testing)

```bash
./Meridian --replay /path/to/data.jsonl
```

### All Command Line Options

| Option | Description |
|--------|-------------|
| `--wizard` | Interactive configuration wizard (recommended for new users) |
| `--auto-config` | Quick auto-configuration from environment variables |
| `--detect-providers` | Show available providers and their status |
| `--validate-credentials` | Validate configured API credentials |
| `--generate-config` | Generate a configuration template |
| `--ui` | Start web dashboard interface |
| `--http-port <port>` | Enable HTTP monitoring endpoint on specified port |
| `--watch-config` | Enable hot-reload of configuration |
| `--backfill` | Run historical data backfill |
| `--replay <path>` | Replay events from JSONL file |
| `--selftest` | Run system self-tests |
| `--validate-config` | Validate configuration without starting |
| `--dry-run` | Comprehensive validation without starting |
| `--export` | Export data to analysis format |
| `--http-port <port>` | Set HTTP server port (default: 8080) |
| `--status-port <port>` | Set status endpoint port |
| `--backfill-provider <name>` | Backfill provider to use |
| `--backfill-symbols <list>` | Comma-separated symbols to backfill |
| `--backfill-from <date>` | Backfill start date (YYYY-MM-DD) |
| `--backfill-to <date>` | Backfill end date (YYYY-MM-DD) |
| `--profile <name>` | Export profile (python-pandas, r-stats, quantconnect-lean) |
| `--output <path>` | Export output directory |

### Examples

**Start with web UI:**

```bash
./Meridian --ui
```

**Run backfill for specific symbols:**

```bash
./Meridian --backfill \
  --backfill-symbols AAPL,MSFT,GOOGL \
  --backfill-from 2024-01-01 \
  --backfill-to 2024-12-31
```

**Deployment with custom port:**

```bash
./Meridian --http-port 9090 --watch-config
```

**Export to Python/Pandas format:**

```bash
./Meridian --export \
  --profile python-pandas \
  --symbols SPY,AAPL \
  --output ./exports
```

---

## Makefile Commands

The project includes a Makefile for common tasks:

### Build & Test

```bash
make build        # Build the project in Release mode
make test         # Run all tests
make test-fsharp  # Run F# tests only
make clean        # Clean build artifacts
```

### Run Application

```bash
make run          # Run with default settings
make run-ui       # Run with web dashboard
make run-backfill # Run historical backfill
```

### Docker

```bash
make docker       # Build and run Docker container
make docker-build # Build Docker image only
make docker-push  # Push to container registry
```

### Documentation

```bash
make docs         # Generate documentation
make verify-adrs  # Verify ADR compliance
```

### Diagnostics

```bash
make doctor       # Run full diagnostic check
make diagnose     # Build diagnostics (buildctl)
make metrics      # Show build metrics
make help         # Show all available commands
```

---

## Health Endpoints & Monitoring

### Health Endpoints

When running with `--http-port` or `--ui`, the following endpoints are available:

| Endpoint | Description |
|----------|-------------|
| `/` | HTML dashboard (auto-refreshing) |
| `/status` | JSON status with metrics |
| `/metrics` | Prometheus metrics |
| `/health` | Health check (returns 200 if healthy) |
| `/ready` | Readiness probe (Kubernetes) |
| `/live` | Liveness probe (Kubernetes) |

### Status Endpoint

```bash
curl http://localhost:8080/status | jq .
```

**Response:**

```json
{
  "timestampUtc": "2024-01-15T14:30:00Z",
  "isConnected": true,
  "provider": "Alpaca",
  "uptime": "02:15:30",
  "metrics": {
    "published": 150000,
    "dropped": 0,
    "integrity": 5,
    "historicalBars": 1000
  }
}
```

### Prometheus Metrics

```bash
curl http://localhost:8080/metrics
```

**Available Metrics:**

- `marketdata_trades_total` - Total trades processed
- `marketdata_quotes_total` - Total quotes processed
- `marketdata_depth_updates_total` - Total order book updates
- `marketdata_latency_seconds` - Processing latency histogram
- `marketdata_connection_status` - Connection state (0/1)
- `marketdata_backpressure_events_total` - Backpressure events

### Prometheus/Grafana Integration

Deploy the provided monitoring stack:

```bash
cd deploy/monitoring
docker-compose up -d
```

Access Grafana at `http://localhost:3000` (default: admin/admin)

---

## Security Best Practices

### Credential Management

1. **Never store credentials in code or config files**
   - Use environment variables for API keys
   - Use Windows Credential Manager for desktop app
   - Use secrets management (Vault, AWS Secrets Manager) in production

2. **Environment Variable Setup**

   ```bash
   # Create a .env file (add to .gitignore!)
   echo "ALPACA__KEYID=your-key" >> .env
   echo "ALPACA__SECRETKEY=your-secret" >> .env

   # Load in shell
   export $(cat .env | xargs)
   ```

3. **Docker Secrets**

   ```yaml
   services:
     collector:
       environment:
         - ALPACA__KEYID_FILE=/run/secrets/alpaca_key
       secrets:
         - alpaca_key
   ```

### Network Security

1. **Use TLS for remote connections**

   ```json
   {
     "Https": {
       "Enabled": true,
       "CertPath": "/path/to/cert.pem",
       "KeyPath": "/path/to/key.pem"
     }
   }
   ```

2. **Firewall configuration**
   - Only expose required ports (8080 for dashboard)
   - Use private networks for provider connections

3. **API rate limiting**
   - Configure rate limits to avoid provider bans
   - Use built-in rate limit tracking

### Data Security

1. **Encrypt archived data**

   ```json
   {
     "Archival": {
       "Encryption": {
         "Enabled": true,
         "Algorithm": "AES-256-GCM"
       }
     }
   }
   ```

2. **Backup verification**
   - Regularly verify backup integrity
   - Test restore procedures

---

## Troubleshooting

### Common Issues

#### 1. "Configuration file not found"

**Cause:** Missing `appsettings.json`

**Solution:**

```bash
cp config/appsettings.sample.json config/appsettings.json
# Edit config/appsettings.json with your settings
```

#### 2. "Connection failed to Interactive Brokers"

**Causes:**

- TWS/Gateway not running
- API connections not enabled
- Wrong port number

**Solution:**

1. Ensure TWS or IB Gateway is running
2. Check File > Global Configuration > API > Settings
3. Verify "Enable ActiveX and Socket Clients" is checked
4. Confirm port matches your configuration (7497 for TWS, 4001 for Gateway)

#### 3. "Alpaca authentication failed"

**Causes:**

- Invalid API credentials
- Wrong environment (sandbox vs production)

**Solution:**

1. Verify credentials in Alpaca dashboard
2. Check `UseSandbox` setting matches your credentials
3. Ensure KeyId and SecretKey are correct
4. Verify environment variables are set: `echo $ALPACA__KEYID`

#### 4. "Permission denied writing to data directory"

**Cause:** Insufficient file system permissions

**Solution:**

```bash
# Linux/macOS
chmod 755 ./data

# Or specify a different directory you have access to
mkdir -p ~/market-data && chmod 755 ~/market-data
```

#### 5. "High CPU usage"

**Causes:**

- Too many symbols with high-frequency updates
- Insufficient system resources
- Market depth subscriptions on many symbols

**Solution:**

1. Reduce number of subscribed symbols
2. Disable market depth for some symbols (depth is more resource-intensive)
3. Increase system resources
4. Check `EventPipeline` channel capacity in logs

#### 6. "Data files not being created"

**Causes:**

- Collector not receiving data from provider
- Storage path issues
- No active symbols configured

**Solution:**

1. Check system status in dashboard
2. Verify provider connection is established
3. Confirm symbols are correctly configured
4. Check logs for errors: `tail -f data/_logs/*.log`

#### 7. "dotnet restore or build failures"

**Causes:**

- Missing or incompatible dependencies
- NuGet package resolution issues
- Platform-specific targeting issues (e.g., Windows-specific packages on Linux)
- Network issues with NuGet servers

**Solution:**

Use diagnostic logging to identify the issue:

```bash
# Restore with diagnostic logging (solution-level)
dotnet restore /p:EnableWindowsTargeting=true -v diag

# Build with diagnostic logging
dotnet build -c Release -v diag

# Or use Makefile diagnostics
make doctor
```

**Verbosity Levels:**

- `-v q` or `--verbosity quiet` - Minimal output
- `-v m` or `--verbosity minimal` - Basic information
- `-v n` or `--verbosity normal` - Standard output (default)
- `-v d` or `--verbosity detailed` - Detailed information
- `-v diag` or `--verbosity diagnostic` - Extensive diagnostic logging

**Common Issues Revealed by Diagnostic Logs:**

1. **NETSDK1100 Error (Windows-specific TFMs on non-Windows)**

   ```bash
   # Solution: Use EnableWindowsTargeting property
   dotnet restore /p:EnableWindowsTargeting=true
   ```

2. **NuGet Package Not Found**

   ```bash
   dotnet nuget list source
   dotnet nuget locals all --clear
   dotnet restore --force
   ```

3. **Version Conflicts**

   ```bash
   dotnet list package --outdated
   dotnet clean && dotnet restore
   ```

4. **Network/Proxy Issues**
   - Configure NuGet proxy if behind corporate firewall
   - Use offline feed if needed

**Saving Diagnostic Logs:**

```bash
# Save diagnostic output to a file for analysis
dotnet restore /p:EnableWindowsTargeting=true -v diag > restore-diag.log 2>&1

# Then search for specific errors
grep -i "error" restore-diag.log
grep -i "warning" restore-diag.log
```

#### 8. "Provider rate limit exceeded"

**Cause:** Too many API requests to provider

**Solution:**

1. Check `ProviderRateLimitTracker` logs
2. Reduce number of symbols or request frequency
3. Use provider with higher limits
4. Configure request delays in backfill settings

#### 9. "Memory usage keeps growing"

**Causes:**

- Unbounded channel capacity
- Memory leaks in provider client
- Too many concurrent subscriptions

**Solution:**

1. Configure bounded channels:

   ```json
   {
     "EventPipeline": {
       "ChannelCapacity": 10000,
       "BoundedChannelFullMode": "DropOldest"
     }
   }
   ```

2. Enable garbage collection logging
3. Reduce concurrent subscriptions

#### 10. "Catastrophic failure" when updating WSL on Windows

**Context:** The Docker-based DevContainer and `install.ps1 -Mode Docker` workflows require
Windows Subsystem for Linux 2 (WSL 2). Attempting to install or update WSL may fail with:

```text
Downloading: Windows Subsystem for Linux 2.6.3
Installing: Windows Subsystem for Linux 2.6.3
Catastrophic failure
```

**Causes:**

- Command was not run as Administrator (elevation is required)
- Windows Update is pending a reboot
- Corrupted WSL installation state
- Virtual Machine Platform Windows feature not enabled

**Resolution — try in order:**

1. **Run as Administrator**

   Open PowerShell as Administrator and retry:

   ```powershell
   wsl.exe --update
   ```

2. **Install or update from the Microsoft Store**

   Search for "Windows Subsystem for Linux" in the Microsoft Store and click **Update** or **Install**.

3. **Install from GitHub releases (offline / behind firewall)**

   Download the latest `.msixbundle` from the [WSL releases page](https://github.com/microsoft/WSL/releases)
   and double-click to install, or install from an elevated PowerShell:

   ```powershell
   Add-AppxPackage .\Microsoft.WSL_<version>_x64_ARM64.msixbundle
   ```

4. **Enable required Windows features first, then update**

   ```powershell
   # Run as Administrator
   dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
   dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
   Restart-Computer
   # After reboot, from an elevated PowerShell:
   wsl --set-default-version 2
   wsl --update
   ```

5. **Reinstall WSL from scratch**

   ```powershell
   # Run as Administrator — replaces the existing WSL installation
   wsl --install
   ```

**Verify WSL 2 is active after fixing:**

```powershell
wsl --status          # Should report WSL version 2
wsl --list --verbose  # Lists installed distros with their WSL version
```

Once WSL 2 is working, Docker Desktop will use it automatically. Re-run the installer:

```powershell
.\build\scripts\install\install.ps1 -Mode Docker
```

### Logging

Logs are stored in the data directory under `_logs/`:

```text
data/
  _logs/
    collector-2024-01-15.log
```

**Log Levels:**

- **Debug**: Detailed diagnostic information
- **Information**: General informational messages
- **Warning**: Warning messages for non-critical issues
- **Error**: Error messages for failures
- **Fatal**: Critical errors that cause shutdown

**Viewing Logs:**

```bash
# View latest log
tail -f data/_logs/collector-$(date +%Y-%m-%d).log

# Search for errors
grep ERROR data/_logs/*.log

# Count errors by type
grep ERROR data/_logs/*.log | cut -d: -f4 | sort | uniq -c
```

**Configure Log Level:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Meridian": "Debug"
    }
  }
}
```

---

## FAQ

### General

#### Q: How much disk space do I need?

**A:** It depends on:

- Number of symbols
- Data types (trades, depth, quotes)
- Trading hours and activity
- Compression enabled

**Estimates** (per symbol, per day, with compression):

- Trades only: 10-50 MB
- Trades + Depth: 100-500 MB
- Very active stocks (SPY, QQQ): 1+ GB

#### Q: Can I run multiple instances?

**A:** Yes, but:

- Each instance needs its own configuration file
- Each instance needs a unique data directory OR different symbols
- For IB: Each instance needs a unique Client ID

#### Q: Is data collection real-time?

**A:** Yes! Data is captured as it arrives from providers:

- **IB**: True tick-by-tick real-time (with market data subscription)
- **Alpaca**: Real-time WebSocket streaming
- **Latency**: Typically <100ms from exchange to disk

#### Q: Can I use the collected data with other tools?

**A:** Absolutely! Data is in JSONL format, easily loaded by:

- **Python pandas**: `pd.read_json(path, lines=True)`
- **QuantConnect LEAN**: Built-in integration
- **Command line tools**: `jq`, `grep`, etc.
- **Custom tools**: Any JSON parser

### Data Providers

#### Q: Do I need an IB subscription for market data?

**A:** Depends:

- **Real-time data**: Requires IB market data subscription
- **Delayed data**: Available without subscription (15-20 min delay)
- **Paper trading account**: Can access delayed data

#### Q: Which provider is best for beginners?

**A:** Alpaca is recommended for beginners because:

- Free tier available
- Simple API key authentication
- Good documentation
- Real-time US equities data

#### Q: Can I collect options, futures, or forex data?

**A:** Yes! Set the `SecurityType` in symbol configuration:

- `STK`: Stocks
- `ETF`: Exchange-traded funds
- `OPT`: Equity options
- `IND_OPT`: Index options
- `FOP`: Futures options
- `FUT`: Futures
- `SSF`: Single-stock futures
- `CASH`: Forex / spot FX
- `IND`: Indices
- `CMDTY`: Commodities
- `CRYPTO`: Crypto assets
- `CFD`: Contracts for difference
- `BOND`: Bonds
- `FUND`: Funds
- `WAR`: Warrants
- `BAG`: Combination / spread instruments
- `MARGIN`: Margin products

**Example:**

```json
{
  "Symbol": "ESZ4",
  "SecurityType": "FUT",
  "Exchange": "CME",
  "Currency": "USD"
}
```

### Storage & Backup

#### Q: How do I backup my data?

**A:** Simply copy the data directory:

```bash
# Create backup
tar -czf backup-2024-01-15.tar.gz data/

# Restore backup
tar -xzf backup-2024-01-15.tar.gz
```

Consider:

- Cloud storage (AWS S3, Azure Blob, Google Cloud Storage)
- Regular automated backups
- Version control for configuration files

#### Q: What's the best storage format for analysis?

**A:** Depends on your use case:

- **Python/pandas**: Parquet (fastest for large datasets)
- **R**: CSV or Parquet
- **QuantConnect**: Native Lean format
- **Excel**: XLSX (limited to 1M rows)

### Operations

#### Q: Can I run this in Docker?

**A:** Yes! Example Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:9.0
COPY Meridian /app/Meridian
COPY config/appsettings.json /app/appsettings.json
WORKDIR /app
RUN chmod +x Meridian
EXPOSE 8080
ENV ALPACA__KEYID=""
ENV ALPACA__SECRETKEY=""
CMD ["./Meridian", "--http-port", "8080", "--watch-config"]
```

Or use the provided docker-compose:

```bash
cd deploy/docker
docker-compose up -d
```

#### Q: How do I stop the collector gracefully?

**A:** Press `Ctrl+C` or send SIGTERM:

```bash
# The application will:
# 1. Stop accepting new data
# 2. Flush all pending events to disk
# 3. Close connections gracefully
# 4. Exit with status 0
```

#### Q: Can I change symbols without restarting?

**A:** Yes! If running with `--watch-config`:

1. Edit `appsettings.json`
2. Save the file
3. Application will reload automatically
4. New symbols will be subscribed
5. Removed symbols will be unsubscribed

#### Q: How do I monitor the collector in production?

**A:** Several options:

1. **Prometheus/Grafana**: Use `/metrics` endpoint
2. **Health checks**: Use `/health`, `/ready`, `/live` endpoints
3. **Status API**: Use `/status` for JSON metrics
4. **Logs**: Monitor `data/_logs/` directory
5. **Systemd**: Use the provided service file in `deploy/systemd/`

---

## Support and Resources

### Documentation

| Document | Description |
|----------|-------------|
| [README.md](../README.md) | Project overview and quick start |
| [HELP.md](HELP.md) | This comprehensive user guide |
| [getting-started/README.md](getting-started/README.md) | Step-by-step setup guide |
| [Configuration](#configuration) | Configuration reference |
| [Troubleshooting](#troubleshooting) | Troubleshooting guide |
| [operations/operator-runbook.md](operations/operator-runbook.md) | Operations and deployment |
| [architecture/overview.md](architecture/overview.md) | System architecture |
| [architecture/storage-design.md](architecture/storage-design.md) | Storage design details |
| [providers/backfill-guide.md](providers/backfill-guide.md) | Historical data guide |
| [integrations/lean-integration.md](integrations/lean-integration.md) | QuantConnect Lean guide |
| [adr/](adr/) | Architecture Decision Records |

### Provider-Specific Guides

| Guide | Provider |
|-------|----------|
| [providers/interactive-brokers-setup.md](providers/interactive-brokers-setup.md) | Interactive Brokers |
| [providers/alpaca-setup.md](providers/alpaca-setup.md) | Alpaca |
| [providers/provider-comparison.md](providers/provider-comparison.md) | Provider comparison |

### Getting Help

1. **Check the logs**: Most issues are logged with detailed error messages
2. **Run diagnostics**: `make doctor` for comprehensive system check
3. **Review documentation**: Comprehensive docs cover most scenarios
4. **GitHub Issues**: Report bugs or request features at https://github.com/rodoHasArrived/Meridian-main/issues

### Best Practices

1. **Start Small**: Begin with 1-3 symbols, then scale up
2. **Monitor Resources**: Watch CPU, memory, and disk usage
3. **Regular Backups**: Automate data backups
4. **Test First**: Use paper trading or sandbox accounts initially
5. **Update Regularly**: Keep software up to date for bug fixes and features
6. **Review Logs**: Periodically check logs for warnings or errors
7. **Validate Data**: Spot-check collected data for accuracy
8. **Use Environment Variables**: Never commit credentials to version control
9. **Enable Compression**: Reduces storage costs significantly
10. **Set Up Monitoring**: Use Prometheus/Grafana for production deployments

### Contributing

We welcome contributions! If you've found a bug or have a feature request, please open an issue on GitHub.

Before contributing code:

1. Read [CLAUDE.md](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) for coding guidelines
2. Review [docs/adr/](adr/) for architectural decisions
3. Run tests: `make test`
4. Ensure code follows the style guide

---

**Version:** 1.7.2
**Last Updated:** 2026-03-26
**License:** See LICENSE file
