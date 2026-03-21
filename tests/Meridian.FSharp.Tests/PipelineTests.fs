/// Unit tests for F# pipeline transformations.
module Meridian.FSharp.Tests.PipelineTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Pipeline.Transforms

let createTestTrade symbol price seqNum : MarketEvent =
    MarketEvent.Trade {
        Symbol = symbol
        Price = price
        Quantity = 100L
        Side = AggressorSide.Buyer
        SequenceNumber = seqNum
        Timestamp = DateTimeOffset.UtcNow
        ExchangeTimestamp = None
        StreamId = None
        Venue = None
    }

let createTestQuote symbol bidPrice askPrice seqNum : MarketEvent =
    MarketEvent.Quote {
        Symbol = symbol
        BidPrice = bidPrice
        BidSize = 1000L
        AskPrice = askPrice
        AskSize = 500L
        SequenceNumber = seqNum
        Timestamp = DateTimeOffset.UtcNow
        ExchangeTimestamp = None
        StreamId = None
    }

[<Fact>]
let ``filterBySymbol filters to matching symbol`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "MSFT" 350.00m 2L
        createTestTrade "AAPL" 151.00m 3L
    ]

    let filtered = filterBySymbol "AAPL" events |> Seq.toList
    filtered.Length |> should equal 2

    filtered
    |> List.forall (fun e ->
        match MarketEvent.getSymbol e with
        | Some s -> s = "AAPL"
        | None -> false)
    |> should equal true

[<Fact>]
let ``filterBySymbols filters to symbol set`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "MSFT" 350.00m 2L
        createTestTrade "GOOGL" 140.00m 3L
        createTestTrade "AAPL" 151.00m 4L
    ]

    let symbols = Set.ofList ["AAPL"; "GOOGL"]
    let filtered = filterBySymbols symbols events |> Seq.toList
    filtered.Length |> should equal 3

[<Fact>]
let ``filterTrades returns only trade events`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestQuote "AAPL" 149.90m 150.00m 2L
        createTestTrade "MSFT" 350.00m 3L
    ]

    let trades = filterTrades events |> Seq.toList
    trades.Length |> should equal 2

[<Fact>]
let ``filterQuotes returns only quote events`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestQuote "AAPL" 149.90m 150.00m 2L
        createTestQuote "MSFT" 349.90m 350.00m 3L
    ]

    let quotes = filterQuotes events |> Seq.toList
    quotes.Length |> should equal 2

[<Fact>]
let ``filterByTimeRange filters to range`` () =
    let now = DateTimeOffset.UtcNow
    let events = [
        MarketEvent.Trade {
            Symbol = "AAPL"
            Price = 150.00m
            Quantity = 100L
            Side = AggressorSide.Buyer
            SequenceNumber = 1L
            Timestamp = now.AddHours(-2.0)
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }
        MarketEvent.Trade {
            Symbol = "AAPL"
            Price = 151.00m
            Quantity = 100L
            Side = AggressorSide.Buyer
            SequenceNumber = 2L
            Timestamp = now.AddHours(-1.0)
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }
        MarketEvent.Trade {
            Symbol = "AAPL"
            Price = 152.00m
            Quantity = 100L
            Side = AggressorSide.Buyer
            SequenceNumber = 3L
            Timestamp = now
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }
    ]

    let startTime = now.AddHours(-1.5)
    let endTime = now.AddMinutes(-30.0)
    let filtered = filterByTimeRange startTime endTime events |> Seq.toList
    filtered.Length |> should equal 1

[<Fact>]
let ``partitionByType separates event types`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestQuote "AAPL" 149.90m 150.00m 2L
        createTestTrade "MSFT" 350.00m 3L
        createTestQuote "MSFT" 349.90m 350.00m 4L
    ]

    let partitioned = partitionByType events
    partitioned.Trades.Length |> should equal 2
    partitioned.Quotes.Length |> should equal 2
    partitioned.Depth.Length |> should equal 0

[<Fact>]
let ``groupBySymbol groups correctly`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "MSFT" 350.00m 2L
        createTestTrade "AAPL" 151.00m 3L
    ]

    let grouped = groupBySymbol events
    grouped.Count |> should equal 2
    grouped.["AAPL"].Length |> should equal 2
    grouped.["MSFT"].Length |> should equal 1

[<Fact>]
let ``deduplicate removes duplicates`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "AAPL" 151.00m 1L // Duplicate sequence
        createTestTrade "AAPL" 152.00m 2L
    ]

    let deduped = deduplicate events |> Seq.toList
    deduped.Length |> should equal 2

[<Fact>]
let ``bufferByCount creates correct size buffers`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "AAPL" 151.00m 2L
        createTestTrade "AAPL" 152.00m 3L
        createTestTrade "AAPL" 153.00m 4L
        createTestTrade "AAPL" 154.00m 5L
    ]

    let buffers = bufferByCount 2 events |> Seq.toList
    buffers.Length |> should equal 3 // 2, 2, 1
    buffers.[0].Length |> should equal 2
    buffers.[1].Length |> should equal 2
    buffers.[2].Length |> should equal 1

[<Fact>]
let ``enrichQuotes adds calculated fields`` () =
    let quote: QuoteEvent = {
        Symbol = "AAPL"
        BidPrice = 149.90m
        BidSize = 1000L
        AskPrice = 150.10m
        AskSize = 500L
        SequenceNumber = 1L
        Timestamp = DateTimeOffset.UtcNow
        ExchangeTimestamp = None
        StreamId = None
    }

    let enriched = enrichQuotes [quote] |> Seq.head
    enriched.Spread |> should equal (Some 0.20m)
    enriched.MidPrice |> should equal (Some 150.00m)
    enriched.Imbalance.IsSome |> should equal true

[<Fact>]
let ``TransformPipeline chains transforms`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "MSFT" 350.00m 2L
        createTestTrade "AAPL" 151.00m 1L // Duplicate
        createTestTrade "AAPL" 152.00m 3L
    ]

    let result =
        TransformPipeline.create()
        |> TransformPipeline.filterSymbol "AAPL"
        |> TransformPipeline.dedupe
        |> TransformPipeline.run
        <| events
        |> Seq.toList

    result.Length |> should equal 2

[<Fact>]
let ``pipeline operator chains correctly`` () =
    let events = [
        createTestTrade "AAPL" 150.00m 1L
        createTestTrade "MSFT" 350.00m 2L
        createTestTrade "AAPL" 151.00m 3L
    ]

    let result =
        events
        |>> filterBySymbol "AAPL"
        |> Seq.toList

    result.Length |> should equal 2

[<Fact>]
let ``mergeStreams combines and sorts`` () =
    let now = DateTimeOffset.UtcNow
    let stream1 = [
        MarketEvent.Trade {
            Symbol = "AAPL"
            Price = 150.00m
            Quantity = 100L
            Side = AggressorSide.Buyer
            SequenceNumber = 1L
            Timestamp = now.AddSeconds(1.0)
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }
    ]
    let stream2 = [
        MarketEvent.Trade {
            Symbol = "MSFT"
            Price = 350.00m
            Quantity = 100L
            Side = AggressorSide.Buyer
            SequenceNumber = 2L
            Timestamp = now.AddSeconds(0.5)
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        }
    ]

    let merged = mergeStreams [stream1; stream2] |> Seq.toList
    merged.Length |> should equal 2

    // MSFT should come first (earlier timestamp)
    match MarketEvent.getSymbol merged.[0] with
    | Some s -> s |> should equal "MSFT"
    | None -> failwith "Expected symbol"
