/// Aggregation functions for market data analysis.
/// Includes VWAP, TWAP, volume analysis, and trade flow metrics.
module Meridian.FSharp.Calculations.Aggregations

open System
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
    let tradeList = trades |> Seq.toList

    match tradeList with
    | [] -> None
    | _ ->
        let sizes = tradeList |> List.map (fun t -> t.Quantity) |> List.sort
        let count = List.length sizes
        let total = List.sum sizes

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
            MaxSize = List.max sizes
            MinSize = List.min sizes
            Vwap = vwap trades
        }

/// Calculate price range (high - low).
[<CompiledName("PriceRange")>]
let priceRange (trades: TradeEvent seq) : decimal option =
    let prices = trades |> Seq.map (fun t -> t.Price) |> Seq.toList

    match prices with
    | [] -> None
    | _ -> Some (List.max prices - List.min prices)

/// Calculate price return from first to last trade.
[<CompiledName("PriceReturn")>]
let priceReturn (trades: TradeEvent seq) : decimal option =
    let tradeList = trades |> Seq.toList

    match tradeList with
    | [] -> None
    | [single] -> Some 0m
    | _ ->
        let sorted = tradeList |> List.sortBy (fun t -> t.Timestamp)
        let firstPrice = (List.head sorted).Price
        let lastPrice = (List.last sorted).Price

        if firstPrice > 0m then
            Some ((lastPrice - firstPrice) / firstPrice * 100m)
        else
            None

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
    let tradeList = trades |> Seq.toList

    match tradeList with
    | [] | [_] -> None
    | _ ->
        let sorted = tradeList |> List.sortBy (fun t -> t.Timestamp)
        let startTime = (List.head sorted).Timestamp
        let endTime = (List.last sorted).Timestamp
        let duration = (endTime - startTime).TotalSeconds

        if duration > 0.0 then
            Some (decimal (List.length sorted) / decimal duration)
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

/// Create OHLCV bar from trades.
[<CompiledName("CreateOhlcvBar")>]
let createOhlcvBar (trades: TradeEvent seq) : OhlcvBar option =
    let tradeList = trades |> Seq.toList

    match tradeList with
    | [] -> None
    | _ ->
        let sorted = tradeList |> List.sortBy (fun t -> t.Timestamp)
        let prices = sorted |> List.map (fun t -> t.Price)

        Some {
            Open = (List.head sorted).Price
            High = List.max prices
            Low = List.min prices
            Close = (List.last sorted).Price
            Volume = totalVolume trades
            TradeCount = List.length sorted
            Vwap = vwap trades
            StartTime = (List.head sorted).Timestamp
            EndTime = (List.last sorted).Timestamp
        }

/// Group trades by time interval and create OHLCV bars.
[<CompiledName("CreateOhlcvBars")>]
let createOhlcvBars (intervalSeconds: int) (trades: TradeEvent seq) : OhlcvBar list =
    trades
    |> Seq.groupBy (fun t ->
        let epoch = t.Timestamp.ToUnixTimeSeconds()
        epoch - (epoch % int64 intervalSeconds))
    |> Seq.sortBy fst
    |> Seq.choose (fun (_, groupTrades) -> createOhlcvBar groupTrades)
    |> Seq.toList
