> **AUTO-GENERATED — DO NOT EDIT**
> This file is generated automatically. Manual edits will be overwritten.
> See `docs/generated/README.md` for details on how generation works.

# Provider Registry

> Updated on 2026-03-27. Source of truth: `src/Meridian.Infrastructure/Adapters/`.
> For full documentation see [docs/providers/data-sources.md](../providers/data-sources.md).

This document lists all data providers available in Meridian.

## Real-Time Streaming Providers

| Provider | Class | Status | Notes |
|----------|-------|--------|-------|
| Alpaca Markets | `AlpacaMarketDataClient` | ✅ Active | US equities; REST + WebSocket |
| Interactive Brokers | `IBMarketDataClient` | ✅ Active (with `IBAPI`) | Global multi-asset; requires IBAPI vendor path for live use |
| Polygon.io | `PolygonMarketDataClient` | ✅ Active | Equities, options, forex, crypto WebSocket |
| NYSE | `NyseMarketDataClient` | ✅ Active | NYSE-focused feed via `NYSEDataSource` |
| StockSharp | `StockSharpMarketDataClient` | ✅ Active (with `STOCKSHARP`) | Connector-dependent multi-exchange |
| Synthetic | `SyntheticMarketDataClient` | ✅ Active | Deterministic offline streaming for development and tests |
| CppTrader | `CppTraderMarketDataClient` | ✅ Active | Native matching engine integration via `Meridian.Infrastructure.CppTrader` |
| Failover | `FailoverAwareMarketDataClient` | ✅ Active | Streaming failover wrapper |

## Historical Data Providers (Backfill)

| Provider | Class | Free Tier | Rate Limits |
|----------|-------|-----------|-------------|
| Yahoo Finance | `YahooFinanceHistoricalDataProvider` | Yes (unofficial API) | ~2000/hour |
| Stooq | `StooqHistoricalDataProvider` | Yes | Respectful usage |
| Alpaca | `AlpacaHistoricalDataProvider` | Requires credentials | 200/min |
| Tiingo | `TiingoHistoricalDataProvider` | Requires credentials | 50/hour free tier |
| Alpha Vantage | `AlphaVantageHistoricalDataProvider` | Requires credentials | 25/day free tier |
| Finnhub | `FinnhubHistoricalDataProvider` | Requires credentials | 60/min |
| Nasdaq Data Link | `NasdaqDataLinkHistoricalDataProvider` | Limited free tier | Varies |
| Polygon | `PolygonHistoricalDataProvider` | Requires credentials | 5/min free tier |
| Interactive Brokers | `IBHistoricalDataProvider` | With account (IBAPI) | Per IB limits |
| StockSharp | `StockSharpHistoricalDataProvider` | With account/license (`STOCKSHARP`) | Connector-dependent |
| Twelve Data | `TwelveDataHistoricalDataProvider` | Requires credentials | 8/min free tier |
| FRED | `FredHistoricalDataProvider` | Free API key | 120/min |
| Synthetic | `SyntheticHistoricalDataProvider` | No credentials | Unlimited local |
| Composite | `CompositeHistoricalDataProvider` | N/A | Multi-source failover |

## Symbol Search Providers

| Provider | Class | Status | Notes |
|----------|-------|--------|-------|
| Alpaca | `AlpacaSymbolSearchProviderRefactored` | ✅ Active | US equities, trading-status aware |
| Finnhub | `FinnhubSymbolSearchProviderRefactored` | ✅ Active | 60K+ global symbols |
| Polygon | `PolygonSymbolSearchProvider` | ✅ Active | US equities, filterable |
| OpenFIGI | `OpenFigiClient` | ✅ Active | Global FIGI normalization |
| StockSharp | `StockSharpSymbolSearchProvider` | ✅ Active (with `STOCKSHARP`) | Connector-native security lookup |
| Synthetic | `SyntheticMarketDataClient` | ✅ Active | Offline stock and ETF catalog |

## Brokerage Gateway Implementations

| Provider | Class | Notes |
|----------|-------|-------|
| Alpaca | `AlpacaBrokerageGateway` | Order routing with fractional quantity support |
| Interactive Brokers | `IBBrokerageGateway` | Conditional on `IBAPI` |
| StockSharp | `StockSharpBrokerageGateway` | Connector-based order routing |
| Base | `BaseBrokerageGateway` | Abstract brokerage adapter base class |
| Adapter | `BrokerageGatewayAdapter` | Order routing wrapper for `IBrokerageGateway` |

## Provider Configuration

Providers are configured via environment variables or `appsettings.json`:

```bash
# Streaming providers
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key
export POLYGON__APIKEY=your-api-key

# Historical providers
export TIINGO__TOKEN=your-token
export ALPHAVANTAGE__APIKEY=your-key
export FINNHUB__APIKEY=your-key
export TWELVEDATA__APIKEY=your-key
export FRED__APIKEY=your-key
```

## Adding a New Provider

1. Create provider class in `src/Meridian.Infrastructure/Adapters/{Name}/`
2. Implement `IMarketDataClient` (streaming) or `IHistoricalDataProvider` (backfill)
3. Add `[DataSource]` attribute with provider metadata
4. Add `[ImplementsAdr]` attributes for ADR compliance
5. Register in DI container
6. Add configuration section
7. Write tests

See the [provider implementation guide](../development/provider-implementation.md) and [provider template](../examples/provider-template/README.md) for details.

---

*For full provider documentation including setup guides and rate limits, see [docs/providers/data-sources.md](../providers/data-sources.md).*
