namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type SecurityStatus =
    | Active
    | Inactive

[<RequireQualifiedAccess>]
module SecurityStatus =
    let isActive status = status = SecurityStatus.Active
    let isInactive status = status = SecurityStatus.Inactive

    let asString status =
        match status with
        | SecurityStatus.Active -> "Active"
        | SecurityStatus.Inactive -> "Inactive"

type CommonTerms = {
    DisplayName: string
    Currency: string
    CountryOfRisk: string option
    IssuerName: string option
    Exchange: string option
    LotSize: decimal option
    TickSize: decimal option
    /// ISO 10383 Market Identifier Code of the primary listing venue (e.g. "XNAS", "XNYS").
    PrimaryListingMic: string option
    /// Country of legal incorporation; may differ from CountryOfRisk (e.g. Bermuda-domiciled NYSE-listed company).
    CountryOfIncorporation: string option
    /// Standard settlement lag in business days (e.g. 1 for T+1 US equities, 2 for most bonds and EU equities).
    SettlementCycleDays: int option
    /// Named holiday calendar used for settlement and accrual calculations (e.g. "NYSE", "LDN", "T2S").
    HolidayCalendarId: string option
}

[<RequireQualifiedAccess>]
module CommonTerms =
    let normalizedDisplayName (terms: CommonTerms) =
        terms.DisplayName.Trim()

    let normalizedCurrency (terms: CommonTerms) =
        terms.Currency.Trim().ToUpperInvariant()

    let withNormalizedCoreFields (terms: CommonTerms) =
        {
            terms with
                DisplayName = normalizedDisplayName terms
                Currency = normalizedCurrency terms
                PrimaryListingMic = terms.PrimaryListingMic |> Option.map (fun m -> m.Trim().ToUpperInvariant())
        }

type EquityTerms = {
    Capital: EquityCapitalTerms
} with
    member this.ShareClass = this.Capital.ShareClass
    member this.VotingRightsCat = this.Capital.VotingRightsCat
    member this.Classification = this.Capital.Classification


/// Exercise style for options and warrants.
[<RequireQualifiedAccess>]
type ExerciseStyle =
    | American
    | European
    | Bermudan

/// Single-case option-chain identity used by option-contract and series projections.
type OptChainId = OptChainId of string

type OptionTerms = {
    Dates: MaturityTerms
    ListingDetail: ListingDetailTerms
    Exercise: ExerciseTerms
    TradingParameters: TradingParams
} with
    member this.Expiry = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


type FutureTerms = {
    Dates: MaturityTerms
    ListingDetail: ListingDetailTerms
    TradingParameters: TradingParams
} with
    member this.Expiry = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


type BondTerms = {
    Dates: MaturityTerms
    CouponStructure: BondCouponTerms
    Callability: EconomicCallTerms
    IssuerTerms: IssuerRef
    DebtClassification: DebtClassificationTerms
    RedemptionFacts: BondRedemptionTerms
    // --- Valuation and redemption terms ---
    /// Contractual principal instalment schedule for sinking-fund and other scheduled-principal
    /// subclasses. Empty for bullet bonds. The BondSubclass taxonomy already names SinkingFund;
    /// this is the term data that lets cash-flow and amortization math act on it.
    Principal: PrincipalScheduleTerms
    /// Declared schedule conventions (asset-class terms version 2); empty until declared.
    DeclaredSchedule: DeclaredScheduleTerms
    /// Declared call and put schedules (asset-class terms version 3); empty until declared.

} with
    member this.ScheduleConventions = this.DeclaredSchedule.Conventions
    member this.CallPutSchedules = this.DeclaredSchedule.Exercises
    member this.IsCallable = this.Callability.IsCallable
    member this.CallDate = this.Callability.FirstCallDate
    member this.Seniority = this.DebtClassification.Seniority
    member this.Subclass = this.DebtClassification.Subclass
    member this.Par = this.RedemptionFacts.Par
    member this.LegalFinalMaturity = this.RedemptionFacts.LegalFinalMaturity
    member this.PreRefundDate = this.RedemptionFacts.PreRefundDate
    member this.MandatoryPutDate = this.RedemptionFacts.MandatoryPutDate
    member this.PaymentFrequency = this.CouponStructure.PaymentFrequency
    member this.IssuerName = this.IssuerTerms.IssuerName
    member this.Maturity = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.IssueDate = this.Dates.IssueDate
    member this.Coupon = this.CouponStructure.Structure
    member this.PrincipalSchedule = this.Principal.Payments


[<RequireQualifiedAccess>]
module BondTerms =
    let fixedRate maturity couponRate dayCount issuerName =
        { Dates = { EffectiveDate = None; IssueDate = None; MaturityDate = Some (maturity) }; CouponStructure = { Structure = BondCouponStructure.Fixed(couponRate, dayCount); PaymentFrequency = None }
          Callability = EconomicCallTerms.scalar false None; IssuerTerms = { IssuerRef.empty with IssuerName = issuerName }
          DebtClassification = { Subclass = BondSubclass.Corporate; Seniority = None }
          RedemptionFacts = BondRedemptionTerms.empty
          Principal = { Payments = [] }
          DeclaredSchedule = DeclaredScheduleTerms.empty }

    let floatingRate maturity index spreadBps issuerName =
        { Dates = { EffectiveDate = None; IssueDate = None; MaturityDate = Some (maturity) }; CouponStructure = { Structure = BondCouponStructure.Floating(index, spreadBps, None, None, None); PaymentFrequency = None }
          Callability = EconomicCallTerms.scalar false None; IssuerTerms = { IssuerRef.empty with IssuerName = issuerName }
          DebtClassification = { Subclass = BondSubclass.FloatingRate; Seniority = None }
          RedemptionFacts = BondRedemptionTerms.empty
          Principal = { Payments = [] }
          DeclaredSchedule = DeclaredScheduleTerms.empty }

    let zeroCoupon maturity issuerName =
        { Dates = { EffectiveDate = None; IssueDate = None; MaturityDate = Some (maturity) }; CouponStructure = { Structure = BondCouponStructure.ZeroCoupon; PaymentFrequency = None }
          Callability = EconomicCallTerms.scalar false None; IssuerTerms = { IssuerRef.empty with IssuerName = issuerName }
          DebtClassification = { Subclass = BondSubclass.Corporate; Seniority = None }
          RedemptionFacts = BondRedemptionTerms.empty
          Principal = { Payments = [] }
          DeclaredSchedule = DeclaredScheduleTerms.empty }

    let couponRate (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(rate, _) -> Some rate
        | BondCouponStructure.Floating _ -> None
        | BondCouponStructure.ZeroCoupon -> None
        // Step rates live in the dated schedule (see couponRateAsOf); a single scalar would
        // misstate every period but the one it was sampled in.
        | BondCouponStructure.Step _ -> None
        | BondCouponStructure.InflationLinked(realRate, _, _, _, _) -> Some realRate

    let dayCount (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(_, dc) -> dc
        | BondCouponStructure.Floating(_, _, _, _, dc) -> dc
        | BondCouponStructure.ZeroCoupon -> None
        | BondCouponStructure.Step(_, dc) -> dc
        | BondCouponStructure.InflationLinked(_, _, _, _, dc) -> dc

    /// The coupon rate payable as of a date: for step coupons, the latest scheduled step
    /// effective on or before <paramref name="asOf"/>; for other structures, the scalar rate
    /// (when one exists). None for floating/zero coupons and for a step date before the first step.
    let couponRateAsOf (asOf: DateOnly) (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Step(schedule, _) ->
            schedule
            |> List.filter (fun entry -> entry.EffectiveDate <= asOf)
            |> List.sortBy (fun entry -> entry.EffectiveDate)
            |> List.tryLast
            |> Option.map (fun entry -> entry.Rate)
        | _ -> couponRate terms

type FxSpotTerms = {
    CurrencyPair: CurrencyPairTerms

}

type DepositTerms = {
    Dates: MaturityTerms
    IssuerTerms: IssuerRef
    CouponTerms: CouponTerms
    Callability: EconomicCallTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.IsCallable = this.Callability.IsCallable
    member this.DepositType = (this.IssuerTerms.IssuerProgram |> Option.defaultValue System.String.Empty)
    member this.InstitutionName = (this.IssuerTerms.InstitutionName |> Option.defaultValue System.String.Empty)
    member this.Maturity = this.Dates.MaturityDate
    member this.InterestRate = this.CouponTerms.CouponRate
    member this.DayCount = this.CouponTerms.DayCount

type MoneyMarketFundTerms = {
    FundTerms: FundTerms
} with
    member this.FundFamily = this.FundTerms.FundFamily
    member this.SweepEligible = (this.FundTerms.SweepEligible |> Option.defaultValue false)
    member this.WeightedAverageMaturityDays = this.FundTerms.WeightedAverageMaturityDays
    member this.LiquidityFeeEligible = (this.FundTerms.LiquidityFeeEligible |> Option.defaultValue false)


type CertificateOfDepositTerms = {
    Dates: MaturityTerms
    IssuerTerms: IssuerRef
    CouponTerms: CouponTerms
    Callability: EconomicCallTerms
    /// Declared schedule conventions (asset-class terms version 2); empty until declared.
    DeclaredSchedule: DeclaredScheduleTerms
    /// Declared call and put schedules (asset-class terms version 3); empty until declared.
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.ScheduleConventions = this.DeclaredSchedule.Conventions
    member this.CallPutSchedules = this.DeclaredSchedule.Exercises
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.CallableDate = this.Callability.FirstCallDate
    member this.IssuerName = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Maturity = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.CouponRate = this.CouponTerms.CouponRate
    member this.DayCount = this.CouponTerms.DayCount

type CommercialPaperTerms = {
    Dates: MaturityTerms
    IssuerTerms: IssuerRef
    DiscountTerms: DiscountTerms
    CouponTerms: CouponTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.IssuerName = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.IsAssetBacked = this.IssuerTerms.IssuerProgram = Some "AssetBacked"
    member this.Maturity = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.DiscountRate = this.DiscountTerms.DiscountRate
    member this.DayCount = this.CouponTerms.DayCount

type TreasuryBillTerms = {
    Dates: MaturityTerms
    Auction: AuctionTerms
    IssuerTerms: IssuerRef
    DiscountTerms: DiscountTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.AuctionDate = this.Auction.AuctionDate
    member this.CUSIP = this.IssuerTerms.IssuerProgram
    member this.Maturity = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.DiscountRate = this.DiscountTerms.DiscountRate

type RepoTerms = {
    FinancingTerms: FinancingTerms
    DiscountTerms: DiscountTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.Counterparty = (this.FinancingTerms.Counterparty |> Option.defaultValue System.String.Empty)
    member this.StartDate = (this.FinancingTerms.OpenDate |> Option.defaultValue System.DateOnly.MinValue)
    member this.EndDate = (this.FinancingTerms.CloseDate |> Option.defaultValue System.DateOnly.MinValue)
    member this.CollateralType = this.FinancingTerms.CollateralType
    member this.Haircut = this.FinancingTerms.Haircut
    member this.RepoRate = this.DiscountTerms.YieldRate

type CashSweepTerms = {
    SweepTerms: SweepTerms
    DiscountTerms: DiscountTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.ProgramName = (this.SweepTerms.ProgramName |> Option.defaultValue System.String.Empty)
    member this.SweepVehicleType = (this.SweepTerms.SweepVehicleType |> Option.map SweepVehicle.label |> Option.defaultValue System.String.Empty)
    member this.SweepFrequency = this.SweepTerms.SweepFrequency |> Option.map PaymentFrequency.label
    member this.TargetAccountType = this.SweepTerms.TargetAccountType
    member this.YieldRate = this.DiscountTerms.YieldRate

type OtherSecurityTerms = {
    Dates: MaturityTerms
    ClassificationDetail: ClassificationDetailTerms
    IssuerTerms: IssuerRef
} with
    member this.Category = this.ClassificationDetail.Category
    member this.SubType = this.ClassificationDetail.SubType
    member this.IssuerName = this.IssuerTerms.IssuerName
    member this.SettlementType = this.IssuerTerms.IssuerProgram
    member this.Maturity = this.Dates.MaturityDate


type SwapTerms = {
    Dates: MaturityTerms
    SwapLegs: SwapLegTerms
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.EffectiveDate = (this.Dates.EffectiveDate |> Option.defaultValue DateOnly.MinValue)
    member this.MaturityDate = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.Legs = this.SwapLegs.Legs


type DirectLoanTerms = {
    Dates: MaturityTerms
    IssuerTerms: IssuerRef
    LoanCovenants: LoanCovenantTerms
    // --- Floating-rate loan economics ---
    /// Floating rate reference index (e.g. "SOFR", "LIBOR", "EURIBOR").
    FloatingRate: FloatingRateTerms
    /// Current all-in coupon rate (reference rate + spread, subject to floor/cap).
    CouponTerms: CouponTerms
    /// Contractual or expected principal instalment schedule.
    Principal: PrincipalScheduleTerms
    /// Operational reference, not loan economics: the pricing source for market value
    /// (e.g. "IHSMarkit", "Refinitiv", "Client"). Declared Operational in the term schema.
    ReferenceDataSource: ReferenceDataSourceTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.PricingSource = this.ReferenceDataSource.PricingSource
    member this.Borrower = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Maturity = this.Dates.MaturityDate
    member this.ReferenceIndex = this.FloatingRate.ReferenceIndex
    member this.SpreadBps = this.FloatingRate.SpreadBps
    member this.ResetFrequency = this.FloatingRate.ResetFrequency
    member this.CurrentCouponRate = this.CouponTerms.CouponRate
    member this.Covenants = this.LoanCovenants.Entries
    member this.PrincipalSchedule = this.Principal.Payments


/// A dated pool-factor point: the outstanding factor effective on <c>AsOfDate</c>. The typed
/// counterpart of the free-text factor-schedule term, so amortization can be seeded from a dated
/// schedule instead of a single scalar factor.
type FactorScheduleEntry = EconomicFactorScheduleEntry

/// The securitisation family a structured-credit tranche belongs to. Carried as a typed term so
/// reporting, risk and reconciliation can partition on it instead of inferring it from the free-text
/// tranche label or the collateral type; <c>OtherStructuredCreditKind</c> keeps an unrecognised
/// vendor label verbatim rather than dropping it.
type StructuredCreditTerms = {
    CouponTerms: CouponTerms
    TrancheContract: TrancheContractTerms
    Pool: StructuredProductTerms
    Dates: MaturityTerms
    /// Declared schedule conventions (asset-class terms version 3); empty until declared.
    DeclaredSchedule: DeclaredScheduleTerms
    /// Declared call and put schedules (asset-class terms version 4); empty until declared.
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.ScheduleConventions = this.DeclaredSchedule.Conventions
    member this.CallPutSchedules = this.DeclaredSchedule.Exercises
    member this.CouponOrIndex = this.CouponTerms.CouponType |> Option.map CouponKind.label |> Option.defaultValue String.Empty
    member this.Kind = this.TrancheContract.Kind
    member this.Seniority = this.TrancheContract.Seniority
    member this.AttachmentPoint = this.TrancheContract.AttachmentPoint
    member this.DetachmentPoint = this.TrancheContract.DetachmentPoint
    member this.ReinvestmentPeriodEnd = this.TrancheContract.ReinvestmentPeriodEnd
    member this.NonCallPeriodEnd = this.TrancheContract.NonCallPeriodEnd
    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.Tranche = (this.Pool.TrancheClass |> Option.defaultValue String.Empty)
    member this.PoolId = this.Pool.PoolIdentifier
    member this.CollateralType = (this.Pool.CollateralType |> Option.defaultValue String.Empty)
    member this.OriginalFace = (this.Pool.NotionalBalance |> Option.defaultValue 0m)
    member this.CurrentFactor = this.Pool.Factor
    member this.FactorSchedule = this.Pool.FactorScheduleReference
    member this.FactorScheduleEntries = this.Pool.FactorScheduleEntries
    member this.WeightedAverageCoupon = this.Pool.WeightedAvgCoupon
    member this.WeightedAverageMaturityMonths = this.Pool.WeightedAvgMaturityMonths
    member this.WeightedAverageLoanAgeMonths = this.Pool.WeightedAvgLoanAgeMos
    member this.Maturity = this.Dates.MaturityDate


type PrivateFundInterestTerms = {
    PrivateAssetValuation: PrivateAssetValuationTerms
    Notional: NotionalTerms
    IssuerTerms: IssuerRef
} with
    member this.GpSponsor = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Strategy = (this.IssuerTerms.IssuerProgram |> Option.defaultValue System.String.Empty)


type PrivateCompanyEquityTerms = {
    Capital: EquityCapitalTerms
    PrivateAssetValuation: PrivateAssetValuationTerms
    IssuerTerms: IssuerRef
} with
    member this.ShareClass = this.Capital.ShareClass |> Option.defaultValue String.Empty
    member this.Issuer = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Round = (this.IssuerTerms.IssuerProgram |> Option.defaultValue System.String.Empty)


type RealEstateHoldingTerms = {
    PrivateAssetValuation: PrivateAssetValuationTerms
    IssuerTerms: IssuerRef
} with
    member this.PropertyType = (this.IssuerTerms.IssuerProgram |> Option.defaultValue System.String.Empty)
    member this.Sponsor = this.IssuerTerms.IssuerName


type CommitmentGuaranteeTerms = {
    Dates: MaturityTerms
    Notional: NotionalTerms
    IssuerTerms: IssuerRef
    ContractualRate: ContractualRateTerms
    CollateralTerms: FinancingTerms
    LoanCovenants: LoanCovenantTerms
} with
    member this.FeeRate = this.ContractualRate.FeeRate
    member this.Collateral = this.CollateralTerms.CollateralType

    member this.Counterparty = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Beneficiary = this.IssuerTerms.IssuerProgram
    member this.EffectiveDate = (this.Dates.EffectiveDate |> Option.defaultValue DateOnly.MinValue)
    member this.ExpiryDate = this.Dates.MaturityDate
    member this.Covenants = this.LoanCovenants.Entries


/// Terms for mutual funds, ETFs, hedge funds, REITs, and closed-end funds.
/// These instruments generally do not amortize; market value is units × NAV/price.
type InvestmentFundTerms = {
    FundShare: FundShareTerms
    FundTerms: FundTerms
    ReferenceDataSource: ReferenceDataSourceTerms
} with
    member this.FundType = this.FundShare.FundType
    member this.NavCurrency = this.FundShare.NavCurrency
    member this.DistributionPolicy = this.FundShare.DistributionPolicy
    member this.IsStableNav = this.FundShare.IsStableNav
    member this.PricingSource = this.ReferenceDataSource.PricingSource
    member this.FundFamily = this.FundTerms.FundFamily


type CommodityTerms = {
    CommodityContract: CommodityContractTerms
} with
    member this.CommodityType = this.CommodityContract.CommodityType
    member this.Denomination = this.CommodityContract.Denomination
    member this.ContractSize = this.CommodityContract.ContractSize

type CryptoTerms = {
    CurrencyPair: CurrencyPairTerms
    DigitalAsset: DigitalAssetTerms
} with
    member this.Network = this.DigitalAsset.Network

type CfdTerms = {
    UnderlyingExposure: UnderlyingExposureTerms
} with
    member this.UnderlyingAssetClass = this.UnderlyingExposure.UnderlyingAssetClass
    member this.UnderlyingDescription = this.UnderlyingExposure.UnderlyingDescription
    member this.Leverage = this.UnderlyingExposure.Leverage

type WarrantTerms = {
    Dates: MaturityTerms
    Exercise: ExerciseTerms
    TradingParameters: TradingParams
} with
    member this.Expiry = this.Dates.MaturityDate


/// Profile-backed custom asset: the typed governance envelope plus the complete
/// asset-specific-terms document preserved verbatim. Profile fields are dynamic and governed
/// by the approved profile version, so the domain carries the document opaquely instead of
/// modeling per-profile shapes; the envelope fields are the ones every custom asset must carry.
/// Carrying the raw document is what makes serialize → deserialize → serialize lossless for
/// the extension point — previously CustomAsset collapsed into OtherSecurity and dropped the
/// profile envelope on any amend.
type CustomAssetTerms = ProfileEnvelopeTerms

/// Outright FX forward: an agreed exchange of the base and quote currencies at a forward rate on
/// the value date. Deliverable unless SettlementType says otherwise.
type FxForwardTerms = {
    Dates: MaturityTerms
    CurrencyPair: CurrencyPairTerms
    Notional: NotionalTerms
    /// Settlement method label ("Physical" by default, "Cash" for a cash-settled forward).
    Settlement: SettlementTerms
} with
    member this.SettlementType = this.Settlement.SettlementType
    member this.TradeDate = this.Dates.EffectiveDate
    member this.ValueDate = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


/// Non-deliverable forward: cash-settled in the settlement currency against a published fixing of
/// the base/quote rate on the fixing date; no exchange of the restricted currency takes place.
type NonDeliverableForwardTerms = {
    Dates: MaturityTerms
    CurrencyPair: CurrencyPairTerms
    Notional: NotionalTerms
    /// Published fixing source the settlement rate is read from (operational reference).
    ReferenceDataSource: ReferenceDataSourceTerms
} with
    member this.FixingSource = this.ReferenceDataSource.FixingSource
    member this.FixingDate = (this.Dates.EffectiveDate |> Option.defaultValue DateOnly.MinValue)
    member this.ValueDate = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


/// Single-name or index credit default swap: protection bought or sold on a reference entity (or
/// index) for a running fixed coupon over the protection period.
type CreditDefaultSwapTerms = {
    Dates: MaturityTerms
    CreditProtection: CreditProtectionTerms
    Notional: NotionalTerms
    /// Running fixed coupon in basis points per annum (e.g. 100 or 500 under the standard contract).
    ContractualRate: ContractualRateTerms
} with
    member this.FixedCouponBps = this.ContractualRate.FixedCouponBps |> Option.defaultValue 0m
    member this.PaymentFrequency = this.ContractualRate.PaymentFrequency
    member this.DayCount = this.ContractualRate.DayCount

    member this.EffectiveDate = (this.Dates.EffectiveDate |> Option.defaultValue DateOnly.MinValue)
    member this.MaturityDate = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


/// Option to enter an interest-rate swap: the holder may pay ("Payer") or receive ("Receiver") the
/// strike fixed rate against the floating index on the underlying swap dates.
type SwaptionTerms = {
    Dates: MaturityTerms
    Exercise: ExerciseTerms
    Notional: NotionalTerms
    UnderlyingSwap: UnderlyingSwapTerms
    /// Settlement method label ("Physical" enters the swap, "Cash" pays its value).
    Settlement: SettlementTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.UnderlyingMaturityDate = this.UnderlyingSwap.MaturityDate
    member this.FloatingIndex = this.UnderlyingSwap.FloatingIndex
    member this.FixedLegPaymentFrequency = this.UnderlyingSwap.FixedLegPaymentFrequency
    member this.FixedLegDayCount = this.UnderlyingSwap.FixedLegDayCount
    member this.SettlementType = this.Settlement.SettlementType

    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.Expiry = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.UnderlyingEffectiveDate = this.Dates.EffectiveDate


/// Interest-rate cap, floor or collar on a floating reference index over the protection period.
type CapFloorTerms = {
    Dates: MaturityTerms
    Exercise: ExerciseTerms
    FloatingRate: FloatingRateTerms
    Notional: NotionalTerms
    ContractualRate: ContractualRateTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.DayCount = this.ContractualRate.DayCount

    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.EffectiveDate = (this.Dates.EffectiveDate |> Option.defaultValue DateOnly.MinValue)
    member this.MaturityDate = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)


/// Issuer note whose return is linked to a reference asset or basket (principal-protected notes,
/// participation notes, reverse convertibles, autocallables). Held and priced by face like debt.
type StructuredNoteTerms = {
    Dates: MaturityTerms
    StructuredPayoff: StructuredPayoffTerms
    Notional: NotionalTerms
    IssuerTerms: IssuerRef
    ContractualRate: ContractualRateTerms
    /// Optional governed declaration of this record's scalar rate unit; no inferred default.
    RateConvention: RateConventionTerms option
} with
    member this.CouponRate = this.ContractualRate.CouponRate
    member this.PaymentFrequency = this.ContractualRate.PaymentFrequency

    member this.RateUnit = this.RateConvention |> Option.map (fun declaration -> declaration.RateUnit)
    member this.IssuerName = (this.IssuerTerms.IssuerName |> Option.defaultValue System.String.Empty)
    member this.Maturity = (this.Dates.MaturityDate |> Option.defaultValue DateOnly.MinValue)
    member this.IssueDate = this.Dates.IssueDate


[<RequireQualifiedAccess>]
type SecurityKind =
    | Equity of EquityTerms
    | Option of OptionTerms
    | Future of FutureTerms
    | Bond of BondTerms
    | FxSpot of FxSpotTerms
    | Deposit of DepositTerms
    | MoneyMarketFund of MoneyMarketFundTerms
    | CertificateOfDeposit of CertificateOfDepositTerms
    | CommercialPaper of CommercialPaperTerms
    | TreasuryBill of TreasuryBillTerms
    | Repo of RepoTerms
    | CashSweep of CashSweepTerms
    | OtherSecurity of OtherSecurityTerms
    | Swap of SwapTerms
    | DirectLoan of DirectLoanTerms
    | StructuredCredit of StructuredCreditTerms
    | PrivateFundInterest of PrivateFundInterestTerms
    | PrivateCompanyEquity of PrivateCompanyEquityTerms
    | RealEstateHolding of RealEstateHoldingTerms
    | CommitmentGuarantee of CommitmentGuaranteeTerms
    | Commodity of CommodityTerms
    | CryptoCurrency of CryptoTerms
    | Cfd of CfdTerms
    | Warrant of WarrantTerms
    /// Outright deliverable FX forward.
    | FxForward of FxForwardTerms
    /// Cash-settled non-deliverable FX forward.
    | NonDeliverableForward of NonDeliverableForwardTerms
    /// Single-name or index credit default swap.
    | CreditDefaultSwap of CreditDefaultSwapTerms
    /// Option to enter an interest-rate swap.
    | Swaption of SwaptionTerms
    /// Interest-rate cap, floor or collar.
    | CapFloor of CapFloorTerms
    /// Issuer note with a payoff linked to a reference asset or basket.
    | StructuredNote of StructuredNoteTerms
    /// Mutual fund, ETF, hedge fund, REIT, or closed-end fund.
    /// Market value = units × NAV (or market price); no amortization in general.
    | InvestmentFund of InvestmentFundTerms
    /// Governed profile-backed custom asset (private and other assets modeled via
    /// approved custom profiles). Terms are carried as an opaque, verbatim document.
    | CustomAsset of CustomAssetTerms

/// Structural classification metadata for a single asset class.
/// Data-dependent refinements (e.g. demand vs. time deposits, free-text OtherSecurity
/// sub-types) are layered on top of this default by callers; everything that is fixed
/// per asset class lives here.
type AssetClassDescriptor = {
    /// Canonical asset-class name (e.g. "Equity", "Option"); also the classification TypeName.
    AssetClassName: string
    AssetClass: AssetClass
    Family: AssetFamily option
    /// Default economic sub-type for the asset class.
    SubType: SecuritySubType
    /// Default issuer archetype, when one is implied by the asset class.
    IssuerType: string option
    /// Default risk country, when one is implied by the asset class (e.g. US Treasury bills).
    RiskCountry: string option
    /// True for exchange-traded or OTC derivatives.
    IsDerivative: bool
}

/// Single source of truth for Security Master asset classification.
///
/// Every <see cref="SecurityKind"/> resolves to exactly one <see cref="AssetClassDescriptor"/>
/// here, and all asset-class dispatch — the asset-class string, the derivative predicate, and
/// the canonical <see cref="SecurityClassification"/> consumed by the economic-definition upgrade
/// path — is derived from this table rather than being re-encoded at each call site. Adding a new
/// asset class means adding one <c>key</c> arm and one <c>Descriptors</c> entry; every downstream
/// projection then updates automatically.
[<RequireQualifiedAccess>]
module AssetClassRegistry =
    /// The one canonical metadata table, one entry per asset class. Order matches the
    /// <see cref="SecurityKind"/> declaration so the taxonomy reads top-to-bottom.
    let private descriptors : AssetClassDescriptor list =
        [ { AssetClassName = "Equity"; AssetClass = AssetClass.Equity; Family = Some AssetFamily.CommonEquity
            SubType = SecuritySubType.CommonShare; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "Option"; AssetClass = AssetClass.Derivative; Family = Some AssetFamily.ListedDerivative
            SubType = SecuritySubType.OptionContract; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "Future"; AssetClass = AssetClass.Derivative; Family = Some AssetFamily.ListedDerivative
            SubType = SecuritySubType.FutureContract; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "Bond"; AssetClass = AssetClass.FixedIncome; Family = Some AssetFamily.CorporateDebt
            SubType = SecuritySubType.CorporateBond; IssuerType = Some "Corporate"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "FxSpot"; AssetClass = AssetClass.Other; Family = None
            SubType = SecuritySubType.OtherSubType "FxSpot"; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "Deposit"; AssetClass = AssetClass.CashEquivalent; Family = Some AssetFamily.BankProduct
            SubType = SecuritySubType.TimeDeposit; IssuerType = Some "Bank"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "MoneyMarketFund"; AssetClass = AssetClass.Fund; Family = Some AssetFamily.MoneyMarket
            SubType = SecuritySubType.MoneyMarketFund; IssuerType = Some "FundVehicle"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CertificateOfDeposit"; AssetClass = AssetClass.CashEquivalent; Family = Some AssetFamily.BankProduct
            SubType = SecuritySubType.CertificateOfDeposit; IssuerType = Some "Bank"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CommercialPaper"; AssetClass = AssetClass.CashEquivalent; Family = Some AssetFamily.CorporateDebt
            SubType = SecuritySubType.CommercialPaper; IssuerType = Some "Corporate"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "TreasuryBill"; AssetClass = AssetClass.FixedIncome; Family = Some AssetFamily.Sovereign
            SubType = SecuritySubType.TreasuryBill; IssuerType = Some "Sovereign"; RiskCountry = Some "US"; IsDerivative = false }
          { AssetClassName = "Repo"; AssetClass = AssetClass.Financing; Family = Some AssetFamily.RepurchaseAgreement
            SubType = SecuritySubType.Repo; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CashSweep"; AssetClass = AssetClass.CashEquivalent; Family = Some AssetFamily.StructuredCash
            SubType = SecuritySubType.CashSweep; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "OtherSecurity"; AssetClass = AssetClass.Other; Family = None
            SubType = SecuritySubType.OtherSubType "OtherSecurity"; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "Swap"; AssetClass = AssetClass.Derivative; Family = Some AssetFamily.ListedDerivative
            SubType = SecuritySubType.SwapContract; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "DirectLoan"; AssetClass = AssetClass.PrivateCredit; Family = Some AssetFamily.PrivateLoan
            SubType = SecuritySubType.DirectLoan; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "StructuredCredit"; AssetClass = AssetClass.FixedIncome; Family = Some AssetFamily.SecuritizedCredit
            SubType = SecuritySubType.StructuredCredit; IssuerType = Some "StructuredVehicle"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "PrivateFundInterest"; AssetClass = AssetClass.Fund; Family = Some AssetFamily.PartnershipEquity
            SubType = SecuritySubType.LimitedPartnershipInterest; IssuerType = Some "FundVehicle"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "PrivateCompanyEquity"; AssetClass = AssetClass.Equity; Family = Some (AssetFamily.OtherFamily "PrivateCompanyEquity")
            SubType = SecuritySubType.PrivateCompanyEquity; IssuerType = Some "PrivateCompany"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "RealEstateHolding"; AssetClass = AssetClass.Other; Family = Some (AssetFamily.OtherFamily "RealEstate")
            SubType = SecuritySubType.RealEstateHolding; IssuerType = Some "PropertyOrSpv"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CommitmentGuarantee"; AssetClass = AssetClass.Financing; Family = Some (AssetFamily.OtherFamily "CommitmentGuarantee")
            SubType = SecuritySubType.CommitmentGuarantee; IssuerType = Some "Counterparty"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "Commodity"; AssetClass = AssetClass.Other; Family = Some (AssetFamily.OtherFamily "Commodity")
            SubType = SecuritySubType.OtherSubType "Commodity"; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CryptoCurrency"; AssetClass = AssetClass.Other; Family = Some (AssetFamily.OtherFamily "Crypto")
            SubType = SecuritySubType.OtherSubType "CryptoCurrency"; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "Cfd"; AssetClass = AssetClass.Derivative; Family = Some AssetFamily.ListedDerivative
            SubType = SecuritySubType.OtherSubType "Cfd"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "Warrant"; AssetClass = AssetClass.Derivative; Family = Some AssetFamily.ListedDerivative
            SubType = SecuritySubType.OtherSubType "Warrant"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "FxForward"; AssetClass = AssetClass.Derivative; Family = Some (AssetFamily.OtherFamily "OtcDerivative")
            SubType = SecuritySubType.OtherSubType "FxForward"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "NonDeliverableForward"; AssetClass = AssetClass.Derivative; Family = Some (AssetFamily.OtherFamily "OtcDerivative")
            SubType = SecuritySubType.OtherSubType "NonDeliverableForward"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "CreditDefaultSwap"; AssetClass = AssetClass.Derivative; Family = Some (AssetFamily.OtherFamily "OtcDerivative")
            SubType = SecuritySubType.OtherSubType "CreditDefaultSwap"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "Swaption"; AssetClass = AssetClass.Derivative; Family = Some (AssetFamily.OtherFamily "OtcDerivative")
            SubType = SecuritySubType.OtherSubType "Swaption"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "CapFloor"; AssetClass = AssetClass.Derivative; Family = Some (AssetFamily.OtherFamily "OtcDerivative")
            SubType = SecuritySubType.OtherSubType "CapFloor"; IssuerType = None; RiskCountry = None; IsDerivative = true }
          { AssetClassName = "StructuredNote"; AssetClass = AssetClass.FixedIncome; Family = Some AssetFamily.CorporateDebt
            SubType = SecuritySubType.OtherSubType "StructuredNote"; IssuerType = Some "Corporate"; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "InvestmentFund"; AssetClass = AssetClass.Fund; Family = None
            SubType = SecuritySubType.OtherSubType "InvestmentFund"; IssuerType = None; RiskCountry = None; IsDerivative = false }
          { AssetClassName = "CustomAsset"; AssetClass = AssetClass.Other; Family = Some (AssetFamily.OtherFamily "CustomAsset")
            SubType = SecuritySubType.OtherSubType "CustomAsset"; IssuerType = None; RiskCountry = None; IsDerivative = false } ]

    let private byName =
        descriptors |> List.map (fun descriptor -> descriptor.AssetClassName, descriptor) |> Map.ofList

    /// The single discriminated-union projection: maps a kind to its canonical asset-class name.
    /// This is the only place that pattern-matches <see cref="SecurityKind"/> for classification;
    /// all descriptor metadata is then resolved from the table above.
    let private keyOf (kind: SecurityKind) : string =
        match kind with
        | SecurityKind.Equity _ -> "Equity"
        | SecurityKind.Option _ -> "Option"
        | SecurityKind.Future _ -> "Future"
        | SecurityKind.Bond _ -> "Bond"
        | SecurityKind.FxSpot _ -> "FxSpot"
        | SecurityKind.Deposit _ -> "Deposit"
        | SecurityKind.MoneyMarketFund _ -> "MoneyMarketFund"
        | SecurityKind.CertificateOfDeposit _ -> "CertificateOfDeposit"
        | SecurityKind.CommercialPaper _ -> "CommercialPaper"
        | SecurityKind.TreasuryBill _ -> "TreasuryBill"
        | SecurityKind.Repo _ -> "Repo"
        | SecurityKind.CashSweep _ -> "CashSweep"
        | SecurityKind.OtherSecurity _ -> "OtherSecurity"
        | SecurityKind.Swap _ -> "Swap"
        | SecurityKind.DirectLoan _ -> "DirectLoan"
        | SecurityKind.StructuredCredit _ -> "StructuredCredit"
        | SecurityKind.PrivateFundInterest _ -> "PrivateFundInterest"
        | SecurityKind.PrivateCompanyEquity _ -> "PrivateCompanyEquity"
        | SecurityKind.RealEstateHolding _ -> "RealEstateHolding"
        | SecurityKind.CommitmentGuarantee _ -> "CommitmentGuarantee"
        | SecurityKind.Commodity _ -> "Commodity"
        | SecurityKind.CryptoCurrency _ -> "CryptoCurrency"
        | SecurityKind.Cfd _ -> "Cfd"
        | SecurityKind.Warrant _ -> "Warrant"
        | SecurityKind.FxForward _ -> "FxForward"
        | SecurityKind.NonDeliverableForward _ -> "NonDeliverableForward"
        | SecurityKind.CreditDefaultSwap _ -> "CreditDefaultSwap"
        | SecurityKind.Swaption _ -> "Swaption"
        | SecurityKind.CapFloor _ -> "CapFloor"
        | SecurityKind.StructuredNote _ -> "StructuredNote"
        | SecurityKind.InvestmentFund _ -> "InvestmentFund"
        | SecurityKind.CustomAsset _ -> "CustomAsset"

    /// Every asset-class name known to the registry, in declaration order.
    let assetClasses : string list =
        descriptors |> List.map (fun descriptor -> descriptor.AssetClassName)

    /// Resolves the descriptor for a kind. Total by construction — <see cref="keyOf"/> and the
    /// <c>descriptors</c> table are locked together by <c>AssetClassRegistry</c> tests.
    let descriptor (kind: SecurityKind) : AssetClassDescriptor =
        let key = keyOf kind
        match Map.tryFind key byName with
        | Some descriptor -> descriptor
        | None -> failwithf "AssetClassRegistry: no descriptor registered for asset class '%s'." key

    /// Looks up a descriptor by canonical asset-class name.
    let tryDescriptorByName (assetClass: string) : AssetClassDescriptor option =
        Map.tryFind assetClass byName

    /// Canonical asset-class string for a kind.
    let assetClassName (kind: SecurityKind) : string =
        (descriptor kind).AssetClassName

    /// True when the kind is an exchange-traded or OTC derivative.
    let isDerivative (kind: SecurityKind) : bool =
        (descriptor kind).IsDerivative

    /// Structural <see cref="SecurityClassification"/> for a kind. Callers that need
    /// instrument-data-dependent refinement (e.g. demand vs. time deposits) refine the
    /// returned value; sector/industry taxonomy is layered on separately.
    let classification (kind: SecurityKind) : SecurityClassification =
        let descriptor = descriptor kind
        { AssetClass = descriptor.AssetClass
          Family = descriptor.Family
          SubType = descriptor.SubType
          TypeName = descriptor.AssetClassName
          IssuerType = descriptor.IssuerType
          RiskCountry = descriptor.RiskCountry
          Taxonomy = None }

type Provenance = {
    SourceSystem: string
    SourceRecordId: string option
    AsOf: DateTimeOffset
    UpdatedBy: string
    Reason: string option
}

[<RequireQualifiedAccess>]
module Provenance =
    let withUpdatedAsOf asOf (provenance: Provenance) =
        { provenance with AsOf = asOf }

    let normalize (provenance: Provenance) =
        {
            provenance with
                SourceSystem = provenance.SourceSystem.Trim()
                UpdatedBy = provenance.UpdatedBy.Trim()
        }

type SecurityMasterRecord = {
    SecurityId: SecurityId
    Status: SecurityStatus
    Common: CommonTerms
    Identifiers: Identifier list
    Kind: SecurityKind
    Version: int64
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    Provenance: Provenance
}

[<RequireQualifiedAccess>]
module SecurityMasterRecord =
    let primaryIdentifier (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.tryFind (fun identifier -> identifier.IsPrimary)

    let assetClass (record: SecurityMasterRecord) =
        AssetClassRegistry.assetClassName record.Kind

    let isActive (record: SecurityMasterRecord) =
        SecurityStatus.isActive record.Status

    let isInactive (record: SecurityMasterRecord) =
        SecurityStatus.isInactive record.Status

    let containsIdentifier (identifier: Identifier) (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.exists (SecurityIdentifier.sameIdentity identifier)

    let activeIdentifiersAt asOf (record: SecurityMasterRecord) =
        record.Identifiers
        |> List.filter (SecurityIdentifier.isActiveAt asOf)

    let withIdentifiers identifiers (record: SecurityMasterRecord) =
        { record with Identifiers = identifiers }

    let withCommon common (record: SecurityMasterRecord) =
        { record with Common = common }

    let withKind kind (record: SecurityMasterRecord) =
        { record with Kind = kind }

    let withVersion version (record: SecurityMasterRecord) =
        { record with Version = version }

    let withProvenance provenance (record: SecurityMasterRecord) =
        { record with Provenance = provenance }

    let deactivate effectiveTo provenance (record: SecurityMasterRecord) =
        {
            record with
                Status = SecurityStatus.Inactive
                EffectiveTo = Some effectiveTo
                Provenance = provenance
        }

    let normalize (record: SecurityMasterRecord) =
        {
            record with
                Common = record.Common |> CommonTerms.withNormalizedCoreFields
                Provenance = record.Provenance |> Provenance.normalize
        }

[<RequireQualifiedAccess>]
module SecurityKind =
    let assetClass kind =
        AssetClassRegistry.assetClassName kind

    let underlyingSecurityId kind =
        match kind with
        | SecurityKind.Option terms -> terms.Exercise.UnderlyingSecurityId
        | SecurityKind.Warrant terms -> terms.Exercise.UnderlyingSecurityId
        | _ -> None

    let isDerivative kind =
        AssetClassRegistry.isDerivative kind
