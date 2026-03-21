/// Market event types with type-safe discriminated unions.
/// Provides exhaustive pattern matching for all event types
/// and eliminates null reference exceptions.
module Meridian.FSharp.Domain.MarketEvents

open System
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity

/// Trade event representing a single executed trade.
[<CLIMutable>]
type TradeEvent = {
    /// Trading symbol (e.g., "AAPL", "SPY")
    Symbol: string
    /// Trade execution price
    Price: decimal
    /// Trade size/quantity
    Quantity: int64
    /// Inferred aggressor side
    Side: AggressorSide
    /// Sequence number for ordering
    SequenceNumber: int64
    /// Local timestamp when event was received
    Timestamp: DateTimeOffset
    /// Exchange timestamp (if available)
    ExchangeTimestamp: DateTimeOffset option
    /// Stream identifier for multi-stream reconciliation
    StreamId: string option
    /// Venue/exchange identifier
    Venue: string option
}

/// Quote event representing best bid/offer update.
[<CLIMutable>]
type QuoteEvent = {
    /// Trading symbol
    Symbol: string
    /// Best bid price
    BidPrice: decimal
    /// Best bid size
    BidSize: int64
    /// Best ask price
    AskPrice: decimal
    /// Best ask size
    AskSize: int64
    /// Sequence number for ordering
    SequenceNumber: int64
    /// Local timestamp
    Timestamp: DateTimeOffset
    /// Exchange timestamp (if available)
    ExchangeTimestamp: DateTimeOffset option
    /// Stream identifier
    StreamId: string option
}

/// Order book level with price, quantity, and order count.
[<CLIMutable>]
type BookLevel = {
    /// Price level
    Price: decimal
    /// Total quantity at this level
    Quantity: int64
    /// Number of orders at this level
    OrderCount: int
}

/// Market depth event representing order book snapshot or update.
[<CLIMutable>]
type DepthEvent = {
    /// Trading symbol
    Symbol: string
    /// Side of the book (bid or ask)
    Side: Side
    /// Depth level (0 = top of book)
    Level: int
    /// Price at this level
    Price: decimal
    /// Quantity at this level
    Quantity: int64
    /// Number of orders at this level
    OrderCount: int
    /// Sequence number for ordering
    SequenceNumber: int64
    /// Local timestamp
    Timestamp: DateTimeOffset
    /// Stream identifier
    StreamId: string option
}

/// Complete order book snapshot.
[<CLIMutable>]
type OrderBookSnapshot = {
    /// Trading symbol
    Symbol: string
    /// Bid side levels (sorted by price descending)
    Bids: BookLevel list
    /// Ask side levels (sorted by price ascending)
    Asks: BookLevel list
    /// Sequence number for ordering
    SequenceNumber: int64
    /// Snapshot timestamp
    Timestamp: DateTimeOffset
    /// Stream identifier
    StreamId: string option
}

/// Historical bar (OHLCV) data.
[<CLIMutable>]
type BarEvent = {
    /// Trading symbol
    Symbol: string
    /// Bar open price
    Open: decimal
    /// Bar high price
    High: decimal
    /// Bar low price
    Low: decimal
    /// Bar close price
    Close: decimal
    /// Bar volume
    Volume: int64
    /// Bar start timestamp
    Timestamp: DateTimeOffset
    /// Bar period in seconds
    PeriodSeconds: int
}

/// Market event type enumeration for pattern matching.
[<RequireQualifiedAccess>]
type MarketEventType =
    | Trade
    | Quote
    | Depth
    | OrderBook
    | Bar
    | Integrity
    | Heartbeat

/// Unified market event with type-safe payload.
/// Uses discriminated unions to ensure exhaustive handling
/// of all event types.
[<RequireQualifiedAccess>]
type MarketEvent =
    /// Trade execution event
    | Trade of TradeEvent
    /// Best bid/offer quote update
    | Quote of QuoteEvent
    /// Order book depth update
    | Depth of DepthEvent
    /// Complete order book snapshot
    | OrderBook of OrderBookSnapshot
    /// Historical bar data
    | Bar of BarEvent
    /// Data integrity event
    | Integrity of IntegrityEvent
    /// System heartbeat
    | Heartbeat of timestamp: DateTimeOffset * source: string

/// Helper functions for working with market events.
module MarketEvent =

    /// Get the symbol from any market event
    [<CompiledName("GetSymbol")>]
    let getSymbol (event: MarketEvent) : string option =
        match event with
        | MarketEvent.Trade t -> Some t.Symbol
        | MarketEvent.Quote q -> Some q.Symbol
        | MarketEvent.Depth d -> Some d.Symbol
        | MarketEvent.OrderBook ob -> Some ob.Symbol
        | MarketEvent.Bar b -> Some b.Symbol
        | MarketEvent.Integrity i -> Some i.Symbol
        | MarketEvent.Heartbeat _ -> None

    /// Get the timestamp from any market event
    [<CompiledName("GetTimestamp")>]
    let getTimestamp (event: MarketEvent) : DateTimeOffset =
        match event with
        | MarketEvent.Trade t -> t.Timestamp
        | MarketEvent.Quote q -> q.Timestamp
        | MarketEvent.Depth d -> d.Timestamp
        | MarketEvent.OrderBook ob -> ob.Timestamp
        | MarketEvent.Bar b -> b.Timestamp
        | MarketEvent.Integrity i -> i.Timestamp
        | MarketEvent.Heartbeat (ts, _) -> ts

    /// Get the sequence number from any market event
    [<CompiledName("GetSequenceNumber")>]
    let getSequenceNumber (event: MarketEvent) : int64 option =
        match event with
        | MarketEvent.Trade t -> Some t.SequenceNumber
        | MarketEvent.Quote q -> Some q.SequenceNumber
        | MarketEvent.Depth d -> Some d.SequenceNumber
        | MarketEvent.OrderBook ob -> Some ob.SequenceNumber
        | MarketEvent.Bar _ -> None
        | MarketEvent.Integrity i -> Some i.SequenceNumber
        | MarketEvent.Heartbeat _ -> None

    /// Get the event type
    [<CompiledName("GetEventType")>]
    let getEventType (event: MarketEvent) : MarketEventType =
        match event with
        | MarketEvent.Trade _ -> MarketEventType.Trade
        | MarketEvent.Quote _ -> MarketEventType.Quote
        | MarketEvent.Depth _ -> MarketEventType.Depth
        | MarketEvent.OrderBook _ -> MarketEventType.OrderBook
        | MarketEvent.Bar _ -> MarketEventType.Bar
        | MarketEvent.Integrity _ -> MarketEventType.Integrity
        | MarketEvent.Heartbeat _ -> MarketEventType.Heartbeat

    /// Check if event is a trade
    [<CompiledName("IsTrade")>]
    let isTrade (event: MarketEvent) : bool =
        match event with
        | MarketEvent.Trade _ -> true
        | _ -> false

    /// Check if event is a quote
    [<CompiledName("IsQuote")>]
    let isQuote (event: MarketEvent) : bool =
        match event with
        | MarketEvent.Quote _ -> true
        | _ -> false

    /// Check if event is an integrity event
    [<CompiledName("IsIntegrity")>]
    let isIntegrity (event: MarketEvent) : bool =
        match event with
        | MarketEvent.Integrity _ -> true
        | _ -> false

    /// Create a trade event
    [<CompiledName("CreateTrade")>]
    let createTrade symbol price quantity side seqNum timestamp =
        MarketEvent.Trade {
            Symbol = symbol
            Price = price
            Quantity = quantity
            Side = side
            SequenceNumber = seqNum
            Timestamp = timestamp
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }

    /// Create a quote event
    [<CompiledName("CreateQuote")>]
    let createQuote symbol bidPrice bidSize askPrice askSize seqNum timestamp =
        MarketEvent.Quote {
            Symbol = symbol
            BidPrice = bidPrice
            BidSize = bidSize
            AskPrice = askPrice
            AskSize = askSize
            SequenceNumber = seqNum
            Timestamp = timestamp
            ExchangeTimestamp = None
            StreamId = None
        }

    /// Create a heartbeat event
    [<CompiledName("CreateHeartbeat")>]
    let createHeartbeat timestamp source =
        MarketEvent.Heartbeat(timestamp, source)
