# Options Functionality Roadmap

**Created:** 2026-04-06  
**Status:** In Progress  
**Owner:** Platform Team

---

## 1. Current State Assessment

### 1.1 Domain Models (✅ Complete)

All core domain primitives for options are implemented and production-ready:

| Type | Location | Status |
|------|----------|--------|
| `OptionContractSpec` | `Meridian.Contracts/Domain/Models/` | ✅ Full model with OCC symbol, Greeks, strike, expiry, right, style |
| `OptionQuote` | `Meridian.Contracts/Domain/Models/` | ✅ Bid/ask/mid/IV/OI per contract |
| `OptionTrade` | `Meridian.Contracts/Domain/Models/` | ✅ Tick-level option trade print |
| `OptionChainSnapshot` | `Meridian.Contracts/Domain/Models/` | ✅ Typed `Calls`/`Puts` collections keyed by strike |
| `GreeksSnapshot` | `Meridian.Contracts/Domain/Models/` | ✅ Delta/Gamma/Theta/Vega/Rho + IV |
| `OptionRight` | `Meridian.Contracts/Domain/Enums/` | ✅ Call / Put |
| `OptionStyle` | `Meridian.Contracts/Domain/Enums/` | ✅ American / European |
| `OptionContractSpec.ToOccSymbol()` | `OptionContractSpec.cs:114` | ✅ Standard 21-char OCC symbol generator |
| `OptionContractSpec.DaysToExpiration()` | `OptionContractSpec.cs:127` | ✅ Calendar-day DTE helper |
| `OptionContractSpec.IsExpired()` | `OptionContractSpec.cs:133` | ✅ Expiry check |

### 1.2 Provider Interfaces (✅ Complete)

| Interface | Location | Status |
|-----------|----------|--------|
| `IOptionsChainProvider` | `Meridian.ProviderSdk/IOptionsChainProvider.cs` | ✅ Three methods: `GetExpirationsAsync`, `GetChainSnapshotAsync`, `GetContractDetailsAsync` |
| `IOptionsChainProvider.GetExpirationsAsync` | | ✅ Returns sorted future expiration dates for a symbol |
| `IOptionsChainProvider.GetChainSnapshotAsync` | | ✅ Returns full chain snapshot with optional strike-range filter |
| `IOptionsChainProvider.GetContractDetailsAsync` | | ✅ Returns single contract by OCC symbol |

### 1.3 Provider Implementations

| Provider | File | Status |
|----------|------|--------|
| `SyntheticOptionsChainProvider` | `Meridian.Infrastructure/Adapters/Synthetic/` | ✅ **New** — deterministic BSM-priced options for offline dev/test/backtest |
| `PolygonOptionsChainProvider` | `Meridian.Infrastructure/Adapters/Polygon/` | 🟡 **New scaffold** — HTTP client wired, JSON DTOs defined, parsing not yet validated against live API |
| `AlpacaOptionsChainProvider` | `Meridian.Infrastructure/Adapters/Alpaca/` | 🟡 **New scaffold** — HTTP client wired and JSON DTOs defined, but current chain mapping still cannot materialize a domain-valid `OptionChainSnapshot` because the mapper does not yet enrich a non-zero underlying spot price and still needs live-response validation |

### 1.4 Backtesting Engine Options Support

| Capability | Status |
|------------|--------|
| `IBacktestContext.GetOptionChainAsync` | ✅ **New** — strategy code can fetch option chains during replay |
| `IBacktestContext.GetNearestExpirationAsync` | ✅ **New** — find nearest valid expiration given min DTE |
| `BacktestContext` implementation | ✅ **New** — delegates to injected `IOptionsChainProvider?` |
| `BacktestEngine` wiring | ✅ **New** — `IOptionsChainProvider?` optional ctor param threaded to `BacktestContext` |
| Option order fill simulation | 🔴 Not implemented — fills use equity fill model regardless of option type |
| Option P&L tracking | 🔴 Not implemented — `SimulatedPortfolio` treats option positions like equity |
| Options-aware margin/risk | 🔴 Not implemented — no notional/greeks-based margin model |

### 1.5 Existing Options Strategy (Reference Implementation)

`CoveredCallOverwriteStrategy` (`Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite/`) provides a complete worked example of an options strategy in the current codebase. It uses BSM internally for sizing and demonstrates the intended IBacktestContext patterns.

### 1.6 OptionsChainService (Application Layer)

`Meridian.Application/Services/OptionsChainService.cs` wraps `IOptionsChainProvider` for REST API exposure. Currently resolves the first registered `IOptionsChainProvider` from DI. With the new DI registration in `ProviderFeatureRegistration`, the priority order is:

1. **AlpacaOptionsChainProvider** (requires `ALPACA_KEY_ID` + `ALPACA_SECRET_KEY`)
2. **PolygonOptionsChainProvider** (requires `POLYGON_API_KEY`)
3. **SyntheticOptionsChainProvider** (always available — fallback)

`OptionsChainService.FetchConfiguredChainsAsync(...)` already knows how to walk `DerivativesConfig.Underlyings`, expiration filters, and strike-range settings. However, there is not yet a hosted/background collector that invokes it on `ChainSnapshotIntervalSeconds`, so configured underlyings are not polled unless a caller manually triggers refresh/fetch flows.

### 1.7 REST API Endpoints

`OptionsEndpoints.cs` exposes:

| Route | Description |
|-------|-------------|
| `GET /api/options/{symbol}/expirations` | Available expiration dates |
| `GET /api/options/{symbol}/chain` | Full chain snapshot (+ optional `expiration`, `strikeRange` query params) |
| `GET /api/options/contract/{occSymbol}` | Single contract details |

### 1.8 WPF UI

`OptionsViewModel.cs` and `OptionsPage.xaml` provide a UI for browsing option chains. Currently wired to `OptionsChainService`.

### 1.9 Paper Trading / Execution

| Capability | Status |
|------------|--------|
| `OptionContractSpec` on `OrderRequest` (Execution.Sdk) | 🔴 Not added — option orders use the equity symbol only |
| `PaperTradingGateway` BSM fill pricing | 🔴 Not implemented — fills use `ScaffoldMarketFillPrice` constant |
| Option exercise/assignment simulation | 🔴 Not implemented |
| `IRiskValidator` greeks-based rules | 🔴 Not implemented — position limit rule is quantity-based only |

---

## 2. Remaining Work by Priority

### Phase 1 — Provider Validation (High, ~1–2 sprints)

**Goal:** Validate `PolygonOptionsChainProvider` and `AlpacaOptionsChainProvider` against live APIs.

- [ ] **P1.1** Write integration tests for `PolygonOptionsChainProvider` using recorded HTTP fixtures (similar to `PolygonRecordedSessionReplayTests`)
- [ ] **P1.2** Write integration tests for `AlpacaOptionsChainProvider` using recorded HTTP fixtures
- [ ] **P1.3** Validate Polygon v3 snapshot response shape against `PolygonSnapshotResult` DTO — map all fields
- [ ] **P1.4** Validate Alpaca v1beta1 snapshot response shape against `AlpacaSnapshotResult` DTO — map all fields
- [ ] **P1.4a** Fix Alpaca chain snapshot construction so `OptionChainSnapshot` can be created with a domain-valid `UnderlyingPrice` instead of the current hard-coded `0m`
- [ ] **P1.4b** Replace the naive Alpaca OCC ticker decoding in `MapToChainSnapshot` with deterministic contract parsing so calls/puts and strikes are reconstructed correctly for all underlyings
- [ ] **P1.5** Map Greek fields correctly (Polygon: `greeks.delta`, Alpaca: `greeks.delta`)
- [ ] **P1.6** Handle Polygon pagination (`next_url` cursor) in `GetChainSnapshotAsync` for large chains
- [ ] **P1.7** Handle Alpaca next-page token in `GetChainSnapshotAsync`
- [ ] **P1.8** Add IV surface / term structure support to `PolygonOptionsChainProvider` via `GetVolatilitySurfaceAsync`
- [ ] **P1.9** Register providers in `DataSourceRegistry` attribute discovery path (currently hardcoded in `ProviderFeatureRegistration`)

### Phase 2 — Backtest Fill Model for Options (High, ~1 sprint)

**Goal:** Simulate realistic option fills in the backtest engine.

- [ ] **P2.1** Add `OptionContractSpec?` to `Meridian.Backtesting.Sdk.OrderRequest` 
- [ ] **P2.2** Implement `OptionsFillModel` in `Meridian.Backtesting/FillModels/` that prices fills using BSM mid-price at current IV
- [ ] **P2.3** Route option orders through `OptionsFillModel` in `BacktestEngine.ProcessFills()`
- [ ] **P2.4** Track option positions separately in `SimulatedPortfolio` (notional = `quantity × 100 × premium`)
- [ ] **P2.5** Implement option expiry processing in `BacktestEngine`: at expiry, auto-exercise ITM options or expire worthless OTM
- [ ] **P2.6** Add portfolio P&L attribution for option positions (`premium_collected`, `realized_gain_loss`)
- [ ] **P2.7** Handle early assignment for American-style options on ex-dividend dates (use `ICorporateActionProvider`)

### Phase 3 — Execution / Paper Trading Options (Medium, ~1–2 sprints)

**Goal:** Support option orders in paper trading mode.

- [ ] **P3.1** Add `OptionContractSpec?` to `Meridian.Execution.Sdk.OrderRequest`
- [ ] **P3.2** Update `PaperTradingGateway.SimulateFillAsync` to use live chain mid-price for option fills when `IOptionsChainProvider` is injected
- [ ] **P3.3** Implement option expiry handling in `PaperTradingPortfolio` (daily sweep on expiry dates)
- [ ] **P3.4** Implement greeks-based position sizing in `PositionLimitRule` (delta-equivalent notional limit)
- [ ] **P3.5** Add `IBrokerageGateway` option order support to `AlpacaBrokerageGateway` (POST /v2/orders with option symbol)
- [ ] **P3.6** Add option position reconciliation to `PositionReconciliationService`

### Phase 4 — Streaming Option Data (Medium, ~1–2 sprints)

**Goal:** Real-time option quote streaming for live strategies.

- [ ] **P4.1** Extend `IMarketDataClient` interface to support `SubscribeToOptionQuotesAsync`
- [ ] **P4.2** Implement streaming option quotes in `AlpacaMarketDataClient` (Alpaca options WebSocket stream)
- [ ] **P4.3** Implement streaming option quotes in `PolygonMarketDataClient` (Polygon `O.*` subscription)
- [ ] **P4.4** Add `OptionDataCollector` processing for live quote events (currently exists but may not receive WS events)
- [ ] **P4.5** Publish `OptionQuote` events through `EventPipeline` to storage sinks (JSONL + Parquet)
- [ ] **P4.6** Add live IV calculation service that maintains per-symbol IV surface from streaming quotes
- [ ] **P4.7** Add a hosted/background options chain collector that polls `OptionsChainService.FetchConfiguredChainsAsync(...)` on `DerivativesConfig.ChainSnapshotIntervalSeconds`
- [ ] **P4.8** Wire background polling to `DerivativesConfig.Enabled`, `CaptureChainSnapshots`, configured `Underlyings`, and hot-reload so cached chains and tracked underlyings populate without manual refresh calls

### Phase 5 — Symbol / Chain Discovery Improvements (Low, ~0.5 sprint)

**Goal:** Better UX for discovering option symbols.

- [ ] **P5.1** Add `SearchOptionsAsync(string query)` to `IOptionsChainProvider` for OCC symbol lookup
- [ ] **P5.2** Implement `PolygonOptionsChainProvider.SearchOptionsAsync` via `/v3/reference/options/contracts?search=`
- [ ] **P5.3** Wire options symbol search into `SymbolSearchService` as a separate `OptionSymbolSearchProvider`
- [ ] **P5.4** Add options to the WPF symbol-add flow (currently only equity symbols)
- [ ] **P5.5** Cache chain expirations per symbol in `OptionsChainService` with TTL = 1 hour

### Phase 6 — Risk and Compliance (Low, ~1 sprint)

**Goal:** Production-grade risk controls for option positions.

- [ ] **P6.1** Implement `OptionsRiskValidator` implementing `IRiskRule` with configurable:
  - Max naked short delta exposure
  - Max short vega (volatility risk)
  - Max portfolio theta per day
- [ ] **P6.2** Add greeks aggregation to `AggregatePortfolioService` across all option positions
- [ ] **P6.3** Implement margin model for option strategies (`RegTMarginModel` extension for covered calls, spreads)
- [ ] **P6.4** Add daily options expiry sweep to `TradingCalendar` service

---

## 3. Testing Coverage

| Area | Current Tests | Target |
|------|---------------|--------|
| `SyntheticOptionsChainProvider` | ✅ 17 tests (new) | ✅ Done |
| `PolygonOptionsChainProvider` | 🔴 0 tests | HTTP fixture replay tests |
| `AlpacaOptionsChainProvider` | 🔴 0 tests | HTTP fixture replay tests |
| `BacktestContext.GetOptionChainAsync` | 🔴 0 tests | Unit tests with `SyntheticOptionsChainProvider` |
| `CoveredCallOverwriteStrategy` | ✅ Existing tests | Already covered |
| Option fill model | 🔴 0 tests | Fill model unit tests |
| Option expiry simulation | 🔴 0 tests | Integration tests |

---

## 4. Architecture Notes

### Provider Registration Priority

```
AlpacaOptionsChainProvider  (Priority=8, requires credentials)
PolygonOptionsChainProvider (Priority=15, requires credentials)
SyntheticOptionsChainProvider (Priority=200, always available)
```

`OptionsChainService` (Application layer) uses `GetService<IOptionsChainProvider>()` which resolves the **first** registration — currently `AlpacaOptionsChainProvider`. When credentials are absent, the live provider will throw or return empty results; the intent is that `OptionsChainService` falls back to the `Synthetic` provider if the live provider fails.

**TODO:** Implement explicit fallback chain in `OptionsChainService` using `IEnumerable<IOptionsChainProvider>` (registered in DI for all three) with health-check gating.

### Current Collection Gap

`OptionsChainService.FetchConfiguredChainsAsync(...)` is present, but there is no host-level scheduler/collector invoking it today. As a result:

- `/api/options/underlyings` only reflects symbols that have already been fetched manually
- `/api/options/chains/{underlyingSymbol}` returns cached data only after an explicit refresh/fetch path populates the collector
- `DerivativesConfig.CaptureChainSnapshots` and `ChainSnapshotIntervalSeconds` describe intended behavior that is not yet wired into a running background loop

### Backtesting Integration

```
BacktestEngine
  └── BacktestContext(optionsProvider: IOptionsChainProvider?)
        ├── GetOptionChainAsync()  → IOptionsChainProvider.GetChainSnapshotAsync()
        └── GetNearestExpirationAsync() → IOptionsChainProvider.GetExpirationsAsync()
```

Strategies call `ctx.GetOptionChainAsync("SPY", nextExpiry)` to fetch live chain data at each bar. For backtesting with the `SyntheticOptionsChainProvider`, chain prices are always consistent with the underlying's synthetic price (BSM-priced against the current bar's close).

### JSON Source Generator Compliance (ADR-014)

`PolygonOptionsChainProvider` and `AlpacaOptionsChainProvider` include private `JsonSerializerContext` classes (`PolygonOptionsJsonContext`, `AlpacaOptionsJsonContext`) with `[JsonSerializable]` annotations for all DTO types. These satisfy the ADR-014 no-reflection serialization requirement.

---

## 5. Quick Wins (can be done now, < 1 day each)

1. **Add `SyntheticOptionsChainProvider` to OptionsChainService fallback** — wire `IEnumerable<IOptionsChainProvider>` for resilience
2. **Connect `BacktestEngine` to DI-injected `IOptionsChainProvider`** in `BackfillModeRunner`/`BacktestStudioRunOrchestrator`
3. **Extend `OptionsViewModel`** to show IV, greeks columns in the WPF chain table
4. **Add options data to `FixtureDataService`** so the UI renders options pages in fixture/demo mode

---

*This document should be updated as phases are completed. Use `- [x]` checkboxes to track progress.*
