# Data Provider Comparison Guide

**Last Updated:** 2026-01-30
**Version:** 1.6.1

This document provides a comprehensive comparison of all data providers supported by the Meridian.

---

## Quick Comparison Matrix

### Streaming Providers (Real-Time)

| Provider | Status | Setup | Free Tier | Data Quality | Best For |
|----------|--------|-------|-----------|--------------|----------|
| **Alpaca** | ✅ Implemented | Easy | IEX feed | Good | Development, basic collection |
| **Interactive Brokers** | ⚠️ IBAPI Required | Complex | Cboe One + IEX | Excellent | Professional trading, L2 depth |
| **Polygon** | ⚠️ Partial | Medium | 5 calls/min | Excellent | (Future implementation) |
| **StockSharp** | ⚠️ Requires setup | Medium | Varies | Good | Multi-exchange (8 connectors) |
| **NYSE** | ⚠️ Requires credentials | Medium | Subscription | Excellent | NYSE-specific feeds |

### Historical Providers (Backfill)

| Provider | Status | Free Tier | Rate Limit | Coverage | Data Quality |
|----------|--------|-----------|------------|----------|--------------|
| **Alpaca** | ✅ Implemented | Unlimited | 200/min | US equities | Excellent |
| **Yahoo Finance** | ✅ Implemented | Unlimited | ~2000/hr | 50K+ global | Good |
| **Stooq** | ✅ Implemented | Unlimited | Respectful | Global | Good |
| **Nasdaq Data Link** | ✅ Implemented | Limited | 300/10s | Alternative | Excellent |
| **Tiingo** | ✅ Implemented | 1K/day | 50/hr | 65K+ securities | Excellent |
| **Finnhub** | ✅ Implemented | 60/min | 60/min | 60K+ global | Good |
| **Alpha Vantage** | ✅ Implemented | 25/day | 5/min | US + global | Good |
| **Polygon** | ✅ Implemented | 5/min | 5/min | US equities | Excellent |
| **IB Historical** | ⚠️ IBAPI Required | With account | Strict | US + global | Excellent |

---

## Detailed Provider Profiles

### Alpaca

**Best For:** Development, hobbyist trading, simple data collection

| Attribute | Details |
|-----------|---------|
| **Type** | Streaming + Historical |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Low (no SDK required) |
| **Authentication** | API Key + Secret |
| **Free Tier** | IEX real-time + unlimited historical |
| **Paid Tier** | SIP consolidated feed ($9/month) |
| **Rate Limits** | 200 req/min (generous) |
| **Data Types** | Trades, Quotes, Bars, Auctions |

**Pros:**
- Easy setup with environment variables
- No special SDK installation
- Generous free tier
- Good documentation

**Cons:**
- IEX feed only captures ~2-5% of market volume
- No Level 2 market depth
- US equities only

**Files:**
- `Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs`
- `Infrastructure/Adapters/Core/AlpacaHistoricalDataProvider.cs`

---

### Interactive Brokers

**Best For:** Professional trading, comprehensive market data, L2 depth

| Attribute | Details |
|-----------|---------|
| **Type** | Streaming + Historical |
| **Status** | ⚠️ Requires IBAPI build flag |
| **Setup Complexity** | High (TWS/Gateway + SDK) |
| **Authentication** | Account + TWS connection |
| **Free Tier** | Cboe One + IEX streaming |
| **Paid Tier** | Various exchange subscriptions |
| **Rate Limits** | Strict (50 msg/sec, 100 data lines) |
| **Data Types** | Trades, Quotes, L2 Depth, Scanners |

**Pros:**
- High-quality consolidated data
- Level 2 market depth
- Market scanners
- Global markets coverage

**Cons:**
- Complex setup (requires TWS/Gateway)
- Strict rate limits
- $500 minimum account balance
- API learning curve

**Files:**
- `Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs`
- `Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.cs`
- `Infrastructure/Adapters/Core/IBHistoricalDataProvider.cs` (with IBAPI)

---

### Yahoo Finance

**Best For:** Historical backfill, global coverage, cost-free data

| Attribute | Details |
|-----------|---------|
| **Type** | Historical only |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Very Low (no auth required) |
| **Authentication** | None |
| **Free Tier** | Unlimited |
| **Rate Limits** | ~2000 req/hr |
| **Data Types** | Daily/Weekly/Monthly OHLCV |

**Pros:**
- No authentication required
- 50,000+ global securities
- 20+ years historical data
- Dividend and split adjusted prices

**Cons:**
- Daily frequency only (no intraday)
- Unofficial API (may change)
- No real-time streaming
- Occasional data gaps

**Files:**
- `Infrastructure/Adapters/Core/YahooFinanceHistoricalDataProvider.cs`

---

### Tiingo

**Best For:** Dividend-adjusted data, corporate actions

| Attribute | Details |
|-----------|---------|
| **Type** | Historical only |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Low (API key required) |
| **Authentication** | API Token |
| **Free Tier** | 1,000 req/day, 50/hr |
| **Data Types** | Daily OHLCV, dividends, splits |

**Pros:**
- High-quality dividend-adjusted data
- Full corporate actions history
- Clean, well-documented API
- 30+ years history for major equities

**Cons:**
- Daily data only (no intraday)
- Limited free tier
- No real-time streaming
- US focus primarily

**Files:**
- `Infrastructure/Adapters/Core/TiingoHistoricalDataProvider.cs`

---

### Finnhub

**Best For:** Company fundamentals, earnings calendar

| Attribute | Details |
|-----------|---------|
| **Type** | Historical + Reference |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Low (API key required) |
| **Authentication** | API Key |
| **Free Tier** | 60 calls/min |
| **Data Types** | OHLCV, Fundamentals, Earnings, News |

**Pros:**
- Generous free tier (60/min)
- Company fundamentals included
- Earnings calendar
- News sentiment data

**Cons:**
- Daily OHLCV only
- Limited historical depth
- Premium features require subscription

**Files:**
- `Infrastructure/Adapters/Core/FinnhubHistoricalDataProvider.cs`

---

### Stooq

**Best For:** Global indices, forex, commodities

| Attribute | Details |
|-----------|---------|
| **Type** | Historical only |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Very Low (no auth required) |
| **Authentication** | None |
| **Free Tier** | Unlimited |
| **Data Types** | Daily OHLCV |

**Pros:**
- No authentication required
- Global coverage (indices, forex, commodities)
- 20+ years history
- Simple CSV API

**Cons:**
- Daily data only
- No dividend adjustment
- No corporate actions data
- Less reliable than paid sources

**Files:**
- `Infrastructure/Adapters/Core/StooqHistoricalDataProvider.cs`

---

### Nasdaq Data Link (formerly Quandl)

**Best For:** Alternative data, economic indicators

| Attribute | Details |
|-----------|---------|
| **Type** | Historical only |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Low (API key required) |
| **Authentication** | API Key |
| **Free Tier** | Limited datasets |
| **Data Types** | Time series, tables |

**Pros:**
- FRED economic data
- Alternative datasets
- High data quality
- Professional API

**Cons:**
- Limited free datasets
- Premium data requires subscription
- Not primarily for equity prices

**Files:**
- `Infrastructure/Adapters/Core/NasdaqDataLinkHistoricalDataProvider.cs`

---

### Alpha Vantage

**Best For:** Intraday historical data

| Attribute | Details |
|-----------|---------|
| **Type** | Historical only |
| **Status** | ✅ Implemented |
| **Setup Complexity** | Low (API key required) |
| **Authentication** | API Key |
| **Free Tier** | 25 req/day |
| **Data Types** | Intraday + Daily OHLCV |

**Pros:**
- Intraday data (1/5/15/30/60 min)
- Global indices and forex
- Easy API

**Cons:**
- Severely limited free tier (25/day)
- Not practical for bulk backfill
- Slow data updates

**Files:**
- `Infrastructure/Adapters/Core/AlphaVantageHistoricalDataProvider.cs`

---

### Polygon.io

**Best For:** Historical bars, trades, quotes

| Attribute | Details |
|-----------|---------|
| **Type** | Historical (streaming stub) |
| **Status** | ✅ Historical Production, ❌ Streaming Stub |
| **Setup Complexity** | Low (API key required) |
| **Authentication** | API Key |
| **Free Tier** | 5 calls/min |
| **Data Types** | OHLCV, trades, quotes |

**Pros:**
- High-quality data
- Tick-level historical data
- Options and crypto available

**Cons:**
- Very limited free tier
- Streaming not yet implemented
- Expensive premium tiers

**Files:**
- `Infrastructure/Adapters/Core/PolygonHistoricalDataProvider.cs`
- `Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` (stub)

---

### StockSharp

**Best For:** Multi-exchange connectivity, futures, crypto exchanges

| Attribute | Details |
|-----------|---------|
| **Type** | Streaming + Historical |
| **Status** | ⚠️ Requires STOCKSHARP build flag |
| **Setup Complexity** | Medium (connector packages required) |
| **Authentication** | Varies by connector |
| **Connector Types** | 8 implemented (5 public + 3 crypto) |

**Supported Connectors:**

| Connector | Markets | Status | Notes |
|-----------|---------|--------|-------|
| Rithmic | CME, NYMEX, COMEX, CBOT futures | Public | Low-latency direct access |
| IQFeed | US equities, options | Public | Historical lookups |
| CQG | Futures, options | Public | Excellent historical coverage |
| InteractiveBrokers | Global multi-asset | Public | Via StockSharp adapter |
| Custom | Any via AdapterType | Public | Extensible adapter system |
| Binance | Crypto spot & futures | Licensed | Requires crowdfunding membership |
| Coinbase | Coinbase Pro markets | Licensed | Requires crowdfunding membership |
| Kraken | Crypto spot | Licensed | Requires crowdfunding membership |

**Note:** Crypto connectors (Binance, Coinbase, Kraken) require StockSharp crowdfunding membership. See https://stocksharp.com/store/ for licensing.

**Pros:**
- 90+ data sources via StockSharp ecosystem
- Professional-grade market data infrastructure
- Unified API across multiple exchanges
- Active development and community

**Cons:**
- Requires separate StockSharp packages
- Crypto connectors require paid license
- Learning curve for StockSharp API
- Build flags required for each connector

**Files:**
- `Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs`
- `Infrastructure/Adapters/StockSharp/StockSharpConnectorFactory.cs`

---

## Feature Comparison Matrix

### Data Types Supported

| Provider | Trades | Quotes | Bars | L2 Depth | Fundamentals |
|----------|--------|--------|------|----------|--------------|
| Alpaca | ✅ | ✅ | ✅ | ❌ | ❌ |
| Interactive Brokers | ✅ | ✅ | ✅ | ✅ | ❌ |
| Yahoo Finance | ❌ | ❌ | ✅ | ❌ | ❌ |
| Tiingo | ❌ | ❌ | ✅ | ❌ | ❌ |
| Finnhub | ❌ | ❌ | ✅ | ❌ | ✅ |
| Stooq | ❌ | ❌ | ✅ | ❌ | ❌ |
| Polygon | ✅ | ✅ | ✅ | ❌ | ❌ |

### Data Quality Features

| Provider | Split Adjusted | Dividend Adjusted | Corporate Actions |
|----------|---------------|-------------------|-------------------|
| Alpaca | ✅ | ✅ | ✅ |
| Interactive Brokers | ✅ | ✅ | ✅ Full |
| Yahoo Finance | ✅ | ✅ (adj close) | Dividends, Splits |
| Tiingo | ✅ | ✅ (all OHLCV) | ✅ Full |
| Finnhub | ✅ | ✅ | Dividends, Splits |
| Stooq | ✅ | ❌ | ❌ |
| Polygon | ✅ | ✅ | Dividends, Splits |

---

## Recommended Provider Combinations

### Development/Testing
```
Primary Streaming: Alpaca (IEX)
Historical Backfill: Yahoo Finance + Stooq
```

### Production - Basic
```
Primary Streaming: Alpaca (SIP)
Historical Backfill: Alpaca + Yahoo Finance
Fundamentals: Finnhub
```

### Production - Professional
```
Primary Streaming: Interactive Brokers
L2 Depth: Interactive Brokers
Historical Backfill: IB + Tiingo + Alpaca
Fundamentals: Finnhub
```

### Research/Backtesting
```
Historical: CompositeProvider with:
  - Tiingo (dividend-adjusted)
  - Yahoo Finance (global coverage)
  - Alpaca (recent data)
  - Stooq (indices, forex)
```

---

## CompositeHistoricalDataProvider

The `CompositeHistoricalDataProvider` automatically manages multiple providers:

```csharp
var composite = new CompositeHistoricalDataProvider(new[]
{
    tiingoProvider,    // Priority 1: Best for adjusted data
    yahooProvider,     // Priority 2: Wide coverage
    alpacaProvider,    // Priority 3: Recent data
    stooqProvider      // Priority 4: Fallback
});

// Automatic failover and rate-limit rotation
var bars = await composite.GetBarsAsync("AAPL", from, to);
```

**Features:**
- Automatic failover on errors
- Rate-limit rotation across providers
- Priority-based selection
- Health checking

---

## Cost Analysis

### Free Tier Comparison

| Provider | Monthly Cost | Data Volume | Limitations |
|----------|--------------|-------------|-------------|
| Alpaca (IEX) | $0 | Unlimited | IEX only (~2-5% volume) |
| Yahoo Finance | $0 | Unlimited | Daily data, unofficial API |
| Stooq | $0 | Unlimited | Daily data, no adjustments |
| Tiingo | $0 | 1K req/day | Rate limited |
| Finnhub | $0 | 60 req/min | Limited fundamentals |
| Alpha Vantage | $0 | 25 req/day | Severely limited |
| Polygon | $0 | 5 req/min | Very limited |
| IB | $0* | 100 data lines | Account required |

*Interactive Brokers requires $500 minimum account balance

### Paid Tier Recommendations

| Use Case | Recommended | Monthly Cost |
|----------|-------------|--------------|
| Hobbyist | Free tiers only | $0 |
| Serious Research | Alpaca SIP | $9 |
| Day Trading | IB + data subs | $50-100+ |
| Institutional | Multiple premium | $500+ |

---

## Related Documentation

- [Alpaca Setup Guide](alpaca-setup.md)
- [Interactive Brokers Setup](interactive-brokers-setup.md)
- [Historical Backfill Guide](backfill-guide.md)
- [Data Sources Reference](data-sources.md)
- [Configuration Guide](../HELP.md#configuration)

---

*Last Updated: 2026-02-01*
