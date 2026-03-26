# Security Master Productization Roadmap

**Last Updated:** 2026-03-26  
**Status:** In Progress — Wave 6 delivery  
**Owner:** Platform team  
**Audience:** Architecture, API, UI, and data contributors

---

## Naming Standard

All new F# types and C# DTOs introduced by this roadmap must follow the
[Meridian Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).

**Quick reference for types proposed in this roadmap:**

| New concept | Required name form |
|---|---|
| Corporate Action identifier | `CorpActId = CorpActId of Guid` |
| Corporate Action domain event DU | `CorpActEvent` (not `CorporateActionEvent`) |
| Bond term-sheet record | `BondDef` (not `BondTerms` for top-level; sub-records like `CouponTerms` remain as-is) |
| Status union for corp actions | `CorpActStat = Announced \| Confirmed \| Settled \| Cancelled` |
| Security–issuer join record | `SecIssLnk` |
| Dividend trait record | `IncTr = { IsIncomeProducing: bool; DivRate: decimal option; PayFreq: string option }` |
| Convertible trait record | `ConvTr = { IsConvertible: bool; ConvRatio: decimal option; ConvPx: decimal option }` |
| Callable flag on bond | `IsCallable: bool` (not `Callable: bool`) |
| Maturity date field | `MaturityDt: DateOnly option` (new F# code uses `Dt` suffix) |

---

## Summary

Meridian's Security Master has contracts, Postgres-backed services, F# domain modules, and REST endpoints. This roadmap captures six prioritized ideas that together move Security Master from a backend capability to a first-class platform layer.

**Wave 6 delivery state (as of 2026-03-26):**

| # | Idea | Status |
|---|------|--------|
| 1 | Corporate Action Events | 🔲 Planned |
| 2 | Bond Term Richness | ✅ Delivered |
| 3 | Trading Parameters | 🔶 Partial |
| 4 | Exchange Bulk Ingest | 🔲 Planned |
| 5 | Golden Record Conflict Resolution | 🔲 Planned |
| 6 | WPF Security Master Browser | ✅ Delivered |

---

## Delivered Capabilities (not in original scope)

The following capabilities were implemented as foundational work for this wave and are now shipped:

- **`SecurityMasterProjectionWarmupService`** (`IHostedService`) — warms the in-memory projection cache on startup when `PreloadProjectionCache` is enabled, eliminating cold-start latency. (`src/Meridian.Application/SecurityMaster/SecurityMasterProjectionWarmupService.cs`)
- **`SecurityMasterOptionsValidator`** (`IValidateOptions<SecurityMasterOptions>`) — validates connection string, schema, `SnapshotIntervalVersions`, and `ProjectionReplayBatchSize` at startup. (`src/Meridian.Application/SecurityMaster/SecurityMasterOptionsValidator.cs`)
- **`SecurityMasterRebuildOrchestrator`** — differential event-replay that picks up only new events since the last checkpoint, avoiding full re-projection on every restart. (`src/Meridian.Application/SecurityMaster/SecurityMasterRebuildOrchestrator.cs`)
- **Full-text search migration** — Postgres `tsvector` column backed by a `gin` index and an `AFTER INSERT OR UPDATE` trigger, fed from `display_name`, `primary_identifier_value`, `asset_class`, `issuer_name`, `exchange_code`, and `currency`. (`src/Meridian.Storage/SecurityMaster/Migrations/002_security_master_fts.sql`)
- **`ISecurityMasterQueryService.GetEconomicDefinitionByIdAsync`** — full aggregate rebuild on demand for governance drill-in surfaces.
- **`SecurityMasterSecurityReferenceLookup`** (`ISecurityReferenceLookup`) — adapts the Security Master query service to portfolio/ledger workstation enrichment; resolves by `Ticker` identifier and derives sub-type from asset class. (`src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs`)
- **Workstation DTOs** — `SecurityMasterWorkstationDto`, `SecurityClassificationSummaryDto`, `SecurityEconomicDefinitionSummaryDto`, `SecurityIdentityDrillInDto` in `Meridian.Contracts.Workstation`. (`src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs`)
- **Security enrichment tests** — `SecurityEnrichmentTests` (portfolio/ledger resolution paths) and `SecurityMasterReferenceLookupTests` (unresolved identity, degraded metadata, sub-type derivation). (`tests/Meridian.Tests/SecurityMaster/`)

---

## Idea 1 — Corporate Action Events

**Status: 🔲 Planned**

### Problem

Corporate actions (dividends, splits, mergers, spin-offs, rights issues) are not yet modeled as domain events in Security Master. Backtesting and portfolio tracking silently ignore them, which produces incorrect historical P&L and position history.

### Current State

`SecurityMasterEvents.fs` defines three event cases only: `SecurityCreated`, `TermsAmended`, `SecurityDeactivated`. No corporate action event types exist. `ISecurityMasterEventStore` stores and replays these events but has no corporate action surface.

### Proposed Work

- Add `CorpActEvent` discriminated union to `SecurityMasterEvents.fs`: `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption`, `RightsIssue`. Each case carries `CorpActId of Guid`, `securityId`, `exDate: DateOnly`, `payDate: DateOnly option`, and event-specific fields.
- Extend `ISecurityMasterEventStore` and `PostgresSecurityMasterEventStore` to persist and replay `CorpActEvent` records (separate event stream keyed by `security_id`).
- Add `CorporateActionAdjustmentService` that applies split/dividend adjustments to `HistoricalBar` data on read.
- Expose `GET /api/security-master/{id}/corporate-actions` endpoint returning a time-ordered list of `CorpActEvent` envelopes.
- Backtest engine: integrate adjustment service so fill prices are split-adjusted automatically.

### Acceptance Criteria

- `SecurityMasterAggregateRebuilder` correctly folds `CorpActEvent` records into the aggregate.
- `BacktestEngine` replay produces adjusted bar prices when corporate actions are present.
- At least one provider (Polygon or Alpha Vantage) feeds corporate action data into the event store.

---

## Idea 2 — Bond Term Richness

**Status: ✅ Delivered**

### What Was Delivered

`SecurityTermModules.fs` now models fixed-income terms as a rich, composable module record rather than a single monolithic discriminated union case. The following term modules are fully implemented:

| Module | F# Type | Key Fields |
|--------|---------|-----------|
| Maturity | `MaturityTerms` | `EffectiveDate`, `IssueDate`, `MaturityDate` (all `DateOnly option`) |
| Coupon | `CouponTerms` | `CouponType`, `CouponRate`, `PaymentFrequency`, `DayCount` |
| Discount | `DiscountTerms` | `DiscountRate`, `YieldRate` |
| Floating rate | `FloatingRateTerms` | `ReferenceIndex`, `SpreadBps`, `ResetFrequency`, `FloorRate`, `CapRate` |
| Accrual | `AccrualTerms` | `AccrualMethod`, `AccrualStartDate`, `ExDividendDays`, `BusinessDayConvention`, `HolidayCalendar` |
| Payment | `PaymentTerms` | `PaymentFrequency`, `PaymentLagDays`, `PaymentCurrency` |
| Redemption | `RedemptionTerms` | `RedemptionType`, `RedemptionPrice`, `IsBullet`, `IsAmortizing` |
| Call | `EconomicCallTerms` | `IsCallable`, `FirstCallDate`, `CallPrice` |
| Issuer | `IssuerTerms` | `IssuerName`, `InstitutionName`, `IssuerProgram` |
| Equity behavior | `EquityBehaviorTerms` | `ShareClass`, `VotingRights`, `DistributionType` |
| Fund | `FundTerms` | `FundFamily`, `WeightedAverageMaturityDays`, `SweepEligible`, `LiquidityFeeEligible` |
| Sweep | `SweepTerms` | `ProgramName`, `SweepVehicleType`, `SweepFrequency`, `TargetAccountType` |
| Financing | `FinancingTerms` | `Counterparty`, `CollateralType`, `Haircut`, `OpenDate`, `CloseDate` |
| Auction | `AuctionTerms` | `AuctionDate`, `AuctionType` |

All modules are optional fields on `SecurityTermModules`, so equities continue to carry only `EquityBehavior` and bonds carry `Maturity`, `Coupon`, `Redemption`, `Call`, `Issuer`, etc. The Postgres `securities` table persists the term modules through `asset_specific_terms jsonb`. The `SecurityMasterDbMapper` handles hydration. Existing equity tests pass unchanged.

---

## Idea 3 — Trading Parameters

**Status: 🔶 Partial**

### What Was Delivered

- `lot_size numeric` and `tick_size numeric` columns are present in `securities` (migration 001).
- `exchange_code text` and `country_of_risk text` are persisted at the projection level.

### Remaining Work

- `SecurityTermModules` does not yet have a `TradingParameters` module (`LotSize`, `TickSize`, `ContractMultiplier`, `MarginRequirementPercent`, `TradingHoursUtc`, `CircuitBreakerThresholdPercent`).
- `ISecurityMasterQueryService` does not expose `GetTradingParametersAsync(symbolId, asOf)`.
- `PaperTradingGateway` does not consult Security Master for lot-size or tick-size validation.
- `BacktestEngine` fill models do not round prices to instrument tick size.
- No `/api/security-master/{id}/trading-parameters` endpoint.

### Proposed Work

- Add `TradingParams` module record to `SecurityTermModules.fs`: `LotSize: decimal option`, `TickSize: decimal option`, `ContractMultiplier: decimal option`, `MarginRequirementPct: decimal option`, `TradingHoursUtc: string option`, `CircuitBreakerThresholdPct: decimal option`.
- Add `GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct)` to `ISecurityMasterQueryService` and implement via `SecurityMasterQueryService`.
- Wire into `PaperTradingGateway`: inject `ISecurityMasterQueryService`; reject sub-lot orders and round prices to tick size before validation.
- Wire into `BacktestEngine` fill models (`OrderBookFillModel`, `BarMidpointFillModel`): snap fill price to tick grid when `TickSize > 0`.
- Add `GET /api/security-master/{id}/trading-parameters` endpoint.

### Acceptance Criteria

- `PaperTradingGateway` rejects a sub-lot order for an instrument with `LotSize > 1`.
- `BacktestEngine` rounds fill prices to the instrument's tick size.
- Parameters are time-travel-queryable (returns parameters as-of a given date).

---

## Idea 4 — Exchange Bulk Ingest

**Status: 🔲 Planned**

### Problem

Populating Security Master for broad equity coverage (thousands of instruments) currently requires per-symbol API calls. There is no bulk-ingest path from exchange listing files or provider bulk endpoints.

### Proposed Work

- Add `ISecurityMasterBulkIngestService` with `IngestFromCsvAsync(Stream, BulkIngestOptions)` and `IngestFromProviderAsync(string providerId, BulkIngestOptions)`.
- Implement CSV parser for common exchange listing formats (NASDAQ, NYSE, LSE basic export).
- Implement `PolygonSecurityMasterIngestProvider` that pages through `/v3/reference/tickers` and maps to `CreateSecurityRequest`.
- Add CLI command `--security-master-ingest --provider polygon --exchange XNAS` wired through `CommandDispatcher`.
- Emit progress events through `EventPipeline` so the web dashboard can poll ingest status.

### Acceptance Criteria

- A NASDAQ CSV listing of 500 symbols ingests in under 60 seconds on a local Postgres instance.
- Duplicate symbols are detected and skipped (idempotent).
- Ingest status is visible via `GET /api/security-master/ingest/status`.

---

## Idea 5 — Golden Record Conflict Resolution

**Status: 🔲 Planned**

### Problem

When multiple providers contribute data for the same instrument (e.g., Polygon and Alpaca both return metadata for `AAPL`), there is no reconciliation or confidence-scoring mechanism. The last writer wins, and data quality degrades silently.

### Proposed Work

- Add `GoldenRecordResolver` service that receives `SecurityMasterConflict` (same instrument, differing field values from different providers) and applies a configurable resolution strategy: `PreferProvider`, `MostRecent`, `HighestConfidence`, `ManualReview`.
- Add `SecurityMasterConflict` domain type to `SecurityMasterEvents.fs` and persist via the event store.
- Extend `SecurityMasterProjectionService` to detect and emit conflicts rather than silently overwrite.
- Add `GET /api/security-master/conflicts` endpoint returning open conflicts with field-level diff.
- Add `POST /api/security-master/conflicts/{id}/resolve` endpoint accepting resolution strategy.
- Surface conflict count as a badge in the web dashboard Security Master panel.

### Acceptance Criteria

- Two providers with differing `Name` for the same FIGI produce a stored `SecurityMasterConflict`.
- Resolving via `PreferProvider` updates the golden record projection and closes the conflict.
- Unresolved conflict count is visible in the dashboard.

---

## Idea 6 — WPF Security Master Browser

**Status: ✅ Delivered**

### What Was Delivered

- **`SecurityMasterViewModel`** (`BindableBase`) — `SearchQuery`, `ActiveOnly`, `IsLoading`, `StatusText`, `SelectedSecurity` bindable properties; `Results` (`ObservableCollection<SecurityMasterWorkstationDto>`); `SearchCommand`, `ClearCommand`, `OpenDetailCommand`. No business logic in code-behind. (`src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs`)
- **`SecurityMasterPage.xaml` / `SecurityMasterPage.xaml.cs`** — thin code-behind; `DataContext` wired to `SecurityMasterViewModel`. (`src/Meridian.Wpf/Views/SecurityMasterPage.xaml`)
- **Navigation registration** — `Pages.cs` declares `SecurityMasterPage` and `NavigationService` registers `"SecurityMaster" → typeof(SecurityMasterPage)`.
- **Workstation DTOs** — `SecurityMasterWorkstationDto` (classification + economic summary), `SecurityIdentityDrillInDto` (full identifier and alias drill-in), in `Meridian.Contracts.Workstation`.
- **Tests** — `SecurityMasterReferenceLookupTests` covers `SecurityMasterSecurityReferenceLookup` (unresolved identity, degraded metadata, sub-type derivation); `SecurityEnrichmentTests` covers `PortfolioReadService` and `LedgerReadService` enrichment paths (resolved, partial, missing). (`tests/Meridian.Tests/SecurityMaster/`)

### Remaining Detail Panel Work

The corporate action timeline and trading parameters detail panel (originally scoped in Idea 6) depend on Ideas 1 and 3 being completed first. The ViewModel is structured to accept those fields once the APIs exist.

---

## Sequencing

| Order | Idea | Status | Reason |
|-------|------|--------|--------|
| 1 | Bond Term Richness | ✅ Done | Data model foundation; enables fixed-income workflows downstream |
| 2 | WPF Security Master Browser | ✅ Done | UI surface on top of completed backend capabilities |
| 3 | Trading Parameters | 🔶 Partial | Correctness for execution + backtesting; schema is in place |
| 4 | Corporate Action Events | 🔲 Next | Requires stable domain model; feeds directly into backtest correctness |
| 5 | Exchange Bulk Ingest | 🔲 Queued | Needs stable domain model; enables broad coverage |
| 6 | Golden Record Conflict Resolution | 🔲 Queued | Needs multi-provider ingest to generate meaningful conflicts |

---

## Reference

- `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`
- `src/Meridian.FSharp/Domain/SecurityTermModules.fs`
- `src/Meridian.FSharp/Domain/SecurityMasterEvents.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`
- `src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs`
- `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs`
- `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs`
- `tests/Meridian.Tests/SecurityMaster/`
- [`ROADMAP.md`](../status/ROADMAP.md) — Wave 6
- [`FEATURE_INVENTORY.md`](../status/FEATURE_INVENTORY.md) — Security Master section
