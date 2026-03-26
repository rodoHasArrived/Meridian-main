# Security Master Productization Roadmap

**Last Updated:** 2026-03-26  
**Status:** Planned — Wave 6 delivery  
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

Meridian's Security Master already has contracts, Postgres-backed services, F# domain modules, and REST endpoints. What it lacks is a productized, operator-facing surface that makes it the authoritative instrument-definition layer for research, portfolio tracking, governance, and ledger workflows.

This roadmap captures six prioritized ideas that together move Security Master from a backend capability to a first-class platform layer.

---

## Idea 1 — Corporate Action Events

### Problem

Corporate actions (dividends, splits, mergers, spin-offs, rights issues) are not yet modeled as domain events in Security Master. Backtesting and portfolio tracking silently ignore them, which produces incorrect historical P&L and position history.

### Proposed Work

- Add `CorporateActionEvent` domain record types to `Meridian.FSharp` (`SecurityMasterEvents.fs`): `Dividend`, `StockSplit`, `SpinOff`, `MergerAbsorption`, `RightsIssue`.
- Extend `ISecurityMasterEventStore` and the Postgres event store to persist and replay corporate action events.
- Add `CorporateActionAdjustmentService` that applies split/dividend adjustments to `HistoricalBar` data on read.
- Expose `/api/security-master/{id}/corporate-actions` endpoint returning a time-ordered list of events.
- Backtest engine: integrate adjustment service so fill prices are split-adjusted automatically.

### Acceptance Criteria

- `SecurityMasterAggregateRebuilder` correctly folds corporate action events into the aggregate.
- `BacktestEngine` replay produces adjusted bar prices when corporate actions are present.
- At least one provider (Polygon or Alpha Vantage) feeds corporate action data into the event store.

---

## Idea 2 — Bond Term Richness

### Problem

`SecurityEconomicDefinition` covers equities well but is sparse for fixed-income instruments. Bond-specific terms (coupon rate, frequency, maturity date, day-count convention, issue price, callable flag, seniority) are missing, making the Security Master inadequate for any fixed-income workflow.

### Proposed Work

- Extend `SecurityEconomicDefinition` (F# type in `SecurityEconomicDefinition.fs`) with a `BondTerms` discriminated union case.
- Add fields: `CouponRate`, `CouponFrequency`, `MaturityDate`, `DayCountConvention`, `IssuePrice`, `IsCallable`, `CallSchedule`, `Seniority`, `IssuerName`, `Rating`.
- Update `SecurityMasterDbMapper` to persist and hydrate `BondTerms`.
- Update `SecurityDtos` contracts and the JSON source generator context.
- Add F# tests in `DomainTests.fs` covering bond-term round-trip serialization and aggregate rebuild.

### Acceptance Criteria

- A bond security round-trips through Postgres with all term fields intact.
- Bond search returns coupon/maturity in the response payload.
- Existing equity tests pass unchanged.

---

## Idea 3 — Trading Parameters

### Problem

Instrument-level trading parameters (lot size, tick size, contract size, margin requirement, trading hours, circuit-breaker thresholds) are not stored in Security Master. The execution layer and backtesting engine currently hard-code or ignore these, producing unrealistic fills for non-standard instruments.

### Proposed Work

- Add `TradingParameters` record to `SecurityEconomicDefinition.fs`: `LotSize`, `TickSize`, `ContractMultiplier`, `MarginRequirementPercent`, `TradingHoursUtc`, `CircuitBreakerThresholdPercent`.
- Expose `GetTradingParametersAsync(symbolId, asOf)` on `ISecurityMasterQueryService`.
- Wire trading parameters into `PaperTradingGateway` fill validation (reject orders violating lot size or tick size).
- Wire into `BacktestEngine` so fill models respect tick size and lot size.
- Expose via `/api/security-master/{id}/trading-parameters`.

### Acceptance Criteria

- `PaperTradingGateway` rejects a sub-lot order for an instrument with `LotSize > 1`.
- `BacktestEngine` rounds fill prices to the instrument's tick size.
- Parameters are time-travel-queryable (returns parameters as-of a given date).

---

## Idea 4 — Exchange Bulk Ingest

### Problem

Populating Security Master for broad equity coverage (thousands of instruments) currently requires per-symbol API calls. There is no bulk-ingest path from exchange listing files or provider bulk endpoints.

### Proposed Work

- Add `ISecurityMasterBulkIngestService` with `IngestFromCsvAsync(Stream, BulkIngestOptions)` and `IngestFromProviderAsync(string providerId, BulkIngestOptions)`.
- Implement CSV parser for common exchange listing formats (NASDAQ, NYSE, LSE basic export).
- Implement `PolygonSecurityMasterIngestProvider` that pages through `/v3/reference/tickers` and maps to `SecurityMasterCommand.CreateSecurity`.
- Add CLI command `--security-master-ingest --provider polygon --exchange XNAS` wired through `CommandDispatcher`.
- Emit progress events through `EventPipeline` so the web dashboard can poll ingest status.

### Acceptance Criteria

- A NASDAQ CSV listing of 500 symbols ingests in under 60 seconds on a local Postgres instance.
- Duplicate symbols are detected and skipped (idempotent).
- Ingest status is visible via `/api/security-master/ingest/status`.

---

## Idea 5 — Golden Record Conflict Resolution

### Problem

When multiple providers contribute data for the same instrument (e.g., Polygon and Alpaca both return metadata for `AAPL`), there is no reconciliation or confidence-scoring mechanism. The last writer wins, and data quality degrades silently.

### Proposed Work

- Add `GoldenRecordResolver` service that receives `SecurityMasterConflict` (same instrument, differing field values from different providers) and applies a configurable resolution strategy: `PreferProvider`, `MostRecent`, `HighestConfidence`, `ManualReview`.
- Add `SecurityMasterConflict` domain type to `SecurityMasterEvents.fs` and persist via the event store.
- Extend `SecurityMasterProjectionService` to detect and emit conflicts rather than silently overwrite.
- Add `/api/security-master/conflicts` endpoint returning open conflicts with field-level diff.
- Add `/api/security-master/conflicts/{id}/resolve` endpoint accepting resolution strategy.
- Surface conflict count as a badge in the web dashboard Security Master panel.

### Acceptance Criteria

- Two providers with differing `Name` for the same FIGI produce a stored `SecurityMasterConflict`.
- Resolving via `PreferProvider` updates the golden record projection and closes the conflict.
- Unresolved conflict count is visible in the dashboard.

---

## Idea 6 — WPF Security Master Browser

### Problem

The WPF desktop app has no Security Master surface. Operators running the WPF build cannot search, browse, or edit instrument definitions from the desktop.

### Proposed Work

- Add `SecurityMasterViewModel` inheriting from `BindableBase` with `SearchQuery`, `Results` (observable collection), `SelectedSecurity`, `LoadingState` properties and `SearchCommand`, `RefreshCommand`, `OpenDetailCommand`.
- Add `SecurityMasterPage.xaml` / `SecurityMasterPage.xaml.cs` (thin code-behind, `DataContext = ViewModel`).
- Add detail panel showing all `SecurityEconomicDefinition` fields, corporate action timeline, and trading parameters.
- Register page in `Pages.cs` and add navigation entry to `MainPage.xaml` sidebar.
- Add `SecurityMasterServiceTests` in `Meridian.Wpf.Tests` for `SecurityMasterViewModel` command lifecycle.

### Acceptance Criteria

- `SecurityMasterPage` loads and displays search results from the API.
- Selecting a result opens a detail panel with all fields populated.
- ViewModel inherits `BindableBase`; no business logic in code-behind.
- All tests pass on Windows with `EnableFullWpfBuild=true`.

---

## Sequencing

| Order | Idea | Reason |
|-------|------|--------|
| 1 | Bond Term Richness | Data model foundation; enables fixed-income workflows downstream |
| 2 | Trading Parameters | Correctness for execution + backtesting; self-contained schema change |
| 3 | Corporate Action Events | Requires stable domain model; feeds directly into backtest correctness |
| 4 | Exchange Bulk Ingest | Needs stable domain model; enables broad coverage |
| 5 | Golden Record Conflict Resolution | Needs multi-provider ingest to generate meaningful conflicts |
| 6 | WPF Security Master Browser | UI surface on top of completed backend capabilities |

---

## Reference

- `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`
- `src/Meridian.FSharp/Domain/SecurityMasterEvents.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`
- `src/Meridian.Wpf/ViewModels/` (WPF MVVM patterns)
- [`ROADMAP.md`](../status/ROADMAP.md) — Wave 6
- [`FEATURE_INVENTORY.md`](../status/FEATURE_INVENTORY.md) — Security Master section
