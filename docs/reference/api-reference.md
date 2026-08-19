---
title: API Reference
status: active
owner: core-team
reviewed: 2026-06-02
audience: developers-and-operators
---

# API Reference

This section describes how API documentation is generated, where to find the key namespaces, and how to keep the published API surface up to date.

## How API docs are generated

API pages are generated from XML documentation comments in the .NET projects and built with DocFX.

### Prerequisites

- .NET SDK (matching the repository's target framework)
- DocFX CLI installed and available on `PATH`

### Build API docs

```bash
docfx docfx.json
```

### Serve locally for preview

```bash
docfx docfx.json --serve
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

The application exposes a REST API when running with `--mode desktop`. All `/api/*` endpoints require an API key when `MDC_API_KEY` is set, except requests already authenticated by a browser login session (the workstation UI authenticates with the session cookie + CSRF token instead of an API key). Swagger UI is available at `/swagger` in development mode.

### Authentication

Set the `MDC_API_KEY` environment variable to enable API-key authentication for out-of-band clients (scripts, service-to-service calls). Pass the key via:
- `X-Api-Key` header (the only accepted transport — query-string keys are rejected to avoid leakage via URLs, logs, and browser history)

Health probes (`/healthz`, `/readyz`, `/livez`) are always exempt.

#### What a key is allowed to do

Authenticating a key is not the same as authorizing it. Most `/api/*` routes declare a permission, and
a caller that carries no permissions is refused by those routes even with a valid key. A validated key
therefore carries a role, named by `MDC_API_KEY_ROLE`:

| Value | Effect |
| --- | --- |
| unset or `ReadOnly` | The key carries `ReadOnly`, which holds `ViewMarketData`, `ViewHistoricalData`, `ViewAnalytics` and `ViewStrategies`. It is restricted to `GET`, `HEAD` and `OPTIONS`, so an unscoped key can read live and historical market data and analytics, but cannot call command-shaped endpoints or reach ledger, reporting, fund or security-master surfaces. Other methods are refused with `403`. Note that `ViewHistoricalData` is also accepted by the `/api/storage/*` reads, so a `ReadOnly` key can see storage statistics and on-disk layout, including absolute host paths from `GET /api/storage/symbol/path`; give a key `ReadOnly` only where that is acceptable. `ViewStrategies` buys a key nothing in practice: every strategy-run read sits under the tenant-scoped `/api/workstation` surface described below. |
| another role name (`Admin`, `TradeDesk`, `Accounting`, …) | The key carries that role's permissions. Match it to what the calling script actually needs. |
| anything else | Requests are refused with `503` rather than quietly falling back, so a typo surfaces instead of applying a permission set nobody chose. Only role **names** are accepted — a numeric value is rejected, because `Admin` is the zero value and `0` would otherwise resolve to full administrator. |

Scripts that mutate configuration or read governed data need a role that holds the relevant
permission — for example `POST /api/symbols/add` requires `ModifyConfig` and `POST /api/backfill/run`
requires `TriggerBackfill`, so a `ReadOnly` key is refused by both.

Two families stay out of reach of a key, because a role is not an operator and a key carries no
tenant:

- **Routes declaring an operator session** — workflow presets, saved explorer views, first-run
  acknowledgements (`GET /api/workstation/first-run`, `POST /api/workstation/first-run/outcomes/complete`)
  and the desktop launcher. These identify their data by the calling operator, and a key is one
  shared credential with no operator behind it.
- **The tenant-scoped `/api/workstation` route group** — those routes require a tenant scope, which a
  validated key is never given. Strategy-run reads live here, so a key holding `ViewStrategies` still
  cannot reach them; read governed data through the API routes outside this surface instead.

Neither is a blanket rule about the `/api/workstation` path prefix, and one route in particular is an
exception worth knowing: **`POST /api/workstation/first-run/complete` is gated by `AdminMaintenance`
rather than by session or tenant**, so a key configured with a role holding that permission can
complete first-run setup and provision sample data, recorded against the `api-key` actor. Give a key
`Admin` only where that is intended. The first-run and desktop-launcher routes are mapped outside the
tenant-scoped group, so tenant scope does not stand in front of them.

**Example:**

```bash
curl -H "X-Api-Key: your-api-key" http://localhost:8080/api/status
```

When authentication fails, the API returns `401 Unauthorized`:

```json
{ "error": "Unauthorized. Provide a valid API key via the X-Api-Key header." }
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
cat <<'JSON' > /tmp/backfill-request.json
{
  "provider": "stooq",
  "symbols": ["SPY", "AAPL"],
  "from": "2024-01-01",
  "to": "2024-01-31"
}
JSON
curl -X POST http://localhost:8080/api/backfill/run \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  --data-binary @/tmp/backfill-request.json
```

### Fund Structure: `GET /api/fund-structure/cash-flow-view`

For `scopeKind=LedgerGroup`, `ledgerGroupId` now uses the shared `LedgerGroupId` normalization contract:

- leading/trailing whitespace is trimmed before lookup
- blank values are rejected with HTTP `400`
- only letters, digits, `-`, `_`, `.`, and `:` are valid characters
- invalid values are rejected with HTTP `400`

Grouping in application services uses the same normalization path so endpoint filtering and
accounting-view ledger grouping resolve IDs consistently.

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
| Maintenance | 19 | Archive maintenance schedules, executions, status, presets |
| Providers | 8 | Provider status, metrics, catalog, comparison, latency |
| Options | 7 | Options chains, expirations, strikes, quotes, refresh, provider status |
| Execution | 21 | Execution blotter, keyed position actions, session continuity, orders, health, audit, controls |
| Failover | 7 | Failover rules, health, force failover |
| Interactive Brokers | 3 | IB-specific status, error codes, API limits |
| Symbol Mapping | 5 | Cross-provider symbol mappings, CSV import |
| Live Data | 6 | Real-time trades, quotes, order book, order flow |
| Symbols | 15 | Symbol CRUD, bulk operations, search, statistics |
| Storage | 19 | Storage stats, health, tiers, search, catalog |
| Storage Quality | 9 | Data quality scores, alerts, trends, anomalies |
| Quality Drops | 2 | Pipeline dropped event audit statistics |
| Health | 12 | System health, Kubernetes probes, Prometheus metrics, SSE |

### High-Risk Operational and Security Surfaces

These routes are prioritized for operator hardening, permission checks, and mutation-rate-limit monitoring.

#### Path-First Validation Index (Area 5 Priority)

This table is intentionally **path-first** to support API documentation validation tooling.

| Path | Method | Surface | Operational/Security Notes |
|---|---|---|---|
| `/api/ledger/journal-automation/dividend-intake` | POST | Ledger Automation | Runs dividend intake into governed manual journal drafts; requires fund-scoped write tenant permission and mutation rate limiting. |
| `/api/ledger/journal-automation/fee-accrual-intake` | POST | Ledger Automation | Runs fee-accrual intake into governed manual journal drafts; requires fund-scoped write tenant permission and mutation rate limiting. |
| `/api/projections/{projectionRunId:guid}/flows` | GET | Direct Lending | Returns projection cash-flow detail for a projection run. |
| `/api/journals/{journalEntryId:guid}/post` | POST | Direct Lending | Posts pending journal entries to direct-lending ledger surfaces. |
| `/api/reconciliation/{runId:guid}/results` | GET | Direct Lending | Returns reconciliation run results for operator review. |
| `/api/reconciliation/exceptions` | GET | Direct Lending | Lists unresolved reconciliation exceptions requiring action. |
| `/api/reconciliation/exceptions/{exceptionId:guid}/resolve` | POST | Direct Lending | Resolves reconciliation exceptions (governed mutation route). |
| `/api/servicer-reports` | POST | Direct Lending | Creates servicer report batch import records. |
| `/api/servicer-reports/{batchId:guid}` | GET | Direct Lending | Returns servicer report batch summary/status. |
| `/api/servicer-reports/{batchId:guid}/position-lines` | GET | Direct Lending | Returns imported position-line records for a batch. |
| `/api/servicer-reports/{batchId:guid}/transaction-lines` | GET | Direct Lending | Returns imported transaction-line records for a batch. |
| `/api/loans/rebuild-all` | POST | Direct Lending | Triggers portfolio-wide loan snapshot rebuild. |
| `/api/loans/rebuild-checkpoints` | GET | Direct Lending | Reads rebuild checkpoint posture before triggering rebuild mutation. |
| `/api/loans/portfolio` | GET | Direct Lending | Reads aggregated direct-lending portfolio posture. |
| `/api/security-master/corporate-actions/ingest` | POST | Security Master | Runs provider-backed corporate-action ingest across mastered tickers; requires Security Master mutation permission and returns retained ingest evidence. |

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
| POST | `/api/backfill/cost-estimate` | Estimate provider API calls, quota impact, wall-clock time, and adaptive partition windows |
| POST | `/api/backfill/run` | Execute backfill |
| POST | `/api/backfill/run/preview` | Preview backfill (dry run) |
| GET | `/api/backfill/progress` | Current operation progress |

Cost-estimate responses include per-provider `PartitionStrategy` and `AdaptivePartitions` values
for estimator-planned intraday or large-range windows. Treat these fields as planning evidence;
execution history, checkpoints, and follow-up quality/gap checks remain the completion proof.

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
| GET | `/api/providers/capability-matrix` | Provider by instrument-type capability matrix, including discovery failures |
| GET | `/api/providers/catalog` | Provider catalog with metadata |
| GET | `/api/providers/catalog/{providerId}` | Single provider catalog entry |
| GET | `/api/providers/latency` | Latency statistics |
| GET | `/api/connections` | Connection health summary |

Provider catalog responses expose capability flags such as `supportsOptionsChain` and `supportsBrokerage`, which desktop setup flows can use to distinguish data feeds from broker-backed options providers like Robinhood.

### Options (`/api/options/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/options/expirations/{underlyingSymbol}` | Available expirations for an underlying |
| GET | `/api/options/strikes/{underlyingSymbol}/{expiration}` | Available strikes for a specific expiration |
| GET | `/api/options/chains/{underlyingSymbol}` | Cached or fetched option chain snapshots |
| GET | `/api/options/quotes/{underlyingSymbol}` | Cached option quotes for an underlying |
| GET | `/api/options/summary` | Options summary plus active provider identity, mode, and fallback state |
| GET | `/api/options/underlyings` | Tracked option underlyings |
| POST | `/api/options/refresh` | Refresh a specific option chain snapshot |

`/api/options/summary` now includes:

- `providerId` and `providerDisplayName` for the active provider
- `providerMode` with `Configured`, `Fallback`, or `Unavailable`
- `isFallbackProvider` to distinguish synthetic fallback from live providers
- `providerStatusMessage` for UI-ready status text

### Execution (`/api/execution/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/execution/account` | Account-level execution snapshot |
| GET | `/api/execution/positions` | Legacy paper-trading positions list |
| GET | `/api/execution/positions/blotter` | Broker-aware blotter snapshot used by desktop position views |
| POST | `/api/execution/positions/actions/close` | Submit a keyed close action for a blotter position |
| POST | `/api/execution/positions/actions/upsize` | Submit a keyed upsize action for a blotter position |
| POST | `/api/execution/positions/{symbol}/close` | Legacy symbol-based close action; rejects ambiguous matches |
| GET | `/api/execution/orders` | Open orders |
| POST | `/api/execution/orders/submit` | Submit an order |
| POST | `/api/execution/orders/{orderId}/cancel` | Cancel a single order |
| POST | `/api/execution/orders/cancel-all` | Cancel all open orders |
| GET | `/api/execution/portfolio` | Portfolio snapshot |
| GET | `/api/execution/health` | Gateway health and live-connection summary |
| GET | `/api/execution/capabilities` | Order-gateway capabilities |
| GET | `/api/execution/audit` | Operator audit trail |
| GET | `/api/execution/controls` | Execution operator controls snapshot |
| POST | `/api/execution/controls/circuit-breaker` | Open or close the execution circuit breaker |
| GET | `/api/execution/sessions` | Paper-session summaries for the trading cockpit |
| GET | `/api/execution/sessions/{sessionId}` | Paper-session detail including tracked symbols, portfolio, and order history |
| POST | `/api/execution/sessions/create` | Create a paper session and persist the requested symbol universe |
| POST | `/api/execution/sessions/{sessionId}/close` | Close a paper session and return an audited operator action result |
| GET | `/api/execution/sessions/{sessionId}/replay` | Replay persisted fills and report whether the replay matches the current session state |

`/api/execution/positions/blotter` is the preferred position endpoint for desktop execution surfaces because it returns broker-backed/live state, a source label, a status message, and keyed position metadata needed for broker option actions.

`/api/execution/sessions/{sessionId}/replay` is the Wave 2 operator continuity check for paper trading: it replays the durable fill log, compares the replayed portfolio to the current session snapshot, and records the verification step in the execution audit trail.

`/api/execution/orders/submit` and the two position actions answer `202 Accepted` with a
`PendingApproval` outcome when a risk rule parks the order for governed approval. That is not a
failure: nothing routed, but a durable queue entry can still execute the order once an approver
releases it. Clients must not resubmit — each attempt mints a new client order id, and several
parked orders for one intent can all release.

### Risk (`/api/risk/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/risk/rules` | Live status for every registered risk rule; requires `ViewTrades` |
| GET | `/api/risk/rules/{ruleName}/status` | Live status for one rule; requires `ViewTrades` |
| GET | `/api/risk/rules/{ruleName}/config` | Operator-managed thresholds for one rule |
| PUT | `/api/risk/rules/{ruleName}/config` | Update thresholds; requires risk-configuration permission |
| GET | `/api/risk/escalations` | Governed-approval queue: orders parked by an `Escalate`-severity rule |
| POST | `/api/risk/escalations/{escalationId}/approve` | Approve a parked order, optionally releasing it |
| POST | `/api/risk/escalations/{escalationId}/deny` | Deny a parked order and withdraw its escalation |

Rule status is a trade read, not a configuration read: it carries aggregate gross exposure across
every registered portfolio and violation reasons that can name traded symbols, so both status
routes require `ViewTrades`.

The escalation queue is the governed-approval surface for orders a risk rule parked rather than
rejected. `GET /api/risk/escalations` returns only entries the caller is authorized to see —
fund-scoped entries require scoped `ManageOrders` authority over the owning account, and
administrators see all. Approve and deny each require a written rationale, which is retained with
the decision and surfaced in the audit trail; a request without one is rejected. Segregation of
duties is enforced against the retained submitter: the operator who submitted an order cannot
approve it. Approving with release re-checks the approver's scoped authority, because the release
bypasses `/api/execution/orders/submit` and its gates. Approvals are one-shot tokens matched
against a full fingerprint of the retained request including its client order id; a release the
gateway refuses re-arms the approval so a transient failure never retires an operator's decision,
and denying or withdrawing an entry also retires any approvals linked through its token chain.

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
| PUT | `/api/maintenance/schedules/{scheduleId}` | Update a schedule; returns `409 Conflict` for a stale retained revision |
| DELETE | `/api/maintenance/schedules/{scheduleId}` | Delete a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/enable` | Enable a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/disable` | Disable a schedule |
| POST | `/api/maintenance/schedules/{scheduleId}/trigger` | Trigger a schedule immediately; returns `409 Conflict` while that schedule already has queued or running work |
| POST | `/api/maintenance/schedules/{scheduleId}/run` | Compatibility alias for immediate schedule execution with the same `409 Conflict` behavior |
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

Maintenance cron validation accepts five-field expressions plus Meridian's ordinal-weekday
extension `d#n` in the day-of-week field (for example, `0#1` means the first Sunday). When both
day-of-month and day-of-week are restricted, standard day-field OR semantics apply.

### Quality Drops (`/api/quality/drops/*`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/quality/drops` | Dropped event statistics |
| GET | `/api/quality/drops/{symbol}` | Drops for specific symbol |

`GET /api/quality/errors/statistics` retains the existing `totalErrors` and `errorRate` meanings:
both describe the bounded records currently retained. The additive `retainedTotalErrors` and
`retainedErrorRate` fields make that scope explicit, while `lifetimeTotalErrors` and
`lifetimeErrorRate` report detections over the current tracker generation.

### Environment Designer (`/api/environment-designer/*`)

These endpoints expose the company-umbrella environment designer workflow used by the WPF
governance admin surface.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/environment-designer/drafts` | List environment drafts ordered by last update. |
| GET | `/api/environment-designer/drafts/{draftId}` | Load a single environment draft. |
| POST | `/api/environment-designer/drafts` | Create a new environment draft. |
| PUT | `/api/environment-designer/drafts/{draftId}` | Save an edited environment draft. |
| DELETE | `/api/environment-designer/drafts/{draftId}` | Delete an environment draft. |
| POST | `/api/environment-designer/validate` | Validate a draft, optionally against a publish plan. |
| POST | `/api/environment-designer/publish/preview` | Preview publish diff and destructive-change status. |
| POST | `/api/environment-designer/publish` | Publish a draft and create a new versioned runtime projection. |
| GET | `/api/environment-designer/versions` | List published versions, optionally filtered by organization. |
| GET | `/api/environment-designer/versions/current` | Get the current published version for an organization or the latest current version. |
| GET | `/api/environment-designer/versions/{versionId}` | Load a published version. |
| POST | `/api/environment-designer/versions/{versionId}/rollback` | Roll back the current published version pointer. |
| GET | `/api/environment-designer/runtime/current` | Fetch the current published runtime projection. |
| GET | `/api/environment-designer/runtime/versions/{versionId}` | Fetch the runtime projection for a specific published version. |


### Workstation Accounting Control Routes (v1 additive compatibility)

The workstation accounting-control workflows use additive-compatible v1 contracts.
Clients must tolerate additional JSON fields and additive state values without breaking existing
flows. Legacy governance/data-operations aliases remain compatibility routes for retained clients.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/workstation/accounting` | Accounting workspace payload for ledger, reconciliation, approvals, and accounting-control posture. |
| GET | `/api/workstation/data` | Data workspace payload for provider posture, backfill, storage, and quality handoffs. |
| GET | `/api/workstation/data/ingestion-operations` | Filterable durable ingestion-job summary with checkpoint, retry, failure, action, and evidence posture. |
| GET | `/api/workstation/data/ingestion-operations/{jobId}` | Ingestion job detail with checkpoint and per-symbol progress. |
| POST | `/api/workstation/data/ingestion-operations/{jobId}/actions/{action}` | Apply a permitted pause, resume, retry, or cancel transition with rationale, idempotency, and retained evidence. |
| GET | `/api/workstation/data/storage-assurance` | Consolidated storage health, quality, canonicalization, capacity, tier, alert, and permission posture. |
| POST | `/api/workstation/data/storage-assurance/actions/preview` | Preview a quality check, temporary-file cleanup, or copy-only tier migration. |
| POST | `/api/workstation/data/storage-assurance/actions/execute` | Execute an unexpired preview after permission, rationale, typed-confirmation, and candidate revalidation checks. |
| GET | `/api/workstation/trading/readiness` | Trading readiness projection with operator sign-off and accounting-control work-item posture. |
| GET | `/api/workstation/operator/inbox` | Account-scoped operator queue for readiness + reconciliation work. |
| GET | `/api/workstation/trading` | Trading shell payload that embeds readiness state. |
| GET | `/api/execution/sessions/{sessionId}/replay` | Replay explainability payload used for readiness verification evidence. |

See [Reconciliation Operations](../operators/reconciliation-operations.md),
[Fund Operations Persistence Cutover](../operators/fund-ops-persistence-cutover.md), and
[Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) for transition rules, required
metadata, approval gates, and rejection/reopen guidance.

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

**See also:** [Architecture Overview](../architecture/overview.md) · [Domain Model](../architecture/domains.md) · [Provider Capability Matrix](provider-capability-matrix.md)

---

**Version:** 1.6.3
**Last Updated:** 2026-06-02
**Audience:** Contributors maintaining the HTTP API surface and AI assistants working on endpoint documentation.


### Coverage-Reported Gaps (Workstation + Config Alias)

The coverage audit also tracks workstation shell routes and compatibility aliases.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/config/data-sources` | Backward-compatible alias for data source listing (`/api/config/datasources`). |
| POST | `/api/config/data-sources` | Backward-compatible alias for create/update of data sources. |
| GET | `/session` | Legacy/session route marker reported by coverage scanner; workstation session data is served at `/api/workstation/session`. |
| GET | `/research` | Legacy/research route marker reported by coverage scanner; Strategy payload is served at `/api/workstation/strategy`, with `/api/workstation/research` retained as a compatibility alias. |
| GET | `/workstation` | Workstation shell entry point that serves the React index page. |
| GET | `/workstation/{*path}` | SPA fallback route for workstation client-side navigation. |
