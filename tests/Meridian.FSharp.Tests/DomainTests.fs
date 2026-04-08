/// Unit tests for F# domain types.
module Meridian.FSharp.Tests.DomainTests

open System
open System.Text.Json
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain
open Meridian.FSharp.SecurityMasterInterop

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

let private createEquityCreateCommand classification =
    let effectiveFrom = DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero)

    {
        SecurityId = SecurityId(Guid.NewGuid())
        Common = {
            DisplayName = "Convertible Preferred Test Security"
            Currency = "USD"
            CountryOfRisk = None
            IssuerName = Some "Meridian Test Issuer"
            Exchange = Some "NYSE"
            LotSize = Some 100m
            TickSize = Some 0.01m
        }
        Identifiers = [
            {
                Kind = IdentifierKind.Ticker
                Value = "MTEST"
                IsPrimary = true
                ValidFrom = effectiveFrom
                ValidTo = None
            }
        ]
        Kind =
            SecurityKind.Equity {
                ShareClass = Some "A"
                VotingRightsCat = Some VotingRightsCat.LimitedVoting
                Classification = classification
            }
        EffectiveFrom = effectiveFrom
        Provenance = {
            SourceSystem = "domain-tests"
            SourceRecordId = None
            AsOf = effectiveFrom
            UpdatedBy = "domain-tests"
            Reason = Some "phase-1-5"
        }
    }

let private createSecurityRecord classification =
    match SecurityMaster.create (createEquityCreateCommand classification) with
    | Ok [ SecurityMasterEvent.SecurityCreated record ] -> record
    | Ok _ -> failwith "Expected a single SecurityCreated event"
    | Error errors -> failwithf "Expected SecurityCreated record, got: %A" errors

let private getDateOnlyProperty (element: JsonElement) (propertyName: string) =
    element.GetProperty(propertyName).GetString() |> DateOnly.Parse

let private createConvertiblePreferredClassification () =
    let preferredTerms = {
        DividendRate = Some 6.25m
        DividendType = DividendType.Fixed
        RedemptionPrice = Some 25.00m
        RedemptionDate = Some (DateOnly(2032, 1, 15))
        CallableDate = Some (DateOnly(2030, 1, 15))
        ParticipationTerms = Some {
            ParticipatesInCommonDividends = true
            AdditionalDividendThreshold = Some 1.50m
        }
        LiquidationPreference = LiquidationPreference.Senior 1.0m
    }
    let convertibleTerms = {
        UnderlyingSecurityId = SecurityId(Guid.NewGuid())
        ConversionRatio = 2.50m
        ConversionPrice = Some 48.00m
        ConversionStartDate = Some (DateOnly(2027, 1, 15))
        ConversionEndDate = Some (DateOnly(2031, 12, 31))
    }

    preferredTerms, convertibleTerms, EquityClassification.ConvertiblePreferred(preferredTerms, convertibleTerms)

let private createEquityAmendCommand
    (currentRecord: SecurityMasterRecord)
    (nextClassification: EquityClassification)
    (commonOverride: CommonTerms option)
    : AmendTerms =
    let nextKind =
        match currentRecord.Kind with
        | SecurityKind.Equity terms ->
            SecurityKind.Equity { terms with Classification = Some nextClassification }
        | _ ->
            failwith "Expected SecurityKind.Equity"

    {
        SecurityId = currentRecord.SecurityId
        ExpectedVersion = currentRecord.Version
        Common = commonOverride
        Kind = Some nextKind
        IdentifiersToAdd = []
        IdentifiersToExpire = []
        EffectiveFrom = currentRecord.EffectiveFrom
        Provenance = {
            SourceSystem = "domain-tests"
            SourceRecordId = None
            AsOf = currentRecord.EffectiveFrom.AddMinutes(5.0)
            UpdatedBy = "domain-tests"
            Reason = Some "phase-1-5-amend"
        }
    }

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
        Subclass = Some BondSubclass.Corporate
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
    CorpActEvent.eventType (CorpActEvent.ReturnOfCapital(sid, id, date, None, 2.50m, "USD")) |> should equal "ReturnOfCapital"

// ---------------------------------------------------------------------------
// New domain additions — VotingRightsCat, BondSubclass, OptionTerms,
// SwapTerms.CalendarRefs, ReturnOfCapital
// ---------------------------------------------------------------------------

[<Fact>]
let ``EquityTerms carries VotingRightsCat correctly`` () =
    let allCases = [
        VotingRightsCat.FullVoting,    "FullVoting"
        VotingRightsCat.LimitedVoting, "LimitedVoting"
        VotingRightsCat.NonVoting,     "NonVoting"
        VotingRightsCat.DualClass,     "DualClass"
        VotingRightsCat.SuperVoting,   "SuperVoting"
    ]
    for (cat, expected) in allCases do
        let terms = { ShareClass = None; VotingRightsCat = Some cat; Classification = None }
        terms.VotingRightsCat |> should equal (Some cat)
        VotingRightsCat.asString cat |> should equal expected

[<Fact>]
let ``EquityTerms VotingRightsCat OtherVotingRights carries string payload`` () =
    let terms = {
        ShareClass = None
        VotingRightsCat = Some (VotingRightsCat.OtherVotingRights "Restricted")
        Classification = None
    }
    match terms.VotingRightsCat with
    | Some (VotingRightsCat.OtherVotingRights label) -> label |> should equal "Restricted"
    | _ -> failwith "Expected OtherVotingRights"

[<Fact>]
let ``SecurityMaster.create keeps common equity flows valid when classification is omitted`` () =
    let command = createEquityCreateCommand None

    match SecurityMaster.create command with
    | Ok [ SecurityMasterEvent.SecurityCreated record ] ->
        match record.Kind with
        | SecurityKind.Equity terms ->
            terms.Classification |> should equal None
            terms.ShareClass |> should equal (Some "A")
        | _ -> failwith "Expected SecurityKind.Equity"
    | Ok _ -> failwith "Expected a single SecurityCreated event"
    | Error errors -> failwithf "Expected common equity flow to remain valid, got: %A" errors

[<Fact>]
let ``SecurityMaster.create accepts convertible preferred equity terms`` () =
    let _, _, classification = createConvertiblePreferredClassification ()
    let command = createEquityCreateCommand (Some classification)

    match SecurityMaster.create command with
    | Ok [ SecurityMasterEvent.SecurityCreated record ] ->
        match record.Kind with
        | SecurityKind.Equity terms ->
            terms.Classification |> should equal (Some classification)
        | _ -> failwith "Expected SecurityKind.Equity"
    | Ok _ -> failwith "Expected a single SecurityCreated event"
    | Error errors -> failwithf "Expected convertible preferred equity terms to validate, got: %A" errors

[<Fact>]
let ``SecurityMaster.create rejects invalid preferred and convertible term constraints`` () =
    let preferredTerms = {
        DividendRate = Some -0.50m
        DividendType = DividendType.Cumulative
        RedemptionPrice = Some 0m
        RedemptionDate = Some (DateOnly(2030, 6, 1))
        CallableDate = Some (DateOnly(2031, 6, 1))
        ParticipationTerms = Some {
            ParticipatesInCommonDividends = true
            AdditionalDividendThreshold = Some -1.0m
        }
        LiquidationPreference = LiquidationPreference.Senior 0m
    }
    let convertibleTerms = {
        UnderlyingSecurityId = SecurityId(Guid.Empty)
        ConversionRatio = 0m
        ConversionPrice = Some 0m
        ConversionStartDate = Some (DateOnly(2032, 1, 1))
        ConversionEndDate = Some (DateOnly(2031, 1, 1))
    }
    let command =
        createEquityCreateCommand
            (Some (EquityClassification.ConvertiblePreferred(preferredTerms, convertibleTerms)))

    match SecurityMaster.create command with
    | Error errors ->
        let codes = errors |> List.map (fun error -> error.Code)
        codes |> List.contains "equity_dividend_rate_invalid" |> should equal true
        codes |> List.contains "equity_redemption_price_invalid" |> should equal true
        codes |> List.contains "equity_callable_date_invalid" |> should equal true
        codes |> List.contains "equity_participation_threshold_invalid" |> should equal true
        codes |> List.contains "equity_liquidation_preference_invalid" |> should equal true
        codes |> List.contains "equity_underlying_security_required" |> should equal true
        codes |> List.contains "equity_conversion_ratio_invalid" |> should equal true
        codes |> List.contains "equity_conversion_price_invalid" |> should equal true
        codes |> List.contains "equity_conversion_window_invalid" |> should equal true
    | Ok _ -> failwith "Expected invalid preferred and convertible terms to be rejected"

[<Fact>]
let ``SecurityMaster.amend emits PreferredTermsAmended when only preferred terms change`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let nextClassification =
        EquityClassification.ConvertiblePreferred(
            { preferredTerms with DividendRate = Some 7.00m },
            convertibleTerms)
    let command = createEquityAmendCommand currentRecord nextClassification None

    match SecurityMaster.amend currentRecord command with
    | Ok [ SecurityMasterEvent.PreferredTermsAmended(beforeVersion, record) ] ->
        beforeVersion |> should equal currentRecord.Version
        record.Version |> should equal (currentRecord.Version + 1L)

        match record.Kind with
        | SecurityKind.Equity terms ->
            terms.Classification |> should equal (Some nextClassification)
        | _ ->
            failwith "Expected SecurityKind.Equity"
    | Ok events ->
        failwithf "Expected PreferredTermsAmended event, got: %A" events
    | Error errors ->
        failwithf "Expected preferred amend to succeed, got: %A" errors

[<Fact>]
let ``SecurityMaster.amend emits ConversionTermsAmended when only conversion terms change`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let nextClassification =
        EquityClassification.ConvertiblePreferred(
            preferredTerms,
            { convertibleTerms with ConversionRatio = 3.00m })
    let command = createEquityAmendCommand currentRecord nextClassification None

    match SecurityMaster.amend currentRecord command with
    | Ok [ SecurityMasterEvent.ConversionTermsAmended(beforeVersion, record) ] ->
        beforeVersion |> should equal currentRecord.Version
        record.Version |> should equal (currentRecord.Version + 1L)

        match record.Kind with
        | SecurityKind.Equity terms ->
            terms.Classification |> should equal (Some nextClassification)
        | _ ->
            failwith "Expected SecurityKind.Equity"
    | Ok events ->
        failwithf "Expected ConversionTermsAmended event, got: %A" events
    | Error errors ->
        failwithf "Expected conversion amend to succeed, got: %A" errors

[<Fact>]
let ``SecurityMaster.amend keeps generic TermsAmended when common and preferred terms change together`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let nextCommon = {
        currentRecord.Common with
            DisplayName = "Convertible Preferred Test Security Updated"
    }
    let nextClassification =
        EquityClassification.ConvertiblePreferred(
            { preferredTerms with RedemptionPrice = Some 26.50m },
            convertibleTerms)
    let command = createEquityAmendCommand currentRecord nextClassification (Some nextCommon)

    match SecurityMaster.amend currentRecord command with
    | Ok [ SecurityMasterEvent.TermsAmended(beforeVersion, record) ] ->
        beforeVersion |> should equal currentRecord.Version
        record.Common.DisplayName |> should equal nextCommon.DisplayName
    | Ok events ->
        failwithf "Expected generic TermsAmended event, got: %A" events
    | Error errors ->
        failwithf "Expected mixed amend to succeed, got: %A" errors

[<Fact>]
let ``SecurityMasterCommandFacade surfaces specialized event types and clears them on validation failure`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let validClassification =
        EquityClassification.ConvertiblePreferred(
            { preferredTerms with CallableDate = Some (DateOnly(2030, 6, 15)) },
            convertibleTerms)
    let validCommand = createEquityAmendCommand currentRecord validClassification None
    let validResult = SecurityMasterCommandFacade.Amend(currentRecord, validCommand)
    validResult.IsSuccess |> should equal true
    validResult.PrimaryEventType |> should equal "PreferredTermsAmended"
    validResult.EventTypes |> should equal [| "PreferredTermsAmended" |]

    let invalidClassification =
        EquityClassification.ConvertiblePreferred(
            { preferredTerms with DividendRate = Some -1.00m },
            convertibleTerms)
    let invalidCommand = createEquityAmendCommand currentRecord invalidClassification None
    let invalidResult = SecurityMasterCommandFacade.Amend(currentRecord, invalidCommand)
    invalidResult.IsSuccess |> should equal false
    invalidResult.PrimaryEventType |> should equal String.Empty
    invalidResult.EventTypes |> should equal [||]

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes convertible preferred nested term payloads`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let record = createSecurityRecord (Some classification)
    let wrapper = SecurityMasterSnapshotWrapper(record)
    use document = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = document.RootElement

    payload.GetProperty("classification").GetString() |> should equal "ConvertiblePreferred"
    payload.GetProperty("shareClass").GetString() |> should equal "A"
    payload.GetProperty("votingRightsCat").GetString() |> should equal "LimitedVoting"

    let preferredPayload = payload.GetProperty("preferredTerms")
    preferredPayload.GetProperty("dividendType").GetString() |> should equal "Fixed"
    preferredPayload.GetProperty("redemptionPrice").GetDecimal() |> should equal preferredTerms.RedemptionPrice.Value
    getDateOnlyProperty preferredPayload "callableDate" |> should equal preferredTerms.CallableDate.Value
    let liquidationPreference = preferredPayload.GetProperty("liquidationPreference")
    liquidationPreference.GetProperty("kind").GetString() |> should equal "Senior"
    liquidationPreference.GetProperty("multiple").GetDecimal() |> should equal 1.0m
    let participationTerms = preferredPayload.GetProperty("participationTerms")
    participationTerms.GetProperty("participatesInCommonDividends").GetBoolean() |> should equal true
    participationTerms.GetProperty("additionalDividendThreshold").GetDecimal() |> should equal preferredTerms.ParticipationTerms.Value.AdditionalDividendThreshold.Value

    let convertiblePayload = payload.GetProperty("convertibleTerms")
    let (SecurityId underlyingSecurityId) = convertibleTerms.UnderlyingSecurityId
    convertiblePayload.GetProperty("underlyingSecurityId").GetGuid() |> should equal underlyingSecurityId
    convertiblePayload.GetProperty("conversionRatio").GetDecimal() |> should equal convertibleTerms.ConversionRatio
    convertiblePayload.GetProperty("conversionPrice").GetDecimal() |> should equal convertibleTerms.ConversionPrice.Value
    getDateOnlyProperty convertiblePayload "conversionStartDate" |> should equal convertibleTerms.ConversionStartDate.Value
    getDateOnlyProperty convertiblePayload "conversionEndDate" |> should equal convertibleTerms.ConversionEndDate.Value

[<Fact>]
let ``SecurityMasterLegacyUpgrade preserves convertible preferred classification in economic definition`` () =
    let preferredTerms, _, classification = createConvertiblePreferredClassification ()
    let record = createSecurityRecord (Some classification)
    let definition = SecurityMasterLegacyUpgrade.toEconomicDefinition record

    definition.Classification.AssetClass |> should equal AssetClass.Equity
    definition.Classification.Family |> should equal (Some AssetFamily.PreferredEquity)
    definition.Classification.SubType |> should equal SecuritySubType.PreferredShare
    definition.Classification.TypeName |> should equal "ConvertiblePreferredEquity"

    let equityBehavior = definition.Terms.EquityBehavior |> Option.defaultWith (fun () -> failwith "Expected equity behavior terms")
    equityBehavior.ShareClass |> should equal (Some "A")
    equityBehavior.DistributionType |> should equal (Some (DividendType.asString preferredTerms.DividendType))

    let redemption = definition.Terms.Redemption |> Option.defaultWith (fun () -> failwith "Expected redemption terms")
    redemption.RedemptionPrice |> should equal preferredTerms.RedemptionPrice

    let call = definition.Terms.Call |> Option.defaultWith (fun () -> failwith "Expected call terms")
    call.IsCallable |> should equal true
    call.FirstCallDate |> should equal preferredTerms.CallableDate

[<Fact>]
let ``BondTerms factory carries BondSubclass correctly`` () =
    let maturity = DateOnly(2032, 6, 1)
    let terms = { BondTerms.fixedRate maturity 4.5m None None with Subclass = Some BondSubclass.Convertible }
    terms.Subclass |> should equal (Some BondSubclass.Convertible)

[<Fact>]
let ``BondSubclass OtherBond carries string payload`` () =
    let sub = BondSubclass.OtherBond "CovLite"
    match sub with
    | BondSubclass.OtherBond label -> label |> should equal "CovLite"
    | _ -> failwith "Expected OtherBond"

[<Fact>]
let ``OptionTerms carries UnderlyingInstrumentType correctly`` () =
    let sid = SecurityId(Guid.NewGuid())
    let makeTerms instrType = {
        UnderlyingId = sid
        PutCall = "Call"
        Strike = 150m
        Expiry = DateOnly(2025, 12, 19)
        Multiplier = 100m
        UnderlyingInstrumentType = Some instrType
    }
    makeTerms Meridian.Contracts.Domain.Enums.InstrumentType.Equity
    |> fun t -> t.UnderlyingInstrumentType |> should equal (Some Meridian.Contracts.Domain.Enums.InstrumentType.Equity)
    makeTerms Meridian.Contracts.Domain.Enums.InstrumentType.Future
    |> fun t -> t.UnderlyingInstrumentType |> should equal (Some Meridian.Contracts.Domain.Enums.InstrumentType.Future)
    makeTerms Meridian.Contracts.Domain.Enums.InstrumentType.Index
    |> fun t -> t.UnderlyingInstrumentType |> should equal (Some Meridian.Contracts.Domain.Enums.InstrumentType.Index)
    makeTerms Meridian.Contracts.Domain.Enums.InstrumentType.Swap
    |> fun t -> t.UnderlyingInstrumentType |> should equal (Some Meridian.Contracts.Domain.Enums.InstrumentType.Swap)
    let noUnderlyingTerms = { makeTerms Meridian.Contracts.Domain.Enums.InstrumentType.Equity with UnderlyingInstrumentType = None }
    noUnderlyingTerms.UnderlyingInstrumentType |> should equal None

[<Fact>]
let ``SwapTerms CalendarRefs is accessible and can hold multiple entries`` () =
    let terms = {
        EffectiveDate = DateOnly(2024, 1, 15)
        MaturityDate = DateOnly(2034, 1, 15)
        Legs = []
        CalendarRefs = [ "TARGET2"; "FedWire" ]
    }
    terms.CalendarRefs |> should equal [ "TARGET2"; "FedWire" ]
    terms.CalendarRefs |> List.length |> should equal 2

[<Fact>]
let ``ReturnOfCapital securityId and exDate are extracted correctly`` () =
    let sid = SecurityId(Guid.NewGuid())
    let id = CorpActId(Guid.NewGuid())
    let exDate = DateOnly(2024, 9, 30)
    let evt = CorpActEvent.ReturnOfCapital(sid, id, exDate, Some (DateOnly(2024, 10, 15)), 1.25m, "USD")
    CorpActEvent.securityId evt |> should equal sid
    CorpActEvent.corpActId evt |> should equal id
    CorpActEvent.exDate evt |> should equal exDate
    CorpActEvent.eventType evt |> should equal "ReturnOfCapital"
