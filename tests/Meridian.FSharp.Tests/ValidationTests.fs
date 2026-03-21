/// Unit tests for F# validation logic.
module Meridian.FSharp.Tests.ValidationTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Validation.ValidationTypes
open Meridian.FSharp.Validation.TradeValidator
open Meridian.FSharp.Validation.QuoteValidator

let createValidTrade () : TradeEvent = {
    Symbol = "AAPL"
    Price = 150.00m
    Quantity = 100L
    Side = AggressorSide.Buyer
    SequenceNumber = 1L
    Timestamp = DateTimeOffset.UtcNow
    ExchangeTimestamp = None
    StreamId = None
    Venue = None
}

let createValidQuote () : QuoteEvent = {
    Symbol = "AAPL"
    BidPrice = 149.95m
    BidSize = 1000L
    AskPrice = 150.05m
    AskSize = 500L
    SequenceNumber = 1L
    Timestamp = DateTimeOffset.UtcNow
    ExchangeTimestamp = None
    StreamId = None
}

[<Fact>]
let ``Validate.price accepts valid positive price`` () =
    let result = Validate.priceDefault 100.00m
    match result with
    | Ok value -> value |> should equal 100.00m
    | Error _ -> failwith "Expected Ok result"

[<Fact>]
let ``Validate.price rejects zero price`` () =
    let result = Validate.priceDefault 0m
    match result with
    | Error [ValidationError.InvalidPrice _] -> ()
    | _ -> failwith "Expected InvalidPrice error"

[<Fact>]
let ``Validate.price rejects negative price`` () =
    let result = Validate.priceDefault -10.00m
    match result with
    | Error [ValidationError.InvalidPrice _] -> ()
    | _ -> failwith "Expected InvalidPrice error"

[<Fact>]
let ``Validate.price rejects price above maximum`` () =
    let result = Validate.price 1000m 2000m
    match result with
    | Error [ValidationError.InvalidPrice (value, _)] ->
        value |> should equal 2000m
    | _ -> failwith "Expected InvalidPrice error"

[<Fact>]
let ``Validate.quantity accepts valid quantity`` () =
    let result = Validate.quantityDefault 100L
    match result with
    | Ok value -> value |> should equal 100L
    | Error _ -> failwith "Expected Ok result"

[<Fact>]
let ``Validate.quantity rejects negative quantity`` () =
    let result = Validate.quantityDefault -1L
    match result with
    | Error [ValidationError.InvalidQuantity _] -> ()
    | _ -> failwith "Expected InvalidQuantity error"

[<Fact>]
let ``Validate.symbol accepts valid symbol`` () =
    let result = Validate.symbolDefault "AAPL"
    match result with
    | Ok value -> value |> should equal "AAPL"
    | Error _ -> failwith "Expected Ok result"

[<Fact>]
let ``Validate.symbol rejects empty symbol`` () =
    let result = Validate.symbolDefault ""
    match result with
    | Error [ValidationError.InvalidSymbol _] -> ()
    | _ -> failwith "Expected InvalidSymbol error"

[<Fact>]
let ``Validate.symbol rejects symbol exceeding max length`` () =
    let result = Validate.symbol 5 "TOOLONGSYMBOL"
    match result with
    | Error [ValidationError.InvalidSymbol _] -> ()
    | _ -> failwith "Expected InvalidSymbol error"

[<Fact>]
let ``Validate.spread accepts positive spread`` () =
    let result = Validate.spread 100.00m 100.10m
    match result with
    | Ok (bid, ask) ->
        bid |> should equal 100.00m
        ask |> should equal 100.10m
    | Error _ -> failwith "Expected Ok result"

[<Fact>]
let ``Validate.spread rejects crossed quotes`` () =
    let result = Validate.spread 100.10m 100.00m
    match result with
    | Error [ValidationError.InvalidSpread _] -> ()
    | _ -> failwith "Expected InvalidSpread error"

[<Fact>]
let ``Validate.spread rejects equal bid/ask`` () =
    let result = Validate.spread 100.00m 100.00m
    match result with
    | Error [ValidationError.InvalidSpread _] -> ()
    | _ -> failwith "Expected InvalidSpread error"

[<Fact>]
let ``validateTrade succeeds for valid trade`` () =
    let trade = createValidTrade()
    let result = validateTradeDefault trade
    match result with
    | Ok validated ->
        validated.Symbol |> should equal "AAPL"
        validated.Price |> should equal 150.00m
    | Error _ -> failwith "Expected valid trade"

[<Fact>]
let ``validateTrade fails with invalid price`` () =
    let trade = { createValidTrade() with Price = -10.00m }
    let result = validateTradeDefault trade
    match result with
    | Error errors ->
        errors |> should not' (be Empty)
    | Ok _ -> failwith "Expected validation failure"

[<Fact>]
let ``validateTrade accumulates multiple errors`` () =
    let trade = {
        createValidTrade() with
            Price = -10.00m
            Quantity = -5L
            Symbol = ""
    }
    let result = validateTradeDefault trade
    match result with
    | Error errors ->
        errors.Length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "Expected validation failure"

[<Fact>]
let ``isValidTrade returns true for valid trade`` () =
    let trade = createValidTrade()
    isValidTrade trade |> should equal true

[<Fact>]
let ``isValidTrade returns false for invalid trade`` () =
    let trade = { createValidTrade() with Price = 0m }
    isValidTrade trade |> should equal false

[<Fact>]
let ``validateQuote succeeds for valid quote`` () =
    let quote = createValidQuote()
    let result = validateQuoteDefault quote
    match result with
    | Ok validated ->
        validated.Symbol |> should equal "AAPL"
        validated.BidPrice |> should equal 149.95m
    | Error _ -> failwith "Expected valid quote"

[<Fact>]
let ``validateQuote fails for crossed quotes`` () =
    let quote = { createValidQuote() with BidPrice = 150.10m; AskPrice = 150.00m }
    let result = validateQuoteDefault quote
    match result with
    | Error errors ->
        errors |> should not' (be Empty)
    | Ok _ -> failwith "Expected validation failure for crossed quotes"

[<Fact>]
let ``hasValidSpread returns true for valid spread`` () =
    let quote = createValidQuote()
    hasValidSpread quote |> should equal true

[<Fact>]
let ``hasValidSpread returns false for crossed spread`` () =
    let quote = { createValidQuote() with BidPrice = 150.10m; AskPrice = 150.00m }
    hasValidSpread quote |> should equal false

[<Fact>]
let ``getSpread calculates correct spread`` () =
    let quote = createValidQuote()
    let spread = getSpread quote
    spread |> should equal (Some 0.10m)

[<Fact>]
let ``getMidPrice calculates correct mid-price`` () =
    let quote = createValidQuote()
    let mid = getMidPrice quote
    mid |> should equal (Some 150.00m)

[<Fact>]
let ``TradeValidationConfig.createRealTime creates strict config`` () =
    let config = TradeValidationConfig.createRealTime 100L
    config.MaxTimestampAge.TotalSeconds |> should be (lessThan 10.0)
    config.LastSequenceNumber |> should equal (Some 100L)

[<Fact>]
let ``TradeValidationConfig.createHistorical creates lenient config`` () =
    let config = TradeValidationConfig.createHistorical()
    config.MaxTimestampAge.TotalDays |> should be (greaterThan 365.0)
    config.LastSequenceNumber |> should equal None
