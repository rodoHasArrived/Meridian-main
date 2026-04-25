# Security Master Productization Roadmap

**Last Updated:** 2026-04-25
**Status:** Delivered Security Master baseline; Wave 4 governance and fund-operations follow-ons remain active in the canonical roadmap
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

Meridian's Security Master has contracts, Postgres-backed services, F# domain modules, and REST endpoints. This roadmap originally captured six prioritized ideas that moved Security Master from a backend capability into a first-class platform layer. It now records the delivered workstation/read-model productization plus the completed Security Master operator workflow closure that made ingest posture, conflict resolution, and governance drill-ins first-class workstation journeys.

Security Master is no longer a future roadmap wave. In the canonical roadmap it is a delivered baseline feeding the active governance productization path.

**Delivered mechanics snapshot (as of 2026-04-16):**

| # | Idea | Status |
|---|------|--------|
| 1 | Corporate Action Events | ✅ Delivered |
| 2 | Bond Term Richness | ✅ Delivered |
| 3 | Trading Parameters | ✅ Delivered |
| 4 | Exchange Bulk Ingest | ✅ Delivered |
| 5 | Golden Record Conflict Resolution | ✅ Delivered |
| 6 | WPF Security Master Browser | ✅ Delivered |

**Canonical status source note (2026-04-25):**

- Treat [`../status/ROADMAP.md`](../status/ROADMAP.md) and [`../status/PROGRAM_STATE.md`](../status/PROGRAM_STATE.md) as the canonical wave status sources.
- Treat this roadmap as the delivered-baseline reference for Security Master mechanics and the starting point for Wave 4 governance/fund-operations follow-ons.
- `docs/status/FEATURE_INVENTORY.md` should mirror the delivered Security Master baseline without treating Security Master as a separate later-wave item.

## Security Master Operator Closure (2026-04-16)

The Security Master operator slice is now complete:

1. **Automatic ingest-time conflict detection**
   - create, amend, import, and projection-write flows record `SecurityMasterConflict` entries instead of silently overwriting mismatches.
2. **Ingest status polling surface**
   - `GET /api/security-master/ingest/status` returns active-import, last-completed-import, and unresolved-conflict posture for workstation polling.
3. **Governance drill-in continuity**
   - Security Master now drills directly into portfolio, ledger, reconciliation, cash-flow, and report-pack operator workflows.
4. **Release-gate journey evidence**
   - endpoint coverage plus WPF journey tests now verify ingest polling, conflict resolution, report-pack continuity, and cross-workspace drill-ins.

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

**Status: ✅ Delivered**

### What Was Delivered

- **`SecurityMasterCsvParser`** — parses common exchange listing CSV formats into `CreateSecurityRequest` lists with error accumulation.
- **`SecurityMasterImportService` / `ISecurityMasterImportService`** — orchestrates CSV/JSON bulk import with per-row error handling, duplicate detection (skips on "already exists"), and `IProgress<SecurityMasterImportProgress>` reporting.
- **`SecurityMasterCommands`** — `--security-master-ingest <file.csv|file.json>` CLI command wired through `CommandDispatcher`; prints per-row progress and final summary.
- **`POST /api/security-master/import`** endpoint — accepts `SecurityMasterImportRequest` (file content + extension) and streams import results.
- **`PolygonSecurityMasterIngestProvider`** — bulk reference ingest provider for Polygon-backed exchange listing imports.
- **`GET /api/security-master/ingest/status`** — typed ingest-status endpoint backed by `ISecurityMasterIngestStatusService`, including active import, last completed import, and unresolved conflict count for dashboard polling.

### Acceptance Criteria — Status

| Criterion | Status |
|---|---|
| Polygon provider ingests a full exchange listing without manual CSV export. | ✅ Done |
| `GET /api/security-master/ingest/status` returns in-progress and last-completed ingest summary. | ✅ Done |

---

## Idea 5 — Golden Record Conflict Resolution

**Status: ✅ Delivered**

### What Was Delivered

- **`SecurityMasterConflict` DTO** — `ConflictId`, `SecurityId`, `ConflictKind`, `FieldPath`, `ProviderA/B`, `ValueA/B`, `DetectedAt`, `Status` fields.
- **`ISecurityMasterConflictService` / `SecurityMasterConflictService`** — on-demand identifier-ambiguity detection scanning all projections; `GetOpenConflictsAsync`, `GetConflictAsync`, `ResolveAsync` (marks as Resolved or Dismissed); uses a deterministic stable `ConflictId` (MD5 of identifier tuple) so re-detection yields the same ID.
- **`ResolveConflictRequest`** DTO — `ConflictId`, `Resolution`, `ResolvedBy`, optional `Reason`.
- **REST endpoints** — `GET /api/security-master/conflicts` and `POST /api/security-master/conflicts/{id}/resolve` in `SecurityMasterEndpoints.cs`.
- **Automatic conflict recording** — create, amend, import, and projection-write flows now record conflicts through `ISecurityMasterConflictService` instead of silently overwriting conflicting projection values.

### Acceptance Criteria — Status

| Criterion | Status |
|---|---|
| Two providers with differing `DisplayName` for the same FIGI trigger an automatic `SecurityMasterConflict` record during ingest. | ✅ Done |
| Unresolved conflict count is surfaced in the Security Master workstation operator surface. | ✅ Done |

---

## Idea 6 — WPF Security Master Browser

**Status: ✅ Delivered**

### What Was Delivered

- **`SecurityMasterViewModel`** (`BindableBase`) — `SearchQuery`, `ActiveOnly`, `IsLoading`, `StatusText`, `SelectedSecurity` bindable properties; `Results` (`ObservableCollection<SecurityMasterWorkstationDto>`); `SearchCommand`, `ClearCommand`, `OpenDetailCommand`. No business logic in code-behind. (`src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs`)
- **`SecurityMasterPage.xaml` / `SecurityMasterPage.xaml.cs`** — thin code-behind; `DataContext` wired to `SecurityMasterViewModel`. (`src/Meridian.Wpf/Views/SecurityMasterPage.xaml`)
- **Navigation registration** — `Pages.cs` declares `SecurityMasterPage` and `NavigationService` registers `"SecurityMaster" → typeof(SecurityMasterPage)`.
- **Workstation DTOs** — `SecurityMasterWorkstationDto` (classification + economic summary), `SecurityIdentityDrillInDto` (full identifier and alias drill-in), in `Meridian.Contracts.Workstation`.
- **Operator workflow surface** — ingest polling, conflict queue/review/resolution commands, and explicit drill-ins into fund portfolio, ledger, reconciliation, cash-flow, and report-pack journeys.
- **Tests** — `SecurityMasterReferenceLookupTests` covers `SecurityMasterSecurityReferenceLookup` (unresolved identity, degraded metadata, sub-type derivation); `SecurityEnrichmentTests` covers `PortfolioReadService` and `LedgerReadService` enrichment paths (resolved, partial, missing). (`tests/Meridian.Tests/SecurityMaster/`)

---

## Sequencing

| Order | Idea | Status | Notes |
|-------|------|--------|-------|
| 1 | Bond Term Richness | ✅ Done | Data model foundation; enables fixed-income workflows downstream |
| 2 | WPF Security Master Browser | ✅ Done | UI surface on top of completed backend capabilities |
| 3 | Trading Parameters | ✅ Done | All six fields exposed; PaperTradingGateway validates lot size and snaps to tick grid |
| 4 | Corporate Action Events | ✅ Done | Domain model, storage, endpoints, WPF timeline, and backtest adjustment service are all wired |
| 5 | Exchange Bulk Ingest | ✅ Done | CSV/JSON import, CLI, provider ingest, and typed ingest-status polling surface are live |
| 6 | Golden Record Conflict Resolution | ✅ Done | Automatic ingest-time conflict detection, resolve endpoints, and workstation operator queue are all live |

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
- [`ROADMAP.md`](../status/ROADMAP.md) — delivered Security Master baseline plus Wave 4 governance follow-ons
- [`FEATURE_INVENTORY.md`](../status/FEATURE_INVENTORY.md) — Security Master section
