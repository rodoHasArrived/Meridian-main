# Security Master Guide

**Last Updated:** 2026-03-21
**Owner:** Core Team
**Scope:** Engineering / Operations / Product
**Review Cadence:** When asset class coverage or API changes

---

## Overview

Security Master is the event-sourced golden record for all financial instruments (securities) in the Meridian platform. It provides a centralized, version-controlled, audit-trailed definition of securities across 19 asset classes, supporting trading execution, backtesting, and portfolio reconciliation.

**Key capabilities:**

- **Event-sourced storage** — Every change (creation, amendment, deactivation) is recorded with full audit trail
- **Multi-identifier support** — Resolve securities by ISIN, CUSIP, Ticker, FIGI, SEDOL, ProviderSymbol, or InternalCode
- **Asset class polymorphism** — 19 distinct asset classes with class-specific economic terms (coupon, strike, multiplier, etc.)
- **Version-based concurrency** — Optimistic locking prevents concurrent amendment conflicts
- **Corporate actions** — Immutable record of dividends, splits, mergers, and other adjustments
- **Trading parameters** — Lot size, tick size, and trading status for order routing and fill models
- **Full-text search** — Query by display name, issuer, or identifier with filtering by asset class and status

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

### Projection Cache vs. Snapshots (duplication audit)

- **Today:** An in-memory `SecurityMasterProjectionCache` is warmed from snapshot+event rebuilds, while durable snapshots live in `security_snapshots`. Warmup also persists the rebuilt projection batch back into `security_master_cache`, so we hold the same projection state in three places (cache, snapshot store, projection table).
- **Risk:** Divergence between the cache and persisted projections when only one path is updated (e.g., cache warmed but batch persist fails, or snapshots saved without cache refresh) leads to stale query results and inconsistent conflict detection.
- **Consolidation plan:**  
  1. Make the projection table the single source of truth for warm starts: load projections from `PersistProjectionBatchAsync` first, falling back to snapshot+event rebuild only when the batch is missing or stale.  
  2. Treat `SecurityMasterProjectionCache` as a read-through layer that is always populated from the projection table; writes flow store → cache via a single hook.  
  3. Emit a single checkpoint after both the projection batch and cache are updated to avoid split-brain states; surface metrics when batch write or cache hydrate diverge.  
  4. Longer term: collapse snapshot writes into the same transaction as projection persistence so rebuilds use one persisted shape.

To verify:

```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'security_master';
SELECT table_name FROM information_schema.tables WHERE table_schema = 'security_master';
```

---

## Asset Class Coverage

Security Master supports 19 asset classes:

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
| **Commodity** | Physical and financial commodity instruments | Commodity type, denomination, contract size |
| **CryptoCurrency** | Cryptocurrency spot pairs | Base currency, quote currency, network |
| **Cfd** | Contracts for difference | Underlying asset class, description, leverage |
| **Warrant** | Equity and debt warrants | Underlying ID, warrant type, strike, expiry, multiplier |
| **OtherSecurity** | Fallback for unmapped instruments | Category, sub-type, maturity, issuer |

All asset classes also have **common terms:** Display name, Currency (ISO 4217 code), Country of risk, Issuer name, Exchange, Lot size, Tick size.

---

## API Endpoints

### Create Security
```
POST /api/security-master/create
```
Returns 201 Created with security detail including UUID and version 1.

### Retrieve by ID
```
GET /api/security-master/{securityId}
```
Returns full economic definition. Returns 404 if not found.

### Resolve by Identifier
```
POST /api/security-master/resolve
```
Resolves by ISIN, CUSIP, Ticker, FIGI, SEDOL, LEI, RIC, Bloomberg ID, or custom identifier.

### Search Securities
```
POST /api/security-master/search
```
Full-text search by display name, issuer, or identifier. Supports filtering by asset class, status, and pagination.

### Retrieve Event History
```
GET /api/security-master/{securityId}/history?take=100
```
Returns audit trail of all changes (SecurityCreated, TermsAmended, SecurityDeactivated, IdentifierAdded, CorporateActionRecorded).

### Amend Terms
```
POST /api/security-master/amend
```
Updates economic terms with optimistic concurrency control. Returns 409 Conflict if version mismatch.

### Deactivate Security
```
POST /api/security-master/deactivate
```
Soft delete. Returns 204 No Content. Irreversible.

### Upsert Identifier Alias
```
POST /api/security-master/aliases/upsert
```
Adds or updates an external identifier (provider symbol mapping).

### Get Trading Parameters
```
GET /api/security-master/{securityId}/trading-parameters
```
Returns lot size, tick size, and trading status for order routing and fill models.

### Get Corporate Actions
```
GET /api/security-master/{securityId}/corporate-actions
```
Returns dividend, split, merger, spinoff, and rights issue events in ex-date order.

### Record Corporate Action
```
POST /api/security-master/{securityId}/corporate-actions
```
Appends immutable corporate action event. Used by backtesting price adjustment workflows.

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
