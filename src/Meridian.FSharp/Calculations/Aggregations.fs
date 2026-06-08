/// Aggregation functions for market data analysis.
/// Includes VWAP, TWAP, volume analysis, and trade flow metrics.
module Meridian.FSharp.Calculations.Aggregations

open System
open System.Collections.Generic
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides

/// Calculate Volume-Weighted Average Price (VWAP).
[<CompiledName("Vwap")>]
let vwap (trades: TradeEvent seq) : decimal option =
    let totalValue, totalVolume =
        trades
        |> Seq.fold (fun (value, volume) trade ->
            let tradeValue = trade.Price * decimal trade.Quantity
            (value + tradeValue, volume + decimal trade.Quantity)
        ) (0m, 0m)

    if totalVolume > 0m then
        Some (totalValue / totalVolume)
    else
        None

/// Calculate Time-Weighted Average Price (TWAP).
[<CompiledName("Twap")>]
let twap (trades: TradeEvent seq) : decimal option =
    let tradeList = trades |> Seq.toList

    match tradeList with
    | [] -> None
    | [single] -> Some single.Price
    | _ ->
        // Calculate time-weighted average
        let sortedTrades = tradeList |> List.sortBy (fun t -> t.Timestamp)
        let startTime = (List.head sortedTrades).Timestamp
        let endTime = (List.last sortedTrades).Timestamp
        let totalDuration = (endTime - startTime).TotalSeconds

        if totalDuration <= 0.0 then
            // All trades at same time, use simple average
            let sum = sortedTrades |> List.sumBy (fun t -> t.Price)
            Some (sum / decimal (List.length sortedTrades))
        else
            // Weight each price by time until next trade
            let weighted =
                sortedTrades
                |> List.pairwise
                |> List.sumBy (fun (current, next) ->
                    let duration = (next.Timestamp - current.Timestamp).TotalSeconds
                    current.Price * decimal duration)

            Some (weighted / decimal totalDuration)

/// Calculate total volume from trades.
[<CompiledName("TotalVolume")>]
let totalVolume (trades: TradeEvent seq) : int64 =
    trades |> Seq.sumBy (fun t -> t.Quantity)

/// Calculate buy/sell volume breakdown.
type VolumeBreakdown = {
    /// Total buy volume (buyer-initiated trades)
    BuyVolume: int64
    /// Total sell volume (seller-initiated trades)
    SellVolume: int64
    /// Unknown aggressor volume
    UnknownVolume: int64
    /// Total volume
    TotalVolume: int64
    /// Buy ratio (0 to 1)
    BuyRatio: decimal
}

/// Calculate volume breakdown by aggressor side.
[<CompiledName("VolumeBreakdown")>]
let volumeBreakdown (trades: TradeEvent seq) : VolumeBreakdown =
    let buyVol, sellVol, unknownVol =
        trades
        |> Seq.fold (fun (buy, sell, unknown) trade ->
            match trade.Side with
            | AggressorSide.Buyer -> (buy + trade.Quantity, sell, unknown)
            | AggressorSide.Seller -> (buy, sell + trade.Quantity, unknown)
            | AggressorSide.Unknown -> (buy, sell, unknown + trade.Quantity)
        ) (0L, 0L, 0L)

    let total = buyVol + sellVol + unknownVol
    let buyRatio =
        if total > 0L then decimal buyVol / decimal total
        else 0.5m

    { BuyVolume = buyVol
      SellVolume = sellVol
      UnknownVolume = unknownVol
      TotalVolume = total
      BuyRatio = buyRatio }

/// Calculate Order Flow Imbalance (OFI).
/// OFI = sum of signed trade volumes, positive for buyer-initiated
[<CompiledName("OrderFlowImbalance")>]
let orderFlowImbalance (trades: TradeEvent seq) : int64 =
    trades
    |> Seq.sumBy (fun trade ->
        match trade.Side with
        | AggressorSide.Buyer -> trade.Quantity
        | AggressorSide.Seller -> -trade.Quantity
        | AggressorSide.Unknown -> 0L)

/// Calculate trade count and average size.
type TradeStatistics = {
    /// Total number of trades
    TradeCount: int
    /// Total volume traded
    TotalVolume: int64
    /// Average trade size
    AverageSize: decimal
    /// Median trade size
    MedianSize: int64
    /// Maximum trade size
    MaxSize: int64
    /// Minimum trade size
    MinSize: int64
    /// VWAP
    Vwap: decimal option
}

/// Calculate trade statistics.
[<CompiledName("TradeStatistics")>]
let tradeStatistics (trades: TradeEvent seq) : TradeStatistics option =
    let sizes = ResizeArray<int64>()
    let mutable total = 0L
    let mutable totalValue = 0m
    let mutable totalVwapVolume = 0m

    for trade in trades do
        sizes.Add(trade.Quantity)
        total <- total + trade.Quantity
        totalValue <- totalValue + trade.Price * decimal trade.Quantity
        totalVwapVolume <- totalVwapVolume + decimal trade.Quantity

    let count = sizes.Count

    if count = 0 then
        None
    else
        sizes.Sort()

        let median =
            if count % 2 = 0 then
                (sizes.[count / 2 - 1] + sizes.[count / 2]) / 2L
            else
                sizes.[count / 2]

        Some {
            TradeCount = count
            TotalVolume = total
            AverageSize = decimal total / decimal count
            MedianSize = median
            MaxSize = sizes.[count - 1]
            MinSize = sizes.[0]
            Vwap =
                if totalVwapVolume > 0m then
                    Some (totalValue / totalVwapVolume)
                else
                    None
        }

/// Calculate price range (high - low).
[<CompiledName("PriceRange")>]
let priceRange (trades: TradeEvent seq) : decimal option =
    let mutable hasTrade = false
    let mutable low = 0m
    let mutable high = 0m

    for trade in trades do
        if not hasTrade then
            low <- trade.Price
            high <- trade.Price
            hasTrade <- true
        else
            if trade.Price < low then low <- trade.Price
            if trade.Price > high then high <- trade.Price

    if hasTrade then Some (high - low) else None

/// Calculate price return from first to last trade.
[<CompiledName("PriceReturn")>]
let priceReturn (trades: TradeEvent seq) : decimal option =
    let mutable count = 0
    let mutable firstTimestamp = DateTimeOffset.MinValue
    let mutable firstPrice = 0m
    let mutable lastTimestamp = DateTimeOffset.MinValue
    let mutable lastPrice = 0m

    for trade in trades do
        if count = 0 || trade.Timestamp < firstTimestamp then
            firstTimestamp <- trade.Timestamp
            firstPrice <- trade.Price

        if count = 0 || trade.Timestamp >= lastTimestamp then
            lastTimestamp <- trade.Timestamp
            lastPrice <- trade.Price

        count <- count + 1

    if count = 0 then None
    elif count = 1 then Some 0m
    elif firstPrice > 0m then Some ((lastPrice - firstPrice) / firstPrice * 100m)
    else None

/// Calculate rolling VWAP.
[<CompiledName("RollingVwap")>]
let rollingVwap (windowSize: int) (trades: TradeEvent list) : (DateTimeOffset * decimal) list =
    if windowSize <= 0 || List.isEmpty trades then []
    else
        trades
        |> List.windowed windowSize
        |> List.choose (fun window ->
            let lastTrade = List.last window
            match vwap window with
            | Some v -> Some (lastTrade.Timestamp, v)
            | None -> None)

/// Calculate trade arrival rate (trades per second).
[<CompiledName("TradeArrivalRate")>]
let tradeArrivalRate (trades: TradeEvent seq) : decimal option =
    let mutable count = 0
    let mutable startTime = DateTimeOffset.MinValue
    let mutable endTime = DateTimeOffset.MinValue

    for trade in trades do
        if count = 0 || trade.Timestamp < startTime then
            startTime <- trade.Timestamp

        if count = 0 || trade.Timestamp > endTime then
            endTime <- trade.Timestamp

        count <- count + 1

    if count <= 1 then
        None
    else
        let duration = (endTime - startTime).TotalSeconds

        if duration > 0.0 then
            Some (decimal count / decimal duration)
        else
            None

/// Aggregate trades into OHLCV bars.
type OhlcvBar = {
    Open: decimal
    High: decimal
    Low: decimal
    Close: decimal
    Volume: int64
    TradeCount: int
    Vwap: decimal option
    StartTime: DateTimeOffset
    EndTime: DateTimeOffset
}

type private OhlcvAccumulator = {
    mutable Open: decimal
    mutable High: decimal
    mutable Low: decimal
    mutable Close: decimal
    mutable Volume: int64
    mutable TradeCount: int
    mutable VwapValue: decimal
    mutable VwapVolume: decimal
    mutable StartTime: DateTimeOffset
    mutable EndTime: DateTimeOffset
}

let private createOhlcvAccumulator (trade: TradeEvent) =
    { Open = trade.Price
      High = trade.Price
      Low = trade.Price
      Close = trade.Price
      Volume = trade.Quantity
      TradeCount = 1
      VwapValue = trade.Price * decimal trade.Quantity
      VwapVolume = decimal trade.Quantity
      StartTime = trade.Timestamp
      EndTime = trade.Timestamp }

let private addTradeToOhlcvAccumulator (acc: OhlcvAccumulator) (trade: TradeEvent) =
    if trade.Timestamp < acc.StartTime then
        acc.StartTime <- trade.Timestamp
        acc.Open <- trade.Price

    if trade.Timestamp >= acc.EndTime then
        acc.EndTime <- trade.Timestamp
        acc.Close <- trade.Price

    if trade.Price > acc.High then acc.High <- trade.Price
    if trade.Price < acc.Low then acc.Low <- trade.Price

    acc.Volume <- acc.Volume + trade.Quantity
    acc.TradeCount <- acc.TradeCount + 1
    acc.VwapValue <- acc.VwapValue + trade.Price * decimal trade.Quantity
    acc.VwapVolume <- acc.VwapVolume + decimal trade.Quantity

let private toOhlcvBar (acc: OhlcvAccumulator) =
    { Open = acc.Open
      High = acc.High
      Low = acc.Low
      Close = acc.Close
      Volume = acc.Volume
      TradeCount = acc.TradeCount
      Vwap =
        if acc.VwapVolume > 0m then
            Some (acc.VwapValue / acc.VwapVolume)
        else
            None
      StartTime = acc.StartTime
      EndTime = acc.EndTime }

/// Create OHLCV bar from trades.
[<CompiledName("CreateOhlcvBar")>]
let createOhlcvBar (trades: TradeEvent seq) : OhlcvBar option =
    let mutable acc: OhlcvAccumulator option = None

    for trade in trades do
        match acc with
        | None -> acc <- Some (createOhlcvAccumulator trade)
        | Some existing -> addTradeToOhlcvAccumulator existing trade

    acc |> Option.map toOhlcvBar

/// Group trades by time interval and create OHLCV bars.
[<CompiledName("CreateOhlcvBars")>]
let createOhlcvBars (intervalSeconds: int) (trades: TradeEvent seq) : OhlcvBar list =
    if intervalSeconds <= 0 then
        invalidArg (nameof intervalSeconds) "Interval seconds must be greater than zero."

    let buckets = SortedDictionary<int64, OhlcvAccumulator>()

    for trade in trades do
        let epoch = trade.Timestamp.ToUnixTimeSeconds()
        let bucket = epoch - (epoch % int64 intervalSeconds)

        match buckets.TryGetValue(bucket) with
        | true, acc -> addTradeToOhlcvAccumulator acc trade
        | false, _ -> buckets.Add(bucket, createOhlcvAccumulator trade)

    buckets.Values
    |> Seq.map toOhlcvBar
    |> Seq.toList
