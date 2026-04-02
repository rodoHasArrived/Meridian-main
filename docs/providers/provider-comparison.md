# Data Provider Comparison Guide

**Last Updated:** 2026-03-31
**Version:** 1.7.0

This guide compares the providers currently implemented in Meridian so you can choose the right mix for development, research, and production workflows.

For what the repo validates today versus what still requires manual runtime checks, see [Provider Confidence Baseline](provider-confidence-baseline.md).

---

## Quick Selection

### Streaming Providers

| Provider | Status | Setup | Free Tier | Best For | Notes |
|----------|--------|-------|-----------|----------|-------|
| Alpaca | Implemented | Easy | IEX feed | Development and basic US streaming | Simple setup, no Level 2 depth |
| Interactive Brokers | Implemented with `IBAPI` | Complex | Cboe One + IEX | Professional trading and L2 depth | Real TWS / Gateway access requires the official `IBApi` surface; non-`IBAPI` builds stay on simulation or explicit runtime guidance |
| Polygon | Implemented | Medium | 5 calls/min free tier | Aggregated real-time feeds | Strong streaming quality, premium plans scale better |
| StockSharp | Implemented with `STOCKSHARP` | Medium | Varies by connector | Multi-exchange connectivity | Coverage depends on connector, package surface, and license |
| NYSE Streaming | Implemented | Medium | Subscription | NYSE-specific feed workflows | Backed by `NYSEDataSource` |
| Synthetic | Implemented | Very Low | Unlimited | Offline development and demos | Deterministic, not live market data |

### Historical Providers

| Provider | Status | Free Tier | Rate Limit | Coverage | Best For |
|----------|--------|-----------|------------|----------|----------|
| Alpaca | Implemented | With account | 200/min | US equities | Recent US history plus vendor continuity |
| Yahoo Finance | Implemented | Unlimited | ~2000/hour | Global equities, ETFs, indices, forex, crypto | Broad no-auth fallback |
| Stooq | Implemented | Unlimited | Respectful | Global equities, indices, forex, commodities | Free daily fallback |
| Nasdaq Data Link | Implemented | Limited | 300/10s | Alternative data and macro datasets | Research datasets and macro series |
| Tiingo | Implemented | 1000/day, 50/hour | 50/hour | US and international equities | Adjusted history and corporate actions |
| Finnhub | Implemented | 60/min | 60/min | Global securities | History plus company reference data |
| Alpha Vantage | Implemented | 25/day | 5/min | Equities, indices, forex, crypto | Narrow intraday lookups |
| Polygon | Implemented | 5/min | 5/min | US equities, options, forex, crypto | High-quality premium-oriented history |
| Interactive Brokers | Requires `IBAPI` | With account | Strict pacing | US and global multi-asset | Institutional workflow continuity once the official `IBApi` path is enabled; smoke builds are compile-only |
| StockSharp | Requires `STOCKSHARP` | Connector-dependent | Connector-dependent | Multi-exchange | Depends on configured connector plus installed StockSharp package surfaces |
| Twelve Data | Implemented | 800/day, 8/min | 8/min | Equities, ETFs, forex, crypto | Credentialed international fallback |
| FRED | Implemented | Free API key | 120/min | Economic time series | Macro overlays and research inputs |
| Synthetic | Implemented | Unlimited | None | Deterministic offline scenarios | Testing, fixtures, demos |

### Symbol Search And Reference

| Provider | Status | Coverage | Best For |
|----------|--------|----------|----------|
| Alpaca Symbol Search | Implemented | US equities | Broker-aligned US symbol lookup |
| Finnhub Symbol Search | Implemented | Global securities | Company and symbol discovery |
| Polygon Symbol Search | Implemented | US equities | Filterable symbol search |
| OpenFIGI | Implemented | Global instruments | Identifier normalization |
| StockSharp Symbol Search | Requires `STOCKSHARP` | Connector-dependent multi-asset | Connector-native security lookup |
| Synthetic Symbol Search | Implemented | Offline stock and ETF catalog | Fixture-mode and demo workflows |

---

## Provider Profiles

### Alpaca

**Best For:** Development, simple US market data collection, and an easy first provider.

**Strengths**
- Straightforward credential model
- Streaming plus historical support in the same provider family
- Good developer ergonomics for local setup

**Tradeoffs**
- Free streaming is IEX-only, not a full consolidated feed
- No Level 2 depth
- US-focused

**Implementation**
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaHistoricalDataProvider.cs`

### Interactive Brokers

**Best For:** Professional trading workflows, Level 2 depth, and broker-aligned production setups.

**Strengths**
- High-quality market data
- Supports streaming, historical, and broader multi-asset workflows
- Strong fit for real trading operations

**Tradeoffs**
- Higher setup complexity
- Requires TWS or Gateway plus the official `IBApi` surface behind `IBAPI`
- Strict pacing rules

**Operator Notes**
- Meridian's non-`IBAPI` path keeps the provider visible through `IBSimulationClient` and targeted runtime guidance, but it does not provide real broker connectivity.
- `EnableIbApiSmoke=true` is intended only for compile verification of the gated code path.
- Use [Interactive Brokers Setup](interactive-brokers-setup.md) for the supported vendor-DLL/project path and smoke-build path.
- Treat live TWS/Gateway connectivity as a manual validation step, not something proven by the default CI path.

**Implementation**
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs`

### Polygon

**Best For:** Aggregated real-time streaming plus premium-quality historical market data.

**Strengths**
- Streaming support for trades, quotes, and aggregates
- Historical coverage extends beyond simple daily bars
- Good fit for data-heavy research and premium production setups

**Tradeoffs**
- Free tier is very limited
- Better experience typically requires a paid plan

**Operator Notes**
- Meridian validates Polygon primarily through committed replay fixtures and parser/subscription tests.
- Live websocket behavior still depends on credentials, feed selection, and Polygon plan entitlements.

**Implementation**
- `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/Polygon/PolygonHistoricalDataProvider.cs`
- `src/Meridian.Infrastructure/Adapters/Polygon/PolygonSymbolSearchProvider.cs`

### StockSharp

**Best For:** Connector-based access to many venues and asset classes from one integration surface.

**Strengths**
- Broad connector ecosystem
- Streaming, historical, and symbol-search paths
- Good fit when a specific supported connector matches your venue needs

**Tradeoffs**
- Build and licensing complexity
- Coverage depends on connector configuration rather than one fixed API

**Operator Notes**
- StockSharp is a connector-runtime integration, so runtime behavior and historical availability vary by adapter, package availability, and venue entitlements.
- Unsupported connector or missing-package paths should be treated as setup/configuration issues and resolved through [StockSharp Connector Guide](stocksharp-connectors.md).
- The default repo baseline validates connector metadata and guidance without claiming that every named connector is available in the current build.

### NYSE Streaming

**Best For:** NYSE-focused streaming workflows where direct exchange semantics matter.

**Strengths**
- Unified trade, quote, and depth lifecycle through `NyseMarketDataClient`
- Dedicated parser and lifecycle tests for companion subscriptions and mixed feed behavior
- Exchange-oriented path separate from aggregated feeds

**Tradeoffs**
- Requires NYSE credentials and feed entitlements
- Level 2 depth expectations depend on feed tier

**Operator Notes**
- Meridian validates NYSE behavior offline through lifecycle and parser tests around `NYSEDataSource`.
- Credentialed websocket and REST behavior still need manual runtime verification in operator environments.

**Implementation**
- `src/Meridian.Infrastructure/Adapters/NYSE/NyseMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs`

**Implementation**
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpHistoricalDataProvider.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpSymbolSearchProvider.cs`
- `docs/providers/stocksharp-connectors.md`

### Yahoo Finance

**Best For:** Broad, no-auth historical fallback coverage.

**Strengths**
- No credentials required
- Large global instrument surface
- Useful as a resilience layer in composite backfill flows

**Tradeoffs**
- Unofficial API
- No live streaming
- Daily-history-oriented workflow

**Implementation**
- `src/Meridian.Infrastructure/Adapters/YahooFinance/YahooFinanceHistoricalDataProvider.cs`

### Tiingo

**Best For:** Adjusted historical data and corporate actions.

**Strengths**
- Clean adjusted-data workflow
- Useful dividend and split support
- Good complement to Yahoo Finance and Alpaca

**Tradeoffs**
- Free tier is rate limited
- Historical only

**Implementation**
- `src/Meridian.Infrastructure/Adapters/Tiingo/TiingoHistoricalDataProvider.cs`

### Twelve Data

**Best For:** Credentialed international OHLCV coverage.

**Strengths**
- Covers equities, ETFs, forex, and crypto
- Useful fallback when broader international data is needed

**Tradeoffs**
- Free tier is rate limited
- Historical only

**Implementation**
- `src/Meridian.Infrastructure/Adapters/TwelveData/TwelveDataHistoricalDataProvider.cs`

### FRED

**Best For:** Macro and economic data in research or backtesting workflows.

**Strengths**
- Strong macro coverage
- Useful for rates, inflation, GDP, labor, and other economic overlays

**Tradeoffs**
- Not an equities price feed
- Series-ID workflow differs from ticker-centric providers

**Implementation**
- `src/Meridian.Infrastructure/Adapters/Fred/FredHistoricalDataProvider.cs`

### Synthetic

**Best For:** Offline development, fixture mode, demos, and deterministic testing.

**Strengths**
- No credentials or external network dependency
- Supports streaming, historical, and symbol-search workflows
- Deterministic outputs help repeatable tests and UI demos

**Tradeoffs**
- Not live market data
- Not appropriate for production trading decisions

**Implementation**
- `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticMarketDataClient.cs`
- `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticHistoricalDataProvider.cs`

---

## Feature Matrix

| Provider | Trades | Quotes | Bars | L2 Depth | Symbol Search | Corporate Actions |
|----------|--------|--------|------|----------|---------------|-------------------|
| Alpaca | Yes | Yes | Yes | No | Yes | Yes |
| Interactive Brokers | Yes | Yes | Yes | Yes | Connector workflow | Yes |
| Polygon | Yes | Yes | Yes | No | Yes | Yes |
| StockSharp | Yes | Yes | Yes | Yes | Yes | Connector-dependent |
| NYSE Streaming | Yes | Yes | No | Yes | No | No |
| Yahoo Finance | No | No | Yes | No | No | Adjusted close plus actions |
| Tiingo | No | No | Yes | No | No | Full dividends and splits |
| Finnhub | No | No | Yes | No | Yes | Dividends and splits |
| Stooq | No | No | Yes | No | No | No |
| Twelve Data | No | No | Yes | No | No | No |
| FRED | No | No | Yes | No | No | No |
| Synthetic | Yes | Yes | Yes | Yes | Yes | Yes |

---

## Recommended Combinations

### Local Development

- Streaming: `Synthetic` or `Alpaca`
- Historical: `Yahoo Finance` + `Stooq`
- Symbol search: `Synthetic`, `Alpaca`, or `Polygon`
- If you need to exercise provider-specific confidence without live credentials, prefer the replay/runtime-guidance baseline in [Provider Confidence Baseline](provider-confidence-baseline.md).

### Research And Backtesting

- Historical: `CompositeHistoricalDataProvider`
- High-quality adjusted data: `Tiingo`
- Broad fallback coverage: `Yahoo Finance` and `Stooq`
- Macro overlays: `FRED`

### Production-Oriented Trading

- Streaming: `Interactive Brokers`, `Polygon`, or `NYSE Streaming`
- Historical continuity: `Alpaca`, `Polygon`, or `Interactive Brokers` after the official `IBApi` path is enabled
- Reference search: `OpenFIGI` plus a broker or vendor symbol-search provider
- Validate vendor entitlements and local runtime dependencies separately before treating any compile-gated provider path as production-ready.

---

## Related Documentation

- [Data Sources Reference](data-sources.md) - Current provider inventory
- [Backfill Guide](backfill-guide.md) - Historical data workflows
- [Alpaca Setup](alpaca-setup.md) - Alpaca credentials and setup
- [Interactive Brokers Setup](interactive-brokers-setup.md) - IB TWS / Gateway setup
- [Configuration Guide](../HELP.md#configuration) - User-facing configuration overview

---

*Last Updated: 2026-03-21*
