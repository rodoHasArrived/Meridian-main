/// Unit tests for F# domain types.
module Meridian.FSharp.Tests.DomainTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain

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

open Meridian.FSharp.Domain

[<Fact>]
let ``BondTerms fixedRate factory sets coupon correctly`` () =
    let maturity = DateOnly(2030, 6, 15)
    let terms = BondTerms.fixedRate maturity 5.25m (Some "30/360") (Some "Acme Corp")
    terms.Maturity |> should equal maturity
    terms.IsCallable |> should equal false
    terms.IssuerName |> should equal (Some "Acme Corp")
    match terms.Coupon with
    | BondCouponStructure.Fixed(rate, dc) ->
        rate |> should equal 5.25m
        dc |> should equal (Some "30/360")
    | _ -> failwith "Expected Fixed coupon"

[<Fact>]
let ``BondTerms floatingRate factory sets coupon correctly`` () =
    let maturity = DateOnly(2028, 3, 1)
    let terms = BondTerms.floatingRate maturity "SOFR" (Some 150m) (Some "Issuer Inc")
    terms.Maturity |> should equal maturity
    match terms.Coupon with
    | BondCouponStructure.Floating(index, spread, _, _, _) ->
        index |> should equal "SOFR"
        spread |> should equal (Some 150m)
    | _ -> failwith "Expected Floating coupon"

[<Fact>]
let ``BondTerms zeroCoupon factory creates zero-coupon bond`` () =
    let maturity = DateOnly(2025, 12, 31)
    let terms = BondTerms.zeroCoupon maturity None
    terms.Maturity |> should equal maturity
    match terms.Coupon with
    | BondCouponStructure.ZeroCoupon -> ()
    | _ -> failwith "Expected ZeroCoupon"

[<Fact>]
let ``BondTerms couponRate returns Some for fixed and None for zero-coupon`` () =
    let fixedBond = BondTerms.fixedRate (DateOnly(2030, 1, 1)) 3.5m None None
    let zero = BondTerms.zeroCoupon (DateOnly(2030, 1, 1)) None
    BondTerms.couponRate fixedBond |> should equal (Some 3.5m)
    BondTerms.couponRate zero |> should equal None

[<Fact>]
let ``BondTerms callable bond preserves callDate`` () =
    let maturity = DateOnly(2035, 6, 1)
    let callDate = DateOnly(2028, 6, 1)
    let terms = {
        Maturity = maturity
        IssueDate = Some (DateOnly(2020, 6, 1))
        Coupon = BondCouponStructure.Fixed(4.0m, Some "Act/360")
        IsCallable = true
        CallDate = Some callDate
        IssuerName = Some "Corp A"
        Seniority = Some "Senior"
    }
    terms.IsCallable |> should equal true
    terms.CallDate |> should equal (Some callDate)

// ---------------------------------------------------------------------------
// CorpActEvent module tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``CorpActEvent.securityId extracts securityId from Dividend`` () =
    let sid = SecurityId(Guid.NewGuid())
    let evt = CorpActEvent.Dividend(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 2, 1), None, 1.00m, "USD")
    CorpActEvent.securityId evt |> should equal sid

[<Fact>]
let ``CorpActEvent.securityId extracts securityId from StockSplit`` () =
    let sid = SecurityId(Guid.NewGuid())
    let evt = CorpActEvent.StockSplit(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 3, 1), 2m)
    CorpActEvent.securityId evt |> should equal sid

[<Fact>]
let ``CorpActEvent.securityId extracts securityId from SpinOff`` () =
    let sid = SecurityId(Guid.NewGuid())
    let evt = CorpActEvent.SpinOff(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 4, 1), SecurityId(Guid.NewGuid()), 0.5m)
    CorpActEvent.securityId evt |> should equal sid

[<Fact>]
let ``CorpActEvent.securityId extracts securityId from MergerAbsorption`` () =
    let sid = SecurityId(Guid.NewGuid())
    let evt = CorpActEvent.MergerAbsorption(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 5, 1), SecurityId(Guid.NewGuid()), 1.25m)
    CorpActEvent.securityId evt |> should equal sid

[<Fact>]
let ``CorpActEvent.securityId extracts securityId from RightsIssue`` () =
    let sid = SecurityId(Guid.NewGuid())
    let evt = CorpActEvent.RightsIssue(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 6, 1), 15.00m, 2.0m)
    CorpActEvent.securityId evt |> should equal sid

[<Fact>]
let ``CorpActEvent.corpActId extracts id from each case`` () =
    let sid = SecurityId(Guid.NewGuid())
    let id = CorpActId(Guid.NewGuid())
    let div = CorpActEvent.Dividend(sid, id, DateOnly(2024, 2, 1), None, 1.00m, "USD")
    let split = CorpActEvent.StockSplit(sid, id, DateOnly(2024, 3, 1), 2m)
    CorpActEvent.corpActId div |> should equal id
    CorpActEvent.corpActId split |> should equal id

[<Fact>]
let ``CorpActEvent.exDate extracts ex-date from all cases`` () =
    let sid = SecurityId(Guid.NewGuid())
    let id = CorpActId(Guid.NewGuid())
    let exDate = DateOnly(2024, 7, 15)
    let div = CorpActEvent.Dividend(sid, id, exDate, None, 0.50m, "USD")
    let split = CorpActEvent.StockSplit(sid, id, exDate, 3m)
    let spinOff = CorpActEvent.SpinOff(sid, id, exDate, SecurityId(Guid.NewGuid()), 0.25m)
    let merger = CorpActEvent.MergerAbsorption(sid, id, exDate, SecurityId(Guid.NewGuid()), 0.8m)
    let rights = CorpActEvent.RightsIssue(sid, id, exDate, 10.00m, 1.0m)
    CorpActEvent.exDate div |> should equal exDate
    CorpActEvent.exDate split |> should equal exDate
    CorpActEvent.exDate spinOff |> should equal exDate
    CorpActEvent.exDate merger |> should equal exDate
    CorpActEvent.exDate rights |> should equal exDate

[<Fact>]
let ``CorpActEvent.eventType returns correct string for each case`` () =
    let sid = SecurityId(Guid.NewGuid())
    let id = CorpActId(Guid.NewGuid())
    let date = DateOnly(2024, 1, 1)
    CorpActEvent.eventType (CorpActEvent.Dividend(sid, id, date, None, 1m, "USD")) |> should equal "Dividend"
    CorpActEvent.eventType (CorpActEvent.StockSplit(sid, id, date, 2m)) |> should equal "StockSplit"
    CorpActEvent.eventType (CorpActEvent.SpinOff(sid, id, date, SecurityId(Guid.NewGuid()), 0.5m)) |> should equal "SpinOff"
    CorpActEvent.eventType (CorpActEvent.MergerAbsorption(sid, id, date, SecurityId(Guid.NewGuid()), 1m)) |> should equal "MergerAbsorption"
    CorpActEvent.eventType (CorpActEvent.RightsIssue(sid, id, date, 10m, 1m)) |> should equal "RightsIssue"
