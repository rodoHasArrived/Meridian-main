// Generated from SecurityAssetTermsCodecBindings by SecurityFlatCodecGenerator. Do not edit:
// change the binding table and regenerate (MERIDIAN_ACCEPT_GENERATED_CODEC=1, then run
// SecurityFlatCodecGenerationTests).
namespace Meridian.FSharp.SecurityMasterInterop

open System.Text.Json
open Meridian.FSharp.Domain

/// The flat writer arms generated from the binding table: one function per class, each the
/// class's unstamped asset-specific terms document.
[<RequireQualifiedAccess>]
module internal GeneratedFlatWriter =

    let writeOption (schemaVersion: int) (terms: OptionTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               expiry = terms.Expiry
               optChainId = terms.ListingDetail.OptChainId
               settlementType = terms.ListingDetail.SettlementType
               isAdjusted = (terms.ListingDetail.IsAdjusted |> Option.defaultValue false)
               lastTradingDt = terms.ListingDetail.LastTradingDt
               putCall = (terms.Exercise.OptionRight |> Option.get)
               strike = (terms.Exercise.StrikePrice |> Option.get)
               exerciseStyle = terms.Exercise.ExerciseStyle
               underlyingId = (let (SecurityId value) = (terms.Exercise.UnderlyingSecurityId |> Option.get) in value)
               multiplier = (terms.TradingParameters.ContractMultiplier |> Option.get) |})

    let writeFuture (schemaVersion: int) (terms: FutureTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               expiry = terms.Expiry
               settlementType = terms.ListingDetail.SettlementType
               lastTradingDt = terms.ListingDetail.LastTradingDt
               rootSymbol = (terms.ListingDetail.RootSymbol |> Option.get)
               contractMonth = (terms.ListingDetail.ContractMonth |> Option.get)
               firstNoticeDt = terms.ListingDetail.FirstNoticeDt
               deliveryMonthDt = terms.ListingDetail.DeliveryMonthDt
               deliveryLocationCode = terms.ListingDetail.DeliveryLocationCode
               isRollTarget = (terms.ListingDetail.IsRollTarget |> Option.defaultValue false)
               rollWindowDays = terms.ListingDetail.RollWindowDays
               multiplier = (terms.TradingParameters.ContractMultiplier |> Option.get) |})

    let writeBond (schemaVersion: int) (writesScheduleConventions: bool) (writesCallPutSchedules: bool) (terms: BondTerms) : string =
        let payload =
            {| schemaVersion = schemaVersion
               issueDate = terms.Dates.IssueDate
               maturity = terms.Maturity
               couponType = FlatWriterParts.couponType terms.Coupon
               couponRate = BondTerms.couponRate terms
               floatingIndex = FlatWriterParts.floatingIndex terms.Coupon
               spreadBps = FlatWriterParts.floatingSpreadBps terms.Coupon
               capRate = FlatWriterParts.floatingCapRate terms.Coupon
               floorRate = FlatWriterParts.floatingFloorRate terms.Coupon
               stepSchedule = FlatWriterParts.stepSchedule terms.Coupon
               inflationIndex = FlatWriterParts.inflationIndex terms.Coupon
               inflationBaseIndexValue = FlatWriterParts.inflationBaseIndexValue terms.Coupon
               inflationIndexRatio = FlatWriterParts.inflationIndexRatio terms.Coupon
               dayCount = BondTerms.dayCount terms |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
               paymentFrequency = terms.PaymentFrequency |> Option.map PaymentFrequency.label
               isCallable = terms.IsCallable
               callDate = terms.CallDate
               issuerName = terms.IssuerTerms.IssuerName
               subclass = BondSubclass.label terms.Subclass
               seniority = terms.DebtClassification.Seniority
               par = terms.RedemptionFacts.Par
               legalFinalMaturity = terms.RedemptionFacts.LegalFinalMaturity
               preRefundDate = terms.RedemptionFacts.PreRefundDate
               mandatoryPutDate = terms.RedemptionFacts.MandatoryPutDate
               principalSchedule =
                    terms.PrincipalSchedule
                    |> List.map (fun entry ->
                        {| paymentDate = entry.PaymentDate
                           amount = entry.Amount |}) |}
        if writesCallPutSchedules then
            JsonSerializer.Serialize(
                {| payload with
                     scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                     callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                     putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
        elif writesScheduleConventions then
            JsonSerializer.Serialize(
                {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
        else
            JsonSerializer.Serialize(payload)

    let writeFxSpot (schemaVersion: int) (terms: FxSpotTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               baseCurrency = (terms.CurrencyPair.BaseCurrency |> Option.get)
               quoteCurrency = (terms.CurrencyPair.QuoteCurrency |> Option.get) |})

    let writeDeposit (schemaVersion: int) (terms: DepositTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               maturity = terms.Dates.MaturityDate
               institutionName = terms.InstitutionName
               depositType = terms.DepositType
               interestRate = terms.CouponTerms.CouponRate
               dayCount = terms.CouponTerms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
               isCallable = terms.IsCallable |})

    let writeMoneyMarketFund (schemaVersion: int) (terms: MoneyMarketFundTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               fundFamily = terms.FundTerms.FundFamily
               weightedAverageMaturityDays = terms.FundTerms.WeightedAverageMaturityDays
               sweepEligible = (terms.FundTerms.SweepEligible |> Option.defaultValue false)
               liquidityFeeEligible = (terms.FundTerms.LiquidityFeeEligible |> Option.defaultValue false) |})

    let writeCertificateOfDeposit (schemaVersion: int) (writesScheduleConventions: bool) (writesCallPutSchedules: bool) (terms: CertificateOfDepositTerms) : string =
        let payload =
            {| schemaVersion = schemaVersion
               maturity = terms.Maturity
               issuerName = terms.IssuerName
               couponRate = terms.CouponTerms.CouponRate
               dayCount = terms.CouponTerms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
               callableDate = terms.CallableDate |}
        if writesCallPutSchedules then
            JsonSerializer.Serialize(
                {| payload with
                     scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                     callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                     putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
        elif writesScheduleConventions then
            JsonSerializer.Serialize(
                {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
        else
            JsonSerializer.Serialize(payload)

    let writeCommercialPaper (schemaVersion: int) (terms: CommercialPaperTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               maturity = terms.Maturity
               issuerName = terms.IssuerName
               isAssetBacked = terms.IsAssetBacked
               discountRate = terms.DiscountTerms.DiscountRate
               dayCount = terms.CouponTerms.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel |})

    let writeTreasuryBill (schemaVersion: int) (terms: TreasuryBillTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               maturity = terms.Maturity
               auctionDate = terms.Auction.AuctionDate
               cusip = terms.IssuerTerms.IssuerProgram
               discountRate = terms.DiscountTerms.DiscountRate |})

    let writeRepo (schemaVersion: int) (terms: RepoTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               counterparty = terms.Counterparty
               collateralType = terms.FinancingTerms.CollateralType
               haircut = terms.FinancingTerms.Haircut
               startDate = terms.StartDate
               endDate = terms.EndDate
               repoRate = terms.DiscountTerms.YieldRate |})

    let writeCashSweep (schemaVersion: int) (terms: CashSweepTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               programName = terms.ProgramName
               sweepVehicleType = terms.SweepVehicleType
               sweepFrequency = terms.SweepFrequency
               targetAccountType = terms.SweepTerms.TargetAccountType
               yieldRate = terms.DiscountTerms.YieldRate |})

    let writeOtherSecurity (schemaVersion: int) (terms: OtherSecurityTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               maturity = terms.Dates.MaturityDate
               category = terms.ClassificationDetail.Category
               subType = terms.ClassificationDetail.SubType
               issuerName = terms.IssuerTerms.IssuerName
               settlementType = terms.IssuerTerms.IssuerProgram |})

    let writeSwap (schemaVersion: int) (writesScheduleConventions: bool) (terms: SwapTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               effectiveDate = terms.EffectiveDate
               maturityDate = terms.MaturityDate
               legs = terms.Legs |> List.map (FlatWriterParts.swapLeg writesScheduleConventions) |})

    let writeDirectLoan (schemaVersion: int) (terms: DirectLoanTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               maturity = terms.Dates.MaturityDate
               borrower = terms.Borrower
               covenants =
                    terms.Covenants
                    |> List.map (fun entry ->
                        {| covenantType = entry.CovenantType
                           threshold = entry.Threshold
                           notes = entry.Notes |})
               referenceIndex = terms.FloatingRate.ReferenceIndex
               spreadBps = terms.FloatingRate.SpreadBps
               resetFrequency = terms.FloatingRate.ResetFrequency
               currentCouponRate = terms.CouponTerms.CouponRate
               principalSchedule =
                    terms.PrincipalSchedule
                    |> List.map (fun entry ->
                        {| paymentDate = entry.PaymentDate
                           amount = entry.Amount |})
               pricingSource = terms.ReferenceDataSource.PricingSource |})

    let writeStructuredCredit (schemaVersion: int) (writesScheduleConventions: bool) (writesCallPutSchedules: bool) (terms: StructuredCreditTerms) : string =
        let payload =
            {| schemaVersion = schemaVersion
               couponOrIndex = terms.CouponOrIndex
               structuredCreditKind = terms.Kind |> Option.map StructuredCreditKind.asString
               seniority = terms.TrancheContract.Seniority
               attachmentPoint = terms.TrancheContract.AttachmentPoint
               detachmentPoint = terms.TrancheContract.DetachmentPoint
               reinvestmentPeriodEnd = terms.TrancheContract.ReinvestmentPeriodEnd
               nonCallPeriodEnd = terms.TrancheContract.NonCallPeriodEnd
               currentFactor = terms.Pool.Factor
               weightedAverageCoupon = terms.Pool.WeightedAvgCoupon
               weightedAverageMaturityMonths = terms.Pool.WeightedAvgMaturityMonths
               weightedAverageLoanAgeMonths = terms.Pool.WeightedAvgLoanAgeMos
               collateralType = terms.CollateralType
               poolId = terms.Pool.PoolIdentifier
               tranche = terms.Tranche
               originalFace = terms.OriginalFace
               factorSchedule = terms.Pool.FactorScheduleReference
               factorScheduleEntries =
                    terms.FactorScheduleEntries
                    |> List.map (fun entry ->
                        {| asOfDate = entry.AsOfDate
                           factor = entry.Factor |})
               maturity = terms.Dates.MaturityDate |}
        if writesCallPutSchedules then
            JsonSerializer.Serialize(
                {| payload with
                     scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions
                     callSchedule = SecurityTermsWriteVersion.callSchedulePayload terms.CallPutSchedules.Calls
                     putSchedule = SecurityTermsWriteVersion.putSchedulePayload terms.CallPutSchedules.Puts |})
        elif writesScheduleConventions then
            JsonSerializer.Serialize(
                {| payload with scheduleConventions = SecurityTermsWriteVersion.conventionsPayload terms.ScheduleConventions |})
        else
            JsonSerializer.Serialize(payload)

    let writePrivateFundInterest (schemaVersion: int) (terms: PrivateFundInterestTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               vintage = (terms.PrivateAssetValuation.Vintage |> Option.get)
               navDate = (terms.PrivateAssetValuation.NavDate |> Option.get)
               lockup = terms.PrivateAssetValuation.Lockup
               commitment = (terms.Notional.CommittedAmount |> Option.get)
               fundedAmount = terms.Notional.FundedAmount
               unfundedAmount = terms.Notional.UnfundedAmount
               gpSponsor = terms.GpSponsor
               strategy = terms.Strategy |})

    let writePrivateCompanyEquity (schemaVersion: int) (terms: PrivateCompanyEquityTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               shareClass = terms.ShareClass
               ownershipPercent = terms.PrivateAssetValuation.OwnershipPercent
               costBasis = (terms.PrivateAssetValuation.CostBasis |> Option.get)
               latestValuation = terms.PrivateAssetValuation.LatestValuation
               transferRestrictions = terms.PrivateAssetValuation.TransferRestrictions
               issuer = terms.Issuer
               round = terms.Round |})

    let writeRealEstateHolding (schemaVersion: int) (terms: RealEstateHoldingTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               ownershipPercent = terms.PrivateAssetValuation.OwnershipPercent
               addressOrMarket = (terms.PrivateAssetValuation.AddressOrMarket |> Option.get)
               appraisalValue = (terms.PrivateAssetValuation.AppraisalValue |> Option.get)
               valuationDate = (terms.PrivateAssetValuation.ValuationDate |> Option.get)
               debtStack = terms.PrivateAssetValuation.DebtStack
               sponsor = terms.IssuerTerms.IssuerName
               propertyType = terms.PropertyType |})

    let writeCommitmentGuarantee (schemaVersion: int) (terms: CommitmentGuaranteeTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               effectiveDate = terms.EffectiveDate
               expiryDate = terms.Dates.MaturityDate
               committedAmount = (terms.Notional.CommittedAmount |> Option.get)
               unfundedAmount = terms.Notional.UnfundedAmount
               counterparty = terms.Counterparty
               beneficiary = terms.IssuerTerms.IssuerProgram
               feeRate = terms.ContractualRate.FeeRate
               collateral = terms.CollateralTerms.CollateralType
               covenants =
                    terms.Covenants
                    |> List.map (fun entry ->
                        {| covenantType = entry.CovenantType
                           threshold = entry.Threshold
                           notes = entry.Notes |}) |})

    let writeCommodity (schemaVersion: int) (terms: CommodityTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               commodityType = terms.CommodityContract.CommodityType
               denomination = terms.CommodityContract.Denomination
               contractSize = terms.CommodityContract.ContractSize |})

    let writeCryptoCurrency (schemaVersion: int) (terms: CryptoTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               baseCurrency = (terms.CurrencyPair.BaseCurrency |> Option.get)
               quoteCurrency = (terms.CurrencyPair.QuoteCurrency |> Option.get)
               network = terms.DigitalAsset.Network |})

    let writeCfd (schemaVersion: int) (terms: CfdTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               underlyingAssetClass = terms.UnderlyingExposure.UnderlyingAssetClass
               underlyingDescription = terms.UnderlyingExposure.UnderlyingDescription
               leverage = terms.UnderlyingExposure.Leverage |})

    let writeWarrant (schemaVersion: int) (terms: WarrantTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               expiry = terms.Dates.MaturityDate
               warrantType = (terms.Exercise.OptionRight |> Option.get)
               strike = terms.Exercise.StrikePrice
               underlyingId = (let (SecurityId value) = (terms.Exercise.UnderlyingSecurityId |> Option.get) in value)
               multiplier = terms.TradingParameters.ContractMultiplier |})

    let writeFxForward (schemaVersion: int) (terms: FxForwardTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               tradeDate = terms.Dates.EffectiveDate
               valueDate = terms.ValueDate
               baseCurrency = (terms.CurrencyPair.BaseCurrency |> Option.get)
               quoteCurrency = (terms.CurrencyPair.QuoteCurrency |> Option.get)
               forwardRate = terms.CurrencyPair.ForwardRate
               notional = terms.Notional.Notional
               settlementType = terms.Settlement.SettlementType |})

    let writeNonDeliverableForward (schemaVersion: int) (terms: NonDeliverableForwardTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               fixingDate = terms.FixingDate
               valueDate = terms.ValueDate
               baseCurrency = (terms.CurrencyPair.BaseCurrency |> Option.get)
               quoteCurrency = (terms.CurrencyPair.QuoteCurrency |> Option.get)
               settlementCurrency = (terms.CurrencyPair.SettlementCurrency |> Option.get)
               forwardRate = terms.CurrencyPair.ForwardRate
               notional = terms.Notional.Notional
               fixingSource = terms.ReferenceDataSource.FixingSource |})

    let writeCreditDefaultSwap (schemaVersion: int) (terms: CreditDefaultSwapTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               effectiveDate = terms.EffectiveDate
               maturityDate = terms.MaturityDate
               referenceEntity = terms.CreditProtection.ReferenceEntity
               referenceObligation = terms.CreditProtection.ReferenceObligation
               protectionSide = terms.CreditProtection.ProtectionSide
               seniority = terms.CreditProtection.Seniority
               restructuringClause = terms.CreditProtection.RestructuringClause
               upfrontPoints = terms.CreditProtection.UpfrontPoints
               recoveryRateAssumption = terms.CreditProtection.RecoveryRateAssumption
               indexName = terms.CreditProtection.IndexName
               notional = (terms.Notional.Notional |> Option.get)
               fixedCouponBps = terms.FixedCouponBps
               paymentFrequency = terms.ContractualRate.PaymentFrequency
               dayCount = terms.ContractualRate.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel |})

    let writeSwaption (schemaVersion: int) (terms: SwaptionTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               underlyingEffectiveDate = terms.Dates.EffectiveDate
               expiry = terms.Expiry
               payerReceiver = (terms.Exercise.OptionRight |> Option.get)
               strikeRate = (terms.Exercise.StrikeRate |> Option.get)
               exerciseStyle = terms.Exercise.ExerciseStyle
               notional = (terms.Notional.Notional |> Option.get)
               underlyingMaturityDate = terms.UnderlyingSwap.MaturityDate
               floatingIndex = terms.UnderlyingSwap.FloatingIndex
               fixedLegPaymentFrequency = terms.UnderlyingSwap.FixedLegPaymentFrequency
               fixedLegDayCount = terms.UnderlyingSwap.FixedLegDayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel
               settlementType = terms.Settlement.SettlementType |})

    let writeCapFloor (schemaVersion: int) (terms: CapFloorTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               effectiveDate = terms.EffectiveDate
               maturityDate = terms.MaturityDate
               capFloorType = (terms.Exercise.OptionRight |> Option.get)
               premiumAmount = terms.Exercise.PremiumAmount
               referenceIndex = (terms.FloatingRate.ReferenceIndex |> Option.get)
               resetFrequency = terms.FloatingRate.ResetFrequency
               floorStrike = terms.FloatingRate.FloorRate
               capStrike = terms.FloatingRate.CapRate
               notional = (terms.Notional.Notional |> Option.get)
               dayCount = terms.ContractualRate.DayCount |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel |})

    let writeStructuredNote (schemaVersion: int) (terms: StructuredNoteTerms) : string =
        JsonSerializer.Serialize(
            {| schemaVersion = schemaVersion
               issueDate = terms.Dates.IssueDate
               maturity = terms.Maturity
               underlyingDescription = terms.StructuredPayoff.UnderlyingDescription
               principalProtectionPct = terms.StructuredPayoff.PrincipalProtectionPct
               participationRate = terms.StructuredPayoff.ParticipationRate
               returnCap = terms.StructuredPayoff.ReturnCap
               barrierLevel = terms.StructuredPayoff.BarrierLevel
               denomination = terms.Notional.Denomination
               issuerName = terms.IssuerName
               couponRate = terms.ContractualRate.CouponRate
               paymentFrequency = terms.ContractualRate.PaymentFrequency |})
