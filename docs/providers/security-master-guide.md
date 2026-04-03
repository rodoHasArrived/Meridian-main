# Security Master Guide

**Last Updated:** 2026-04-03
**Owner:** Core Team
**Scope:** Engineering / Operations / Product
**Review Cadence:** When asset class coverage or API changes

---

## Overview

Security Master is the event-sourced golden record for all financial instruments (securities) in the Meridian platform. It provides a centralized, version-controlled, audit-trailed definition of securities across 15 asset classes, supporting trading execution, backtesting, and portfolio reconciliation.

**Key capabilities:**

- **Event-sourced storage** — Every change (creation, amendment, deactivation) is recorded with full audit trail
- **Multi-identifier support** — Resolve securities by ISIN, CUSIP, Ticker, FIGI, SEDOL, ProviderSymbol, or InternalCode
- **Asset class polymorphism** — 15 distinct asset classes with class-specific economic terms (coupon, strike, multiplier, etc.)
- **Version-based concurrency** — Optimistic locking prevents concurrent amendment conflicts
- **Corporate actions** — Immutable record of dividends, splits, mergers, and other adjustments
- **Trading parameters** — Lot size, tick size, and trading status for order routing and fill models
- **Full-text search** — Query by display name, issuer, or identifier with optional active-only filtering
- **Conflict detection** — Identifies duplicate or conflicting identifier registrations across providers
- **Bulk import** — CSV/JSON file import and direct Polygon.io ingest via CLI or HTTP API

---

## Setup

### Environment Variables

Security Master requires a PostgreSQL database. Set these environment variables:

```bash
export MERIDIAN_SECURITY_MASTER_CONNECTION_STRING="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=secret"
export MERIDIAN_SECURITY_MASTER_SCHEMA="security_master"
```

If not set, the platform uses defaults:
- `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING`: `Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=secret`
- `MERIDIAN_SECURITY_MASTER_SCHEMA`: `security_master`

### PostgreSQL Requirements

1. **Version:** PostgreSQL 12+
2. **Privileges:** User must have CREATE SCHEMA and CREATE TABLE on the target database
3. **Connection:** Ensure the host/port are accessible and TLS settings match your infrastructure

### Running Migrations

Migrations run automatically on platform startup via `SecurityMasterMigrationRunner`. Migrations create:

- `security_master.securities` — Current security state (denormalized read model)
- `security_master.security_events` — Event stream (immutable log)
- `security_master.corporate_actions` — Corporate action log
- Supporting indexes and constraints

To verify:

```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'security_master';
SELECT table_name FROM information_schema.tables WHERE table_schema = 'security_master';
```

### Configuration Reference

Once the database connection is in place, review the `SecurityMasterOptions` defaults used by the application at startup. The current implementation documents runtime overrides through environment variables for the database settings below; the remaining options use built-in defaults unless changed in code:

| Option | Default | Runtime override | Description |
|--------|---------|------------------|-------------|
| `ConnectionString` | `""` | `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` | PostgreSQL connection string (required) |
| `Schema` | `security_master` | `MERIDIAN_SECURITY_MASTER_SCHEMA` | PostgreSQL schema name |
| `SnapshotIntervalVersions` | `50` | None documented | Save an aggregate snapshot every N versions to speed up event replay |
| `ProjectionReplayBatchSize` | `500` | None documented | Number of events to replay per batch when rebuilding projections |
| `PreloadProjectionCache` | `true` | None documented | Warm the in-memory projection cache on startup |
| `ResolveInactiveByDefault` | `true` | None documented | Include inactive (deactivated) securities when resolving by identifier |

---

## Asset Class Coverage

Security Master supports 15 asset classes:

| Asset Class | Description | Key Terms |
|-------------|-------------|-----------|
| **Equity** | Common and preferred stocks | Share class, dividend yield |
| **Bond** | Fixed-income debt instruments | Maturity, coupon structure (fixed/floating), call date, seniority |
| **Option** | Derivative contracts | Underlying ID, put/call, strike, expiry, multiplier |
| **Future** | Futures contracts | Root symbol, contract month, expiry, multiplier |
| **FxSpot** | Foreign exchange spot pairs | Base currency, quote currency |
| **Deposit** | Bank deposits and money market instruments | Deposit type, maturity, interest rate, callable flag |
| **MoneyMarketFund** | Cash sweep and money market fund vehicles | Fund family, sweep eligibility, WAM, liquidity fee flag |
| **CertificateOfDeposit** | CDs and structured deposits | Issuer, maturity, coupon, callable, day count |
| **CommercialPaper** | Short-term corporate debt | Issuer, maturity, discount rate, asset-backed flag |
| **TreasuryBill** | US government short-term debt | Maturity, auction date, CUSIP, discount rate |
| **Repo** | Repurchase agreements | Counterparty, start/end dates, repo rate, collateral type |
| **Swap** | Interest rate and other swaps | Legs (fixed/floating), maturity, currency |
| **DirectLoan** | Direct lending / syndicated loans | Borrower, maturity, covenants |
| **CashSweep** | Cash sweep programs | Program name, sweep vehicle, sweep frequency, target account type |
| **OtherSecurity** | Fallback for unmapped instruments | Category, sub-type, maturity, issuer |

All asset classes also have **common terms:** Display name, Currency (ISO 4217 code), Country of risk, Issuer name, Exchange, Lot size, Tick size.

### Supported Identifier Types

The `SecurityIdentifierKind` enum defines all recognized identifier types:

| Kind | Description |
|------|-------------|
| `Ticker` | Exchange ticker symbol (e.g. `AAPL`) |
| `Isin` | ISO 6166 International Securities Identification Number |
| `Cusip` | CUSIP (9-character North American identifier) |
| `Sedol` | Stock Exchange Daily Official List identifier (London) |
| `Figi` | Financial Instrument Global Identifier (OpenFIGI) |
| `ProviderSymbol` | Provider-specific symbol (e.g. Alpaca, Polygon, IB contract ID) |
| `InternalCode` | Internal reference code assigned by the platform |

---

## API Endpoints

All endpoints share the base path `/api/security-master`. Security Master endpoints are only available when `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` is configured.

### Create Security
```
POST /api/security-master
```
Creates a new security record. Returns `201 Created` with a `SecurityDetailDto` containing the generated UUID, version 1, and full economic terms. Supported asset classes: Equity, Bond, Option, Future, FxSpot, Deposit, MoneyMarketFund, CertificateOfDeposit, CommercialPaper, TreasuryBill, Repo, CashSweep, Swap, DirectLoan, OtherSecurity.

### Retrieve by ID
```
GET /api/security-master/{securityId}
```
Returns the full economic definition. Returns `404 Not Found` if the security does not exist.

### Resolve by Identifier
```
POST /api/security-master/resolve
```
Resolves a security by an external identifier (ISIN, CUSIP, Ticker, FIGI, SEDOL, ProviderSymbol, or InternalCode). Supports optional `provider` filter and `activeOnly` flag. Returns `404 Not Found` if no match or if `activeOnly=true` and the security is inactive.

### Search Securities
```
POST /api/security-master/search
```
Full-text search by display name, issuer, or identifier. Supports the optional `activeOnly` flag and pagination via `take` and `skip`. Returns a paginated list of `SecuritySummaryDto` objects.

### Retrieve Event History
```
GET /api/security-master/{securityId}/history?take=100
```
Returns the audit trail of all changes in ascending sequence order. Supported event types: `SecurityCreated`, `TermsAmended`, `SecurityDeactivated`, `IdentifierAdded`, `CorporateActionRecorded`. The `take` query parameter limits results (default: 100). Returns `404 Not Found` if no history exists.

### Amend Terms
```
POST /api/security-master/amend
```
Updates economic terms with optimistic concurrency control. The request must include the current `ExpectedVersion`; a version mismatch causes a conflict error. Amended terms create a new event in the audit trail and increment the version by 1.

### Deactivate Security
```
POST /api/security-master/deactivate
```
Soft-deletes a security. Returns `204 No Content`. The record remains in the database for audit purposes. Deactivation is recorded as an event. Cannot be undone.

### Upsert Identifier Alias
```
POST /api/security-master/aliases/upsert
```
Adds or updates an external identifier alias (provider symbol mapping). Upserts by alias kind + provider — updates an existing alias if found, otherwise creates a new one.

### Get Trading Parameters
```
GET /api/security-master/{securityId}/trading-parameters
```
Returns lot size, tick size, contract multiplier, margin requirement, trading hours, and circuit breaker threshold for order routing and fill models. Returns `404 Not Found` if the security does not exist or has expired.

### Get Corporate Actions
```
GET /api/security-master/{securityId}/corporate-actions
```
Returns all corporate action events (Dividend, StockSplit, SpinOff, MergerAbsorption, RightsIssue, and others) sorted by ex-date. Returns an empty list if no actions are recorded.

### Record Corporate Action
```
POST /api/security-master/{securityId}/corporate-actions
```
Appends an immutable corporate action event. The `SecurityId` in the request body must match the route parameter. Returns `400 Bad Request` on mismatch. Used by backtesting price adjustment workflows.

### Get Identifier Conflicts
```
GET /api/security-master/conflicts
```
Returns all open identifier conflicts — cases where the same ISIN, CUSIP, FIGI, or Ticker is registered to more than one security across different providers. Conflicts are detected automatically during ingestion.

### Resolve a Conflict
```
POST /api/security-master/conflicts/{conflictId}/resolve
```
Marks a specific conflict as resolved. The `ConflictId` in the request body must match the route parameter. Returns `404 Not Found` if the conflict does not exist.

### Bulk Import (HTTP)
```
POST /api/security-master/import
```
Imports securities from a CSV or JSON payload over HTTP. Request body: `{ "fileContent": "...", "fileExtension": ".csv" }`. Returns an import result with `Imported`, `Skipped`, `Failed`, and `ConflictsDetected` counters, plus an `Errors` list of error strings.

### Ingest Status
```
GET /api/security-master/ingest/status
```
Returns the current count of open identifier conflicts and the retrieval timestamp. Useful for monitoring ingest health.

---

## Bulk Import

### CLI Import

The `--security-master-ingest` command bulk-imports securities from a file or directly from Polygon.io:

```bash
# Import from a CSV file
dotnet run --project src/Meridian -- --security-master-ingest ./securities.csv

# Import from a JSON file
dotnet run --project src/Meridian -- --security-master-ingest ./securities.json

# Ingest from Polygon.io (all tickers)
dotnet run --project src/Meridian -- --security-master-ingest --provider polygon

# Ingest from Polygon.io filtered by exchange and asset type
dotnet run --project src/Meridian -- --security-master-ingest --provider polygon --exchange XNAS --type CS
```

**Requirements:**
- `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` must be set
- For Polygon ingest: `POLYGON_API_KEY` must be set

**Output summary:**
```
Import complete:
  Imported  : 4821
  Skipped   : 103   (duplicates)
  Failed    : 2
  Conflicts : 7
```

### CSV Format

CSV files must include a header row. Required columns: `AssetClass`, `Ticker`, `DisplayName`, `Currency`. Optional columns follow the asset-class-specific term schema.

### JSON Format

JSON files contain an array of `CreateSecurityRequest` objects:

```json
[
  {
    "securityId": "00000000-0000-0000-0000-000000000000",
    "assetClass": "Equity",
    "commonTerms": { "displayName": "Apple Inc.", "currency": "USD", "exchange": "XNAS" },
    "assetSpecificTerms": { "shareClass": "Common" },
    "identifiers": [
      { "kind": "Ticker", "value": "AAPL", "isPrimary": true, "validFrom": "2020-01-01T00:00:00Z" },
      { "kind": "Isin",   "value": "US0378331005", "isPrimary": false, "validFrom": "2020-01-01T00:00:00Z" }
    ],
    "effectiveFrom": "2020-01-01T00:00:00Z",
    "sourceSystem": "import",
    "updatedBy": "operator"
  }
]
```

> **Note:** Set `securityId` to the nil UUID (`00000000-0000-0000-0000-000000000000`) when generating a new record from a file; the platform will assign the authoritative UUID on first `CreateAsync` call. To import with a known stable UUID (e.g. for idempotent re-runs), provide the same UUID each time — duplicate creates are silently skipped.

### Polygon Integration

The `--provider polygon` path fetches tickers from the Polygon `/v3/reference/tickers` API using cursor-based pagination (250 results per page). The free tier is rate-limited to 5 requests/minute; the importer automatically waits between pages.

Polygon asset type codes mapped to Security Master asset classes:

| Polygon Type | Asset Class |
|-------------|-------------|
| `CS`, `OS` | Equity |
| `ETF`, `ETV`, `ETN` | Equity (fund sub-type) |
| Other | OtherSecurity |

---

## Conflict Detection

Security Master automatically detects identifier conflicts during ingestion — situations where the same identifier on a projection is mapped to more than one security by different providers.

**Workflow:**
1. Conflict detection is recorded via `ISecurityMasterConflictService.RecordConflictsForProjectionAsync` after `CreateAsync` and `AmendTermsAsync`.
2. Conflicts are listed at `GET /api/security-master/conflicts`.
3. Operators review and resolve conflicts via `POST /api/security-master/conflicts/{conflictId}/resolve`.

**Resolution strategies:** Mark one record as authoritative, expire the conflicting alias, or merge the records manually via `AmendTermsAsync`.

---

## WPF Desktop UI

The WPF desktop application includes **SecurityMasterPage** with:

- Full-text search by display name, issuer, or identifier
- Asset class and status filtering
- Full economic definition detail view
- Event history timeline with amendment tracking
- Identifier aliases table
- Corporate actions log
- Amendment form with optimistic concurrency control
- Create wizard for new securities

---

## Backtest Integration

Security Master integrates with backtesting for accurate pricing:

```csharp
var backtest = new BacktestRequest
{
    Strategy = strategy,
    Symbol = "AAPL",
    AdjustForCorporateActions = true  // Enable adjustments
};
```

When enabled:
- Historical bar closes adjusted backward for splits and dividends
- Position sizes adjusted forward on split ex-dates
- Corporate action events applied in sequence by ex-date

---

## Related Documentation

- [Environment Variables Reference](../reference/environment-variables.md) — Configuration reference
- [Provider Comparison](provider-comparison.md) — Data source selection guidance
- [Backfill Guide](backfill-guide.md) — Historical data collection
- [Provider Implementation Guide](../development/provider-implementation.md) — Adding new data providers
- [Architecture Overview](../architecture/overview.md) — System design and data flow
