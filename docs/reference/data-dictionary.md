# Meridian - Data Dictionary

**Version:** 2.0.0
**Generated:** 2026-02-22
**Schema Version:** 1

This document provides comprehensive data dictionaries for all event types in the Meridian system, including field descriptions, data types, valid ranges, and exchange-specific codes.

---

## Table of Contents

1. [Event Container](#event-container)
2. [Core Event Types](#core-event-types)
   - [Trade](#trade)
   - [LOBSnapshot (L2 Order Book)](#lobsnapshot-l2-order-book)
   - [BboQuotePayload](#bboquotepayload)
   - [OrderFlowStatistics](#orderflowstatistics)
   - [IntegrityEvent](#integrityevent)
   - [DepthIntegrityEvent](#depthintegrityevent)
3. [Historical Event Types](#historical-event-types)
   - [HistoricalBar](#historicalbar)
   - [HistoricalQuote](#historicalquote)
   - [HistoricalTrade](#historicaltrade)
   - [HistoricalAuction](#historicalauction)
4. [Supporting Types](#supporting-types)
   - [OrderBookLevel](#orderbooklevel)
   - [AuctionPrice](#auctionprice)
5. [Adapter Input Models](#adapter-input-models)
   - [MarketTradeUpdate](#markettradeupdate)
   - [MarketQuoteUpdate](#marketquoteupdate)
   - [MarketDepthUpdate](#marketdepthupdate)
6. [Enumerations](#enumerations)
7. [Exchange Codes](#exchange-codes)
8. [Trade Conditions](#trade-conditions)
9. [Quote Conditions](#quote-conditions)
10. [Tape Identifiers](#tape-identifiers)
11. [JSON Serialization](#json-serialization)

---

## Event Container

### MarketEvent

The primary container record that wraps all market data events with common metadata.

**Location:** `src/Meridian.Contracts/Domain/Events/MarketEvent.cs`
**JSON Discriminator:** Uses `Type` field for polymorphism

| Field | Type | Description | Required | Valid Range | Default | Example |
|-------|------|-------------|----------|-------------|---------|---------|
| `Timestamp` | `DateTimeOffset` | Event timestamp in UTC with nanosecond precision | Yes | Any valid datetime | - | `2026-01-09T14:30:00.123456789Z` |
| `Symbol` | `string` | Trading symbol/ticker | Yes | Non-empty string | - | `"AAPL"`, `"SPY"` |
| `Type` | `MarketEventType` | Event type discriminator | Yes | See [MarketEventType](#marketeventtype) | - | `Trade` |
| `Payload` | `MarketEventPayload?` | Polymorphic event payload | No | Depends on Type | `null` | See event types below |
| `Sequence` | `long` | Sequence number for ordering and replay | No | >= 0 | `0` | `123456789` |
| `Source` | `string` | Data source/provider identifier | No | Non-empty string | `"IB"` | `"Alpaca"`, `"NYSE"` |
| `SchemaVersion` | `int` | Event schema version for compatibility | No | >= 1 | `1` | `1` |
| `Tier` | `MarketEventTier` | Processing tier classification | No | `Raw`, `Derived` | `Raw` | `Raw` |

**Sample JSON:**
```json
{
  "timestamp": "2026-01-09T14:30:00.123456789Z",
  "symbol": "AAPL",
  "type": "Trade",
  "sequence": 123456789,
  "source": "Alpaca",
  "schemaVersion": 1,
  "tier": "Raw",
  "payload": {
    "price": 189.42,
    "size": 100,
    "exchange": "NASDAQ"
  }
}
```

---

## Core Event Types

All event payloads inherit from `MarketEventPayload` abstract record and use JSON polymorphism via the `"kind"` discriminator property.

### Trade

Represents a single executed trade (tick-by-tick trade print) with sequence validation.

**Location:** `src/Meridian.Contracts/Domain/Models/Trade.cs`
**JSON Kind:** `"trade"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | Exchange timestamp or observation time | Yes | Any valid datetime | No | `2026-01-09T14:30:00.123Z` |
| `Symbol` | `string` | Trading symbol | Yes | Non-empty, non-whitespace | No | `"AAPL"` |
| `Price` | `decimal` | Trade execution price | Yes | > 0 | No | `185.2500` |
| `Size` | `long` | Trade quantity in shares | Yes | >= 0 | No | `100` |
| `Aggressor` | `AggressorSide` | Side that initiated the trade | Yes | `Buy`, `Sell`, `Unknown` | No | `Buy` |
| `SequenceNumber` | `long` | Trade sequence for ordering | Yes | >= 0 | No | `12345` |
| `StreamId` | `string?` | Multi-stream reconciliation identifier | No | Any string | Yes | `"STREAM-001"` |
| `Venue` | `string?` | Exchange/venue identifier | No | Any string | Yes | `"XNAS"` |

**Validation Rules:**
- `Price` must be greater than 0
- `Size` must be greater than or equal to 0
- `Symbol` cannot be null, empty, or whitespace

**Aggressor Inference Logic:**
- Trade price >= Best Ask → `Buy` aggressor
- Trade price <= Best Bid → `Sell` aggressor
- Otherwise → `Unknown`

**Sample JSON:**
```json
{
  "kind": "trade",
  "timestamp": "2026-01-09T14:30:00.123456789Z",
  "symbol": "AAPL",
  "price": 185.2500,
  "size": 100,
  "aggressor": "Buy",
  "sequenceNumber": 12345,
  "streamId": "STREAM-001",
  "venue": "XNAS"
}
```

---

### LOBSnapshot (L2 Order Book)

Full Level-2 order book state snapshot with bid/ask ladders.

**Location:** `src/Meridian.Contracts/Domain/Models/LOBSnapshot.cs`
**JSON Kind:** `"l2"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | Snapshot timestamp | Yes | Any valid datetime | No | `2026-01-09T14:30:00Z` |
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `Bids` | `IReadOnlyList<OrderBookLevel>` | Bid levels (best-to-worst, Level 0 = best) | Yes | - | No | See [OrderBookLevel](#orderbooklevel) |
| `Asks` | `IReadOnlyList<OrderBookLevel>` | Ask levels (best-to-worst, Level 0 = best) | Yes | - | No | See [OrderBookLevel](#orderbooklevel) |
| `MidPrice` | `decimal?` | Derived mid-price: (BestBid + BestAsk) / 2 | No | > 0 | Yes | `185.2475` |
| `MicroPrice` | `decimal?` | Size-weighted mid-price | No | > 0 | Yes | `185.2480` |
| `Imbalance` | `decimal?` | Top-of-book imbalance: (BidSize - AskSize) / (BidSize + AskSize) | No | -1.0 to 1.0 | Yes | `0.25` |
| `MarketState` | `MarketState` | Current market trading state | No | See [MarketState](#marketstate) | No | `Normal` |
| `SequenceNumber` | `long` | Replay continuity sequence | No | >= 0 | No | `12345` |
| `StreamId` | `string?` | Multi-source reconciliation ID | No | Any string | Yes | `"DEPTH-001"` |
| `Venue` | `string?` | Exchange identifier | No | Any string | Yes | `"XNAS"` |

**Calculated Properties:**
- `MidPrice` = (BestBid.Price + BestAsk.Price) / 2 when both sides present
- `Imbalance` = (BidSize - AskSize) / (BidSize + AskSize) at top-of-book

**Sample JSON:**
```json
{
  "kind": "l2",
  "timestamp": "2026-01-09T14:30:00Z",
  "symbol": "AAPL",
  "bids": [
    { "side": "Bid", "level": 0, "price": 185.24, "size": 500 },
    { "side": "Bid", "level": 1, "price": 185.23, "size": 300 }
  ],
  "asks": [
    { "side": "Ask", "level": 0, "price": 185.25, "size": 400 },
    { "side": "Ask", "level": 1, "price": 185.26, "size": 600 }
  ],
  "midPrice": 185.245,
  "imbalance": 0.111,
  "marketState": "Normal",
  "sequenceNumber": 12345
}
```

---

### BboQuotePayload

Best Bid and Offer (BBO/NBBO) snapshot with calculated spread and mid-price.

**Location:** `src/Meridian.Contracts/Domain/Models/BboQuotePayload.cs`
**JSON Kind:** `"bbo"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | Quote timestamp | Yes | Any valid datetime | No | `2026-01-09T14:30:00Z` |
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `BidPrice` | `decimal` | Best bid price | Yes | >= 0 | No | `185.24` |
| `BidSize` | `long` | Best bid quantity | Yes | >= 0 | No | `500` |
| `AskPrice` | `decimal` | Best ask price | Yes | >= 0 | No | `185.25` |
| `AskSize` | `long` | Best ask quantity | Yes | >= 0 | No | `400` |
| `MidPrice` | `decimal?` | Calculated (BidPrice + AskPrice) / 2 | No | > 0 | Yes | `185.245` |
| `Spread` | `decimal?` | Calculated AskPrice - BidPrice | No | >= 0 | Yes | `0.01` |
| `SequenceNumber` | `long` | Per-symbol sequence number | Yes | >= 0 | No | `12345` |
| `StreamId` | `string?` | Stream identifier | No | Any string | Yes | `"QUOTE-001"` |
| `Venue` | `string?` | Exchange identifier | No | Any string | Yes | `"XNAS"` |

**Calculation Rules:**
- `MidPrice` and `Spread` are populated only when:
  - `BidPrice > 0`
  - `AskPrice > 0`
  - `AskPrice >= BidPrice`

**Sample JSON:**
```json
{
  "kind": "bbo",
  "timestamp": "2026-01-09T14:30:00Z",
  "symbol": "AAPL",
  "bidPrice": 185.24,
  "bidSize": 500,
  "askPrice": 185.25,
  "askSize": 400,
  "midPrice": 185.245,
  "spread": 0.01,
  "sequenceNumber": 12345
}
```

---

### OrderFlowStatistics

Rolling order-flow statistics derived from recent trades including VWAP, imbalance, and volume splits.

**Location:** `src/Meridian.Contracts/Domain/Models/OrderFlowStatistics.cs`
**JSON Kind:** `"orderflow"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | Statistics timestamp | Yes | Any valid datetime | No | `2026-01-09T14:30:00Z` |
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `BuyVolume` | `long` | Cumulative volume of aggressor-buy trades | Yes | >= 0 | No | `50000` |
| `SellVolume` | `long` | Cumulative volume of aggressor-sell trades | Yes | >= 0 | No | `45000` |
| `UnknownVolume` | `long` | Volume with unknown aggressor | Yes | >= 0 | No | `5000` |
| `VWAP` | `decimal` | Volume-weighted average price | Yes | > 0 | No | `185.2467` |
| `Imbalance` | `decimal` | Ratio: (BuyVolume - SellVolume) / TotalVolume | Yes | -1.0 to 1.0 | No | `0.05` |
| `TradeCount` | `int` | Number of trades in rolling window | Yes | >= 0 | No | `1234` |
| `SequenceNumber` | `long` | Event sequence number | Yes | >= 0 | No | `12345` |
| `StreamId` | `string?` | Stream identifier | No | Any string | Yes | `"FLOW-001"` |
| `Venue` | `string?` | Exchange identifier | No | Any string | Yes | `"XNAS"` |

**Calculation Formulas:**
- `TotalVolume` = BuyVolume + SellVolume + UnknownVolume
- `Imbalance` = (BuyVolume - SellVolume) / TotalVolume
- `VWAP` = Σ(Price × Size) / Σ(Size)

**Emission:** Generated after each trade by `TradeDataCollector`

**Sample JSON:**
```json
{
  "kind": "orderflow",
  "timestamp": "2026-01-09T14:30:00Z",
  "symbol": "AAPL",
  "buyVolume": 50000,
  "sellVolume": 45000,
  "unknownVolume": 5000,
  "vwap": 185.2467,
  "imbalance": 0.05,
  "tradeCount": 1234,
  "sequenceNumber": 12345
}
```

---

### IntegrityEvent

Data integrity and continuity anomalies for trade sequences.

**Location:** `src/Meridian.Contracts/Domain/Models/IntegrityEvent.cs`
**JSON Kind:** `"integrity"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | When anomaly was detected | Yes | Any valid datetime | No | `2026-01-09T14:30:00Z` |
| `Symbol` | `string` | Affected trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `Severity` | `IntegritySeverity` | Severity level of the anomaly | Yes | `Info`, `Warning`, `Error` | No | `Warning` |
| `Description` | `string` | Human-readable error message | Yes | Non-empty | No | `"Sequence gap detected"` |
| `ErrorCode` | `int?` | Numeric error code | No | See table below | Yes | `1001` |
| `SequenceNumber` | `long` | Received/anomalous sequence number | Yes | >= 0 | No | `12350` |
| `StreamId` | `string?` | Stream identifier | No | Any string | Yes | `"TRADE-001"` |
| `Venue` | `string?` | Exchange identifier | No | Any string | Yes | `"XNAS"` |

**Error Codes:**

| Code | Name | Description |
|------|------|-------------|
| `1001` | SequenceGap | Gap in sequence numbers (expected vs. received) |
| `1002` | OutOfOrder | Message received out of sequence order |

**Factory Methods:**
- `IntegrityEvent.SequenceGap(timestamp, symbol, expectedNext, received)` → ErrorCode: 1001
- `IntegrityEvent.OutOfOrder(timestamp, symbol, lastSeq, received)` → ErrorCode: 1002

**Sample JSON:**
```json
{
  "kind": "integrity",
  "timestamp": "2026-01-09T14:30:00Z",
  "symbol": "AAPL",
  "severity": "Warning",
  "description": "Sequence gap detected: expected 12346, received 12350",
  "errorCode": 1001,
  "sequenceNumber": 12350
}
```

---

### DepthIntegrityEvent

Order book integrity violations (gaps, out-of-order, invalid positions, stale data).

**Location:** `src/Meridian.Contracts/Domain/Models/DepthIntegrityEvent.cs`
**JSON Kind:** `"depth_integrity"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | When violation was detected | Yes | Any valid datetime | No | `2026-01-09T14:30:00Z` |
| `Symbol` | `string` | Affected trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `Kind` | `DepthIntegrityKind` | Type of integrity violation | Yes | See [DepthIntegrityKind](#depthintegritykind) | No | `Gap` |
| `Description` | `string` | Detailed error description | Yes | Non-empty | No | `"Gap at position 5"` |
| `Position` | `int` | Level/position of offending update | Yes | >= 0 | No | `5` |
| `Operation` | `DepthOperation` | Operation that caused the violation | Yes | `Insert`, `Update`, `Delete` | No | `Insert` |
| `Side` | `OrderBookSide` | Bid or Ask side | Yes | `Bid`, `Ask` | No | `Bid` |
| `SequenceNumber` | `long` | Violation sequence number | Yes | >= 0 | No | `12345` |
| `StreamId` | `string?` | Stream identifier | No | Any string | Yes | `"DEPTH-001"` |
| `Venue` | `string?` | Exchange identifier | No | Any string | Yes | `"XNAS"` |

**Response:** Symbol is frozen until operator calls `ResetSymbolStream(symbol)`

**Sample JSON:**
```json
{
  "kind": "depth_integrity",
  "timestamp": "2026-01-09T14:30:00Z",
  "symbol": "AAPL",
  "kind": "Gap",
  "description": "Gap detected at position 5 on Bid side",
  "position": 5,
  "operation": "Insert",
  "side": "Bid",
  "sequenceNumber": 12345
}
```

---

## Historical Event Types

Event types for historical/backfill data from various providers.

### HistoricalBar

Daily OHLCV (Open-High-Low-Close-Volume) bars from backfill providers.

**Location:** `src/Meridian.Contracts/Domain/Models/HistoricalBar.cs`
**JSON Kind:** `"historical_bar"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `SessionDate` | `DateOnly` | Trading session date | Yes | Valid date | No | `2026-01-08` |
| `Open` | `decimal` | Opening price | Yes | > 0 | No | `185.00` |
| `High` | `decimal` | Highest price during session | Yes | > 0 | No | `186.50` |
| `Low` | `decimal` | Lowest price during session | Yes | > 0 | No | `184.25` |
| `Close` | `decimal` | Closing price | Yes | > 0 | No | `185.75` |
| `Volume` | `long` | Total volume during session | Yes | >= 0 | No | `45000000` |
| `Source` | `string` | Provider name | No | Non-empty | No | `"stooq"` |
| `SequenceNumber` | `long` | Event sequence | No | >= 0 | No | `1` |

**Validation Rules:**
- All OHLC values must be > 0
- `Low` <= `Open`, `Close`, `High`
- `High` >= `Open`, `Close`, `Low`
- `Volume` >= 0

**Helper Methods:**
- `ToTimestampUtc()`: Converts SessionDate to DateTimeOffset at UTC midnight

**Sample JSON:**
```json
{
  "kind": "historical_bar",
  "symbol": "AAPL",
  "sessionDate": "2026-01-08",
  "open": 185.00,
  "high": 186.50,
  "low": 184.25,
  "close": 185.75,
  "volume": 45000000,
  "source": "stooq",
  "sequenceNumber": 1
}
```

---

### HistoricalQuote

National Best Bid and Offer (NBBO) quote data from historical providers.

**Location:** `src/Meridian.Contracts/Domain/Models/HistoricalQuote.cs`
**JSON Kind:** `"historical_quote"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `Timestamp` | `DateTimeOffset` | Quote timestamp | Yes | Any valid datetime | No | `2026-01-08T14:30:00Z` |
| `BidExchange` | `string` | Exchange with best bid | Yes | Exchange code | No | `"XNAS"` |
| `BidPrice` | `decimal` | Best bid price | Yes | >= 0 | No | `185.24` |
| `BidSize` | `long` | Best bid quantity | Yes | >= 0 | No | `500` |
| `AskExchange` | `string` | Exchange with best ask | Yes | Exchange code | No | `"XNYS"` |
| `AskPrice` | `decimal` | Best ask price | Yes | >= 0 | No | `185.25` |
| `AskSize` | `long` | Best ask quantity | Yes | >= 0 | No | `400` |
| `Conditions` | `IReadOnlyList<string>?` | Quote condition codes | No | See [Quote Conditions](#quote-conditions) | Yes | `["Q"]` |
| `Tape` | `string?` | Tape identifier | No | See [Tape Identifiers](#tape-identifiers) | Yes | `"A"` |
| `Source` | `string` | Provider name | No | Non-empty | No | `"alpaca"` |
| `SequenceNumber` | `long` | Event sequence | No | >= 0 | No | `12345` |

**Calculated Properties:**
- `Spread` = AskPrice - BidPrice
- `MidPrice` = (AskPrice + BidPrice) / 2
- `SpreadBps` = (Spread / MidPrice) * 10000 (spread in basis points)

**Sample JSON:**
```json
{
  "kind": "historical_quote",
  "symbol": "AAPL",
  "timestamp": "2026-01-08T14:30:00Z",
  "bidExchange": "XNAS",
  "bidPrice": 185.24,
  "bidSize": 500,
  "askExchange": "XNYS",
  "askPrice": 185.25,
  "askSize": 400,
  "conditions": ["Q"],
  "tape": "A",
  "source": "alpaca",
  "sequenceNumber": 12345
}
```

---

### HistoricalTrade

Individual executed trades from historical data providers.

**Location:** `src/Meridian.Contracts/Domain/Models/HistoricalTrade.cs`
**JSON Kind:** `"historical_trade"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `Timestamp` | `DateTimeOffset` | Trade timestamp | Yes | Any valid datetime | No | `2026-01-08T14:30:00Z` |
| `Exchange` | `string` | Exchange where trade occurred | Yes | Exchange code | No | `"XNAS"` |
| `Price` | `decimal` | Trade price | Yes | > 0 | No | `185.25` |
| `Size` | `long` | Trade quantity | Yes | > 0 | No | `100` |
| `TradeId` | `string` | Unique trade identifier | Yes | Non-empty | No | `"T123456789"` |
| `Conditions` | `IReadOnlyList<string>?` | Trade condition codes | No | See [Trade Conditions](#trade-conditions) | Yes | `["@", "F"]` |
| `Tape` | `string?` | Tape identifier | No | See [Tape Identifiers](#tape-identifiers) | Yes | `"A"` |
| `Source` | `string` | Provider name | No | Non-empty | No | `"alpaca"` |
| `SequenceNumber` | `long` | Event sequence | No | >= 0 | No | `12345` |

**Calculated Properties:**
- `NotionalValue` = Price * Size

**Validation Rules:**
- `Symbol` and `TradeId` required and non-empty
- `Price` > 0
- `Size` > 0

**Sample JSON:**
```json
{
  "kind": "historical_trade",
  "symbol": "AAPL",
  "timestamp": "2026-01-08T14:30:00.123456789Z",
  "exchange": "XNAS",
  "price": 185.25,
  "size": 100,
  "tradeId": "T123456789",
  "conditions": ["@"],
  "tape": "A",
  "source": "alpaca",
  "sequenceNumber": 12345
}
```

---

### HistoricalAuction

Opening and closing auction information for trading sessions.

**Location:** `src/Meridian.Contracts/Domain/Models/HistoricalAuction.cs`
**JSON Kind:** `"historical_auction"`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Symbol` | `string` | Trading symbol | Yes | Non-empty | No | `"AAPL"` |
| `SessionDate` | `DateOnly` | Trading session date | Yes | Valid date | No | `2026-01-08` |
| `OpeningAuctions` | `IReadOnlyList<AuctionPrice>` | Opening auction prices | Yes | - | No | See [AuctionPrice](#auctionprice) |
| `ClosingAuctions` | `IReadOnlyList<AuctionPrice>` | Closing auction prices | Yes | - | No | See [AuctionPrice](#auctionprice) |
| `Source` | `string` | Provider name | No | Non-empty | No | `"alpaca"` |
| `SequenceNumber` | `long` | Event sequence | No | >= 0 | No | `1` |

**Calculated Properties:**
- `PrimaryOpenPrice`: First opening auction price
- `PrimaryOpenVolume`: First opening auction volume
- `PrimaryClosePrice`: First closing auction price
- `PrimaryCloseVolume`: First closing auction volume
- `TotalOpeningVolume`: Sum of all opening auction sizes
- `TotalClosingVolume`: Sum of all closing auction sizes

**Sample JSON:**
```json
{
  "kind": "historical_auction",
  "symbol": "AAPL",
  "sessionDate": "2026-01-08",
  "openingAuctions": [
    { "timestamp": "2026-01-08T14:30:00Z", "price": 185.00, "size": 50000, "exchange": "XNAS" }
  ],
  "closingAuctions": [
    { "timestamp": "2026-01-08T21:00:00Z", "price": 185.75, "size": 75000, "exchange": "XNAS" }
  ],
  "source": "alpaca",
  "sequenceNumber": 1
}
```

---

## Supporting Types

### OrderBookLevel

Represents a single price level in the order book.

**Location:** `src/Meridian.Contracts/Domain/Models/OrderBookLevel.cs`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Side` | `OrderBookSide` | Bid or Ask | Yes | `Bid`, `Ask` | No | `Bid` |
| `Level` | `int` | Level in book (0 = best) | Yes | >= 0 | No | `0` |
| `Price` | `decimal` | Price at this level | Yes | > 0 | No | `185.24` |
| `Size` | `decimal` | Total quantity at this level | Yes | >= 0 | No | `500` |
| `MarketMaker` | `string?` | Market maker identifier (NASDAQ) | No | Any string | Yes | `"GSCO"` |

**Sample JSON:**
```json
{
  "side": "Bid",
  "level": 0,
  "price": 185.24,
  "size": 500,
  "marketMaker": "GSCO"
}
```

---

### AuctionPrice

Represents a single auction price point.

**Location:** `src/Meridian.Contracts/Domain/Models/HistoricalAuction.cs`

| Field | Type | Description | Required | Valid Range | Nullable | Example |
|-------|------|-------------|----------|-------------|----------|---------|
| `Timestamp` | `DateTimeOffset` | Auction timestamp | Yes | Any valid datetime | No | `2026-01-08T14:30:00Z` |
| `Price` | `decimal` | Auction price | Yes | > 0 | No | `185.00` |
| `Size` | `long` | Auction quantity | Yes | >= 0 | No | `50000` |
| `Exchange` | `string?` | Exchange identifier | No | Exchange code | Yes | `"XNAS"` |
| `Condition` | `string?` | Auction condition | No | Any string | Yes | `"O"` |

---

## Adapter Input Models

These models are used as normalized inputs to collectors but are not stored as event payloads.

### MarketTradeUpdate

**Location:** `src/Meridian.Domain/Models/MarketTradeUpdate.cs`

Input to `TradeDataCollector`; converted to `Trade` payload.

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | Trade timestamp |
| `Symbol` | `string` | Trading symbol |
| `Price` | `decimal` | Trade price |
| `Size` | `long` | Trade size |
| `Aggressor` | `AggressorSide` | Aggressor side |
| `SequenceNumber` | `long` | Sequence number |
| `StreamId` | `string?` | Stream identifier |
| `Venue` | `string?` | Exchange identifier |

---

### MarketQuoteUpdate

**Location:** `src/Meridian.Contracts/Domain/Models/MarketQuoteUpdate.cs`

Input to `QuoteCollector`; converted to `BboQuotePayload`.

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | Quote timestamp |
| `Symbol` | `string` | Trading symbol |
| `BidPrice` | `decimal` | Best bid price |
| `BidSize` | `long` | Best bid size |
| `AskPrice` | `decimal` | Best ask price |
| `AskSize` | `long` | Best ask size |
| `SequenceNumber` | `long?` | Sequence number |
| `StreamId` | `string?` | Stream identifier |
| `Venue` | `string?` | Exchange identifier |

---

### MarketDepthUpdate

**Location:** `src/Meridian.Domain/Models/MarketDepthUpdate.cs`

Input to `MarketDepthCollector`; applied to order book state.

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | Update timestamp |
| `Symbol` | `string` | Trading symbol |
| `Position` | `int` | Level position in book |
| `Operation` | `DepthOperation` | Insert, Update, or Delete |
| `Side` | `OrderBookSide` | Bid or Ask |
| `Price` | `decimal` | Price at level |
| `Size` | `decimal` | Size at level |
| `MarketMaker` | `string?` | Market maker ID |
| `SequenceNumber` | `long` | Sequence number |
| `StreamId` | `string?` | Stream identifier |
| `Venue` | `string?` | Exchange identifier |

---

## Enumerations

### MarketEventType

Discriminator for event types in the `MarketEvent` container.

**Location:** `src/Meridian.Contracts/Domain/Enums/MarketEventType.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Unknown` | `0` | Unknown or uninitialized event type |
| `L2Snapshot` | `1` | Level-2 order book snapshot |
| `BboQuote` | `2` | Best bid/offer quote |
| `Trade` | `3` | Trade execution |
| `OrderFlow` | `4` | Order flow statistics |
| `Heartbeat` | `5` | Connection heartbeat |
| `ConnectionStatus` | `6` | Connection state change |
| `Integrity` | `7` | Data integrity event |
| `HistoricalBar` | `8` | Historical OHLCV bar |
| `HistoricalQuote` | `9` | Historical NBBO quote |
| `HistoricalTrade` | `10` | Historical trade print |
| `HistoricalAuction` | `11` | Historical auction data |
| `AggregateBar` | `12` | Real-time aggregate OHLCV bar |
| `Quote` | `13` | Quote update event |
| `Depth` | `14` | Order book depth update |
| `OptionQuote` | `15` | Option quote with greeks/IV |
| `OptionTrade` | `16` | Option trade execution |
| `OptionGreeks` | `17` | Option greeks snapshot |
| `OptionChain` | `18` | Option chain snapshot |
| `OpenInterest` | `19` | Option open interest update |

---

### AggressorSide

Indicates which side initiated a trade.

**Location:** `src/Meridian.Contracts/Domain/Enums/AggressorSide.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Unknown` | `0` | Unable to determine aggressor |
| `Buy` | `1` | Buyer-initiated (hit the ask) |
| `Sell` | `2` | Seller-initiated (hit the bid) |

**Inference Logic:**
- Trade price >= Ask price → `Buy`
- Trade price <= Bid price → `Sell`
- Otherwise → `Unknown`

---

### OrderBookSide

Order book side indicator.

**Location:** `src/Meridian.Contracts/Domain/Enums/OrderBookSide.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Bid` | `0` | Buy side / demand |
| `Ask` | `1` | Sell side / supply |

---

### DepthOperation

Order book update operation type.

**Location:** `src/Meridian.Contracts/Domain/Enums/DepthOperation.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Insert` | `0` | Insert new level at position |
| `Update` | `1` | Update existing level |
| `Delete` | `2` | Remove level at position |

**Note:** Aligns with Interactive Brokers conventions.

---

### IntegritySeverity

Severity level for integrity events.

**Location:** `src/Meridian.Contracts/Domain/Enums/IntegritySeverity.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Info` | `0` | Informational only |
| `Warning` | `1` | Potential issue, non-critical |
| `Error` | `2` | Error requiring attention |
| `Critical` | `3` | Critical error (F# only) |

---

### DepthIntegrityKind

Type of order book integrity violation.

**Location:** `src/Meridian.Contracts/Domain/Enums/DepthIntegrityKind.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Unknown` | `0` | Unknown violation type |
| `Gap` | `1` | Gap in order book levels |
| `OutOfOrder` | `2` | Updates received out of order |
| `InvalidPosition` | `3` | Invalid position index |
| `Stale` | `4` | Data is stale/outdated |

---

### MarketState

Current trading state of the market.

**Location:** `src/Meridian.Contracts/Domain/Enums/MarketState.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Normal` | `0` | Normal trading |
| `Closed` | `1` | Market closed |
| `Halted` | `2` | Trading halted |
| `Unknown` | `3` | Unknown state |

---

### MarketEventTier

Processing tier classification for events.

**Location:** `src/Meridian.Contracts/Domain/Enums/MarketEventTier.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Raw` | `0` | Raw events from data source |
| `Derived` | `1` | Enriched/calculated events |

---

### ConnectionStatus

Connection state for data providers.

**Location:** `src/Meridian.Contracts/Domain/Enums/ConnectionStatus.cs`

| Value | Code | Description |
|-------|------|-------------|
| `Disconnected` | `0` | Not connected |
| `Connecting` | `1` | Connection in progress |
| `Connected` | `2` | Successfully connected |
| `Reconnecting` | `3` | Attempting reconnection |
| `Faulted` | `4` | Connection failed/faulted |

---

## Exchange Codes

Standard exchange identifiers used in the system. Based on Market Identifier Codes (MIC).

| Code | Exchange Name | Description |
|------|---------------|-------------|
| `XNAS` | NASDAQ Stock Market | Primary NASDAQ listing venue |
| `XNYS` | New York Stock Exchange | Primary NYSE listing venue |
| `ARCX` | NYSE Arca | NYSE Arca electronic exchange |
| `XASE` | NYSE American (AMEX) | NYSE American (formerly AMEX) |
| `BATS` | CBOE BZX Exchange | Cboe BZX Exchange |
| `BATY` | CBOE BYX Exchange | Cboe BYX Exchange |
| `EDGA` | CBOE EDGA Exchange | Cboe EDGA Exchange |
| `EDGX` | CBOE EDGX Exchange | Cboe EDGX Exchange |
| `IEXG` | IEX Exchange | Investors Exchange |
| `XCHI` | Chicago Stock Exchange | Chicago Stock Exchange |
| `XPHL` | NASDAQ OMX PHLX | NASDAQ PHLX (Philadelphia) |
| `XBOS` | NASDAQ OMX BX | NASDAQ BX (Boston) |
| `MEMX` | Members Exchange | Members Exchange |
| `LTSE` | Long-Term Stock Exchange | Long-Term Stock Exchange |

**Usage:** Exchange codes appear in the `Exchange`, `BidExchange`, `AskExchange`, and `Venue` fields.

---

## Trade Conditions

Trade condition codes indicating special circumstances of a trade. May appear in the `Conditions` array field.

| Code | Name | Description |
|------|------|-------------|
| `@` | Regular Sale | Standard trade, included in last sale price |
| `A` | Acquisition | Trade resulting from acquisition |
| `B` | Bunched Trade | Multiple orders executed as single trade |
| `C` | Cash Sale | Same-day settlement |
| `D` | Distribution | Distribution trade |
| `E` | Automatic Execution | Electronically executed |
| `F` | Intermarket Sweep | Intermarket sweep order |
| `G` | Opening/Reopening Trade Detail | Opening or reopening print |
| `H` | Intraday Trade Detail | Intraday trade detail |
| `I` | CAP Election Trade | CAP election trade |
| `K` | Rule 155 Trade (NYSE AMEX) | NYSE AMEX Rule 155 trade |
| `L` | Sold Last | Reported late, out of sequence |
| `M` | Market Center Close Price | Market center official close |
| `N` | Next Day | Next day settlement |
| `O` | Opening Trade Detail | Opening trade detail |
| `P` | Prior Reference Price | Prior reference price |
| `Q` | Market Center Open Price | Market center official open |
| `R` | Seller | Seller's option |
| `S` | Split Trade | Trade split across multiple reports |
| `T` | Form T | Extended hours trade (Form T) |
| `U` | Extended Trading Hours (Sold Out of Sequence) | Extended hours, out of sequence |
| `V` | Contingent Trade | Contingent trade |
| `W` | Average Price Trade | Average price trade |
| `X` | Cross Trade | Cross trade |
| `Y` | Yellow Flag Regular Trade | Yellow flag regular trade |
| `Z` | Sold (Out of Sequence) | Reported out of sequence |
| `1` | Stopped Stock (Regular Trade) | Stopped stock |
| `4` | Derivatively Priced | Derivatively priced |
| `5` | Re-Opening Prints | Re-opening prints |
| `6` | Closing Prints | Closing prints |
| `7` | Qualified Contingent Trade | QCT trade |
| `8` | Placeholder For 611 Exempt | Reg NMS 611 exempt |
| `9` | Corrected Consolidated Close | Corrected consolidated close |

**Important Notes:**
- Multiple conditions can apply to a single trade
- Condition `@` (Regular Sale) is the most common
- Conditions `T` and `U` indicate extended hours trading
- Conditions `L`, `U`, `Z` indicate out-of-sequence reporting

---

## Quote Conditions

Quote condition codes indicating special circumstances of a quote. May appear in the `Conditions` array field.

| Code | Name | Description |
|------|------|-------------|
| `A` | Slow Quote Offer Side | Slow quote, offer side |
| `B` | Slow Quote Bid Side | Slow quote, bid side |
| `C` | Closing | Closing quote |
| `D` | News Dissemination | News being disseminated |
| `E` | Slow Quote LRP Bid Side | Slow quote LRP, bid side |
| `F` | Slow Quote LRP Offer Side | Slow quote LRP, offer side |
| `G` | Slow Quote Bid and Offer Side | Slow quote, both sides |
| `H` | Slow Quote LRP Bid and Offer Side | Slow quote LRP, both sides |
| `I` | Order Imbalance | Order imbalance exists |
| `J` | Due to Related Security - News Dissemination | Related security news |
| `K` | Due to Related Security - News Pending | Related security news pending |
| `L` | Additional Information | Additional information available |
| `M` | Non-Firm Quote | Quote is non-firm |
| `N` | News Pending | News pending |
| `O` | Opening | Opening quote |
| `P` | Additional Information - Due to Related Security | Related security additional info |
| `Q` | Regular | Regular/normal quote |
| `R` | Rotation | Market rotation |
| `S` | Suspended Trading | Trading suspended |
| `T` | Trading Range Indication | Trading range indication |
| `U` | Slow Quote On Bid And Offer (No Firm Quote) | Slow quote, no firm quote |
| `V` | Slow Quote Set Slow List | Slow quote, on slow list |
| `W` | Slow Quote LRP Bid Side And Offer Side | Slow quote LRP, both sides |
| `X` | Closed | Market closed |
| `Y` | Slow Quote Demand Side | Slow quote, demand side |
| `Z` | Slow Quote No Reason | Slow quote, no specific reason |
| `0` | No Special Condition | No special condition |
| `1` | Manual/Slow Quote | Manual or slow quote |
| `2` | Fast Trading | Fast trading |
| `3` | Rotation | Market rotation |

**Important Notes:**
- `Q` (Regular) is the most common condition
- Conditions `A`, `B`, `G`, `H`, `U`, `V`, `W`, `Y`, `Z`, `1` indicate slow quotes
- Condition `S` indicates trading is suspended
- Condition `X` indicates market is closed

---

## Tape Identifiers

Consolidated tape identifiers for U.S. equity markets.

| Tape | Name | Securities |
|------|------|------------|
| `A` | Tape A | NYSE-listed securities |
| `B` | Tape B | NYSE Arca, BATS, regional exchange-listed securities |
| `C` | Tape C | NASDAQ-listed securities |

**Usage:** The `Tape` field in HistoricalTrade and HistoricalQuote indicates which consolidated tape the data belongs to.

---

## JSON Serialization

### Polymorphic Serialization

Event payloads use JSON polymorphic serialization with a `"kind"` discriminator property.

**JSON Type Discriminators:**

| Event Type | JSON Kind Value |
|------------|-----------------|
| Trade | `"trade"` |
| LOBSnapshot | `"l2"` |
| BboQuotePayload | `"bbo"` |
| OrderFlowStatistics | `"orderflow"` |
| IntegrityEvent | `"integrity"` |
| DepthIntegrityEvent | `"depth_integrity"` |
| HistoricalBar | `"historical_bar"` |
| HistoricalQuote | `"historical_quote"` |
| HistoricalTrade | `"historical_trade"` |
| HistoricalAuction | `"historical_auction"` |
| L2SnapshotPayload | `"l2payload"` |

### Serialization Settings

The system uses `System.Text.Json` with the following default settings:
- `PropertyNamingPolicy`: camelCase
- `WriteIndented`: false (production), true (export)
- `DefaultIgnoreCondition`: WhenWritingNull

### Complete Event Example

```json
{
  "timestamp": "2026-01-09T14:30:00.123456789Z",
  "symbol": "AAPL",
  "type": "Trade",
  "sequence": 123456789,
  "source": "Alpaca",
  "schemaVersion": 1,
  "tier": "Raw",
  "payload": {
    "kind": "trade",
    "timestamp": "2026-01-09T14:30:00.123456789Z",
    "symbol": "AAPL",
    "price": 185.25,
    "size": 100,
    "aggressor": "Buy",
    "sequenceNumber": 123456789,
    "venue": "XNAS"
  }
}
```

---

## F# Domain Types

For F# implementations, corresponding discriminated unions and types are available in:
`src/Meridian.FSharp/Domain/`

Key F# types:
- `MarketEvent` (discriminated union)
- `TradeEvent`, `QuoteEvent`, `DepthEvent`, `BarEvent` (records)
- `IntegrityEvent` with `IntegrityEventType` (discriminated union for detailed error types)

See `docs/ai/claude/CLAUDE.fsharp.md` for F# domain model details.

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.0.0 | 2026-01-09 | Added HistoricalQuote, HistoricalTrade, HistoricalAuction; comprehensive exchange codes |
| 1.5.0 | 2025-10-01 | Added OrderFlowStatistics, DepthIntegrityEvent |
| 1.0.0 | 2025-01-01 | Initial release with Trade, LOBSnapshot, BboQuote, IntegrityEvent, HistoricalBar |

---

*Generated for Meridian v1.0.0 (repository snapshot)*
