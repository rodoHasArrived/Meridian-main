# Security Master Guide

**Last Updated:** 2026-05-29
**Owner:** Core Team
**Scope:** Engineering / Operations / Product
**Review Cadence:** When asset class coverage or API changes

---

## Overview

Security Master is the event-sourced golden record for all financial instruments (securities) in the Meridian platform. It provides a centralized, version-controlled, audit-trailed definition of securities across the supported public, cash, derivative, and private/security fallback asset classes, supporting trading execution, backtesting, ledger workflows, reconciliation, and report-pack evidence.

**Key capabilities:**

- **Event-sourced storage** — Every change (creation, amendment, deactivation) is recorded with full audit trail
- **Multi-identifier support** — Resolve securities by ISIN, CUSIP, Ticker, FIGI, SEDOL, OCC option symbol, LEI, RIC, Bloomberg ID, and custom provider aliases
- **Asset class polymorphism** — 14 distinct asset classes with class-specific economic terms (coupon, strike, multiplier, etc.)
- **Version-based concurrency** — Optimistic locking prevents concurrent amendment conflicts
- **Corporate actions** — Immutable record of dividends, splits, mergers, and other adjustments
- **Trading parameters** — Lot size, tick size, and trading status for order routing and fill models
- **Structured validation** — Read-only validation reports surface severity, issue code, affected fields, suggested action, and evidence links before downstream workflows rely on a record
- **Trust snapshot projections** — Workstation trust snapshots now bundle validation posture, identifier/provider-mapping coverage, and schema-compatibility context beside the selected security
- **Full-text search** — Query by display name, issuer, or identifier with filtering by asset class and status
- **Custom asset profile governance** — Approved starter profiles, governed draft/approval/rollback lineage, and profile-backed securities can be read through canonical Security Master API surfaces

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

---

## Asset Class Coverage

Security Master currently supports these asset classes:

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
| **Commodity** | Commodity spot or reference instruments | Commodity type, denomination, contract size |
| **CryptoCurrency** | Crypto currency pairs | Base currency, quote currency, network |
| **Cfd** | Contracts for difference | Underlying asset class, leverage |
| **Warrant** | Listed or private warrants | Underlying ID, warrant type, strike, expiry |
| **CashSweep** | Cash sweep programs | Program name, sweep vehicle, sweep frequency, target account type |
| **OtherSecurity** | Fallback for unmapped instruments | Category, sub-type, maturity, issuer |
| **CustomAsset** | Profile-backed alternative/private assets | Approved custom profile ID, pinned profile version, typed profile fields, profile approval metadata |

All asset classes also have **common terms:** Display name, Currency (ISO 4217 code), Country of risk, Issuer name, Exchange, Lot size, Tick size.

Shared asset metadata is now centralized in `SecurityAssetClassCatalog`, which keeps workstation capability hints, preferred identifier kinds, and basic create-workflow support aligned across projections and UI workflows. Schema literals are likewise centralized in `SecurityMasterSchemaVersions` so legacy asset-specific payloads and newer economic-definition payloads do not drift across adapters, projections, and trust-snapshot consumers.
Profile-backed custom assets use `SecurityMasterSchemaVersions.CustomAssetProfileTerms` and can be stored either as `CustomAsset` records or as `OtherSecurity` records with `customProfileId` and `profileVersion` populated.

---

## API Endpoints

### Create Security
```
POST /api/security-master/create
```
Returns 201 Created with security detail including UUID and version 1.
Profile-backed `CustomAsset` records and profile-backed `OtherSecurity` records must include
`schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms`, `customProfileId`,
`profileVersion`, `profileFields`, and `profileApproval`. The create path preserves those terms in
the Security Master projection, snapshot, and event payload while using the existing generic
security backing model for compiled-domain compatibility.

### Retrieve by ID
```
GET /api/security-master/{securityId}
```
Returns full economic definition. Returns 404 if not found.

### Validate Security
```
GET /api/security-master/{securityId}/validation
```
Returns a `SecurityValidationReportDto` without mutating the record. The report includes:

- `severity`: `Info`, `Warning`, `Error`, or `Critical`
- `code`: stable issue code such as `SM_DUPLICATE_CANONICAL_IDENTIFIER`
- `title` and `message`: operator-readable issue description
- `affectedFields`: fields such as `identifiers.Isin` or `assetSpecificTerms.strike`
- `suggestedAction`: the next remediation step
- `evidenceLinks`: related Security Master records or evidence packet targets when available

Validation currently checks common record shape, legacy asset-specific schema-version compatibility, custom asset profile-version pinning, profile approval metadata, typed profile fields, profile identifier coverage, effective-date windows, identifier presence and primary-identifier rules, projection-vs-identifier primary consistency, duplicate canonical identifiers, provider-symbol conflicts, provenance freshness, pricing-source metadata, accounting-classification metadata, and registry-backed asset-class term rules.

### List Custom Asset Profiles
```
GET /api/security-master/asset-profiles
```
Returns approved custom asset profile definitions used by profile-backed create and amend workflows.
The catalog includes seeded starter templates plus approved versions from the governed profile
store. Each definition includes profile id, approved version, typed field schema, required flags,
enum/range metadata, identifier preferences, lifecycle states, accounting-impact hints, and
approval metadata.

### Govern Custom Asset Profiles
```
GET  /api/security-master/asset-profiles/{profileId}/lineage
POST /api/security-master/asset-profiles/drafts
POST /api/security-master/asset-profiles/approve
POST /api/security-master/asset-profiles/rollback
```
Profile governance endpoints require `AdminMaintenance` plus Security Master read access. Draft
requests stage a deterministic no-code profile version, approval requests make a draft usable for
new Security Master records, and rollback requests create a new approved version copied from an
earlier approved or superseded version. Every mutation records actor, rationale, correlation id,
profile version, status, and approval reference in the lineage response. Superseded profile
versions remain available to validation so securities pinned to historical approved versions stay
interpretable after later profile changes.

The browser Settings workspace exposes this governance lane for operators: approved starter
profiles can be reviewed, copied into drafts, approved or rolled back with lineage evidence, and
used to create `CustomAsset` records pinned to the approved profile version. The UI submits the same
typed no-code field definitions and canonical Security Master create payloads documented here; it
does not run user-authored scripts or treat provider payloads as canonical terms.

### Resolve by Identifier
```
POST /api/security-master/resolve
```
Resolves by ISIN, CUSIP, Ticker, FIGI, SEDOL, LEI, RIC, Bloomberg ID, or custom identifier.
The request can also carry `asOfUtc` so effective-dated identifiers resolve against a historical or
forward-looking point in time instead of always using the current clock.

### Search Securities
```
POST /api/security-master/search
```
Full-text search by display name, issuer, identifier, or custom profile field. Supports active-only
filtering and pagination. Profile-backed searches can provide `customProfileId`, `profileVersion`,
`profileFieldKey`, and `profileFieldValue`; a text `query` is optional when at least one custom
profile filter is present. Profile filters match only records with approved pinned
`customProfileId` / `profileVersion` metadata in the asset-specific terms.

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
For profile-backed custom assets, amendments preserve the pinned custom profile metadata and typed
field values. If an amendment supplies a new profile-backed asset-specific payload, the new payload
becomes the authoritative pinned profile terms for the resulting version.

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

### Get Workstation Trust Snapshot
```
GET /api/workstation/security-master/securities/{securityId}/trust-snapshot
```
Returns the selected-security workstation projection used by retained desktop governance workflows. In addition to trust posture, history, and downstream impact, the snapshot now includes:

- `validationReport` — read-only blocking/advisory Security Master validation issues
- `identifierSummary` — active identifiers, aliases, and provider-mapping coverage
- `schemaCompatibility` — legacy asset-specific schema version plus normalized economic-terms schema version, including explicit review messaging when payloads drift beyond supported workstation compatibility
- `changeHistory` — structured audit rows with version, actor, origin, source-system provenance, reason, and changed-field summaries derived from the Security Master event stream
- `scheduleSummary` — typed cash-flow/factor schedule posture derived from economic terms and corporate-action context
- `lotModel` — typed lot/open-position modeling guidance derived from asset class, factors, and trading parameters
- `scheduleBook` — first-class effective-dated schedule entities with event rows, factor history, and provenance history sourced from economic terms, corporate actions, and Security Master history
- `openLotReadModel` — stable open-lot rows keyed by `SecurityId`, `PortfolioId`, `AccountScopeId`, and `LotId`, including factor-adjusted face/current-face projections for structured fixed-income holdings

`changeHistory` is additive to the raw `history` event envelopes: the envelope stream remains the lossless audit source, while the structured rows give the workstation a UI-ready versioned trail for actor/source review and change summaries. `scheduleBook` is additive to `scheduleSummary`: the summary remains the quick UI posture, while the book carries auditable rows for scheduled cash flows, factor updates, lifecycle dates, and provenance. `openLotReadModel` is likewise additive to `lotModel`: the model still explains how a security should reconcile, while the read model materializes scoped lots from strategy-run portfolio snapshots without turning Security Master into the accounting ledger.

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

## Validation Integration

`SecurityValidationService` is the reusable backend seam for modules that need Security Master trust checks without invoking a create/amend/deactivate workflow. It uses `AssetClassValidatorRegistry` so asset-class-specific rules remain modular. Downstream services should consume the validation report before accepting Security Master data for strategy-run inputs, lots and positions, ledger classification, reconciliation breaks, valuation/report-pack evidence, or governed approvals.

Blocking issues are `Error` or `Critical`; `Warning` issues identify stale, incomplete, or governance-sensitive metadata that may require operator review before a workflow is promoted.

---

## Related Documentation

- [Environment Variables Reference](../reference/environment-variables.md) — Configuration reference
- [Provider Comparison](provider-comparison.md) — Data source selection guidance
- [Backfill Guide](backfill-guide.md) — Historical data collection
- [Provider Implementation Guide](../development/provider-implementation.md) — Adding new data providers
- [Architecture Overview](../architecture/overview.md) — System design and data flow
