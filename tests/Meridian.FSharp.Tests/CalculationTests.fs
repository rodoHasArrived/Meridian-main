/// Unit tests for F# calculation functions.
module Meridian.FSharp.Tests.CalculationTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Calculations.Aggregations

// Module aliases to avoid ambiguous reference errors between Spread and Imbalance
// (both have fromQuote, fromOrderBook, calculateStatistics with same signatures)
module SpreadCalc = Meridian.FSharp.Calculations.Spread
module ImbalanceCalc = Meridian.FSharp.Calculations.Imbalance

let createTestQuote bidPrice bidSize askPrice askSize : QuoteEvent = {
    Symbol = "TEST"
    BidPrice = bidPrice
    BidSize = bidSize
    AskPrice = askPrice
    AskSize = askSize
    SequenceNumber = 1L
    Timestamp = DateTimeOffset.UtcNow
    ExchangeTimestamp = None
    StreamId = None
}

let createTestTrade price quantity side seqNum : TradeEvent = {
    Symbol = "TEST"
    Price = price
    Quantity = quantity
    Side = side
    SequenceNumber = seqNum
    Timestamp = DateTimeOffset.UtcNow
    ExchangeTimestamp = None
    StreamId = None
    Venue = None
}

// Spread Tests

[<Fact>]
let ``calculate returns correct spread`` () =
    let spread = SpreadCalc.calculate 100.00m 100.10m
    spread |> should equal (Some 0.10m)

[<Fact>]
let ``calculate returns None for invalid prices`` () =
    SpreadCalc.calculate 0m 100.00m |> should equal None
    SpreadCalc.calculate 100.00m 0m |> should equal None
    SpreadCalc.calculate 100.10m 100.00m |> should equal None // crossed

[<Fact>]
let ``midPrice calculates correct value`` () =
    let mid = SpreadCalc.midPrice 100.00m 100.10m
    mid |> should equal (Some 100.05m)

[<Fact>]
let ``spreadBps calculates correct basis points`` () =
    let bps = SpreadCalc.spreadBps 100.00m 100.10m
    match bps with
    | Some value ->
        // 0.10 / 100.05 * 10000 ≈ 9.995
        value |> should be (greaterThan 9.9m)
        value |> should be (lessThan 10.1m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``fromQuote calculates spread from quote`` () =
    let quote = createTestQuote 99.90m 1000L 100.10m 500L
    let spread = SpreadCalc.fromQuote quote
    spread |> should equal (Some 0.20m)

[<Fact>]
let ``effectiveSpread calculates correct value`` () =
    // Trade at mid should have 0 effective spread
    let effSpread = SpreadCalc.effectiveSpread 100.05m 100.00m 100.10m
    effSpread |> should equal (Some 0.00m)

    // Trade at ask should have effective spread = quoted spread
    let effSpread2 = SpreadCalc.effectiveSpread 100.10m 100.00m 100.10m
    effSpread2 |> should equal (Some 0.10m)

[<Fact>]
let ``relativeSpread calculates percentage`` () =
    let relSpread = SpreadCalc.relativeSpread 100.00m 100.10m
    match relSpread with
    | Some value ->
        // 0.10 / 100.05 * 100 ≈ 0.0999%
        value |> should be (greaterThan 0.09m)
        value |> should be (lessThan 0.11m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``halfSpread calculates half of spread`` () =
    let half = SpreadCalc.halfSpread 100.00m 100.10m
    half |> should equal (Some 0.05m)

[<Fact>]
let ``halfSpread returns None for invalid prices`` () =
    SpreadCalc.halfSpread 0m 100.00m |> should equal None
    SpreadCalc.halfSpread 100.10m 100.00m |> should equal None // crossed

[<Fact>]
let ``midPriceFromQuote calculates mid from quote`` () =
    let quote = createTestQuote 100.00m 1000L 100.10m 500L
    let mid = SpreadCalc.midPriceFromQuote quote
    mid |> should equal (Some 100.05m)

[<Fact>]
let ``spreadBpsFromQuote calculates bps from quote`` () =
    let quote = createTestQuote 100.00m 1000L 100.10m 500L
    let bps = SpreadCalc.spreadBpsFromQuote quote
    match bps with
    | Some value ->
        // 0.10 / 100.05 * 10000 ≈ 9.995
        value |> should be (greaterThan 9.9m)
        value |> should be (lessThan 10.1m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``effectiveSpreadBps calculates effective spread in basis points`` () =
    // Trade at ask: effective spread = 0.10, mid = 100.05
    // bps = 0.10 / 100.05 * 10000 ≈ 9.995
    let effBps = SpreadCalc.effectiveSpreadBps 100.10m 100.00m 100.10m
    match effBps with
    | Some value ->
        value |> should be (greaterThan 9.9m)
        value |> should be (lessThan 10.1m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``effectiveSpreadBps at mid returns zero`` () =
    let effBps = SpreadCalc.effectiveSpreadBps 100.05m 100.00m 100.10m
    effBps |> should equal (Some 0.0m)

let createTestOrderBook bidPrice bidQty askPrice askQty : OrderBookSnapshot = {
    Symbol = "TEST"
    Bids = [ { Price = bidPrice; Quantity = bidQty; OrderCount = 1 } ]
    Asks = [ { Price = askPrice; Quantity = askQty; OrderCount = 1 } ]
    SequenceNumber = 1L
    Timestamp = DateTimeOffset.UtcNow
    StreamId = None
}

let createEmptyOrderBook () : OrderBookSnapshot = {
    Symbol = "TEST"
    Bids = []
    Asks = []
    SequenceNumber = 1L
    Timestamp = DateTimeOffset.UtcNow
    StreamId = None
}

[<Fact>]
let ``fromOrderBook calculates spread from order book`` () =
    let book = createTestOrderBook 100.00m 1000L 100.10m 500L
    let spread = SpreadCalc.fromOrderBook book
    spread |> should equal (Some 0.10m)

[<Fact>]
let ``fromOrderBook returns None for empty book`` () =
    let book = createEmptyOrderBook ()
    let spread = SpreadCalc.fromOrderBook book
    spread |> should equal None

[<Fact>]
let ``midPriceFromOrderBook calculates mid from order book`` () =
    let book = createTestOrderBook 100.00m 1000L 100.10m 500L
    let mid = SpreadCalc.midPriceFromOrderBook book
    mid |> should equal (Some 100.05m)

[<Fact>]
let ``midPriceFromOrderBook returns None for empty book`` () =
    let book = createEmptyOrderBook ()
    let mid = SpreadCalc.midPriceFromOrderBook book
    mid |> should equal None

[<Fact>]
let ``spreadBpsFromOrderBook calculates bps from order book`` () =
    let book = createTestOrderBook 100.00m 1000L 100.10m 500L
    let bps = SpreadCalc.spreadBpsFromOrderBook book
    match bps with
    | Some value ->
        value |> should be (greaterThan 9.9m)
        value |> should be (lessThan 10.1m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``spreadBpsFromOrderBook returns None for empty book`` () =
    let book = createEmptyOrderBook ()
    let bps = SpreadCalc.spreadBpsFromOrderBook book
    bps |> should equal None

[<Fact>]
let ``isSpreadAcceptable returns true when under threshold`` () =
    // Spread is ~10 bps, threshold is 20 bps
    let result = SpreadCalc.isSpreadAcceptable 20m 100.00m 100.10m
    result |> should equal true

[<Fact>]
let ``isSpreadAcceptable returns false when over threshold`` () =
    // Spread is ~10 bps, threshold is 5 bps
    let result = SpreadCalc.isSpreadAcceptable 5m 100.00m 100.10m
    result |> should equal false

[<Fact>]
let ``isSpreadAcceptable returns false for invalid prices`` () =
    let result = SpreadCalc.isSpreadAcceptable 20m 0m 100.00m
    result |> should equal false

[<Fact>]
let ``calculateStatistics returns correct stats for multiple quotes`` () =
    let quotes = [
        createTestQuote 100.00m 1000L 100.10m 500L  // spread = 0.10
        createTestQuote 100.00m 1000L 100.20m 500L  // spread = 0.20
        createTestQuote 100.00m 1000L 100.15m 500L  // spread = 0.15
    ]
    let stats = SpreadCalc.calculateStatistics quotes
    match stats with
    | Some s ->
        s.Count |> should equal 3
        s.MinSpread |> should equal 0.10m
        s.MaxSpread |> should equal 0.20m
        s.AvgSpread |> should equal 0.15m
        s.MedianSpread |> should equal 0.15m
    | None -> failwith "Expected Some stats"

[<Fact>]
let ``calculateStatistics returns None for empty list`` () =
    let stats = SpreadCalc.calculateStatistics Seq.empty
    stats |> should equal None

[<Fact>]
let ``calculateStatistics handles single quote`` () =
    let quotes = [ createTestQuote 100.00m 1000L 100.10m 500L ]
    let stats = SpreadCalc.calculateStatistics quotes
    match stats with
    | Some s ->
        s.Count |> should equal 1
        s.MinSpread |> should equal 0.10m
        s.MaxSpread |> should equal 0.10m
        s.AvgSpread |> should equal 0.10m
        s.MedianSpread |> should equal 0.10m
        s.StdDevSpread |> should equal 0m
    | None -> failwith "Expected Some stats"

// Imbalance Tests

[<Fact>]
let ``Imbalance.calculate returns correct value`` () =
    // Equal sizes = 0 imbalance
    let balanced = ImbalanceCalc.calculate 1000L 1000L
    balanced |> should equal (Some 0m)

    // All bid = +1 imbalance
    let allBid = ImbalanceCalc.calculate 1000L 0L
    allBid |> should equal (Some 1m)

    // All ask = -1 imbalance
    let allAsk = ImbalanceCalc.calculate 0L 1000L
    allAsk |> should equal (Some -1m)

[<Fact>]
let ``Imbalance.calculate with unequal sizes`` () =
    // 75% bid, 25% ask => (75-25)/(75+25) = 0.5
    let result = ImbalanceCalc.calculate 750L 250L
    result |> should equal (Some 0.5m)

[<Fact>]
let ``Imbalance.calculate returns None for zero total`` () =
    let result = ImbalanceCalc.calculate 0L 0L
    result |> should equal None

[<Fact>]
let ``Imbalance.fromQuote calculates from quote`` () =
    let quote = createTestQuote 100.00m 1000L 100.10m 500L
    let imbalance = ImbalanceCalc.fromQuote quote
    // (1000 - 500) / (1000 + 500) = 500/1500 ≈ 0.333
    match imbalance with
    | Some value ->
        value |> should be (greaterThan 0.3m)
        value |> should be (lessThan 0.4m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``getImbalanceDirection returns Buy for positive imbalance`` () =
    let direction = ImbalanceCalc.getImbalanceDirection 0.5m
    direction |> should equal (Some Side.Buy)

[<Fact>]
let ``getImbalanceDirection returns Sell for negative imbalance`` () =
    let direction = ImbalanceCalc.getImbalanceDirection -0.5m
    direction |> should equal (Some Side.Sell)

[<Fact>]
let ``getImbalanceDirection returns None for balanced`` () =
    let direction = ImbalanceCalc.getImbalanceDirection 0.05m
    direction |> should equal None

[<Fact>]
let ``isSignificantImbalance checks threshold`` () =
    ImbalanceCalc.isSignificantImbalance 0.3m 0.5m |> should equal true
    ImbalanceCalc.isSignificantImbalance 0.3m 0.2m |> should equal false

// Aggregation Tests

[<Fact>]
let ``vwap calculates correct value`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 101.00m 200L AggressorSide.Buyer 2L
    ]
    // VWAP = (100*100 + 101*200) / (100 + 200) = 30200/300 = 100.666...
    let result = vwap trades
    match result with
    | Some v ->
        v |> should be (greaterThan 100.6m)
        v |> should be (lessThan 100.7m)
    | None -> failwith "Expected Some value"

[<Fact>]
let ``vwap returns None for empty list`` () =
    let result = vwap Seq.empty
    result |> should equal None

[<Fact>]
let ``totalVolume sums quantities`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 101.00m 200L AggressorSide.Buyer 2L
        createTestTrade 102.00m 50L AggressorSide.Seller 3L
    ]
    let total = totalVolume trades
    total |> should equal 350L

[<Fact>]
let ``volumeBreakdown calculates correct volumes`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 101.00m 200L AggressorSide.Seller 2L
        createTestTrade 102.00m 50L AggressorSide.Unknown 3L
    ]
    let breakdown = volumeBreakdown trades
    breakdown.BuyVolume |> should equal 100L
    breakdown.SellVolume |> should equal 200L
    breakdown.UnknownVolume |> should equal 50L
    breakdown.TotalVolume |> should equal 350L

[<Fact>]
let ``orderFlowImbalance calculates signed sum`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 101.00m 200L AggressorSide.Seller 2L
    ]
    let ofi = orderFlowImbalance trades
    // 100 - 200 = -100
    ofi |> should equal -100L

[<Fact>]
let ``priceRange calculates high minus low`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 105.00m 100L AggressorSide.Buyer 2L
        createTestTrade 102.00m 100L AggressorSide.Buyer 3L
    ]
    let range = priceRange trades
    range |> should equal (Some 5.00m)

[<Fact>]
let ``priceReturn calculates percentage change`` () =
    let now = DateTimeOffset.UtcNow
    let trades = [
        { createTestTrade 100.00m 100L AggressorSide.Buyer 1L with Timestamp = now }
        { createTestTrade 105.00m 100L AggressorSide.Buyer 2L with Timestamp = now.AddSeconds(1.0) }
    ]
    let pctReturn = priceReturn trades
    // (105 - 100) / 100 * 100 = 5%
    pctReturn |> should equal (Some 5.00m)

[<Fact>]
let ``tradeStatistics calculates correct stats`` () =
    let trades = [
        createTestTrade 100.00m 100L AggressorSide.Buyer 1L
        createTestTrade 101.00m 200L AggressorSide.Buyer 2L
        createTestTrade 102.00m 50L AggressorSide.Seller 3L
    ]
    let stats = tradeStatistics trades
    match stats with
    | Some s ->
        s.TradeCount |> should equal 3
        s.TotalVolume |> should equal 350L
        s.MinSize |> should equal 50L
        s.MaxSize |> should equal 200L
    | None -> failwith "Expected Some stats"

[<Fact>]
let ``createOhlcvBar creates correct bar`` () =
    let now = DateTimeOffset.UtcNow
    let trades = [
        { createTestTrade 100.00m 100L AggressorSide.Buyer 1L with Timestamp = now }
        { createTestTrade 105.00m 100L AggressorSide.Buyer 2L with Timestamp = now.AddSeconds(1.0) }
        { createTestTrade 98.00m 100L AggressorSide.Seller 3L with Timestamp = now.AddSeconds(2.0) }
        { createTestTrade 102.00m 100L AggressorSide.Buyer 4L with Timestamp = now.AddSeconds(3.0) }
    ]
    let bar = createOhlcvBar trades
    match bar with
    | Some b ->
        b.Open |> should equal 100.00m
        b.High |> should equal 105.00m
        b.Low |> should equal 98.00m
        b.Close |> should equal 102.00m
        b.Volume |> should equal 400L
        b.TradeCount |> should equal 4
    | None -> failwith "Expected Some bar"
