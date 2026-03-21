/// Unit tests for F# domain types.
module Meridian.FSharp.Tests.DomainTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Domain.MarketEvents

[<Fact>]
let ``Side.ToInt converts Buy to 0`` () =
    Side.Buy.ToInt() |> should equal 0

[<Fact>]
let ``Side.ToInt converts Sell to 1`` () =
    Side.Sell.ToInt() |> should equal 1

[<Fact>]
let ``Side.FromInt converts 0 to Buy`` () =
    Side.FromInt(0) |> should equal Side.Buy

[<Fact>]
let ``Side.FromInt converts 1 to Sell`` () =
    Side.FromInt(1) |> should equal Side.Sell

[<Fact>]
let ``AggressorSide.ToInt converts correctly`` () =
    AggressorSide.Unknown.ToInt() |> should equal 0
    AggressorSide.Buyer.ToInt() |> should equal 1
    AggressorSide.Seller.ToInt() |> should equal 2

[<Fact>]
let ``AggressorSide.FromInt converts correctly`` () =
    AggressorSide.FromInt(0) |> should equal AggressorSide.Unknown
    AggressorSide.FromInt(1) |> should equal AggressorSide.Buyer
    AggressorSide.FromInt(2) |> should equal AggressorSide.Seller

[<Fact>]
let ``inferAggressor returns Buyer when trade at ask`` () =
    let result = inferAggressor 100.50m (Some 100.00m) (Some 100.50m)
    result |> should equal AggressorSide.Buyer

[<Fact>]
let ``inferAggressor returns Seller when trade at bid`` () =
    let result = inferAggressor 100.00m (Some 100.00m) (Some 100.50m)
    result |> should equal AggressorSide.Seller

[<Fact>]
let ``inferAggressor returns Unknown when no BBO`` () =
    let result = inferAggressor 100.25m None None
    result |> should equal AggressorSide.Unknown

[<Fact>]
let ``IntegritySeverity converts to int correctly`` () =
    IntegritySeverity.Info.ToInt() |> should equal 0
    IntegritySeverity.Warning.ToInt() |> should equal 1
    IntegritySeverity.Error.ToInt() |> should equal 2
    IntegritySeverity.Critical.ToInt() |> should equal 3

[<Fact>]
let ``IntegrityEvent.sequenceGap creates correct event`` () =
    let ts = DateTimeOffset.UtcNow
    let event = IntegrityEvent.sequenceGap "AAPL" ts 100L 105L 105L None None

    event.Symbol |> should equal "AAPL"
    event.Severity |> should equal IntegritySeverity.Error
    event.SequenceNumber |> should equal 105L

    match event.EventType with
    | IntegrityEventType.SequenceGap(expected, received) ->
        expected |> should equal 100L
        received |> should equal 105L
    | _ -> failwith "Expected SequenceGap event"

[<Fact>]
let ``IntegrityEvent.getDescription returns correct text`` () =
    let ts = DateTimeOffset.UtcNow
    let event = IntegrityEvent.negativeSpread "SPY" ts 100.50m 100.40m 1L

    let desc = IntegrityEvent.getDescription event
    desc.Contains("Negative spread") |> should equal true
    desc.Contains("100.50") |> should equal true
    desc.Contains("100.40") |> should equal true

[<Fact>]
let ``MarketEvent.getSymbol returns symbol for trade`` () =
    let trade = MarketEvent.createTrade "AAPL" 150.00m 100L AggressorSide.Buyer 1L DateTimeOffset.UtcNow
    MarketEvent.getSymbol trade |> should equal (Some "AAPL")

[<Fact>]
let ``MarketEvent.getSymbol returns None for heartbeat`` () =
    let heartbeat = MarketEvent.createHeartbeat DateTimeOffset.UtcNow "TEST"
    MarketEvent.getSymbol heartbeat |> should equal None

[<Fact>]
let ``MarketEvent.isTrade returns true for trade event`` () =
    let trade = MarketEvent.createTrade "AAPL" 150.00m 100L AggressorSide.Buyer 1L DateTimeOffset.UtcNow
    MarketEvent.isTrade trade |> should equal true

[<Fact>]
let ``MarketEvent.isTrade returns false for quote event`` () =
    let quote = MarketEvent.createQuote "AAPL" 149.90m 1000L 150.00m 500L 1L DateTimeOffset.UtcNow
    MarketEvent.isTrade quote |> should equal false

[<Fact>]
let ``MarketEvent.getEventType returns correct type`` () =
    let trade = MarketEvent.createTrade "AAPL" 150.00m 100L AggressorSide.Buyer 1L DateTimeOffset.UtcNow
    MarketEvent.getEventType trade |> should equal MarketEventType.Trade

    let quote = MarketEvent.createQuote "AAPL" 149.90m 1000L 150.00m 500L 1L DateTimeOffset.UtcNow
    MarketEvent.getEventType quote |> should equal MarketEventType.Quote

[<Fact>]
let ``TradeEvent with CLIMutable can be created`` () =
    let trade: TradeEvent = {
        Symbol = "MSFT"
        Price = 350.00m
        Quantity = 200L
        Side = AggressorSide.Seller
        SequenceNumber = 42L
        Timestamp = DateTimeOffset.UtcNow
        ExchangeTimestamp = None
        StreamId = Some "stream-1"
        Venue = Some "NASDAQ"
    }

    trade.Symbol |> should equal "MSFT"
    trade.Price |> should equal 350.00m
    trade.Quantity |> should equal 200L

[<Fact>]
let ``QuoteEvent with CLIMutable can be created`` () =
    let quote: QuoteEvent = {
        Symbol = "GOOGL"
        BidPrice = 140.00m
        BidSize = 500L
        AskPrice = 140.05m
        AskSize = 300L
        SequenceNumber = 1L
        Timestamp = DateTimeOffset.UtcNow
        ExchangeTimestamp = Some DateTimeOffset.UtcNow
        StreamId = None
    }

    quote.Symbol |> should equal "GOOGL"
    quote.BidPrice |> should equal 140.00m
    quote.AskPrice |> should equal 140.05m
