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

    let assetSpecificTermsJson =
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

            let classification =
                terms.Classification |> Option.map EquityClassification.asString

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
                   classification = classification
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
                   dayCount = BondTerms.dayCount terms
                   isCallable = terms.IsCallable
                   callDate = terms.CallDate
                   issuerName = terms.IssuerName
                   seniority = terms.Seniority
                   subclass = terms.Subclass.ToString()
                   par = terms.Par
                   paymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.label
                   legalFinalMaturity = terms.LegalFinalMaturity
                   preRefundDate = terms.PreRefundDate
                   mandatoryPutDate = terms.MandatoryPutDate |})
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
                   // Key spellings are the primary aliases StructuredCashFlowTermsResolver probes
                   // for. A leg written here must be readable by that resolver without falling
                   // back to a vendor alias, so any rename must move in lock-step with it.
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
                               exchangesPrincipal = leg.ExchangesPrincipal |}) |})
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
                   // Emitted as an array of {asOfDate, factor} rows — the shape
                   // StructuredCashFlowTermsResolver.ReadFactorSchedule accepts. This term was
                   // previously serialized as a free-text string, which that reader skips, so
                   // factor-based tranches restated face off the scalar currentFactor alone.
                   factorSchedule =
                        terms.FactorSchedule
                        |> List.map (fun entry ->
                            {| asOfDate = entry.AsOfDate
                               factor = entry.Factor
                               source = entry.Source
                               evidenceLink = entry.EvidenceLink
                               sourceContentHash = entry.SourceContentHash |})
                   factorScheduleNote = terms.FactorScheduleNote |})
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
        | SecurityKind.InvestmentFund terms ->
            JsonSerializer.Serialize(
                {| schemaVersion = schemaVersion
                   fundType = terms.FundType
                   fundFamily = terms.FundFamily
                   navCurrency = terms.NavCurrency
                   distributionPolicy = terms.DistributionPolicy |> Option.map DistributionPolicy.label
                   isStableNav = terms.IsStableNav
                   pricingSource = terms.PricingSource |})

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
