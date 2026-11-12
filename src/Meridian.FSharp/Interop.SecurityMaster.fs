namespace Meridian.FSharp.SecurityMasterInterop

open System
open System.Text.Json
open Meridian.Contracts.SecurityMaster
open Meridian.FSharp.Domain

/// Private helpers for F# option → C# nullable conversion, mirroring the helpers in Interop.fs.
[<AutoOpen>]
module private NullableHelpers =
    let inline toNullable (opt: 'T option) : Nullable<'T> =
        match opt with
        | Some v -> Nullable v
        | None -> Nullable()

    let inline toNullableRef (opt: 'T option) : 'T =
        match opt with
        | Some v -> v
        | None -> Unchecked.defaultof<'T>

/// The asset-class terms version a flat payload is written at (ADR-024, readable before writable):
/// the class's S2 document floor, raised to the highest version among the versioned terms the record
/// holds (scheduleConventions; callSchedule and putSchedule). A record read from a node that writes a
/// newer version (a bond stamped 3 with declared calls) therefore re-serializes with its terms and
/// stamp; a record that declares nothing preserves its pre-contract floor and bytes.
[<RequireQualifiedAccess>]
module internal SecurityTermsWriteVersion =
    let private scheduleConventionsKey = "scheduleConventions"
    let private callScheduleKey = "callSchedule"
    let private putScheduleKey = "putSchedule"

    let private fieldSince (assetClass: string) (key: string) =
        match SecurityAssetTermsSchema.Field(assetClass, key) with
        | null -> invalidOp (sprintf "The term schema declares no '%s' on '%s'; the flat writer and the schema have drifted." key assetClass)
        | field -> field.SinceTermsVersion

    let private elementFieldSince (assetClass: string) (container: string) (key: string) =
        match SecurityAssetTermsSchema.ElementFields(assetClass, container) |> Seq.tryFind (fun field -> field.Key = key) with
        | Some field -> field.SinceTermsVersion
        | None -> invalidOp (sprintf "The term schema declares no '%s.%s' on '%s'; the flat writer and the schema have drifted." container key assetClass)

    /// The terms version that introduced the scheduleConventions block on the record's class (on a
    /// swap, on each leg).
    let blockSince (kind: SecurityKind) =
        let assetClass = SecurityKind.assetClass kind
        match kind with
        | SecurityKind.Swap _ -> elementFieldSince assetClass "legs" scheduleConventionsKey
        | _ -> fieldSince assetClass scheduleConventionsKey

    /// The terms version that introduced the callSchedule and putSchedule arrays on the record's
    /// class. The writer emits both together, in the tier above scheduleConventions.
    let schedulesSince (kind: SecurityKind) =
        let assetClass = SecurityKind.assetClass kind
        match kind with
        | SecurityKind.Bond _
        | SecurityKind.CertificateOfDeposit _
        | SecurityKind.StructuredCredit _ ->
            let calls = fieldSince assetClass callScheduleKey
            let puts = fieldSince assetClass putScheduleKey
            if calls <> puts || calls <= blockSince kind then
                invalidOp
                    (sprintf
                        "The flat writer emits callSchedule and putSchedule together, in the tier above scheduleConventions, but '%s' declares them at versions %d and %d; the schema has drifted."
                        assetClass
                        calls
                        puts)
            calls
        | _ -> invalidOp (sprintf "'%s' declares no call or put schedules." assetClass)

    let private declares (conventions: ScheduleConventions) = not (ScheduleConventions.isEmpty conventions)

    let private holdsSchedules (schedules: CallPutSchedules) = not (CallPutSchedules.isEmpty schedules)

    /// The version the record's flat payload is written at: its S2 document floor, raised to the
    /// highest version among the versioned terms the record holds.
    let effective (kind: SecurityKind) =
        let floor = SecurityAssetClassCatalog.GetStampFloor(SecurityKind.assetClass kind)
        // Every versioned term the record can hold, with whether it holds it.
        let held =
            match kind with
            | SecurityKind.Bond terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.CertificateOfDeposit terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.StructuredCredit terms ->
                [ declares terms.ScheduleConventions, blockSince kind
                  holdsSchedules terms.CallPutSchedules, schedulesSince kind ]
            | SecurityKind.Swap terms ->
                [ terms.Legs |> List.exists (fun leg -> declares leg.ScheduleConventions), blockSince kind ]
            | _ -> []
        let rateConvention =
            match kind with
            | SecurityKind.Deposit terms -> terms.RateConvention
            | SecurityKind.CertificateOfDeposit terms -> terms.RateConvention
            | SecurityKind.CommercialPaper terms -> terms.RateConvention
            | SecurityKind.TreasuryBill terms -> terms.RateConvention
            | SecurityKind.Repo terms -> terms.RateConvention
            | SecurityKind.CashSweep terms -> terms.RateConvention
            | SecurityKind.DirectLoan terms -> terms.RateConvention
            | SecurityKind.StructuredCredit terms -> terms.RateConvention
            | SecurityKind.Swap terms -> terms.RateConvention
            | SecurityKind.Swaption terms -> terms.RateConvention
            | SecurityKind.CapFloor terms -> terms.RateConvention
            | SecurityKind.StructuredNote terms -> terms.RateConvention
            | _ -> None
        let declaredFloor =
            if rateConvention.IsSome then max floor (fieldSince (SecurityKind.assetClass kind) "rateUnit")
            else floor
        held |> List.filter fst |> List.fold (fun version (_, since) -> max version since) declaredFloor

    /// The canonical written block (frozen by ADR-024): null when nothing is declared, otherwise all
    /// twelve members in alphabetical order, with null or [] for an unset one.
    let conventionsPayload (conventions: ScheduleConventions) =
        if ScheduleConventions.isEmpty conventions then
            None
        else
            Some
                {| accrualAdjusted = conventions.AccrualAdjusted
                   businessDayConvention = conventions.BusinessDayConvention
                   couponCashBasis = conventions.CouponCashBasis
                   couponRateUnit = conventions.CouponRateUnit
                   direction = conventions.Direction
                   endOfMonthRule = conventions.EndOfMonthRule
                   firstRegularDate = conventions.FirstRegularDate
                   lastRegularDate = conventions.LastRegularDate
                   paymentCalendars = conventions.PaymentCalendars
                   paymentLagBusinessDays = conventions.PaymentLagBusinessDays
                   rollConvention = conventions.RollConvention
                   stubPolicy = conventions.StubPolicy |}

    /// The canonical written call schedule (frozen by ADR-024): one object per entry in stored order,
    /// carrying every member in alphabetical order with null for an unset one; [] when none is declared.
    let callSchedulePayload (calls: CallScheduleTerm list) =
        calls
        |> List.map (fun call ->
            {| callDate = call.CallDate
               callEndDate = call.CallEndDate
               callPrice = call.CallPrice
               exerciseFrequency = call.ExerciseFrequency
               exerciseStyle = call.ExerciseStyle
               makeWholeSpreadBps = call.MakeWholeSpreadBps
               maximumNoticeDays = call.MaximumNoticeDays
               minimumNoticeDays = call.MinimumNoticeDays
               noticeDayType = call.NoticeDayType
               partialRedemption = call.PartialRedemption
               priceBasis = call.PriceBasis
               priceType = call.PriceType |})

    /// The canonical written put schedule, under the same rules as the call schedule.
    let putSchedulePayload (puts: PutScheduleTerm list) =
        puts
        |> List.map (fun put ->
            {| exerciseFrequency = put.ExerciseFrequency
               exerciseStyle = put.ExerciseStyle
               maximumNoticeDays = put.MaximumNoticeDays
               minimumNoticeDays = put.MinimumNoticeDays
               noticeDayType = put.NoticeDayType
               priceBasis = put.PriceBasis
               putDate = put.PutDate
               putEndDate = put.PutEndDate
               putPrice = put.PutPrice |})

/// One lossless typed equity serializer shared by the economic document and flat codec.
module EquityCapitalCodec =
    let toPayload (terms: EquityCapitalTerms) =
        let preferredTermsToLegacy (preferred: PreferredTerms) =
            let liquidationKind, liquidationMultiple =
                match preferred.LiquidationPreference with
                | LiquidationPreference.Pari -> "Pari", None
                | LiquidationPreference.Senior multiple -> "Senior", Some multiple
                | LiquidationPreference.Subordinated -> "Subordinated", None

            {| dividendRate = preferred.DividendRate
               dividendType = preferred.DividendType |> DividendType.asString
               redemptionPrice = preferred.RedemptionPrice
               redemptionDate = preferred.RedemptionDate
               callableDate = preferred.CallableDate
               participationTerms =
                    preferred.ParticipationTerms
                    |> Option.map (fun terms ->
                        {| participatesInCommonDividends = terms.ParticipatesInCommonDividends
                           additionalDividendThreshold = terms.AdditionalDividendThreshold |})
               liquidationPreference =
                    {| kind = liquidationKind
                       multiple = liquidationMultiple |} |}

        let convertibleTermsToLegacy (convertible: ConvertibleTerms) =
            let (SecurityId underlyingSecurityId) = convertible.UnderlyingSecurityId
            {| underlyingSecurityId = underlyingSecurityId
               conversionRatio = convertible.ConversionRatio
               conversionPrice = convertible.ConversionPrice
               conversionStartDate = convertible.ConversionStartDate
               conversionEndDate = convertible.ConversionEndDate |}

        // "Other" classifications serialize as the discriminant "Other" plus the raw label in
        // otherClassification — writing the raw label into the classification slot produced a
        // value the deserializer did not recognize, failing every read of the row.
        let classification, otherClassification =
            match terms.Classification with
            | Some (EquityClassification.Other label) -> Some "Other", Some label
            | Some value -> Some (EquityClassification.asString value), None
            | None -> None, None

        let preferredTerms, convertibleTerms =
            match terms.Classification with
            | Some (EquityClassification.Preferred preferred) ->
                Some (preferredTermsToLegacy preferred), None
            | Some (EquityClassification.Convertible convertible) ->
                None, Some (convertibleTermsToLegacy convertible)
            | Some (EquityClassification.ConvertiblePreferred (preferred, convertible)) ->
                Some (preferredTermsToLegacy preferred), Some (convertibleTermsToLegacy convertible)
            | _ ->
                None, None

        {| shareClass = terms.ShareClass
           votingRightsCat = terms.VotingRightsCat |> Option.map VotingRightsCat.asString
           classification = classification
           otherClassification = otherClassification
           preferredTerms = preferredTerms
           convertibleTerms = convertibleTerms |}

    let toJson (terms: EquityCapitalTerms) = JsonSerializer.Serialize(toPayload terms)

/// Exact typed tranche serialization; the flat codec keeps its existing field spellings.
module TrancheContractCodec =
    let toJson (terms: TrancheContractTerms) =
        JsonSerializer.Serialize(
            {| structuredCreditKind = terms.Kind |> Option.map StructuredCreditKind.asString
               seniority = terms.Seniority
               attachmentPoint = terms.AttachmentPoint
               detachmentPoint = terms.DetachmentPoint
               reinvestmentPeriodEnd = terms.ReinvestmentPeriodEnd
               nonCallPeriodEnd = terms.NonCallPeriodEnd |})

type SecurityIdentifierSnapshot(identifier: Identifier) =
    do
        if not (SecurityIdentifier.providerMetadataMatchesKind identifier) then
            invalidArg
                (nameof identifier.Provider)
                "ProviderSymbol metadata must match the authoritative provider namespace carried by Identifier.Kind."

    let provider =
        match identifier.Kind with
        | IdentifierKind.ProviderSymbol authoritativeProvider -> authoritativeProvider
        | _ -> identifier.Provider |> toNullableRef

    let kind = SecurityIdentifier.kindName identifier

    member _.Kind = kind
    member _.Value = identifier.Value
    member _.IsPrimary = identifier.IsPrimary
    member _.ValidFrom = identifier.ValidFrom
    member _.ValidTo = toNullable identifier.ValidTo
    member _.Provider = provider

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterSnapshotWrapper(record: SecurityMasterRecord) =
    let schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms

    // Idea 2: delegate to the domain function instead of duplicating the match.
    let assetClass = SecurityKind.assetClass record.Kind

    // The version this record's flat payload is written at, and which versioned keys it carries:
    // from a term's version upward its key is always present (the block null when nothing is
    // declared, the schedules [] when none is declared: ADR-024's frozen written form); below it the
    // key is absent.
    let termsVersion = SecurityTermsWriteVersion.effective record.Kind

    let writesScheduleConventions () =
        termsVersion >= SecurityTermsWriteVersion.blockSince record.Kind

    // Called only for the classes that declare the schedules.
    let writesCallPutSchedules () =
        termsVersion >= SecurityTermsWriteVersion.schedulesSince record.Kind

    let unstampedAssetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.Equity terms ->
            let payload = EquityCapitalCodec.toPayload terms.Capital
            JsonSerializer.Serialize({| payload with schemaVersion = schemaVersion |})
        | SecurityKind.Option terms -> GeneratedFlatWriter.writeOption schemaVersion terms
        | SecurityKind.Future terms -> GeneratedFlatWriter.writeFuture schemaVersion terms
        | SecurityKind.Bond terms ->
            let couponType, floatingIndex, spreadBps, capRate, floorRate =
                match terms.Coupon with
                | BondCouponStructure.Fixed _ -> "Fixed", None, None, None, None
                | BondCouponStructure.Floating(index, spread, cap, floor, _) -> "Floating", Some index, spread, cap, floor
                | BondCouponStructure.ZeroCoupon -> "ZeroCoupon", None, None, None, None
                | BondCouponStructure.Step _ -> "Step", None, None, None, None
                | BondCouponStructure.InflationLinked _ -> "InflationLinked", None, None, None, None
            // The dated step schedule and inflation indexation are coupon-structure payload: an
            // empty array / null asserts "not a step / not inflation-linked" so the field set
            // stays constant across coupon variants (the round-trip guard's exact-match contract).
            let stepSchedule =
                match terms.Coupon with
                | BondCouponStructure.Step(schedule, _) ->
                    schedule
                    |> List.map (fun entry ->
                        {| effectiveDate = entry.EffectiveDate
                           rate = entry.Rate |})
                | _ -> []
            let inflationIndex, inflationBaseIndexValue, inflationIndexRatio =
                match terms.Coupon with
                | BondCouponStructure.InflationLinked(_, indexName, baseIndexValue, indexRatio, _) ->
                    Some indexName, baseIndexValue, indexRatio
                | _ -> None, None, None
            let payload =
                {| schemaVersion = schemaVersion
                   maturity = terms.Maturity
                   issueDate = terms.IssueDate
                   couponType = couponType
                   couponRate = BondTerms.couponRate terms
                   floatingIndex = floatingIndex
                   spreadBps = spreadBps
                   capRate = capRate
                   floorRate = floorRate
                   stepSchedule = stepSchedule
                   inflationIndex = inflationIndex
                   inflationBaseIndexValue = inflationBaseIndexValue
                   inflationIndexRatio = inflationIndexRatio
                   dayCount = BondTerms.dayCount terms |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
                   isCallable = terms.IsCallable
                   callDate = terms.CallDate
                   issuerName = terms.IssuerName
                   seniority = terms.Seniority
                   subclass = BondSubclass.label terms.Subclass
                   par = terms.Par
                   paymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.label
                   legalFinalMaturity = terms.LegalFinalMaturity
                   preRefundDate = terms.PreRefundDate
                   mandatoryPutDate = terms.MandatoryPutDate
                   principalSchedule =
                        terms.PrincipalSchedule
                        |> List.map (fun entry ->
                            {| paymentDate = entry.PaymentDate
                               amount = entry.Amount |}) |}
            if writesCallPutSchedules () then
                JsonSerializer.Serialize(
                    {| payload with
                         scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                         callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                         putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
            elif writesScheduleConventions () then
                JsonSerializer.Serialize(
                    {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
            else
                JsonSerializer.Serialize(payload)
        | SecurityKind.FxSpot terms -> GeneratedFlatWriter.writeFxSpot schemaVersion terms
        | SecurityKind.Deposit terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   depositType = terms.DepositType
                   institutionName = terms.InstitutionName
                   maturity = terms.Maturity
                   interestRate = terms.InterestRate
                   dayCount = terms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
                   isCallable = terms.IsCallable |})
        | SecurityKind.MoneyMarketFund terms -> GeneratedFlatWriter.writeMoneyMarketFund schemaVersion terms
        | SecurityKind.CertificateOfDeposit terms ->
            let payload =
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   couponRate = terms.CouponRate
                   callableDate = terms.CallableDate
                   dayCount = terms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel |}
            if writesCallPutSchedules () then
                JsonSerializer.Serialize(
                    {| payload with
                         scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                         callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                         putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
            elif writesScheduleConventions () then
                JsonSerializer.Serialize(
                    {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
            else
                JsonSerializer.Serialize(payload)
        | SecurityKind.CommercialPaper terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   discountRate = terms.DiscountRate
                   dayCount = terms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
                   isAssetBacked = terms.IsAssetBacked |})
        | SecurityKind.TreasuryBill terms -> GeneratedFlatWriter.writeTreasuryBill schemaVersion terms
        | SecurityKind.Repo terms -> GeneratedFlatWriter.writeRepo schemaVersion terms
        | SecurityKind.CashSweep terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   programName = terms.ProgramName
                   sweepVehicleType = terms.SweepVehicleType
                   sweepFrequency = terms.SweepFrequency
                   targetAccountType = terms.TargetAccountType
                   yieldRate = terms.YieldRate |})
        | SecurityKind.OtherSecurity terms -> GeneratedFlatWriter.writeOtherSecurity schemaVersion terms
        | SecurityKind.Swap terms ->
            let legPayload (leg: SwapLeg) =
                {| legId = leg.LegId
                   legType = leg.LegType
                   currency = leg.Currency
                   direction = leg.Direction
                   index = leg.Index
                   fixedRate = leg.FixedRate
                   spreadBps = leg.SpreadBps
                   currentIndexRate = leg.CurrentIndexRate
                   notional = leg.Notional
                   paymentFrequency = leg.PaymentFrequency
                   dayCount = leg.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
                   exchangesPrincipal = leg.ExchangesPrincipal
                   notionalSchedule =
                       leg.NotionalSchedule
                       |> List.map (fun step -> {| effectiveDate = step.EffectiveDate; notional = step.Notional |})
                   resetSchedule =
                       leg.ResetSchedule
                       |> List.map (fun entry -> {| resetDate = entry.ResetDate; fixingRate = entry.FixingRate |}) |}
            if writesScheduleConventions () then
                JsonSerializer.Serialize(
                    {| schemaVersion = schemaVersion
                       effectiveDate = terms.EffectiveDate
                       maturityDate = terms.MaturityDate
                       legs =
                           terms.Legs
                           |> List.map (fun leg ->
                               {| legPayload leg with
                                    scheduleConventions = SecurityTermsWriteVersion.conventionsPayload leg.ScheduleConventions |}) |})
            else
                JsonSerializer.Serialize(
                    {| schemaVersion = schemaVersion
                       effectiveDate = terms.EffectiveDate
                       maturityDate = terms.MaturityDate
                       legs = terms.Legs |> List.map legPayload |})
        | SecurityKind.DirectLoan terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   borrower = terms.Borrower
                   maturity = terms.Maturity
                   referenceIndex = terms.ReferenceIndex
                   spreadBps = terms.SpreadBps
                   currentCouponRate = terms.CurrentCouponRate
                   resetFrequency = terms.ResetFrequency
                   pricingSource = terms.PricingSource
                   covenants =
                        terms.Covenants
                        |> List.map (fun covenant ->
                            {| covenantType = covenant.CovenantType
                               threshold = covenant.Threshold
                               notes = covenant.Notes |})
                   principalSchedule =
                        terms.PrincipalSchedule
                        |> List.map (fun entry ->
                            {| paymentDate = entry.PaymentDate
                               amount = entry.Amount |}) |})
        | SecurityKind.StructuredCredit terms ->
            let payload =
                {| schemaVersion = schemaVersion
                   tranche = terms.Tranche
                   poolId = terms.PoolId
                   collateralType = terms.CollateralType
                   originalFace = terms.OriginalFace
                   currentFactor = terms.CurrentFactor
                   couponOrIndex = terms.CouponOrIndex
                   factorSchedule = terms.FactorSchedule
                   factorScheduleEntries =
                        terms.FactorScheduleEntries
                        |> List.map (fun entry ->
                            {| asOfDate = entry.AsOfDate
                               factor = entry.Factor |})
                   maturity = terms.Maturity
                   structuredCreditKind = terms.Kind |> Option.map StructuredCreditKind.asString
                   seniority = terms.Seniority
                   attachmentPoint = terms.AttachmentPoint
                   detachmentPoint = terms.DetachmentPoint
                   weightedAverageCoupon = terms.WeightedAverageCoupon
                   weightedAverageMaturityMonths = terms.WeightedAverageMaturityMonths
                   weightedAverageLoanAgeMonths = terms.WeightedAverageLoanAgeMonths
                   reinvestmentPeriodEnd = terms.ReinvestmentPeriodEnd
                   nonCallPeriodEnd = terms.NonCallPeriodEnd |}
            if writesCallPutSchedules () then
                JsonSerializer.Serialize(
                    {| payload with
                         scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                         callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                         putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
            elif writesScheduleConventions () then
                JsonSerializer.Serialize(
                    {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
            else
                JsonSerializer.Serialize(payload)
        | SecurityKind.PrivateFundInterest terms -> GeneratedFlatWriter.writePrivateFundInterest schemaVersion terms
        | SecurityKind.PrivateCompanyEquity terms -> GeneratedFlatWriter.writePrivateCompanyEquity schemaVersion terms
        | SecurityKind.RealEstateHolding terms -> GeneratedFlatWriter.writeRealEstateHolding schemaVersion terms
        | SecurityKind.CommitmentGuarantee terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   counterparty = terms.Counterparty
                   beneficiary = terms.Beneficiary
                   committedAmount = (terms.Notional.CommittedAmount |> Option.get)
                   unfundedAmount = terms.Notional.UnfundedAmount
                   effectiveDate = terms.EffectiveDate
                   expiryDate = terms.ExpiryDate
                   feeRate = terms.FeeRate
                   collateral = terms.Collateral
                   covenants =
                        terms.Covenants
                        |> List.map (fun covenant ->
                            {| covenantType = covenant.CovenantType
                               threshold = covenant.Threshold
                               notes = covenant.Notes |}) |})
        | SecurityKind.Commodity terms -> GeneratedFlatWriter.writeCommodity schemaVersion terms
        | SecurityKind.CryptoCurrency terms -> GeneratedFlatWriter.writeCryptoCurrency schemaVersion terms
        | SecurityKind.Cfd terms -> GeneratedFlatWriter.writeCfd schemaVersion terms
        | SecurityKind.Warrant terms -> GeneratedFlatWriter.writeWarrant schemaVersion terms
        | SecurityKind.FxForward terms -> GeneratedFlatWriter.writeFxForward schemaVersion terms
        | SecurityKind.NonDeliverableForward terms -> GeneratedFlatWriter.writeNonDeliverableForward schemaVersion terms
        | SecurityKind.CreditDefaultSwap terms -> GeneratedFlatWriter.writeCreditDefaultSwap schemaVersion terms
        | SecurityKind.Swaption terms -> GeneratedFlatWriter.writeSwaption schemaVersion terms
        | SecurityKind.CapFloor terms -> GeneratedFlatWriter.writeCapFloor schemaVersion terms
        | SecurityKind.StructuredNote terms -> GeneratedFlatWriter.writeStructuredNote schemaVersion terms
        | SecurityKind.InvestmentFund terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   fundType = terms.FundType
                   fundFamily = terms.FundFamily
                   navCurrency = terms.NavCurrency
                   distributionPolicy = terms.DistributionPolicy |> Option.map DistributionPolicy.label
                   isStableNav = terms.IsStableNav
                   pricingSource = terms.PricingSource |})
        | SecurityKind.CustomAsset terms ->
            // The custom-asset document is emitted verbatim: it already carries its own
            // schemaVersion (the CustomAssetProfile family) and its dynamic, profile-governed
            // keys must survive serialize → deserialize → serialize without loss.
            terms.TermsJson

    // Flat payloads are stamped with their format and asset-class terms version so readers
    // dispatch on what the document declares rather than on the shared schemaVersion integer.
    // The custom-asset profile document is the one exception: it is emitted verbatim.
    let baseAssetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.CustomAsset _ -> unstampedAssetSpecificTermsJson
        | _ -> SecurityTermsEnvelope.StampFlat(unstampedAssetSpecificTermsJson, assetClass, termsVersion)

    let assetSpecificTermsJson =
        let unit =
            match record.Kind with
            | SecurityKind.Swap terms -> terms.RateUnit
            | SecurityKind.Deposit terms -> terms.RateUnit
            | SecurityKind.CertificateOfDeposit terms -> terms.RateUnit
            | SecurityKind.CommercialPaper terms -> terms.RateUnit
            | SecurityKind.TreasuryBill terms -> terms.RateUnit
            | SecurityKind.Repo terms -> terms.RateUnit
            | SecurityKind.CashSweep terms -> terms.RateUnit
            | SecurityKind.DirectLoan terms -> terms.RateUnit
            | SecurityKind.StructuredCredit terms -> terms.RateUnit
            | SecurityKind.Swaption terms -> terms.RateUnit
            | SecurityKind.CapFloor terms -> terms.RateUnit
            | SecurityKind.StructuredNote terms -> terms.RateUnit
            | _ -> None
        match unit with
        | None -> baseAssetSpecificTermsJson
        | Some token ->
            let document = System.Text.Json.Nodes.JsonNode.Parse(baseAssetSpecificTermsJson).AsObject()
            document["rateUnit"] <- System.Text.Json.Nodes.JsonValue.Create(token)
            let version = SecurityAssetTermsSchema.Field(assetClass, "rateUnit").SinceTermsVersion
            SecurityTermsEnvelope.StampFlat(document.ToJsonString(), assetClass, max version termsVersion)

    let commonTermsJson =
        JsonSerializer.Serialize(
            {| displayName = record.Common.DisplayName
               currency = record.Common.Currency
               countryOfRisk = record.Common.CountryOfRisk
               issuerName = record.Common.IssuerName
               exchange = record.Common.Exchange
               lotSize = record.Common.LotSize
               tickSize = record.Common.TickSize
               primaryListingMic = record.Common.PrimaryListingMic
               countryOfIncorporation = record.Common.CountryOfIncorporation
               settlementCycleDays = record.Common.SettlementCycleDays
               holidayCalendarId = record.Common.HolidayCalendarId |})

    let provenanceJson =
        JsonSerializer.Serialize(
            {| sourceSystem = record.Provenance.SourceSystem
               sourceRecordId = record.Provenance.SourceRecordId
               asOf = record.Provenance.AsOf
               updatedBy = record.Provenance.UpdatedBy
               reason = record.Provenance.Reason |})

    // Idea 2: delegate to SecurityMasterRecord.primaryIdentifier instead of duplicating List.tryFind.
    let primaryIdentifier = SecurityMasterRecord.primaryIdentifier record

    member _.Record = record
    member _.SecurityId = let (SecurityId id) = record.SecurityId in id
    member _.AssetClass = assetClass
    // Idea 2: delegate to SecurityStatus.asString instead of inline if/else.
    member _.Status = SecurityStatus.asString record.Status
    member _.DisplayName = record.Common.DisplayName
    member _.Currency = record.Common.Currency
    member _.CommonTermsJson = commonTermsJson
    member _.AssetSpecificTermsJson = assetSpecificTermsJson
    member _.ProvenanceJson = provenanceJson
    member _.Version = record.Version
    member _.EffectiveFrom = record.EffectiveFrom
    // Idea 8: use NullableHelpers.toNullable instead of inline match.
    member _.EffectiveTo = toNullable record.EffectiveTo
    member _.PrimaryIdentifierKind =
        primaryIdentifier
        |> Option.map (fun identifier -> SecurityIdentifierSnapshot(identifier).Kind)
        |> Option.defaultValue String.Empty
    member _.PrimaryIdentifierValue =
        primaryIdentifier
        |> Option.map (fun identifier -> identifier.Value)
        |> Option.defaultValue String.Empty
    member _.Identifiers =
        record.Identifiers
        |> List.map SecurityIdentifierSnapshot
        |> List.toArray

/// C#-friendly DTO that carries both the error code and human-readable message
/// from a SecurityMaster validation failure.
[<Sealed>]
type SecurityValidationErrorDto(code: string, message: string) =
    member _.Code = code
    member _.Message = message

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterCommandResultWrapper(result: Result<SecurityMasterRecord, SecurityValidationError list>) =
    member _.IsSuccess =
        match result with
        | Ok _ -> true
        | Error _ -> false

    member _.Snapshot =
        match result with
        | Ok record -> SecurityMasterSnapshotWrapper(record)
        | Error _ -> null

    /// Error messages only (kept for backward compatibility).
    member _.Errors =
        match result with
        | Ok _ -> Array.empty
        | Error errors -> errors |> List.map (fun error -> error.Message) |> List.toArray

    // Idea 3: expose both code and message so callers can distinguish error kinds.
    member _.ErrorDetails =
        match result with
        | Ok _ -> Array.empty
        | Error errors ->
            errors
            |> List.map (fun e -> SecurityValidationErrorDto(e.Code, e.Message))
            |> List.toArray

// Idea 6: renamed from SecurityMasterAggregate to SecurityMasterCommandFacade to
// reflect that this is a stateless facade over the aggregate command functions,
// not a stateful aggregate object.
[<Sealed>]
type SecurityMasterCommandFacade private () =
    static member private ToResult(initialState: SecurityMasterRecord option, events: Result<SecurityMasterEvent list, SecurityValidationError list>) =
        match events with
        | Error errors -> SecurityMasterCommandResultWrapper(Error errors)
        | Ok eventList ->
            let finalState = eventList |> List.fold SecurityMaster.evolve initialState
            match finalState with
            | Some record -> SecurityMasterCommandResultWrapper(Ok record)
            | None ->
                SecurityMasterCommandResultWrapper(
                    Error [ {
                        Code = "missing_state"
                        Message = "Security master command produced no aggregate state."
                    } ])

    static member Create(command: CreateSecurity) =
        SecurityMasterCommandFacade.ToResult(None, SecurityMaster.create command)

    static member Amend(current: SecurityMasterRecord, command: AmendTerms) =
        SecurityMasterCommandFacade.ToResult(Some current, SecurityMaster.amend current command)

    static member Deactivate(current: SecurityMasterRecord, command: DeactivateSecurity) =
        SecurityMasterCommandFacade.ToResult(Some current, SecurityMaster.deactivate current command)

    /// Standalone kind-invariant validation for the C# write path: a profile-backed record
    /// reclassified to its resolved first-class kind must also satisfy that kind's rule set,
    /// which can be stricter than the pinned profile's.
    static member ValidateKindInvariants(kind: SecurityKind) =
        SecurityMaster.validateKindInvariants kind |> List.toArray

    /// ADR-027 stage 2 (S2-B1): the module-expressible subset of the kind rule set, evaluated
    /// over the economic definition's modules and gated by the classification's canonical
    /// TypeName. The stage-2 equivalence tests pin, per class, that this agrees code-for-code
    /// with ValidateKindInvariants on the codes DefinitionExpressibleCodes lists; from S2-B2 the
    /// command write path validates through it.
    static member ValidateDefinitionInvariants(typeName: string, modules: SecurityTermModules) =
        SecurityMaster.validateDefinitionInvariants typeName modules |> List.toArray

    /// Per-class ledger of the term-rule error codes, each of which the definition surface
    /// expresses (complete since the stage-3 contraction: no code is codec-only).
    static member DefinitionExpressibleCodes =
        SecurityMaster.definitionExpressibleCodes
        |> Map.toSeq
        |> Seq.map (fun (typeName, codes) -> System.Collections.Generic.KeyValuePair(typeName, codes |> List.toArray))
        |> fun pairs -> System.Collections.Generic.Dictionary<string, string[]>(pairs) :> System.Collections.Generic.IReadOnlyDictionary<string, string[]>
