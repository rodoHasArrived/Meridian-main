/// Pipeline transformation functions for market event streams.
/// Provides composable operators for filtering, mapping, and aggregating events.
module Meridian.FSharp.Pipeline.Transforms

open System
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Validation.ValidationTypes
open Meridian.FSharp.Validation.ValidationPipeline

// Module aliases to avoid shadowing with record field names
module SpreadCalc = Meridian.FSharp.Calculations.Spread
module ImbalanceCalc = Meridian.FSharp.Calculations.Imbalance

/// Filter events by symbol.
[<CompiledName("FilterBySymbol")>]
let filterBySymbol (symbol: string) (events: MarketEvent seq) : MarketEvent seq =
    events
    |> Seq.filter (fun event ->
        match MarketEvent.getSymbol event with
        | Some s -> s = symbol
        | None -> false)

/// Filter events by symbol list.
[<CompiledName("FilterBySymbols")>]
let filterBySymbols (symbols: string Set) (events: MarketEvent seq) : MarketEvent seq =
    events
    |> Seq.filter (fun event ->
        match MarketEvent.getSymbol event with
        | Some s -> Set.contains s symbols
        | None -> false)

/// Filter events by time range.
[<CompiledName("FilterByTimeRange")>]
let filterByTimeRange (startTime: DateTimeOffset) (endTime: DateTimeOffset) (events: MarketEvent seq) : MarketEvent seq =
    events
    |> Seq.filter (fun event ->
        let ts = MarketEvent.getTimestamp event
        ts >= startTime && ts <= endTime)

/// Filter to only trade events.
[<CompiledName("FilterTrades")>]
let filterTrades (events: MarketEvent seq) : TradeEvent seq =
    events
    |> Seq.choose (function
        | MarketEvent.Trade t -> Some t
        | _ -> None)

/// Filter to only quote events.
[<CompiledName("FilterQuotes")>]
let filterQuotes (events: MarketEvent seq) : QuoteEvent seq =
    events
    |> Seq.choose (function
        | MarketEvent.Quote q -> Some q
        | _ -> None)

/// Filter to only depth events.
[<CompiledName("FilterDepth")>]
let filterDepth (events: MarketEvent seq) : DepthEvent seq =
    events
    |> Seq.choose (function
        | MarketEvent.Depth d -> Some d
        | _ -> None)

/// Filter to only integrity events.
[<CompiledName("FilterIntegrity")>]
let filterIntegrity (events: MarketEvent seq) : IntegrityEvent seq =
    events
    |> Seq.choose (function
        | MarketEvent.Integrity i -> Some i
        | _ -> None)

/// Enrich trades with aggressor inference based on BBO.
[<CompiledName("EnrichWithAggressor")>]
let enrichWithAggressor (events: MarketEvent seq) : MarketEvent seq =
    let mutable lastQuote: QuoteEvent option = None

    events
    |> Seq.map (fun event ->
        match event with
        | MarketEvent.Quote q ->
            lastQuote <- Some q
            event
        | MarketEvent.Trade t ->
            match lastQuote with
            | Some q when t.Symbol = q.Symbol ->
                let inferredSide = inferAggressor t.Price (Some q.BidPrice) (Some q.AskPrice)
                MarketEvent.Trade { t with Side = inferredSide }
            | _ -> event
        | _ -> event)

/// Add spread calculations to quotes.
type EnrichedQuote = {
    Quote: QuoteEvent
    Spread: decimal option
    SpreadBps: decimal option
    MidPrice: decimal option
    Imbalance: decimal option
}

/// Enrich quotes with calculated fields.
[<CompiledName("EnrichQuotes")>]
let enrichQuotes (quotes: QuoteEvent seq) : EnrichedQuote seq =
    quotes
    |> Seq.map (fun q ->
        { Quote = q
          Spread = SpreadCalc.fromQuote q
          SpreadBps = SpreadCalc.spreadBpsFromQuote q
          MidPrice = SpreadCalc.midPriceFromQuote q
          Imbalance = ImbalanceCalc.fromQuote q })

/// Validate and filter events.
[<CompiledName("ValidateAndFilter")>]
let validateAndFilter (events: MarketEvent seq) : MarketEvent seq =
    events
    |> Seq.choose (fun event ->
        match MarketEventValidation.validateMarketEvent event with
        | Ok e -> Some e
        | Error _ -> None)

/// Partition events by type.
type PartitionedEvents = {
    Trades: TradeEvent list
    Quotes: QuoteEvent list
    Depth: DepthEvent list
    Integrity: IntegrityEvent list
    Other: MarketEvent list
}

/// Partition events by their type.
[<CompiledName("PartitionByType")>]
let partitionByType (events: MarketEvent seq) : PartitionedEvents =
    let mutable trades = []
    let mutable quotes = []
    let mutable depth = []
    let mutable integrity = []
    let mutable other = []

    for event in events do
        match event with
        | MarketEvent.Trade t -> trades <- t :: trades
        | MarketEvent.Quote q -> quotes <- q :: quotes
        | MarketEvent.Depth d -> depth <- d :: depth
        | MarketEvent.Integrity i -> integrity <- i :: integrity
        | _ -> other <- event :: other

    { Trades = List.rev trades
      Quotes = List.rev quotes
      Depth = List.rev depth
      Integrity = List.rev integrity
      Other = List.rev other }

/// Group events by symbol.
[<CompiledName("GroupBySymbol")>]
let groupBySymbol (events: MarketEvent seq) : Map<string, MarketEvent list> =
    events
    |> Seq.choose (fun event ->
        match MarketEvent.getSymbol event with
        | Some s -> Some (s, event)
        | None -> None)
    |> Seq.groupBy fst
    |> Seq.map (fun (symbol, events) -> symbol, events |> Seq.map snd |> Seq.toList)
    |> Map.ofSeq

/// Sample events at regular intervals.
[<CompiledName("SampleAtInterval")>]
let sampleAtInterval (intervalMs: int) (events: MarketEvent seq) : MarketEvent seq =
    let mutable lastSampleTime = DateTimeOffset.MinValue

    events
    |> Seq.filter (fun event ->
        let ts = MarketEvent.getTimestamp event
        let elapsed = (ts - lastSampleTime).TotalMilliseconds

        if elapsed >= float intervalMs then
            lastSampleTime <- ts
            true
        else
            false)

/// Deduplicate events by sequence number.
[<CompiledName("Deduplicate")>]
let deduplicate (events: MarketEvent seq) : MarketEvent seq =
    let seen = System.Collections.Generic.HashSet<int64>()

    events
    |> Seq.filter (fun event ->
        match MarketEvent.getSequenceNumber event with
        | Some seq -> seen.Add(seq)
        | None -> true)

/// Merge multiple event streams in timestamp order.
[<CompiledName("MergeStreams")>]
let mergeStreams (streams: MarketEvent seq list) : MarketEvent seq =
    streams
    |> Seq.concat
    |> Seq.sortBy MarketEvent.getTimestamp

/// Buffer events by count.
[<CompiledName("BufferByCount")>]
let bufferByCount (count: int) (events: MarketEvent seq) : MarketEvent list seq =
    events
    |> Seq.chunkBySize count
    |> Seq.map Array.toList

/// Buffer events by time window.
[<CompiledName("BufferByTime")>]
let bufferByTime (windowMs: int) (events: MarketEvent seq) : MarketEvent list seq =
    events
    |> Seq.groupBy (fun event ->
        let ts = MarketEvent.getTimestamp event
        ts.ToUnixTimeMilliseconds() / int64 windowMs)
    |> Seq.map (fun (_, group) -> Seq.toList group)

// ==================== Advanced Transforms ====================

/// Result of computing a simple moving average over trade prices.
type MovingAveragePoint = {
    Timestamp: DateTimeOffset
    Symbol: string
    Price: decimal
    Sma: decimal
}

/// Compute Simple Moving Average (SMA) over trade prices.
[<CompiledName("SimpleMovingAverage")>]
let simpleMovingAverage (windowSize: int) (trades: TradeEvent seq) : MovingAveragePoint seq =
    trades
    |> Seq.windowed windowSize
    |> Seq.map (fun window ->
        let latest = Array.last window
        let avg = window |> Array.averageBy (fun t -> float t.Price) |> decimal
        { Timestamp = latest.Timestamp
          Symbol = latest.Symbol
          Price = latest.Price
          Sma = avg })

/// Compute Exponential Moving Average (EMA) over trade prices.
[<CompiledName("ExponentialMovingAverage")>]
let exponentialMovingAverage (period: int) (trades: TradeEvent seq) : MovingAveragePoint seq =
    let multiplier = 2.0m / (decimal period + 1.0m)
    let tradeList = trades |> Seq.toArray

    if tradeList.Length < period then Seq.empty
    else
        let seedAvg =
            tradeList.[0..period-1]
            |> Array.averageBy (fun t -> float t.Price)
            |> decimal

        tradeList.[period..]
        |> Array.scan (fun ema trade ->
            (trade.Price - ema) * multiplier + ema
        ) seedAvg
        |> Array.skip 1
        |> Array.mapi (fun i ema ->
            let trade = tradeList.[period + i]
            { Timestamp = trade.Timestamp
              Symbol = trade.Symbol
              Price = trade.Price
              Sma = ema })
        |> Array.toSeq

/// Rate of change result.
type RateOfChangePoint = {
    Timestamp: DateTimeOffset
    Symbol: string
    Price: decimal
    RateOfChange: decimal
}

/// Compute Rate of Change (ROC) as percentage change over N periods.
[<CompiledName("RateOfChange")>]
let rateOfChange (periods: int) (trades: TradeEvent seq) : RateOfChangePoint seq =
    let tradeArr = trades |> Seq.toArray

    if tradeArr.Length <= periods then Seq.empty
    else
        tradeArr
        |> Array.mapi (fun i trade ->
            if i < periods then None
            else
                let prevPrice = tradeArr.[i - periods].Price
                if prevPrice = 0m then None
                else
                    Some { Timestamp = trade.Timestamp
                           Symbol = trade.Symbol
                           Price = trade.Price
                           RateOfChange = (trade.Price - prevPrice) / prevPrice * 100m })
        |> Array.choose id
        |> Array.toSeq

/// Gap detection result.
type TimeGap = {
    Symbol: string option
    GapStart: DateTimeOffset
    GapEnd: DateTimeOffset
    DurationMs: float
}

/// Detect time gaps in the event stream exceeding a threshold.
[<CompiledName("DetectGaps")>]
let detectGaps (thresholdMs: float) (events: MarketEvent seq) : TimeGap seq =
    events
    |> Seq.pairwise
    |> Seq.choose (fun (prev, curr) ->
        let prevTs = MarketEvent.getTimestamp prev
        let currTs = MarketEvent.getTimestamp curr
        let gap = (currTs - prevTs).TotalMilliseconds

        if gap > thresholdMs then
            Some { Symbol = MarketEvent.getSymbol curr
                   GapStart = prevTs
                   GapEnd = currTs
                   DurationMs = gap }
        else
            None)

/// Throttle events to at most one per specified millisecond interval per symbol.
[<CompiledName("ThrottleBySymbol")>]
let throttleBySymbol (intervalMs: int) (events: MarketEvent seq) : MarketEvent seq =
    let lastEmitted = System.Collections.Generic.Dictionary<string, DateTimeOffset>()

    events
    |> Seq.filter (fun event ->
        let symbol =
            match MarketEvent.getSymbol event with
            | Some s -> s
            | None -> "__heartbeat__"
        let ts = MarketEvent.getTimestamp event

        match lastEmitted.TryGetValue(symbol) with
        | true, lastTs when (ts - lastTs).TotalMilliseconds < float intervalMs ->
            false
        | _ ->
            lastEmitted.[symbol] <- ts
            true)

/// Normalize trade prices to percentage returns from the first trade.
[<CompiledName("NormalizePrices")>]
let normalizePrices (trades: TradeEvent seq) : (TradeEvent * decimal) seq =
    let mutable firstPrice = None

    trades
    |> Seq.map (fun trade ->
        match firstPrice with
        | None ->
            firstPrice <- Some trade.Price
            (trade, 0m)
        | Some fp when fp > 0m ->
            let pctReturn = (trade.Price - fp) / fp * 100m
            (trade, pctReturn)
        | _ ->
            (trade, 0m))

/// Lag events by a specified number of positions (useful for lead/lag analysis).
[<CompiledName("Lag")>]
let lag (n: int) (events: MarketEvent seq) : (MarketEvent * MarketEvent option) seq =
    let buffer = System.Collections.Generic.Queue<MarketEvent>()

    events
    |> Seq.map (fun event ->
        buffer.Enqueue(event)
        if buffer.Count > n then
            let lagged = buffer.Dequeue()
            (event, Some lagged)
        else
            (event, None))

/// Pipeline composition operator.
let (|>>) (events: MarketEvent seq) (transform: MarketEvent seq -> MarketEvent seq) : MarketEvent seq =
    transform events

/// Create a transformation pipeline.
type TransformPipeline = {
    Transforms: (MarketEvent seq -> MarketEvent seq) list
}

module TransformPipeline =

    /// Create an empty pipeline.
    [<CompiledName("Create")>]
    let create () = { Transforms = [] }

    /// Add a transform to the pipeline.
    [<CompiledName("Add")>]
    let add (transform: MarketEvent seq -> MarketEvent seq) (pipeline: TransformPipeline) =
        { Transforms = pipeline.Transforms @ [transform] }

    /// Run the pipeline on events.
    [<CompiledName("Run")>]
    let run (pipeline: TransformPipeline) (events: MarketEvent seq) =
        pipeline.Transforms
        |> List.fold (fun acc transform -> transform acc) events

    /// Add symbol filter.
    [<CompiledName("FilterSymbol")>]
    let filterSymbol (symbol: string) (pipeline: TransformPipeline) =
        add (filterBySymbol symbol) pipeline

    /// Add time range filter.
    [<CompiledName("FilterTime")>]
    let filterTime (startTime: DateTimeOffset) (endTime: DateTimeOffset) (pipeline: TransformPipeline) =
        add (filterByTimeRange startTime endTime) pipeline

    /// Add validation filter.
    [<CompiledName("Validate")>]
    let validate (pipeline: TransformPipeline) =
        add validateAndFilter pipeline

    /// Add deduplication.
    [<CompiledName("Dedupe")>]
    let dedupe (pipeline: TransformPipeline) =
        add deduplicate pipeline

    /// Add throttling per symbol.
    [<CompiledName("Throttle")>]
    let throttle (intervalMs: int) (pipeline: TransformPipeline) =
        add (throttleBySymbol intervalMs) pipeline

    /// Add gap detection filter (keeps events, detects gaps as side effect).
    [<CompiledName("FilterGaps")>]
    let filterGaps (thresholdMs: float) (onGap: TimeGap -> unit) (pipeline: TransformPipeline) =
        let transform (events: MarketEvent seq) =
            events
            |> Seq.pairwise
            |> Seq.collect (fun (prev, curr) ->
                let prevTs = MarketEvent.getTimestamp prev
                let currTs = MarketEvent.getTimestamp curr
                let gap = (currTs - prevTs).TotalMilliseconds

                if gap > thresholdMs then
                    onGap { Symbol = MarketEvent.getSymbol curr
                            GapStart = prevTs
                            GapEnd = currTs
                            DurationMs = gap }
                seq { curr })
        add transform pipeline

    /// Add sampling transform.
    [<CompiledName("Sample")>]
    let sample (intervalMs: int) (pipeline: TransformPipeline) =
        add (sampleAtInterval intervalMs) pipeline
