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
- `StatusHttpServer` - Status + Prometheus metrics endpoint.
- `BackfillService` - Historical backfill orchestration.

### Storage and archival layer

- `JsonlStorageSink` - JSONL persistence under the configured storage policy.
- `ParquetStorageSink` - Optional columnar sink; select it through `Storage.Sinks` (for example,
  `["jsonl", "parquet"]`) or the compatibility `Storage.EnableParquetSink` setting. The config list
  maps to runtime `StorageOptions.ActiveSinks`; see
  [`StorageConfigExtensions`](../../src/Meridian.Storage/Config/StorageConfigExtensions.cs).
- `TieredStorageManager` - Hot/warm/cold storage tiering.
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

These optional types are excluded from the host's default build. See
[Lean Integration](../integrations/lean-integration.md) for `EnableLeanIntegration=true`, data-path
compatibility, and the separate Lean-host verification requirements.

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
| unset or `ReadOnly` | The key carries `ReadOnly`, which holds `ViewMarketData`, `ViewHistoricalData`, `ViewAnalytics` and `ViewStrategies`. An unscoped key can read live and historical market data and analytics, but cannot reach ledger, reporting, fund or security-master surfaces. It is also method-capped — see below. Note that `ViewHistoricalData` is accepted by the `/api/storage/*` reads, so a `ReadOnly` key can see storage statistics and on-disk layout, including absolute host paths from `GET /api/storage/symbol/path`; give a key `ReadOnly` only where that is acceptable. `ViewStrategies` reaches the `/api/lean/*` reads, which are not tenant-scoped and disclose the deployment's Lean installation path and data directory, its algorithm source listing, auto-export destination and last error, and backtest history, status and results. Strategy-run reads themselves sit under the tenant-scoped `/api/workstation` surface described below and stay out of a key's reach, but the Lean integration surface does not — weigh that before giving a key `ReadOnly`. |
| `Analysis` or `Executive` | Both hold `ReadOnly`'s four permissions plus `ExportData`, `ViewSecurityMaster`, `ViewDirectLending` and `ViewReporting`; `Executive` adds `ViewTrades`. Neither holds any `Manage`, `Modify`, `Execute` or `Admin` permission, so both are method-capped alongside `ReadOnly` — see below. |
| another role name (`Admin`, `TradeDesk`, `Accounting`, …) | The key carries that role's permissions, with no method cap. Match it to what the calling script actually needs. |
| anything else | Requests are refused with `503` rather than quietly falling back, so a typo surfaces instead of applying a permission set nobody chose. Only role **names** are accepted — a numeric value is rejected, because `Admin` is the zero value and `0` would otherwise resolve to full administrator. |

##### The method cap on read-only roles

`ReadOnly`, `Analysis` and `Executive` are the built-in roles whose permission sets carry no `Manage`,
`Modify`, `Execute` or `Admin` permission at all. A principal carrying any of them — an API key or an
optional-mode anonymous caller — is restricted to `GET`, `HEAD` and `OPTIONS`, with two exceptions.
Anything else outside the safe methods is refused with `403` before the route's own permission check
runs, and the message names the variable to change.

The first exception is a route that declares `ExportData`, because `Analysis` and `Executive` are
granted that permission outright and the export routes are `POST` only because they accept a request
body.

The second is a route that carries an explicit **non-mutating declaration**. Some reads cannot fit
their query in a URL, so they are `POST` while changing nothing. Each such route states why, per
route, after its handler was read — the declaration is a claim about that handler, never a
convention applied to a path prefix. These routes carry one today:

| Route | Why it is a read | Permission it still enforces |
| --- | --- | --- |
| `POST /api/workstation/data/query` | `SqlStatementGuard` admits one `SELECT`-family statement with no embedded semicolon and a blocked-keyword list. | `ViewHistoricalData` |
| `POST /api/workstation/evidence/vault/search` | Evidence-vault lookup by linkage criteria; the handler only reads, through `IEvidenceArtifactStore.FindByLinkageAsync`. | `ViewReporting`, plus workstation tenant scope |
| `POST /api/alignment/preview` | Computes and returns an alignment preview from the request body; the sibling `POST /api/alignment/create` route is what persists one. | `ViewHistoricalData` |
| `POST /api/workstation/runs/compare` | Compares retained strategy runs through `StrategyRunComparisonService`, which holds only the run read service. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/workstation/runs/diff` | Diffs two retained runs by the same read path. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/security-master/resolve` | Resolves one record through `ISecurityMasterQueryService.GetByIdentifierAsync`; the body carries the identifier kind, value, provider and as-of. | `ViewSecurityMaster` or `ModifySecurityMaster` |
| `POST /api/security-master/search` | Search through `ISecurityMasterQueryService.SearchAsync`; the body carries the query because it has several optional filters. | `ViewSecurityMaster` or `ModifySecurityMaster` |
| `POST /api/workstation/strategy/designer/validate` | Normalizes and validates the posted document in memory; `StrategyDesignService` holds only a field catalog and templates. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/workstation/strategy/designer/preview` | Previews the posted document in memory. Saving a design is a separate route. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/workstation/strategy/engine/validate-run` | Validates a posted run request against the engine registry; it does not start or record a run. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/strategies/covered-call/chain-preview` | Fetches the option chain, filters in memory and returns a projection, never touching the run channel. | `ViewStrategies` or `ManageStrategies` |
| `POST /api/fund-structure/reporting/templates/render` | Resolves the template, evaluates the grids through `ReportWriterGridEngine` in memory and returns a DTO; `ReportingWorkflowService.Render` persists nothing. The body carries the template id, parameters, grid definitions and dataset rows. | `ViewReporting`, `ManageReporting`, `ApproveReporting`, `DeliverReporting` or `AdminMaintenance` |

The declaration only lifts the method cap; it grants nothing, as the last column shows. A capped
principal reaches one of these routes only when it also holds that route's permission, so `ReadOnly`
can run the data query but not the evidence-vault search. An undeclared non-safe route stays refused,
so the failure mode of a forgotten declaration is a lost capability rather than a reopened mutation.

Query-style `POST` routes whose permission no capped role holds — the `Validate` and `Preview` routes
declaring `ManageStorage`, `ManageCredentials`, `ModifyConfig`, `AdminMaintenance` or a `Manage`
permission — deliberately carry no declaration. The cap never reaches them, so declaring one would add
the risk of a mis-read handler for no capability gained.

The cap exists because a handful of legacy routes mutate process-local state while declaring only a
view permission — `POST /api/replay/start` and `POST /api/sampling/create` both declare
`ViewHistoricalData`, which all three roles hold. Without the cap, naming one of these
roles would hand an unauthenticated or out-of-band caller those commands. It applies to the
**resolved role**, not to the posture that resolved it, so both non-session principals behave
identically.

The cap does not apply to a logged-in operator session: a signed-in `Analysis` user is governed by
route permissions alone.

Scripts that mutate configuration or read governed data need a role that holds the relevant
permission — for example `POST /api/symbols/add` requires `ModifyConfig` and `POST /api/backfill/run`
requires `TriggerBackfill`, so a `ReadOnly` key is refused by both. A method-capped role is refused
those regardless of permission.

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
| `/api/loans/index-fixings` | POST | Direct Lending | Records or revises a benchmark fixing in a declared unit (insert-only revisions); requires ManageDirectLending, fund-scoped write tenant and mutation rate limiting. |
| `/api/loans/index-fixings` | GET | Direct Lending | Lists effective fixings, filtered by `index`, `from`, `to`. |
| `/api/loans/{loanId:guid}/rate-resets/scheduled` | POST | Direct Lending | Applies a scheduled floating-rate reset from the fixing store; returns 422 when the fixing is missing (fail closed). |
| `/api/loans/{loanId:guid}/pik/capitalizations` | POST | Direct Lending | Capitalizes accrued PIK interest into principal on a policy date (Dr LoanPrincipal / Cr PikInterestReceivable). |
| `/api/loans/{loanId:guid}/schedules/servicing` | GET | Direct Lending | Previews reset dates (with fixing availability) and PIK capitalization dates in a window of up to 3,660 days. |
| `/api/security-master/corporate-actions/ingest` | POST | Security Master | Runs provider-backed corporate-action ingest across mastered tickers; requires Security Master mutation permission and returns retained ingest evidence. |

### Ledger account oversight (`/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/*`)

These bounded shared routes require an authenticated workstation session with tenant and company
scope. The authoritative ledger book must identify the exact requested Account structure node,
and its retained profile owner must match the session tenant and company. Unowned profiles and
broader fund/entity books refuse access; requests do not establish ownership. Session identity
supplies assignment, preparation and review actors. API-key authentication alone does not grant
access to this route family.

| Path | Method | Description |
|---|---|---|
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/oversight/context` | GET | Returns the verified account/book/base currency, session actor and current cash, price-review and provisioning capabilities for the browser workbench. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/oversight/provision` | POST | Requires `AdminMaintenance`; explicitly provisions the durable cash-continuity and forecast stores without resetting missing or corrupt initialized history. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/runs` | POST | Accepts `AccountCashReconciliationRequest`; retains a complete two-source native-currency balance comparison, original rows and executed tolerance version, returning `AccountCashReconciliationRun`. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/cases` | GET | Returns exact-account `AccountCashReconciliationCase` history, owner, version, source resolution and aging as of the retained observation or resolution. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/cases/{caseId}/assignments` | POST | Accepts `AssignAccountCashCaseRequest` with `expectedVersion`, `owner` and `reason`; appends a session-actor assignment and returns the updated case. Stale versions conflict. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-reconciliation/runs/{runId}/replay` | GET | Reproduces the retained run's source and tolerance evidence without consulting current sources or changing case state. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}` | POST | Accepts `CreateSecurityClosePriceSetRequest`; returns an immutable `SecurityClosePriceSetDto` containing retained hierarchy selections, observations, units, supported overrides and visible blockers. Optional `requestId` binds an immutable normalized creation command to the snapshot identity; an identical retry recovers that retained set before reading later prices, while conflicting reuse fails. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}` | GET | Returns all retained exact-account/book/date versions in ascending order, including immutable approvals, after stored evidence validation. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}/{snapshotId:guid}` | GET | Returns the exact account/book/date snapshot and its retained approval, or `404` when no such snapshot exists. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/close-price-sets/{valuationDate}/{snapshotId:guid}/approval` | POST | Accepts `ApproveSecurityClosePriceSetRequest`; independently approves the immutable snapshot digest with reviewer rationale. Unresolved blockers, same-maker review and superseded drafts conflict. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts` | POST | Accepts `AccountCashForecastRequestDto`; verifies each selected balance, flow and dated FX input against independently reviewed typed Evidence Vault source bytes before retaining an `AccountCashForecastSnapshotDto`. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts` | GET | Returns exact-account forecast history after deterministic evidence replay, including linked prior predictions and certification blockers. |
| `/api/ledger/books/{ledgerBookId:guid}/accounts/{accountId:guid}/cash-forecasts/{snapshotId}` | GET | Returns the retained exact-account forecast snapshot after replay verification, or `404` when absent. |

Reads require one of `AdminMaintenance`, `ManageDirectLending`, `ViewLedgerReports` or
`ManageLedgerReports`; writes exclude `ViewLedgerReports`. Price reads additionally require
`ViewSecurityMaster` or `ModifySecurityMaster`, and price preparation/approval additionally require
`ModifySecurityMaster`. Provisioning requires `AdminMaintenance`. Mutations use the existing
fund-write scope and mutation-rate-limit gates.

Account/book IDs in request bodies must agree with the route; forecast base currency comes from
the authoritative book. `valuationDate` uses `YYYY-MM-DD` and a UTC valuation cutoff. Observation
and forecast-recording timestamps cannot exceed server capture time. Reconciliation HTTP
`sourceType` values are `0` Prime, `1` Custodian, `2` Administrator and `3` InternalLedger. Forecast
HTTP `status` values are `0` Expected and `1` Actual; `provenance.role` values are `0` Observation,
`1` Projection and `2` DeclaredFx. The separately retained typed source JSON uses enum names;
price units use `CurrencyPerUnit` or `PercentOfPar`.
Amounts, prices, tolerances, FX rates and derived financial values in oversight HTTP responses
use exact plain decimal strings. Integer versions remain JSON numbers. Decimal-text request
values are supported; this transport representation does not change retained domain digests.
Identical concurrent approval retries return the same retained decision and approval timestamp;
different reviewers or rationales cannot replace that immutable decision.

Incorrect account authority returns `403`; invalid inputs, including null forecast collection
entries or future recording times, return `422`. Conflicting retained inputs and stale versions
return `409`. Unsupported, unavailable or mismatched forecast source authority returns `409` with
`CASH_FORECAST_SOURCE_AUTHORITY_REQUIRED`. Missing service composition or corrupt forecast
history returns `503`. Corrected sources change the same cash case; corrected prices and forecast
inputs append linked versions. Source clearing and cash-equation certification confer no journal,
period-close, report-publication or payment-execution authority.

Source: [shared endpoint mapper](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.AccountOversight.cs),
[route constants](../../src/Meridian.Contracts/Api/UiApiRoutes.cs),
[cash-continuity contracts](../../src/Meridian.FinancialOperations/Reconciliation/AccountCashReconciliationModels.cs),
[close-price contracts](../../src/Meridian.Contracts/SecurityMaster/SecurityMasterClosePrices.cs) and
[forecast contracts](../../src/Meridian.Contracts/AssetOperations/AccountCashForecastDtos.cs).
For provisioning, source JSON and retained-review field requirements, see
[bounded account oversight](../operators/account-oversight.md).

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
| POST | `/api/execution/positions/actions/close` | Submit a keyed close action for a blotter position; rejects a `lotSelection` the close order cannot honour |
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
| GET | `/api/ingestion/jobs/resumable` | List jobs eligible for resume (failed or paused with checkpoint) |
| GET | `/api/ingestion/summary` | Summary of all jobs by state and workload type |

Reads require `ViewHistoricalData` or `TriggerBackfill`; create requires `TriggerBackfill` and is rate limited. Raw state-transition and delete routes are not exposed: operator pause, resume, retry and cancel go through the governed workstation ingestion-operation action route, which requires a rationale and idempotency key, enforces tenant/company scope and retains action evidence.

### Banking (`/api/banking/*`)

All routes except the transaction list take a required `entityId` query parameter. Reads require
`ViewDirectLending`; payment initiation, approval, rejection, currency remediation and bank-evidence
recording require `ManageDirectLending`. Malformed request bodies return `400`.

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/banking/payments` | Initiate a pending payment |
| GET | `/api/banking/payments/pending` | Pending payments for an entity (payment-approvals screen) |
| POST | `/api/banking/payments/{pendingPaymentId}/approve` | Approve a pending payment |
| POST | `/api/banking/payments/{pendingPaymentId}/reject` | Reject a pending payment |
| POST | `/api/banking/payments/{pendingPaymentId}/currency-remediation` | Remediate a payment's currency |
| POST | `/api/banking/payments/{pendingPaymentId}/bank-evidence` | Record bank evidence for a payment |
| GET | `/api/banking/transactions` | Bank transaction records, optionally filtered by `entityId` |
| POST | `/api/dev/seed/bank-transactions` | Seed synthetic bank transactions; mapped only in Development and Test hosts, requires `AdminMaintenance` |

### Money market funds (`/api/security-master/money-market-funds/*`)

Reads (including `POST .../search`) require `ViewSecurityMaster` or `ModifySecurityMaster`;
`POST .../{securityId}/rebuild` requires `ModifySecurityMaster`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/security-master/money-market-funds/{securityId}` | MMF reference record |
| POST | `/api/security-master/money-market-funds/search` | Search MMFs by filter criteria |
| GET | `/api/security-master/money-market-funds/{securityId}/liquidity` | Liquidity projection for one MMF |
| GET | `/api/security-master/money-market-funds/liquidity/liquid` | All active MMFs in the Liquid state |
| GET | `/api/security-master/money-market-funds/{securityId}/sweep-profile` | Sweep-eligibility and liquidity-fee profile |
| GET | `/api/security-master/money-market-funds/family/{familyName}` | Fund-family projection |
| POST | `/api/security-master/money-market-funds/{securityId}/rebuild` | Rebuild projections for one MMF |
| GET | `/api/security-master/money-market-funds/rebuild/checkpoints` | Projection rebuild checkpoints |

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

<!-- BEGIN GENERATED HTTP DECLARATION REFERENCE -->
## HTTP declaration reference

Generated by `python3 build/scripts/docs/render-endpoint-reference.py`; verify without writing with `--check`.

This source reference records literal `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch`, `MapHead`, and `MapOptions` declarations. It preserves separate HTTP methods on the same path. Group prefixes declared locally are composed and route constraints are normalized (`{id:guid}` becomes `{id}`); inspect the linked source for the original constraints.

**Relative declaration** means a mapping method receives its group from a caller. Its displayed path is a fragment, not a standalone URL. Where local mapping-method calls can be followed, caller-derived paths are shown as mounts; versioned aliases are preserved. The receiver and source identify the registration context; dynamic callers may add other mounts. Constant-based route declarations are outside this literal-declaration reference. This is a lexical source reference, not a runtime route inventory, authorization specification, or proof that a host registers an endpoint.

Handler parameters retain C# binding types, including injected services and infrastructure parameters; they are not all request-body fields. Body-read operations list explicitly decoded types. Response entries quote declared `.Produces*` metadata and observed `Results`/`TypedResults` factories, not an exhaustive response schema or status guarantee. A dash means the declaration supplies no extractable metadata; follow the source and detailed contracts above.

**454 declarations** from **49 source files**.


### HostAdapters

Source: [src/Meridian.Application/Composition/HostAdapters.cs](../../src/Meridian.Application/Composition/HostAdapters.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/health` | GET | `() => …` | `Results.Json` | — | [L60](../../src/Meridian.Application/Composition/HostAdapters.cs#L60) |
| `/healthz` | GET | `() => …` | `Results.Ok` | — | [L71](../../src/Meridian.Application/Composition/HostAdapters.cs#L71) |
| `/ready` | GET | `() => …` | `Results.Ok` | — | [L72](../../src/Meridian.Application/Composition/HostAdapters.cs#L72) |
| `/live` | GET | `() => …` | `Results.Ok` | — | [L73](../../src/Meridian.Application/Composition/HostAdapters.cs#L73) |
| `/health` | GET | `() => …` | `Results.Json` | — | [L99](../../src/Meridian.Application/Composition/HostAdapters.cs#L99) |
| `/healthz` | GET | `() => …` | `Results.Ok` | — | [L111](../../src/Meridian.Application/Composition/HostAdapters.cs#L111) |
| `/health` | GET | `() => …` | `Results.Json` | — | [L148](../../src/Meridian.Application/Composition/HostAdapters.cs#L148) |
| `/healthz` | GET | `() => …` | `Results.Ok` | — | [L160](../../src/Meridian.Application/Composition/HostAdapters.cs#L160) |
| `/ready` | GET | `() => …` | `Results.Ok` | — | [L161](../../src/Meridian.Application/Composition/HostAdapters.cs#L161) |
| `/live` | GET | `() => …` | `Results.Ok` | — | [L162](../../src/Meridian.Application/Composition/HostAdapters.cs#L162) |
| `/health` | GET | `() => …` | `Results.Json` | — | [L184](../../src/Meridian.Application/Composition/HostAdapters.cs#L184) |

### ArchiveMaintenanceEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/maintenance/schedules/{scheduleId}` | PUT | `async (ArchiveMaintenanceScheduleManager scheduleManager, string scheduleId, UpdateMaintenanceScheduleRequest req, CancellationToken ct) => …` | `Results.NotFound`; `Results.Json`; `Results.BadRequest`; `Results.Conflict` | — | [L37](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L37) |
| `/api/maintenance/schedules/{scheduleId}` | DELETE | `async (ArchiveMaintenanceScheduleManager scheduleManager, string scheduleId, CancellationToken ct) => …` | `Results.Ok`; `Results.NotFound` | — | [L99](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L99) |
| `/api/maintenance/schedules/{scheduleId}/trigger` | POST | `async (ScheduledArchiveMaintenanceService maintenanceService, string scheduleId, CancellationToken ct) => …` | `Results.Json`; `Results.NotFound`; `Results.Conflict` | — | [L119](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L119) |
| `/api/maintenance/execute` | POST | `async (ScheduledArchiveMaintenanceService maintenanceService, ExecuteMaintenanceRequest req, CancellationToken ct) => …` | `Results.BadRequest`; `Results.Json` | — | [L142](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L142) |
| `/api/maintenance/executions/{executionId}/cancel` | POST | `async (ScheduledArchiveMaintenanceService maintenanceService, string executionId) => …` | `Results.Ok`; `Results.NotFound` | — | [L166](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L166) |
| `/api/maintenance/executions` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, int? limit) => …` | `Results.Json` | — | [L183](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L183) |
| `/api/maintenance/executions/{executionId}` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, string executionId) => …` | `Results.NotFound`; `Results.Json` | — | [L197](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L197) |
| `/api/maintenance/schedules/{scheduleId}/executions` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, string scheduleId, int? limit) => …` | `Results.Json` | — | [L213](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L213) |
| `/api/maintenance/executions/failed` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, int? limit) => …` | `Results.Json` | — | [L228](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L228) |
| `/api/maintenance/schedules/summary` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager) => …` | `Results.Json` | — | [L244](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L244) |
| `/api/maintenance/schedules/{scheduleId}/summary` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, string scheduleId, int? recentCount) => …` | `Results.Json` | — | [L256](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L256) |
| `/api/maintenance/statistics` | GET | `async (ArchiveMaintenanceScheduleManager scheduleManager, int? hours) => …` | `Results.Json` | — | [L271](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L271) |
| `/api/maintenance/status` | GET | `async (ScheduledArchiveMaintenanceService maintenanceService) => …` | `Results.Json` | — | [L299](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L299) |
| `/api/maintenance/validate-cron` | POST | `async (ValidateMaintenanceCronRequest req) => …` | `Results.BadRequest`; `Results.Json` | — | [L313](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L313) |
| `/api/maintenance/presets` | GET | `async () => …` | `Results.Json` | — | [L353](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L353) |
| `/api/maintenance/task-types` | GET | `async () => …` | `Results.Json` | — | [L410](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L410) |
| `/api/maintenance/executions/cleanup` | POST | `async (ArchiveMaintenanceScheduleManager scheduleManager, CleanupHistoryRequest? req) => …` | `Results.Ok` | — | [L432](../../src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs#L432) |

### BankingEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/banking/payments` | POST | `async (JsonElement body, HttpContext context) => …` | `Produces<PendingPaymentDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("InitiatePayment")` | [L25](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L25) |
| `/api/banking/payments/pending` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<PendingPaymentDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetPendingPayments")` | [L64](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L64) |
| `/api/banking/payments/{pendingPaymentId}/currency-remediation` | POST | `async (Guid pendingPaymentId, JsonElement body, HttpContext context) => …` | `Produces<PendingPaymentDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status409Conflict)`; `Produces(StatusCodes.Status422UnprocessableEntity)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RemediatePaymentCurrency")` | [L84](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L84) |
| `/api/banking/payments/{pendingPaymentId}/approve` | POST | `async (Guid pendingPaymentId, JsonElement body, HttpContext context) => …` | `Produces<PendingPaymentDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status409Conflict)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApprovePayment")` | [L140](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L140) |
| `/api/banking/payments/{pendingPaymentId}/reject` | POST | `async (Guid pendingPaymentId, JsonElement body, HttpContext context) => …` | `Produces<PendingPaymentDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status409Conflict)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RejectPayment")` | [L193](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L193) |
| `/api/banking/payments/{pendingPaymentId}/bank-evidence` | POST | `async (Guid pendingPaymentId, JsonElement body, HttpContext context) => …` | `Produces<BankTransactionDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status409Conflict)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RecordPaymentBankEvidence")` | [L250](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L250) |
| `/api/banking/transactions` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<BankTransactionDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetBankTransactions")` | [L311](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L311) |
| `/api/dev/seed/bank-transactions` | POST | `async (JsonElement body, HttpContext context) => …` | `Produces<BankTransactionSeedResultDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("SeedBankTransactions")` | [L339](../../src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs#L339) |

### BrokerageConnectionEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/brokerage-connections/robinhood/connect` | POST | `async (HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("StartRobinhoodBrokerageConnection")` | [L21](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L21) |
| `/api/brokerage-connections/robinhood/status` | GET | `async (HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetRobinhoodBrokerageConnectionStatus")` | [L37](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L37) |
| `/api/brokerage-connections/robinhood/callback` | GET | `async (string? code, string? state, string? error, HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("CompleteRobinhoodBrokerageConnection")` | [L53](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L53) |
| `/api/brokerage-connections/robinhood` | DELETE | `async (HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("RevokeRobinhoodBrokerageConnection")` | [L69](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L69) |
| `/api/brokerage-connections/alpaca/status` | GET | `async (HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status501NotImplemented)` | `WithName("GetAlpacaBrokerageConnectionStatus")` | [L88](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L88) |
| `/api/brokerage-connections/alpaca/connect` | POST | `async (AlpacaBrokerageConnectionRequestDto request, HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status501NotImplemented)` | `WithName("ConnectAlpacaBrokerageConnection")` | [L102](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L102) |
| `/api/brokerage-connections/alpaca` | DELETE | `async (HttpContext context) => …` | `Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status501NotImplemented)` | `WithName("RevokeAlpacaBrokerageConnection")` | [L116](../../src/Meridian.Ui.Shared/Endpoints/BrokerageConnectionEndpoints.cs#L116) |

### ComplianceEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/compliance/approval-requests` | POST | `(HttpContext http, ComplianceApprovalRequestCommand request, [FromServices]IComplianceApprovalStore approvals) => …` | `Results.Json` | — | [L18](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L18) |
| `/api/compliance/approval-requests/{approvalRequestId}/decisions` | POST | `(HttpContext http, string approvalRequestId, ComplianceApprovalDecisionCommand request, [FromServices]IComplianceApprovalStore approvals) => …` | `Results.Ok`; `Results.NotFound`; `Results.Conflict` | — | [L29](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L29) |
| `/api/compliance/actions/evaluate` | POST | `(HttpContext http, ComplianceActionRequest request, [FromServices]ICompliancePolicyEngine policy, [FromServices]ImmutableAuditLogService auditLog) => …` | `Results.Json` | — | [L51](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L51) |
| `/api/compliance/audit/extract` | GET | `([FromServices]ImmutableAuditLogService auditLog) => …` | `Results.Ok` | — | [L69](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L69) |
| `/api/compliance/controls/attestation` | GET | `([FromServices]ImmutableAuditLogService auditLog) => …` | `Results.Ok` | — | [L73](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L73) |
| `/api/compliance/access-reviews/assess` | POST | `async (HttpContext http, AccessReviewRunRequest request, [FromServices]AccessReviewService reviews, CancellationToken ct) => …` | `Results.Ok` | — | [L88](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L88) |
| `/api/compliance/access-reviews/run` | POST | `async (HttpContext http, AccessReviewRunRequest request, [FromServices]AccessReviewService reviews, CancellationToken ct) => …` | `Results.Ok` | — | [L104](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L104) |
| `/api/compliance/access-reviews` | GET | `([FromServices]AccessReviewService reviews) => …` | `Results.Ok` | — | [L126](../../src/Meridian.Ui.Shared/Endpoints/Compliance/ComplianceEndpoints.cs#L126) |

### DirectLendingEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/loans/{loanId}/projections/preview` | POST | `async (Guid loanId, ContractualCashFlowRequest? request, HttpContext context) => …` | `Produces<ContractualCashFlowResult>()`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.BadRequest`; `Results.NotFound`; `Results.Json`; `Results.ValidationProblem` | `WithName("PreviewContractualLoanCashFlows")` | [L44](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L44) |
| `/api/loans` | POST | `async (JsonElement body, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateLoan")` | [L66](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L66) |
| `/api/loans/{loanId}` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetLoan")` | [L96](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L96) |
| `/api/loans/{loanId}/projections/contract` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetLoanContractProjection")` | [L111](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L111) |
| `/api/loans/{loanId}/history` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<LoanEventLineageDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanHistory")` | [L126](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L126) |
| `/api/loans/{loanId}/rebuild-state` | POST | `async (Guid loanId, HttpContext context) => …` | `Produces<LoanAggregateSnapshotDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("RebuildLoanState")` | [L140](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L140) |
| `/api/loans/{loanId}/terms-versions` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<LoanTermsVersionDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanTermsVersions")` | [L158](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L158) |
| `/api/loans/{loanId}/projections/terms-versions` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<LoanTermsVersionDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanTermsVersionProjections")` | [L172](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L172) |
| `/api/loans/{loanId}/terms` | PUT | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("AmendLoanTerms")` | [L186](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L186) |
| `/api/loans/{loanId}/activate` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ActivateLoan")` | [L217](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L217) |
| `/api/loans/{loanId}/servicing-state` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetLoanServicingState")` | [L240](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L240) |
| `/api/loans/{loanId}/projections/servicing` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetLoanServicingProjection")` | [L255](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L255) |
| `/api/loans/{loanId}/projections/drawdown-lots` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<DrawdownLotDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanDrawdownLotProjections")` | [L270](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L270) |
| `/api/loans/{loanId}/projections/revisions` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<ServicingRevisionDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanServicingRevisionProjections")` | [L284](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L284) |
| `/api/loans/{loanId}/projections/accruals` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<DailyAccrualEntryDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanAccrualEntryProjections")` | [L298](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L298) |
| `/api/loans/{loanId}/drawdowns` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("BookLoanDrawdown")` | [L312](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L312) |
| `/api/loans/{loanId}/rate-resets` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApplyLoanRateReset")` | [L343](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L343) |
| `/api/loans/{loanId}/payments/principal` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApplyLoanPrincipalPayment")` | [L374](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L374) |
| `/api/loans/{loanId}/accruals/daily` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<DailyAccrualEntryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("PostDailyLoanAccrual")` | [L405](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L405) |
| `/api/loans/{loanId}/payments` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L436](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L436) |
| `/api/loans/{loanId}/cash-transactions` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L463](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L463) |
| `/api/loans/{loanId}/payment-allocations` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L471](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L471) |
| `/api/loans/{loanId}/fees` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L479](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L479) |
| `/api/loans/{loanId}/fee-balances` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L506](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L506) |
| `/api/loans/operations` | GET | `async (Guid? ledgerBookId, HttpContext context) => …` | `Produces<DirectLendingOperationsReadModelDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetDirectLendingOperations")` | [L514](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L514) |
| `/api/loans/servicer-statements/preview` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<ServicerStatementImportRequestDto>` | `Produces<ServicerStatementPreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("PreviewServicerStatement")` | [L527](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L527) |
| `/api/loans/servicer-statements/import` | POST | `async (JsonElement body, HttpContext context) => …` | `Produces<ServicerStatementImportResultDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("ImportServicerStatement")` | [L551](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L551) |
| `/api/loans/servicer-statements/{batchId}` | GET | `async (Guid batchId, HttpContext context) => …` | `Produces<ServicerStatementPreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetServicerStatementBatch")` | [L575](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L575) |
| `/api/loans/servicer-statements/{batchId}/rows` | GET | `async (Guid batchId, HttpContext context) => …` | `Produces<IReadOnlyList<ServicerStatementRowDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetServicerStatementRows")` | [L585](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L585) |
| `/api/loans/servicer-statements/{batchId}/apply` | POST | `async (Guid batchId, JsonElement body, HttpContext context) => …` | `Produces<ServicerStatementApplyResultDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApplyServicerStatementRows")` | [L595](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L595) |
| `/api/loans/{loanId}/collateral` | GET | `async (Guid loanId, HttpContext context) => …` | `Produces<IReadOnlyList<CollateralDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLoanCollateral")` | [L619](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L619) |
| `/api/loans/{loanId}/collateral` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("AddLoanCollateral")` | [L629](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L629) |
| `/api/loans/{loanId}/collateral/remove` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RemoveLoanCollateral")` | [L659](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L659) |
| `/api/loans/{loanId}/collateral/value` | PUT | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("UpdateLoanCollateralValue")` | [L689](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L689) |
| `/api/loans/{loanId}/status-transitions` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("TransitionLoanStatus")` | [L719](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L719) |
| `/api/loans/{loanId}/pik` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ToggleLoanPik")` | [L749](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L749) |
| `/api/loans/index-fixings` | POST | `async (JsonElement body, HttpContext context) => …` | `Produces<DirectLendingIndexFixingDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RecordDirectLendingIndexFixing")` | [L781](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L781) |
| `/api/loans/index-fixings` | GET | `async (string? index, DateOnly? from, DateOnly? to, HttpContext context) => …` | `Produces<IReadOnlyList<DirectLendingIndexFixingDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetDirectLendingIndexFixings")` | [L811](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L811) |
| `/api/loans/{loanId}/rate-resets/scheduled` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status422UnprocessableEntity)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApplyScheduledLoanRateReset")` | [L825](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L825) |
| `/api/loans/{loanId}/pik/capitalizations` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("CapitalizeLoanPik")` | [L856](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L856) |
| `/api/loans/{loanId}/schedules/servicing` | GET | `async (Guid loanId, DateOnly? from, DateOnly? to, HttpContext context) => …` | `Produces<LoanServicingSchedulePreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json`; `Results.ValidationProblem` | `WithName("GetLoanServicingSchedulePreview")` | [L886](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L886) |
| `/api/loans/{loanId}/restructures` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanContractDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RestructureLoan")` | [L910](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L910) |
| `/api/loans/{loanId}/amortization/discount-premium` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("AmortizeLoanDiscountPremium")` | [L940](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L940) |
| `/api/loans/{loanId}/writeoffs` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L970](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L970) |
| `/api/loans/{loanId}/unapplied-cash/apply` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ApplyLoanUnappliedCash")` | [L999](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L999) |
| `/api/loans/{loanId}/unapplied-cash/refund` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("RefundLoanUnappliedCash")` | [L1031](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1031) |
| `/api/loans/{loanId}/prepayment-penalties` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ChargeLoanPrepaymentPenalty")` | [L1061](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1061) |
| `/api/loans/{loanId}/prepayment-penalty` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …` | `Produces<LoanServicingStateDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L1091](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1091) |
| `/api/loans/{loanId}/projections` | POST | `async (Guid loanId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Results.Problem`; `Results.Json` | — | [L1120](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1120) |
| `/api/loans/{loanId}/projections` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L1148](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1148) |
| `/api/projections/{projectionRunId}/flows` | GET | `async (Guid projectionRunId, HttpContext context) => …` | `Results.Json` | — | [L1156](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1156) |
| `/api/loans/{loanId}/journals` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L1165](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1165) |
| `/api/journals/{journalEntryId}/post` | POST | `async (Guid journalEntryId, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L1173](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1173) |
| `/api/loans/{loanId}/reconcile` | POST | `async (Guid loanId, HttpContext context) => …` | `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L1196](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1196) |
| `/api/loans/{loanId}/reconciliation-runs` | GET | `async (Guid loanId, HttpContext context) => …` | `Results.Json` | — | [L1222](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1222) |
| `/api/reconciliation/{runId}/results` | GET | `async (Guid runId, HttpContext context) => …` | `Results.Json` | — | [L1230](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1230) |
| `/api/reconciliation/exceptions` | GET | `async (HttpContext context) => …` | `Results.Json` | — | [L1239](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1239) |
| `/api/reconciliation/exceptions/{exceptionId}/resolve` | POST | `async (Guid exceptionId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L1248](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1248) |
| `/api/servicer-reports` | POST | `async (JsonElement body, HttpContext context) => …` | `Results.Problem`; `Results.Json` | — | [L1271](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1271) |
| `/api/servicer-reports/{batchId}` | GET | `async (Guid batchId, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L1299](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1299) |
| `/api/servicer-reports/{batchId}/position-lines` | GET | `async (Guid batchId, HttpContext context) => …` | `Results.Json` | — | [L1307](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1307) |
| `/api/servicer-reports/{batchId}/transaction-lines` | GET | `async (Guid batchId, HttpContext context) => …` | `Results.Json` | — | [L1316](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1316) |
| `/api/loans/rebuild-checkpoints` | GET | `async (HttpContext context) => …` | `Results.Json` | — | [L1325](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1325) |
| `/api/loans/rebuild-all` | POST | `async (HttpContext context) => …` | `Results.Json` | — | [L1345](../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs#L1345) |

### EnvironmentDesignerEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/environment-designer/drafts` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<EnvironmentDraftDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("ListEnvironmentDrafts")` | [L20](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L20) |
| `/api/environment-designer/drafts/{draftId}` | GET | `async (Guid draftId, HttpContext context) => …` | `Produces<EnvironmentDraftDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetEnvironmentDraft")` | [L32](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L32) |
| `/api/environment-designer/drafts` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateEnvironmentDraftRequest>` | `Produces<EnvironmentDraftDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateEnvironmentDraft")` | [L45](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L45) |
| `/api/environment-designer/drafts/{draftId}` | PUT | `async (Guid draftId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<EnvironmentDraftDto>` | `Produces<EnvironmentDraftDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("SaveEnvironmentDraft")` | [L65](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L65) |
| `/api/environment-designer/drafts/{draftId}` | DELETE | `async (Guid draftId, HttpContext context) => …` | `Produces(StatusCodes.Status204NoContent)`; `Results.NoContent` | `WithName("DeleteEnvironmentDraft")` | [L87](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L87) |
| `/api/environment-designer/validate` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<ValidateDraftEnvelope>` | `Produces<EnvironmentValidationResultDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("ValidateEnvironmentDraft")` | [L100](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L100) |
| `/api/environment-designer/publish/preview` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<EnvironmentPublishPlanDto>` | `Produces<EnvironmentPublishPreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("PreviewEnvironmentPublish")` | [L122](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L122) |
| `/api/environment-designer/publish` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<EnvironmentPublishPlanDto>` | `Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("PublishEnvironmentDraft")` | [L142](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L142) |
| `/api/environment-designer/versions` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<PublishedEnvironmentVersionDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("ListPublishedEnvironmentVersions")` | [L162](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L162) |
| `/api/environment-designer/versions/current` | GET | `async (HttpContext context) => …` | `Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetCurrentPublishedEnvironmentVersion")` | [L175](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L175) |
| `/api/environment-designer/versions/{versionId}` | GET | `async (Guid versionId, HttpContext context) => …` | `Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetPublishedEnvironmentVersion")` | [L189](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L189) |
| `/api/environment-designer/versions/{versionId}/rollback` | POST | `async (Guid versionId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<RollbackEnvironmentVersionRequest>` | `Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("RollbackEnvironmentVersion")` | [L202](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L202) |
| `/api/environment-designer/runtime/current` | GET | `async (HttpContext context) => …` | `Produces<PublishedEnvironmentRuntimeDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetCurrentEnvironmentRuntime")` | [L224](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L224) |
| `/api/environment-designer/runtime/versions/{versionId}` | GET | `async (Guid versionId, HttpContext context) => …` | `Produces<PublishedEnvironmentRuntimeDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetEnvironmentRuntimeForVersion")` | [L238](../../src/Meridian.Ui.Shared/Endpoints/EnvironmentDesignerEndpoints.cs#L238) |

### EvidenceEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/workstation/evidence/{subjectKind}/{subjectId}/{fileName}` | GET | `async (string subjectKind, string subjectId, string fileName, HttpContext context) => …` | `Produces(200, contentType : "application/json")`; `Produces<EvidenceEndpointErrorDto>(404)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.NotFound`; `Results.StatusCode`; `Results.File` | `WithName("GetWorkstationEvidenceManifest")` | [L24](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs#L24) |
| `/workstation/evidence/vault/{vaultId}` | GET | `async (string vaultId, HttpContext context) => …` | `Produces(200, contentType : "application/json")`; `Produces<EvidenceEndpointErrorDto>(404)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.StatusCode`; `Results.Conflict`; `Results.NotFound`; `Results.File` | `WithName("GetWorkstationEvidenceVaultManifest")` | [L82](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs#L82) |

### EvidenceEndpoints.SinglePrincipalAttestation

Source: [src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/attestations/receipts/{receiptId}/export` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/receipts/{receiptId}/export` | GET | `(Guid receiptId, HttpContext context) => …` | `Produces(StatusCodes.Status200OK, contentType : "application/json")`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.File` | `WithName("ExportPreparerAttestationProof")` | [L17](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L17) |
| `/attestations` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations` | GET | `async Task<IResult>(HttpContext context) => …` | `Produces<SinglePrincipalAttestationWorkspaceDto>()`; `Results.Json` | `WithName("GetSinglePrincipalAttestationWorkspace")` | [L38](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L38) |
| `/attestations/subjects` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/subjects` | GET | `(string vaultId, string documentId, HttpContext context) => …` | `Produces<SinglePrincipalAttestationSubjectSnapshotDto>()`; `Results.NotFound`; `Results.Json` | `WithName("GetPreparerAttestationSubject")` | [L51](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L51) |
| `/attestations/drafts/{securityId}/{runId}` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/drafts/{securityId}/{runId}` | GET | `(Guid securityId, Guid runId, HttpContext context) => …` | `Produces<SinglePrincipalAttestationSubjectSnapshotDto>()`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetPreparerDraftCheckSubject")` | [L66](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L66) |
| `/attestations/declaration` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/declaration` | POST | `(ChangeSinglePrincipalDeclarationRequestDto request, HttpContext context) => …` | `Produces<SinglePrincipalDeclarationDto>()`; `Results.Json` | `WithName("ChangeSinglePrincipalDeclaration")` | [L84](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L84) |
| `/attestations/receipts` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/receipts` | POST | `(RecordPreparerAttestationRequestDto request, HttpContext context) => …` | `Produces<PreparerAttestationReceiptDto>()`; `Results.NotFound`; `Results.Json` | `WithName("RecordPreparerAttestation")` | [L99](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L99) |
| `/attestations/revocations` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/evidence/attestations/revocations` | POST | `(RevokePreparerAttestationRequestDto request, HttpContext context) => …` | `Produces<PreparerAttestationRevocationDto>()`; `Results.NotFound`; `Results.Json` | `WithName("RevokePreparerAttestation")` | [L123](../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.SinglePrincipalAttestation.cs#L123) |

### ExecutionEndpoints.Audit

Source: [src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Audit.cs](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Audit.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/audit` (relative declaration on `group`)<br>Caller-derived mounts: `/api/execution/audit` | GET | `async (int? take, HttpContext context) => …` | `Produces<IReadOnlyList<ExecutionAuditEntry>>(200)`; `Results.Json` | `WithName("GetExecutionAudit")` | [L18](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Audit.cs#L18) |
| `/audit/search` (relative declaration on `group`)<br>Caller-derived mounts: `/api/execution/audit/search` | GET | `async (string? searchText, string? runId, string? category, string? action, string? outcome, string? actor, string? symbol, string? correlationId, string? objectKind, string? objectId, string? relatedObjectId, string? orderId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? limit, HttpContext context) => …` | `Produces<AuditTrailExplorerResultDto>(200)`; `Results.Json` | `WithName("SearchExecutionAuditTrail")` | [L34](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Audit.cs#L34) |

### ExecutionEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/execution/account` | GET | `(HttpContext context) => …` | `Produces<ExecutionAccountSnapshot>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionAccount")` | [L51](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L51) |
| `/api/execution/positions` | GET | `(HttpContext context) => …` | `Produces<ExecutionPosition[]>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionPositions")` | [L71](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L71) |
| `/api/execution/positions/blotter` | GET | `async (HttpContext context) => …` | `Produces<ExecutionBlotterSnapshotResponse>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionBlotterPositions")` | [L84](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L84) |
| `/api/execution/portfolio` | GET | `(HttpContext context) => …` | `Produces<ExecutionPortfolioSnapshot>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionPortfolio")` | [L98](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L98) |
| `/api/execution/orders` | GET | `(HttpContext context) => …` | `Produces<IReadOnlyList<OrderState>>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetOpenOrders")` | [L120](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L120) |
| `/api/execution/orders/{orderId}` | GET | `(string orderId, HttpContext context) => …` | `Produces<OrderState>(200)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("GetOrderById")` | [L133](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L133) |
| `/api/execution/orders/submit` | POST | `async (OrderRequest request, HttpContext context) => …` | `Produces<OrderResult>(201)`; `Produces<OrderResult>(202)`; `Produces<OrderResult>(400)`; `Produces<OrderResult>(403)`; `Produces(429)`; `Produces<OrderResult>(502)`; `Produces(503)`; `ProducesProblem(502)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("SubmitOrder")` | [L149](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L149) |
| `/api/execution/orders/{orderId}/cancel` | POST | `async (string orderId, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces<TradingActionResult>(400)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("CancelOrder")` | [L244](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L244) |
| `/api/execution/orders/cancel-all` | POST | `async (HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces(403)`; `Produces(429)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("CancelAllOrders")` | [L300](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L300) |
| `/api/execution/health` | GET | `(HttpContext context) => …` | `Produces<ExecutionGatewayHealth>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionHealth")` | [L351](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L351) |
| `/api/execution/capabilities` | GET | `(HttpContext context) => …` | `Produces<OrderGatewayCapabilities>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionCapabilities")` | [L371](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L371) |
| `/api/execution/controls` | GET | `(HttpContext context) => …` | `Produces<ExecutionControlSnapshot>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionControls")` | [L385](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L385) |
| `/api/execution/controls/circuit-breaker` | POST | `async (UpdateExecutionCircuitBreakerRequest request, HttpContext context) => …` | `Produces<ExecutionControlSnapshot>(200)`; `Produces(403)`; `Produces(429)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("UpdateExecutionCircuitBreaker")` | [L399](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L399) |
| `/api/execution/controls/position-limits/default` | POST | `async (UpdateExecutionDefaultPositionLimitRequest request, HttpContext context) => …` | `Produces<ExecutionControlSnapshot>(200)`; `Produces(403)`; `Produces(429)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("UpdateExecutionDefaultPositionLimit")` | [L511](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L511) |
| `/api/execution/controls/position-limits/{symbol}` | POST | `async (string symbol, UpdateExecutionSymbolPositionLimitRequest request, HttpContext context) => …` | `Produces<ExecutionControlSnapshot>(200)`; `Produces(403)`; `Produces(429)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("UpdateExecutionSymbolPositionLimit")` | [L542](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L542) |
| `/api/execution/controls/manual-overrides` | POST | `async (CreateExecutionManualOverrideRequest request, HttpContext context) => …` | `Produces<ExecutionManualOverride>(201)`; `Produces(403)`; `Produces(429)`; `Produces(400)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json`; `Results.BadRequest` | `WithName("CreateExecutionManualOverride")` | [L573](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L573) |
| `/api/execution/controls/manual-overrides/{overrideId}/clear` | POST | `async (string overrideId, ClearExecutionManualOverrideRequest request, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces(403)`; `Produces(429)`; `Produces(404)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ClearExecutionManualOverride")` | [L619](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L619) |
| `/api/execution/sessions` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<PaperSessionSummaryDto>>(200)`; `Results.Json` | `WithName("GetExecutionSessions")` | [L667](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L667) |
| `/api/execution/sessions/{sessionId}` | GET | `async (string sessionId, HttpContext context) => …` | `Produces<PaperSessionDetailDto>(200)`; `Produces(404)`; `Results.NotFound`; `Results.Json` | `WithName("GetExecutionSessionById")` | [L680](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L680) |
| `/api/execution/sessions/{sessionId}/tca` | GET | `async (string sessionId, HttpContext context) => …` | `Produces<SessionTcaReport>(200)`; `Produces(404)`; `Results.NotFound`; `Results.Json` | `WithName("GetExecutionSessionTcaReport")` | [L694](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L694) |
| `/api/execution/sessions/create` | POST | `async (CreatePaperSessionRequest request, HttpContext context) => …` | `Produces<PaperSessionSummaryDto>(201)`; `Produces(400)`; `Produces(429)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.BadRequest`; `Results.Json` | `WithName("CreateExecutionSession")` | [L716](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L716) |
| `/api/execution/sessions/{sessionId}/close` | POST | `async (string sessionId, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces(404)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("CloseExecutionSession")` | [L785](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L785) |
| `/api/execution/sessions/{sessionId}/replay` | GET | `async (string sessionId, HttpContext context) => …` | `Produces<PaperSessionReplayVerificationDto>(200)`; `Produces(404)`; `Produces(503)`; `Results.Unauthorized`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("ReplayExecutionSession")` | [L840](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L840) |
| `/api/execution/accounts` | GET | `(HttpContext context) => …` | `Produces<IReadOnlyList<ExecutionAccountDetailSnapshot>>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionAccounts")` | [L913](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L913) |
| `/api/execution/accounts/{accountId}` | GET | `(string accountId, HttpContext context) => …` | `Produces<ExecutionAccountDetailSnapshot>(200)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("GetExecutionAccountById")` | [L933](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L933) |
| `/api/execution/accounts/{accountId}/positions` | GET | `(string accountId, HttpContext context) => …` | `Produces<ExecutionPosition[]>(200)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("GetExecutionAccountPositions")` | [L955](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L955) |
| `/api/execution/portfolio/aggregate` | GET | `(HttpContext context) => …` | `Produces<MultiAccountPortfolioSnapshot>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetExecutionPortfolioAggregate")` | [L979](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L979) |
| `/api/execution/positions/actions/close` | POST | `async (ExecutionPositionActionRequest request, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces<TradingActionResult>(400)`; `Produces(403)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("ClosePositionByKey")` | [L999](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L999) |
| `/api/execution/positions/actions/upsize` | POST | `async (ExecutionPositionActionRequest request, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces<TradingActionResult>(400)`; `Produces(403)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("UpsizePositionByKey")` | [L1062](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L1062) |
| `/api/execution/positions/{symbol}/close` | POST | `async (string symbol, Guid? fundAccountId, HttpContext context) => …` | `Produces<TradingActionResult>(200)`; `Produces<TradingActionResult>(400)`; `Produces(403)`; `Produces(429)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("ClosePosition")` | [L1115](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs#L1115) |

### ExecutionEndpoints.Workflows

Source: [src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/execution/orders/preview` | POST | `async (OrderRequest request, HttpContext context) => …` | `Results.BadRequest`; `Results.StatusCode`; `Results.Json` | — | [L18](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L18) |
| `/api/execution/strategy-parity` | GET | `async (Guid accountId, string runId, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L36](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L36) |
| `/api/execution/journal/orders` | GET | `async (Guid accountId, string? symbol, string? strategy, string? tag, DateTimeOffset? from, DateTimeOffset? to, string? status, int? take, HttpContext context) => …` | `Results.Json` | — | [L45](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L45) |
| `/api/execution/journal/orders/{orderId}` | GET | `async (string orderId, Guid accountId, string? attemptId, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L55](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L55) |
| `/api/execution/journal/summary` | GET | `async (Guid accountId, DateTimeOffset? from, DateTimeOffset? to, HttpContext context) => …` | `Results.Json` | — | [L65](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L65) |
| `/api/execution/notifications` | GET | `async (Guid accountId, HttpContext context) => …` | `Results.Json` | — | [L74](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L74) |
| `/api/execution/notifications/stream` | GET | `async (Guid accountId, HttpContext context) => …` | — | — | [L84](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L84) |
| `/api/execution/alert-settings` | GET | `async (Guid accountId, HttpContext context) => …` | `Results.Json` | — | [L116](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L116) |
| `/api/execution/alert-settings` | POST | `async (ExecutionAlertSettings settings, HttpContext context) => …` | `Results.StatusCode`; `Results.BadRequest`; `Results.NoContent` | — | [L126](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.Workflows.cs#L126) |

### FirstRunEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/workstation/first-run` | GET | `async (HttpContext context, FirstRunExperienceService service, CancellationToken ct) => …` | `Results.Ok` | — | [L18](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs#L18) |
| `/api/workstation/first-run/complete` | POST | `async (HttpContext context, CompleteFirstRunRequestDto request, FirstRunExperienceService service, CancellationToken ct) => …` | `Results.Ok`; `Results.BadRequest` | — | [L21](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs#L21) |
| `/api/workstation/first-run/outcomes/complete` | POST | `async (HttpContext context, CompleteActivationOutcomeRequestDto request, FirstRunExperienceService service, CancellationToken ct) => …` | `Results.Ok`; `Results.BadRequest` | — | [L27](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs#L27) |
| `/api/workstation/desktop/launch` | POST | `(HttpContext context, DesktopLaunchRequest request, [FromServices]DesktopWorkstationLaunchService service) => …` | `Results.NotFound`; `Results.Ok`; `Results.Json` | — | [L33](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs#L33) |
| `/api/auth/desktop-launch/{ticket}` | GET | `(HttpContext context, string ticket, [FromServices]DesktopLaunchTicketService service) => …` | `Results.NotFound`; `Results.Ok` | — | [L56](../../src/Meridian.Ui.Shared/Endpoints/FirstRunEndpoints.cs#L56) |

### FundAccountEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-accounts` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<AccountSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateFundAccount")` | [L36](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L36) |
| `/api/fund-accounts/{accountId}` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<AccountSummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetFundAccount")` | [L59](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L59) |
| `/api/fund-accounts` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("QueryFundAccounts")` | [L78](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L78) |
| `/api/fund-accounts/fund/{fundId}` | GET | `async (Guid fundId, HttpContext context) => …` | `Produces<FundAccountsDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetFundAccounts")` | [L119](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L119) |
| `/api/funds/{fundId}/accounts` | GET | `async (Guid fundId, string? accountType, string? status, HttpContext context) => …` | `Produces<IReadOnlyList<AccountSummaryDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Json` | `WithName("GetFundAccountsByOwnershipTraversal")` | [L137](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L137) |
| `/api/fund-accounts/{accountId}/custodian-details` | PATCH | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<AccountSummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("UpdateCustodianAccountDetails")` | [L167](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L167) |
| `/api/fund-accounts/{accountId}/bank-details` | PATCH | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<AccountSummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("UpdateBankAccountDetails")` | [L197](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L197) |
| `/api/fund-accounts/{accountId}` | DELETE | `async (Guid accountId, HttpContext context) => …` | `Produces<AccountSummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Unauthorized`; `Results.NotFound`; `Results.Json` | `WithName("DeactivateFundAccount")` | [L227](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L227) |
| `/api/fund-accounts/brokerage-sync/accounts` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<WorkstationBrokerageAccountDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("DiscoverBrokerageSyncAccounts")` | [L253](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L253) |
| `/api/portfolio/household` | GET | `async (string? provider, HttpContext context) => …` | `Produces<BrokerageHouseholdPortfolioDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetBrokerageHouseholdPortfolio")` | [L276](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L276) |
| `/api/fund-accounts/{accountId}/brokerage-sync/status` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<WorkstationBrokerageSyncStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetAccountBrokerageSyncStatus")` | [L297](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L297) |
| `/api/fund-accounts/{accountId}/brokerage-sync/link` | POST | `async (Guid accountId, HttpContext context) => …` | `Produces<WorkstationBrokerageAccountLinkDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Problem`; `Results.Json` | `WithName("LinkAccountBrokerageSync")` | [L313](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L313) |
| `/api/fund-accounts/{accountId}/brokerage-sync/run` | POST | `async (Guid accountId, HttpContext context) => …` | `Produces<WorkstationBrokerageSyncStatusDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("RunAccountBrokerageSync")` | [L337](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L337) |
| `/api/fund-accounts/{accountId}/brokerage-sync/positions` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<FundAccountBrokeragePositionDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetAccountBrokerageSyncPositions")` | [L356](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L356) |
| `/api/fund-accounts/{accountId}/brokerage-sync/activity` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<FundAccountBrokerageSyncActivityDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.NotFound`; `Results.Json` | `WithName("GetAccountBrokerageSyncActivity")` | [L372](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L372) |
| `/api/fund-accounts/{accountId}/brokerage-sync/reconcile-ledger` | POST | `async (Guid accountId, HttpContext context) => …` | `Produces<ProviderLedgerReconciliationDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("RunProviderLedgerReconciliation")` | [L391](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L391) |
| `/api/fund-accounts/{accountId}/brokerage-sync/reconciliation/latest` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<ProviderLedgerReconciliationDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.NotFound`; `Results.Json` | `WithName("GetLatestProviderLedgerReconciliation")` | [L436](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L436) |
| `/api/fund-accounts/{accountId}/close-readiness` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<FundAccountCloseReadinessDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.NotFound`; `Results.Json` | `WithName("GetFundAccountCloseReadiness")` | [L463](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L463) |
| `/api/fund-accounts/{accountId}/performance` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<BrokeragePortfolioPerformanceDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetAccountBrokeragePerformance")` | [L493](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L493) |
| `/api/fund-accounts/{accountId}/cash-flow` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<BrokerageCashFlowSummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status501NotImplemented)`; `Results.Json` | `WithName("GetAccountBrokerageCashFlow")` | [L512](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L512) |
| `/api/fund-accounts/{accountId}/balance-snapshots` | POST | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<AccountBalanceSnapshotDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("RecordAccountBalanceSnapshot")` | [L533](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L533) |
| `/api/fund-accounts/{accountId}/balance-snapshots` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<AccountBalanceSnapshotDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountBalanceHistory")` | [L565](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L565) |
| `/api/fund-accounts/{accountId}/balance-snapshots/latest` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<AccountBalanceSnapshotDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetLatestAccountBalanceSnapshot")` | [L587](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L587) |
| `/api/fund-accounts/{accountId}/sync-history` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<AccountSyncHistoryEntryDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountSyncHistory")` | [L608](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L608) |
| `/api/fund-accounts/{accountId}/readiness` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<AccountReadinessSnapshotDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetAccountReadiness")` | [L627](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L627) |
| `/api/fund-accounts/{accountId}/custodian-statements` | POST | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<CustodianStatementBatchDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("IngestCustodianStatement")` | [L648](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L648) |
| `/api/fund-accounts/{accountId}/custodian-positions` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<CustodianPositionLineDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("GetCustodianPositions")` | [L685](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L685) |
| `/api/fund-accounts/{accountId}/bank-statements` | POST | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<BankStatementBatchDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("IngestBankStatement")` | [L707](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L707) |
| `/api/fund-accounts/{accountId}/bank-statement-lines` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<BankStatementLineDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetBankStatementLines")` | [L744](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L744) |
| `/api/fund-accounts/{accountId}/reconcile` | POST | `async (Guid accountId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize` | `Produces<AccountReconciliationRunDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("ReconcileFundAccount")` | [L768](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L768) |
| `/api/fund-accounts/{accountId}/reconciliation-runs` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<IReadOnlyList<AccountReconciliationRunDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountReconciliationRuns")` | [L793](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L793) |
| `/api/fund-accounts/{accountId}/reconciliation-queue-status` | GET | `async (Guid accountId, HttpContext context) => …` | `Produces<ReconciliationQueueAccountStatusDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountReconciliationQueueStatus")` | [L812](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L812) |
| `/api/fund-accounts/reconciliation-runs/{runId}/results` | GET | `async (Guid runId, HttpContext context) => …` | `Produces<IReadOnlyList<AccountReconciliationResultDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountReconciliationResults")` | [L853](../../src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs#L853) |

### FundStructureEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/setup-drafts/validate` | POST | `(JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<FundStructureSetupDraftDto>` | `Produces<FundStructureSetupPreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("ValidateFundStructureSetupDraft")` | [L29](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L29) |
| `/api/fund-structure/setup-drafts/create` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<FundStructureSetupDraftDto>` | `Produces<FundStructureSetupResultDto>(StatusCodes.Status201Created)`; `Produces<FundStructureSetupValidationSummaryDto>(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("CreateFundStructureSetupDraft")` | [L52](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L52) |
| `/api/fund-structure/organizations` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateOrganizationRequest>` | `Produces<OrganizationSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateOrganization")` | [L95](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L95) |
| `/api/fund-structure/businesses` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateBusinessRequest>` | `Produces<BusinessSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateBusiness")` | [L114](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L114) |
| `/api/fund-structure/clients` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateClientRequest>` | `Produces<ClientSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateClient")` | [L133](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L133) |
| `/api/fund-structure/funds` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateFundRequest>` | `Produces<FundSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateStructureFund")` | [L152](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L152) |
| `/api/fund-structure/sleeves` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateSleeveRequest>` | `Produces<SleeveSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateSleeve")` | [L171](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L171) |
| `/api/fund-structure/vehicles` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateVehicleRequest>` | `Produces<VehicleSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateVehicle")` | [L190](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L190) |
| `/api/fund-structure/entities` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateLegalEntityRequest>` | `Produces<LegalEntitySummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateLegalEntity")` | [L209](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L209) |
| `/api/fund-structure/entities/{entityId}` | PUT | `async (Guid entityId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<UpdateLegalEntityProfileRequest>` | `Produces<LegalEntitySummaryDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("UpdateLegalEntityProfile")` | [L228](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L228) |
| `/api/fund-structure/investment-portfolios` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<CreateInvestmentPortfolioRequest>` | `Produces<InvestmentPortfolioSummaryDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("CreateInvestmentPortfolio")` | [L267](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L267) |
| `/api/fund-structure/links` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<LinkFundStructureNodesRequest>` | `Produces<OwnershipLinkDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("LinkFundStructureNodes")` | [L286](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L286) |
| `/api/fund-structure/links/{ownershipLinkId}` | PUT | `async (Guid ownershipLinkId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<UpdateOwnershipLinkRequest>` | `Produces<OwnershipLinkDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("UpdateOwnershipLink")` | [L305](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L305) |
| `/api/fund-structure/links/{ownershipLinkId}/expire` | POST | `async (Guid ownershipLinkId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<ExpireOwnershipLinkRequest>` | `Produces<OwnershipLinkDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("ExpireOwnershipLink")` | [L335](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L335) |
| `/api/fund-structure/links/{ownershipLinkId}/replace` | POST | `async (Guid ownershipLinkId, JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<ReplaceOwnershipLinkRequest>` | `Produces<OwnershipLinkDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("ReplaceOwnershipLink")` | [L365](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L365) |
| `/api/fund-structure/links/validate` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<ValidateOwnershipGraphRequest>` | `Produces<OwnershipGraphValidationResultDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("ValidateOwnershipGraph")` | [L395](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L395) |
| `/api/fund-structure/assignments` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<AssignFundStructureNodeRequest>` | `Produces<FundStructureAssignmentDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Results.Problem`; `Results.Json` | `WithName("AssignFundStructureNode")` | [L409](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L409) |
| `/api/fund-structure/ledger-mapping-assignments` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<LedgerMappingAssignmentRequestDto>` | `Produces<LedgerMappingAssignmentResultDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.Unauthorized`; `Results.Json` | `WithName("AssignLedgerMapping")` | [L434](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L434) |
| `/api/fund-structure/graph` | GET | `async (HttpContext context) => …` | `Produces<OrganizationStructureGraphDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetOrganizationStructureGraph")` | [L537](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L537) |
| `/api/fund-structure/legacy-graph` | GET | `async (HttpContext context) => …` | `Produces<FundStructureGraphDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLegacyFundStructureGraph")` | [L569](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L569) |
| `/api/fund-structure/businesses/{businessId}/advisory-view` | GET | `async (Guid businessId, HttpContext context) => …` | `Produces<AdvisoryStructureViewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetAdvisoryStructureView")` | [L595](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L595) |
| `/api/fund-structure/businesses/{businessId}/fund-view` | GET | `async (Guid businessId, HttpContext context) => …` | `Produces<FundOperatingViewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetFundOperatingStructureView")` | [L628](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L628) |
| `/api/fund-structure/accounting-view` | GET | `async (HttpContext context) => …` | `Produces<AccountingStructureViewDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAccountingStructureView")` | [L665](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L665) |
| `/api/fund-structure/ledger-mapping-view` | GET | `async (HttpContext context) => …` | `Produces<LedgerMappingWorkbenchDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetLedgerMappingWorkbench")` | [L696](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L696) |
| `/api/fund-structure/cash-flow-view` | GET | `async (HttpContext context) => …` | `Produces<GovernanceCashFlowViewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `Results.Json`; `Results.NotFound`; `Results.Problem` | `WithName("GetGovernanceCashFlowView")` | [L729](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L729) |
| `/api/fund-structure/reporting/templates` | GET | `(HttpContext context) => …` | `Produces<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Json` | `WithName("ListReportTemplates")` | [L809](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L809) |
| `/api/fund-structure/reporting/templates` | POST | `(ReportTemplateDefinitionDto request, HttpContext context) => …` | `Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("RegisterReportTemplate")` | [L837](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L837) |
| `/api/fund-structure/reporting/templates/drafts` | POST | `(ReportTemplateDraftRequestDto request, HttpContext context) => …` | `Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("CreateReportTemplateDraft")` | [L890](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L890) |
| `/api/fund-structure/reporting/templates/{templateName}/versions/{version}/submit` | POST | `(string templateName, int version, ReportTemplateDecisionRequestDto? request, HttpContext context) => …` | `Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("SubmitReportTemplateDraft")` | [L942](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L942) |
| `/api/fund-structure/reporting/templates/{templateName}/versions/{version}/approve` | POST | `(string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) => …` | `Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("ApproveReportTemplateDraft")` | [L991](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L991) |
| `/api/fund-structure/reporting/templates/{templateName}/versions/{version}/reject` | POST | `(string templateName, int version, ReportTemplateDecisionRequestDto request, HttpContext context) => …` | `Produces<ReportTemplateGovernanceRecordDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("RejectReportTemplateDraft")` | [L1040](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1040) |
| `/api/fund-structure/reporting/templates/render` | POST | `(RenderReportTemplateRequestDto request, HttpContext context) => …` | `Results.Problem`; `Results.Json` | — | [L1089](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1089) |
| `/api/fund-structure/reporting/runs` | GET | `async (int? limit, HttpContext context) => …` | `Produces<WorkstationReportingHistoryPayload>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `Results.Json`; `Results.Problem` | `WithName("ListCanonicalReportingRuns")` | [L1126](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1126) |
| `/api/fund-structure/reporting/runs/readiness` | POST | `async (ReportingRunRequestDto request, HttpContext context) => …` | `Produces<ReportingRunReadinessDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("AssessReportingRunReadiness")` | [L1174](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1174) |
| `/api/fund-structure/reporting/runs` | POST | `async (ReportingRunRequestDto request, HttpContext context) => …` | `Produces<ReportingRunResultDto>(StatusCodes.Status201Created)`; `Produces<ReportingRunReadinessDto>(StatusCodes.Status409Conflict)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("RunReportingNow")` | [L1224](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1224) |
| `/api/fund-structure/reporting/runs/{runId}/audit` | GET | `(string runId, HttpContext context) => …` | `Produces<ReportingRunAuditTrailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Json`; `Results.Problem` | `WithName("GetReportingRunAuditTrail")` | [L1323](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1323) |
| `/api/fund-structure/reporting/runs/{runId}/report-writer-grids/{gridId}` | GET | `(string runId, string gridId, string? format, HttpContext context) => …` | `Produces<ReportWriterGridRenderDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status200OK, contentType : "application/json")`; `Produces(StatusCodes.Status200OK, contentType : "text/csv")`; `Produces(StatusCodes.Status200OK, contentType : "application/pdf")`; `Produces(StatusCodes.Status200OK, contentType : StructuredXlsxContentType)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Json`; `Results.File`; `Results.Problem` | `WithName("GetReportingRunReportWriterGrid")` | [L1390](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1390) |
| `/api/fund-structure/reporting/starter-kits` | GET | `(HttpContext context) => …` | `Produces<IReadOnlyList<ReportingStarterKitDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Json` | `WithName("ListReportingStarterKits")` | [L1441](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1441) |
| `/api/fund-structure/reporting/starter-kits/{kitId}/provision` | POST | `(string kitId, HttpContext context) => …` | `Produces<ReportingStarterKitProvisionResultDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("ProvisionReportingStarterKit")` | [L1455](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1455) |
| `/api/fund-structure/reporting/schedules` | GET | `(HttpContext context) => …` | `Produces<IReadOnlyList<ReportingScheduleRecordDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Json` | `WithName("ListReportingSchedules")` | [L1506](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1506) |
| `/api/fund-structure/reporting/schedules` | POST | `async Task<IResult>(ReportingScheduleUpsertRequestDto request, HttpContext context) => …` | `Produces<ReportingScheduleRecordDto>(StatusCodes.Status201Created)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("UpsertReportingSchedule")` | [L1522](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1522) |
| `/api/fund-structure/reporting/schedules/{scheduleId}/pause` | POST | `(string scheduleId, HttpContext context) => …` | `Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("PauseReportingSchedule")` | [L1576](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1576) |
| `/api/fund-structure/reporting/schedules/{scheduleId}/resume` | POST | `(string scheduleId, HttpContext context) => …` | `Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("ResumeReportingSchedule")` | [L1581](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1581) |
| `/api/fund-structure/reporting/schedules/{scheduleId}/disable` | POST | `(string scheduleId, HttpContext context) => …` | `Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("DisableReportingSchedule")` | [L1586](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1586) |
| `/api/fund-structure/reporting/schedules/{scheduleId}` | DELETE | `(string scheduleId, HttpContext context) => …` | `Produces<ReportingScheduleRecordDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("DeleteReportingSchedule")` | [L1591](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1591) |
| `/api/fund-structure/reporting/schedules/{scheduleId}/run` | POST | `async (string scheduleId, HttpContext context) => …` | `Produces<ReportingScheduleRunResultDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `Results.Unauthorized`; `Results.Json`; `Results.Problem` | `WithName("RunReportingScheduleNow")` | [L1598](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1598) |
| `/api/fund-structure/reporting/schedules/run-due` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("RunDueReportingSchedules")` | [L1643](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1643) |
| `/api/fund-structure/reporting/packs/history` | GET | `(string period, string fundAccountId, HttpContext context) => …` | `Produces<IReadOnlyList<ReportPackWorkflowRecordDto>>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status410Gone)`; `Results.Json` | — | [L1652](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1652) |
| `/api/fund-structure/report-packs/{reportId}` | GET | `async (Guid reportId, HttpContext context) => …` | `Produces<FundReportPackSnapshotDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status410Gone)`; `Results.NotFound`; `Results.Json` | `WithName("GetFundReportPack")` | [L1682](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1682) |
| `/api/fund-structure/report-packs/{reportId}/evidence-bundle` | GET | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("ExportFundReportPackEvidenceBundle")` | [L1721](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1721) |
| `/api/fund-structure/report-packs/{reportId}/ledger-provenance` | GET | `async (Guid reportId, string scopeKey, HttpContext context) => …` | `Produces<LedgerAmountProvenanceDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status410Gone)`; `Results.NotFound`; `Results.Json` | `WithName("GetFundReportPackLedgerProvenance")` | [L1729](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1729) |
| `/api/fund-structure/accounting/transaction-lab/preview` | POST | `(InvestmentAccountingTransactionLabRequestDto request, HttpContext context) => …` | `Produces<InvestmentAccountingTransactionLabPreviewDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("PreviewInvestmentAccountingTransactionLab")` | [L1778](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs#L1778) |

### FundStructureEndpoints.ReportingCompatibility

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/workspace-view` (relative declaration on `group`)<br>Caller-derived mounts: `/api/fund-structure/workspace-view` | GET | `async (HttpContext context) => …` | `Produces<FundOperationsWorkspaceDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.Problem`; `Results.Json` | `WithName("GetFundOperationsWorkspaceView")` | [L23](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs#L23) |
| `/report-pack-preview` (relative declaration on `legacyReportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/report-pack-preview` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("PreviewFundReportPack")` | [L60](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs#L60) |
| `/report-packs` (relative declaration on `legacyReportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/report-packs` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("GenerateFundReportPack")` | [L69](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs#L69) |
| `/report-packs` (relative declaration on `legacyReportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/report-packs` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<FundReportPackHistoryItemDto>>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status410Gone)`; `Results.Problem`; `Results.Json` | `WithName("GetFundReportPackHistory")` | [L78](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs#L78) |
| `/structured-exports/{exportId}` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/structured-exports/{exportId}` | GET | `async (string exportId, string fundProfileId, DateTimeOffset? asOf, string? currency, string? format, HttpContext context) => …` | `Produces<StructuredReportingExportPayloadDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status200OK, contentType : "application/json")`; `Produces(StatusCodes.Status200OK, contentType : "text/csv")`; `Produces(StatusCodes.Status200OK, contentType : StructuredXlsxContentType)`; `Produces(StatusCodes.Status400BadRequest)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Results.Problem`; `Results.File`; `Results.Json` | `WithName("GetStructuredReportingExport")` | [L125](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingCompatibility.cs#L125) |

### FundStructureEndpoints.ReportingGovernance

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/reporting/runs/series/{seriesId}` | GET | `(string seriesId, HttpContext context) => …` | `Produces<ReportingGovernanceSeriesHistoryDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("GetGovernedReportingSeries")` | [L33](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L33) |
| `/api/fund-structure/reporting/runs/{runId}/restatement-requests` | GET | `(string runId, HttpContext context) => …` | `Produces<IReadOnlyList<ReportingGovernanceRestatementDto>>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("ListGovernedReportingRestatements")` | [L49](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L49) |
| `/api/fund-structure/reporting/runs/restatement-requests/{requestId}` | GET | `(string requestId, HttpContext context) => …` | `Produces<ReportingGovernanceRestatementDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("GetGovernedReportingRestatement")` | [L65](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L65) |
| `/api/fund-structure/reporting/runs/{runId}` | GET | `(string runId, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("GetGovernedReportingRun")` | [L81](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L81) |
| `/api/fund-structure/reporting/runs/{runId}/amounts` | GET | `(string runId, HttpContext context) => …` | `Produces<IReadOnlyList<Meridian.Contracts.Workstation.ReportLedgerAmountBindingDto>>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("ListGovernedReportingAmounts")` | [L97](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L97) |
| `/api/fund-structure/reporting/runs/{runId}/artifacts` | GET | `(string runId, HttpContext context) => …` | `Produces<IReadOnlyList<ReportingGovernanceRetainedArtifactDto>>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("ListGovernedReportingArtifacts")` | [L108](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L108) |
| `/api/fund-structure/reporting/runs/{runId}/artifacts/{artifactId}` | GET | `(string runId, string artifactId, HttpContext context) => …` | `Produces(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("DownloadGovernedReportingArtifact")` | [L125](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L125) |
| `/api/fund-structure/reporting/runs/{runId}/govern` | POST | `(string runId, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status201Created)`; `ProducesProblem(StatusCodes.Status400BadRequest)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)` | `WithName("GovernCompletedReportingRun")` | [L144](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L144) |
| `/api/fund-structure/reporting/runs/{runId}/validate` | POST | `(string runId, ReportingGovernanceVersionRequestDto request, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("ValidateGovernedReportingRun")` | [L157](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L157) |
| `/api/fund-structure/reporting/runs/{runId}/submit` | POST | `(string runId, ReportingGovernanceVersionRequestDto request, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("SubmitGovernedReportingRun")` | [L172](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L172) |
| `/api/fund-structure/reporting/runs/{runId}/approve` | POST | `(string runId, ReportingGovernanceApprovalRequestDto request, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("ApproveGovernedReportingRun")` | [L187](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L187) |
| `/api/fund-structure/reporting/runs/{runId}/release` | POST | `(string runId, ReportingGovernanceVersionRequestDto request, HttpContext context) => …` | `Produces<GovernedReportingRunDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("ReleaseGovernedReportingRun")` | [L202](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L202) |
| `/api/fund-structure/reporting/runs/{runId}/restatement-requests` | POST | `(string runId, ReportingGovernanceRestatementRequestDto request, HttpContext context) => …` | `Produces<ReportingGovernanceRestatementDto>(StatusCodes.Status201Created)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("RequestGovernedReportingRestatement")` | [L217](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L217) |
| `/api/fund-structure/reporting/runs/restatement-requests/{requestId}/approve` | POST | `(string requestId, ReportingGovernanceRestatementApprovalRequestDto request, HttpContext context) => …` | `Produces<ReportingGovernanceRestatementApprovalDto>(StatusCodes.Status200OK)`; `ProducesProblem(StatusCodes.Status401Unauthorized)`; `ProducesProblem(StatusCodes.Status403Forbidden)`; `ProducesProblem(StatusCodes.Status404NotFound)`; `ProducesProblem(StatusCodes.Status409Conflict)`; `ProducesProblem(StatusCodes.Status503ServiceUnavailable)`; `ProducesValidationProblem()` | `WithName("ApproveGovernedReportingRestatement")` | [L232](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingGovernance.cs#L232) |

### FundStructureEndpoints.ReportingIncomeComparison

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/reporting/comparisons/candidates` | GET | `(Func<HttpContext, Task<IResult>>) (context => …` | — | `WithName("ListReportingIncomeComparisonCandidates")` | [L18](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs#L18) |
| `/api/fund-structure/reporting/comparisons` | POST | `(ReportingIncomeComparisonRequestDto request, HttpContext context) => …` | — | `WithName("CreateReportingIncomeComparison")` | [L22](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs#L22) |
| `/api/fund-structure/reporting/comparisons/{comparisonId}` | GET | `(string comparisonId, HttpContext context) => …` | — | `WithName("GetReportingIncomeComparison")` | [L26](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs#L26) |
| `/api/fund-structure/reporting/comparisons/{comparisonId}/contributions/{contributionId}` | GET | `(string comparisonId, string contributionId, HttpContext context) => …` | — | `WithName("GetReportingIncomeContributionSupport")` | [L30](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingIncomeComparison.cs#L30) |

### FundStructureEndpoints.ReportingRunStream

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingRunStream.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingRunStream.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/reporting/runs/{runId}/stream` | GET | `async (string runId, HttpContext context, CancellationToken ct) => …` | `Produces(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status403Forbidden)`; `Produces(StatusCodes.Status404NotFound)`; `Produces(StatusCodes.Status429TooManyRequests)`; `Produces(StatusCodes.Status503ServiceUnavailable)`; `Results.Problem`; `Results.Empty` | `WithName("GetReportingRunStream")` | [L32](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingRunStream.cs#L32) |

### FundStructureEndpoints.ReportingTombstones

Source: [src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/packs/create` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/create` | POST | `(HttpContext context) => …` | — | — | [L27](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L27) |
| `/packs` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("CreateReportingPackWorkflow")` | [L34](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L34) |
| `/packs/{reportId}/validate` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/validate` | POST | `(HttpContext context) => …` | — | — | [L43](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L43) |
| `/packs/{reportId}/submit` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/submit` | POST | `(HttpContext context) => …` | — | — | [L49](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L49) |
| `/packs/{reportId}/approve` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/approve` | POST | `(HttpContext context) => …` | — | — | [L55](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L55) |
| `/packs/{reportId}/reject` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/reject` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("RejectReportingPackWorkflow")` | [L61](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L61) |
| `/packs/{reportId}/publish` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/publish` | POST | `(HttpContext context) => …` | — | — | [L69](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L69) |
| `/packs/{reportId}/restatements` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/restatements` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("RestateReportingPackWorkflow")` | [L76](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L76) |
| `/packs/{reportId}/archive` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/archive` | POST | `(HttpContext context) => …` | — | — | [L85](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L85) |
| `/packs/{reportId}/deliveries` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/deliveries` | GET | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("GetReportingPackDeliveryHistory")` | [L92](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L92) |
| `/packs/{reportId}/deliveries/{attemptId}/package` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/package` | GET | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("GetReportingPackDeliveryPackage")` | [L100](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L100) |
| `/packs/{reportId}/deliveries/{attemptId}/artifacts/{artifactName}` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/artifacts/{artifactName}` | GET | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("GetReportingPackDeliveryArtifact")` | [L108](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L108) |
| `/packs/{reportId}/deliveries` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/deliveries` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("CreateReportingPackDeliveryAttempt")` | [L124](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L124) |
| `/packs/{reportId}/deliveries/failures` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/deliveries/failures` | POST | `(HttpContext context) => …` | `ProducesProblem(StatusCodes.Status410Gone)` | `WithName("CreateReportingPackDeliveryFailure")` | [L133](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L133) |
| `/packs/{reportId}/restate` (relative declaration on `reportingGroup`)<br>Caller-derived mounts: `/api/fund-structure/reporting/packs/{reportId}/restate` | POST | `(HttpContext context) => …` | — | — | [L142](../../src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.ReportingTombstones.cs#L142) |

### HistoricalEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/historical` | GET | `async (HttpContext context, HistoricalDataQueryService queryService, string symbol, DateOnly? from = null, DateOnly? to = null, string? dataType = null, int? skip = null, int? limit = null) => …` | `Produces(200)`; `Produces(400)`; `Produces(499)`; `Results.BadRequest`; `Results.Json`; `Results.StatusCode` | `WithName("QueryHistoricalData")` | [L24](../../src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs#L24) |
| `/api/historical/symbols` | GET | `(HistoricalDataQueryService queryService) => …` | `Produces(200)`; `Produces(400)`; `Results.Json`; `Results.BadRequest` | `WithName("GetAvailableSymbols")` | [L67](../../src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs#L67) |
| `/api/historical/{symbol}/daterange` | GET | `(HistoricalDataQueryService queryService, string symbol) => …` | `Produces(200)`; `Produces(400)`; `Results.BadRequest`; `Results.Json` | `WithName("GetSymbolDateRange")` | [L84](../../src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs#L84) |
| `/api/historical/{symbol}/bars` | GET | `async (HttpContext context, HistoricalDataQueryService queryService, string symbol, int? intervalMinutes = null, DateOnly? from = null, DateOnly? to = null, int? maxBars = null) => …` | `Produces(200)`; `Produces(400)`; `Produces(499)`; `Results.BadRequest`; `Results.Json`; `Results.StatusCode` | `WithName("GetSymbolBars")` | [L106](../../src/Meridian.Ui.Shared/Endpoints/HistoricalEndpoints.cs#L106) |

### IngestionJobEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ingestion/jobs/resumable` | GET | `(HttpContext ctx) => …` | `Produces(200)`; `Results.Json` | `WithName("GetResumableJobs")`; `WithDescription("Returns all jobs that can be resumed (failed or paused with a checkpoint).")` | [L131](../../src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs#L131) |
| `/api/ingestion/summary` | GET | `(HttpContext ctx) => …` | `Produces<IngestionJobSummary>(200)`; `Results.Json` | `WithName("GetIngestionSummary")`; `WithDescription("Returns a summary of all ingestion jobs by state and workload type.")` | [L146](../../src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs#L146) |

### InitialAccountBootstrapEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/setup/account` | GET | `(InitialAccountBootstrapService bootstrap) => …` | `Results.Content`; `Results.Redirect` | — | [L15](../../src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs#L15) |
| `/api/auth/bootstrap` | POST | `async (HttpContext context, BootstrapAccountRequest request, InitialAccountBootstrapService bootstrap, CancellationToken ct) => …` | `Results.Json`; `Results.Ok` | — | [L22](../../src/Meridian.Ui.Shared/Endpoints/InitialAccountBootstrapEndpoints.cs#L22) |

### IntercompanyAccountingArrangementEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/intercompany-accounting-arrangements` | GET | `(Guid? organizationId, Guid? bookId, DateOnly? asOf, HttpContext context) => …` | `Results.Json` | — | [L23](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L23) |
| `/api/ledger/intercompany-accounting-arrangements/legal-agreements` | GET | `(Guid bookId, DateOnly? asOf, HttpContext context) => …` | `Results.Json` | — | [L61](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L61) |
| `/api/ledger/intercompany-accounting-arrangements/{id}` | GET | `(Guid id, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L72](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L72) |
| `/api/ledger/intercompany-accounting-arrangements/{id}/history` | GET | `(Guid id, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L81](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L81) |
| `/api/ledger/intercompany-accounting-arrangements` | POST | `(CreateIntercompanyAccountingArrangementRequest request, HttpContext context) => …` | `Results.Json` | — | [L91](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L91) |
| `/api/ledger/intercompany-accounting-arrangements/{id}/revisions` | POST | `(Guid id, ReviseIntercompanyAccountingArrangementRequest request, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L104](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L104) |
| `/api/ledger/intercompany-accounting-arrangements/{id}/{action}` | POST | `(Guid id, string action, IntercompanyAccountingArrangementActionRequest request, HttpContext context) => …` | `Results.BadRequest`; `Results.NotFound`; `Results.Json` | — | [L115](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAccountingArrangementEndpoints.cs#L115) |

### IntercompanyAgreementEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/intercompany-agreements` | GET | `async (HttpContext http, Guid? entityId, IntercompanyAgreementTypeDto? agreementType, IntercompanyAgreementStatusDto? status, DateOnly? asOf, DateTimeOffset? knownAtUtc, string? cursor, int? pageSize) => …` | `Results.Json` | `WithName("ListIntercompanyAgreements")` | [L47](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L47) |
| `/api/fund-structure/intercompany-agreements/{id}` | GET | `async (Guid id, HttpContext http, DateOnly? asOf, DateTimeOffset? knownAtUtc) => …` | `Results.NotFound`; `Results.Json` | `WithName("GetIntercompanyAgreement")` | [L70](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L70) |
| `/api/fund-structure/intercompany-agreements/{id}/versions/{version}` | GET | `async (Guid id, int version, HttpContext http) => …` | `Results.NotFound`; `Results.Json` | `WithName("GetIntercompanyAgreementVersion")` | [L77](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L77) |
| `/api/fund-structure/intercompany-agreements/{id}/history` | GET | `async (Guid id, HttpContext http) => …` | `Results.NotFound`; `Results.Json` | `WithName("GetIntercompanyAgreementHistory")` | [L85](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L85) |
| `/api/fund-structure/intercompany-agreements/{id}/journal-lines` | GET | `async (Guid id, Guid ledgerBookId, DateOnly? asOf, string? cursor, int? pageSize, HttpContext http) => …` | `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("GetIntercompanyAgreementJournalLines")` | [L92](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L92) |
| `/api/fund-structure/intercompany-agreements` | POST | `async (IntercompanyAgreementCommandDto command, HttpContext http) => …` | `Results.Json` | `WithName("CreateIntercompanyAgreement")` | [L126](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L126) |
| `/api/fund-structure/intercompany-agreements/{id}/proposals` | POST | `async (Guid id, IntercompanyAgreementCommandDto command, HttpContext http) => …` | `Results.Json` | `WithName("CreateIntercompanyAgreementProposal")` | [L130](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L130) |
| `/api/fund-structure/intercompany-agreements/{id}/proposals/{proposalId}` | PUT | `async (Guid id, Guid proposalId, IntercompanyAgreementCommandDto command, HttpContext http) => …` | `Results.Json` | `WithName("SaveIntercompanyAgreementDraft")` | [L134](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyAgreementEndpoints.cs#L134) |

### IntercompanyOperationsEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/intercompany/capabilities` | GET | `(HttpContext context) => …` | `Results.Json` | — | [L19](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L19) |
| `/api/fund-structure/entities/{entityId}/timeline` | GET | `async (Guid entityId, Guid organizationId, DateTimeOffset? asOf, HttpContext context) => …` | `Results.Problem`; `Results.Json`; `Results.Conflict` | — | [L34](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L34) |
| `/api/ledger/intercompany/proposals` | GET | `async (Guid sourceBookId, HttpContext context) => …` | `Results.Json` | — | [L52](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L52) |
| `/api/ledger/intercompany/balance-schedule` | GET | `async (Guid ledgerBookId, DateOnly? asOf, HttpContext context) => …` | `Results.Json`; `Results.Conflict` | — | [L64](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L64) |
| `/api/ledger/intercompany/proposals/{proposalId}/refresh-preview` | GET | `async (Guid proposalId, HttpContext context) => …` | `Results.Json`; `Results.Conflict` | — | [L82](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L82) |
| `/api/ledger/intercompany/proposals/{proposalId}/refresh` | POST | `async (Guid proposalId, RefreshIntercompanyProposalRequest request, HttpContext context) => …` | `Results.Unauthorized`; `Results.Json`; `Results.Conflict` | — | [L100](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L100) |
| `/api/ledger/intercompany/breaks` | GET | `async (Guid sourceBookId, DateOnly? asOf, HttpContext context) => …` | `Results.Json` | — | [L122](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L122) |
| `/api/ledger/intercompany/proposals/{proposalId}/retry` | POST | `async (Guid proposalId, RetryIntercompanyProposalRequest request, HttpContext context) => …` | `Results.Unauthorized`; `Results.Json`; `Results.Conflict` | — | [L135](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L135) |
| `/api/ledger/intercompany/proposals/{proposalId}/link` | POST | `async (Guid proposalId, LinkIntercompanyCounterpartRequest request, HttpContext context) => …` | `Results.Unauthorized`; `Results.Json`; `Results.Conflict` | — | [L158](../../src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs#L158) |

### LedgerEndpoints.Consolidation

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/consolidation/preview` | GET | `async (Guid organizationId, Guid ownershipRootId, Guid eliminationBookId, Guid periodId, DateOnly asOf, HttpContext context) => …` | `Produces<ConsolidationViewDto>()`; `Produces(StatusCodes.Status400BadRequest)` | `WithName("PreviewConsolidation")` | [L17](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs#L17) |
| `/api/ledger/consolidation/drafts` | POST | `async (ConsolidationRequestDto request, HttpContext context) => …` | `Produces<ConsolidationViewDto>()` | `WithName("DraftConsolidation")` | [L26](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Consolidation.cs#L26) |

### LedgerEndpoints.ConsolidationFx

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/consolidation/fx/rate-sets` | GET | `(Func<HttpContext, Task<IResult>>) (async (HttpContext context) => …` | `Results.Json` | — | [L17](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L17) |
| `/api/ledger/consolidation/fx/rate-sets/{rateSetId}/revisions/{revision}` | GET | `async (Guid rateSetId, int revision, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L26](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L26) |
| `/api/ledger/consolidation/fx/rate-sets/approve` | POST | `async (ConsolidationFxRateSetDto request, HttpContext context) => …` | `Results.Unauthorized`; `Results.Json` | — | [L36](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L36) |
| `/api/ledger/consolidation/fx/books/{bookId}/configuration` | GET | `async (Guid bookId, DateOnly asOf, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L59](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L59) |
| `/api/ledger/consolidation/fx/books/{bookId}/configuration/{revision}` | GET | `async (Guid bookId, int revision, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L70](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L70) |
| `/api/ledger/consolidation/fx/books/{bookId}/configuration/approve` | POST | `async (Guid bookId, ConsolidationFxBookConfigurationDto request, HttpContext context) => …` | `Results.Unauthorized`; `Results.BadRequest`; `Results.Json` | — | [L81](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationFx.cs#L81) |

### LedgerEndpoints.ConsolidationTransitions

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/consolidation/transitions` | GET | `async (Guid organizationId, Guid ownershipRootId, HttpContext context) => …` | `Results.Json` | — | [L20](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs#L20) |
| `/api/ledger/consolidation/transitions/previews/{previewId}` | GET | `async (Guid previewId, HttpContext context) => …` | `Results.NotFound`; `Results.Json` | — | [L35](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs#L35) |
| `/api/ledger/consolidation/transitions/preview` | POST | `async (ConsolidationTransitionRequestDto request, HttpContext context) => …` | `Results.Json`; `Results.Conflict`; `Results.BadRequest` | — | [L51](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs#L51) |
| `/api/ledger/consolidation/transitions/activate` | POST | `async (ApproveConsolidationTransitionRequestDto request, HttpContext context) => …` | `Results.NotFound`; `Results.Json`; `Results.Conflict`; `Results.BadRequest` | — | [L68](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.ConsolidationTransitions.cs#L68) |

### LedgerEndpoints.EntityControls

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/books/{bookId}/controls` | GET | `async (Guid bookId, HttpContext context) => …` | `Results.Json` | — | [L22](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L22) |
| `/api/ledger/books/{bookId}/controls/preflight` | POST | `async (Guid bookId, HttpContext context) => …` | `Results.Json`; `Results.Conflict` | — | [L29](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L29) |
| `/api/ledger/books/{bookId}/controls/activate` | POST | `async (Guid bookId, ActivateLedgerBookControlsRequestDto request, HttpContext context) => …` | `Results.Json`; `Results.Conflict` | — | [L40](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L40) |
| `/api/ledger/controlled-close/authorizations` | POST | `async (ControlledCloseAuthorizationRequestDto request, HttpContext context) => …` | `Results.Json`; `Results.Conflict` | — | [L55](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L55) |
| `/api/fund-structure/entities/{entityId}/lifecycle` | GET | `async (Guid entityId, long? revision, HttpContext context) => …` | `Results.Json` | — | [L80](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L80) |
| `/api/fund-structure/entities/{entityId}/lifecycle/history` | GET | `async (Guid entityId, HttpContext context) => …` | `Results.Json` | — | [L85](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L85) |
| `/api/fund-structure/entities/{entityId}/lifecycle/revisions` | POST | `async (Guid entityId, AppendEntityLifecycleRevisionRequestDto request, HttpContext context) => …` | `Results.BadRequest`; `Results.Json`; `Results.Conflict` | — | [L90](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.EntityControls.cs#L90) |

### LedgerEndpoints.IntercompanyAccountBindings

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/intercompany/account-bindings` | GET | `async (Guid ledgerBookId, DateOnly asOf, HttpContext context) => …` | `Produces<IReadOnlyList<IntercompanyAccountBindingDto>>()`; `Results.Json` | `WithName("ListIntercompanyAccountBindings")` | [L16](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs#L16) |
| `/api/ledger/intercompany/account-bindings/history` | GET | `async (Guid ledgerBookId, HttpContext context) => …` | `Produces<IReadOnlyList<IntercompanyAccountBindingDto>>()`; `Results.Json` | `WithName("ListIntercompanyAccountBindingHistory")` | [L35](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs#L35) |
| `/api/ledger/intercompany/account-bindings` | POST | `async (AppendIntercompanyAccountBindingRequest request, HttpContext context) => …` | `Produces<IntercompanyAccountBindingDto>()`; `Results.BadRequest`; `Results.Json`; `Results.Conflict` | `WithName("AppendIntercompanyAccountBinding")` | [L54](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.IntercompanyAccountBindings.cs#L54) |

### LedgerEndpoints.LegacyIntercompanyAttributions

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/intercompany/legacy-attributions` | GET | `async (Guid ledgerBookId, HttpContext context) => …` | `Results.Json` | — | [L16](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs#L16) |
| `/api/ledger/intercompany/legacy-attributions/candidates` | GET | `async (Guid ledgerBookId, HttpContext context) => …` | `Results.Json` | — | [L29](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs#L29) |
| `/api/ledger/intercompany/legacy-attributions` | POST | `async (AppendLegacyIntercompanyAttributionRequest request, HttpContext context) => …` | `Results.BadRequest`; `Results.Json`; `Results.Conflict` | — | [L63](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.LegacyIntercompanyAttributions.cs#L63) |

### LedgerEndpoints.PeriodComparison

Source: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.PeriodComparison.cs](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.PeriodComparison.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/ledger/books/{ledgerBookId}/periods/{periodId}/comparison` | GET | `async (Guid ledgerBookId, Guid periodId, Guid? baselinePeriodId, DateTimeOffset? asOf, AccountingBasisKindDto? accountingBasis, HttpContext context) => …` | `Produces<PostedPeriodComparisonDto>(200)`; `Produces(400)`; `Produces(403)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.BadRequest`; `Results.Json`; `Results.NotFound` | `WithName("GetPostedLedgerPeriodComparison")` | [L19](../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.PeriodComparison.cs#L19) |

### MoneyMarketFundEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/security-master/money-market-funds/{securityId}` | GET | `async (Guid securityId, HttpContext context) => …` | `Produces<MmfDetailDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetMoneyMarketFundById")` | [L29](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L29) |
| `/api/security-master/money-market-funds/search` | POST | `async (JsonElement body, HttpContext context) => …`<br>Body reads: `Deserialize<MmfSearchQuery>` | `Produces<IReadOnlyList<MmfDetailDto>>(StatusCodes.Status200OK)`; `Results.Problem`; `Results.Json` | `WithName("SearchMoneyMarketFunds")` | [L50](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L50) |
| `/api/security-master/money-market-funds/{securityId}/liquidity` | GET | `async (Guid securityId, HttpContext context) => …` | `Produces<MmfLiquidityDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetMoneyMarketFundLiquidity")` | [L85](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L85) |
| `/api/security-master/money-market-funds/liquidity/liquid` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<MmfLiquidityDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetAllLiquidMoneyMarketFunds")` | [L106](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L106) |
| `/api/security-master/money-market-funds/{securityId}/sweep-profile` | GET | `async (Guid securityId, HttpContext context) => …` | `Produces<MmfSweepProfileDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetMoneyMarketFundSweepProfile")` | [L125](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L125) |
| `/api/security-master/money-market-funds/family/{familyName}` | GET | `async (string familyName, HttpContext context) => …` | `Produces<MmfFundFamilyDto>(StatusCodes.Status200OK)`; `Produces(StatusCodes.Status404NotFound)`; `Results.NotFound`; `Results.Json` | `WithName("GetMoneyMarketFundFamily")` | [L149](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L149) |
| `/api/security-master/money-market-funds/{securityId}/rebuild` | POST | `async (Guid securityId, HttpContext context) => …` | `Produces(StatusCodes.Status204NoContent)`; `Results.NoContent` | `WithName("RebuildMoneyMarketFundProjections")` | [L172](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L172) |
| `/api/security-master/money-market-funds/rebuild/checkpoints` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<MmfRebuildCheckpointDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetMoneyMarketFundRebuildCheckpoints")` | [L191](../../src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs#L191) |

### OmsIntegrationEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/oms/ingest` | POST | `(HttpContext context, OmsInboundMessage request, IOmsIntegrationApiHandler handler) => …` | `Results.Json`; `Results.BadRequest` | — | [L22](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L22) |
| `/api/oms/messages` | GET | `(HttpContext context, IOmsIntegrationApiHandler handler) => …` | `Results.Json` | — | [L39](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L39) |
| `/api/oms/adapters/diagnostics` | GET | `(HttpContext context, IOmsIntegrationApiHandler handler) => …` | `Results.Json` | — | [L49](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L49) |
| `/api/oms/excel/sync` | POST | `(HttpContext context, OmsSyncRequest request, IOmsIntegrationApiHandler handler) => …` | `Results.Json`; `Results.BadRequest` | — | [L59](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L59) |
| `/api/oms/auth/signing-keys/rotate` | POST | `(HttpContext context, OmsKeyRotationRequest request, IOmsIntegrationApiHandler handler) => …` | `Results.Json` | — | [L76](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L76) |
| `/api/oms/audit` | GET | `(HttpContext context, int? take, IOmsIntegrationApiHandler handler) => …` | `Results.Json` | — | [L86](../../src/Meridian.Ui.Shared/Endpoints/OmsIntegrationEndpoints.cs#L86) |

### PackagingEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/packaging/create` | POST | `async (PackageRequest request, CancellationToken ct) => …` | `Results.BadRequest`; `Results.Json`; `Results.StatusCode` | — | [L34](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L34) |
| `/api/packaging/import` | POST | `async (ImportRequest request, CancellationToken ct) => …` | `Results.BadRequest`; `Results.Json`; `Results.StatusCode` | — | [L88](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L88) |
| `/api/packaging/validate` | POST | `async (ValidateRequest request, CancellationToken ct) => …` | `Results.BadRequest`; `Results.Json` | — | [L137](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L137) |
| `/api/packaging/contents` | GET | `async (string path, CancellationToken ct) => …` | `Results.BadRequest`; `Results.Json`; `Results.NotFound` | — | [L171](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L171) |
| `/api/packaging/list` | GET | `async (string? directory) => …` | `Results.BadRequest`; `Results.Json` | — | [L204](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L204) |
| `/api/packaging/{fileName}` | DELETE | `async (string fileName, string? directory) => …` | `Results.BadRequest`; `Results.NotFound`; `Results.Ok` | — | [L249](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L249) |
| `/api/packaging/download/{fileName}` | GET | `async (string fileName, string? directory) => …` | `Results.BadRequest`; `Results.NotFound`; `Results.File` | — | [L290](../../src/Meridian.Ui.Shared/Endpoints/PackagingEndpoints.cs#L290) |

### PortfolioCashLadderEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/PortfolioCashLadderEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/PortfolioCashLadderEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/portfolio/cash-ladder` | GET | `async (HttpContext context, CancellationToken ct) => …` | `Produces<PortfolioCashLadderDto>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetPortfolioCashLadder")` | [L33](../../src/Meridian.Ui.Shared/Endpoints/PortfolioCashLadderEndpoints.cs#L33) |
| `/api/portfolio/cash-ladder/scenarios` | GET | `(HttpContext _) => …` | `Produces<IReadOnlyList<PortfolioCashScenarioDto>>(StatusCodes.Status200OK)`; `Results.Json` | `WithName("GetPortfolioCashLadderScenarios")` | [L56](../../src/Meridian.Ui.Shared/Endpoints/PortfolioCashLadderEndpoints.cs#L56) |

### PrivateCapitalRecordEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/private-capital/records` | GET | `(HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.Json` | `WithName("GetPrivateCapitalRecords")` | [L18](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L18) |
| `/api/private-capital/choices` | GET | `(HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.Json` | `WithName("GetPrivateCapitalRecordChoices")` | [L37](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L37) |
| `/api/private-capital/imports/{batchId}` | GET | `(Guid batchId, HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.NotFound`; `Results.Json` | `WithName("InspectPrivateCapitalImport")` | [L48](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L48) |
| `/api/private-capital/commitments` | POST | `(CreatePrivateCapitalCommitment request, HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.Json` | `WithName("CreatePrivateCapitalRecordCommitment")` | [L60](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L60) |
| `/api/private-capital/preview` | POST | `(PreviewPrivateCapitalImport request, HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.Json` | `WithName("PreviewPrivateCapitalImport")` | [L70](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L70) |
| `/api/private-capital/accept` | POST | `(AcceptPrivateCapitalImport request, HttpContext context, [FromServices]PrivateCapitalRecordService service, CancellationToken ct) => …` | `Results.NotFound`; `Results.Json` | `WithName("AcceptPrivateCapitalImport")` | [L82](../../src/Meridian.Ui.Shared/Endpoints/PrivateCapitalRecordEndpoints.cs#L82) |

### PromotionEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/promotion/evaluate/{runId}` | GET | `async (string runId, HttpContext context) => …` | `Produces<PromotionEvaluationResult>(200)`; `Produces(401)`; `Produces(403)`; `Produces(404)`; `Produces(501)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("EvaluatePromotion")` | [L23](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs#L23) |
| `/api/promotion/approve` | POST | `async (PromotionApprovalRequest request, HttpContext context) => …` | `Produces<PromotionDecisionResult>(201)`; `Produces<PromotionDecisionResult>(400)`; `Produces(401)`; `Produces<PromotionDecisionResult>(403)`; `Produces(501)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("ApprovePromotion")` | [L51](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs#L51) |
| `/api/promotion/reject` | POST | `async (PromotionRejectionRequest request, HttpContext context) => …` | `Produces<PromotionDecisionResult>(200)`; `Produces(401)`; `Produces<PromotionDecisionResult>(403)`; `Produces(501)`; `Results.Unauthorized`; `Results.Problem`; `Results.Json` | `WithName("RejectPromotion")` | [L84](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs#L84) |
| `/api/promotion/history` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<StrategyPromotionRecord>>(200)`; `Produces(401)`; `Produces(403)`; `Produces(501)`; `Results.Problem`; `Results.Json` | `WithName("GetPromotionHistory")` | [L113](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs#L113) |
| `/api/promotion/runs/{runId}/walk-forward-evidence` | POST | `async (string runId, RecordWalkForwardEvidenceRequest request, HttpContext context) => …` | `Produces<StrategyRunWalkForwardEvidence>(201)`; `Produces(400)`; `Produces(401)`; `Produces(403)`; `Produces(404)`; `Produces(501)`; `Results.Problem`; `Results.BadRequest`; `Results.NotFound`; `Results.Json` | `WithName("RecordWalkForwardEvidence")` | [L135](../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs#L135) |

### ProviderCredentialOwnershipEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/provider-routing/connections` | POST | `(CreateOwnedProviderConnectionRequest request, HttpContext context, [FromServices]ProviderConnectionService service) => …` | `Results.Json` | `WithName("CreateOwnedProviderConnection")` | [L22](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L22) |
| `/api/provider-routing/connections/{connectionId}/runtime-credentials` | PUT | `(string connectionId, BindProviderRuntimeCredentialRequest request, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | `Results.Json` | `WithName("BindProviderRuntimeCredentials")` | [L28](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L28) |
| `/api/provider-routing/connections/{connectionId}/runtime-credentials/status` | GET | `(string connectionId, string providerId, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | `Results.Json` | `WithName("GetProviderRuntimeCredentialApplicationStatus")` | [L34](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L34) |
| `/api/provider-routing/connections/{connectionId}/runtime-credentials/apply` | POST | `(string connectionId, ApplyProviderRuntimeCredentialRequest request, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | `Results.Json` | `WithName("ApplyProviderRuntimeCredentials")` | [L40](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L40) |
| `/api/provider-routing/connections/{connectionId}/credentials/migration-preview` | GET | `(string connectionId, string providerId, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | `Results.Json` | `WithName("PreviewProviderCredentialOwnershipMigration")` | [L46](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L46) |
| `/api/provider-routing/connections/{connectionId}/credentials/migrate` | POST | `(string connectionId, ProviderCredentialMigrationRequest request, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | — | `WithName("MigrateProviderCredentialOwnership")` | [L52](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L52) |
| `/api/provider-routing/connections/{connectionId}/credentials/rollback` | POST | `(string connectionId, ProviderCredentialMigrationRequest request, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | — | `WithName("RollbackProviderCredentialOwnership")` | [L55](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L55) |
| `/api/provider-routing/connections/{connectionId}/credentials/finalize` | POST | `(string connectionId, ProviderCredentialMigrationRequest request, HttpContext context, [FromServices]ProviderRuntimeCredentialService service) => …` | `Results.Json` | `WithName("FinalizeProviderCredentialOwnership")` | [L58](../../src/Meridian.Ui.Shared/Endpoints/ProviderCredentialOwnershipEndpoints.cs#L58) |

### ProviderEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/config/data-sources` | GET | `(ConfigStore store) => …` | `Produces(200)`; `Results.Json` | `WithName("GetDataSourcesAlias")`; `WithDescription("Alias for /api/config/datasources for backward compatibility.")` | [L684](../../src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs#L684) |
| `/api/config/data-sources` | POST | `async (HttpContext context, ConfigStore store, DataSourceConfigRequest req) => …` | `Produces(200)`; `Produces(400)`; `Produces(StatusCodes.Status401Unauthorized)`; `Produces(StatusCodes.Status403Forbidden)`; `Results.BadRequest`; `Results.Ok` | `WithName("UpsertDataSourceAlias")`; `WithDescription("Alias for /api/config/datasources POST for backward compatibility.")` | [L702](../../src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs#L702) |

### RiskEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/rules` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/rules`; `/api/v1/risk/rules` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<RiskRuleStatusDto>>(200)`; `Produces(403)`; `Produces(503)`; `Results.Problem`; `Results.Json` | — | [L92](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L92) |
| `/rules/{ruleName}/status` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/rules/{ruleName}/status`; `/api/v1/risk/rules/{ruleName}/status` | GET | `async (string ruleName, HttpContext context) => …` | `Produces<RiskRuleStatusDto>(200)`; `Produces(403)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L118](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L118) |
| `/rules/{ruleName}/config` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/rules/{ruleName}/config`; `/api/v1/risk/rules/{ruleName}/config` | GET | `(string ruleName, HttpContext context) => …` | `Produces<RiskRuleConfigDto>(200)`; `Produces(403)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L150](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L150) |
| `/rules/{ruleName}/config` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/rules/{ruleName}/config`; `/api/v1/risk/rules/{ruleName}/config` | PUT | `async (string ruleName, RiskRuleConfigUpdateRequest request, HttpContext context) => …` | `Produces<RiskRuleConfigDto>(200)`; `Produces(400)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json`; `Results.BadRequest` | — | [L172](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L172) |
| `/escalations` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/escalations`; `/api/v1/risk/escalations` | GET | `async (HttpContext context) => …` | `Produces<IReadOnlyList<RiskEscalationDto>>(200)`; `Produces(403)`; `Produces(503)`; `Results.Problem`; `Results.Json` | — | [L209](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L209) |
| `/escalations/{escalationId}/approve` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/escalations/{escalationId}/approve`; `/api/v1/risk/escalations/{escalationId}/approve` | POST | `async (string escalationId, RiskEscalationResolutionRequest? request, HttpContext context) => …` | `Produces<RiskEscalationApprovalResponse>(200)`; `Produces(403)`; `Produces(404)`; `Produces(409)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json`; `Results.Conflict` | — | [L264](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L264) |
| `/escalations/{escalationId}/deny` (relative declaration on `group`)<br>Caller-derived mounts: `/api/risk/escalations/{escalationId}/deny`; `/api/v1/risk/escalations/{escalationId}/deny` | POST | `async (string escalationId, RiskEscalationResolutionRequest? request, HttpContext context) => …` | `Produces<RiskEscalationDto>(200)`; `Produces(403)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | — | [L408](../../src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs#L408) |

### SecureReportingDistributionEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/fund-structure/reporting/distribution/deliveries` | POST | `async (SecureReportingDeliveryQueueCommand command, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L109](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L109) |
| `/api/fund-structure/reporting/distribution/deliveries/delivery_{jobToken}` | GET | `async (string jobToken, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L123](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L123) |
| `/api/fund-structure/reporting/distribution/packages/{runId}/deliveries` | GET | `async (string runId, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L137](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L137) |
| `/api/fund-structure/reporting/distribution/transports` | GET | `([FromServices]ReportingSecureDistributionApplicationService service, HttpContext context) => …` | `Results.Ok` | — | [L151](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L151) |
| `/api/fund-structure/reporting/distribution/access-grants` | POST | `async (SecureReportingGrantIssueCommand command, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L157](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L157) |
| `/api/fund-structure/reporting/distribution/access-grants/{grantId}` | GET | `async (string grantId, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L180](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L180) |
| `/api/fund-structure/reporting/distribution/packages/{runId}/access-grants` | GET | `async (string runId, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok` | — | [L194](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L194) |
| `/api/fund-structure/reporting/distribution/access-grants/{grantId}/revoke` | POST | `async (string grantId, SecureReportingGrantRevocationRequest request, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Ok`; `Results.NotFound` | — | [L208](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L208) |
| `/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}` | GET | `async (string runId, string artifactId, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.File` | — | [L225](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L225) |
| `/portal/reporting/secure/packages/{runId}` | GET | `async (string runId, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Redirect` | — | [L245](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L245) |
| `/hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts` | POST | `async (string transportId, string jobId, SecureReportingDeliveryReceiptCommand command, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.Accepted` | — | [L264](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L264) |
| `/portal/reporting/access-grants/{grantId}/exchange` | GET | `(string grantId, HttpContext context) => …` | — | — | [L287](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L287) |
| `/portal/reporting/access-grants/{grantId}/exchange` | POST | `async (string grantId, SecureReportingGrantExchangeCommand command, [FromServices]ReportingSecureDistributionApplicationService service, HttpContext context, CancellationToken ct) => …` | `Results.File` | — | [L294](../../src/Meridian.Ui.Shared/Endpoints/SecureReportingDistributionEndpoints.cs#L294) |

### StatusEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/health` | GET | `() => …` | `Produces<HealthCheckResponse>(200)`; `Produces(503)`; `Results.Json` | `WithName("GetHealthApi")`; `WithDescription("Alias for /health endpoint for backward compatibility.")` | [L48](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L48) |
| `/healthz` | GET | `() => …` | `Produces(200)`; `Results.Ok` | `WithName("GetHealthz")`; `WithDescription("Kubernetes-style liveness probe returning 200 if the process is running.")` | [L61](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L61) |
| `/readyz` | GET | `(HttpContext context, CancellationToken ct) => …` | `Produces(200)`; `Produces(503)` | `WithName("GetReadyz")` | [L76](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L76) |
| `/startupz` | GET | `(CancellationToken ct) => …` | `Produces<RuntimeLifecycleSnapshotDto>(200)`; `Produces<RuntimeLifecycleSnapshotDto>(202)`; `Produces<RuntimeLifecycleSnapshotDto>(503)` | `WithName("GetStartupz")`; `WithDescription("Sanitized pre-login startup progress for the local workstation.")` | [L88](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L88) |
| `/startup` | GET | `() => …` | `Produces(StatusCodes.Status200OK, contentType : "text/html")`; `Results.Content` | `WithName("GetStartupCenter")`; `WithDescription("Pre-login lifecycle progress and readiness checks.")` | [L97](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L97) |
| `/livez` | GET | `() => …` | `Produces(200)`; `Results.Ok` | `WithName("GetLivez")` | [L110](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L110) |
| `/api/health/detailed` | GET | `async () => …` | `Produces(200)`; `Produces(503)`; `Results.Json` | `WithName("GetDetailedHealthApi")`; `WithDescription("Alias for /health/detailed endpoint for backward compatibility.")` | [L223](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L223) |
| `/api/events/stream` | GET | `async (HttpContext ctx, CancellationToken ct) => …` | — | — | [L251](../../src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs#L251) |

### StrategyLifecycleEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/strategies/status` | GET | `(HttpContext context) => …` | `Produces<StrategyStatusDto[]>(200)`; `Produces(503)`; `Results.Problem`; `Results.Json` | `WithName("GetAllStrategyStatuses")` | [L30](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L30) |
| `/api/strategies/{strategyId}/status` | GET | `(string strategyId, HttpContext context) => …` | `Produces<StrategyStatusDto>(200)`; `Produces(404)`; `Produces(503)`; `Results.Problem`; `Results.NotFound`; `Results.Json` | `WithName("GetStrategyStatus")` | [L44](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L44) |
| `/api/strategies/{strategyId}/resume` | POST | `async (string strategyId, HttpContext context) => …` | `Produces<VerifiedOperationOutcome>(200)`; `Produces<VerifiedOperationOutcome>(409)`; `Produces<VerifiedOperationOutcome>(423)`; `Produces<VerifiedOperationOutcome>(404)`; `Produces<VerifiedOperationOutcome>(500)`; `Produces<VerifiedOperationOutcome>(503)` | `WithName("ResumeStrategy")` | [L63](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L63) |
| `/api/strategies/{strategyId}/pause` | POST | `async (string strategyId, HttpContext context) => …` | `Produces<VerifiedOperationOutcome>(200)`; `Produces<VerifiedOperationOutcome>(409)`; `Produces<VerifiedOperationOutcome>(423)`; `Produces<VerifiedOperationOutcome>(404)`; `Produces<VerifiedOperationOutcome>(500)`; `Produces<VerifiedOperationOutcome>(503)`; `Produces(404)`; `Produces(503)` | `WithName("PauseStrategy")` | [L161](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L161) |
| `/api/strategies/{strategyId}/stop` | POST | `async (string strategyId, HttpContext context) => …` | `Produces<VerifiedOperationOutcome>(200)`; `Produces<VerifiedOperationOutcome>(409)`; `Produces<VerifiedOperationOutcome>(423)`; `Produces<VerifiedOperationOutcome>(404)`; `Produces<VerifiedOperationOutcome>(500)`; `Produces<VerifiedOperationOutcome>(503)`; `Produces(404)`; `Produces(503)` | `WithName("StopStrategy")` | [L240](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L240) |
| `/api/strategies/{strategyId}/reconcile` | POST | `async (string strategyId, HttpContext context) => …` | `Produces<VerifiedOperationOutcome>(200)`; `Produces<VerifiedOperationOutcome>(409)`; `Produces<VerifiedOperationOutcome>(423)`; `Produces<VerifiedOperationOutcome>(404)`; `Produces<VerifiedOperationOutcome>(500)`; `Produces<VerifiedOperationOutcome>(503)`; `Produces(404)`; `Produces(503)` | `WithName("ReconcileStrategyLifecycle")` | [L319](../../src/Meridian.Ui.Shared/Endpoints/StrategyLifecycleEndpoints.cs#L319) |

### WorkstationEndpoints.CloseChecklistHistory

Source: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseChecklistHistory.cs](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseChecklistHistory.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/operations/continuity/{workflowId}/checklist/history` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/operations/continuity/{workflowId}/checklist/history` | GET | `async (Guid workflowId, HttpContext context) => …` | `Results.Json`; `Results.Problem` | `WithName("GetOperationsCloseChecklistHistory")` | [L14](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseChecklistHistory.cs#L14) |

### WorkstationEndpoints

Source: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/workstation` | GET | `(IWebHostEnvironment environment) => …` | — | — | [L1204](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs#L1204) |
| `/workstation/{*path}` | GET | `(string? path, IWebHostEnvironment environment) => …` | `Results.NotFound`; `Results.File` | — | [L1207](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs#L1207) |

### WorkstationEndpoints.OperatorTriage

Source: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorTriage.cs](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorTriage.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/operator/triage` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/operator/triage` | GET | `async (Guid? fundAccountId, HttpContext context) => …` | `Results.Json` | `WithName("GetOperatorInboxTriage")` | [L21](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorTriage.cs#L21) |
| `/operator/triage/{workItemId}/actions` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/operator/triage/{workItemId}/actions` | POST | `async (string workItemId, OperatorTriageCommand request, HttpContext context) => …` | `Results.Json` | `WithName("ApplyOperatorInboxTriage")` | [L53](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorTriage.cs#L53) |
| `/operator/triage/bulk` (relative declaration on `group`)<br>Caller-derived mounts: `/api/workstation/operator/triage/bulk` | POST | `async (OperatorTriageBulkCommand request, HttpContext context) => …` | `Results.BadRequest`; `Results.Json` | `WithName("BulkAssignOperatorInboxTriage")` | [L69](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperatorTriage.cs#L69) |

### UiServer

Source: [src/Meridian/UiServer.cs](../../src/Meridian/UiServer.cs)

| Path declaration | Method | Handler / decoded body types | Response declarations / factories | Endpoint metadata | Source |
| --- | --- | --- | --- | --- | --- |
| `/api/system/lifecycle` | GET | `(HttpContext context) => …` | `Results.Ok` | — | [L672](../../src/Meridian/UiServer.cs#L672) |
| `/api/system/shutdown` | POST | `async (HttpContext context) => …`<br>Body reads: `DeserializeAsync` | `Results.Json` | — | [L706](../../src/Meridian/UiServer.cs#L706) |
| `/api/system/shutdown/{operationId}` | GET | `(string operationId, HttpContext context) => …` | `Results.Ok` | — | [L760](../../src/Meridian/UiServer.cs#L760) |
| `/api/system/shutdown/receipts/latest` | GET | `async (HttpContext context) => …` | `Results.Ok` | — | [L781](../../src/Meridian/UiServer.cs#L781) |

<!-- END GENERATED HTTP DECLARATION REFERENCE -->
