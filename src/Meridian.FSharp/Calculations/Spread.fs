/// Spread calculation functions for order book analysis.
/// Pure functional implementations for bid-ask spread metrics.
module Meridian.FSharp.Calculations.Spread

open System
open Meridian.FSharp.Domain.MarketEvents

/// Calculate the absolute bid-ask spread.
/// Returns None if prices are invalid.
[<CompiledName("Calculate")>]
let calculate (bidPrice: decimal) (askPrice: decimal) : decimal option =
    if bidPrice > 0m && askPrice > 0m && askPrice > bidPrice then
        Some (askPrice - bidPrice)
    else
        None

/// Calculate the bid-ask spread from a quote event.
[<CompiledName("FromQuote")>]
let fromQuote (quote: QuoteEvent) : decimal option =
    calculate quote.BidPrice quote.AskPrice

/// Calculate the bid-ask spread from an order book snapshot.
[<CompiledName("FromOrderBook")>]
let fromOrderBook (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> calculate bid.Price ask.Price
    | _ -> None

/// Calculate the mid-price (midpoint of bid and ask).
[<CompiledName("MidPrice")>]
let midPrice (bidPrice: decimal) (askPrice: decimal) : decimal option =
    if bidPrice > 0m && askPrice > 0m then
        Some ((bidPrice + askPrice) / 2m)
    else
        None

/// Calculate the mid-price from a quote event.
[<CompiledName("MidPriceFromQuote")>]
let midPriceFromQuote (quote: QuoteEvent) : decimal option =
    midPrice quote.BidPrice quote.AskPrice

/// Calculate the mid-price from an order book snapshot.
[<CompiledName("MidPriceFromOrderBook")>]
let midPriceFromOrderBook (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> midPrice bid.Price ask.Price
    | _ -> None

/// Calculate spread in basis points (1 bp = 0.01%).
/// Spread in bps = (ask - bid) / mid * 10000
[<CompiledName("SpreadBps")>]
let spreadBps (bidPrice: decimal) (askPrice: decimal) : decimal option =
    match midPrice bidPrice askPrice, calculate bidPrice askPrice with
    | Some mid, Some spread when mid > 0m ->
        Some (spread / mid * 10000m)
    | _ -> None

/// Calculate spread in basis points from a quote event.
[<CompiledName("SpreadBpsFromQuote")>]
let spreadBpsFromQuote (quote: QuoteEvent) : decimal option =
    spreadBps quote.BidPrice quote.AskPrice

/// Calculate spread in basis points from an order book snapshot.
[<CompiledName("SpreadBpsFromOrderBook")>]
let spreadBpsFromOrderBook (book: OrderBookSnapshot) : decimal option =
    match book.Bids, book.Asks with
    | bid :: _, ask :: _ -> spreadBps bid.Price ask.Price
    | _ -> None

/// Calculate the relative spread (spread as percentage of mid).
[<CompiledName("RelativeSpread")>]
let relativeSpread (bidPrice: decimal) (askPrice: decimal) : decimal option =
    match midPrice bidPrice askPrice, calculate bidPrice askPrice with
    | Some mid, Some spread when mid > 0m ->
        Some (spread / mid * 100m)
    | _ -> None

/// Calculate effective spread from trade price and mid-price.
/// Effective spread = 2 * |trade price - mid price|
[<CompiledName("EffectiveSpread")>]
let effectiveSpread (tradePrice: decimal) (bidPrice: decimal) (askPrice: decimal) : decimal option =
    match midPrice bidPrice askPrice with
    | Some mid when mid > 0m ->
        Some (2m * abs(tradePrice - mid))
    | _ -> None

/// Calculate effective spread in basis points.
[<CompiledName("EffectiveSpreadBps")>]
let effectiveSpreadBps (tradePrice: decimal) (bidPrice: decimal) (askPrice: decimal) : decimal option =
    match midPrice bidPrice askPrice, effectiveSpread tradePrice bidPrice askPrice with
    | Some mid, Some effSpread when mid > 0m ->
        Some (effSpread / mid * 10000m)
    | _ -> None

/// Calculate quoted half-spread (half of bid-ask spread).
[<CompiledName("HalfSpread")>]
let halfSpread (bidPrice: decimal) (askPrice: decimal) : decimal option =
    calculate bidPrice askPrice
    |> Option.map (fun s -> s / 2m)

/// Spread statistics for a collection of quotes.
type SpreadStatistics = {
    /// Minimum spread observed
    MinSpread: decimal
    /// Maximum spread observed
    MaxSpread: decimal
    /// Average spread
    AvgSpread: decimal
    /// Median spread
    MedianSpread: decimal
    /// Spread standard deviation
    StdDevSpread: decimal
    /// Average spread in basis points
    AvgSpreadBps: decimal
    /// Number of observations
    Count: int
}

/// Calculate spread statistics from a sequence of quotes.
[<CompiledName("CalculateStatistics")>]
let calculateStatistics (quotes: QuoteEvent seq) : SpreadStatistics option =
    let spreads =
        quotes
        |> Seq.choose fromQuote
        |> Seq.toList

    let spreadBpsList =
        quotes
        |> Seq.choose spreadBpsFromQuote
        |> Seq.toList

    match spreads with
    | [] -> None
    | _ ->
        let sortedSpreads = spreads |> List.sort
        let count = List.length sortedSpreads
        let sum = List.sum sortedSpreads
        let avg = sum / decimal count

        let variance =
            sortedSpreads
            |> List.map (fun s -> (s - avg) * (s - avg))
            |> List.sum
            |> fun v -> v / decimal count

        let stdDev = decimal (sqrt (float variance))

        let median =
            if count % 2 = 0 then
                (sortedSpreads.[count / 2 - 1] + sortedSpreads.[count / 2]) / 2m
            else
                sortedSpreads.[count / 2]

        let avgBps =
            if List.isEmpty spreadBpsList then 0m
            else List.sum spreadBpsList / decimal (List.length spreadBpsList)

        Some {
            MinSpread = List.min sortedSpreads
            MaxSpread = List.max sortedSpreads
            AvgSpread = avg
            MedianSpread = median
            StdDevSpread = stdDev
            AvgSpreadBps = avgBps
            Count = count
        }

/// Check if spread is within acceptable bounds.
[<CompiledName("IsSpreadAcceptable")>]
let isSpreadAcceptable (maxSpreadBps: decimal) (bidPrice: decimal) (askPrice: decimal) : bool =
    match spreadBps bidPrice askPrice with
    | Some bps -> bps <= maxSpreadBps
    | None -> false
