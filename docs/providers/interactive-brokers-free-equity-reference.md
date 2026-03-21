# Interactive Brokers TWS API - Free Equity Data Technical Reference

This document provides a comprehensive technical reference for accessing free market data for US equities through the Interactive Brokers TWS API.

**See Also:** [interactive-brokers-setup.md](interactive-brokers-setup.md) for installation and connection setup.

---

## Free Data Availability Summary

**Free Streaming Data on US-listed Stocks and ETFs** - IBKR clients receive free real-time streaming market data on all US-listed stocks and ETFs from Cboe One and IEX.

The free real-time streaming market data on all US-listed stocks and ETFs from Cboe One and IEX is **non-consolidated**. Non-consolidated real-time data only provides data from some exchanges and does not show the NBBO.

**Free Snapshot quotes** - IBKR clients receive up to 100 free snapshot quotes per month.

Clients will receive $1.00 of snapshot quotes free of charge each month.

**Important Limitation:**
In accordance with regulatory requirements, IBKR no longer offers delayed quotation information on U.S. equities to Interactive Brokers LLC clients.

---

## Account Requirements

In order to receive market data through the API, there are a few minimum requirements:

- Opened IB Account (Demo accounts cannot subscribe to data)
- Clients should have $500 USD in their account in addition to the cost of any subscriptions
- Must enable Market Data API access through the Client Portal Market Data Subscriptions Page

The Market Data API Acknowledgement is required for all users who intend to request market data through any API platform. Proceeding without this form being acknowledged, users will receive errors from their market data requests stating that market data is not subscribed.

---

## API Connection Requirements

**Supported Languages:** Python, Java, C++, C#, VisualBasic

The TWS API is a TCP Socket Protocol API based on connectivity to the Trader Workstation or IB Gateway.

**Minimum Versions:**

- Python: 3.11.0
- Java: 21
- C++: C++ 14 Standard

**Connection Ports:**

| Platform              | Port |
|-----------------------|------|
| TWS Paper Trading     | 7497 |
| TWS Live Trading      | 7496 |
| IB Gateway Paper      | 4002 |
| IB Gateway Live       | 4001 |

---

## Real-Time Streaming Data

### `reqMktData` - Level 1 Streaming

**Function Signature:**

```python
reqMktData(reqId, contract, genericTickList, snapshot, regulatorySnapshot, mktDataOptions)
```

**Parameters:**

| Parameter          | Type     | Description                                  |
|--------------------|----------|----------------------------------------------|
| reqId              | int      | Unique identifier for the request            |
| contract           | Contract | The equity contract object                   |
| genericTickList    | string   | Comma-separated generic tick types           |
| snapshot           | bool     | True for one-time snapshot                   |
| regulatorySnapshot | bool     | True for regulatory snapshot ($0.01/request) |
| mktDataOptions     | list     | Reserved, pass empty list                    |

**Standard Tick Types Returned (No Generic Tick Required):**

| Tick ID | Name           | Callback    |
|---------|----------------|-------------|
| 0       | Bid Size       | tickSize    |
| 1       | Bid Price      | tickPrice   |
| 2       | Ask Price      | tickPrice   |
| 3       | Ask Size       | tickSize    |
| 4       | Last Price     | tickPrice   |
| 5       | Last Size      | tickSize    |
| 6       | High           | tickPrice   |
| 7       | Low            | tickPrice   |
| 8       | Volume         | tickSize    |
| 9       | Close Price    | tickPrice   |
| 14      | Open           | tickPrice   |
| 45      | Last Timestamp | tickString  |
| 49      | Halted         | tickGeneric |

**Generic Tick Types for Equities (require `genericTickList` parameter):**

| Generic Tick | Returns Tick IDs | Description                            |
|--------------|------------------|----------------------------------------|
| 100          | 29, 30           | Option Call/Put Volume                 |
| 101          | 27, 28           | Option Call/Put Open Interest          |
| 104          | 23               | Historical Volatility (30-day)         |
| 106          | 24               | Implied Volatility (30-day)            |
| 165          | 15-21            | 13/26/52 week high/low, avg volume     |
| 225          | 34-36            | Auction volume, price, imbalance       |
| 232          | 37               | Mark Price                             |
| 233          | 48               | RT Volume (Time & Sales with VWAP)     |
| 236          | 46, 89           | Shortable indicator + shares available |
| 293          | 54               | Trade Count                            |
| 294          | 55               | Trade Rate (per minute)                |
| 295          | 56               | Volume Rate (per minute)               |
| 318          | 57               | Last RTH Trade                         |
| 375          | 77               | RT Trade Volume                        |
| 411          | 58               | Real-time Historical Volatility        |
| 456          | 59               | Dividends                              |
| 595          | 63-65            | Short-term volume (3/5/10 min)         |

**Example - Request with Free Data:**

```python
contract = Contract()
contract.symbol = "AAPL"
contract.secType = "STK"
contract.exchange = "SMART"
contract.currency = "USD"

# Request streaming data with RT Volume and Shortable ticks
self.reqMktData(1001, contract, "233,236", False, False, [])
```

### `reqTickByTickData` - Tick-by-Tick Data

**Function Signature:**

```python
reqTickByTickData(reqId, contract, tickType, numberOfTicks, ignoreSize)
```

**Tick Types:**

- `Last` - Trade ticks only
- `AllLast` - All trades including combos/derivatives
- `BidAsk` - Bid/Ask updates
- `MidPoint` - Midpoint price updates

---

## Historical Data

**Critical Limitation:**
Unlike TWS, which can create 'delayed charts' for most instruments without any market data subscriptions that have data up until 10-15 minutes prior to the current moment; the API always requires Level 1 streaming real time data to return historical data.

This means **historical data for US equities requires the free Cboe One/IEX streaming subscription to be active**.

### `reqHistoricalData` - Historical Bars

**Function Signature:**

```python
reqHistoricalData(reqId, contract, endDateTime, durationStr, barSizeSetting,
                  whatToShow, useRTH, formatDate, keepUpToDate, chartOptions)
```

**Parameters:**

| Parameter      | Type     | Description                                   |
|----------------|----------|-----------------------------------------------|
| reqId          | int      | Unique request identifier                     |
| contract       | Contract | The equity contract                           |
| endDateTime    | string   | End date/time ("" = now)                      |
| durationStr    | string   | Duration (e.g., "1 D", "1 W", "1 M")          |
| barSizeSetting | string   | Bar size (e.g., "1 min", "1 hour", "1 day")   |
| whatToShow     | string   | Data type (TRADES, MIDPOINT, BID, ASK, etc.)  |
| useRTH         | int      | 1 = Regular Trading Hours only, 0 = all hours |
| formatDate     | int      | 1 = string format, 2 = epoch                  |
| keepUpToDate   | bool     | True = subscribe to updates                   |
| chartOptions   | list     | Reserved, pass empty list                     |

**Valid Duration Strings:**

| Unit    | Symbol | Example |
|---------|--------|---------|
| Seconds | S      | "60 S"  |
| Days    | D      | "5 D"   |
| Weeks   | W      | "2 W"   |
| Months  | M      | "3 M"   |
| Years   | Y      | "1 Y"   |

**Valid Bar Sizes:**

| Category | Options                                                           |
|----------|-------------------------------------------------------------------|
| Seconds  | 1 secs, 5 secs, 10 secs, 15 secs, 30 secs                         |
| Minutes  | 1 min, 2 mins, 3 mins, 5 mins, 10 mins, 15 mins, 20 mins, 30 mins |
| Hours    | 1 hour, 2 hours, 3 hours, 4 hours, 8 hours                        |
| Days+    | 1 day, 1 week, 1 month                                            |

**whatToShow Types for Stocks:**

| Type                      | Description                   | Volume Included |
|---------------------------|-------------------------------|-----------------|
| TRADES                    | Trade prices (split-adjusted) | Yes             |
| MIDPOINT                  | Midpoint of bid/ask           | No              |
| BID                       | Bid prices                    | No              |
| ASK                       | Ask prices                    | No              |
| BID_ASK                   | Time-averaged bid/ask         | No              |
| ADJUSTED_LAST             | Dividend + split adjusted     | Yes             |
| HISTORICAL_VOLATILITY     | 30-day HV                     | No              |
| OPTION_IMPLIED_VOLATILITY | 30-day IV                     | No              |
| SCHEDULE                  | Trading schedule only         | No              |

**Example - Historical Data Request:**

```python
import datetime

contract = Contract()
contract.symbol = "SPY"
contract.secType = "STK"
contract.exchange = "SMART"
contract.currency = "USD"

# Get 1 month of daily bars
queryTime = datetime.datetime.now().strftime("%Y%m%d-%H:%M:%S")
self.reqHistoricalData(4001, contract, queryTime, "1 M", "1 day",
                       "TRADES", 1, 1, False, [])
```

### `reqHistoricalTicks` - Historical Tick Data

```python
reqHistoricalTicks(reqId, contract, startDateTime, endDateTime,
                   numberOfTicks, whatToShow, useRth, ignoreSize, miscOptions)
```

**Maximum:** 1000 ticks per request

**whatToShow Options:** `TRADES`, `BID_ASK`, `MIDPOINT`

---

## Rate Limits & Pacing Rules

### Overall API Message Limit

The TWS is designed to accept up to **fifty** messages per second coming from the client side. This limitation is applied to **all** connected clients combined.

### Market Data Lines Limit

By default, every user has a maxTicker Limit of 100 market data lines and as such can obtain the real time market data of up to 100 instruments simultaneously.

Note: It is important to understand the concept of market data lines since it has an impact not only on the live real time requests but also for requesting market depth and real time bars.

### Historical Data Rate Limits

The maximum number of simultaneous open historical data requests from the API is 50.

**Pacing Violations occur when:**

- Making identical historical data requests within 15 seconds
- Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds
- Making more than 60 requests within any ten minute period
- Note: BID_ASK requests count as **two** requests

At this time Historical Data Limitations for barSize = "1 mins" and greater have been lifted. However, a "soft" slow is still implemented to load-balance requests.

### Duration/Bar Size Constraints

| Duration        | Minimum Bar Size | Maximum Bar Size |
|-----------------|------------------|------------------|
| 60 S            | 1 sec            | 1 min            |
| 1800 S (30 min) | 1 sec            | 30 mins          |
| 3600 S (1 hr)   | 5 secs           | 1 hr             |
| 1 D             | 1 min            | 1 day            |
| 1 W             | 3 mins           | 1 week           |
| 1 M             | 30 mins          | 1 month          |
| 1 Y             | 1 day            | 1 month          |

### Historical Data Unavailability

- Bars <=30 seconds older than **six months**
- Data for securities no longer trading
- Native combo historical data (returns sum of legs instead)

### Tick-by-Tick Limits

- No more than 1 tick-by-tick request can be made for the same instrument within 15 seconds
- Maximum subscriptions determined by same formula as market depth (min 3, max 60)

---

## Implementation Constants for Meridian

```csharp
public static class IBApiLimits
{
    // Connection
    public const int MaxClientsPerTWS = 32;
    public const int MaxMessagesPerSecond = 50;

    // Real-Time Data
    public const int DefaultMarketDataLines = 100;
    public const int MinDepthSubscriptions = 3;
    public const int MaxDepthSubscriptions = 60;

    // Historical Data
    public const int MaxConcurrentHistoricalRequests = 50;
    public const int MaxHistoricalRequestsPer10Min = 60;
    public const int MinSecondsBetweenIdenticalRequests = 15;
    public const int MaxSameContractRequestsPer2Sec = 6;
    public const int MaxHistoricalTicksPerRequest = 1000;

    // Historical Data Retention
    public const int SmallBarMaxAgeMonths = 6;  // Bars <=30 sec

    // Tick-by-Tick
    public const int MinSecondsBetweenTickByTickSameInstrument = 15;

    // Snapshots
    public const int FreeSnapshotsPerMonth = 100;
    public const decimal SnapshotCostUSD = 0.01m;
}
```

---

## Contract Definition for US Equities

```python
from ibapi.contract import Contract

def create_us_stock_contract(symbol: str) -> Contract:
    contract = Contract()
    contract.symbol = symbol
    contract.secType = "STK"
    contract.exchange = "SMART"  # Smart routing
    contract.currency = "USD"
    contract.primaryExchange = "NASDAQ"  # or "NYSE", "ARCA"
    return contract
```

---

## Callback Methods to Implement

**Real-Time Data:**

```python
def tickPrice(self, reqId, tickType, price, attrib): pass
def tickSize(self, reqId, tickType, size): pass
def tickString(self, reqId, tickType, value): pass
def tickGeneric(self, reqId, tickType, value): pass
def tickSnapshotEnd(self, reqId): pass
```

**Historical Data:**

```python
def historicalData(self, reqId, bar): pass
def historicalDataEnd(self, reqId, start, end): pass
def historicalDataUpdate(self, reqId, bar): pass  # For keepUpToDate=True
def historicalTicks(self, reqId, ticks, done): pass
def historicalTicksBidAsk(self, reqId, ticks, done): pass
def historicalTicksLast(self, reqId, ticks, done): pass
```

**Error Handling:**

```python
def error(self, reqId, errorCode, errorString, advancedOrderRejectJson): pass
```

---

## Common Error Codes

| Code  | Description                                    |
|-------|------------------------------------------------|
| 162   | Historical market data Service error           |
| 200   | No security definition found                   |
| 354   | Requested market data not subscribed           |
| 10167 | Requested market data not subscribed (delayed) |
| 10197 | No market data during competing live session   |
| 162   | Pacing violation                               |

---

## Summary: What's Free for US Equities

| Data Type                                | Free Access | Limitations                            |
|------------------------------------------|-------------|----------------------------------------|
| **Real-time streaming (Cboe One + IEX)** | Yes         | Non-consolidated, not NBBO             |
| **Historical bars**                      | Yes*        | Requires active streaming subscription |
| **Historical ticks**                     | Yes*        | 1000 per request max                   |
| **Delayed streaming**                    | No          | Not available for US equities          |
| **Snapshot quotes**                      | 100/month   | $0.01 per additional                   |
| **Level 2 / Market Depth**               | No          | Requires paid subscription             |

\* Historical data requires Level 1 streaming subscription (free via Cboe One/IEX)

---

**Version:** 1.6.1
**Last Updated:** 2026-01-30
**TWS API Version:** 10.19+
**See Also:** [IB Setup](interactive-brokers-setup.md) | [Configuration](../HELP.md#configuration) | [Troubleshooting](../HELP.md#troubleshooting)
