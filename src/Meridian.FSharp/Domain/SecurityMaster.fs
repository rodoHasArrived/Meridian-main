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

[<RequireQualifiedAccess>]
type DividendType =
    | Fixed
    | Floating
    | Cumulative

[<RequireQualifiedAccess>]
module DividendType =
    let asString dividendType =
        match dividendType with
        | DividendType.Fixed -> "Fixed"
        | DividendType.Floating -> "Floating"
        | DividendType.Cumulative -> "Cumulative"

type ParticipationTerms = {
    ParticipatesInCommonDividends: bool
    AdditionalDividendThreshold: decimal option
}

[<RequireQualifiedAccess>]
type LiquidationPreference =
    | Pari
    | Senior of multiple: decimal
    | Subordinated

[<RequireQualifiedAccess>]
module LiquidationPreference =
    let asString preference =
        match preference with
        | LiquidationPreference.Pari -> "Pari"
        | LiquidationPreference.Senior _ -> "Senior"
        | LiquidationPreference.Subordinated -> "Subordinated"

type PreferredTerms = {
    DividendRate: decimal option
    DividendType: DividendType
    RedemptionPrice: decimal option
    RedemptionDate: DateOnly option
    CallableDate: DateOnly option
    ParticipationTerms: ParticipationTerms option
    LiquidationPreference: LiquidationPreference
}

type ConvertibleTerms = {
    UnderlyingSecurityId: SecurityId
    ConversionRatio: decimal
    ConversionPrice: decimal option
    ConversionStartDate: DateOnly option
    ConversionEndDate: DateOnly option
}

[<RequireQualifiedAccess>]
type EquityClassification =
    | Common
    | Preferred of PreferredTerms
    | Convertible of ConvertibleTerms
    | ConvertiblePreferred of PreferredTerms * ConvertibleTerms
    | Other of string

[<RequireQualifiedAccess>]
module EquityClassification =
    let asString classification =
        match classification with
        | EquityClassification.Common -> "Common"
        | EquityClassification.Preferred _ -> "Preferred"
        | EquityClassification.Convertible _ -> "Convertible"
        | EquityClassification.ConvertiblePreferred _ -> "ConvertiblePreferred"
        | EquityClassification.Other s -> s

type EquityTerms = {
    ShareClass: string option
    VotingRightsCat: VotingRightsCat option
    Classification: EquityClassification option
}

/// Exercise style for options and warrants.
[<RequireQualifiedAccess>]
type ExerciseStyle =
    | American
    | European
    | Bermudan

/// Single-case option-chain identity used by option-contract and series projections.
type OptChainId = OptChainId of string

type OptionTerms = {
    UnderlyingId: SecurityId
    PutCall: string
    Strike: decimal
    Expiry: DateOnly
    Multiplier: decimal
    /// Links this contract to its option chain / series aggregate.
    OptChainId: string option
    ExerciseStyle: ExerciseStyle option
    /// "Physical" or "Cash".
    SettlementType: string option
    /// True when this contract has been adjusted for a corporate action (split, special dividend, etc.).
    IsAdjusted: bool
    LastTradingDt: DateOnly option
}

type FutureTerms = {
    RootSymbol: string
    ContractMonth: string
    Expiry: DateOnly
    Multiplier: decimal
    LastTradingDt: DateOnly option
    FirstNoticeDt: DateOnly option
    DeliveryMonthDt: DateOnly option
    /// "Physical" or "Cash".
    SettlementType: string option
    /// Delivery point code for physically settled commodity futures.
    DeliveryLocationCode: string option
    /// True when this contract is the current front-month / roll target.
    IsRollTarget: bool
    /// Number of calendar days before expiry when the roll window opens.
    RollWindowDays: int option
}

/// Discriminated union identifying the bond's economic subclass.
[<RequireQualifiedAccess>]
type BondSubclass =
    | Sovereign
    | Corporate
    | Municipal
    | Agency
    | Convertible
    | InflationLinked
    | FloatingRate
    // --- Scheduled / structured principal ---
    /// Bond with contractual sinking fund or mandatory principal instalments.
    | SinkingFund
    /// Step-rate bond whose coupon increases per a contractual schedule (may be callable at step dates).
    | StepRate
    /// Fixed-rate bond that converts to floating at a specified date.
    | FixedToFloat
    // --- Short-term / money-market debt ---
    /// Variable Rate Demand Note — daily or weekly reset rate; investor may tender on demand.
    | Vrdn
    /// Auction Rate Security — rate resets at periodic auction; Clearwater may hold at par.
    | AuctionRate
    // --- Bank / leveraged finance ---
    /// Syndicated or bilateral bank loan; floating rate, principal schedule per credit agreement.
    | BankLoan
    // --- Asset-backed / structured credit ---
    /// Generic asset-backed security (auto loans, credit cards, student loans, etc.).
    | AssetBacked
    /// Agency or non-agency residential mortgage-backed security (pass-through pool).
    | MortgageBacked
    /// Agency MBS guaranteed by Fannie Mae, Freddie Mac, or Ginnie Mae.
    | AgencyMbs
    /// Commercial mortgage-backed security.
    | CommercialMbs
    /// Collateralized Mortgage Obligation — a CMO tranche carved from an MBS pool.
    | Cmo
    /// Collateralized Loan Obligation — CLO tranche backed by leveraged loans.
    | Clo
    /// Collateralized Debt Obligation — generic CDO tranche.
    | Cdo
    /// Principal-Only strip: receives only scheduled and unscheduled principal cash flows.
    | PrincipalOnly
    /// Interest-Only strip: receives only interest cash flows; notional-referenced.
    | InterestOnly
    /// Inverse Interest-Only strip: leveraged IO with inverse-floating coupon.
    | InverseInterestOnly
    | Other of string

[<RequireQualifiedAccess>]
type BondCouponStructure =
    | Fixed of rate: decimal * dayCount: string option
    | Floating of index: string * spreadBps: decimal option * capRate: decimal option * floorRate: decimal option * dayCount: string option
    | ZeroCoupon

type BondTerms = {
    Maturity: DateOnly
    IssueDate: DateOnly option
    Coupon: BondCouponStructure
    IsCallable: bool
    CallDate: DateOnly option
    IssuerName: string option
    Seniority: string option
    /// Economic subclass of this bond instrument.
    Subclass: BondSubclass
    // --- Clearwater Security Master required properties ---
    /// Face/par value of the bond (e.g. 1000 for a $1,000 par bond).
    /// Used in market value calculation: Par × Factor × Price / 100.
    Par: decimal option
    /// Coupon payment frequency (Annual, SemiAnnual, Quarterly, Monthly, etc.).
    PaymentFrequency: PaymentFrequency option
    /// Latest contractual date by which principal is legally due under the governing documents.
    /// May differ from Maturity for pre-refunded or mandatory-put bonds.
    LegalFinalMaturity: DateOnly option
    /// Date to which a pre-refunded municipal bond is called using escrowed proceeds.
    /// When present, cash flows and amortization target this date rather than final maturity.
    PreRefundDate: DateOnly option
    /// Date of a mandatory put feature that obligates the holder to tender at a set price.
    MandatoryPutDate: DateOnly option
}

[<RequireQualifiedAccess>]
module BondTerms =
    let fixedRate maturity couponRate dayCount issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.Fixed(couponRate, dayCount)
          IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None
          Subclass = BondSubclass.Corporate
          Par = None; PaymentFrequency = None; LegalFinalMaturity = None; PreRefundDate = None; MandatoryPutDate = None }

    let floatingRate maturity index spreadBps issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.Floating(index, spreadBps, None, None, None)
          IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None
          Subclass = BondSubclass.FloatingRate
          Par = None; PaymentFrequency = None; LegalFinalMaturity = None; PreRefundDate = None; MandatoryPutDate = None }

    let zeroCoupon maturity issuerName =
        { Maturity = maturity; IssueDate = None; Coupon = BondCouponStructure.ZeroCoupon
          IsCallable = false; CallDate = None; IssuerName = issuerName; Seniority = None
          Subclass = BondSubclass.Corporate
          Par = None; PaymentFrequency = None; LegalFinalMaturity = None; PreRefundDate = None; MandatoryPutDate = None }

    let couponRate (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(rate, _) -> Some rate
        | BondCouponStructure.Floating _ -> None
        | BondCouponStructure.ZeroCoupon -> None

    let dayCount (terms: BondTerms) =
        match terms.Coupon with
        | BondCouponStructure.Fixed(_, dc) -> dc
        | BondCouponStructure.Floating(_, _, _, _, dc) -> dc
        | BondCouponStructure.ZeroCoupon -> None

type FxSpotTerms = {
    BaseCurrency: string
    QuoteCurrency: string
}

type DepositTerms = {
    DepositType: string
    InstitutionName: string
    Maturity: DateOnly option
    InterestRate: decimal option
    DayCount: string option
    IsCallable: bool
}

type MoneyMarketFundTerms = {
    FundFamily: string option
    SweepEligible: bool
    WeightedAverageMaturityDays: int option
    LiquidityFeeEligible: bool
}

type CertificateOfDepositTerms = {
    IssuerName: string
    Maturity: DateOnly
    CouponRate: decimal option
    CallableDate: DateOnly option
    DayCount: string option
}

type CommercialPaperTerms = {
    IssuerName: string
    Maturity: DateOnly
    DiscountRate: decimal option
    DayCount: string option
    IsAssetBacked: bool
}

type TreasuryBillTerms = {
    Maturity: DateOnly
    AuctionDate: DateOnly option
    CUSIP: string option
    DiscountRate: decimal option
}

type RepoTerms = {
    Counterparty: string
    StartDate: DateOnly
    EndDate: DateOnly
    RepoRate: decimal option
    CollateralType: string option
    Haircut: decimal option
}

type CashSweepTerms = {
    ProgramName: string
    SweepVehicleType: string
    SweepFrequency: string option
    TargetAccountType: string option
    YieldRate: decimal option
}

type OtherSecurityTerms = {
    Category: string
    SubType: string option
    Maturity: DateOnly option
    IssuerName: string option
    SettlementType: string option
}

type SwapLeg = {
    LegType: string
    Currency: string
    Index: string option
    FixedRate: decimal option
}

type SwapTerms = {
    EffectiveDate: DateOnly
    MaturityDate: DateOnly
    Legs: SwapLeg list
}

type Covenant = {
    CovenantType: string
    Threshold: string
    Notes: string option
}

/// A scheduled principal repayment (amortizing or term-loan instalment).
type PrincipalPaymentEntry = {
    PaymentDate: DateOnly
    Amount: decimal
}

type DirectLoanTerms = {
    Borrower: string
    Maturity: DateOnly option
    Covenants: Covenant list
    // --- Clearwater bank loan / direct lending properties ---
    /// Floating rate reference index (e.g. "SOFR", "LIBOR", "EURIBOR").
    ReferenceIndex: string option
    /// Spread over the reference index in basis points (e.g. 350 = SOFR + 3.50%).
    SpreadBps: decimal option
    /// Current all-in coupon rate (reference rate + spread, subject to floor/cap).
    CurrentCouponRate: decimal option
    /// Rate reset frequency (e.g. "Daily", "Monthly", "Quarterly").
    ResetFrequency: string option
    /// Contractual or expected principal instalment schedule.
    PrincipalSchedule: PrincipalPaymentEntry list
    /// Pricing source for market value (e.g. "IHSMarkit", "Refinitiv", "Client").
    PricingSource: string option
}

type StructuredCreditTerms = {
    Tranche: string
    PoolId: string option
    CollateralType: string
    OriginalFace: decimal
    CurrentFactor: decimal option
    CouponOrIndex: string
    FactorSchedule: string option
}

type PrivateFundInterestTerms = {
    GpSponsor: string
    Strategy: string
    Vintage: int
    Commitment: decimal
    FundedAmount: decimal option
    UnfundedAmount: decimal option
    NavDate: DateOnly
    Lockup: string option
}

type PrivateCompanyEquityTerms = {
    Issuer: string
    ShareClass: string
    Round: string
    OwnershipPercent: decimal option
    CostBasis: decimal
    LatestValuation: decimal option
    TransferRestrictions: string option
}

type RealEstateHoldingTerms = {
    PropertyType: string
    AddressOrMarket: string
    OwnershipPercent: decimal
    AppraisalValue: decimal
    ValuationDate: DateOnly
    DebtStack: string option
    Sponsor: string option
}

type CommitmentGuaranteeTerms = {
    Counterparty: string
    Beneficiary: string option
    CommittedAmount: decimal
    UnfundedAmount: decimal option
    EffectiveDate: DateOnly
    ExpiryDate: DateOnly option
    FeeRate: decimal option
    Collateral: string option
    Covenants: Covenant list
}

/// Terms for mutual funds, ETFs, hedge funds, REITs, and closed-end funds.
/// These instruments generally do not amortize; market value is units × NAV/price.
type InvestmentFundTerms = {
    /// Fund category (e.g. "MutualFund", "ETF", "HedgeFund", "REIT", "ClosedEnd").
    FundType: string option
    FundFamily: string option
    /// Currency of the NAV (may differ from the account base currency).
    NavCurrency: string option
    /// Distribution policy (Accumulating, Distributing, etc.).
    DistributionPolicy: DistributionPolicy option
    /// True for stable-NAV money market and government liquidity funds.
    IsStableNav: bool option
    /// Price/NAV data source (e.g. "iMoneyNet", "Bloomberg", "Client").
    PricingSource: string option
}

type CommodityTerms = {
    CommodityType: string
    Denomination: string option
    ContractSize: decimal option
}

type CryptoTerms = {
    BaseCurrency: string
    QuoteCurrency: string
    Network: string option
}

type CfdTerms = {
    UnderlyingAssetClass: string
    UnderlyingDescription: string option
    Leverage: decimal option
}

type WarrantTerms = {
    UnderlyingId: SecurityId
    WarrantType: string
    Strike: decimal option
    Expiry: DateOnly option
    Multiplier: decimal option
}

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
    /// Mutual fund, ETF, hedge fund, REIT, or closed-end fund.
    /// Market value = units × NAV (or market price); no amortization in general.
    | InvestmentFund of InvestmentFundTerms

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
        match record.Kind with
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
        | SecurityKind.InvestmentFund _ -> "InvestmentFund"

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
        | SecurityKind.InvestmentFund _ -> "InvestmentFund"

    let underlyingSecurityId kind =
        match kind with
        | SecurityKind.Option terms -> Some terms.UnderlyingId
        | SecurityKind.Warrant terms -> Some terms.UnderlyingId
        | _ -> None

    let isDerivative kind =
        match kind with
        | SecurityKind.Option _
        | SecurityKind.Future _
        | SecurityKind.Swap _
        | SecurityKind.Cfd _
        | SecurityKind.Warrant _ -> true
        | SecurityKind.Equity _
        | SecurityKind.Bond _
        | SecurityKind.FxSpot _
        | SecurityKind.Deposit _
        | SecurityKind.MoneyMarketFund _
        | SecurityKind.CertificateOfDeposit _
        | SecurityKind.CommercialPaper _
        | SecurityKind.TreasuryBill _
        | SecurityKind.Repo _
        | SecurityKind.CashSweep _
        | SecurityKind.OtherSecurity _
        | SecurityKind.DirectLoan _
        | SecurityKind.StructuredCredit _
        | SecurityKind.PrivateFundInterest _
        | SecurityKind.PrivateCompanyEquity _
        | SecurityKind.RealEstateHolding _
        | SecurityKind.CommitmentGuarantee _
        | SecurityKind.Commodity _
        | SecurityKind.CryptoCurrency _
        | SecurityKind.InvestmentFund _ -> false
