/// Integrity event types for data quality monitoring.
/// Uses discriminated unions to model different integrity issues
/// with their associated data, ensuring exhaustive pattern matching.
module Meridian.FSharp.Domain.Integrity

open System

/// Severity level for integrity events.
[<RequireQualifiedAccess>]
type IntegritySeverity =
    /// Informational event, no action required
    | Info
    /// Warning event, may indicate potential issues
    | Warning
    /// Error event, indicates data quality problem
    | Error
    /// Critical event, requires immediate attention
    | Critical

    /// Convert to integer for C# interop
    member this.ToInt() =
        match this with
        | Info -> 0
        | Warning -> 1
        | Error -> 2
        | Critical -> 3

    /// Create from integer for C# interop
    static member FromInt(value: int) =
        match value with
        | 0 -> Info
        | 1 -> Warning
        | 2 -> Error
        | 3 -> Critical
        | _ -> Warning

/// Integrity event types with associated data.
/// Each case carries the specific data needed to describe that issue.
[<RequireQualifiedAccess>]
type IntegrityEventType =
    /// Sequence number gap detected (missing messages)
    | SequenceGap of expected: int64 * received: int64
    /// Messages received out of order
    | OutOfOrder of lastSeq: int64 * receivedSeq: int64
    /// Negative bid-ask spread (crossed market)
    | NegativeSpread of bid: decimal * ask: decimal
    /// Order book is crossed at the specified level
    | BookCrossed of level: int
    /// Quote data is stale (no updates for specified duration)
    | StaleQuote of staleDuration: TimeSpan
    /// Duplicate message received
    | DuplicateMessage of sequenceNumber: int64
    /// Price spike detected (abnormal price movement)
    | PriceSpike of previousPrice: decimal * currentPrice: decimal * thresholdPercent: decimal
    /// Volume spike detected (abnormal volume)
    | VolumeSpike of previousVolume: int64 * currentVolume: int64 * thresholdMultiple: decimal
    /// Connection issue detected
    | ConnectionIssue of reason: string
    /// Generic data quality issue
    | DataQualityIssue of description: string

/// Complete integrity event with symbol, timestamp, and event details.
type IntegrityEvent = {
    /// Symbol this event relates to
    Symbol: string
    /// When the integrity issue was detected
    Timestamp: DateTimeOffset
    /// Severity of the integrity issue
    Severity: IntegritySeverity
    /// Specific type of integrity event with associated data
    EventType: IntegrityEventType
    /// Sequence number for event ordering
    SequenceNumber: int64
    /// Optional stream identifier for multi-stream scenarios
    StreamId: string option
    /// Optional venue/exchange identifier
    Venue: string option
}

/// Smart constructors for creating well-formed integrity events.
module IntegrityEvent =

    /// Create a sequence gap event
    [<CompiledName("CreateSequenceGap")>]
    let sequenceGap symbol timestamp expected received sequenceNumber streamId venue =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Error
          EventType = IntegrityEventType.SequenceGap(expected, received)
          SequenceNumber = sequenceNumber
          StreamId = streamId
          Venue = venue }

    /// Create an out-of-order event
    [<CompiledName("CreateOutOfOrder")>]
    let outOfOrder symbol timestamp lastSeq receivedSeq sequenceNumber streamId venue =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Warning
          EventType = IntegrityEventType.OutOfOrder(lastSeq, receivedSeq)
          SequenceNumber = sequenceNumber
          StreamId = streamId
          Venue = venue }

    /// Create a negative spread event
    [<CompiledName("CreateNegativeSpread")>]
    let negativeSpread symbol timestamp bid ask sequenceNumber =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Error
          EventType = IntegrityEventType.NegativeSpread(bid, ask)
          SequenceNumber = sequenceNumber
          StreamId = None
          Venue = None }

    /// Create a book crossed event
    [<CompiledName("CreateBookCrossed")>]
    let bookCrossed symbol timestamp level sequenceNumber =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Error
          EventType = IntegrityEventType.BookCrossed(level)
          SequenceNumber = sequenceNumber
          StreamId = None
          Venue = None }

    /// Create a stale quote event
    [<CompiledName("CreateStaleQuote")>]
    let staleQuote symbol timestamp staleDuration sequenceNumber =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Warning
          EventType = IntegrityEventType.StaleQuote(staleDuration)
          SequenceNumber = sequenceNumber
          StreamId = None
          Venue = None }

    /// Create a price spike event
    [<CompiledName("CreatePriceSpike")>]
    let priceSpike symbol timestamp prevPrice currPrice threshold sequenceNumber =
        { Symbol = symbol
          Timestamp = timestamp
          Severity = IntegritySeverity.Warning
          EventType = IntegrityEventType.PriceSpike(prevPrice, currPrice, threshold)
          SequenceNumber = sequenceNumber
          StreamId = None
          Venue = None }

    /// Get a human-readable description of the integrity event
    [<CompiledName("GetDescription")>]
    let getDescription (event: IntegrityEvent) : string =
        match event.EventType with
        | IntegrityEventType.SequenceGap(expected, received) ->
            $"Sequence gap: expected {expected} but received {received}"
        | IntegrityEventType.OutOfOrder(lastSeq, receivedSeq) ->
            $"Out-of-order message: last {lastSeq}, received {receivedSeq}"
        | IntegrityEventType.NegativeSpread(bid, ask) ->
            $"Negative spread: bid {bid} > ask {ask}"
        | IntegrityEventType.BookCrossed(level) ->
            $"Book crossed at level {level}"
        | IntegrityEventType.StaleQuote(duration) ->
            $"Stale quote: no updates for {duration.TotalSeconds:F1}s"
        | IntegrityEventType.DuplicateMessage(seqNum) ->
            $"Duplicate message: sequence {seqNum}"
        | IntegrityEventType.PriceSpike(prev, curr, threshold) ->
            $"Price spike: {prev} -> {curr} (threshold {threshold}%%)"
        | IntegrityEventType.VolumeSpike(prev, curr, threshold) ->
            $"Volume spike: {prev} -> {curr} (threshold {threshold}x)"
        | IntegrityEventType.ConnectionIssue(reason) ->
            $"Connection issue: {reason}"
        | IntegrityEventType.DataQualityIssue(desc) ->
            $"Data quality issue: {desc}"
