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
        Subclass = BondSubclass.Corporate
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
    let evt = CorpActEvent.RightsIssue(sid, CorpActId(Guid.NewGuid()), DateOnly(2024, 6, 1), 15.00m, 2.0m, true, None)
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
    let rights = CorpActEvent.RightsIssue(sid, id, exDate, 10.00m, 1.0m, true, None)
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
    CorpActEvent.eventType (CorpActEvent.RightsIssue(sid, id, date, 10m, 1m, true, None)) |> should equal "RightsIssue"

// ---------------------------------------------------------------------------
// Structured / factorable bond tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``BondSubclass MortgageBacked sets correct subclass on BondTerms`` () =
    let terms = BondTerms.fixedRate (DateOnly(2050, 1, 1)) 4.5m (Some "Act/360") (Some "Freddie Mac")
    let mbs = { terms with Subclass = BondSubclass.MortgageBacked }
    mbs.Subclass |> should equal BondSubclass.MortgageBacked
    mbs.IsCallable |> should equal false

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

let private createBaseCommonTerms () = {
    DisplayName = "Convertible Preferred Test Security"
    Currency = "usd"
    CountryOfRisk = Some "US"
    IssuerName = Some "Alpha Capital"
    Exchange = Some "NASDAQ"
    LotSize = Some 1m
    TickSize = Some 0.01m
    PrimaryListingMic = Some "xnas"
    CountryOfIncorporation = Some "US"
    SettlementCycleDays = Some 2
    HolidayCalendarId = Some "NYSE"
}

let private createBaseProvenance effectiveFrom = {
    SourceSystem = "integration-tests"
    SourceRecordId = Some "security-master-001"
    AsOf = effectiveFrom
    UpdatedBy = "qa-suite"
    Reason = Some "unit-test"
}

let private createPrimaryTicker effectiveFrom = {
    Kind = IdentifierKind.Ticker
    Value = "ACPRA"
    IsPrimary = true
    ValidFrom = effectiveFrom
    ValidTo = None
}

let private createConvertiblePreferredClassification () =
    let preferredTerms = {
        DividendRate = Some 6.25m
        DividendType = DividendType.Fixed
        RedemptionPrice = Some 25.00m
        RedemptionDate = Some (DateOnly(2032, 6, 1))
        CallableDate = Some (DateOnly(2030, 6, 15))
        ParticipationTerms = Some {
            ParticipatesInCommonDividends = true
            AdditionalDividendThreshold = Some 0.50m
        }
        LiquidationPreference = LiquidationPreference.Senior 1.0m
    }

    let convertibleTerms = {
        UnderlyingSecurityId = SecurityId(Guid.NewGuid())
        ConversionRatio = 2.50m
        ConversionPrice = Some 50.00m
        ConversionStartDate = Some (DateOnly(2027, 1, 1))
        ConversionEndDate = Some (DateOnly(2031, 12, 31))
    }

    preferredTerms, convertibleTerms, EquityClassification.ConvertiblePreferred(preferredTerms, convertibleTerms)

let private createEquityCreateCommand classification =
    let effectiveFrom = DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero)

    {
        SecurityId = SecurityId(Guid.NewGuid())
        Common = createBaseCommonTerms ()
        Identifiers = [ createPrimaryTicker effectiveFrom ]
        Kind =
            SecurityKind.Equity {
                ShareClass = Some "A"
                VotingRightsCat = Some VotingRightsCat.LimitedVoting
                Classification = classification
            }
        EffectiveFrom = effectiveFrom
        Provenance = createBaseProvenance effectiveFrom
    }

let private createSecurityRecord (classification: EquityClassification option) : SecurityMasterRecord =
    match SecurityMaster.create (createEquityCreateCommand classification) with
    | Ok [ SecurityMasterEvent.SecurityCreated record ] ->
        record |> SecurityMasterRecord.normalize
    | Ok events ->
        failwithf "Expected a single SecurityCreated event, got: %A" events
    | Error errors ->
        failwithf "Expected create to succeed, got: %A" errors

let private createEquityAmendCommand
    (currentRecord: SecurityMasterRecord)
    (nextClassification: EquityClassification)
    (nextCommon: CommonTerms option)
    : AmendTerms =
    let currentTerms =
        match currentRecord.Kind with
        | SecurityKind.Equity terms -> terms
        | _ -> failwith "Expected SecurityKind.Equity"

    let effectiveFrom = currentRecord.EffectiveFrom.AddDays(1.0)
    {
        SecurityId = currentRecord.SecurityId
        ExpectedVersion = currentRecord.Version
        Common = nextCommon
        Kind = Some (SecurityKind.Equity { currentTerms with Classification = Some nextClassification })
        IdentifiersToAdd = []
        IdentifiersToExpire = []
        EffectiveFrom = effectiveFrom
        Provenance = {
            currentRecord.Provenance with
                AsOf = effectiveFrom
                UpdatedBy = "qa-amend"
                Reason = Some "classification-update"
        }
    }

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
let ``SecurityMaster.create rejects missing primary identifier and blank common fields`` () =
    let baseCommand = createEquityCreateCommand None
    let invalidCommand =
        {
            baseCommand with
                Common = { (createBaseCommonTerms ()) with DisplayName = " "; Currency = " " }
                Identifiers = [ { createPrimaryTicker baseCommand.EffectiveFrom with IsPrimary = false } ]
        }

    match SecurityMaster.create invalidCommand with
    | Error errors ->
        let codes = errors |> List.map (fun error -> error.Code)
        codes |> List.contains "display_name_required" |> should equal true
        codes |> List.contains "currency_required" |> should equal true
        codes |> List.contains "primary_identifier_invalid" |> should equal true
    | Ok events ->
        failwithf "Expected invalid create to be rejected, got: %A" events

[<Fact>]
let ``SecurityMaster.amend emits TermsAmended when classification changes`` () =
    let preferredTerms, convertibleTerms, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let nextClassification =
        EquityClassification.ConvertiblePreferred(
            { preferredTerms with DividendRate = Some 7.00m },
            { convertibleTerms with ConversionRatio = 3.00m })
    let command = createEquityAmendCommand currentRecord nextClassification None

    match SecurityMaster.amend currentRecord command with
    | Ok [ SecurityMasterEvent.TermsAmended(beforeVersion, record) ] ->
        beforeVersion |> should equal currentRecord.Version
        record.Version |> should equal (currentRecord.Version + 1L)

        match record.Kind with
        | SecurityKind.Equity terms ->
            terms.Classification |> should equal (Some nextClassification)
        | _ ->
            failwith "Expected SecurityKind.Equity"
    | Ok events ->
        failwithf "Expected TermsAmended event, got: %A" events
    | Error errors ->
        failwithf "Expected amend to succeed, got: %A" errors

[<Fact>]
let ``SecurityMaster.amend rejects stale expected versions`` () =
    let _, _, classification = createConvertiblePreferredClassification ()
    let currentRecord = createSecurityRecord (Some classification)
    let command = { createEquityAmendCommand currentRecord classification None with ExpectedVersion = 0L }

    match SecurityMaster.amend currentRecord command with
    | Error errors ->
        errors |> List.exists (fun error -> error.Code = "version_conflict") |> should equal true
    | Ok events ->
        failwithf "Expected version conflict, got: %A" events

[<Fact>]
let ``SecurityMasterCommandFacade surfaces snapshots on success and errors on validation failure`` () =
    let validResult = SecurityMasterCommandFacade.Create(createEquityCreateCommand None)
    validResult.IsSuccess |> should equal true
    obj.ReferenceEquals(validResult.Snapshot, null) |> should equal false
    validResult.Snapshot.AssetClass |> should equal "Equity"
    validResult.Snapshot.Currency |> should equal "USD"
    validResult.Errors |> should equal [||]

    let invalidBase = createEquityCreateCommand None
    let invalidCommand =
        {
            invalidBase with
                Common = { (createBaseCommonTerms ()) with DisplayName = " "; Currency = " " }
                Identifiers = [ { createPrimaryTicker invalidBase.EffectiveFrom with IsPrimary = false } ]
        }
    let invalidResult = SecurityMasterCommandFacade.Create(invalidCommand)
    invalidResult.IsSuccess |> should equal false
    obj.ReferenceEquals(invalidResult.Snapshot, null) |> should equal true
    invalidResult.ErrorDetails |> Array.exists (fun error -> error.Code = "display_name_required") |> should equal true
    invalidResult.ErrorDetails |> Array.exists (fun error -> error.Code = "primary_identifier_invalid") |> should equal true

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes current equity payload and identifiers`` () =
    let record = createSecurityRecord None
    let wrapper = SecurityMasterSnapshotWrapper(record)
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    use commonDocument = JsonDocument.Parse(wrapper.CommonTermsJson)
    let payload = assetDocument.RootElement
    let commonPayload = commonDocument.RootElement

    payload.GetProperty("schemaVersion").GetInt32() |> should equal 1
    payload.GetProperty("shareClass").GetString() |> should equal "A"
    commonPayload.GetProperty("primaryListingMic").GetString() |> should equal "XNAS"
    commonPayload.GetProperty("countryOfIncorporation").GetString() |> should equal "US"
    commonPayload.GetProperty("settlementCycleDays").GetInt32() |> should equal 2
    commonPayload.GetProperty("holidayCalendarId").GetString() |> should equal "NYSE"
    wrapper.PrimaryIdentifierKind |> should equal "Ticker"
    wrapper.PrimaryIdentifierValue |> should equal "ACPRA"
    wrapper.Currency |> should equal "USD"

[<Fact>]
let ``SecurityMasterLegacyUpgrade maps preferred classification into term modules`` () =
    let preferredTerms, _, classification = createConvertiblePreferredClassification ()
    let record = createSecurityRecord (Some classification)
    let definition = SecurityMasterLegacyUpgrade.toEconomicDefinition record

    definition.Classification.AssetClass |> should equal AssetClass.Equity
    definition.Classification.TypeName |> should equal "Equity"

    let equityBehavior = definition.Terms.EquityBehavior |> Option.defaultWith (fun () -> failwith "Expected equity behavior terms")
    equityBehavior.ShareClass |> should equal (Some "A")

    match equityBehavior.DistributionType with
    | Some (DistributionPolicy.OtherDistribution label) ->
        label |> should equal "Fixed"
    | _ ->
        failwith "Expected preferred dividend distribution type"

    let redemption = definition.Terms.Redemption |> Option.defaultWith (fun () -> failwith "Expected redemption terms")
    redemption.RedemptionPrice |> should equal preferredTerms.RedemptionPrice

    let call = definition.Terms.Call |> Option.defaultWith (fun () -> failwith "Expected call terms")
    call.IsCallable |> should equal true
    call.FirstCallDate |> should equal preferredTerms.CallableDate

[<Fact>]
let ``BondTerms factory carries BondSubclass correctly`` () =
    let maturity = DateOnly(2032, 6, 1)
    let terms = { BondTerms.fixedRate maturity 4.5m None None with Subclass = BondSubclass.Convertible }
    terms.Subclass |> should equal BondSubclass.Convertible

[<Fact>]
let ``BondSubclass Other carries string payload`` () =
    let sub = BondSubclass.Other "CovLite"
    match sub with
    | BondSubclass.Other label -> label |> should equal "CovLite"
    | _ -> failwith "Expected BondSubclass.Other"

[<Fact>]
let ``OptionTerms carries OptChainId and exercise style correctly`` () =
    let sid = SecurityId(Guid.NewGuid())
    let terms = {
        UnderlyingId = sid
        PutCall = "Call"
        Strike = 150m
        Expiry = DateOnly(2025, 12, 19)
        Multiplier = 100m
        OptChainId = Some "AAPL-20251219"
        ExerciseStyle = Some ExerciseStyle.American
        SettlementType = Some "Physical"
        IsAdjusted = false
        LastTradingDt = Some (DateOnly(2025, 12, 18))
    }

    terms.OptChainId |> should equal (Some "AAPL-20251219")
    terms.ExerciseStyle |> should equal (Some ExerciseStyle.American)
    terms.SettlementType |> should equal (Some "Physical")
    terms.LastTradingDt |> should equal (Some (DateOnly(2025, 12, 18)))

[<Fact>]
let ``MultiCalendarTerms can hold multiple calendar references`` () =
    let terms = {
        Calendars = [
            { CalendarId = "TARGET2"; CalendarPurpose = "Settlement" }
            { CalendarId = "FedWire"; CalendarPurpose = "Payment" }
        ]
    }

    terms.Calendars |> List.length |> should equal 2
    (terms.Calendars |> List.item 0).CalendarId |> should equal "TARGET2"
    (terms.Calendars |> List.item 1).CalendarPurpose |> should equal "Payment"

[<Fact>]
let ``PrepaymentModel Psa carries correct speed`` () =
    let model = PrepaymentModel.Psa 200m
    match model with
    | PrepaymentModel.Psa speed -> speed |> should equal 200m
    | _ -> failwith "unexpected case"

[<Fact>]
let ``PrepaymentModel Cpr carries annual rate`` () =
    let model = PrepaymentModel.Cpr 0.08m
    match model with
    | PrepaymentModel.Cpr rate -> rate |> should equal 0.08m
    | _ -> failwith "unexpected case"

[<Fact>]
let ``SecurityTermModules empty has StructuredProduct None`` () =
    SecurityTermModules.empty.StructuredProduct |> should equal None

[<Fact>]
let ``IO strip StructuredProductTerms sets IsInterestOnly true`` () =
    let sp = {
        Factor = None
        FactorDate = None
        WeightedAvgCoupon = None
        WeightedAvgMaturityMonths = None
        WeightedAvgLoanAgeMos = None
        CollateralType = Some "ResidentialMortgage"
        PoolIdentifier = None
        TrancheClass = Some "IO"
        PrepaymentAssumption = None
        AverageLifeYears = None
        IsInterestOnly = true
        IsPrincipalOnly = false
        NotionalBalance = Some 10_000_000m
        Originator = None
        CreditEnhancementPct = None
    }

    sp.IsInterestOnly |> should equal true
    sp.IsPrincipalOnly |> should equal false
    sp.NotionalBalance |> should equal (Some 10_000_000m)
