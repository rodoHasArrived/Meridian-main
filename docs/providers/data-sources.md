# Data Sources Reference

**Last Updated:** 2026-03-31
**Version:** 1.7.0

This document catalogs the current provider inventory in Meridian across historical backfill, streaming, symbol search, and reference-data workflows.

For the current validation baseline for Polygon, NYSE, Interactive Brokers, and StockSharp, see [Provider Confidence Baseline](provider-confidence-baseline.md). That document is the source of truth for what Meridian validates offline today versus what still requires manual vendor/runtime checks.

---

## Historical Providers

| Provider | Status | Coverage | Free Tier | Implementation |
|----------|--------|----------|-----------|----------------|
| Alpaca | Implemented | US equities, ETFs | Requires credentials | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaHistoricalDataProvider.cs` |
| Yahoo Finance | Implemented | Global equities, ETFs, indices, forex, crypto | Unofficial API | `src/Meridian.Infrastructure/Adapters/YahooFinance/YahooFinanceHistoricalDataProvider.cs` |
| Stooq | Implemented | Global equities, indices, forex, commodities | Unlimited | `src/Meridian.Infrastructure/Adapters/Stooq/StooqHistoricalDataProvider.cs` |
| Nasdaq Data Link | Implemented | Alternative data and macro datasets | Limited | `src/Meridian.Infrastructure/Adapters/NasdaqDataLink/NasdaqDataLinkHistoricalDataProvider.cs` |
| Tiingo | Implemented | US and international equities, ETFs, mutual funds | Requires credentials | `src/Meridian.Infrastructure/Adapters/Tiingo/TiingoHistoricalDataProvider.cs` |
| Finnhub | Implemented | Global securities and company reference data | Requires credentials | `src/Meridian.Infrastructure/Adapters/Finnhub/FinnhubHistoricalDataProvider.cs` |
| Alpha Vantage | Implemented | Equities, indices, forex, crypto | Requires credentials | `src/Meridian.Infrastructure/Adapters/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` |
| Polygon | Implemented | US equities, options, forex, crypto | Requires credentials | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonHistoricalDataProvider.cs` |
| Interactive Brokers | Requires `IBAPI` | US equities, options, futures, forex | With account | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs` |
| StockSharp | Requires `STOCKSHARP` | Connector-dependent multi-exchange coverage | With account/license | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpHistoricalDataProvider.cs` |
| Twelve Data | Implemented | Equities, ETFs, forex, crypto | Requires credentials | `src/Meridian.Infrastructure/Adapters/TwelveData/TwelveDataHistoricalDataProvider.cs` |
| FRED | Implemented | Economic time series mapped to daily bars | Free API key | `src/Meridian.Infrastructure/Adapters/Fred/FredHistoricalDataProvider.cs` |
| Synthetic Historical | Implemented | Deterministic offline bars, quotes, trades, auctions, dividends, splits | No credentials | `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticHistoricalDataProvider.cs` |
| Composite Provider | Implemented | Multi-source failover | N/A | `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs` |

---

## Streaming And Hybrid Providers

| Provider | Status | Coverage | Notes | Implementation |
|----------|--------|----------|-------|----------------|
| Alpaca | Implemented | US equities | REST + WebSocket workflow | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs` |
| Interactive Brokers | Implemented with `IBAPI` | Global multi-asset | Real TWS / Gateway access requires the official `IBApi` vendor path; non-`IBAPI` builds stay on simulation/runtime-guidance paths | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs` |
| Polygon | Implemented | Equities, options, forex, crypto | WebSocket streaming for trades, quotes, and aggregates | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` |
| StockSharp | Implemented with `STOCKSHARP` | Connector-dependent multi-exchange coverage | Connector-runtime path; actual coverage depends on adapter, package surface, and entitlement | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` |
| NYSE Streaming | Implemented | NYSE-focused feed | Unified client backed by `NYSEDataSource` | `src/Meridian.Infrastructure/Adapters/NYSE/NyseMarketDataClient.cs` |
| Synthetic Market Data | Implemented | Offline stocks and ETFs | Deterministic streaming, history, and symbol search | `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticMarketDataClient.cs` |

---

## Symbol Search And Reference Providers

| Provider | Status | Coverage | Notes | Implementation |
|----------|--------|----------|-------|----------------|
| Alpaca Symbol Search | Implemented | US equities | Trading status-aware lookup | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProviderRefactored.cs` |
| Finnhub Symbol Search | Implemented | 60K+ global | Company profile and symbol lookup | `src/Meridian.Infrastructure/Adapters/Finnhub/FinnhubSymbolSearchProviderRefactored.cs` |
| Polygon Symbol Search | Implemented | US equities | Filterable symbol search | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonSymbolSearchProvider.cs` |
| OpenFIGI | Implemented | Global instruments | FIGI normalization and mapping | `src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiClient.cs` |
| StockSharp Symbol Search | Requires `STOCKSHARP` | Connector-dependent multi-asset search | Uses connector-native security lookup | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpSymbolSearchProvider.cs` |
| Synthetic Symbol Search | Implemented | Offline stock and ETF catalog | Good for fixture mode and demos | `src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticMarketDataClient.cs` |

---

## Validation Baseline

| Provider | Offline / CI evidence | Manual follow-on checks |
|----------|------------------------|-------------------------|
| Polygon | Replay fixtures plus `PolygonRecordedSessionReplayTests`, parsing tests, and subscription tests | Live credentials, entitlements, and plan-tier/runtime confirmation |
| NYSE Streaming | Shared-lifecycle, parser, and client tests around `NyseMarketDataClient` / `NYSEDataSource` | Credentialed auth/connectivity and entitlement checks |
| Interactive Brokers | Runtime-guidance tests, order fixtures, and compile-only smoke build | Official vendor `IBApi` path with local TWS/Gateway validation |
| StockSharp | Subscription, connector-factory, and message-conversion capability tests | `EnableStockSharp=true`, connector package installation, and connector-specific local runtime validation |

---

## Provider Notes

### Composite Provider

`CompositeHistoricalDataProvider` manages multiple historical sources with automatic failover, priority ordering, and rate-limit rotation. Use it when you want resilient backfill rather than a single-provider dependency.

### FRED

FRED is not an equities price feed. Meridian normalizes FRED observations into synthetic daily bars so macro series can participate in research and backtesting workflows that expect bar-shaped data.

### Synthetic Providers

The synthetic providers are intended for offline development, demos, deterministic tests, and fixture-mode desktop workflows. They are not substitutes for live or vendor-sourced market data.

### StockSharp

StockSharp support is connector-dependent and gated by the `STOCKSHARP` build path. Available coverage depends on the configured connector, installed package surfaces, and any applicable StockSharp licensing or crowdfunding access. The default CI-friendly path validates guidance and metadata without claiming live connector availability.

### Interactive Brokers

Interactive Brokers support has three distinct modes that operators should not conflate: the real `IBAPI` vendor-DLL/project path for live TWS/Gateway use, the `EnableIbApiSmoke=true` compile-only smoke path for automation, and the non-`IBAPI` simulation/runtime-guidance path that keeps the provider visible without exposing real broker connectivity. The repo validates the latter two paths offline; real connectivity still requires the official vendor path and local runtime checks.

---

## Rate Limit Quick Reference

| Provider | Rate Limit | Suggested Use |
|----------|------------|---------------|
| Yahoo Finance | ~2000/hour | Broad free fallback for daily history |
| Stooq | Respectful usage | Free daily fallback |
| Alpaca | 200/min historical | Recent US history with credentials |
| Tiingo | 50/hour free tier | Adjusted data and corporate actions |
| Finnhub | 60/min | History plus company reference data |
| Alpha Vantage | 25/day free tier | Narrow, targeted lookups only |
| Polygon | 5/min free tier | Premium-quality data, low-volume free use |
| FRED | 120/min | Macro and economic series |
| Twelve Data | 8/min free tier | International OHLCV fallback |
| Synthetic | None | Local-only workflows |

---

## Related Documentation

- [Provider Comparison](provider-comparison.md) - Side-by-side selection guidance
- [Backfill Guide](backfill-guide.md) - Historical data workflows
- [Alpaca Setup](alpaca-setup.md) - Alpaca credential setup
- [Interactive Brokers Setup](interactive-brokers-setup.md) - IB TWS / Gateway setup
- [Environment Variables](../reference/environment-variables.md) - Credential and configuration reference
