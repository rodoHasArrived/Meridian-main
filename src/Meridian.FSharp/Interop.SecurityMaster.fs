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

    let kind =
        match identifier.Kind with
        | IdentifierKind.Ticker -> "Ticker"
        | IdentifierKind.Isin -> "Isin"
        | IdentifierKind.Cusip -> "Cusip"
        | IdentifierKind.Sedol -> "Sedol"
        | IdentifierKind.Figi -> "Figi"
        | IdentifierKind.OccOptionSymbol -> "OccOptionSymbol"
        | IdentifierKind.ProviderSymbol _ -> "ProviderSymbol"
        | IdentifierKind.InternalCode -> "InternalCode"
        | IdentifierKind.Lei -> "Lei"
        | IdentifierKind.PermId -> "PermId"
        | IdentifierKind.Bbgid -> "Bbgid"
        | IdentifierKind.Wkn -> "Wkn"
        | IdentifierKind.Valoren -> "Valoren"
        | IdentifierKind.PermTicker -> "PermTicker"
        | IdentifierKind.Ric -> "Ric"
        | IdentifierKind.Cik -> "Cik"
        | IdentifierKind.LoanX -> "LoanX"
        | IdentifierKind.Clip -> "Clip"
        | IdentifierKind.ListingId -> "ListingId"

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

    let unstampedAssetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.Equity terms ->
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

            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   shareClass = terms.ShareClass
                   votingRightsCat = terms.VotingRightsCat |> Option.map VotingRightsCat.asString
                   classification = classification
                   otherClassification = otherClassification
                   preferredTerms = preferredTerms
                   convertibleTerms = convertibleTerms |})
        | SecurityKind.Option terms ->
            let (SecurityId underlyingId) = terms.UnderlyingId
            let exerciseStyle =
                terms.ExerciseStyle
                |> Option.map (fun style ->
                    match style with
                    | ExerciseStyle.American -> "American"
                    | ExerciseStyle.European -> "European"
                    | ExerciseStyle.Bermudan -> "Bermudan")
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   underlyingId = underlyingId
                   putCall = terms.PutCall
                   strike = terms.Strike
                   expiry = terms.Expiry
                   multiplier = terms.Multiplier
                   optChainId = terms.OptChainId
                   exerciseStyle = exerciseStyle
                   settlementType = terms.SettlementType
                   isAdjusted = terms.IsAdjusted
                   lastTradingDt = terms.LastTradingDt |})
        | SecurityKind.Future terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   rootSymbol = terms.RootSymbol
                   contractMonth = terms.ContractMonth
                   expiry = terms.Expiry
                   multiplier = terms.Multiplier
                   lastTradingDt = terms.LastTradingDt
                   firstNoticeDt = terms.FirstNoticeDt
                   deliveryMonthDt = terms.DeliveryMonthDt
                   settlementType = terms.SettlementType
                   deliveryLocationCode = terms.DeliveryLocationCode
                   isRollTarget = terms.IsRollTarget
                   rollWindowDays = terms.RollWindowDays |})
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
            JsonSerializer.Serialize(
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
                   dayCount = BondTerms.dayCount terms
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
                               amount = entry.Amount |}) |})
        | SecurityKind.FxSpot terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   baseCurrency = terms.BaseCurrency
                   quoteCurrency = terms.QuoteCurrency |})
        | SecurityKind.Deposit terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   depositType = terms.DepositType
                   institutionName = terms.InstitutionName
                   maturity = terms.Maturity
                   interestRate = terms.InterestRate
                   dayCount = terms.DayCount
                   isCallable = terms.IsCallable |})
        | SecurityKind.MoneyMarketFund terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   fundFamily = terms.FundFamily
                   sweepEligible = terms.SweepEligible
                   weightedAverageMaturityDays = terms.WeightedAverageMaturityDays
                   liquidityFeeEligible = terms.LiquidityFeeEligible |})
        | SecurityKind.CertificateOfDeposit terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   couponRate = terms.CouponRate
                   callableDate = terms.CallableDate
                   dayCount = terms.DayCount |})
        | SecurityKind.CommercialPaper terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   maturity = terms.Maturity
                   discountRate = terms.DiscountRate
                   dayCount = terms.DayCount
                   isAssetBacked = terms.IsAssetBacked |})
        | SecurityKind.TreasuryBill terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   maturity = terms.Maturity
                   auctionDate = terms.AuctionDate
                   cusip = terms.CUSIP
                   discountRate = terms.DiscountRate |})
        | SecurityKind.Repo terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   counterparty = terms.Counterparty
                   startDate = terms.StartDate
                   endDate = terms.EndDate
                   repoRate = terms.RepoRate
                   collateralType = terms.CollateralType
                   haircut = terms.Haircut |})
        | SecurityKind.CashSweep terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   programName = terms.ProgramName
                   sweepVehicleType = terms.SweepVehicleType
                   sweepFrequency = terms.SweepFrequency
                   targetAccountType = terms.TargetAccountType
                   yieldRate = terms.YieldRate |})
        | SecurityKind.OtherSecurity terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   category = terms.Category
                   subType = terms.SubType
                   maturity = terms.Maturity
                   issuerName = terms.IssuerName
                   settlementType = terms.SettlementType |})
        | SecurityKind.Swap terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   effectiveDate = terms.EffectiveDate
                   maturityDate = terms.MaturityDate
                   legs =
                        terms.Legs
                        |> List.map (fun leg ->
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
                               dayCount = leg.DayCount
                               exchangesPrincipal = leg.ExchangesPrincipal
                               notionalSchedule =
                                   leg.NotionalSchedule
                                   |> List.map (fun step -> {| effectiveDate = step.EffectiveDate; notional = step.Notional |})
                               resetSchedule =
                                   leg.ResetSchedule
                                   |> List.map (fun entry -> {| resetDate = entry.ResetDate; fixingRate = entry.FixingRate |}) |}) |})
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
            JsonSerializer.Serialize(
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
                   nonCallPeriodEnd = terms.NonCallPeriodEnd |})
        | SecurityKind.PrivateFundInterest terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   gpSponsor = terms.GpSponsor
                   strategy = terms.Strategy
                   vintage = terms.Vintage
                   commitment = terms.Commitment
                   fundedAmount = terms.FundedAmount
                   unfundedAmount = terms.UnfundedAmount
                   navDate = terms.NavDate
                   lockup = terms.Lockup |})
        | SecurityKind.PrivateCompanyEquity terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuer = terms.Issuer
                   shareClass = terms.ShareClass
                   round = terms.Round
                   ownershipPercent = terms.OwnershipPercent
                   costBasis = terms.CostBasis
                   latestValuation = terms.LatestValuation
                   transferRestrictions = terms.TransferRestrictions |})
        | SecurityKind.RealEstateHolding terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   propertyType = terms.PropertyType
                   addressOrMarket = terms.AddressOrMarket
                   ownershipPercent = terms.OwnershipPercent
                   appraisalValue = terms.AppraisalValue
                   valuationDate = terms.ValuationDate
                   debtStack = terms.DebtStack
                   sponsor = terms.Sponsor |})
        | SecurityKind.CommitmentGuarantee terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   counterparty = terms.Counterparty
                   beneficiary = terms.Beneficiary
                   committedAmount = terms.CommittedAmount
                   unfundedAmount = terms.UnfundedAmount
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
        | SecurityKind.Commodity terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   commodityType = terms.CommodityType
                   denomination = terms.Denomination
                   contractSize = terms.ContractSize |})
        | SecurityKind.CryptoCurrency terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   baseCurrency = terms.BaseCurrency
                   quoteCurrency = terms.QuoteCurrency
                   network = terms.Network |})
        | SecurityKind.Cfd terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   underlyingAssetClass = terms.UnderlyingAssetClass
                   underlyingDescription = terms.UnderlyingDescription
                   leverage = terms.Leverage |})
        | SecurityKind.Warrant terms ->
            let (SecurityId underlyingId) = terms.UnderlyingId
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   underlyingId = underlyingId
                   warrantType = terms.WarrantType
                   strike = terms.Strike
                   expiry = terms.Expiry
                   multiplier = terms.Multiplier |})
        | SecurityKind.FxForward terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   baseCurrency = terms.BaseCurrency
                   quoteCurrency = terms.QuoteCurrency
                   notional = terms.Notional
                   forwardRate = terms.ForwardRate
                   tradeDate = terms.TradeDate
                   valueDate = terms.ValueDate
                   settlementType = terms.SettlementType |})
        | SecurityKind.NonDeliverableForward terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   baseCurrency = terms.BaseCurrency
                   quoteCurrency = terms.QuoteCurrency
                   settlementCurrency = terms.SettlementCurrency
                   notional = terms.Notional
                   forwardRate = terms.ForwardRate
                   fixingDate = terms.FixingDate
                   valueDate = terms.ValueDate
                   fixingSource = terms.FixingSource |})
        | SecurityKind.CreditDefaultSwap terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   referenceEntity = terms.ReferenceEntity
                   referenceObligation = terms.ReferenceObligation
                   protectionSide = terms.ProtectionSide
                   notional = terms.Notional
                   fixedCouponBps = terms.FixedCouponBps
                   effectiveDate = terms.EffectiveDate
                   maturityDate = terms.MaturityDate
                   paymentFrequency = terms.PaymentFrequency
                   dayCount = terms.DayCount
                   seniority = terms.Seniority
                   restructuringClause = terms.RestructuringClause
                   upfrontPoints = terms.UpfrontPoints
                   recoveryRateAssumption = terms.RecoveryRateAssumption
                   indexName = terms.IndexName |})
        | SecurityKind.Swaption terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   payerReceiver = terms.PayerReceiver
                   exerciseStyle = terms.ExerciseStyle
                   strikeRate = terms.StrikeRate
                   expiry = terms.Expiry
                   underlyingEffectiveDate = terms.UnderlyingEffectiveDate
                   underlyingMaturityDate = terms.UnderlyingMaturityDate
                   notional = terms.Notional
                   floatingIndex = terms.FloatingIndex
                   fixedLegPaymentFrequency = terms.FixedLegPaymentFrequency
                   fixedLegDayCount = terms.FixedLegDayCount
                   settlementType = terms.SettlementType |})
        | SecurityKind.CapFloor terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   capFloorType = terms.CapFloorType
                   referenceIndex = terms.ReferenceIndex
                   capStrike = terms.CapStrike
                   floorStrike = terms.FloorStrike
                   notional = terms.Notional
                   effectiveDate = terms.EffectiveDate
                   maturityDate = terms.MaturityDate
                   resetFrequency = terms.ResetFrequency
                   dayCount = terms.DayCount
                   premiumAmount = terms.PremiumAmount |})
        | SecurityKind.StructuredNote terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   issuerName = terms.IssuerName
                   underlyingDescription = terms.UnderlyingDescription
                   maturity = terms.Maturity
                   issueDate = terms.IssueDate
                   denomination = terms.Denomination
                   principalProtectionPct = terms.PrincipalProtectionPct
                   participationRate = terms.ParticipationRate
                   returnCap = terms.ReturnCap
                   couponRate = terms.CouponRate
                   paymentFrequency = terms.PaymentFrequency
                   barrierLevel = terms.BarrierLevel |})
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
    let assetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.CustomAsset _ -> unstampedAssetSpecificTermsJson
        | _ -> SecurityTermsEnvelope.StampFlat(unstampedAssetSpecificTermsJson, assetClass)

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
