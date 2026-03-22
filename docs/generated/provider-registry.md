> **AUTO-GENERATED — DO NOT EDIT**
> This file is generated automatically. Manual edits will be overwritten.
> See `docs/generated/README.md` for details on how generation works.

# Provider Registry

> Auto-generated on 2026-03-22 03:02:07 UTC

This document lists all data providers available in the Meridian.

## Real-Time Streaming Providers

| Provider | ID | Class | Type | Category | Status |
|----------|-----|-------|------|----------|--------|
| Alpaca Markets | `alpaca` | — | Streaming | RealTime | ✅ Active |
| Interactive Brokers | `interactive-brokers` | — | Streaming | RealTime | ✅ Active |
| Polygon.io | `polygon` | — | Streaming | RealTime | ✅ Active |
| NYSE | `nyse` | — | Streaming | RealTime | ✅ Active |
| StockSharp | `stocksharp` | — | Streaming | RealTime | ✅ Active |

## Historical Data Providers (Backfill)

| Provider | ID | Free Tier | Rate Limits |
|----------|-----|-----------|-------------|
| Yahoo Finance | `yahoo-finance` | Yes | Varies |
| Stooq | `stooq` | Yes | Varies |
| Tiingo | `tiingo` | Yes | Varies |
| Alpha Vantage | `alpha-vantage` | Yes | Varies |
| Finnhub | `finnhub` | Yes | Varies |
| Nasdaq Data Link | `nasdaq-data-link` | Yes | Varies |

## Provider Configuration

Providers are configured via environment variables or `appsettings.json`:

```bash
# Real-time providers
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key
export POLYGON__APIKEY=your-api-key

# Historical providers
export TIINGO__TOKEN=your-token
export ALPHAVANTAGE__APIKEY=your-key
```

## Adding a New Provider

1. Create provider class in `src/Meridian.Infrastructure/Adapters/{Name}/`
2. Implement `IMarketDataClient` (streaming) or `IHistoricalDataProvider` (backfill)
3. Add `[DataSource]` attribute with provider metadata
4. Add `[ImplementsAdr]` attributes for ADR compliance
5. Register in DI container
6. Add configuration section
7. Write tests

---

*This file is auto-generated. Do not edit manually.*
