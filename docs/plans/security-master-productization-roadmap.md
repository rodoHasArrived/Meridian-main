# Security Master Productization Roadmap

**Last Updated:** 2026-03-30  
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
| 1 | Corporate Action Events | 🔶 Partial |
| 2 | Bond Term Richness | ✅ Delivered |
| 3 | Trading Parameters | ✅ Delivered |
| 4 | Exchange Bulk Ingest | 🔶 Partial |
| 5 | Golden Record Conflict Resolution | 🔶 Partial |
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

**Status: 🔶 Partial**

### What Was Delivered

- **`CorpActEvent` discriminated union** in `SecurityMasterEvents.fs` — `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption`, `RightsIssue` cases with full field coverage including `CorpActId`, `exDate`, `payDate`, `splitRatio`, `distributionRatio`, etc.
- **`CorpActId` opaque type** — `CorpActId of Guid` following the domain naming standard.
- **`CorpActEvent` module** — `securityId`, `corpActId`, `exDate`, `eventType` accessors for all cases.
- **Storage surface** — `ISecurityMasterEventStore.AppendCorporateActionAsync` and `LoadCorporateActionsAsync` implemented and persisted via `PostgresSecurityMasterEventStore`.
- **REST endpoints** — `GET /api/security-master/{id}/corporate-actions` and `POST /api/security-master/{id}/corporate-actions` both wired in `SecurityMasterEndpoints.cs`.
- **`CorporateActionDto`** contract DTO covering all five event types.
- **WPF recording UI** — `SecurityMasterViewModel` includes `CorporateActions` (`ObservableCollection<CorporateActionDto>`) and commands for recording Dividend and StockSplit events.
- **Backtest integration** — `BacktestEngine` applies corporate action adjustments via `ICorporateActionAdjustmentService` when `request.AdjustForCorporateActions` is `true`.

### Remaining Work

- **`CorporateActionAdjustmentService` production implementation** — the current `ICorporateActionAdjustmentService` interface exists but needs a concrete implementation that reads from the `ISecurityMasterEventStore` and applies split/dividend price adjustments to `HistoricalBar` slices.
- **Corporate action timeline visualization** in the WPF detail panel (depends on production service above).

### Current State

`SecurityMasterEvents.fs` defines `CorpActEvent` with all five cases. `ISecurityMasterEventStore` persists and replays corp-action events. The REST surface and WPF UI are complete. The BacktestEngine hook exists but requires a real `ICorporateActionAdjustmentService` implementation backed by the event store.

### Acceptance Criteria (remaining)

- `CorporateActionAdjustmentService` reads corporate actions from `ISecurityMasterEventStore` and adjusts historical bar OHLCV for splits and dividends.
- Backtest P&L curves match expected split-adjusted prices when `AdjustForCorporateActions = true`.
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

**Status: ✅ Delivered**

### What Was Delivered

- **`TradingParams` F# record** in `SecurityTermModules.fs` — `LotSize`, `TickSize`, `ContractMultiplier`, `MarginRequirementPct`, `TradingHoursUtc`, `CircuitBreakerThresholdPct` fields with `option` types; included in `SecurityTermModules` aggregate record.
- **`ISecurityMasterQueryService.GetTradingParametersAsync`** — reads all six fields from `CommonTerms` JSON (`lotSize`, `tickSize`, `contractMultiplier`, `marginRequirementPct`, `tradingHoursUtc`, `circuitBreakerThresholdPct`) and returns a fully populated `TradingParametersDto`.
- **`GET /api/security-master/{id}/trading-parameters`** endpoint — live in `SecurityMasterEndpoints.cs`.
- **`TradingParametersDto`** contract DTO — all seven fields including `AsOf`.
- **`PaperTradingGateway` lot-size validation** — `ValidateOrderAsync` checks that `Quantity` is a positive multiple of `LotSize` when Security Master is configured; rejects sub-lot orders with a descriptive error.
- **`PaperTradingGateway` tick-size price snapping** — `SimulateFillAsync` rounds the fill price to the nearest tick-size grid using `MidpointRounding.AwayFromZero`.
- **`PaperTradingGateway` Security Master injection** — accepts optional `ISecurityMasterQueryService`; results cached per symbol to avoid repeated I/O; all checks are best-effort and do not block execution when Security Master is unavailable.
- **`BacktestEngine` tick-size integration** — `ResolveTickSizesAsync` queries `GetTradingParametersAsync` for each universe symbol and passes the resulting dictionary to `OrderBookFillModel` and `BarMidpointFillModel`.

### Acceptance Criteria — Status

| Criterion | Status |
|---|---|
| `PaperTradingGateway` rejects a sub-lot order for an instrument with `LotSize > 1` | ✅ Done |
| `BacktestEngine` rounds fill prices to the instrument's tick size | ✅ Done |
| Parameters are queryable by `GET /api/security-master/{id}/trading-parameters` | ✅ Done |

---

## Idea 4 — Exchange Bulk Ingest

**Status: 🔶 Partial**

### What Was Delivered

- **`SecurityMasterCsvParser`** — parses common exchange listing CSV formats into `CreateSecurityRequest` lists with error accumulation.
- **`SecurityMasterImportService` / `ISecurityMasterImportService`** — orchestrates CSV/JSON bulk import with per-row error handling, duplicate detection (skips on "already exists"), and `IProgress<SecurityMasterImportProgress>` reporting.
- **`SecurityMasterCommands`** — `--security-master-ingest <file.csv|file.json>` CLI command wired through `CommandDispatcher`; prints per-row progress and final summary.
- **`POST /api/security-master/import`** endpoint — accepts `SecurityMasterImportRequest` (file content + extension) and streams import results.

### Remaining Work

- **`PolygonSecurityMasterIngestProvider`** — pages through Polygon `/v3/reference/tickers` and maps responses to `CreateSecurityRequest`; enables `--security-master-ingest --provider polygon --exchange XNAS`.
- **Ingest status endpoint** — `GET /api/security-master/ingest/status` for dashboard polling.

### Acceptance Criteria (remaining)

- Polygon provider ingests a full exchange listing without manual CSV export.
- `GET /api/security-master/ingest/status` returns in-progress and last-completed ingest summary.

---

## Idea 5 — Golden Record Conflict Resolution

**Status: 🔶 Partial**

### What Was Delivered

- **`SecurityMasterConflict` DTO** — `ConflictId`, `SecurityId`, `ConflictKind`, `FieldPath`, `ProviderA/B`, `ValueA/B`, `DetectedAt`, `Status` fields.
- **`ISecurityMasterConflictService` / `SecurityMasterConflictService`** — on-demand identifier-ambiguity detection scanning all projections; `GetOpenConflictsAsync`, `GetConflictAsync`, `ResolveAsync` (marks as Resolved or Dismissed); uses a deterministic stable `ConflictId` (MD5 of identifier tuple) so re-detection yields the same ID.
- **`ResolveConflictRequest`** DTO — `ConflictId`, `Resolution`, `ResolvedBy`, optional `Reason`.
- **REST endpoints** — `GET /api/security-master/conflicts` and `POST /api/security-master/conflicts/{id}/resolve` in `SecurityMasterEndpoints.cs`.

### Remaining Work

- **Automatic conflict detection on ingest** — `SecurityMasterProjectionService` should detect and record a `SecurityMasterConflict` when two providers contribute conflicting field values for the same FIGI/ISIN rather than silently overwriting.
- **Dashboard conflict badge** — surface unresolved conflict count in the web dashboard Security Master panel.

### Acceptance Criteria (remaining)

- Two providers with differing `DisplayName` for the same FIGI trigger an automatic `SecurityMasterConflict` record during ingest.
- Unresolved conflict count is surfaced in the Security Master dashboard panel.

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
| 3 | Trading Parameters | ✅ Done | All six fields exposed; PaperTradingGateway validates lot size and snaps to tick grid |
| 4 | Corporate Action Events | 🔶 Partial | Domain model + storage + endpoints done; production `ICorporateActionAdjustmentService` implementation remaining |
| 5 | Exchange Bulk Ingest | 🔶 Partial | CSV/JSON import + CLI command done; Polygon provider ingest remaining |
| 6 | Golden Record Conflict Resolution | 🔶 Partial | On-demand detection + resolve REST endpoints done; automatic ingest-time detection remaining |

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
