# Historical Data Providers Evaluation

## Meridian — Backfill Provider Assessment

**Date:** 2026-02-20
**Status:** Updated | Polygon refresh: 2026-02-20
**Author:** Architecture Review

---

## Executive Summary

This document evaluates the 10 historical data providers integrated into the Meridian system for backfill operations. The evaluation assesses data quality, coverage, rate limits, cost, and reliability to guide provider selection and fallback chain configuration.

**Key Finding:** The current multi-provider architecture with `CompositeHistoricalDataProvider` remains well-designed. Alpaca and Polygon should be primary providers for professional use cases, with Stooq and Yahoo Finance as free-tier fallbacks. Polygon remains the preferred provider for high-quality US tick data, but plan/rate-limit assumptions should be validated against Polygon's current docs before final production sizing.

---

## A. Provider Overview

### Integrated Providers

| Provider | Type | Free Tier | Data Types | Primary Use Case |
|----------|------|-----------|------------|------------------|
| Alpaca | Broker API | Yes (with account) | Bars, trades, quotes | Primary US equities |
| Polygon | Market Data | Limited | Full tick data | Professional-grade data |
| Interactive Brokers | Broker API | Yes (with account) | All types | Comprehensive coverage |
| Tiingo | Data Vendor | Yes | Daily bars | Cost-effective daily data |
| Yahoo Finance | Unofficial | Yes | Daily bars | Free fallback |
| Stooq | Free Service | Yes | Daily bars | International coverage |
| Finnhub | Data Vendor | Yes | Daily bars | Alternative source |
| Alpha Vantage | Data Vendor | Yes | Daily bars | Simple API |
| Nasdaq Data Link | Data Vendor | Limited | Various | Specialized datasets |
| StockSharp | Framework | Varies | All types | Multi-source aggregation |

---

## B. Detailed Provider Evaluations

---

### Provider 1: Alpaca Markets

**Recommendation:** Primary provider for US equities

**Best Use Cases:**
- US stock and ETF historical data
- Intraday bar data (1-min to daily)
- Organizations with Alpaca brokerage accounts
- Real-time and historical data from single provider

**Poor Use Cases:**
- International equities
- Options data
- Pre-2016 historical data

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Free with account | No additional cost for brokerage customers |
| High rate limits | 200 requests/minute on free tier |
| Good data quality | Exchange-sourced, adjusted for splits/dividends |
| Consistent API | Well-documented, stable endpoints |
| Real-time + historical | Single provider for both streaming and backfill |
| Crypto support | Includes cryptocurrency data |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| US-only equities | No international stock coverage |
| Limited history | Data typically starts 2016 |
| Account required | Must have Alpaca brokerage account |
| No options | Equity and crypto only |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★★ | Exchange-sourced data |
| Completeness | ★★★★☆ | Good coverage post-2016 |
| Timeliness | ★★★★★ | Near real-time availability |
| Adjustments | ★★★★★ | Properly adjusted for corporate actions |
| Consistency | ★★★★★ | Stable format and delivery |

**Rate Limits:**
- Free tier: 200 requests/minute
- Data tier: Higher limits available
- Pagination: 10,000 bars per request

**Implementation Quality:**
- Location: `Infrastructure/Adapters/Alpaca/`
- Error handling: Comprehensive with retry logic
- Rate limit tracking: Integrated with `ProviderRateLimitTracker`

---

### Provider 2: Polygon.io

**Recommendation:** Primary provider for professional-grade tick data

**Documentation Status (Refreshed):**
- Polygon's documentation is currently served under the Massive branding (`massive.com/docs`) while Polygon API references and SDK naming are still used in many integration contexts.
- Endpoint availability, recency entitlements, and rate limits are plan-specific and can change; engineering should rely on the live docs at implementation time rather than hard-coded values in this evaluation.
- Validation date for this section: **2026-02-20**.

**Best Use Cases:**
- Tick-level trade and quote data
- Options data requirements
- High-frequency research
- Professional trading operations

**Poor Use Cases:**
- Cost-sensitive applications
- Simple daily bar needs only
- International equities

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Tick-level data | Full trade and quote history |
| Options coverage | Complete options chain data |
| Data quality | Institutional-grade accuracy |
| Aggregates | Pre-computed OHLCV at multiple intervals |
| Corporate actions | Comprehensive adjustment data |
| Reference data | Ticker details, market status, holidays |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Cost | Professional usage can become expensive as data depth and request volume increase |
| Free tier limits | Free/basic plans are restrictive for sustained backfill workloads |
| US focus | Limited international coverage |
| Complexity | More complex API than alternatives |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★★ | SIP-sourced, institutional grade |
| Completeness | ★★★★★ | Full tick history available |
| Timeliness | ★★★★★ | Real-time on paid plans |
| Adjustments | ★★★★★ | Split/dividend adjusted |
| Consistency | ★★★★★ | Reliable delivery |

**Rate Limits:**
- Plan-dependent and entitlement-dependent (must be verified in live docs before rollout)
- Use conservative throttling defaults in backfill jobs until account-specific limits are confirmed
- Re-check limits whenever subscription tier changes

**Implementation Quality:**
- Location: `Infrastructure/Adapters/Polygon/`
- Features: Aggregates, trades, quotes, reference data
- Circuit breaker: Polly-based resilience

---

### Provider 3: Interactive Brokers

**Recommendation:** Best for comprehensive multi-asset coverage

**Best Use Cases:**
- Multi-asset class data (stocks, futures, forex, options)
- International market coverage
- Organizations with IB accounts
- Real-time and historical from single source

**Poor Use Cases:**
- Simple backfill needs (complex setup)
- High-frequency bulk requests (pacing rules)
- Organizations without IB relationship

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Asset coverage | Stocks, ETFs, futures, forex, options, bonds |
| Global markets | 150+ markets worldwide |
| Deep history | Decades of data for major instruments |
| Data quality | Exchange-direct feeds |
| Single relationship | Trading + data from one provider |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Pacing rules | Complex rate limiting (no more than 6 requests/2 sec) |
| Setup complexity | Requires TWS or IB Gateway running |
| Connection management | Stateful connection with session limits |
| Error handling | Cryptic error codes |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★★ | Exchange-direct |
| Completeness | ★★★★★ | Comprehensive global coverage |
| Timeliness | ★★★★☆ | Subject to pacing rules |
| Adjustments | ★★★★★ | Configurable adjustment options |
| Consistency | ★★★★☆ | Connection stability varies |

**Rate Limits:**
- Historical: 6 requests per 2 seconds
- Identical requests: 15-second minimum spacing
- Concurrent: 3 historical data connections max

**Implementation Quality:**
- Location: `Infrastructure/Adapters/InteractiveBrokers/`
- Connection: TWS/Gateway via IBApi
- Pacing: Implemented with adaptive throttling

---

### Provider 4: Tiingo

**Recommendation:** Cost-effective daily data with good quality

**Best Use Cases:**
- Daily OHLCV data
- Cost-conscious applications
- US equities and ETFs
- Fundamental data needs

**Poor Use Cases:**
- Intraday data requirements
- International equities
- Tick-level research

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Generous free tier | 500 requests/hour, 50 symbols/request |
| Data quality | Well-maintained, accurate |
| Simple API | Easy to integrate |
| Fundamentals | Includes fundamental data |
| Crypto | Cryptocurrency coverage |
| News | News feed available |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Daily only (free) | Intraday requires paid plan |
| US focus | Limited international |
| No tick data | Aggregates only |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★☆ | Good quality, occasional gaps |
| Completeness | ★★★★☆ | Good US coverage |
| Timeliness | ★★★★☆ | End-of-day updates |
| Adjustments | ★★★★★ | Properly adjusted |
| Consistency | ★★★★☆ | Reliable |

**Rate Limits:**
- Free: 500 requests/hour, 20,000/day
- Power: 5,000 requests/hour
- Commercial: Higher limits

**Implementation Quality:**
- Location: `Infrastructure/Adapters/Tiingo/`
- Clean implementation with proper error handling

---

### Provider 5: Yahoo Finance

**Recommendation:** Free fallback for basic daily data

**Best Use Cases:**
- Free tier fallback
- Basic daily OHLCV
- Quick prototyping
- Non-critical applications

**Poor Use Cases:**
- Production trading systems
- High reliability requirements
- Intraday data
- Commercial applications (ToS concerns)

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Completely free | No API key required |
| Global coverage | International stocks available |
| Long history | Decades of daily data |
| Indices | Major index data available |
| Familiar | Widely known data source |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Unofficial API | No guaranteed stability |
| Terms of Service | Commercial use unclear |
| Rate limiting | Aggressive, undocumented limits |
| Data quality | Occasional errors, delayed corrections |
| No support | No official support channel |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★☆☆ | Generally accurate, occasional errors |
| Completeness | ★★★★☆ | Good coverage but gaps exist |
| Timeliness | ★★★☆☆ | Delayed updates sometimes |
| Adjustments | ★★★★☆ | Usually adjusted correctly |
| Consistency | ★★☆☆☆ | API changes without notice |

**Rate Limits:**
- Undocumented, approximately 2,000/hour
- IP-based limiting
- Can be blocked without warning

**Implementation Quality:**
- Location: `Infrastructure/Adapters/YahooFinance/`
- Defensive implementation with fallback handling

---

### Provider 6: Stooq

**Recommendation:** Excellent free source for international daily data

**Best Use Cases:**
- International equities
- Free tier requirements
- Daily OHLCV data
- European and Asian markets

**Poor Use Cases:**
- Real-time needs
- Intraday data
- US-only applications
- High-frequency requests

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Completely free | No registration required |
| International | Strong European, Asian coverage |
| Long history | Extensive historical data |
| Indices | Global index coverage |
| Currencies | Forex pairs available |
| Commodities | Commodity futures data |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Rate limits | Strict but undocumented |
| No API docs | Reverse-engineered integration |
| Data format | CSV download, not REST API |
| Reliability | Service interruptions possible |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★☆ | Good quality for free source |
| Completeness | ★★★★☆ | Excellent international coverage |
| Timeliness | ★★★☆☆ | End-of-day only |
| Adjustments | ★★★★☆ | Generally adjusted |
| Consistency | ★★★☆☆ | Format stable but service varies |

**Rate Limits:**
- Approximately 10 requests/minute recommended
- Aggressive limiting if exceeded
- No official documentation

**Implementation Quality:**
- Location: `Infrastructure/Adapters/Stooq/`
- CSV parsing with robust error handling

---

### Provider 7: Finnhub

**Recommendation:** Good alternative source with additional features

**Best Use Cases:**
- Alternative data validation
- Sentiment/news data
- SEC filings integration
- Earnings calendar

**Poor Use Cases:**
- Primary historical source
- Tick-level data
- High-volume backfill

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Free tier | 60 calls/minute |
| Alternative data | Sentiment, news, filings |
| Earnings | Earnings calendar and surprises |
| Fundamentals | Financial statements |
| WebSocket | Real-time quotes on free tier |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Limited history | Less comprehensive than alternatives |
| Rate limits | 60/min can be restrictive |
| Data gaps | Occasional missing data |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★☆ | Good quality |
| Completeness | ★★★☆☆ | Some gaps in coverage |
| Timeliness | ★★★★☆ | Good update frequency |
| Adjustments | ★★★★☆ | Adjusted data available |
| Consistency | ★★★★☆ | Stable API |

**Rate Limits:**
- Free: 60 calls/minute
- Premium: Higher limits

**Implementation Quality:**
- Location: `Infrastructure/Adapters/Finnhub/`
- Well-structured with rate limit handling

---

### Provider 8: Alpha Vantage

**Recommendation:** Simple API for basic needs

**Best Use Cases:**
- Simple integration needs
- Educational/prototype projects
- Basic daily data
- Technical indicator data

**Poor Use Cases:**
- Production systems (rate limits)
- Large backfill operations
- Time-sensitive applications

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Simple API | Easy to understand and use |
| Technical indicators | Pre-calculated indicators |
| Forex | Currency pair data |
| Crypto | Cryptocurrency support |
| Documentation | Good API documentation |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Severe rate limits | Only 5 calls/minute on free tier |
| Slow backfill | Rate limits make bulk requests impractical |
| Data quality | Occasional inconsistencies |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★☆☆ | Generally accurate |
| Completeness | ★★★☆☆ | Basic coverage |
| Timeliness | ★★★☆☆ | Delayed updates |
| Adjustments | ★★★★☆ | Adjusted data available |
| Consistency | ★★★☆☆ | Some format variations |

**Rate Limits:**
- Free: 5 calls/minute, 500/day
- Premium: 75 calls/minute

**Implementation Quality:**
- Location: `Infrastructure/Adapters/AlphaVantage/`
- Basic implementation, suitable for fallback

---

### Provider 9: Nasdaq Data Link (Quandl)

**Recommendation:** Specialized datasets and alternative data

**Best Use Cases:**
- Specialized financial datasets
- Economic indicators
- Alternative data research
- Institutional-grade data needs

**Poor Use Cases:**
- Basic equity backfill
- Free tier extensive use
- Simple applications

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| Unique datasets | Data not available elsewhere |
| Economic data | Fed, Treasury, economic indicators |
| Alternative data | Sentiment, satellite, etc. |
| Data quality | Institutional grade |
| Bulk downloads | Full dataset downloads available |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Cost | Premium datasets expensive |
| Limited free | Free tier very restricted |
| Complexity | Dataset discovery can be challenging |
| Integration | Different APIs per dataset |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★★ | Institutional quality |
| Completeness | ★★★★★ | Complete for covered datasets |
| Timeliness | ★★★★☆ | Varies by dataset |
| Adjustments | ★★★★★ | Properly maintained |
| Consistency | ★★★★☆ | Format varies by dataset |

**Rate Limits:**
- Free: 50 calls/day
- Premium: Based on subscription

**Implementation Quality:**
- Location: `Infrastructure/Adapters/NasdaqDataLink/`
- Supports multiple dataset types

---

### Provider 10: StockSharp

**Recommendation:** Multi-source aggregation framework

**Best Use Cases:**
- Aggregating multiple sources
- Complex market data needs
- Russian/Eastern European markets
- Algorithmic trading integration

**Poor Use Cases:**
- Simple backfill needs
- Teams without StockSharp experience
- Lightweight applications

---

**Strengths:**

| Strength | Detail |
|----------|--------|
| 90+ connectors | Massive source coverage |
| Unified API | Single interface for all sources |
| Trading integration | Combined data + execution |
| Open source | Community edition available |
| Backtesting | Integrated strategy testing |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Complexity | Steep learning curve |
| Documentation | Mixed quality |
| Dependencies | Heavy framework footprint |
| Licensing | Commercial features require license |

---

**Data Quality Assessment:**

| Metric | Rating | Notes |
|--------|--------|-------|
| Accuracy | ★★★★☆ | Depends on underlying source |
| Completeness | ★★★★★ | Excellent through aggregation |
| Timeliness | ★★★★☆ | Source-dependent |
| Adjustments | ★★★★☆ | Varies by source |
| Consistency | ★★★☆☆ | Normalization required |

**Rate Limits:**
- Depends on underlying data source
- Framework handles rate limiting per connector

**Implementation Quality:**
- Location: `Infrastructure/Adapters/StockSharp/`
- Leverages StockSharp.Algo library

---

## C. Comparative Summary

### Overall Provider Comparison

| Provider | Quality | Coverage | Free Tier | Rate Limits | Reliability | Recommended Priority |
|----------|---------|----------|-----------|-------------|-------------|---------------------|
| Alpaca | ★★★★★ | US Only | Excellent | 200/min | ★★★★★ | 1 (Primary) |
| Polygon | ★★★★★ | US + Crypto | Poor | Plan-dependent | ★★★★★ | 2 (Professional) |
| IB | ★★★★★ | Global | Good | Complex | ★★★★☆ | 3 (Multi-asset) |
| Tiingo | ★★★★☆ | US | Good | 500/hr | ★★★★☆ | 4 (Daily bars) |
| Stooq | ★★★★☆ | Global | Excellent | Low | ★★★☆☆ | 5 (International) |
| Yahoo | ★★★☆☆ | Global | Excellent | Undocumented | ★★☆☆☆ | 6 (Fallback) |
| Finnhub | ★★★★☆ | US | Good | 60/min | ★★★★☆ | 7 (Alternative) |
| Alpha Vantage | ★★★☆☆ | US | Poor | 5/min | ★★★☆☆ | 8 (Last resort) |
| Nasdaq Data Link | ★★★★★ | Specialized | Poor | 50/day | ★★★★★ | Special purpose |
| StockSharp | ★★★★☆ | Global | Varies | Varies | ★★★★☆ | Special purpose |

### Recommended Fallback Chain

```
Priority 1: Alpaca (if account available)
    ↓ (rate limited or unavailable)
Priority 2: Polygon (if subscription available)
    ↓ (rate limited or unavailable)
Priority 3: Interactive Brokers (if connected)
    ↓ (rate limited or unavailable)
Priority 4: Tiingo
    ↓ (rate limited or unavailable)
Priority 5: Stooq (international) / Finnhub (US)
    ↓ (rate limited or unavailable)
Priority 6: Yahoo Finance (last resort)
```

---

## D. Implementation Assessment

### CompositeHistoricalDataProvider

The current `CompositeHistoricalDataProvider` implementation is well-designed:

**Strengths:**
- Priority-based provider selection
- Automatic fallback on failure
- Rate limit awareness via `ProviderRateLimitTracker`
- Provider health monitoring
- Configurable retry policies

**Current Configuration (from codebase):**
```csharp
// Backfill providers are created in ProviderFactory and ordered by provider.Priority
AlpacaHistoricalDataProvider       // default priority: 5
PolygonHistoricalDataProvider      // default priority: 12
TiingoHistoricalDataProvider       // default priority: 15
FinnhubHistoricalDataProvider      // default priority: 18
StooqHistoricalDataProvider        // default priority: 20
YahooFinanceHistoricalDataProvider // default priority: 22 (last-resort fallback)
```

### Gap Analysis Integration

The `GapAnalyzer` service integrates well with backfill:
- Identifies missing data periods
- Triggers targeted backfill requests
- Validates backfill completeness
- Supports gap repair workflows

---

## E. Recommendations

### For Production Deployments

1. **Establish Alpaca account** as primary provider (free, reliable, good limits)
2. **Consider Polygon subscription** for tick-level data needs, and validate current plan entitlements in the live docs before scaling
3. **Configure IB connection** if multi-asset or international coverage needed
4. **Enable Tiingo** as reliable free-tier backup
5. **Keep Yahoo Finance** as last-resort fallback only

### For Cost-Sensitive Deployments

1. **Tiingo** as primary (generous free tier)
2. **Stooq** for international coverage
3. **Yahoo Finance** as fallback
4. **Avoid** Alpha Vantage (rate limits too restrictive)

### For Professional/Institutional Use

1. **Polygon** professional tier (institutional-grade data; confirm current recency + rate-limit entitlements)
2. **Interactive Brokers** for global coverage
3. **Nasdaq Data Link** for specialized datasets
4. **Alpaca** for redundancy

### Provider Selection by Data Type

| Data Type | Primary | Fallback |
|-----------|---------|----------|
| US Daily Bars | Alpaca | Tiingo, Yahoo |
| US Intraday | Alpaca, Polygon | IB |
| US Tick Data | Polygon | IB |
| International Daily | IB, Stooq | Yahoo |
| Crypto | Alpaca, Polygon | Tiingo |
| Forex | IB | Stooq |
| Options | Polygon | IB |

---

## F. Future Considerations

### Providers to Evaluate

| Provider | Reason to Consider |
|----------|-------------------|
| Databento | High-quality tick data, modern API |
| FirstRate Data | Historical tick data archives |
| Norgate Data | Australian and global coverage |
| EOD Historical | Cost-effective global data |
| Twelve Data | Modern API, good documentation |

### Architecture Improvements

1. **Caching layer** - Cache frequently-requested data to reduce API calls
2. **Parallel backfill** - Request from multiple providers simultaneously
3. **Data validation** - Cross-reference between providers for quality
4. **Cost tracking** - Monitor API usage costs across providers

---

## Key Insight

The multi-provider architecture provides excellent resilience and flexibility. The primary improvement opportunity is not adding more providers but rather:

1. **Optimizing cache utilization** to reduce redundant API calls
2. **Implementing cross-provider validation** to catch data quality issues
3. **Adding cost monitoring** to track and optimize API spend

The current implementation handles the complexity of 10 providers well through the `CompositeHistoricalDataProvider` abstraction.

---

**Evaluation Date:** 2026-02-20
**Last Reviewed:** 2026-03-19
