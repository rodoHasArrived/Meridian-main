# Operator Runbook

Use [Provider Confidence Baseline](../providers/provider-confidence-baseline.md) as the source of truth for what Meridian validates offline today for Polygon, NYSE, Interactive Brokers, and StockSharp. This runbook focuses on operator workflows and deliberately avoids implying broader provider readiness than the repo evidence supports.

## Startup

### Headless / Test Mode
```bash
dotnet run --project src/Meridian/Meridian.csproj
```

This runs a smoke test with simulated data. Output is written to `./data/`.

### Self-Test Mode
```bash
dotnet run --project src/Meridian/Meridian.csproj -- --selftest
```

Runs built-in self-tests (e.g., `DepthBufferSelfTests`) and exits.

### Production (with Live Data)
```bash
dotnet build -p:DefineConstants=IBAPI  # Only needed if using IB provider
dotnet run --project src/Meridian/Meridian.csproj -- --watch-config --http-port 8080
```

- `--watch-config`: Enables hot reload of `appsettings.json` (handled by `ConfigWatcher`)
- `--http-port 8080`: Starts HTTP monitoring server on port 8080 (recommended for production monitoring)
- Building with `IBAPI` enables the official Interactive Brokers vendor path only when the vendor DLL/project reference is present; use the baseline/provider setup docs to distinguish that from simulation or smoke-build modes.

### UI
```bash
dotnet run --project src/Meridian.Ui/Meridian.Ui.csproj
```

---

## Hot Reload

* Edit `appsettings.json`
* Or use UI
* Changes applied without restart (when `--watch-config` is enabled)

Supported:
* Add/remove symbols
* Toggle trades/depth subscriptions
* Change depth levels
* Switch between configured data providers

---

## Integrity Events

### Trade Integrity
`TradeDataCollector` validates sequence numbers for each symbol/stream:
- **OutOfOrder**: Trade rejected if sequence <= previous
- **SequenceGap**: Trade accepted but stats marked stale if sequence skips

### Depth Integrity
`MarketDepthCollector` validates order book operations:
- **Gap**: Insert position out of range
- **OutOfOrder**: Update position doesn't exist
- **InvalidPosition**: Delete position doesn't exist
- **Stale**: Stream frozen from previous error

If integrity events spike:
1. Check provider connectivity
2. Verify market data entitlements
3. Call `ResetSymbolStream(symbol)` or resubscribe affected symbol
4. Inspect JSONL output in `data/`

---

## Monitoring

### HTTP Monitoring Server

Start the built-in HTTP server for real-time monitoring:

```bash
dotnet run -- --http-port 8080
```

Access monitoring endpoints:
- **Dashboard**: http://localhost:8080/ (auto-refreshing HTML dashboard)
- **Prometheus metrics**: http://localhost:8080/metrics
- **JSON status**: http://localhost:8080/status

#### Available Metrics

Prometheus-compatible metrics exposed at `/metrics`:

| Metric | Type | Description |
|--------|------|-------------|
| `mdc_published` | counter | Total events published to pipeline |
| `mdc_dropped` | counter | Events dropped due to backpressure |
| `mdc_integrity` | counter | Integrity validation events |
| `mdc_trades` | counter | Trade events processed |
| `mdc_depth_updates` | counter | Market depth updates processed |
| `mdc_quotes` | counter | Quote updates processed |
| `mdc_events_per_second` | gauge | Current event throughput rate |
| `mdc_drop_rate` | gauge | Drop rate percentage |

#### Integration with Prometheus

Add this scrape configuration to `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'meridian'
    static_configs:
      - targets: ['localhost:8080']
    metrics_path: '/metrics'
    scrape_interval: 5s
```

#### Dashboard Features

The HTML dashboard at `/` provides:
- Real-time metrics display with auto-refresh (2 second interval)
- Table of recent integrity events with timestamps and details
- Links to raw Prometheus and JSON endpoints

### Programmatic Access

Access counters via `Metrics` static class in code:
- `Metrics.Published`: Total events written to pipeline
- `Metrics.Dropped`: Events dropped due to backpressure
- `Metrics.Integrity`: Integrity events emitted

### Legacy Status File (Removed)

> **Removed:** The `--serve-status` option is deprecated. Use `--ui` or `--http-port` instead.

The legacy `--serve-status` option has been removed. Use `--ui` to start the web dashboard which provides real-time access to the same information via `/status`, `/metrics`, and `/health` endpoints.

---

## Shutdown

Use Ctrl+C:
* Subscriptions cancelled
* `EventPipeline` drained and flushed
* Files flushed via `JsonlStorageSink`
* Clean disconnect from providers

---

## Canonical Startup Scripts

### Linux/macOS
From repo root:
```bash
chmod +x START_COLLECTOR.exp STOP_COLLECTOR.exp
./START_COLLECTOR.exp
```

Stop:
```bash
./STOP_COLLECTOR.exp
```

Environment toggles:
- `USE_IBAPI=true|false`
- `START_UI=true|false`
- `BUILD=true|false`
- `DOTNET_CONFIGURATION=Release|Debug`
- `IB_HOST`, `IB_PORT`, `IB_CLIENT_ID`

### Windows (PowerShell)
Start:
```powershell
powershell -ExecutionPolicy Bypass -File .\START_COLLECTOR.ps1
```

Stop:
```powershell
powershell -ExecutionPolicy Bypass -File .\STOP_COLLECTOR.ps1
```

### systemd (Linux service)
Unit file included at:
`deploy/systemd/meridian.service`

Typical install (example):
```bash
sudo mkdir -p /opt/meridian
sudo rsync -a ./ /opt/meridian/
sudo cp deploy/systemd/meridian.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now meridian
sudo journalctl -u meridian -f
```

---

## Preflight Checklist (built-in)

Startup scripts run a preflight step before building/starting:

- Disk space (warn if < 2GB free)
- Directory permissions (data/logs/run writable)
- Config sanity:
  - counts of symbols with trades/depth enabled
  - note: L2 depth requires provider depth entitlements
- Provider reachability (only when using IB with `USE_IBAPI=true`)
  - **Auto-detects port** by testing: `7497, 4002, 7496, 4001`
  - uses the first reachable port unless `IB_PORT` is explicitly set

If preflight fails, the startup script aborts with errors.

---

## Data Provider Configuration

The collector supports multiple data providers through the `DataSource` configuration option.

### Interactive Brokers (IB)

Set `DataSource` to `IB` in `appsettings.json`:

```json
{
  "DataSource": "IB",
  "Symbols": [
    { "Symbol": "AAPL", "SubscribeTrades": true, "SubscribeDepth": true, "DepthLevels": 10 }
  ]
}
```

Build with IBAPI support:
```bash
dotnet build -p:DefineConstants=IBAPI
```

Offline baseline before local vendor checks:
```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~IBRuntimeGuidanceTests|FullyQualifiedName~IBOrderSampleTests"
```

Notes:
- Non-`IBAPI` builds stay on `IBSimulationClient` / runtime-guidance mode rather than real broker connectivity.
- `EnableIbApiSmoke=true` is compile-only verification, not a live runtime check.
- Use [`docs/providers/interactive-brokers-setup.md`](../providers/interactive-brokers-setup.md) for the three-mode setup guidance.

### Alpaca

Set `DataSource` to `Alpaca` in `appsettings.json`:

```json
{
  "DataSource": "Alpaca",
  "Alpaca": {
    "KeyId": "YOUR_KEY_ID",
    "SecretKey": "YOUR_SECRET_KEY",
    "Feed": "iex",
    "UseSandbox": false,
    "SubscribeQuotes": true
  },
  "Symbols": [
    { "Symbol": "AAPL", "SubscribeTrades": true, "SubscribeDepth": false }
  ]
}
```

Notes:
- Alpaca real-time stock data is provided via WebSocket streams with message authentication and subscribe actions. (See Alpaca docs.)
- This integration supports **trade prints** (`T:"t"` messages).
- Quote support (`T:"q"` messages) requires `SubscribeQuotes: true` and is wired to `QuoteCollector`.
- Full Level-2 depth is not supported for stocks via Alpaca.

### Polygon

Set `DataSource` to `Polygon` in `appsettings.json`:

```json
{
  "DataSource": "Polygon",
  "Symbols": [
    { "Symbol": "AAPL", "SubscribeTrades": true }
  ]
}
```

Notes:
- Meridian's Polygon client is a real websocket parser for trades, quotes, and aggregates.
- The repo baseline validates Polygon primarily through committed replay fixtures and targeted parser/subscription tests rather than live vendor connectivity.
- Use [`docs/providers/provider-confidence-baseline.md`](../providers/provider-confidence-baseline.md) for the exact offline commands and fixture paths.

### Startup scripts
If you set `DataSource` to `Alpaca` or `Polygon`, set `USE_IBAPI=false` in the startup scripts (or omit it). IB connectivity checks are skipped when IB is disabled.

### NYSE Streaming

Use NYSE when you need exchange-oriented trade, quote, and depth semantics and you have NYSE credentials plus the required feed entitlements.

Notes:
- The repo baseline validates NYSE lifecycle behavior offline through `NyseMarketDataClient` / `NYSEDataSource` tests.
- Live websocket and REST checks still require local credentials and entitlement validation.
- Treat Level 2 depth as entitlement-dependent.

### StockSharp

Use StockSharp when you need a connector-runtime path that Meridian's native providers do not cover directly.

Notes:
- The default repo baseline validates stub guidance and representative connector metadata without claiming that every connector is live in the current build.
- Real StockSharp use requires `EnableStockSharp=true`, the needed connector packages, and any local vendor software that connector depends on.
- Use [`docs/providers/stocksharp-connectors.md`](../providers/stocksharp-connectors.md) for supported connector types and runtime expectations.

---

## Quote Context for Aggressor Inference

When using quote-capable providers, the system can ingest **quote (BBO)** updates and use them to infer trade aggressor side:

- Trade price >= Ask => Buy aggressor
- Trade price <= Bid => Sell aggressor
- Otherwise => Unknown

To enable quote ingestion in Alpaca mode, set:

```json
{
  "Alpaca": { "SubscribeQuotes": true }
}
```

This will emit `MarketEventType.BboQuote` events with `BboQuotePayload` and improve `Trade` + `OrderFlow` aggressor classification.

To confirm quotes are flowing:

```bash
ls data/AAPL.BboQuote.jsonl
tail -n 5 data/AAPL.BboQuote.jsonl
```

Each record includes `SequenceNumber`, `StreamId`, and `Venue` fields so you can reconcile feeds across providers.

---
## Storage management

These settings and policies keep the storage tier healthy and predictable. Use the configuration snippets and quotas below whenever you tune retention, cleanup, or tiering for a given deployment.

## Storage Configuration

### File Organization

The storage system supports multiple file naming conventions and partitioning strategies. Configure in `appsettings.json`:

```json
{
  "DataRoot": "data",
  "Compress": true,
  "Storage": {
    "NamingConvention": "BySymbol",
    "DatePartition": "Daily",
    "IncludeProvider": false,
    "FilePrefix": null,
    "RetentionDays": 30,
    "MaxTotalMegabytes": 10240
  }
}
```

### Naming Conventions

Choose the organization strategy that matches your workflow:

| Convention | Path Pattern | Best For |
|------------|-------------|----------|
| `BySymbol` | `{root}/{symbol}/{type}/{date}.jsonl` | Analyzing individual symbols over time (default) |
| `ByDate` | `{root}/{date}/{symbol}/{type}.jsonl` | Daily batch processing and archival |
| `ByType` | `{root}/{type}/{symbol}/{date}.jsonl` | Analyzing event types across symbols |
| `Flat` | `{root}/{symbol}_{type}_{date}.jsonl` | Small datasets, simple browsing |

### Date Partitioning

Control file granularity with `DatePartition`:

- **`None`** – Single file per symbol/type (continuous append)
- **`Daily`** – One file per day (default, balanced)
- **`Hourly`** – One file per hour (high-volume scenarios)
- **`Monthly`** – One file per month (long-term storage)

### Retention Policies

Automatic cleanup to manage disk usage:

**Time-based retention** (`RetentionDays`):
- Deletes files older than specified days
- Runs during each write operation
- Example: `"RetentionDays": 30` keeps last 30 days of data

**Capacity-based retention** (`MaxTotalMegabytes`):
- Enforces storage cap by removing oldest files first
- Measured across all files in data root
- Example: `"MaxTotalMegabytes": 10240` limits storage to 10 GB

**Both policies can be combined** – whichever limit is hit first triggers cleanup.

### Compression

Enable gzip compression to reduce disk usage:

```json
{
  "Compress": true
}
```

Files are written with `.jsonl.gz` extension. The replayer automatically decompresses during playback.

### Provider Tagging

Include data source in file paths for multi-provider deployments:

```json
{
  "Storage": {
    "IncludeProvider": true
  }
}
```

Results in paths like: `data/IB/AAPL/Trade/2024-01-15.jsonl`

---

## Data Replay

Replay historical data for backtesting and analysis using `JsonlReplayer`:

```csharp
using Meridian.Storage.Replay;

var replayer = new JsonlReplayer("./data");
await foreach (var evt in replayer.ReadEventsAsync(cancellationToken))
{
    Console.WriteLine($"[{evt.Timestamp:O}] {evt.EventType}: {evt.Symbol}");

    // Process event (trades, quotes, depth, integrity)
    switch (evt.EventType)
    {
        case MarketEventType.Trade:
            // Handle trade
            break;
        case MarketEventType.BboQuote:
            // Handle quote
            break;
    }
}
```

Features:
- Automatically discovers and reads all `.jsonl` and `.jsonl.gz` files in directory tree
- Events are returned in chronological order based on file names
- Supports filtering with LINQ (`.Where()`, `.Take()`, etc.)
- Handles decompression transparently

Example: Replay only trades for specific symbol:

```csharp
var trades = replayer.ReadEventsAsync(ct)
    .Where(e => e.EventType == MarketEventType.Trade && e.Symbol == "AAPL");
```

---

## Historical Data Backfill

### Running a Backfill

**Via Command Line:**
```bash
# Basic backfill with Alpaca
dotnet run -- --backfill --backfill-provider alpaca --backfill-symbols SPY,QQQ \
  --backfill-from 2024-01-01 --backfill-to 2024-12-31

# Composite provider with automatic failover
dotnet run -- --backfill --backfill-provider composite --backfill-symbols SPY
```

**Via Configuration:**
```json
{
  "Backfill": {
    "Enabled": true,
    "Provider": "composite",
    "EnableFallback": true,
    "RateLimitRotation": true,
    "SkipExistingData": true,
    "Symbols": ["SPY", "QQQ", "AAPL"],
    "From": "2024-01-01",
    "To": "2024-12-31"
  }
}
```

### Available Historical Providers

| Provider | ID | Priority | Free | Notes |
|----------|-----|----------|------|-------|
| Alpaca | `alpaca` | 5 | Yes (IEX) | Requires API keys, includes trades/quotes |
| Yahoo Finance | `yahoo` | 10 | Yes | 50K+ global securities, EOD only |
| Stooq | `stooq` | 20 | Yes | US equities, EOD only |
| Nasdaq Data Link | `nasdaq` | 30 | Limited | Requires API key |
| Composite | `composite` | - | - | Automatic failover across all above |

### Monitoring Backfill Jobs

**Dashboard Endpoints:**
- `GET /api/backfill/status` - Current job status
- `GET /api/backfill/jobs` - List all jobs
- `POST /api/backfill/jobs` - Start new job
- `DELETE /api/backfill/jobs/{id}` - Cancel job

**Backfill Logs:**
```bash
tail -f data/_logs/backfill-*.log
```

### Gap Detection and Fill

Run gap analysis:
```bash
dotnet run -- --analyze-gaps --symbols SPY,QQQ --from 2024-01-01 --to 2024-12-31
```

Fill detected gaps only:
```bash
dotnet run -- --backfill --fill-gaps-only --symbols SPY,QQQ
```

---

## WPF Desktop Application

### Starting the Desktop App

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj
```

### Desktop App Pages

| Page | Purpose |
|------|---------|
| **Dashboard** | Real-time metrics, sparklines, data health gauge |
| **Provider** | Configure IB/Alpaca, connection health monitoring |
| **Storage** | Disk usage analytics, tiered storage (hot/warm/cold) |
| **Symbols** | Add/edit symbols, subscription management |
| **Backfill** | Schedule and monitor historical data jobs |
| **Settings** | Theme, notifications, keyboard shortcuts |
| **Trading Hours** | Market hours, session schedules |
| **Data Export** | Export to JSONL, Parquet, CSV |

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | Add new symbol |
| `Ctrl+S` | Save configuration |
| `Ctrl+R` | Refresh data |
| `Ctrl+B` | Start backfill |
| `F5` | Refresh dashboard |
| `Ctrl+,` | Open settings |

---

## Microservices Deployment

For high-throughput deployments, run as microservices:

```bash
cd src/Microservices

# Start all services with Docker Compose
docker compose -f docker-compose.microservices.yml up -d

# With monitoring (Prometheus + Grafana)
docker compose -f docker-compose.microservices.yml --profile monitoring up -d
```

### Service Ports

| Service | Port | Purpose |
|---------|------|---------|
| Gateway | 5000 | API entry point |
| Trade | 5001 | Trade processing |
| OrderBook | 5002 | L2 order book |
| Quote | 5003 | BBO/NBBO quotes |
| Historical | 5004 | Backfill management |
| Validation | 5005 | Data quality |

See [ADR-003: Microservices Decomposition](../adr/003-microservices-decomposition.md) for architecture and trade-offs.

---

## Preferred Stock Configuration (IB-specific)

For IB preferred shares (e.g., PCG-PA, PCG-PB), use explicit `LocalSymbol` to avoid ambiguity:

```json
{
  "Symbol": "PCG-PA",
  "SubscribeTrades": true,
  "SubscribeDepth": true,
  "DepthLevels": 10,
  "SecurityType": "STK",
  "Exchange": "SMART",
  "Currency": "USD",
  "PrimaryExchange": "NYSE",
  "LocalSymbol": "PCG PRA"
}
```

This ensures `ContractFactory` resolves to the correct IB contract.

---

**Version:** 1.6.1
**Last Updated:** 2026-01-30
**See Also:** [Configuration](../HELP.md#configuration) | [Troubleshooting](../HELP.md#troubleshooting) | [Architecture](../architecture/overview.md) | [Lean Integration](../integrations/lean-integration.md)
