namespace Meridian.FSharp.Domain

open System

type MaturityTerms = {
    EffectiveDate: DateOnly option
    IssueDate: DateOnly option
    MaturityDate: DateOnly option
}

type CouponTerms = {
    CouponType: string option
    CouponRate: decimal option
    PaymentFrequency: string option
    DayCount: string option
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
}

type PaymentTerms = {
    PaymentFrequency: string option
    PaymentLagDays: int option
    PaymentCurrency: string option
}

type RedemptionTerms = {
    RedemptionType: string option
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
    SweepVehicleType: string option
    SweepFrequency: string option
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
    DistributionType: string option
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
    Fund: FundTerms option
    TradingParameters: TradingParams option
}

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
        Fund = None
        TradingParameters = None
    }
