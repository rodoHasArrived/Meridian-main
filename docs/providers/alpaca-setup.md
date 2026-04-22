# Alpaca Market Data Setup Guide

**Last Updated:** 2026-03-28
**Version:** 1.7.2

This document provides instructions for setting up Alpaca market data with the Meridian.

---

## Overview

Alpaca provides free real-time and historical market data through WebSocket and REST APIs. Unlike Interactive Brokers, Alpaca requires no special SDK installation - it uses standard HTTP and WebSocket protocols.

### Key Features

- **Free Real-Time Data**: IEX feed included with free account
- **SIP Data**: Premium consolidated feed ($9/month for unlimited)
- **Historical Data**: Unlimited historical bars with free account
- **No Special SDK**: Uses standard WebSocket and HTTP clients
- **Simple Authentication**: API key + secret key

---

## Prerequisites

- Active Alpaca account (paper for development; live for production)
- API Key ID and Secret Key generated from the Alpaca dashboard
- Meridian environment with access to set environment variables or `appsettings.json`

---

## Account Setup

### 1. Create Alpaca Account

1. Visit: https://alpaca.markets/
2. Sign up for a free account
3. Choose between:
   - **Paper Trading**: For development and testing
   - **Live Trading**: For production use

### 2. Generate API Keys

1. Log in to Alpaca Dashboard
2. Navigate to: **API Keys** section
3. Generate a new API key pair:
   - **API Key ID** (APCA-API-KEY-ID)
   - **Secret Key** (APCA-API-SECRET-KEY)

**Important**: Save your secret key immediately - it's only shown once!

---

## Data Feed Options

### IEX Feed (Free)

- Real-time quotes and trades from IEX exchange only
- ~2-5% of total market volume
- Suitable for development and basic monitoring
- No additional cost

### SIP Feed (Premium)

- Consolidated feed from all US exchanges
- Full NBBO (National Best Bid/Offer)
- Required for production-grade data
- $9/month for unlimited real-time
- Historical data included

### Choosing Your Feed

| Use Case | Recommended Feed |
|----------|------------------|
| Development/Testing | IEX (free) |
| Paper Trading | IEX (free) |
| Research/Backtesting | IEX or SIP |
| Live Trading | SIP (consolidated) |
| Production Data Collection | SIP |

---

## Configuration

### Environment Variables

Set your API credentials as environment variables:

```bash
# Linux/macOS
export ALPACA_KEY_ID="your-api-key-id"
export ALPACA_SECRET_KEY="your-secret-key"
export ALPACA_PAPER="true"  # Set to "false" for live trading

# Windows (PowerShell)
$env:ALPACA_KEY_ID = "your-api-key-id"
$env:ALPACA_SECRET_KEY = "your-secret-key"
$env:ALPACA_PAPER = "true"
```

### appsettings.json Configuration

```json
{
  "Alpaca": {
    "Enabled": true,
    "KeyId": "${ALPACA_KEY_ID}",
    "SecretKey": "${ALPACA_SECRET_KEY}",
    "Paper": true,
    "DataFeed": "iex",
    "WebSocket": {
      "ReconnectAttempts": 5,
      "ReconnectDelayMs": 2000
    }
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | bool | `true` | Enable/disable Alpaca provider |
| `KeyId` | string | - | API Key ID |
| `SecretKey` | string | - | API Secret Key |
| `Paper` | bool | `true` | Use paper trading endpoint |
| `DataFeed` | string | `"iex"` | `"iex"` or `"sip"` |
| `ReconnectAttempts` | int | `5` | WebSocket reconnection attempts |
| `ReconnectDelayMs` | int | `2000` | Delay between reconnects (ms) |

---

## API Endpoints

### Base URLs

| Environment | REST API | WebSocket |
|-------------|----------|-----------|
| Paper | `https://paper-api.alpaca.markets` | `wss://stream.data.alpaca.markets` |
| Live | `https://api.alpaca.markets` | `wss://stream.data.alpaca.markets` |

### Data Streams

| Stream | URL | Description |
|--------|-----|-------------|
| Stocks | `wss://stream.data.alpaca.markets/v2/sip` | SIP consolidated |
| Stocks (IEX) | `wss://stream.data.alpaca.markets/v2/iex` | IEX only |
| Crypto | `wss://stream.data.alpaca.markets/v1beta3/crypto/us` | Crypto markets |
| News | `wss://stream.data.alpaca.markets/v1beta1/news` | News feed |

---

## Real-Time Data

### WebSocket Connection

The Meridian's `AlpacaMarketDataClient` handles WebSocket connections automatically:

```csharp
// Connection is managed internally
var client = serviceProvider.GetService<AlpacaMarketDataClient>();
await client.ConnectAsync();

// Subscribe to symbols
var config = new SymbolConfig("AAPL", depth: 1);
client.SubscribeTrades(config);
client.SubscribeQuotes(config);
```

### Message Types

**Trade Message:**
```json
{
  "T": "t",
  "S": "AAPL",
  "i": 12345,
  "x": "V",
  "p": 150.25,
  "s": 100,
  "t": "2026-01-08T14:30:00.123456789Z",
  "c": ["@", "F"],
  "z": "C"
}
```

**Quote Message:**
```json
{
  "T": "q",
  "S": "AAPL",
  "bx": "Q",
  "bp": 150.24,
  "bs": 200,
  "ax": "P",
  "ap": 150.26,
  "as": 300,
  "t": "2026-01-08T14:30:00.123456789Z",
  "c": ["R"],
  "z": "C"
}
```

### Subscription Limits

| Plan | Concurrent Symbols | Rate Limit |
|------|-------------------|------------|
| Free (IEX) | Unlimited | 200 req/min |
| Basic (SIP) | Unlimited | 200 req/min |
| Unlimited | Unlimited | Higher limits |

---

## Historical Data

### REST API Endpoints

| Endpoint | Description |
|----------|-------------|
| `/v2/stocks/{symbol}/bars` | OHLCV bars |
| `/v2/stocks/{symbol}/trades` | Individual trades |
| `/v2/stocks/{symbol}/quotes` | BBO quotes |
| `/v2/stocks/auctions` | Auction data |

### Bar Timeframes

```
1Min, 5Min, 15Min, 30Min, 1Hour, 4Hour, 1Day, 1Week, 1Month
```

### Example: Fetch Historical Bars

```csharp
// Using AlpacaHistoricalDataProvider
var provider = new AlpacaHistoricalDataProvider(apiKey, secretKey);

var bars = await provider.GetBarsAsync(
    symbol: "AAPL",
    from: DateTime.Today.AddDays(-30),
    to: DateTime.Today,
    timeframe: Timeframe.Day
);
```

### Rate Limits

| Endpoint | Free Tier | Paid Tier |
|----------|-----------|-----------|
| Historical Bars | 200/min | 10,000/min |
| Historical Trades | 200/min | 10,000/min |
| Historical Quotes | 200/min | 10,000/min |

---

## Implementation Details

### File Locations

```
src/Meridian.Infrastructure/Adapters/Alpaca/
├── AlpacaMarketDataClient.cs          # Real-time streaming
└── AlpacaHistoricalDataProvider.cs    # Historical data
```

### Features Implemented

| Feature | Status | Notes |
|---------|--------|-------|
| Real-time trades | ✅ | Via WebSocket |
| Real-time quotes | ✅ | Via WebSocket |
| Historical bars | ✅ | 1min to monthly |
| Historical trades | ✅ | Tick-level |
| Historical quotes | ✅ | BBO snapshots |
| Auction data | ✅ | Opening/closing auctions |
| Reconnection | ✅ | Polly-based resilience |
| Rate limiting | ✅ | Built-in throttling |

---

## Troubleshooting

### Connection Issues

**Error: `401 Unauthorized`**
- Verify API key and secret are correct
- Check credentials haven't been revoked
- Ensure using correct endpoint (paper vs live)

**Error: `WebSocket connection failed`**
- Check internet connectivity
- Verify firewall allows outbound WSS connections
- Try paper trading endpoint first

**Error: `subscription limit reached`**
- Check current subscription count
- Unsubscribe unused symbols
- Consider upgrading plan

### Data Issues

**Missing trades or quotes:**
- IEX only shows ~2-5% of market activity
- Switch to SIP feed for comprehensive data
- Check market hours (9:30 AM - 4:00 PM ET)

**Delayed data:**
- Free tier is real-time, not delayed
- Check network latency
- Verify WebSocket connection is active

### Historical Data Issues

**Error: `rate limit exceeded`**
- Add delays between requests
- Use `CompositeHistoricalDataProvider` for automatic rate limiting
- Batch requests by symbol

**Missing historical data:**
- Some symbols may have gaps
- Check symbol was listed during requested period
- Try different timeframes

---

## Best Practices

### Development

1. **Always use Paper Trading** during development
2. **Test with IEX first** before upgrading to SIP
3. **Implement error handling** for all API calls
4. **Log all authentication failures** for debugging

### Production

1. **Use SIP feed** for production-grade data
2. **Monitor WebSocket connection** health
3. **Implement circuit breakers** for API failures
4. **Store credentials securely** (environment variables or secrets manager)

### Performance

1. **Batch subscription requests** to reduce overhead
2. **Use historical data** for backtesting instead of replaying real-time
3. **Implement local caching** for frequently accessed data
4. **Enable compression** for WebSocket messages

---

## API Reference

### Official Documentation

- **API Docs**: https://docs.alpaca.markets/
- **Market Data API**: https://docs.alpaca.markets/docs/about-market-data-api
- **WebSocket Reference**: https://docs.alpaca.markets/docs/real-time-stock-pricing-data
- **Historical Data**: https://docs.alpaca.markets/docs/historical-stock-data

### SDKs (Not Required)

- **Python**: `alpaca-py` (official)
- **C#**: Standard HTTP/WebSocket (no SDK needed)
- **JavaScript**: `@alpacahq/alpaca-trade-api`

---

## Comparison with Other Providers

| Feature | Alpaca | Interactive Brokers | Yahoo Finance |
|---------|--------|--------------------|--------------||
| Free Real-Time | Yes (IEX) | Yes (Cboe One) | No |
| Free Historical | Yes | Yes* | Yes |
| SDK Required | No | Yes | No |
| Setup Complexity | Low | High | Low |
| Data Quality | Good | Excellent | Good |
| Rate Limits | Generous | Strict | Moderate |

*IB historical requires streaming subscription

---

## Related Documentation

- [Data Sources Reference](data-sources.md)
- [Provider Comparison](provider-comparison.md)
- [Historical Backfill Guide](backfill-guide.md)
- [Configuration Guide](../HELP.md#configuration)
- [Troubleshooting](../HELP.md#troubleshooting)

---

*See Also:* [Interactive Brokers Setup](interactive-brokers-setup.md) | [Getting Started](../getting-started/README.md)
