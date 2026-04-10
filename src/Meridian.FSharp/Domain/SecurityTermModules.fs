namespace Meridian.FSharp.Domain

open System

/// Payment cadence for income or fee streams.
type PaymentFrequency =
    | Daily
    | Weekly
    | Monthly
    | Quarterly
    | SemiAnnual
    | Annual
    | OtherFrequency of string

/// Day-count basis for accrual calculations.
type DayCountConvention =
    | Actual360
    | Actual365
    | Thirty360
    | Business252
    | OtherDayCount of string

/// Coupon style for interest-bearing instruments.
type CouponKind =
    | Fixed
    | Floating
    | ZeroCoupon
    | Step
    | DiscountNote
    | OtherCoupon of string

/// Redemption structure for principal return.
type RedemptionStyle =
    | Bullet
    | Callable
    | Putable
    | Amortizing
    | Perpetual
    | OtherRedemption of string

/// Distribution policy for equity-like instruments.
type DistributionPolicy =
    | Accumulating
    | Distributing
    | Sweep
    | OtherDistribution of string

/// Vehicle used when sweeping idle cash.
type SweepVehicle =
    | MoneyMarketFund
    | BankDeposit
    | Repo
    | OtherVehicle of string

type MaturityTerms = {
    EffectiveDate: DateOnly option
    IssueDate: DateOnly option
    MaturityDate: DateOnly option
}

type CouponTerms = {
    CouponType: CouponKind option
    CouponRate: decimal option
    PaymentFrequency: PaymentFrequency option
    DayCount: DayCountConvention option
}

type DiscountTerms = {
    DiscountRate: decimal option
    YieldRate: decimal option
}

type FloatingRateTerms = {
    ReferenceIndex: string option
    SpreadBps: decimal option
    ResetFrequency: string option
    FloorRate: decimal option
    CapRate: decimal option
}

type AccrualTerms = {
    AccrualMethod: string option
    AccrualStartDate: DateOnly option
    ExDividendDays: int option
    BusinessDayConvention: string option
    HolidayCalendar: string option
    DayCount: DayCountConvention option
}

type PaymentTerms = {
    PaymentFrequency: PaymentFrequency option
    PaymentLagDays: int option
    PaymentCurrency: string option
}

type RedemptionTerms = {
    RedemptionType: RedemptionStyle option
    RedemptionPrice: decimal option
    IsBullet: bool option
    IsAmortizing: bool option
}

type EconomicCallTerms = {
    IsCallable: bool
    FirstCallDate: DateOnly option
    CallPrice: decimal option
}

type AuctionTerms = {
    AuctionDate: DateOnly option
    AuctionType: string option
}

type SweepTerms = {
    ProgramName: string option
    SweepVehicleType: SweepVehicle option
    SweepFrequency: PaymentFrequency option
    TargetAccountType: string option
}

type FinancingTerms = {
    Counterparty: string option
    CollateralType: string option
    Haircut: decimal option
    OpenDate: DateOnly option
    CloseDate: DateOnly option
}

type IssuerTerms = {
    IssuerName: string option
    InstitutionName: string option
    IssuerProgram: string option
}

type EquityBehaviorTerms = {
    ShareClass: string option
    VotingRights: string option
    DistributionType: DistributionPolicy option
}

type DepositaryReceiptTerms = {
    DepositaryBank: string option
    OrdinaryShareRatio: decimal option
    UnderlyingCurrency: string option
    UnderlyingIsin: string option
    UnderlyingCountry: string option
}

type FundTerms = {
    FundFamily: string option
    WeightedAverageMaturityDays: int option
    SweepEligible: bool option
    LiquidityFeeEligible: bool option
}

type TradingParams = {
    LotSize: decimal option
    TickSize: decimal option
    ContractMultiplier: decimal option
    MarginRequirementPct: decimal option
    TradingHoursUtc: string option
    CircuitBreakerThresholdPct: decimal option
}

type SecurityTermModules = {
    Maturity: MaturityTerms option
    Coupon: CouponTerms option
    Discount: DiscountTerms option
    FloatingRate: FloatingRateTerms option
    Accrual: AccrualTerms option
    Payment: PaymentTerms option
    Redemption: RedemptionTerms option
    Call: EconomicCallTerms option
    Auction: AuctionTerms option
    Sweep: SweepTerms option
    Financing: FinancingTerms option
    Issuer: IssuerTerms option
    EquityBehavior: EquityBehaviorTerms option
    DepositaryReceipt: DepositaryReceiptTerms option
    Fund: FundTerms option
    TradingParameters: TradingParams option
}

[<RequireQualifiedAccess>]
module PaymentFrequency =

    [<CompiledName("Label")>]
    let label = function
        | Daily -> "Daily"
        | Weekly -> "Weekly"
        | Monthly -> "Monthly"
        | Quarterly -> "Quarterly"
        | SemiAnnual -> "SemiAnnual"
        | Annual -> "Annual"
        | OtherFrequency other -> other

[<RequireQualifiedAccess>]
module DayCountConvention =

    [<CompiledName("Label")>]
    let label = function
        | Actual360 -> "Actual360"
        | Actual365 -> "Actual365"
        | Thirty360 -> "Thirty360"
        | Business252 -> "Business252"
        | OtherDayCount other -> other

[<RequireQualifiedAccess>]
module CouponKind =

    [<CompiledName("Label")>]
    let label = function
        | Fixed -> "Fixed"
        | Floating -> "Floating"
        | ZeroCoupon -> "ZeroCoupon"
        | Step -> "Step"
        | DiscountNote -> "DiscountNote"
        | OtherCoupon other -> other

[<RequireQualifiedAccess>]
module RedemptionStyle =

    [<CompiledName("Label")>]
    let label = function
        | Bullet -> "Bullet"
        | Callable -> "Callable"
        | Putable -> "Putable"
        | Amortizing -> "Amortizing"
        | Perpetual -> "Perpetual"
        | OtherRedemption other -> other

[<RequireQualifiedAccess>]
module DistributionPolicy =

    [<CompiledName("Label")>]
    let label = function
        | Accumulating -> "Accumulating"
        | Distributing -> "Distributing"
        | Sweep -> "Sweep"
        | OtherDistribution other -> other

[<RequireQualifiedAccess>]
module SweepVehicle =

    [<CompiledName("Label")>]
    let label = function
        | MoneyMarketFund -> "MoneyMarketFund"
        | BankDeposit -> "BankDeposit"
        | Repo -> "Repo"
        | OtherVehicle other -> other

[<RequireQualifiedAccess>]
module SecurityTermModules =
    let empty = {
        Maturity = None
        Coupon = None
        Discount = None
        FloatingRate = None
        Accrual = None
        Payment = None
        Redemption = None
        Call = None
        Auction = None
        Sweep = None
        Financing = None
        Issuer = None
        EquityBehavior = None
        DepositaryReceipt = None
        Fund = None
        TradingParameters = None
    }
