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
        Par = None
        PaymentFrequency = None
        LegalFinalMaturity = None
        PreRefundDate = None
        MandatoryPutDate = None
        PrincipalSchedule = []
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
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
    Provider = None
}

[<Fact>]
let ``Identifier source-provider metadata does not change canonical identity`` () =
    let now = DateTimeOffset.UtcNow
    let left = { createPrimaryTicker now with Provider = Some "XNAS" }
    let right = { createPrimaryTicker now with Provider = Some "REFINITIV" }

    SecurityIdentifier.sameIdentity left right |> should equal true

[<Fact>]
let ``ProviderSymbol snapshot rejects provider metadata that contradicts its discriminant`` () =
    let identifier = {
        createPrimaryTicker DateTimeOffset.UtcNow with
            Kind = IdentifierKind.ProviderSymbol "XNAS"
            Provider = Some "REFINITIV"
    }

    (fun () -> SecurityIdentifierSnapshot(identifier) |> ignore)
    |> should throw typeof<ArgumentException>

[<Fact>]
let ``ProviderSymbol snapshot uses the authoritative discriminant for equivalent metadata`` () =
    let identifier = {
        createPrimaryTicker DateTimeOffset.UtcNow with
            Kind = IdentifierKind.ProviderSymbol "XNAS"
            Provider = Some " xnas "
    }

    SecurityIdentifierSnapshot(identifier).Provider |> should equal "XNAS"

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

[<Fact>]
let ``SecurityMasterCommandFacade rejects contradictory ProviderSymbol metadata`` () =
    let baseCommand = createEquityCreateCommand None
    let identifier = {
        createPrimaryTicker baseCommand.EffectiveFrom with
            Kind = IdentifierKind.ProviderSymbol "XNAS"
            Provider = Some "REFINITIV"
    }
    let result = SecurityMasterCommandFacade.Create({ baseCommand with Identifiers = [ identifier ] })

    result.IsSuccess |> should equal false
    obj.ReferenceEquals(result.Snapshot, null) |> should equal true
    result.ErrorDetails
    |> Array.exists (fun error -> error.Code = "identifier_provider_mismatch")
    |> should equal true

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

let private legacySwapLeg : SwapLeg = {
    LegId = None
    LegType = "Fixed"
    Currency = "USD"
    Direction = None
    Index = None
    FixedRate = Some 0.0425m
    SpreadBps = None
    CurrentIndexRate = None
    Notional = None
    PaymentFrequency = None
    DayCount = None
    ExchangesPrincipal = false
    NotionalSchedule = []
    ResetSchedule = []
    ScheduleConventions = ScheduleConventions.empty
}

let private createSwapCommand legs =
    let equityCommand = createEquityCreateCommand None
    { equityCommand with
        Kind = SecurityKind.Swap {
            EffectiveDate = DateOnly(2026, 1, 15)
            MaturityDate = DateOnly(2031, 1, 15)
            Legs = legs
        } }

let private createSwapSnapshot legs =
    match SecurityMaster.create (createSwapCommand legs) with
    | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> SecurityMasterSnapshotWrapper(snapshot)
    | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
    | Error errors -> failwithf "Expected swap create to succeed, got: %A" errors

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes full swap leg economics`` () =
    let fixedLeg = {
        legacySwapLeg with
            LegId = Some "fixed-leg"
            Direction = Some "Pay"
            Notional = Some 25_000_000m
            PaymentFrequency = Some "SemiAnnual"
            DayCount = Some "30/360"
    }
    let floatingLeg = {
        legacySwapLeg with
            LegId = Some "floating-leg"
            LegType = "Floating"
            Currency = "EUR"
            Direction = Some "Receive"
            Index = Some "EURIBOR"
            FixedRate = None
            // A spread below the reference index is valid economics and must survive unchanged.
            SpreadBps = Some -10m
            CurrentIndexRate = Some 0.0312m
            Notional = Some 23_000_000m
            PaymentFrequency = Some "Quarterly"
            DayCount = Some "ACT/360"
            ExchangesPrincipal = true
    }
    let wrapper = createSwapSnapshot [ fixedLeg; floatingLeg ]
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement
    payload.GetProperty("effectiveDate").GetString() |> should equal "2026-01-15"
    payload.GetProperty("maturityDate").GetString() |> should equal "2031-01-15"
    let legs = payload.GetProperty("legs")
    legs.GetArrayLength() |> should equal 2

    let fixedPayload = legs.[0]
    fixedPayload.GetProperty("legId").GetString() |> should equal "fixed-leg"
    fixedPayload.GetProperty("legType").GetString() |> should equal "Fixed"
    fixedPayload.GetProperty("currency").GetString() |> should equal "USD"
    fixedPayload.GetProperty("direction").GetString() |> should equal "Pay"
    fixedPayload.GetProperty("fixedRate").GetDecimal() |> should equal 0.0425m
    fixedPayload.GetProperty("notional").GetDecimal() |> should equal 25_000_000m
    fixedPayload.GetProperty("paymentFrequency").GetString() |> should equal "SemiAnnual"
    fixedPayload.GetProperty("dayCount").GetString() |> should equal "30/360"
    fixedPayload.GetProperty("exchangesPrincipal").GetBoolean() |> should equal false

    let floatingPayload = legs.[1]
    floatingPayload.GetProperty("legId").GetString() |> should equal "floating-leg"
    floatingPayload.GetProperty("legType").GetString() |> should equal "Floating"
    floatingPayload.GetProperty("currency").GetString() |> should equal "EUR"
    floatingPayload.GetProperty("direction").GetString() |> should equal "Receive"
    floatingPayload.GetProperty("index").GetString() |> should equal "EURIBOR"
    floatingPayload.GetProperty("fixedRate").ValueKind |> should equal JsonValueKind.Null
    floatingPayload.GetProperty("spreadBps").GetDecimal() |> should equal -10m
    floatingPayload.GetProperty("currentIndexRate").GetDecimal() |> should equal 0.0312m
    floatingPayload.GetProperty("notional").GetDecimal() |> should equal 23_000_000m
    floatingPayload.GetProperty("paymentFrequency").GetString() |> should equal "Quarterly"
    floatingPayload.GetProperty("dayCount").GetString() |> should equal "ACT/360"
    floatingPayload.GetProperty("exchangesPrincipal").GetBoolean() |> should equal true

[<Fact>]
let ``SecurityMaster accepts swap legs without optional economics`` () =
    let wrapper = createSwapSnapshot [ legacySwapLeg ]
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let leg = assetDocument.RootElement.GetProperty("legs").[0]
    for field in [ "legId"; "direction"; "spreadBps"; "currentIndexRate"; "notional"; "paymentFrequency"; "dayCount" ] do
        leg.GetProperty(field).ValueKind |> should equal JsonValueKind.Null
    leg.GetProperty("fixedRate").GetDecimal() |> should equal 0.0425m
    leg.GetProperty("exchangesPrincipal").GetBoolean() |> should equal false

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes swap leg notional and reset schedules`` () =
    let leg = {
        legacySwapLeg with
            NotionalSchedule =
                [ { EffectiveDate = DateOnly(2026, 1, 15); Notional = 10_000_000m }
                  { EffectiveDate = DateOnly(2028, 1, 15); Notional = 5_000_000m } ]
            ResetSchedule =
                [ { ResetDate = DateOnly(2026, 1, 15); FixingRate = Some 0.0375m }
                  { ResetDate = DateOnly(2026, 4, 15); FixingRate = None } ]
    }
    let wrapper = createSwapSnapshot [ leg ]
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement.GetProperty("legs").[0]
    let notionalSchedule = payload.GetProperty("notionalSchedule")
    notionalSchedule.GetArrayLength() |> should equal 2
    notionalSchedule.[1].GetProperty("effectiveDate").GetString() |> should equal "2028-01-15"
    notionalSchedule.[1].GetProperty("notional").GetDecimal() |> should equal 5_000_000m
    let resetSchedule = payload.GetProperty("resetSchedule")
    resetSchedule.GetArrayLength() |> should equal 2
    resetSchedule.[0].GetProperty("resetDate").GetString() |> should equal "2026-01-15"
    resetSchedule.[0].GetProperty("fixingRate").GetDecimal() |> should equal 0.0375m
    resetSchedule.[1].GetProperty("fixingRate").ValueKind |> should equal JsonValueKind.Null

[<Fact>]
let ``SecurityMaster rejects malformed swap leg schedules`` () =
    let leg = {
        legacySwapLeg with
            NotionalSchedule =
                [ { EffectiveDate = DateOnly(2027, 1, 15); Notional = 0m }
                  { EffectiveDate = DateOnly(2026, 1, 15); Notional = 1m }
                  { EffectiveDate = DateOnly(2032, 1, 15); Notional = 1m } ]
            ResetSchedule =
                [ { ResetDate = DateOnly(2026, 4, 15); FixingRate = None }
                  { ResetDate = DateOnly(2026, 4, 15); FixingRate = None } ]
    }
    match SecurityMaster.create (createSwapCommand [ leg ]) with
    | Error errors ->
        let codes = errors |> List.map (fun error -> error.Code)
        codes |> should contain "swap_leg_notional_schedule_invalid"
        codes |> should contain "swap_leg_notional_schedule_order_invalid"
        codes |> should contain "swap_leg_notional_schedule_range_invalid"
        codes |> should contain "swap_leg_reset_schedule_order_invalid"
    | Ok _ -> failwith "Expected malformed swap leg schedules to be rejected."

[<Theory>]
[<InlineData(0)>]
[<InlineData(-1)>]
let ``SecurityMaster rejects non-positive supplied swap leg notional`` (notional: int) =
    let command = createSwapCommand [ { legacySwapLeg with Notional = Some (decimal notional) } ]
    match SecurityMaster.create command with
    | Error errors -> errors |> List.map (fun error -> error.Code) |> should contain "swap_leg_notional_invalid"
    | Ok _ -> failwith "Expected a non-positive swap leg notional to be rejected."

[<Theory>]
[<InlineData(" ")>]
[<InlineData("Unknown")>]
let ``SecurityMaster rejects unrecognized supplied swap leg direction`` (direction: string) =
    let command = createSwapCommand [ { legacySwapLeg with Direction = Some direction } ]
    match SecurityMaster.create command with
    | Error errors -> errors |> List.map (fun error -> error.Code) |> should contain "swap_leg_direction_invalid"
    | Ok _ -> failwith "Expected an unrecognized swap leg direction to be rejected."

[<Theory>]
[<InlineData("pay")>]
[<InlineData("RECEIVE")>]
let ``SecurityMaster accepts either swap leg direction case-insensitively`` (direction: string) =
    let wrapper = createSwapSnapshot [ { legacySwapLeg with Direction = Some direction } ]
    wrapper.AssetClass |> should equal "Swap"

[<Fact>]
let ``SecurityMaster rejects blank supplied swap leg payment frequency`` () =
    let command = createSwapCommand [ { legacySwapLeg with PaymentFrequency = Some " " } ]
    match SecurityMaster.create command with
    | Error errors -> errors |> List.map (fun error -> error.Code) |> should contain "swap_leg_payment_frequency_invalid"
    | Ok _ -> failwith "Expected a blank swap leg payment frequency to be rejected."

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes floating bond coupon details`` () =
    let maturity = DateOnly(2034, 9, 15)
    let bondTerms = {
        Maturity = maturity
        IssueDate = Some (DateOnly(2024, 9, 15))
        Coupon = BondCouponStructure.Floating("SOFR", Some 150m, Some 7.25m, Some 0.75m, Some "ACT/360")
        IsCallable = true
        CallDate = Some (DateOnly(2030, 9, 15))
        IssuerName = Some "Meridian Capital"
        Seniority = Some "Senior Unsecured"
        Subclass = BondSubclass.FloatingRate
        Par = None
        PaymentFrequency = None
        LegalFinalMaturity = None
        PreRefundDate = None
        MandatoryPutDate = None
        PrincipalSchedule = []
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.Bond bondTerms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let wrapper = SecurityMasterSnapshotWrapper(record)
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement

    payload.GetProperty("couponType").GetString() |> should equal "Floating"
    payload.GetProperty("floatingIndex").GetString() |> should equal "SOFR"
    payload.GetProperty("spreadBps").GetDecimal() |> should equal 150m
    payload.GetProperty("capRate").GetDecimal() |> should equal 7.25m
    payload.GetProperty("floorRate").GetDecimal() |> should equal 0.75m
    payload.GetProperty("dayCount").GetString() |> should equal "ACT/360"

let private stepBondTerms = {
    Maturity = DateOnly(2032, 6, 30)
    IssueDate = Some (DateOnly(2026, 6, 30))
    Coupon =
        BondCouponStructure.Step(
            [ { EffectiveDate = DateOnly(2026, 6, 30); Rate = 3.0m }
              { EffectiveDate = DateOnly(2028, 6, 30); Rate = 4.0m } ],
            Some "30/360")
    IsCallable = false
    CallDate = None
    IssuerName = Some "Step Issuer"
    Seniority = None
    Subclass = BondSubclass.StepRate
    Par = Some 1000m
    PaymentFrequency = None
    LegalFinalMaturity = None
    PreRefundDate = None
    MandatoryPutDate = None
    PrincipalSchedule = []
    ScheduleConventions = ScheduleConventions.empty
    CallPutSchedules = CallPutSchedules.empty
}

[<Fact>]
let ``BondTerms couponRateAsOf resolves the step rate in effect`` () =
    // A step-rate bond has no meaningful scalar rate; the dated schedule is the coupon.
    BondTerms.couponRate stepBondTerms |> should equal None
    BondTerms.couponRateAsOf (DateOnly(2027, 1, 1)) stepBondTerms |> should equal (Some 3.0m)
    BondTerms.couponRateAsOf (DateOnly(2028, 6, 30)) stepBondTerms |> should equal (Some 4.0m)
    BondTerms.couponRateAsOf (DateOnly(2026, 1, 1)) stepBondTerms |> should equal None

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes step coupon schedules`` () =
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.Bond stepBondTerms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let wrapper = SecurityMasterSnapshotWrapper(record)
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement

    payload.GetProperty("couponType").GetString() |> should equal "Step"
    let steps = payload.GetProperty("stepSchedule")
    steps.GetArrayLength() |> should equal 2
    steps.[0].GetProperty("effectiveDate").GetString() |> should equal "2026-06-30"
    steps.[0].GetProperty("rate").GetDecimal() |> should equal 3.0m
    steps.[1].GetProperty("rate").GetDecimal() |> should equal 4.0m
    payload.GetProperty("dayCount").GetString() |> should equal "30/360"

[<Fact>]
let ``SecurityMaster.create rejects an empty step-coupon schedule`` () =
    let emptyStep = { stepBondTerms with Coupon = BondCouponStructure.Step([], Some "30/360") }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.Bond emptyStep }

    match SecurityMaster.create command with
    | Error errors ->
        errors |> List.map (fun error -> error.Code) |> should contain "bond_step_schedule_required"
    | Ok _ -> failwith "Expected create to reject an empty step schedule"

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes inflation-linked coupon terms`` () =
    let linkerTerms = {
        stepBondTerms with
            Coupon = BondCouponStructure.InflationLinked(1.25m, "CPI-U", Some 305.109m, Some 1.0432m, Some "ACT/ACT")
            Subclass = BondSubclass.InflationLinked
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.Bond linkerTerms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let wrapper = SecurityMasterSnapshotWrapper(record)
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement

    payload.GetProperty("couponType").GetString() |> should equal "InflationLinked"
    payload.GetProperty("couponRate").GetDecimal() |> should equal 1.25m
    payload.GetProperty("inflationIndex").GetString() |> should equal "CPI-U"
    payload.GetProperty("inflationBaseIndexValue").GetDecimal() |> should equal 305.109m
    payload.GetProperty("inflationIndexRatio").GetDecimal() |> should equal 1.0432m

[<Fact>]
let ``SecurityMaster.create rejects a non-positive inflation index ratio`` () =
    let badLinker = {
        stepBondTerms with
            Coupon = BondCouponStructure.InflationLinked(1.25m, "CPI-U", Some 305.109m, Some 0m, None)
            Subclass = BondSubclass.InflationLinked
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.Bond badLinker }

    match SecurityMaster.create command with
    | Error errors ->
        errors |> List.map (fun error -> error.Code) |> should contain "bond_inflation_index_values_invalid"
    | Ok _ -> failwith "Expected create to reject a zero index ratio"

[<Fact>]
let ``SecurityMasterSnapshotWrapper serializes structured credit terms`` () =
    let terms = {
        Tranche = "A-1"
        PoolId = Some "POOL-2026-1"
        CollateralType = "CLO"
        OriginalFace = 1_000_000m
        CurrentFactor = Some 0.9825m
        CouponOrIndex = "SOFR+250"
        FactorSchedule = Some "monthly-trustee"
        FactorScheduleEntries = [ { AsOfDate = DateOnly(2026, 7, 1); Factor = 0.9825m } ]
        Maturity = Some (DateOnly(2031, 6, 15))
        Kind = Some StructuredCreditKind.Clo
        Seniority = Some "Mezzanine"
        AttachmentPoint = Some 0.12m
        DetachmentPoint = Some 0.20m
        WeightedAverageCoupon = Some 7.85m
        WeightedAverageMaturityMonths = Some 58
        WeightedAverageLoanAgeMonths = Some 14
        ReinvestmentPeriodEnd = Some (DateOnly(2028, 6, 15))
        NonCallPeriodEnd = Some (DateOnly(2027, 6, 15))
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.StructuredCredit terms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let wrapper = SecurityMasterSnapshotWrapper(record)
    use assetDocument = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    let payload = assetDocument.RootElement

    wrapper.AssetClass |> should equal "StructuredCredit"
    payload.GetProperty("tranche").GetString() |> should equal "A-1"
    payload.GetProperty("poolId").GetString() |> should equal "POOL-2026-1"
    payload.GetProperty("collateralType").GetString() |> should equal "CLO"
    payload.GetProperty("originalFace").GetDecimal() |> should equal 1_000_000m
    payload.GetProperty("currentFactor").GetDecimal() |> should equal 0.9825m
    payload.GetProperty("maturity").GetString() |> should equal "2031-06-15"
    // Typed tranche terms (asset-class terms version 2) travel as flat keys beside the version-1 terms.
    payload.GetProperty("structuredCreditKind").GetString() |> should equal "Clo"
    payload.GetProperty("seniority").GetString() |> should equal "Mezzanine"
    payload.GetProperty("attachmentPoint").GetDecimal() |> should equal 0.12m
    payload.GetProperty("detachmentPoint").GetDecimal() |> should equal 0.20m
    payload.GetProperty("weightedAverageCoupon").GetDecimal() |> should equal 7.85m
    payload.GetProperty("weightedAverageMaturityMonths").GetInt32() |> should equal 58
    payload.GetProperty("weightedAverageLoanAgeMonths").GetInt32() |> should equal 14
    payload.GetProperty("reinvestmentPeriodEnd").GetString() |> should equal "2028-06-15"
    payload.GetProperty("nonCallPeriodEnd").GetString() |> should equal "2027-06-15"

[<Fact>]
let ``StructuredCreditKind labels round-trip and keep an unknown label verbatim`` () =
    for kind in [ StructuredCreditKind.AgencyMbs; StructuredCreditKind.NonAgencyMbs; StructuredCreditKind.CommercialMbs
                  StructuredCreditKind.AssetBacked; StructuredCreditKind.Clo; StructuredCreditKind.Cdo; StructuredCreditKind.Cmo
                  StructuredCreditKind.InterestOnly; StructuredCreditKind.PrincipalOnly ] do
        kind |> StructuredCreditKind.asString |> StructuredCreditKind.parse |> should equal kind

    StructuredCreditKind.parse " Clo " |> should equal StructuredCreditKind.Clo
    // Governed labels match case-insensitively (the taxonomy validator accepts them that way) and
    // canonicalise, so a provider's "interestonly" still drives the typed IO semantics.
    StructuredCreditKind.parse "interestonly" |> should equal StructuredCreditKind.InterestOnly
    StructuredCreditKind.parse "AGENCYMBS" |> should equal StructuredCreditKind.AgencyMbs
    StructuredCreditKind.parse "SyntheticWidget" |> should equal (StructuredCreditKind.OtherStructuredCreditKind "SyntheticWidget")
    StructuredCreditKind.OtherStructuredCreditKind "SyntheticWidget" |> StructuredCreditKind.asString |> should equal "SyntheticWidget"

[<Fact>]
let ``SecurityMasterLegacyUpgrade projects StructuredCredit maturity into the Maturity module`` () =
    // The persisted legal final maturity must reach the shared economic-terms document, not only
    // the retained asset-specific terms: consumers of EconomicTerms (the accounting adapter among
    // them) read maturity only from the Maturity module.
    let terms = {
        Tranche = "A-1"
        PoolId = Some "POOL-2026-1"
        CollateralType = "CLO"
        OriginalFace = 1_000_000m
        CurrentFactor = Some 0.9825m
        CouponOrIndex = "SOFR+250"
        FactorSchedule = Some "monthly-trustee"
        FactorScheduleEntries = []
        Maturity = Some (DateOnly(2031, 6, 15))
        Kind = Some StructuredCreditKind.InterestOnly
        Seniority = None
        AttachmentPoint = None
        DetachmentPoint = None
        WeightedAverageCoupon = Some 6.5m
        WeightedAverageMaturityMonths = Some 240
        WeightedAverageLoanAgeMonths = Some 36
        ReinvestmentPeriodEnd = None
        NonCallPeriodEnd = None
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.StructuredCredit terms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let definition = SecurityMasterLegacyUpgrade.toEconomicDefinition record
    let maturityModule =
        definition.Terms.Maturity |> Option.defaultWith (fun () -> failwith "Expected Maturity term module")
    maturityModule.MaturityDate |> should equal (Some (DateOnly(2031, 6, 15)))

    // The typed pool statistics reach the StructuredProduct module, and the typed kind decides the
    // strip flags instead of the tranche-label heuristic.
    let structuredProduct =
        definition.Terms.StructuredProduct |> Option.defaultWith (fun () -> failwith "Expected StructuredProduct term module")
    structuredProduct.WeightedAvgCoupon |> should equal (Some 6.5m)
    structuredProduct.WeightedAvgMaturityMonths |> should equal (Some 240)
    structuredProduct.WeightedAvgLoanAgeMos |> should equal (Some 36)
    structuredProduct.IsInterestOnly |> should equal true
    structuredProduct.IsPrincipalOnly |> should equal false

    // The free-text factorSchedule is a trustee-report pointer: it rides along as evidence lineage
    // on the StructuredProduct module and no longer poses as a payment or reset frequency.
    structuredProduct.FactorScheduleReference |> should equal (Some "monthly-trustee")
    (definition.Terms.Coupon |> Option.defaultWith (fun () -> failwith "Expected Coupon term module")).PaymentFrequency
    |> should equal None
    (definition.Terms.FloatingRate |> Option.defaultWith (fun () -> failwith "Expected FloatingRate term module")).ResetFrequency
    |> should equal None

[<Fact>]
let ``SecurityMasterLegacyUpgrade keeps a structured note cadence without a coupon rate`` () =
    let terms = {
        IssuerName = "Meridian Bank plc"; UnderlyingDescription = "EURO STOXX 50"; Maturity = DateOnly(2031, 12, 15)
        IssueDate = None; Denomination = Some 1000m; PrincipalProtectionPct = Some 1m; ParticipationRate = None
        ReturnCap = None; CouponRate = None; PaymentFrequency = Some "Quarterly"; BarrierLevel = Some 0.6m
    }
    let equityCommand = createEquityCreateCommand None
    let command = { equityCommand with Kind = SecurityKind.StructuredNote terms }

    let record =
        match SecurityMaster.create command with
        | Ok [ SecurityMasterEvent.SecurityCreated snapshot ] -> snapshot
        | Ok events -> failwithf "Expected SecurityCreated event, got: %A" events
        | Error errors -> failwithf "Expected create to succeed, got: %A" errors

    let definition = SecurityMasterLegacyUpgrade.toEconomicDefinition record
    let coupon = definition.Terms.Coupon |> Option.defaultWith (fun () -> failwith "Expected Coupon term module")
    coupon.PaymentFrequency |> should equal (Some (PaymentFrequency.parse "Quarterly"))
    coupon.CouponRate |> should equal None

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
        FactorScheduleReference = None
        FactorScheduleEntries = []
    }

    sp.IsInterestOnly |> should equal true
    sp.IsPrincipalOnly |> should equal false
    sp.NotionalBalance |> should equal (Some 10_000_000m)

// ---------------------------------------------------------------------------
// SecurityClassification helper functions
// ---------------------------------------------------------------------------

[<Fact>]
let ``SecurityClassification.assetClassName returns canonical string for each AssetClass`` () =
    let cases =
        [
            AssetClass.Equity,       "Equity"
            AssetClass.FixedIncome,  "FixedIncome"
            AssetClass.Fund,         "Fund"
            AssetClass.CashEquivalent, "CashEquivalent"
            AssetClass.Financing,    "Financing"
            AssetClass.Derivative,   "Derivative"
            AssetClass.PrivateCredit,"PrivateCredit"
            AssetClass.Other,        "Other"
        ]

    for (ac, expected) in cases do
        SecurityClassification.assetClassName ac |> should equal expected

[<Fact>]
let ``SecurityClassification.familyName returns canonical string for each AssetFamily`` () =
    let cases =
        [
            AssetFamily.Sovereign,           "Sovereign"
            AssetFamily.CorporateDebt,       "CorporateDebt"
            AssetFamily.BankProduct,         "BankProduct"
            AssetFamily.CommonEquity,        "CommonEquity"
            AssetFamily.MoneyMarket,         "MoneyMarket"
            AssetFamily.RepurchaseAgreement, "RepurchaseAgreement"
            AssetFamily.ListedDerivative,    "ListedDerivative"
            AssetFamily.PrivateLoan,         "PrivateLoan"
            AssetFamily.OtherFamily "Crypto","Crypto"
        ]

    for (fam, expected) in cases do
        SecurityClassification.familyName fam |> should equal expected

[<Fact>]
let ``SecurityClassification.subTypeName returns canonical string for common sub-types`` () =
    let cases =
        [
            SecuritySubType.TreasuryBill,    "TreasuryBill"
            SecuritySubType.CorporateBond,   "CorporateBond"
            SecuritySubType.CommonShare,     "CommonShare"
            SecuritySubType.OptionContract,  "OptionContract"
            SecuritySubType.FutureContract,  "FutureContract"
            SecuritySubType.Repo,            "Repo"
            SecuritySubType.MoneyMarketFund, "MoneyMarketFund"
            SecuritySubType.StructuredCredit, "StructuredCredit"
            SecuritySubType.PrivateCompanyEquity, "PrivateCompanyEquity"
            SecuritySubType.RealEstateHolding, "RealEstateHolding"
            SecuritySubType.CommitmentGuarantee, "CommitmentGuarantee"
            SecuritySubType.OtherSubType "DigitalAsset", "DigitalAsset"
        ]

    for (st, expected) in cases do
        SecurityClassification.subTypeName st |> should equal expected

// ---------------------------------------------------------------------------
// SectorTaxonomy construction
// ---------------------------------------------------------------------------

[<Fact>]
let ``SectorTaxonomy can be constructed with all optional fields absent`` () =
    let taxonomy =
        {
            TaxonomyScheme     = "GICS"
            SectorCode         = None
            IndustryGroupCode  = None
            IndustryCode       = None
            SubIndustryCode    = None
        }

    taxonomy.TaxonomyScheme     |> should equal "GICS"
    taxonomy.SectorCode         |> should equal None
    taxonomy.IndustryGroupCode  |> should equal None
    taxonomy.IndustryCode       |> should equal None
    taxonomy.SubIndustryCode    |> should equal None

[<Fact>]
let ``SectorTaxonomy can be constructed with all optional fields populated`` () =
    let taxonomy =
        {
            TaxonomyScheme     = "GICS"
            SectorCode         = Some "45"
            IndustryGroupCode  = Some "4510"
            IndustryCode       = Some "451020"
            SubIndustryCode    = Some "45102010"
        }

    taxonomy.SectorCode        |> should equal (Some "45")
    taxonomy.IndustryGroupCode |> should equal (Some "4510")
    taxonomy.SubIndustryCode   |> should equal (Some "45102010")

[<Fact>]
let ``SectorTaxonomy supports non-GICS schemes`` () =
    let taxonomy =
        {
            TaxonomyScheme    = "SIC"
            SectorCode        = Some "7372"
            IndustryGroupCode = None
            IndustryCode      = None
            SubIndustryCode   = None
        }

    taxonomy.TaxonomyScheme |> should equal "SIC"
    taxonomy.SectorCode     |> should equal (Some "7372")

// ---------------------------------------------------------------------------
// SecurityEconomicDefinition.normalize — trims and uppercases core fields
// ---------------------------------------------------------------------------

let private makeTestProvenance () : Provenance =
    {
        SourceSystem   = " bloomberg "
        SourceRecordId = Some "BBG001S5N8V8"
        AsOf           = DateTimeOffset.UtcNow
        UpdatedBy      = " data-feed "
        Reason         = None
    }

let private makeTestCommonTerms () : CommonTerms =
    {
        DisplayName         = "  Apple Inc.  "
        Currency            = " usd "
        CountryOfRisk       = Some "US"
        IssuerName          = Some "Apple Inc."
        Exchange            = None
        LotSize             = None
        TickSize            = None
        PrimaryListingMic   = Some " xnas "
        CountryOfIncorporation = None
        SettlementCycleDays = Some 1
        HolidayCalendarId   = None
    }

let private makeTestClassification () : SecurityClassification =
    {
        AssetClass  = AssetClass.Equity
        Family      = Some AssetFamily.CommonEquity
        SubType     = SecuritySubType.CommonShare
        TypeName    = "CommonShare"
        IssuerType  = Some "Corporate"
        RiskCountry = Some "US"
        Taxonomy    = None
    }

[<Fact>]
let ``SecurityEconomicDefinition.normalize trims DisplayName and uppercases Currency`` () =
    let definition : SecurityEconomicDefinition =
        {
            SecurityId     = SecurityId (System.Guid.NewGuid())
            Classification = makeTestClassification ()
            Common         = makeTestCommonTerms ()
            Terms          = SecurityTermModules.empty
            Identifiers    = []
            Status         = SecurityStatus.Active
            Version        = 1L
            EffectiveFrom  = DateTimeOffset.UtcNow.AddDays(-30.0)
            EffectiveTo    = None
            Provenance     = makeTestProvenance ()
        }

    let normalized = SecurityEconomicDefinition.normalize definition

    normalized.Common.DisplayName |> should equal "Apple Inc."
    normalized.Common.Currency    |> should equal "USD"

[<Fact>]
let ``SecurityEconomicDefinition.normalize uppercases PrimaryListingMic`` () =
    let definition : SecurityEconomicDefinition =
        {
            SecurityId     = SecurityId (System.Guid.NewGuid())
            Classification = makeTestClassification ()
            Common         = makeTestCommonTerms ()
            Terms          = SecurityTermModules.empty
            Identifiers    = []
            Status         = SecurityStatus.Active
            Version        = 1L
            EffectiveFrom  = DateTimeOffset.UtcNow.AddDays(-30.0)
            EffectiveTo    = None
            Provenance     = makeTestProvenance ()
        }

    let normalized = SecurityEconomicDefinition.normalize definition

    normalized.Common.PrimaryListingMic |> should equal (Some "XNAS")

[<Fact>]
let ``SecurityEconomicDefinition.normalize trims Provenance SourceSystem and UpdatedBy`` () =
    let definition : SecurityEconomicDefinition =
        {
            SecurityId     = SecurityId (System.Guid.NewGuid())
            Classification = makeTestClassification ()
            Common         = makeTestCommonTerms ()
            Terms          = SecurityTermModules.empty
            Identifiers    = []
            Status         = SecurityStatus.Active
            Version        = 1L
            EffectiveFrom  = DateTimeOffset.UtcNow.AddDays(-30.0)
            EffectiveTo    = None
            Provenance     = makeTestProvenance ()
        }

    let normalized = SecurityEconomicDefinition.normalize definition

    normalized.Provenance.SourceSystem |> should equal "bloomberg"
    normalized.Provenance.UpdatedBy    |> should equal "data-feed"

[<Fact>]
let ``SecurityEconomicDefinition.normalize preserves unchanged optional fields`` () =
    let definition : SecurityEconomicDefinition =
        {
            SecurityId     = SecurityId (System.Guid.NewGuid())
            Classification = makeTestClassification ()
            Common         = makeTestCommonTerms ()
            Terms          = SecurityTermModules.empty
            Identifiers    = []
            Status         = SecurityStatus.Active
            Version        = 1L
            EffectiveFrom  = DateTimeOffset.UtcNow.AddDays(-30.0)
            EffectiveTo    = None
            Provenance     = makeTestProvenance ()
        }

    let normalized = SecurityEconomicDefinition.normalize definition

    normalized.Common.CountryOfRisk    |> should equal (Some "US")
    normalized.Common.SettlementCycleDays |> should equal (Some 1)
    normalized.Classification          |> should equal (makeTestClassification ())

// ---------------------------------------------------------------------------
// EquityClassification module function tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``EquityClassification.asString returns "Common" for Common case`` () =
    EquityClassification.asString EquityClassification.Common |> should equal "Common"

[<Fact>]
let ``EquityClassification.asString returns "Preferred" for Preferred case`` () =
    let terms = {
        DividendRate = Some 5.0m
        DividendType = DividendType.Fixed
        RedemptionPrice = Some 25.0m
        RedemptionDate = None
        CallableDate = None
        ParticipationTerms = None
        LiquidationPreference = LiquidationPreference.Pari
    }
    EquityClassification.asString (EquityClassification.Preferred terms) |> should equal "Preferred"

[<Fact>]
let ``EquityClassification.asString returns "Convertible" for Convertible case`` () =
    let terms = {
        UnderlyingSecurityId = SecurityId(Guid.NewGuid())
        ConversionRatio = 2.0m
        ConversionPrice = Some 50.0m
        ConversionStartDate = None
        ConversionEndDate = None
    }
    EquityClassification.asString (EquityClassification.Convertible terms) |> should equal "Convertible"

[<Fact>]
let ``EquityClassification.asString returns "ConvertiblePreferred" for ConvertiblePreferred case`` () =
    let preferred = {
        DividendRate = Some 6.0m
        DividendType = DividendType.Cumulative
        RedemptionPrice = None
        RedemptionDate = None
        CallableDate = None
        ParticipationTerms = None
        LiquidationPreference = LiquidationPreference.Senior 1.0m
    }
    let convertible = {
        UnderlyingSecurityId = SecurityId(Guid.NewGuid())
        ConversionRatio = 3.0m
        ConversionPrice = None
        ConversionStartDate = None
        ConversionEndDate = None
    }
    EquityClassification.asString (EquityClassification.ConvertiblePreferred(preferred, convertible))
    |> should equal "ConvertiblePreferred"

[<Fact>]
let ``EquityClassification.asString returns the label for Other case`` () =
    EquityClassification.asString (EquityClassification.Other "SpecialPurpose")
    |> should equal "SpecialPurpose"

// ---------------------------------------------------------------------------
// DividendType module function tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``DividendType.asString returns "Fixed" for Fixed`` () =
    DividendType.asString DividendType.Fixed |> should equal "Fixed"

[<Fact>]
let ``DividendType.asString returns "Floating" for Floating`` () =
    DividendType.asString DividendType.Floating |> should equal "Floating"

[<Fact>]
let ``DividendType.asString returns "Cumulative" for Cumulative`` () =
    DividendType.asString DividendType.Cumulative |> should equal "Cumulative"

// ---------------------------------------------------------------------------
// LiquidationPreference module function tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``LiquidationPreference.asString returns "Pari" for Pari`` () =
    LiquidationPreference.asString LiquidationPreference.Pari |> should equal "Pari"

[<Fact>]
let ``LiquidationPreference.asString returns "Senior" for Senior with any multiple`` () =
    LiquidationPreference.asString (LiquidationPreference.Senior 1.5m) |> should equal "Senior"

[<Fact>]
let ``LiquidationPreference.asString returns "Subordinated" for Subordinated`` () =
    LiquidationPreference.asString LiquidationPreference.Subordinated |> should equal "Subordinated"

// ---------------------------------------------------------------------------
// PreferredTerms — DividendType variant coverage
// ---------------------------------------------------------------------------

[<Fact>]
let ``PreferredTerms accepts Floating dividend type`` () =
    let terms = {
        DividendRate = Some 3.5m
        DividendType = DividendType.Floating
        RedemptionPrice = Some 100.0m
        RedemptionDate = Some (DateOnly(2035, 6, 1))
        CallableDate = None
        ParticipationTerms = None
        LiquidationPreference = LiquidationPreference.Pari
    }
    DividendType.asString terms.DividendType |> should equal "Floating"
    terms.DividendRate |> should equal (Some 3.5m)

[<Fact>]
let ``PreferredTerms accepts Cumulative dividend type with participation terms`` () =
    let participation = {
        ParticipatesInCommonDividends = true
        AdditionalDividendThreshold = Some 1.00m
    }
    let terms = {
        DividendRate = Some 4.0m
        DividendType = DividendType.Cumulative
        RedemptionPrice = None
        RedemptionDate = None
        CallableDate = Some (DateOnly(2030, 1, 1))
        ParticipationTerms = Some participation
        LiquidationPreference = LiquidationPreference.Senior 2.0m
    }
    DividendType.asString terms.DividendType |> should equal "Cumulative"
    terms.ParticipationTerms |> should not' (equal None)
    LiquidationPreference.asString terms.LiquidationPreference |> should equal "Senior"

// ---------------------------------------------------------------------------
// ConvertibleTerms — standalone Convertible classification
// ---------------------------------------------------------------------------

[<Fact>]
let ``EquityClassification.Convertible round-trips through asString`` () =
    let underlying = SecurityId(Guid.NewGuid())
    let terms = {
        UnderlyingSecurityId = underlying
        ConversionRatio = 4.25m
        ConversionPrice = Some 120.0m
        ConversionStartDate = Some (DateOnly(2027, 1, 1))
        ConversionEndDate = Some (DateOnly(2032, 12, 31))
    }
    let classification = EquityClassification.Convertible terms
    EquityClassification.asString classification |> should equal "Convertible"

    match classification with
    | EquityClassification.Convertible ct ->
        ct.UnderlyingSecurityId |> should equal underlying
        ct.ConversionRatio |> should equal 4.25m
        ct.ConversionPrice |> should equal (Some 120.0m)
    | _ -> failwith "Expected Convertible case"

// ---------------------------------------------------------------------------
// AssetClassRegistry — single source of truth for asset-class dispatch
// ---------------------------------------------------------------------------

/// Minimal, representative SecurityKind values covering all five derivative
/// kinds plus a spread of non-derivative kinds and the two kinds whose sub-type
/// is refined from instrument data (Deposit, OtherSecurity).
let private registrySampleKinds : (SecurityKind * string) list =
    let secId () = SecurityId(Guid.NewGuid())
    [ SecurityKind.Equity { ShareClass = None; VotingRightsCat = None; Classification = None }, "Equity"
      SecurityKind.Option {
          UnderlyingId = secId (); PutCall = "Call"; Strike = 100m; Expiry = DateOnly(2030, 1, 18)
          Multiplier = 100m; OptChainId = None; ExerciseStyle = None; SettlementType = None
          IsAdjusted = false; LastTradingDt = None }, "Option"
      SecurityKind.Future {
          RootSymbol = "ES"; ContractMonth = "2030-03"; Expiry = DateOnly(2030, 3, 20); Multiplier = 50m
          LastTradingDt = None; FirstNoticeDt = None; DeliveryMonthDt = None; SettlementType = None
          DeliveryLocationCode = None; IsRollTarget = false; RollWindowDays = None }, "Future"
      SecurityKind.Bond (BondTerms.fixedRate (DateOnly(2032, 6, 1)) 4.5m None None), "Bond"
      SecurityKind.TreasuryBill {
          Maturity = DateOnly(2026, 6, 1); AuctionDate = None; CUSIP = None; DiscountRate = None }, "TreasuryBill"
      SecurityKind.Swap { EffectiveDate = DateOnly(2026, 1, 1); MaturityDate = DateOnly(2031, 1, 1); Legs = [] }, "Swap"
      SecurityKind.Cfd { UnderlyingAssetClass = "Equity"; UnderlyingDescription = None; Leverage = None }, "Cfd"
      SecurityKind.Warrant {
          UnderlyingId = secId (); WarrantType = "Call"; Strike = None; Expiry = None; Multiplier = None }, "Warrant"
      SecurityKind.FxForward {
          BaseCurrency = "EUR"; QuoteCurrency = "USD"; Notional = None; ForwardRate = None; TradeDate = None
          ValueDate = DateOnly(2026, 9, 2); SettlementType = None }, "FxForward"
      SecurityKind.NonDeliverableForward {
          BaseCurrency = "USD"; QuoteCurrency = "BRL"; SettlementCurrency = "USD"; Notional = None; ForwardRate = None
          FixingDate = DateOnly(2026, 6, 29); ValueDate = DateOnly(2026, 7, 1); FixingSource = None }, "NonDeliverableForward"
      SecurityKind.CreditDefaultSwap {
          ReferenceEntity = "Meridian Industries Inc"; ReferenceObligation = None; ProtectionSide = "Buy"; Notional = 10_000_000m
          FixedCouponBps = 100m; EffectiveDate = DateOnly(2026, 3, 20); MaturityDate = DateOnly(2031, 6, 20)
          PaymentFrequency = None; DayCount = None; Seniority = None; RestructuringClause = None; UpfrontPoints = None
          RecoveryRateAssumption = None; IndexName = None }, "CreditDefaultSwap"
      SecurityKind.Swaption {
          PayerReceiver = "Payer"; ExerciseStyle = None; StrikeRate = 0.0375m; Expiry = DateOnly(2027, 3, 15)
          UnderlyingEffectiveDate = None; UnderlyingMaturityDate = DateOnly(2037, 3, 17); Notional = 25_000_000m
          FloatingIndex = None; FixedLegPaymentFrequency = None; FixedLegDayCount = None; SettlementType = None }, "Swaption"
      SecurityKind.CapFloor {
          CapFloorType = "Cap"; ReferenceIndex = "SOFR"; CapStrike = Some 0.05m; FloorStrike = None; Notional = 50_000_000m
          EffectiveDate = DateOnly(2026, 4, 1); MaturityDate = DateOnly(2031, 4, 1); ResetFrequency = None; DayCount = None
          PremiumAmount = None }, "CapFloor"
      SecurityKind.StructuredNote {
          IssuerName = "Meridian Bank plc"; UnderlyingDescription = "S&P 500 Index"; Maturity = DateOnly(2031, 12, 15)
          IssueDate = None; Denomination = None; PrincipalProtectionPct = None; ParticipationRate = None; ReturnCap = None
          CouponRate = None; PaymentFrequency = None; BarrierLevel = None }, "StructuredNote"
      SecurityKind.InvestmentFund {
          FundType = None; FundFamily = None; NavCurrency = None; DistributionPolicy = None
          IsStableNav = None; PricingSource = None }, "InvestmentFund"
      SecurityKind.CustomAsset {
          CustomProfileId = "structured-credit-io-po"; ProfileVersion = 3
          TermsJson = """{"schemaVersion":3,"customProfileId":"structured-credit-io-po","profileVersion":3,"profileFields":{}}""" }, "CustomAsset" ]

let private validationErrorCodes kind =
    let command = { createEquityCreateCommand None with Kind = kind }
    match SecurityMaster.create command with
    | Ok _ -> []
    | Error errors -> errors |> List.map (fun validationError -> validationError.Code)

[<Fact>]
let ``Bond principal schedule dates outside the bond term are rejected`` () =
    let maturity = DateOnly(2030, 6, 15)
    let baseTerms =
        { BondTerms.fixedRate maturity 4.5m (Some "30/360") (Some "ACME") with
            IssueDate = Some (DateOnly(2024, 6, 15)) }

    validationErrorCodes (SecurityKind.Bond { baseTerms with PrincipalSchedule = [ { PaymentDate = DateOnly(2031, 1, 1); Amount = 10m } ] })
    |> should contain "bond_principal_schedule_date_invalid"

    validationErrorCodes (SecurityKind.Bond { baseTerms with PrincipalSchedule = [ { PaymentDate = DateOnly(2020, 1, 1); Amount = 10m } ] })
    |> should contain "bond_principal_schedule_date_invalid"

    validationErrorCodes (SecurityKind.Bond { baseTerms with PrincipalSchedule = [ { PaymentDate = DateOnly(2028, 6, 15); Amount = 10m } ] })
    |> should not' (contain "bond_principal_schedule_date_invalid")

[<Fact>]
let ``Bond principal schedules summing to more than Par are rejected`` () =
    let maturity = DateOnly(2030, 6, 15)
    let baseTerms =
        { BondTerms.fixedRate maturity 4.5m (Some "30/360") (Some "ACME") with
            Par = Some 100m }

    // Two 60s against a 100-par bond: projections would cap the second at 40, silently
    // disagreeing with the persisted schedule.
    validationErrorCodes (SecurityKind.Bond { baseTerms with
                                                PrincipalSchedule =
                                                    [ { PaymentDate = DateOnly(2028, 6, 15); Amount = 60m }
                                                      { PaymentDate = DateOnly(2029, 6, 15); Amount = 60m } ] })
    |> should contain "bond_principal_schedule_exceeds_par"

    validationErrorCodes (SecurityKind.Bond { baseTerms with
                                                PrincipalSchedule =
                                                    [ { PaymentDate = DateOnly(2028, 6, 15); Amount = 60m }
                                                      { PaymentDate = DateOnly(2029, 6, 15); Amount = 40m } ] })
    |> should not' (contain "bond_principal_schedule_exceeds_par")

    // Without Par there is no face to validate or project against: downstream substitutes a
    // 100-unit basis and caps instalments, so a schedule without Par is rejected outright.
    validationErrorCodes (SecurityKind.Bond { baseTerms with
                                                Par = None
                                                PrincipalSchedule = [ { PaymentDate = DateOnly(2028, 6, 15); Amount = 500m } ] })
    |> should contain "bond_principal_schedule_requires_par"

    validationErrorCodes (SecurityKind.Bond { baseTerms with
                                                PrincipalSchedule = [ { PaymentDate = DateOnly(2028, 6, 15); Amount = 40m } ] })
    |> should not' (contain "bond_principal_schedule_requires_par")

[<Fact>]
let ``Structured credit factors above one are rejected`` () =
    let baseTerms = {
        Tranche = "A-1"
        PoolId = None
        CollateralType = "CLO"
        OriginalFace = 1_000_000m
        CurrentFactor = Some 0.9m
        CouponOrIndex = "SOFR+250"
        FactorSchedule = None
        FactorScheduleEntries = []
        Maturity = None
        Kind = None
        Seniority = None
        AttachmentPoint = None
        DetachmentPoint = None
        WeightedAverageCoupon = None
        WeightedAverageMaturityMonths = None
        WeightedAverageLoanAgeMonths = None
        ReinvestmentPeriodEnd = None
        NonCallPeriodEnd = None
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with CurrentFactor = Some 1.5m })
    |> should contain "structured_credit_current_factor_invalid"

    // Typed tranche terms: the attachment and detachment points are fractions of the deal balance,
    // ordered attachment <= detachment; the pool statistics cannot be negative; the CLO period
    // dates cannot run past the legal final maturity.
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with AttachmentPoint = Some 12m })
    |> should contain "structured_credit_attachment_point_invalid"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with DetachmentPoint = Some -0.1m })
    |> should contain "structured_credit_detachment_point_invalid"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with AttachmentPoint = Some 0.3m; DetachmentPoint = Some 0.2m })
    |> should contain "structured_credit_attachment_above_detachment"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with AttachmentPoint = Some 0.2m; DetachmentPoint = Some 0.2m })
    |> should not' (contain "structured_credit_attachment_above_detachment")
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with WeightedAverageCoupon = Some -1m })
    |> should contain "structured_credit_weighted_average_coupon_invalid"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with WeightedAverageMaturityMonths = Some -1 })
    |> should contain "structured_credit_weighted_average_maturity_invalid"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with WeightedAverageLoanAgeMonths = Some -1 })
    |> should contain "structured_credit_weighted_average_loan_age_invalid"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with Maturity = Some (DateOnly(2031, 6, 15)); ReinvestmentPeriodEnd = Some (DateOnly(2032, 1, 1)) })
    |> should contain "structured_credit_period_after_maturity"
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with Maturity = Some (DateOnly(2031, 6, 15)); NonCallPeriodEnd = Some (DateOnly(2027, 6, 15)) })
    |> should not' (contain "structured_credit_period_after_maturity")

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with FactorScheduleEntries = [ { AsOfDate = DateOnly(2026, 7, 1); Factor = 1.2m } ] })
    |> should contain "structured_credit_factor_schedule_invalid"

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with FactorScheduleEntries = [ { AsOfDate = DateOnly(2026, 7, 1); Factor = 1.0m } ] })
    |> should not' (contain "structured_credit_factor_schedule_invalid")

    // A rising factor would GROW outstanding principal — invalid retained evidence for the
    // factor-paydown workflow, however valid each individual factor is.
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 1, 1); Factor = 0.8m }
                                                                  { AsOfDate = DateOnly(2026, 2, 1); Factor = 0.9m } ] })
    |> should contain "structured_credit_factor_schedule_not_monotonic"

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 2, 1); Factor = 0.7m }
                                                                  { AsOfDate = DateOnly(2026, 1, 1); Factor = 0.8m } ] })
    |> should not' (contain "structured_credit_factor_schedule_not_monotonic")

    // Two factors on the SAME date make the effective outstanding principal depend on input
    // ordering — readers keep only one of them — so duplicates are rejected even when each
    // individual factor is valid and the pair happens to be non-increasing.
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 1, 1); Factor = 0.8m }
                                                                  { AsOfDate = DateOnly(2026, 1, 1); Factor = 0.7m } ] })
    |> should contain "structured_credit_factor_schedule_duplicate_date"

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 1, 1); Factor = 0.8m }
                                                                  { AsOfDate = DateOnly(2026, 2, 1); Factor = 0.7m } ] })
    |> should not' (contain "structured_credit_factor_schedule_duplicate_date")

    // A factor decline dated AFTER the legal final maturity would emit principal paydowns for a
    // tranche that has already reached term; observations must fall on or before the retained
    // maturity, and a record with no retained maturity keeps the unconstrained behavior.
    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            Maturity = Some (DateOnly(2026, 6, 15))
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 6, 1); Factor = 0.8m }
                                                                  { AsOfDate = DateOnly(2026, 7, 1); Factor = 0.7m } ] })
    |> should contain "structured_credit_factor_after_maturity"

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            Maturity = Some (DateOnly(2026, 6, 15))
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2026, 6, 1); Factor = 0.8m }
                                                                  { AsOfDate = DateOnly(2026, 6, 15); Factor = 0.7m } ] })
    |> should not' (contain "structured_credit_factor_after_maturity")

    validationErrorCodes (SecurityKind.StructuredCredit { baseTerms with
                                                            FactorScheduleEntries =
                                                                [ { AsOfDate = DateOnly(2030, 1, 1); Factor = 0.7m } ] })
    |> should not' (contain "structured_credit_factor_after_maturity")

[<Fact>]
let ``CustomAsset writes require the declared profile envelope in the document`` () =
    let kindWith termsJson =
        SecurityKind.CustomAsset {
            CustomProfileId = "structured-credit-io-po"
            ProfileVersion = 3
            TermsJson = termsJson
        }

    validationErrorCodes (kindWith """{"customProfileId":"structured-credit-io-po","profileFields":{}}""")
    |> should contain "custom_asset_profile_version_missing"

    validationErrorCodes (kindWith """{"customProfileId":"structured-credit-io-po","profileVersion":3}""")
    |> should contain "custom_asset_profile_fields_missing"

    validationErrorCodes (kindWith "not-json")
    |> should contain "custom_asset_terms_invalid"

    // A JSON number is not enough: a fractional version would persist verbatim in the canonical
    // document while the tolerant read defaults it to 1, so the write path requires an integer.
    validationErrorCodes (kindWith """{"customProfileId":"structured-credit-io-po","profileVersion":1.5,"profileFields":{}}""")
    |> should contain "custom_asset_profile_version_invalid"

    validationErrorCodes (kindWith """{"customProfileId":"structured-credit-io-po","profileVersion":0,"profileFields":{}}""")
    |> should contain "custom_asset_profile_version_invalid"

    validationErrorCodes (kindWith """{"customProfileId":"structured-credit-io-po","profileVersion":3,"profileFields":{}}""")
    |> List.isEmpty
    |> should equal true

[<Fact>]
let ``AssetClassRegistry exposes a distinct, non-empty asset-class taxonomy`` () =
    let classes = AssetClassRegistry.assetClasses
    classes |> List.isEmpty |> should equal false
    classes |> List.distinct |> List.length |> should equal (List.length classes)

[<Fact>]
let ``AssetClassRegistry every asset class resolves to a matching descriptor`` () =
    for assetClass in AssetClassRegistry.assetClasses do
        match AssetClassRegistry.tryDescriptorByName assetClass with
        | Some descriptor -> descriptor.AssetClassName |> should equal assetClass
        | None -> failwithf "Expected a descriptor for asset class '%s'" assetClass

[<Fact>]
let ``AssetClassRegistry tryDescriptorByName returns None for an unknown asset class`` () =
    AssetClassRegistry.tryDescriptorByName "NotARealAssetClass" |> should equal None

[<Fact>]
let ``AssetClassRegistry assetClassName agrees with SecurityKind.assetClass`` () =
    for kind, expected in registrySampleKinds do
        AssetClassRegistry.assetClassName kind |> should equal expected
        SecurityKind.assetClass kind |> should equal expected

[<Fact>]
let ``AssetClassRegistry isDerivative agrees with SecurityKind.isDerivative`` () =
    let expectedDerivatives = set [ "Option"; "Future"; "Swap"; "Cfd"; "Warrant"; "FxForward"; "NonDeliverableForward"; "CreditDefaultSwap"; "Swaption"; "CapFloor" ]
    for kind, name in registrySampleKinds do
        let expected = Set.contains name expectedDerivatives
        AssetClassRegistry.isDerivative kind |> should equal expected
        SecurityKind.isDerivative kind |> should equal expected

[<Fact>]
let ``AssetClassRegistry classification carries the expected fixed metadata`` () =
    let equity = AssetClassRegistry.classification (SecurityKind.Equity { ShareClass = None; VotingRightsCat = None; Classification = None })
    equity.AssetClass |> should equal AssetClass.Equity
    equity.Family |> should equal (Some AssetFamily.CommonEquity)
    equity.SubType |> should equal SecuritySubType.CommonShare
    equity.TypeName |> should equal "Equity"

    let tbill = AssetClassRegistry.classification (SecurityKind.TreasuryBill { Maturity = DateOnly(2026, 6, 1); AuctionDate = None; CUSIP = None; DiscountRate = None })
    tbill.AssetClass |> should equal AssetClass.FixedIncome
    tbill.Family |> should equal (Some AssetFamily.Sovereign)
    tbill.SubType |> should equal SecuritySubType.TreasuryBill
    tbill.IssuerType |> should equal (Some "Sovereign")
    tbill.RiskCountry |> should equal (Some "US")

let private economicDefinitionFor (kind: SecurityKind) =
    let record = createSecurityRecord None
    SecurityMasterLegacyUpgrade.toEconomicDefinition { record with Kind = kind }

[<Fact>]
let ``Legacy upgrade refines a demand deposit into the DemandDeposit sub-type`` () =
    let demand =
        economicDefinitionFor (SecurityKind.Deposit {
            DepositType = "DemandDeposit"; InstitutionName = "First Bank"; Maturity = None
            InterestRate = None; DayCount = None; IsCallable = false })
    demand.Classification.AssetClass |> should equal AssetClass.CashEquivalent
    demand.Classification.SubType |> should equal SecuritySubType.DemandDeposit

[<Fact>]
let ``Legacy upgrade defaults a non-demand deposit to the TimeDeposit sub-type`` () =
    let time =
        economicDefinitionFor (SecurityKind.Deposit {
            DepositType = "TimeDeposit"; InstitutionName = "First Bank"; Maturity = None
            InterestRate = None; DayCount = None; IsCallable = false })
    time.Classification.SubType |> should equal SecuritySubType.TimeDeposit

[<Fact>]
let ``Legacy upgrade carries the OtherSecurity sub-type from instrument terms`` () =
    let other =
        economicDefinitionFor (SecurityKind.OtherSecurity {
            Category = "Widget"; SubType = Some "Gizmo"; Maturity = None; IssuerName = None; SettlementType = None })
    other.Classification.AssetClass |> should equal AssetClass.Other
    other.Classification.SubType |> should equal (SecuritySubType.OtherSubType "Gizmo")

[<Fact>]
let ``Legacy upgrade reads IO and PO strips from the tranche label as whole tokens only`` () =
    let structuredProductFor tranche =
        let definition =
            economicDefinitionFor (SecurityKind.StructuredCredit {
                Tranche = tranche; PoolId = None; CollateralType = "AgencyPool"; OriginalFace = 1_000_000m
                CurrentFactor = None; CouponOrIndex = "5.5"; FactorSchedule = None; FactorScheduleEntries = []
                Maturity = None; Kind = None; Seniority = None; AttachmentPoint = None; DetachmentPoint = None
                WeightedAverageCoupon = None; WeightedAverageMaturityMonths = None; WeightedAverageLoanAgeMonths = None
                ReinvestmentPeriodEnd = None; NonCallPeriodEnd = None
                ScheduleConventions = ScheduleConventions.empty
                CallPutSchedules = CallPutSchedules.empty })
        definition.Terms.StructuredProduct |> Option.defaultWith (fun () -> failwith "Expected StructuredProduct term module")

    // "Senior" contains the letters "io"; it is not an interest-only strip.
    (structuredProductFor "Senior").IsInterestOnly |> should equal false
    (structuredProductFor "Senior").IsPrincipalOnly |> should equal false
    (structuredProductFor "IO-1").IsInterestOnly |> should equal true
    (structuredProductFor "A/PO").IsPrincipalOnly |> should equal true
    (structuredProductFor "io").IsInterestOnly |> should equal true
    (structuredProductFor "Portfolio").IsPrincipalOnly |> should equal false

// ── Structured-credit reclassification proposals (ADR-022, report first) ───────────────────────

let private reclassificationFor (kind: SecurityKind) =
    let record = createSecurityRecord None
    SecurityMasterLegacyUpgrade.proposeStructuredCreditReclassification { record with Kind = kind }

let private securitizedBond subclass coupon par =
    SecurityKind.Bond {
        Maturity = DateOnly(2040, 3, 25)
        IssueDate = None
        Coupon = coupon
        IsCallable = false
        CallDate = None
        IssuerName = Some "Trust 2026-1"
        Seniority = Some "Senior"
        Subclass = subclass
        Par = par
        PaymentFrequency = None
        LegalFinalMaturity = None
        PreRefundDate = None
        MandatoryPutDate = None
        PrincipalSchedule = []
        ScheduleConventions = ScheduleConventions.empty
        CallPutSchedules = CallPutSchedules.empty
    }

let private blockerCodes (proposal: SecurityMasterLegacyUpgrade.StructuredCreditReclassificationProposal) =
    proposal.Blockers |> List.map (fun blocker -> blocker.Code)

[<Fact>]
let ``Reclassification proposal carries bond par, coupon, maturity and kind but blocks on tranche and collateral`` () =
    let proposal =
        reclassificationFor (securitizedBond BondSubclass.AgencyMbs (BondCouponStructure.Fixed(5.5m, None)) (Some 1000m))
        |> Option.defaultWith (fun () -> failwith "Expected a securitised bond to be a reclassification candidate")

    proposal.SourceLabel |> should equal "AgencyMbs"
    proposal.Draft.Kind |> should equal (Some StructuredCreditKind.AgencyMbs)
    proposal.Draft.OriginalFace |> should equal (Some 1000m)
    proposal.Draft.CouponOrIndex |> should equal (Some "5.5")
    proposal.Draft.Maturity |> should equal (Some (DateOnly(2040, 3, 25)))
    proposal.Draft.Seniority |> should equal (Some "Senior")
    blockerCodes proposal |> should equal [ "structured_credit_tranche_required"; "structured_credit_collateral_type_required" ]
    proposal.ProposedTerms |> should equal None

[<Fact>]
let ``Reclassification proposal takes the floating index as couponOrIndex`` () =
    let proposal =
        reclassificationFor (securitizedBond BondSubclass.Clo (BondCouponStructure.Floating("SOFR", Some 250m, None, None, None)) None)
        |> Option.defaultWith (fun () -> failwith "Expected a CLO bond to be a reclassification candidate")

    proposal.Draft.CouponOrIndex |> should equal (Some "SOFR")
    proposal.Draft.Kind |> should equal (Some StructuredCreditKind.Clo)
    blockerCodes proposal |> should contain "structured_credit_original_face_required"

[<Fact>]
let ``Reclassification proposal never guesses between agency and non-agency MBS`` () =
    let proposal =
        reclassificationFor (securitizedBond BondSubclass.MortgageBacked BondCouponStructure.ZeroCoupon (Some 500m))
        |> Option.defaultWith (fun () -> failwith "Expected a mortgage-backed bond to be a reclassification candidate")

    proposal.Draft.Kind |> should equal None
    blockerCodes proposal |> should contain "structured_credit_kind_ambiguous"
    proposal.ProposedTerms |> should equal None

[<Fact>]
let ``Reclassification proposal blocks on kinds and coupon structures StructuredCredit cannot represent`` () =
    let inverseIo =
        reclassificationFor (securitizedBond BondSubclass.InverseInterestOnly BondCouponStructure.ZeroCoupon None)
        |> Option.defaultWith (fun () -> failwith "Expected an inverse IO bond to be a reclassification candidate")
    blockerCodes inverseIo |> should contain "structured_credit_kind_unrepresentable"

    let stepCoupon =
        BondCouponStructure.Step([ ({ EffectiveDate = DateOnly(2027, 1, 1); Rate = 4m } : StepCouponEntry) ], None)
    let step =
        reclassificationFor (securitizedBond BondSubclass.Cdo stepCoupon None)
        |> Option.defaultWith (fun () -> failwith "Expected a CDO bond to be a reclassification candidate")
    step.Draft.CouponOrIndex |> should equal None
    blockerCodes step |> should contain "structured_credit_coupon_structure_unrepresentable"

[<Fact>]
let ``Reclassification proposal ignores bonds outside the securitised subclasses and non-securitised custom rows`` () =
    reclassificationFor (securitizedBond BondSubclass.Corporate (BondCouponStructure.Fixed(4m, None)) (Some 1000m))
    |> should equal None

    reclassificationFor (SecurityKind.CustomAsset {
        CustomProfileId = "private-fund"; ProfileVersion = 1
        TermsJson = """{"customProfileId":"private-fund","profileVersion":1,"category":"PrivateFund","profileFields":{}}""" })
    |> should equal None

    reclassificationFor (SecurityKind.OtherSecurity {
        Category = "Widget"; SubType = None; Maturity = None; IssuerName = None; SettlementType = None })
    |> should equal None

    reclassificationFor (SecurityKind.Equity { ShareClass = None; VotingRightsCat = None; Classification = None })
    |> should equal None

[<Fact>]
let ``Reclassification proposal is ready when a custom profile carries every required StructuredCredit term`` () =
    let termsJson =
        """{"customProfileId":"legacy-abs","profileVersion":2,"category":"CLO",
            "profileFields":{"tranche":"B","poolId":"CLO-2026-1","collateralType":"Leveraged loans",
                             "originalFace":2500000,"currentFactor":0.91,"couponOrIndex":"SOFR+325",
                             "maturity":"2037-10-20","seniority":"Mezzanine"}}"""
    let proposal =
        reclassificationFor (SecurityKind.CustomAsset { CustomProfileId = "legacy-abs"; ProfileVersion = 2; TermsJson = termsJson })
        |> Option.defaultWith (fun () -> failwith "Expected a CLO custom asset to be a reclassification candidate")

    proposal.SourceLabel |> should equal "CLO"
    proposal.Blockers |> should be Empty
    let terms = proposal.ProposedTerms |> Option.defaultWith (fun () -> failwith "Expected proposed StructuredCredit terms")
    terms.Tranche |> should equal "B"
    terms.PoolId |> should equal (Some "CLO-2026-1")
    terms.CollateralType |> should equal "Leveraged loans"
    terms.OriginalFace |> should equal 2500000m
    terms.CurrentFactor |> should equal (Some 0.91m)
    terms.CouponOrIndex |> should equal "SOFR+325"
    terms.Maturity |> should equal (Some (DateOnly(2037, 10, 20)))
    terms.Kind |> should equal (Some StructuredCreditKind.Clo)
    terms.Seniority |> should equal (Some "Mezzanine")
    terms.FactorScheduleEntries |> should be Empty
    terms.AttachmentPoint |> should equal None

[<Fact>]
let ``Reclassification proposal lets a typed kind on the custom profile resolve an ambiguous category`` () =
    let termsJson =
        """{"customProfileId":"legacy-mbs","profileVersion":1,"category":"MBS",
            "profileFields":{"structuredCreditKind":"NonAgencyMbs","tranche":"M1"}}"""
    let proposal =
        reclassificationFor (SecurityKind.CustomAsset { CustomProfileId = "legacy-mbs"; ProfileVersion = 1; TermsJson = termsJson })
        |> Option.defaultWith (fun () -> failwith "Expected an MBS custom asset to be a reclassification candidate")

    proposal.Draft.Kind |> should equal (Some StructuredCreditKind.NonAgencyMbs)
    proposal.Draft.Tranche |> should equal (Some "M1")
    blockerCodes proposal |> should not' (contain "structured_credit_kind_ambiguous")
    blockerCodes proposal |> should contain "structured_credit_collateral_type_required"

[<Fact>]
let ``Reclassification proposal reads envelope-less legacy custom rows read back as OtherSecurity`` () =
    let proposal =
        reclassificationFor (SecurityKind.OtherSecurity {
            Category = "ABS"; SubType = None; Maturity = Some (DateOnly(2030, 1, 15)); IssuerName = None; SettlementType = None })
        |> Option.defaultWith (fun () -> failwith "Expected an ABS legacy row to be a reclassification candidate")

    proposal.SourceLabel |> should equal "ABS"
    proposal.Draft.Kind |> should equal (Some StructuredCreditKind.AssetBacked)
    proposal.Draft.Maturity |> should equal (Some (DateOnly(2030, 1, 15)))
    blockerCodes proposal
    |> should equal [
        "structured_credit_tranche_required"
        "structured_credit_collateral_type_required"
        "structured_credit_original_face_required"
        "structured_credit_coupon_or_index_required" ]

// ── Schedule conventions: read ahead of write (ADR-024, increment 4a) ───────────────────────────

/// Every member declared, with accrualAdjusted=false beside a ModifiedFollowing convention (the
/// economic accrual must keep the convention verbatim) and two payment calendars in unsorted order.
let private declaredConventions =
    { ScheduleConventions.empty with
        BusinessDayConvention = Some "ModifiedFollowing"
        AccrualAdjusted = Some false
        PaymentCalendars = [ "USNY"; "GBLO" ]
        PaymentLagBusinessDays = Some 2
        RollConvention = Some "15"
        EndOfMonthRule = Some false
        FirstRegularDate = Some (DateOnly(2026, 12, 15))
        LastRegularDate = Some (DateOnly(2034, 6, 15))
        Direction = Some "Backward"
        StubPolicy = Some "Short"
        CouponRateUnit = Some "Percent"
        CouponCashBasis = Some "Level" }

/// The frozen canonical written form of declaredConventions: all twelve members, in ordinal order.
let private declaredConventionsJson =
    """{"accrualAdjusted":false,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"Level","couponRateUnit":"Percent","direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2026-12-15","lastRegularDate":"2034-06-15","paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","stubPolicy":"Short"}"""

let private scheduleBond conventions =
    SecurityKind.Bond { BondTerms.fixedRate (DateOnly(2034, 6, 15)) 4.5m (Some "30/360") (Some "ACME") with ScheduleConventions = conventions }

let private scheduleDeposit conventions =
    SecurityKind.CertificateOfDeposit ({
        IssuerName = "First Bank"
        Maturity = DateOnly(2028, 3, 15)
        CouponRate = Some 4.1m
        CallableDate = None
        DayCount = Some "ACT/360"
        ScheduleConventions = conventions
        CallPutSchedules = CallPutSchedules.empty
    } : CertificateOfDepositTerms)

let private scheduleStructuredCredit conventions =
    SecurityKind.StructuredCredit ({
        Tranche = "A-1"; PoolId = None; CollateralType = "CLO"; OriginalFace = 1_000_000m
        CurrentFactor = None; CouponOrIndex = "SOFR+250"; FactorSchedule = None; FactorScheduleEntries = []
        Maturity = Some (DateOnly(2034, 6, 15)); Kind = Some StructuredCreditKind.Clo; Seniority = None
        AttachmentPoint = None; DetachmentPoint = None; WeightedAverageCoupon = None
        WeightedAverageMaturityMonths = None; WeightedAverageLoanAgeMonths = None
        ReinvestmentPeriodEnd = None; NonCallPeriodEnd = None
        ScheduleConventions = conventions
        CallPutSchedules = CallPutSchedules.empty
    } : StructuredCreditTerms)

let private scheduleSwap legs =
    SecurityKind.Swap { EffectiveDate = DateOnly(2026, 1, 15); MaturityDate = DateOnly(2031, 1, 15); Legs = legs }

/// The asset class and the stamped flat terms the snapshot wrapper writes for a record of the kind.
let private flatTermsFor (kind: SecurityKind) =
    let wrapper = SecurityMasterSnapshotWrapper({ createSecurityRecord None with Kind = kind })
    use document = JsonDocument.Parse(wrapper.AssetSpecificTermsJson)
    wrapper.AssetClass, document.RootElement.Clone()

let private hasProperty (name: string) (element: JsonElement) =
    match element.TryGetProperty(name) with
    | true, _ -> true
    | _ -> false

let private termsStamp (payload: JsonElement) =
    payload.GetProperty("assetClassTermsVersion").GetInt32()

/// A container's element members as the term schema declares them for the class: the conventions
/// block (on a swap, per leg) or a call or put schedule entry.
let private schemaElementMembers assetClass container =
    Meridian.Contracts.SecurityMaster.SecurityAssetTermsSchema.ElementFields(assetClass, container)
    |> Seq.map (fun field -> field.Key)
    |> List.ofSeq

let private ordinal (keys: string list) =
    keys |> List.sortWith (fun left right -> String.CompareOrdinal(left, right))

/// The Accrual module a declared block fills: the convention and joint calendar, nothing else.
let private conventionAccrual convention calendar =
    Some ({ AccrualMethod = None; AccrualStartDate = None; ExDividendDays = None
            BusinessDayConvention = convention; HolidayCalendar = calendar; DayCount = None } : AccrualTerms)

[<Fact>]
let ``ScheduleConventions.isEmpty is true only for the empty block`` () =
    ScheduleConventions.isEmpty ScheduleConventions.empty |> should equal true

    // A blank calendar entry or an empty-string convention is a declaration at the F# level: the
    // C# reader must drop blank calendars and turn blank strings into None before they reach F#.
    let blankCalendar = { ScheduleConventions.empty with PaymentCalendars = [ " " ] }
    ScheduleConventions.isEmpty blankCalendar |> should equal false
    ScheduleConventions.isEmpty { ScheduleConventions.empty with BusinessDayConvention = Some "" } |> should equal false
    // So the writer stamps it at the block's version and writes the block, although the economic
    // accrual reads nothing from it; changing blank handling has to update this pin on purpose.
    let assetClass, payload = flatTermsFor (scheduleBond blankCalendar)
    (assetClass, termsStamp payload) |> should equal ("Bond", 2)
    payload.GetProperty("scheduleConventions").GetProperty("paymentCalendars").GetRawText() |> should equal """[" "]"""
    (economicDefinitionFor (scheduleBond blankCalendar)).Terms.Accrual |> should equal None

    // Declaring any one member, false and zero included, is a declaration.
    let declaringOneMember =
        [ "businessDayConvention", { ScheduleConventions.empty with BusinessDayConvention = Some "Following" }
          "accrualAdjusted", { ScheduleConventions.empty with AccrualAdjusted = Some false }
          "paymentCalendars", { ScheduleConventions.empty with PaymentCalendars = [ "USNY" ] }
          "paymentLagBusinessDays", { ScheduleConventions.empty with PaymentLagBusinessDays = Some 0 }
          "rollConvention", { ScheduleConventions.empty with RollConvention = Some "EndOfMonth" }
          "endOfMonthRule", { ScheduleConventions.empty with EndOfMonthRule = Some false }
          "firstRegularDate", { ScheduleConventions.empty with FirstRegularDate = Some (DateOnly(2026, 12, 15)) }
          "lastRegularDate", { ScheduleConventions.empty with LastRegularDate = Some (DateOnly(2034, 6, 15)) }
          "direction", { ScheduleConventions.empty with Direction = Some "Forward" }
          "stubPolicy", { ScheduleConventions.empty with StubPolicy = Some "Short" }
          "couponRateUnit", { ScheduleConventions.empty with CouponRateUnit = Some "Fraction" }
          "couponCashBasis", { ScheduleConventions.empty with CouponCashBasis = Some "DayCount" } ]

    // One flip per member the term schema declares for the block, so a new member needs a case here.
    declaringOneMember |> List.map fst |> ordinal |> should equal (ordinal (schemaElementMembers "Bond" "scheduleConventions"))
    for memberName, conventions in declaringOneMember do
        (memberName, ScheduleConventions.isEmpty conventions) |> should equal (memberName, false)

[<Fact>]
let ``Flat writer stamps every class at its S2 document floor and omits scheduleConventions when nothing is declared`` () =
    // The registry table, plus the readable-ahead classes it does not carry and a swap with legs.
    let kinds =
        (registrySampleKinds |> List.map fst)
        @ [ scheduleDeposit ScheduleConventions.empty
            scheduleStructuredCredit ScheduleConventions.empty
            scheduleSwap [ legacySwapLeg; { legacySwapLeg with LegType = "Floating"; FixedRate = None; Index = Some "SOFR" } ] ]

    for kind in kinds do
        let assetClass, payload = flatTermsFor kind
        match kind with
        | SecurityKind.CustomAsset _ ->
            // A profile envelope is written verbatim, never stamped.
            (assetClass, hasProperty "assetClassTermsVersion" payload) |> should equal (assetClass, false)
        | _ ->
            (assetClass, termsStamp payload)
            |> should equal (assetClass, Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog.GetStampFloor(assetClass))

        // Below the block's version the key is absent, at the root and on every leg.
        (assetClass, hasProperty "scheduleConventions" payload) |> should equal (assetClass, false)
        // So are the call and put schedules, which sit a version above the block.
        (assetClass, hasProperty "callSchedule" payload, hasProperty "putSchedule" payload)
        |> should equal (assetClass, false, false)
        match payload.TryGetProperty("legs") with
        | true, legs ->
            for leg in legs.EnumerateArray() do
                (assetClass, hasProperty "scheduleConventions" leg) |> should equal (assetClass, false)
        | _ -> ()

[<Fact>]
let ``Flat writer keeps declared schedule conventions as the canonical block at the version that reads them`` () =
    for kind, expectedStamp in [ scheduleBond declaredConventions, 2
                                 scheduleDeposit declaredConventions, 2
                                 scheduleStructuredCredit declaredConventions, 3 ] do
        let assetClass, payload = flatTermsFor kind
        (assetClass, termsStamp payload) |> should equal (assetClass, expectedStamp)
        let block = payload.GetProperty("scheduleConventions")
        (assetClass, block.GetRawText()) |> should equal (assetClass, declaredConventionsJson)
        (assetClass, block.EnumerateObject() |> Seq.map (fun property -> property.Name) |> List.ofSeq)
        |> should equal (assetClass, ordinal (schemaElementMembers assetClass "scheduleConventions"))
        // The node reads what it re-serializes.
        Meridian.Contracts.SecurityMaster.SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload)

[<Fact>]
let ``Flat writer writes every schedule-convention member, unset ones as null or empty, when one is declared`` () =
    let assetClass, payload = flatTermsFor (scheduleBond { ScheduleConventions.empty with StubPolicy = Some "Long" })

    assetClass |> should equal "Bond"
    termsStamp payload |> should equal 2
    payload.GetProperty("scheduleConventions").GetRawText()
    |> should equal
        """{"accrualAdjusted":null,"businessDayConvention":null,"couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":[],"paymentLagBusinessDays":null,"rollConvention":null,"stubPolicy":"Long"}"""

[<Fact>]
let ``Flat writer stamps a swap with one declaring leg at version 2 and writes the key on every leg`` () =
    let declaringLeg = { legacySwapLeg with LegId = Some "fixed-leg"; Direction = Some "Pay"; ScheduleConventions = declaredConventions }
    let plainLeg =
        { legacySwapLeg with
            LegId = Some "floating-leg"; LegType = "Floating"; Direction = Some "Receive"; Index = Some "SOFR"; FixedRate = None }
    let assetClass, payload = flatTermsFor (scheduleSwap [ declaringLeg; plainLeg ])

    assetClass |> should equal "Swap"
    termsStamp payload |> should equal 2
    // A swap carries the block per leg, never at the root.
    hasProperty "scheduleConventions" payload |> should equal false
    let legs = payload.GetProperty("legs")
    let declaringBlock = legs.[0].GetProperty("scheduleConventions")
    declaringBlock.GetRawText() |> should equal declaredConventionsJson
    declaringBlock.EnumerateObject() |> Seq.map (fun property -> property.Name) |> List.ofSeq
    |> should equal (ordinal (schemaElementMembers "Swap" "legs.scheduleConventions"))
    legs.[1].GetProperty("scheduleConventions").ValueKind |> should equal JsonValueKind.Null
    // The leg's pay/receive direction stays beside the block's generation direction.
    legs.[0].GetProperty("direction").GetString() |> should equal "Pay"
    legs.[1].GetProperty("direction").GetString() |> should equal "Receive"
    Meridian.Contracts.SecurityMaster.SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload)

[<Fact>]
let ``Economic accrual follows the frozen schedule-convention rules with no common-terms calendar fallback`` () =
    let bondDefinition conventions = economicDefinitionFor (scheduleBond conventions)

    // The record's common terms name NYSE; the accrual never falls back to it.
    let full = bondDefinition declaredConventions
    full.Common.HolidayCalendarId |> should equal (Some "NYSE")
    // accrualAdjusted=false leaves the convention verbatim (it never turns into Unadjusted), and the
    // payment calendars become one joint calendar id.
    full.Terms.Accrual |> should equal (conventionAccrual (Some "ModifiedFollowing") (Some "GBLO+USNY"))

    // Nothing that is not a business-day convention or a payment calendar fills the module.
    (bondDefinition { declaredConventions with BusinessDayConvention = None; PaymentCalendars = [] }).Terms.Accrual
    |> should equal None
    (bondDefinition { ScheduleConventions.empty with PaymentCalendars = [ " "; "" ] }).Terms.Accrual |> should equal None
    (bondDefinition ScheduleConventions.empty).Terms.Accrual |> should equal None

    // A convention alone keeps its alias verbatim and names no calendar.
    let conventionOnly = bondDefinition { ScheduleConventions.empty with BusinessDayConvention = Some "MF" }
    conventionOnly.Terms.Accrual |> should equal (conventionAccrual (Some "MF") None)
    conventionOnly.Common.HolidayCalendarId |> should equal (Some "NYSE")

    // A single calendar is its own id, and it is the payment calendar, not the common one.
    let calendarOnly = bondDefinition { ScheduleConventions.empty with PaymentCalendars = [ "GBLO" ] }
    calendarOnly.Terms.Accrual |> should equal (conventionAccrual None (Some "GBLO"))
    calendarOnly.Common.HolidayCalendarId |> should equal (Some "NYSE")

    // Duplicate, case-variant and blank calendars join under BusinessDayCalendars.JointCalendarId.
    let calendars = [ " usny "; "GBLO"; "USNY"; " " ]
    let joint = Meridian.Contracts.Calendars.BusinessDayCalendars.JointCalendarId(calendars)
    joint |> should equal "GBLO+usny"
    (bondDefinition { ScheduleConventions.empty with PaymentCalendars = calendars }).Terms.Accrual
    |> should equal (conventionAccrual None (Some joint))

[<Fact>]
let ``Economic accrual fills from declared conventions on bonds, deposits and structured credit but not swaps`` () =
    let expected = conventionAccrual (Some "ModifiedFollowing") (Some "GBLO+USNY")
    (economicDefinitionFor (scheduleDeposit declaredConventions)).Terms.Accrual |> should equal expected
    (economicDefinitionFor (scheduleStructuredCredit declaredConventions)).Terms.Accrual |> should equal expected
    (economicDefinitionFor (scheduleDeposit ScheduleConventions.empty)).Terms.Accrual |> should equal None
    (economicDefinitionFor (scheduleStructuredCredit ScheduleConventions.empty)).Terms.Accrual |> should equal None

    // A swap's conventions are per leg; the swap arm leaves the module unset.
    (economicDefinitionFor (scheduleSwap [ { legacySwapLeg with ScheduleConventions = declaredConventions } ])).Terms.Accrual
    |> should equal None

[<Fact>]
let ``Reclassification proposal blocks a bond that declares schedule conventions`` () =
    let agencyBond conventions =
        match securitizedBond BondSubclass.AgencyMbs (BondCouponStructure.Fixed(5.5m, None)) (Some 1000m) with
        | SecurityKind.Bond terms -> SecurityKind.Bond { terms with ScheduleConventions = conventions }
        | other -> other

    let proposal =
        reclassificationFor (agencyBond { ScheduleConventions.empty with StubPolicy = Some "Short" })
        |> Option.defaultWith (fun () -> failwith "Expected a securitised bond to be a reclassification candidate")

    blockerCodes proposal
    |> should equal [
        "structured_credit_schedule_conventions_unwritable"
        "structured_credit_tranche_required"
        "structured_credit_collateral_type_required" ]
    (proposal.Blockers |> List.find (fun blocker -> blocker.Code = "structured_credit_schedule_conventions_unwritable")).Message
    |> should equal "The bond declares scheduleConventions, but the StructuredCredit schema has no top-level issueDate or paymentFrequency; repair the declared schedule before reclassification."
    proposal.ProposedTerms |> should equal None
    // The draft still reports what the bond carries.
    proposal.Draft.Kind |> should equal (Some StructuredCreditKind.AgencyMbs)
    proposal.Draft.OriginalFace |> should equal (Some 1000m)

    let plain =
        reclassificationFor (agencyBond ScheduleConventions.empty)
        |> Option.defaultWith (fun () -> failwith "Expected a securitised bond to be a reclassification candidate")
    blockerCodes plain |> should not' (contain "structured_credit_schedule_conventions_unwritable")

// ── Call and put schedules: read ahead of write (ADR-024, increment 4b) ─────────────────────────

/// The kind with its declared call and put schedules replaced (bonds, deposits and structured credit).
let private withCallPutSchedules (schedules: CallPutSchedules) (kind: SecurityKind) =
    match kind with
    | SecurityKind.Bond terms -> SecurityKind.Bond { terms with CallPutSchedules = schedules }
    | SecurityKind.CertificateOfDeposit terms -> SecurityKind.CertificateOfDeposit { terms with CallPutSchedules = schedules }
    | SecurityKind.StructuredCredit terms -> SecurityKind.StructuredCredit { terms with CallPutSchedules = schedules }
    | other -> failwithf "'%s' declares no call or put schedules." (SecurityKind.assetClass other)

let private callsOnly (calls: CallScheduleTerm list) = { CallPutSchedules.empty with Calls = calls }

let private putsOnly (puts: PutScheduleTerm list) = { CallPutSchedules.empty with Puts = puts }

let private firstCall = CallScheduleTerm.onDate (DateOnly(2029, 6, 15))

let private firstPut = PutScheduleTerm.onDate (DateOnly(2030, 6, 15))

/// The frozen canonical written entries of firstCall and firstPut: every member in ordinal order,
/// unset ones null.
let private firstCallJson =
    """{"callDate":"2029-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}"""

let private firstPutJson =
    """{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":"2030-06-15","putEndDate":null,"putPrice":null}"""

let private economicCallFor (kind: SecurityKind) = (economicDefinitionFor kind).Terms.Call

let private economicCallEntry date price isParCall callType =
    ({ CallDt = date; CallPx = price; IsParCall = isParCall; CallType = callType } : CallScheduleEntry)

let private economicPutEntry date price = ({ PutDt = date; PutPx = price } : PutScheduleEntry)

/// A bond with the call scalars and the declared schedules.
let private callableBond isCallable callDate schedules =
    SecurityKind.Bond
        { BondTerms.fixedRate (DateOnly(2034, 6, 15)) 4.5m (Some "30/360") (Some "ACME") with
            IsCallable = isCallable
            CallDate = callDate
            CallPutSchedules = schedules }

/// A certificate of deposit with the scalar callable date and the declared schedules.
let private callableDeposit callableDate schedules =
    match scheduleDeposit ScheduleConventions.empty with
    | SecurityKind.CertificateOfDeposit terms ->
        SecurityKind.CertificateOfDeposit { terms with CallableDate = callableDate; CallPutSchedules = schedules }
    | other -> other

[<Fact>]
let ``CallPutSchedules.isEmpty is true only for the empty schedules and onDate states only the date`` () =
    CallPutSchedules.isEmpty CallPutSchedules.empty |> should equal true
    CallPutSchedules.empty |> should equal ({ Calls = []; Puts = [] } : CallPutSchedules)

    // One entry of either kind, with nothing but its date, is a declaration.
    CallPutSchedules.isEmpty (callsOnly [ firstCall ]) |> should equal false
    CallPutSchedules.isEmpty (putsOnly [ firstPut ]) |> should equal false
    CallPutSchedules.isEmpty ({ Calls = [ firstCall ]; Puts = [ firstPut ] } : CallPutSchedules) |> should equal false

    CallScheduleTerm.onDate (DateOnly(2029, 6, 15))
    |> should equal
        ({ CallDate = DateOnly(2029, 6, 15)
           CallEndDate = None
           ExerciseStyle = None
           ExerciseFrequency = None
           CallPrice = None
           PriceBasis = None
           PriceType = None
           MakeWholeSpreadBps = None
           PartialRedemption = None
           MinimumNoticeDays = None
           MaximumNoticeDays = None
           NoticeDayType = None } : CallScheduleTerm)
    PutScheduleTerm.onDate (DateOnly(2030, 6, 15))
    |> should equal
        ({ PutDate = DateOnly(2030, 6, 15)
           PutEndDate = None
           ExerciseStyle = None
           ExerciseFrequency = None
           PutPrice = None
           PriceBasis = None
           MinimumNoticeDays = None
           MaximumNoticeDays = None
           NoticeDayType = None } : PutScheduleTerm)

[<Fact>]
let ``Flat writer stamps the highest version among the terms a record holds and writes both schedules from theirs`` () =
    let oneCall = callsOnly [ firstCall ]
    let onePut = putsOnly [ firstPut ]
    let callJson = "[" + firstCallJson + "]"
    let putJson = "[" + firstPutJson + "]"
    // The kind, its stamp, its scheduleConventions text and its callSchedule and putSchedule texts
    // (None: both keys absent).
    let cases : (SecurityKind * int * string * (string * string) option) list =
        [ scheduleBond declaredConventions, 2, declaredConventionsJson, None
          scheduleBond ScheduleConventions.empty |> withCallPutSchedules oneCall, 3, "null", Some (callJson, "[]")
          scheduleBond ScheduleConventions.empty |> withCallPutSchedules onePut, 3, "null", Some ("[]", putJson)
          scheduleBond declaredConventions |> withCallPutSchedules oneCall, 3, declaredConventionsJson, Some (callJson, "[]")
          scheduleDeposit declaredConventions, 2, declaredConventionsJson, None
          scheduleDeposit ScheduleConventions.empty |> withCallPutSchedules oneCall, 3, "null", Some (callJson, "[]")
          scheduleDeposit ScheduleConventions.empty |> withCallPutSchedules onePut, 3, "null", Some ("[]", putJson)
          scheduleStructuredCredit declaredConventions, 3, declaredConventionsJson, None
          scheduleStructuredCredit ScheduleConventions.empty |> withCallPutSchedules oneCall, 4, "null", Some (callJson, "[]")
          scheduleStructuredCredit ScheduleConventions.empty |> withCallPutSchedules onePut, 4, "null", Some ("[]", putJson)
          scheduleStructuredCredit declaredConventions |> withCallPutSchedules onePut, 4, declaredConventionsJson, Some ("[]", putJson) ]

    for index, (kind, expectedStamp, expectedConventions, expectedSchedules) in List.indexed cases do
        let assetClass, payload = flatTermsFor kind
        let label = sprintf "case %d (%s)" index assetClass
        (label, termsStamp payload) |> should equal (label, expectedStamp)
        // The block's key is present from its version up, null when nothing is declared.
        (label, payload.GetProperty("scheduleConventions").GetRawText()) |> should equal (label, expectedConventions)
        match expectedSchedules with
        | Some (calls, puts) ->
            // From the schedules' version up both keys are present, [] when none is declared.
            (label, payload.GetProperty("callSchedule").GetRawText(), payload.GetProperty("putSchedule").GetRawText())
            |> should equal (label, calls, puts)
        | None ->
            (label, hasProperty "callSchedule" payload, hasProperty "putSchedule" payload) |> should equal (label, false, false)
        // The node reads what it re-serializes.
        Meridian.Contracts.SecurityMaster.SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload)

[<Fact>]
let ``Flat writer writes declared call and put entries in stored order with every member, unset ones as null`` () =
    let laterCall =
        ({ CallDate = DateOnly(2031, 6, 15)
           CallEndDate = Some (DateOnly(2032, 6, 15))
           ExerciseStyle = Some "Bermudan"
           ExerciseFrequency = Some "SemiAnnual"
           CallPrice = Some 101.25m
           PriceBasis = Some "PercentOfPar"
           PriceType = Some "Fixed"
           MakeWholeSpreadBps = None
           PartialRedemption = Some "ProRata"
           MinimumNoticeDays = Some 30
           MaximumNoticeDays = Some 60
           NoticeDayType = Some "Business" } : CallScheduleTerm)
    let earlierCall = { CallScheduleTerm.onDate (DateOnly(2029, 6, 15)) with PriceType = Some "MakeWhole"; MakeWholeSpreadBps = Some 25m }
    let laterPut =
        ({ PutDate = DateOnly(2032, 6, 15)
           PutEndDate = Some (DateOnly(2032, 7, 15))
           ExerciseStyle = Some "American"
           ExerciseFrequency = None
           PutPrice = Some 100m
           PriceBasis = Some "PercentOfAccretedValue"
           MinimumNoticeDays = Some 15
           MaximumNoticeDays = Some 45
           NoticeDayType = Some "Calendar" } : PutScheduleTerm)
    let schedules : CallPutSchedules = { Calls = [ laterCall; earlierCall ]; Puts = [ laterPut; firstPut ] }

    let assetClass, payload = flatTermsFor (scheduleBond ScheduleConventions.empty |> withCallPutSchedules schedules)

    (assetClass, termsStamp payload) |> should equal ("Bond", 3)
    // Entries keep the order they were declared in; the writer never sorts them by date.
    payload.GetProperty("callSchedule").GetRawText()
    |> should equal
        """[{"callDate":"2031-06-15","callEndDate":"2032-06-15","callPrice":101.25,"exerciseFrequency":"SemiAnnual","exerciseStyle":"Bermudan","makeWholeSpreadBps":null,"maximumNoticeDays":60,"minimumNoticeDays":30,"noticeDayType":"Business","partialRedemption":"ProRata","priceBasis":"PercentOfPar","priceType":"Fixed"},{"callDate":"2029-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":25,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":"MakeWhole"}]"""
    payload.GetProperty("putSchedule").GetRawText()
    |> should equal (
        """[{"exerciseFrequency":null,"exerciseStyle":"American","maximumNoticeDays":45,"minimumNoticeDays":15,"noticeDayType":"Calendar","priceBasis":"PercentOfAccretedValue","putDate":"2032-06-15","putEndDate":"2032-07-15","putPrice":100},"""
        + firstPutJson + "]")

[<Fact>]
let ``Flat writer pins the whole bond payload with declared call and put schedules`` () =
    let _, payload = flatTermsFor (scheduleBond ScheduleConventions.empty |> withCallPutSchedules { Calls = [ firstCall ]; Puts = [ firstPut ] })

    payload.GetRawText()
    |> should equal (
        """{"callDate":null,"callSchedule":[""" + firstCallJson
        + """],"capRate":null,"couponRate":4.5,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":"ACME","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2034-06-15","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":["""
        + firstPutJson
        + """],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""")

[<Fact>]
let ``Flat writer pins the whole deposit payload, with callSchedule ordinally before callableDate`` () =
    let call = CallScheduleTerm.onDate (DateOnly(2027, 9, 15))
    let _, payload = flatTermsFor (callableDeposit (Some (DateOnly(2027, 3, 15))) (callsOnly [ call ]))

    // 'S' (0x53) sorts before 'a' (0x61), so the array precedes the scalar callable date.
    payload.GetRawText()
    |> should equal
        """{"callSchedule":[{"callDate":"2027-09-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"callableDate":"2027-03-15","couponRate":4.1,"dayCount":"ACT/360","issuerName":"First Bank","maturity":"2028-03-15","putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3}"""

[<Fact>]
let ``Flat writer writes exactly the registered call and put entry members, in ordinal order`` () =
    let schedules : CallPutSchedules = { Calls = [ firstCall ]; Puts = [ firstPut ] }
    for build in [ scheduleBond; scheduleDeposit; scheduleStructuredCredit ] do
        let assetClass, payload = flatTermsFor (build ScheduleConventions.empty |> withCallPutSchedules schedules)
        for container in [ "callSchedule"; "putSchedule" ] do
            let written = payload.GetProperty(container).[0].EnumerateObject() |> Seq.map (fun property -> property.Name) |> List.ofSeq
            // A member the schema registers but the writer drops (or the reverse) fails here.
            (assetClass, container, written)
            |> should equal (assetClass, container, ordinal (schemaElementMembers assetClass container))

[<Fact>]
let ``Economic call fill sorts declared entries, carries only Fixed percent-of-par prices and takes the first make-whole spread`` () =
    let dated (year: int) (month: int) (day: int) = DateOnly(year, month, day)
    let call date = CallScheduleTerm.onDate date
    let put date = PutScheduleTerm.onDate date
    // Stored out of date order; each comment names the economic entry the fill makes of it.
    let calls =
        [ // A later make-whole spread, first in stored order: not the spread the fill takes.
          { call (dated 2033 6 15) with PriceType = Some "MakeWhole"; MakeWholeSpreadBps = Some 50m; ExerciseStyle = Some "European" }
          // A fixed par price on a par basis: carried, and a par call.
          { call (dated 2032 6 15) with CallPrice = Some 100m; PriceType = Some "Fixed"; PriceBasis = Some "PercentOfPar"; ExerciseStyle = Some "Bermudan" }
          // No type or basis counts as Fixed on PercentOfPar: carried, not a par call.
          { call (dated 2029 6 15) with CallPrice = Some 102.5m; ExerciseStyle = Some "European" }
          // The earliest make-whole carries no spread; its floor price is not a call price.
          { call (dated 2028 6 15) with CallPrice = Some 100m; PriceType = Some "MakeWhole"; ExerciseStyle = Some "American" }
          // An accreted-value price is not a percent-of-par price.
          { call (dated 2030 6 15) with CallPrice = Some 100m; PriceType = Some "Fixed"; PriceBasis = Some "PercentOfAccretedValue" }
          // No type on a declared par basis counts as Fixed: carried, and a par call.
          { call (dated 2035 6 15) with CallPrice = Some 100m; PriceBasis = Some "PercentOfPar" }
          // The first make-whole by date that carries a spread.
          { call (dated 2031 6 15) with PriceType = Some "MakeWhole"; MakeWholeSpreadBps = Some 25m }
          // Same date as the 102.5 call and stored after it: the sort is stable.
          { call (dated 2029 6 15) with CallPrice = Some 101m; PriceType = Some "Fixed"; PriceBasis = Some "PercentOfPar"; ExerciseStyle = Some "American" }
          // A spread on a Fixed entry is not a make-whole spread.
          { call (dated 2027 12 15) with CallPrice = Some 103m; PriceType = Some "Fixed"; MakeWholeSpreadBps = Some 10m }
          // Undeclared spellings compare ordinally: neither a Fixed price nor a make-whole.
          { call (dated 2034 1 15) with CallPrice = Some 100m; PriceType = Some "Makewhole"; MakeWholeSpreadBps = Some 75m; ExerciseStyle = Some "American" }
          { call (dated 2034 3 15) with CallPrice = Some 100m; PriceType = Some "Fixed"; PriceBasis = Some "percentOfPar" } ]
    let puts =
        [ // An undeclared basis spelling compares ordinally: its price is not carried.
          { put (dated 2032 6 15) with PutPrice = Some 100m; PriceBasis = Some "percentOfPar" }
          { put (dated 2031 6 15) with PutPrice = Some 100m }
          { put (dated 2029 6 15) with PutPrice = Some 99.5m; PriceBasis = Some "PercentOfAccretedValue" }
          { put (dated 2030 6 15) with PutPrice = Some 101m; PriceBasis = Some "PercentOfPar" }
          put (dated 2030 6 15) ]

    economicCallFor (callableBond true (Some (dated 2030 1 15)) ({ Calls = calls; Puts = puts } : CallPutSchedules))
    |> should equal (
        Some ({ IsCallable = true
                // The scalar call date, verbatim, although an earlier call is declared.
                FirstCallDate = Some (dated 2030 1 15)
                CallPrice = None
                CallSchedule =
                    [ economicCallEntry (dated 2027 12 15) (Some 103m) false None
                      economicCallEntry (dated 2028 6 15) None false (Some "MakeWhole")
                      economicCallEntry (dated 2029 6 15) (Some 102.5m) false (Some "European")
                      economicCallEntry (dated 2029 6 15) (Some 101m) false (Some "American")
                      economicCallEntry (dated 2030 6 15) None false None
                      economicCallEntry (dated 2031 6 15) None false (Some "MakeWhole")
                      economicCallEntry (dated 2032 6 15) (Some 100m) true (Some "Bermudan")
                      economicCallEntry (dated 2033 6 15) None false (Some "MakeWhole")
                      economicCallEntry (dated 2034 1 15) None false (Some "American")
                      economicCallEntry (dated 2034 3 15) None false None
                      economicCallEntry (dated 2035 6 15) (Some 100m) true None ]
                MakeWholeSpreadBps = Some 25m
                IsPuttable = true
                PutSchedule =
                    [ economicPutEntry (dated 2029 6 15) None
                      economicPutEntry (dated 2030 6 15) (Some 101m)
                      economicPutEntry (dated 2030 6 15) None
                      economicPutEntry (dated 2031 6 15) (Some 100m)
                      economicPutEntry (dated 2032 6 15) None ] } : EconomicCallTerms))

[<Fact>]
let ``Economic call fill keeps bond and deposit call scalars verbatim and fills structured credit only from declared schedules`` () =
    let declaredCall = { CallScheduleTerm.onDate (DateOnly(2027, 9, 15)) with CallPrice = Some 100m }
    let declaredPut = { PutScheduleTerm.onDate (DateOnly(2027, 12, 15)) with PutPrice = Some 100m }
    let callEntry = economicCallEntry (DateOnly(2027, 9, 15)) (Some 100m) true None
    let putEntry = economicPutEntry (DateOnly(2027, 12, 15)) (Some 100m)
    let callTerms isCallable firstCallDate calls puts =
        Some ({ IsCallable = isCallable; FirstCallDate = firstCallDate; CallPrice = None; CallSchedule = calls
                MakeWholeSpreadBps = None; IsPuttable = not (List.isEmpty puts); PutSchedule = puts } : EconomicCallTerms)

    // A bond's isCallable and callDate are never derived from its declared calls.
    economicCallFor (callableBond false None (callsOnly [ declaredCall ]))
    |> should equal (callTerms false None [ callEntry ] [])

    // A mandatory put date alone is not a holder put; a declared put is.
    let mandatoryPutBond schedules =
        match callableBond false None schedules with
        | SecurityKind.Bond terms -> SecurityKind.Bond { terms with MandatoryPutDate = Some (DateOnly(2030, 6, 15)) }
        | other -> other
    economicCallFor (mandatoryPutBond CallPutSchedules.empty) |> should equal (callTerms false None [] [])
    economicCallFor (mandatoryPutBond (callsOnly [ declaredCall ])) |> should equal (callTerms false None [ callEntry ] [])
    economicCallFor (mandatoryPutBond (putsOnly [ declaredPut ])) |> should equal (callTerms false None [] [ putEntry ])

    // A deposit's callableDate alone sets IsCallable and FirstCallDate.
    economicCallFor (callableDeposit (Some (DateOnly(2027, 3, 15))) ({ Calls = [ declaredCall ]; Puts = [ declaredPut ] } : CallPutSchedules))
    |> should equal (callTerms true (Some (DateOnly(2027, 3, 15))) [ callEntry ] [ putEntry ])
    economicCallFor (callableDeposit None (callsOnly [ declaredCall ]))
    |> should equal (callTerms false None [ callEntry ] [])

    // Structured credit has no call scalars: nothing declared leaves the module unset (conventions
    // included), and a declared schedule fills only the lists, IsCallable meaning "calls declared".
    // The non-call period never feeds the module.
    let structuredCredit conventions schedules =
        match scheduleStructuredCredit conventions |> withCallPutSchedules schedules with
        | SecurityKind.StructuredCredit terms -> SecurityKind.StructuredCredit { terms with NonCallPeriodEnd = Some (DateOnly(2027, 6, 15)) }
        | other -> other
    economicCallFor (structuredCredit ScheduleConventions.empty CallPutSchedules.empty) |> should equal None
    economicCallFor (structuredCredit declaredConventions CallPutSchedules.empty) |> should equal None
    economicCallFor (structuredCredit ScheduleConventions.empty (callsOnly [ declaredCall ]))
    |> should equal (callTerms true None [ callEntry ] [])
    economicCallFor (structuredCredit ScheduleConventions.empty (putsOnly [ declaredPut ]))
    |> should equal (callTerms false None [] [ putEntry ])
    economicCallFor (structuredCredit declaredConventions ({ Calls = [ declaredCall ]; Puts = [ declaredPut ] } : CallPutSchedules))
    |> should equal (callTerms true None [ callEntry ] [ putEntry ])

[<Fact>]
let ``Economic call fill reproduces the scalar-only call terms exactly when no schedule is declared`` () =
    let scalarOnly isCallable firstCallDate =
        Some ({ IsCallable = isCallable
                FirstCallDate = firstCallDate
                CallPrice = None
                CallSchedule = []
                MakeWholeSpreadBps = None
                IsPuttable = false
                PutSchedule = [] } : EconomicCallTerms)

    economicCallFor (callableBond true (Some (DateOnly(2030, 1, 15))) CallPutSchedules.empty)
    |> should equal (scalarOnly true (Some (DateOnly(2030, 1, 15))))
    economicCallFor (scheduleBond ScheduleConventions.empty) |> should equal (scalarOnly false None)
    economicCallFor (callableDeposit (Some (DateOnly(2027, 3, 15))) CallPutSchedules.empty)
    |> should equal (scalarOnly true (Some (DateOnly(2027, 3, 15))))
    economicCallFor (scheduleDeposit ScheduleConventions.empty) |> should equal (scalarOnly false None)
    economicCallFor (scheduleStructuredCredit ScheduleConventions.empty) |> should equal None

[<Fact>]
let ``Reclassification proposal retains the conventions blocker after call and put schedules become writable`` () =
    let agencyBond conventions schedules =
        match securitizedBond BondSubclass.AgencyMbs (BondCouponStructure.Fixed(5.5m, None)) (Some 1000m) with
        | SecurityKind.Bond terms -> SecurityKind.Bond { terms with ScheduleConventions = conventions; CallPutSchedules = schedules }
        | other -> other
    let proposalFor conventions schedules =
        reclassificationFor (agencyBond conventions schedules)
        |> Option.defaultWith (fun () -> failwith "Expected a securitised bond to be a reclassification candidate")
    let messageOf code (proposal: SecurityMasterLegacyUpgrade.StructuredCreditReclassificationProposal) =
        (proposal.Blockers |> List.find (fun blocker -> blocker.Code = code)).Message

    let calls = proposalFor ScheduleConventions.empty (callsOnly [ firstCall ])
    blockerCodes calls
    |> should equal [
        "structured_credit_tranche_required"
        "structured_credit_collateral_type_required" ]
    blockerCodes calls |> List.contains "structured_credit_call_put_schedules_unwritable" |> should equal false
    calls.ProposedTerms |> should equal None
    // The draft still reports what the bond carries.
    calls.Draft.Kind |> should equal (Some StructuredCreditKind.AgencyMbs)

    // Conventions remain unrepresentable because the class has no issue-date/frequency anchors.
    let both = proposalFor { ScheduleConventions.empty with StubPolicy = Some "Short" } (putsOnly [ firstPut ])
    blockerCodes both
    |> should equal [
        "structured_credit_schedule_conventions_unwritable"
        "structured_credit_tranche_required"
        "structured_credit_collateral_type_required" ]
    messageOf "structured_credit_schedule_conventions_unwritable" both
    |> should equal "The bond declares scheduleConventions, but the StructuredCredit schema has no top-level issueDate or paymentFrequency; repair the declared schedule before reclassification."
    blockerCodes both |> List.contains "structured_credit_call_put_schedules_unwritable" |> should equal false
    both.ProposedTerms |> should equal None

    let plain = proposalFor ScheduleConventions.empty CallPutSchedules.empty
    blockerCodes plain |> should equal [ "structured_credit_tranche_required"; "structured_credit_collateral_type_required" ]
