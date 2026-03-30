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
| 3 | Trading Parameters | ✅ Delivered |
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

**Status: ✅ Delivered**

### What Was Delivered

- `CorpActEvent` discriminated union in `SecurityMasterEvents.fs`: `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption`, `RightsIssue`. Each case carries `CorpActId of Guid`, `securityId`, `exDate: DateOnly`, `payDate: DateOnly option`, and event-specific fields.
- `ISecurityMasterEventStore.LoadCorporateActionsAsync` and `PostgresSecurityMasterEventStore` persist/replay `CorpActEvent` records (separate event stream keyed by `security_id`, returned in ascending ex-date order).
- `CorporateActionAdjustmentService` applies split/dividend adjustments to `HistoricalBar` data via `AdjustAsync`.
- `GET /api/security-master/{id}/corporate-actions` endpoint returns time-ordered `CorporateActionDto` list.
- `BacktestEngine` integrates `ICorporateActionAdjustmentService` (optional injection); activated when `BacktestRequest.AdjustForCorporateActions = true`.
- Full unit-test coverage in `CorporateActionAdjustmentServiceTests`: split factor combination, dividend price adjustment, no-op when security not found.

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

**Status: ✅ Delivered**

### What Was Delivered

- `TradingParametersDto` and `TradingParameters` F# module in `SecurityTermModules.fs`: `LotSize`, `TickSize`, `ContractMultiplier`, `MarginRequirementPct`, `TradingHoursUtc`, `CircuitBreakerThresholdPct`.
- `ISecurityMasterQueryService.GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf)` implemented in `SecurityMasterQueryService`.
- `PaperTradingGateway` accepts optional `ISecurityMasterQueryService`; rejects sub-lot orders via `ValidateLotSizeAsync` (looks up security by ticker, reads `LotSize`, returns validation error if `qty % lotSize != 0`). Non-blocking on Security Master unavailability.
- `BacktestEngine` resolves tick sizes via `ResolveTickSizesAsync` and snaps fill prices to the tick grid in fill models when `TickSize > 0`.
- `GET /api/security-master/{id}/trading-parameters` endpoint live.
- 6 new `PaperTradingGatewayTests` covering valid lot, sub-lot rejection, sell-order sub-lot, security-not-found passthrough, and no-security-master passthrough.

---

## Idea 4 — Exchange Bulk Ingest

**Status: ✅ Delivered**

### What Was Delivered

- `ISecurityMasterImportService` / `SecurityMasterImportService` accepts CSV or JSON file content and bulk-creates securities via `ISecurityMasterService`.
- `SecurityMasterCsvParser` parses exchange listing CSVs (Ticker, Name, AssetClass, Currency, Exchange, ISIN, CUSIP, FIGI columns; case-insensitive; RFC 4180 quoted fields).
- Idempotent: duplicate symbols caught on `CreateAsync` are counted as `Skipped`, not `Failed`.
- Progress callbacks via `IProgress<SecurityMasterImportProgress>` (total, processed, imported, failed).
- CLI command `--security-master-ingest <file.csv|file.json>` wired through `CommandDispatcher`; prints per-row progress and final summary.
- `POST /api/security-master/import` HTTP endpoint (multipart form-data, returns `SecurityMasterImportResult`).
- WPF import surface in `SecurityMasterViewModel` + `SecurityMasterPage`.

---

## Idea 5 — Golden Record Conflict Resolution

**Status: ✅ Delivered**

### What Was Delivered

- `SecurityMasterConflict` domain record in `Meridian.Contracts.SecurityMaster`: `ConflictId`, `SecurityId`, `ConflictKind`, `FieldPath`, `ProviderA/B`, `ValueA/B`, `DetectedAt`, `Status`.
- `ISecurityMasterConflictService` / `SecurityMasterConflictService` (in-memory, backed by `ISecurityMasterStore`): detects identifier-ambiguity conflicts by scanning all projections for same-identifier-different-SecurityId pairs; deterministic `ConflictId` derived from MD5 of sorted key tuple (stable across re-detections).
- `GetOpenConflictsAsync` — returns all `Status = "Open"` conflicts; adds newly detected, preserves existing resolution state.
- `ResolveAsync(ResolveConflictRequest)` — marks conflict `"Resolved"` or `"Dismissed"` and logs the resolver.
- `GET /api/security-master/conflicts` and `POST /api/security-master/conflicts/{conflictId}/resolve` endpoints registered in `SecurityMasterEndpoints`.
- Service registered in `StorageFeatureRegistration`.

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

The corporate action timeline and trading parameters detail panel are fully backed by the APIs delivered in Ideas 1 and 3. The ViewModel is structured to present those fields; a dedicated drill-in panel can be wired in a follow-on UI sprint.

---

## Sequencing

| Order | Idea | Status | Notes |
|-------|------|--------|-------|
| 1 | Bond Term Richness | ✅ Done | Data model foundation; enables fixed-income workflows downstream |
| 2 | WPF Security Master Browser | ✅ Done | UI surface on top of completed backend capabilities |
| 3 | Trading Parameters | ✅ Done | `PaperTradingGateway` lot-size validation + `BacktestEngine` tick-size rounding |
| 4 | Corporate Action Events | ✅ Done | `CorporateActionAdjustmentService`; backtest replay; Postgres store + endpoint |
| 5 | Exchange Bulk Ingest | ✅ Done | `SecurityMasterImportService`; CSV/JSON; CLI `--security-master-ingest`; HTTP endpoint |
| 6 | Golden Record Conflict Resolution | ✅ Done | `SecurityMasterConflictService`; conflict list + resolve endpoints |

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
