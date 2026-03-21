/// Order book imbalance calculations.
/// Measures the relative pressure between bid and ask sides.
module Meridian.FSharp.Calculations.Imbalance

open System
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides

/// Calculate order book imbalance from bid and ask quantities.
/// Returns a value between -1 and +1:
/// - Positive values indicate buying pressure (more bid volume)
/// - Negative values indicate selling pressure (more ask volume)
/// - Zero indicates balanced book
[<CompiledName("Calculate")>]
let calculate (bidQuantity: int64) (askQuantity: int64) : decimal option =
    let bidQty = decimal bidQuantity
    let askQty = decimal askQuantity
    let total = bidQty + askQty

    if total > 0m then
        Some ((bidQty - askQty) / total)
    else
        None

/// Calculate imbalance from a quote event (using top-of-book sizes).
[<CompiledName("FromQuote")>]
let fromQuote (quote: QuoteEvent) : decimal option =
    calculate quote.BidSize quote.AskSize

/// Calculate imbalance from an order book snapshot (using top levels).
[<CompiledName("FromOrderBook")>]
let fromOrderBook (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> calculate bid.Quantity ask.Quantity
    | _ -> None

/// Calculate imbalance using multiple levels of the order book.
/// Weights deeper levels less than top-of-book.
[<CompiledName("FromOrderBookWeighted")>]
let fromOrderBookWeighted (levels: int) (decayFactor: decimal) (book: OrderBookSnapshot) : decimal option =
    let weightedSum (bookLevels: BookLevel list) =
        bookLevels
        |> List.truncate levels
        |> List.mapi (fun i level ->
            let weight = pown decayFactor i
            decimal level.Quantity * weight)
        |> List.sum

    let bidSum = weightedSum book.Bids
    let askSum = weightedSum book.Asks
    let total = bidSum + askSum

    if total > 0m then
        Some ((bidSum - askSum) / total)
    else
        None

/// Calculate volume-weighted imbalance across all levels.
[<CompiledName("VolumeWeightedImbalance")>]
let volumeWeightedImbalance (book: OrderBookSnapshot) : decimal option =
    let totalBidVolume =
        book.Bids |> List.sumBy (fun l -> l.Quantity) |> decimal

    let totalAskVolume =
        book.Asks |> List.sumBy (fun l -> l.Quantity) |> decimal

    let total = totalBidVolume + totalAskVolume

    if total > 0m then
        Some ((totalBidVolume - totalAskVolume) / total)
    else
        None

/// Calculate price-weighted imbalance.
/// Weights volume by proximity to mid-price.
[<CompiledName("PriceWeightedImbalance")>]
let priceWeightedImbalance (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | [], _ | _, [] -> None
    | bidTop :: _, askTop :: _ ->
        let midPrice = (bidTop.Price + askTop.Price) / 2m

        let weightedVolume (levels: BookLevel list) (isBid: bool) =
            levels
            |> List.sumBy (fun level ->
                let distance = abs(level.Price - midPrice)
                let weight = if distance > 0m then 1m / distance else 1m
                decimal level.Quantity * weight)

        let bidWeighted = weightedVolume book.Bids true
        let askWeighted = weightedVolume book.Asks false
        let total = bidWeighted + askWeighted

        if total > 0m then
            Some ((bidWeighted - askWeighted) / total)
        else
            None

/// Microprice calculation (volume-weighted mid-price).
/// Gives a more accurate estimate of fair value than simple mid-price.
[<CompiledName("Microprice")>]
let microprice (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ ->
        let bidQty = decimal bid.Quantity
        let askQty = decimal ask.Quantity
        let total = bidQty + askQty

        if total > 0m then
            // Microprice = (bid_price * ask_qty + ask_price * bid_qty) / (bid_qty + ask_qty)
            Some ((bid.Price * askQty + ask.Price * bidQty) / total)
        else
            None
    | _ -> None

/// Calculate the imbalance direction.
[<CompiledName("GetImbalanceDirection")>]
let getImbalanceDirection (imbalance: decimal) : Side option =
    if imbalance > 0.1m then Some Side.Buy
    elif imbalance < -0.1m then Some Side.Sell
    else None

/// Imbalance signal strength (0 to 1).
[<CompiledName("SignalStrength")>]
let signalStrength (imbalance: decimal) : decimal =
    abs imbalance

/// Check if imbalance exceeds a threshold.
[<CompiledName("IsSignificantImbalance")>]
let isSignificantImbalance (threshold: decimal) (imbalance: decimal) : bool =
    abs imbalance >= threshold

/// Imbalance statistics over a time period.
type ImbalanceStatistics = {
    /// Average imbalance
    Average: decimal
    /// Imbalance standard deviation
    StdDev: decimal
    /// Percentage of time with buy pressure
    BuyPressurePercent: decimal
    /// Percentage of time with sell pressure
    SellPressurePercent: decimal
    /// Maximum buy pressure observed
    MaxBuyPressure: decimal
    /// Maximum sell pressure observed
    MaxSellPressure: decimal
    /// Number of observations
    Count: int
}

/// Calculate imbalance statistics from a sequence of quotes.
[<CompiledName("CalculateStatistics")>]
let calculateStatistics (quotes: QuoteEvent seq) : ImbalanceStatistics option =
    let imbalances =
        quotes
        |> Seq.choose fromQuote
        |> Seq.toList

    match imbalances with
    | [] -> None
    | _ ->
        let count = List.length imbalances
        let sum = List.sum imbalances
        let avg = sum / decimal count

        let variance =
            imbalances
            |> List.map (fun i -> (i - avg) * (i - avg))
            |> List.sum
            |> fun v -> v / decimal count

        let stdDev = decimal (sqrt (float variance))

        let buyCount = imbalances |> List.filter (fun i -> i > 0.1m) |> List.length
        let sellCount = imbalances |> List.filter (fun i -> i < -0.1m) |> List.length

        Some {
            Average = avg
            StdDev = stdDev
            BuyPressurePercent = decimal buyCount / decimal count * 100m
            SellPressurePercent = decimal sellCount / decimal count * 100m
            MaxBuyPressure = List.max imbalances
            MaxSellPressure = List.min imbalances
            Count = count
        }

/// Calculate rolling imbalance average.
[<CompiledName("RollingAverage")>]
let rollingAverage (window: int) (imbalances: decimal list) : decimal list =
    if window <= 0 || List.isEmpty imbalances then []
    else
        imbalances
        |> List.windowed window
        |> List.map (fun w -> List.sum w / decimal (List.length w))
