# API Reference

This section describes how API documentation is generated, where to find the key namespaces, and how to keep the published API surface up to date.

## How API docs are generated

API pages are generated from XML documentation comments in the .NET projects and built with DocFX.

### Prerequisites

- .NET SDK (matching the repository's target framework)
- DocFX CLI installed and available on `PATH`

### Build API docs

```bash
docfx docs/docfx/docfx.json
```

### Serve locally for preview

```bash
docfx docs/docfx/docfx.json --serve
```

## API map

Use this map as a starting point before diving into generated type/member pages.

### Domain layer

- `TradeDataCollector` - Tick-by-tick trade processing with sequence validation.
- `MarketDepthCollector` - L2 order book maintenance with integrity checks.
- `QuoteCollector` - BBO cache and quote event emission.
- `SymbolSubscriptionTracker` - Thread-safe subscription management base class.

### Application layer

- `EventPipeline` - Bounded channel for event routing.
- `EventPipelinePolicy` - Shared bounded-channel policy configuration.
- `ConfigurationService` - Wizard, auto-config, validation, and reload lifecycle.
- `JsonlStorageSink` - Append-only JSONL persistence.
- `ParquetStorageSink` - Columnar Parquet persistence (experimental).
- `TieredStorageManager` - Hot/warm/cold storage tiering.
- `StatusHttpServer` - Status + Prometheus metrics endpoint.
- `BackfillService` - Historical backfill orchestration.

### Storage and archival layer

- `WriteAheadLog` - Crash-safe persistence with transaction semantics.
- `ArchivalStorageService` - WAL-backed archival writes with checksums.
- `CompressionProfileManager` - Compression profile selection (LZ4/ZSTD/Gzip).
- `SchemaVersionManager` - Schema versioning + migration coordination.
- `AnalysisExportService` - Analysis-oriented export profiles.
- `AnalysisQualityReport` - Pre-export quality checks and reporting.

### Infrastructure layer

#### Streaming providers

- `IBMarketDataClient` - Interactive Brokers streaming client (IBAPI build flag).
- `AlpacaMarketDataClient` - Alpaca WebSocket client.
- `PolygonMarketDataClient` - Polygon streaming client (stub).
- `StockSharpMarketDataClient` - StockSharp connector (multi-source).

#### Historical providers

- `AlpacaHistoricalDataProvider` - Alpaca REST OHLCV bars.
- `YahooFinanceHistoricalDataProvider` - Yahoo Finance EOD data.
- `StooqHistoricalDataProvider` - Stooq EOD data.
- `NasdaqDataLinkHistoricalDataProvider` - Nasdaq Data Link (Quandl).
- `AlphaVantageHistoricalDataProvider` - Alpha Vantage intraday data.
- `FinnhubHistoricalDataProvider` - Finnhub historical fundamentals/market data.
- `TiingoHistoricalDataProvider` - Tiingo premium market data.
- `PolygonHistoricalDataProvider` - Polygon aggregated historical data.
- `IBHistoricalDataProvider` - Interactive Brokers historical data.
- `CompositeHistoricalDataProvider` - Provider failover and fallback orchestration.
- `BaseHistoricalDataProvider` - Shared HTTP/retry/rate-limit base implementation.

#### Symbol discovery and normalization

- `AlpacaSymbolSearchProvider` - Symbol search via Alpaca.
- `FinnhubSymbolSearchProvider` - Global symbol search via Finnhub.
- `PolygonSymbolSearchProvider` - US equities symbol search via Polygon.
- `OpenFigiSymbolResolver` - Cross-provider symbol normalization via OpenFIGI.
- `SymbolNormalization` - Canonical symbol/venue normalization utilities.

#### Infrastructure utilities

- `HttpResponseHandler` - Centralized API error handling and response parsing.
- `CredentialValidator` - API credential and configuration validation.

### Lean integration

- `MeridianTradeData` - Lean `BaseData` implementation for trades.
- `MeridianQuoteData` - Lean `BaseData` implementation for quotes.
- `MeridianDataProvider` - Lean `IDataProvider` integration.

### F# domain library

- `MarketEvents` - Type-safe discriminated unions for domain events.
- `ValidationPipeline` - Railway-oriented validation and error accumulation.
- `Spread`/`Imbalance`/`Aggregations` - Pure calculation modules.
- `Transforms` - Functional stream transformation pipeline.

## REST API Endpoints

The application exposes a REST API when running with `--ui` or `--mode web`. All `/api/*` endpoints require an API key when `MDC_API_KEY` is set. Swagger UI is available at `/swagger` in development mode.

### Authentication

Set the `MDC_API_KEY` environment variable to enable authentication. Pass the key via:
- `X-Api-Key` header (recommended)
- `api_key` query parameter

Health probes (`/healthz`, `/readyz`, `/livez`) are always exempt.

**Example:**

```bash
# With header (recommended)
curl -H "X-Api-Key: your-api-key" http://localhost:8080/api/status

# With query parameter
curl http://localhost:8080/api/status?api_key=your-api-key
```

When authentication fails, the API returns `401 Unauthorized`:

```json
{ "error": "Unauthorized", "message": "Missing or invalid API key" }
```

### Rate Limiting

- **Global**: 120 requests/minute per API key or IP address
- **Mutations**: POST/PUT/DELETE endpoints have an additional 10 requests/minute limit
- Rate limit headers: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `Retry-After`

When rate-limited, the API returns `429 Too Many Requests` with a `Retry-After` header.

### Common Usage Examples

**Check system status:**

```bash
curl http://localhost:8080/api/status
```

```json
{
  "status": "Running",
  "uptime": "02:15:30",
  "provider": "ALPACA",
  "symbolCount": 5,
  "eventsPerSecond": 42.3,
  "lastEventAt": "2026-02-25T14:30:00Z"
}
```

**Add symbols to monitoring:**

```bash
curl -X POST http://localhost:8080/api/symbols/add \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"symbols": ["AAPL", "MSFT", "GOOGL"]}'
```

**Get live quote for a symbol:**

```bash
curl http://localhost:8080/api/data/quotes/SPY
```

**Run a historical backfill:**

```bash
curl -X POST http://localhost:8080/api/backfill/run \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{
    "provider": "stooq",
    "symbols": ["SPY", "AAPL"],
    "from": "2024-01-01",
    "to": "2024-01-31"
  }'
```

**Check data quality for a symbol:**

```bash
curl http://localhost:8080/api/storage/quality/symbol/SPY
```

### Error Responses

All endpoints return errors in a consistent format:

```json
{
  "error": "ErrorCodeName",
  "message": "Human-readable description of the error",
  "details": { }
}
```

| HTTP Status | Meaning |
|-------------|---------|
| `400` | Bad request — invalid parameters or payload |
| `401` | Unauthorized — missing or invalid API key |
| `404` | Not found — symbol or resource does not exist |
| `429` | Too many requests — rate limit exceeded |
| `500` | Internal server error — unexpected failure |

### Endpoint Groups

| Tag | Count | Description |
|-----|-------|-------------|
| Configuration | 14 | Config CRUD, data source management, derivatives |
| Backfill | 5 | Historical data backfill execution, preview, progress |
| Backfill Checkpoints | 3 | Job checkpoint retrieval and resume |
| Historical Data | 2 | Stored historical data query and date-range lookup |
| Ingestion Jobs | 2 | Resumable ingestion job listing and summary |
| Packaging | 6 | Portable data package creation, import, validation, listing |
| Maintenance | 18 | Archive maintenance schedules, executions, status, presets |
| Providers | 8 | Provider status, metrics, catalog, comparison, latency |
| Failover | 7 | Failover rules, health, force failover |
| Interactive Brokers | 3 | IB-specific status, error codes, API limits |
| Symbol Mapping | 5 | Cross-provider symbol mappings, CSV import |
| Live Data | 6 | Real-time trades, quotes, order book, order flow |
| Symbols | 15 | Symbol CRUD, bulk operations, search, statistics |
| Storage | 19 | Storage stats, health, tiers, search, catalog |
| Storage Quality | 9 | Data quality scores, alerts, trends, anomalies |
| Quality Drops | 2 | Pipeline dropped event audit statistics |
| Health | 12 | System health, Kubernetes probes, Prometheus metrics, SSE |

### Symbols (`/api/symbols/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/symbols` | All configured symbols |
| GET | `/api/symbols/monitored` | Symbols with monitoring config |
| GET | `/api/symbols/archived` | Symbols with stored data |
| GET | `/api/symbols/{symbol}/status` | Detailed status for one symbol |
| POST | `/api/symbols/add` | Add symbols to configuration |
| POST | `/api/symbols/{symbol}/remove` | Remove symbol from monitoring |
| POST | `/api/symbols/{symbol}/archive` | Archive symbol (keep data) |
| POST | `/api/symbols/bulk-add` | Add multiple symbols at once |
| POST | `/api/symbols/bulk-remove` | Remove multiple symbols |
| GET | `/api/symbols/search?q=` | Search configured symbols |
| POST | `/api/symbols/validate` | Validate symbol identifiers |
| GET | `/api/symbols/statistics` | Aggregate symbol statistics |
| GET | `/api/symbols/{symbol}/trades` | Recent trade files |
| GET | `/api/symbols/{symbol}/depth` | Recent depth files |
| POST | `/api/symbols/batch` | Batch add/remove operations |

### Storage (`/api/storage/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/storage/profiles` | Available storage profile presets |
| GET | `/api/storage/stats` | Overall storage statistics |
| GET | `/api/storage/breakdown` | Breakdown by symbol/type |
| GET | `/api/storage/health` | Storage health summary |
| GET | `/api/storage/catalog` | Full storage catalog |
| GET | `/api/storage/search/files?symbol=&q=` | Search stored files |
| GET | `/api/storage/symbol/{symbol}/info` | Storage info for a symbol |
| GET | `/api/storage/symbol/{symbol}/stats` | Detailed symbol stats |
| GET | `/api/storage/symbol/{symbol}/files` | List symbol data files |
| GET | `/api/storage/symbol/{symbol}/path` | Storage path for symbol |
| GET | `/api/storage/cleanup/candidates` | Files eligible for cleanup |
| POST | `/api/storage/cleanup` | Run storage cleanup |
| GET | `/api/storage/archive/stats` | Archive tier statistics |
| GET | `/api/storage/health/check` | Detailed health check |
| GET | `/api/storage/health/orphans` | Find orphaned files |
| GET | `/api/storage/tiers/statistics` | Tier statistics |
| GET | `/api/storage/tiers/plan?days=` | Migration plan preview |
| POST | `/api/storage/tiers/migrate` | Execute tier migration |
| POST | `/api/storage/maintenance/defrag` | Run defragmentation |

### Storage Quality (`/api/storage/quality/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/storage/quality/summary` | Overall quality summary |
| GET | `/api/storage/quality/scores` | Quality scores for all files |
| GET | `/api/storage/quality/symbol/{symbol}` | Quality for a specific symbol |
| GET | `/api/storage/quality/alerts` | Active quality alerts |
| POST | `/api/storage/quality/alerts/{alertId}/acknowledge` | Acknowledge alert |
| GET | `/api/storage/quality/rankings/{symbol}` | Source rankings |
| GET | `/api/storage/quality/trends?days=` | Quality trends over time |
| GET | `/api/storage/quality/anomalies` | Detected quality anomalies |
| POST | `/api/storage/quality/check` | Run quality check on path |

### Configuration (`/api/config/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/config` | Full configuration |
| POST | `/api/config/datasource` | Update active data source |
| POST | `/api/config/alpaca` | Update Alpaca configuration |
| POST | `/api/config/storage` | Update storage settings |
| POST | `/api/config/symbols` | Add or update symbol |
| DELETE | `/api/config/symbols/{symbol}` | Remove symbol |
| GET | `/api/config/derivatives` | Get derivatives configuration |
| POST | `/api/config/derivatives` | Update derivatives configuration |
| GET | `/api/config/datasources` | List all data sources |
| POST | `/api/config/datasources` | Create or update data source |
| DELETE | `/api/config/datasources/{id}` | Delete data source |
| POST | `/api/config/datasources/{id}/toggle` | Toggle source enabled |
| POST | `/api/config/datasources/defaults` | Set default sources |
| POST | `/api/config/datasources/failover` | Update failover settings |

### Backfill (`/api/backfill/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/backfill/providers` | List available providers |
| GET | `/api/backfill/status` | Last backfill status |
| POST | `/api/backfill/run` | Execute backfill |
| POST | `/api/backfill/run/preview` | Preview backfill (dry run) |
| GET | `/api/backfill/progress` | Current operation progress |

### Backfill Checkpoints (`/api/backfill/checkpoints/*`)

Checkpoint endpoints expose the persisted job state that enables backfill operations to be paused and resumed after a restart or failure.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/backfill/checkpoints` | List all available checkpoints |
| GET | `/api/backfill/checkpoints/resumable` | List checkpoints for jobs that can be resumed |
| GET | `/api/backfill/checkpoints/{jobId}` | Checkpoint details for a specific job |
| GET | `/api/backfill/checkpoints/{jobId}/pending` | Symbols still pending for a resumable checkpoint |
| POST | `/api/backfill/checkpoints/{jobId}/resume` | Resume a failed or incomplete backfill job |

### Providers (`/api/providers/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/providers/status` | All provider status |
| GET | `/api/providers/metrics` | Provider metrics |
| GET | `/api/providers/metrics/{providerId}` | Single provider metrics |
| GET | `/api/providers/comparison` | Feature comparison |
| GET | `/api/providers/catalog` | Provider catalog with metadata |
| GET | `/api/providers/catalog/{providerId}` | Single provider catalog entry |
| GET | `/api/providers/latency` | Latency statistics |
| GET | `/api/connections` | Connection health summary |

### Failover (`/api/failover/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/failover/config` | Failover configuration |
| POST | `/api/failover/config` | Update failover settings |
| GET | `/api/failover/rules` | All failover rules |
| POST | `/api/failover/rules` | Create or update rule |
| DELETE | `/api/failover/rules/{id}` | Delete failover rule |
| POST | `/api/failover/force/{ruleId}` | Force failover to provider |
| GET | `/api/failover/health` | Provider health snapshots |

### Interactive Brokers (`/api/providers/ib/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/providers/ib/status` | IB connection status and capabilities |
| GET | `/api/providers/ib/error-codes` | IB error code reference |
| GET | `/api/providers/ib/limits` | IB API limits |

### Symbol Mapping (`/api/symbols/mappings/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/symbols/mappings` | All symbol mappings |
| POST | `/api/symbols/mappings` | Create or update mapping |
| GET | `/api/symbols/mappings/{symbol}` | Single symbol mapping |
| DELETE | `/api/symbols/mappings/{symbol}` | Delete symbol mapping |
| POST | `/api/symbols/mappings/import` | Import mappings from CSV |

### Live Data (`/api/data/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/data/trades/{symbol}` | Recent trades for symbol |
| GET | `/api/data/quotes/{symbol}` | Latest quote for symbol |
| GET | `/api/data/orderbook/{symbol}` | Order book snapshot |
| GET | `/api/data/bbo/{symbol}` | Best bid/offer |
| GET | `/api/data/orderflow/{symbol}` | Order flow statistics |
| GET | `/api/data/health` | Live data health overview |

### Historical Data (`/api/historical/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/historical` | Query stored historical bars for a symbol |
| GET | `/api/historical/symbols` | List symbols that have stored historical data |
| GET | `/api/historical/{symbol}/daterange` | Date range of available data for a symbol |

### Ingestion Jobs (`/api/ingestion/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/ingestion/jobs` | List all ingestion jobs |
| POST | `/api/ingestion/jobs` | Create a new ingestion job (Draft state) |
| GET | `/api/ingestion/jobs/{jobId}` | Get a specific ingestion job |
| DELETE | `/api/ingestion/jobs/{jobId}` | Delete a terminal ingestion job |
| POST | `/api/ingestion/jobs/{jobId}/transition` | Transition job state |
| GET | `/api/ingestion/jobs/resumable` | List jobs eligible for resume (failed or paused with checkpoint) |
| GET | `/api/ingestion/summary` | Summary of all jobs by state and workload type |

### Packaging (`/api/packaging/*`)

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/packaging/create` | Create a portable data package |
| POST | `/api/packaging/import` | Import a package into storage |
| POST | `/api/packaging/validate` | Validate a package's integrity |
| GET | `/api/packaging/list` | List available packages |
| GET | `/api/packaging/contents` | List contents of a specific package (`?path=`) |
| GET | `/api/packaging/download/{fileName}` | Download a package file |
| DELETE | `/api/packaging/{fileName}` | Delete a package file |

### Maintenance (`/api/maintenance/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/maintenance/schedules` | List all maintenance schedules |
| POST | `/api/maintenance/schedules` | Create a maintenance schedule |
| GET | `/api/maintenance/schedules/{scheduleId}` | Get a specific schedule |
| PUT | `/api/maintenance/schedules/{scheduleId}` | Update a schedule |
| DELETE | `/api/maintenance/schedules/{scheduleId}` | Delete a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/enable` | Enable a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/disable` | Disable a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/trigger` | Trigger a schedule immediately |
| GET | `/api/maintenance/schedules/{scheduleId}/executions` | Execution history for a schedule |
| GET | `/api/maintenance/schedules/{scheduleId}/summary` | Summary for a specific schedule |
| GET | `/api/maintenance/schedules/summary` | Summary across all schedules |
| POST | `/api/maintenance/execute` | Run a maintenance task immediately |
| POST | `/api/maintenance/executions/{executionId}/cancel` | Cancel a running execution |
| GET | `/api/maintenance/executions` | List all executions |
| GET | `/api/maintenance/executions/{executionId}` | Get a specific execution |
| GET | `/api/maintenance/executions/failed` | List failed executions |
| POST | `/api/maintenance/executions/cleanup` | Clean up old execution records |
| GET | `/api/maintenance/statistics` | Overall maintenance statistics |
| GET | `/api/maintenance/status` | Current maintenance service status |
| POST | `/api/maintenance/validate-cron` | Validate a cron expression |
| GET | `/api/maintenance/presets` | List available maintenance task presets |
| GET | `/api/maintenance/task-types` | List available task type identifiers |

### Quality Drops (`/api/quality/drops/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/quality/drops` | Dropped event statistics |
| GET | `/api/quality/drops/{symbol}` | Drops for specific symbol |

### Health (`/healthz`, `/api/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/healthz` | Kubernetes health probe |
| GET | `/readyz` | Kubernetes readiness probe |
| GET | `/livez` | Kubernetes liveness probe |
| GET | `/health` | Comprehensive health status |
| GET | `/ready` | Readiness check |
| GET | `/live` | Liveness check |
| GET | `/metrics` | Prometheus metrics |
| GET | `/api/status` | Full system status |
| GET | `/api/health/detailed` | Detailed health check |
| GET | `/api/errors` | Error log with filtering |
| GET | `/api/backpressure` | Backpressure status |
| GET | `/api/events/stream` | Server-Sent Events stream |

## Maintenance checklist

When adding or changing public APIs:

1. Update XML documentation comments in source.
2. Regenerate docs with DocFX.
3. Verify generated pages include new members and examples.
4. Cross-link relevant architecture or provider docs when behavior changes.

---

**See also:** [Architecture Overview](../architecture/overview.md) · [Domain Model](../architecture/domains.md) · [Provider Comparison](../providers/provider-comparison.md)

---

**Version:** 1.6.2
**Last Updated:** 2026-03-16
**Audience:** Contributors maintaining the HTTP API surface and AI assistants working on endpoint documentation.


### Coverage-Reported Gaps (Workstation + Config Alias)

The coverage audit also tracks workstation shell routes and compatibility aliases.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/config/data-sources` | Backward-compatible alias for data source listing (`/api/config/datasources`). |
| POST | `/api/config/data-sources` | Backward-compatible alias for create/update of data sources. |
| GET | `/session` | Legacy/session route marker reported by coverage scanner; workstation session data is served at `/api/workstation/session`. |
| GET | `/research` | Legacy/research route marker reported by coverage scanner; research payload is served at `/api/workstation/research`. |
| GET | `/workstation` | Workstation shell entry point that serves the React index page. |
| GET | `/workstation/{*path}` | SPA fallback route for workstation client-side navigation. |
