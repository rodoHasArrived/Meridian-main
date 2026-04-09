# Security Master Productization Roadmap

**Last Updated:** 2026-04-07
**Status:** Delivered — Wave 6 mechanics plus workstation/read-model productization
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

Meridian's Security Master has contracts, Postgres-backed services, F# domain modules, and REST endpoints. This roadmap captured the six Wave 6 mechanics required to establish Security Master as a platform layer, and now records the follow-on workstation/read-model productization that makes those mechanics operator-usable.

**Wave 6 delivery state (as of 2026-03-26):**

| # | Idea | Status |
|---|------|--------|
| 1 | Corporate Action Events | ✅ Delivered |
| 2 | Bond Term Richness | ✅ Delivered |
| 3 | Trading Parameters | ✅ Delivered |
| 4 | Exchange Bulk Ingest | ✅ Delivered |
| 5 | Golden Record Conflict Resolution | ✅ Delivered |
| 6 | WPF Security Master Browser | ✅ Delivered |

---

## Productization Completion (2026-04-07)

Security Master is now productized as the authoritative instrument-definition seam across the workstation shell rather than remaining a backend-only subsystem.

### Delivered in this completion pass

- **Desktop hardening** — `SecurityMasterPage` now resolves in all desktop environments. WPF composition registers real-or-null `ISecurityMasterImportService` and `ITradingParametersBackfillService` implementations, and the page falls back to degraded status text plus disabled unsupported actions when Security Master storage or Polygon credentials are absent.
- **Canonical enrichment contract** — `WorkstationSecurityReference` is now the single cross-workspace enrichment contract with explicit coverage/provenance fields: coverage status, matched identifier kind/value, matched provider, and optional resolution reason.
- **Shared read-model propagation** — portfolio, ledger, reconciliation, and governance payloads now carry structured Security Master references instead of count-only or narrative-only signals, so positions, journal rows, and reconciliation issues all point back to the same authoritative security identity.
- **Cross-workspace delivery** — Research, Trading, and Governance workstation surfaces now expose Security Master identity, classification, subtype, currency, coverage state, and detail deep links from the shared workstation payloads.
- **Operator surface split** — WPF remains the primary maintenance surface for create/amend/deactivate/import/backfill workflows, while the React governance flow now consumes detail/history/economic-definition routes for drill-ins, unresolved-coverage deep links, and conflict refresh.

### Remaining follow-on work

- Expand Security Master-backed governance/report-pack workflows beyond the current coverage, reconciliation, and drill-in surfaces.
- Add richer task/assignment and remediation workflows for unresolved mappings once operator workflow ownership is finalized.
- Keep operability work focused on provider-native ingest automation and platform reliability rather than redefining the Security Master domain model.

---

## Delivered Capabilities (not in original scope)

The following capabilities were implemented as foundational work for this wave and are now shipped:

- **`SecurityMasterProjectionWarmupService`** (`IHostedService`) — warms the in-memory projection cache on startup when `PreloadProjectionCache` is enabled, eliminating cold-start latency. (`src/Meridian.Application/SecurityMaster/SecurityMasterProjectionWarmupService.cs`)
- **`SecurityMasterOptionsValidator`** (`IValidateOptions<SecurityMasterOptions>`) — validates connection string, schema, `SnapshotIntervalVersions`, and `ProjectionReplayBatchSize` at startup. (`src/Meridian.Application/SecurityMaster/SecurityMasterOptionsValidator.cs`)
- **`SecurityMasterRebuildOrchestrator`** — differential event-replay that picks up only new events since the last checkpoint, avoiding full re-projection on every restart. (`src/Meridian.Application/SecurityMaster/SecurityMasterRebuildOrchestrator.cs`)
- **Full-text search migration** — Postgres `tsvector` column backed by a `gin` index and an `AFTER INSERT OR UPDATE` trigger, fed from `display_name`, `primary_identifier_value`, `asset_class`, `issuer_name`, `exchange_code`, and `currency`. (`src/Meridian.Storage/SecurityMaster/Migrations/002_security_master_fts.sql`)
- **`ISecurityMasterQueryService.GetEconomicDefinitionByIdAsync`** — full aggregate rebuild on demand for governance drill-in surfaces.
- **`SecurityMasterSecurityReferenceLookup`** (`ISecurityReferenceLookup`) — adapts the Security Master query service to portfolio/ledger workstation enrichment; resolves by exact identifiers (`ISIN`, `CUSIP`, `SEDOL`, `FIGI`), provider-symbol syntaxes, normalized tickers, and venue/decorator-stripped workstation symbols before deriving sub-type from asset class. (`src/Meridian.Ui.Shared/Services/SecurityMasterSecurityReferenceLookup.cs`)
- **Workstation DTOs** — `SecurityMasterWorkstationDto`, `SecurityClassificationSummaryDto`, `SecurityEconomicDefinitionSummaryDto`, `SecurityIdentityDrillInDto` in `Meridian.Contracts.Workstation`. (`src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs`)
- **Security enrichment tests** — `SecurityEnrichmentTests` (portfolio/ledger resolution paths) and `SecurityMasterReferenceLookupTests` (unresolved identity, degraded metadata, sub-type derivation). (`tests/Meridian.Tests/SecurityMaster/`)

---

## Operator Fund Operations Delivery

Security Master is now wired into the governance-first fund-operations workflow rather than stopping at backend enrichment.

### What Was Delivered

- **Fund-operations DTO expansion** — `FundWorkspaceSummary`, `FundAccountSummary`, `FundPortfolioPosition`, `FundReconciliationItem`, and `ReconciliationSummary` now carry Security Master coverage, entity/sleeve/vehicle structure, strategy/run linkage, and reconciliation security-issue counts. (`src/Meridian.Contracts/Workstation/FundOperationsDtos.cs`)
- **Account and structure workflows** — governance account projections now expose `EntityId`, `SleeveId`, `VehicleId`, `StrategyId`, `RunId`, plus operator-readable `StructureLabel` and `WorkflowLabel` values so teams can work from legal-entity and strategy context instead of raw account rows. (`src/Meridian.Wpf/Services/FundAccountReadService.cs`)
- **Fund portfolio operator drill-in** — aggregated fund positions now preserve Security Master identity metadata, show mapped/partial/unresolved coverage state, and support opening the selected position directly into the Security Master browser. (`src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs`, `src/Meridian.Wpf/Views/FundLedgerPage.xaml`)
- **Strategy-run reconciliation integration** — governance reconciliation now loads strategy-run reconciliation results alongside account-level runs, surfaces `SecurityIssueCount`, distinguishes `ScopeLabel` (`Account` vs `Strategy Run`), and raises Security Master coverage breaks into the same operator queue. (`src/Meridian.Wpf/Services/ReconciliationReadService.cs`)
- **Operator-facing WPF delivery** — the fund ledger page now shows structure/workflow context on accounts, Security Master coverage on portfolio rows, reconciliation coverage columns, and top-level overview text that calls out unresolved mappings and open reconciliation security coverage issues. (`src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs`, `src/Meridian.Wpf/Views/FundLedgerPage.xaml`)
- **Desktop service registration** — reconciliation projection/run services are registered in the desktop composition root so strategy-run coverage and reconciliation drill-ins work in the WPF operator shell, not only in tests. (`src/Meridian.Wpf/App.xaml.cs`)
- **Coverage-closing regression tests** — focused WPF and workstation tests now cover identifier heuristics, fund-ops Security Master carry-through, and strategy-level reconciliation security coverage. (`tests/Meridian.Tests/SecurityMaster/SecurityMasterReferenceLookupTests.cs`, `tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs`)

### Operator Outcome

Operators can now answer all of the following from the fund-operations shell without dropping into backend diagnostics:

- Which aggregated positions are still missing Security Master coverage.
- Which accounts belong to which entity/sleeve/vehicle structure.
- Which accounts are tied to strategy/run-driven workflows versus manual/external workflows.
- Which reconciliation runs have open security-coverage issues, including strategy-run driven breaks.
- Which unresolved position should be opened directly in Security Master for remediation.

### Remaining Follow-on Work

- Extend the same operator-first Security Master posture into additional governance surfaces that are still run-centric outside the fund-operations shell.
- Add explicit remediation actions for unresolved mappings and reconciliation coverage breaks once the task/assignment workflow is finalized.

---

## Idea 1 — Corporate Action Events

**Status: ✅ Delivered**

### What Was Delivered

- **`CorpActEvent` discriminated union** in `SecurityMasterEvents.fs` — `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption`, `RightsIssue` cases with full field coverage including `CorpActId`, `exDate`, `payDate`, `splitRatio`, `distributionRatio`, etc.
- **`CorpActId` opaque type** — `CorpActId of Guid` following the domain naming standard.
- **`CorpActEvent` module** — `securityId`, `corpActId`, `exDate`, `eventType` accessors for all cases.
- **Storage surface** — `ISecurityMasterEventStore.AppendCorporateActionAsync` and `LoadCorporateActionsAsync` implemented and persisted via `PostgresSecurityMasterEventStore`.
- **REST endpoints** — `GET /api/security-master/{id}/corporate-actions` and `POST /api/security-master/{id}/corporate-actions` both wired in `SecurityMasterEndpoints.cs`.
- **`CorporateActionDto`** contract DTO covering all five event types.
- **WPF recording UI** — `SecurityMasterViewModel` includes `CorporateActions` (`ObservableCollection<CorporateActionDto>`) and commands for recording Dividend and StockSplit events.
- **Backtest integration** — `BacktestEngine` applies corporate action adjustments via `ICorporateActionAdjustmentService` when `request.AdjustForCorporateActions` is `true`.

### Follow-on Productization Work

- Expand operator-facing corporate-action presentation beyond the current WPF/governance drill-ins as broader governance report-pack workflows land.
- Add more historical/operator reporting views on top of the delivered event, storage, and adjustment infrastructure.

### Current State

`SecurityMasterEvents.fs` defines `CorpActEvent` with all five cases. `ISecurityMasterEventStore` persists and replays corp-action events. The REST surface, WPF UI, and backtest adjustment path are all live.

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

### Follow-on Operability Work

- Add provider-native exchange ingest automation so operators can trigger vendor-backed listing imports without preparing CSV/JSON payloads first.
- Add ingest-status polling/telemetry for long-running operational bulk loads.

---

## Idea 5 — Golden Record Conflict Resolution

**Status: ✅ Delivered**

### What Was Delivered

- **`SecurityMasterConflict` DTO** — `ConflictId`, `SecurityId`, `ConflictKind`, `FieldPath`, `ProviderA/B`, `ValueA/B`, `DetectedAt`, `Status` fields.
- **`ISecurityMasterConflictService` / `SecurityMasterConflictService`** — on-demand identifier-ambiguity detection scanning all projections; `GetOpenConflictsAsync`, `GetConflictAsync`, `ResolveAsync` (marks as Resolved or Dismissed); uses a deterministic stable `ConflictId` (MD5 of identifier tuple) so re-detection yields the same ID.
- **`ResolveConflictRequest`** DTO — `ConflictId`, `Resolution`, `ResolvedBy`, optional `Reason`.
- **REST endpoints** — `GET /api/security-master/conflicts` and `POST /api/security-master/conflicts/{id}/resolve` in `SecurityMasterEndpoints.cs`.

### Follow-on Operability Work

- Move conflict creation closer to ingest/projection time so provider disagreements are raised automatically as data lands.
- Add additional operator notification/badge surfaces on top of the delivered governance conflict-review workflow.

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
| 3 | Trading Parameters | ✅ Done | All six fields exposed; PaperTradingGateway validates lot size and snaps to tick grid |
| 4 | Corporate Action Events | ✅ Done | Domain model, storage, adjustment service, and operator-facing drill-ins are available |
| 5 | Exchange Bulk Ingest | ✅ Done | CSV/JSON import, CLI command, API endpoint, and desktop/operator access are available |
| 6 | Golden Record Conflict Resolution | ✅ Done | Conflict list, resolution workflow, governance refresh, and workstation drill-ins are available |

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
