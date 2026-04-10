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

type CallScheduleEntry = {
    CallDt: DateOnly
    /// Call price as percentage of par (e.g. 102.5 means 102.5% of face value).
    CallPx: decimal option
    IsParCall: bool
    /// e.g. "American", "European", "Bermudan", "Makewhole".
    CallType: string option
}

type PutScheduleEntry = {
    PutDt: DateOnly
    PutPx: decimal option
}

type EconomicCallTerms = {
    IsCallable: bool
    /// Convenience field — mirrors the first entry in CallSchedule for quick access.
    FirstCallDate: DateOnly option
    /// Convenience field — mirrors the first entry's price for quick access.
    CallPrice: decimal option
    /// Full step-up call schedule ordered ascending by CallDt.
    CallSchedule: CallScheduleEntry list
    MakeWholeSpreadBps: decimal option
    IsPuttable: bool
    PutSchedule: PutScheduleEntry list
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

/// Enriched issuer reference with LEI linkage and parent hierarchy.
/// Replaces the flat IssuerTerms record.
type IssuerRef = {
    IssuerName: string option
    InstitutionName: string option
    IssuerProgram: string option
    /// 20-character Legal Entity Identifier (ISO 17442); denormalized from the Identifiers list for fast access.
    LeiCode: string option
    UltimateParentName: string option
    /// Short free-text sector (e.g. "Technology"); full taxonomy is in SecurityClassification.Taxonomy.
    IssuerSector: string option
    IssuerCountry: string option
}

/// Voting rights category for equity instruments.
[<RequireQualifiedAccess>]
type VotingRightsCat =
    | FullVoting
    | LimitedVoting
    | NonVoting
    | DualClass
    | SuperVoting
    | OtherVotingRights of string

[<RequireQualifiedAccess>]
module VotingRightsCat =
    let asString cat =
        match cat with
        | VotingRightsCat.FullVoting -> "FullVoting"
        | VotingRightsCat.LimitedVoting -> "LimitedVoting"
        | VotingRightsCat.NonVoting -> "NonVoting"
        | VotingRightsCat.DualClass -> "DualClass"
        | VotingRightsCat.SuperVoting -> "SuperVoting"
        | VotingRightsCat.OtherVotingRights v -> v

type EquityBehaviorTerms = {
    ShareClass: string option
    VotingRightsCat: VotingRightsCat option
    DistributionType: string option
    /// Total shares issued (including restricted).
    SharesOutstanding: int64 option
    /// Free-float share count (excludes insider / restricted holdings).
    FloatShares: int64 option
    IsRestricted: bool
    IsDualClass: bool
    IsPrimaryListing: bool
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

// ---------------------------------------------------------------------------
// New term modules (P2 / P3 expansion)
// ---------------------------------------------------------------------------

/// Per-agency credit rating entry.
type RatingEntry = {
    /// Rating agency name (e.g. "Moody's", "S&P", "Fitch", "DBRS").
    Agency: string
    /// Long-form rating grade (e.g. "Baa2", "BBB-").
    Grade: string
    Outlook: string option
    /// Level of the rating (e.g. "Issuer", "Instrument", "Senior").
    IssuanceLevel: string option
    AsOfDt: DateOnly
}

/// Credit rating module — supports multiple agencies and maintains current ratings per agency.
type CreditRatingTerms = {
    Ratings: RatingEntry list
    /// Meridian-computed composite rating string (e.g. "BBB+").
    CompositeRating: string option
    IsInvestmentGrade: bool
    IsHighYield: bool
}

/// Depositary receipt terms — for ADR, GDR, and similar instruments.
type DepositaryReceiptTerms = {
    /// SecurityId of the underlying ordinary share in the security master.
    UnderlyingSecurityId: SecurityId option
    DepositaryBank: string option
    /// Number of underlying ordinary shares per depositary receipt unit.
    OrdinaryShareRatio: decimal option
    /// ISO 10383 MIC of the exchange where the underlying ordinary share trades.
    UnderlyingExchangeMic: string option
    UnderlyingCurrency: string option
    CusipOfOrdinary: string option
}

/// Dividend frequency for equity dividend schedule.
[<RequireQualifiedAccess>]
type DividendFrequency =
    | Annual
    | SemiAnnual
    | Quarterly
    | Monthly
    | Irregular
    | NoDividend

/// Dividend schedule module — snapshot of dividend cadence for common and preferred equity.
type DividendScheduleTerms = {
    DividendFrequency: DividendFrequency
    /// Trailing 12-month gross dividend per share.
    AnnualDividendRate: decimal option
    LastExDate: DateOnly option
    LastPayDate: DateOnly option
    LastDividendPerShare: decimal option
    /// Currency of dividend payments (may differ from security's own currency).
    DividendCurrency: string option
    /// True when dividends qualify for preferential US tax treatment.
    IsQualifiedDividend: bool
}

/// Covenant kind for bond and loan covenants.
[<RequireQualifiedAccess>]
type CovenantKind =
    | FinancialMaintenance  // must maintain a ratio (e.g. leverage ≤ 4x)
    | Incurrence            // triggered on a specific action (e.g. taking on new debt)
    | Negative              // prohibits certain actions
    | Affirmative           // requires certain actions

/// A single covenant on a bond or loan.
type CovenantEntry = {
    Kind: CovenantKind
    Description: string
    Threshold: decimal option
    /// Metric name, e.g. "Net Debt / EBITDA".
    Metric: string option
    TestFrequency: string option
}

/// Covenant module — applicable to bonds, loans, and direct credit instruments.
type CovenantTerms = {
    Covenants: CovenantEntry list
}

/// ESG / sustainability classification module.
type EsgTerms = {
    /// Normalised score 0–100.
    EsgScore: decimal option
    EsgProvider: string option
    /// Provider-native rating string (e.g. "AA", "B", "CCC", "High Risk").
    EsgRating: string option
    EsgAsOf: DateOnly option
    /// Green Bond Principles (ICMA) aligned issuance.
    IsGreenBond: bool
    IsSocialBond: bool
    IsSustainabilityLinked: bool
    /// UN Sustainable Development Goals (e.g. ["SDG 7", "SDG 13"]).
    UNSDGAlignments: string list
    /// SFDR Principal Adverse Impact indicator flags.
    PaiIndicatorFlags: string list
}

/// Venue / market structure terms — distinguishes listed vs. OTC instruments.
type VenueTerms = {
    IsListedOnExchange: bool
    PrimaryExchMic: string option
    ExecutionVenueMic: string option
    CcpName: string option
    /// OTC master agreement protocol (e.g. "ISDA", "GMRA").
    OtcProtocol: string option
}

// ---------------------------------------------------------------------------
// Structured / factorable bond terms (MBS, CMO, CLO, ABS, PO, IO, etc.)
// ---------------------------------------------------------------------------

/// Prepayment speed assumption used for cash-flow analytics on factorable bonds.
[<RequireQualifiedAccess>]
type PrepaymentModel =
    /// PSA standard prepayment model expressed as a percentage (e.g. 150.0 = 150% PSA).
    | Psa of speed: decimal
    /// Constant Prepayment Rate — annualised fraction of the remaining pool (e.g. 0.06 = 6% CPR).
    | Cpr of annualRate: decimal
    /// Single Monthly Mortality — monthly equivalent of CPR.
    | Smm of monthlyRate: decimal

/// Analytics and pool-level data for factorable structured-credit instruments
/// (MBS, CMO, CLO, ABS, PO strips, IO strips, CMO residuals, etc.).
type StructuredProductTerms = {
    /// Remaining principal balance as a fraction of original face value (0.0–1.0).
    /// Declines over time as scheduled and unscheduled principal is returned.
    Factor: decimal option
    /// As-of date for the current Factor.
    FactorDate: DateOnly option
    /// Weighted-average coupon of the underlying loan pool (%).
    WeightedAvgCoupon: decimal option
    /// Weighted-average remaining maturity of the pool loans in months.
    WeightedAvgMaturityMonths: int option
    /// Weighted-average loan age of the pool in months (WALA).
    WeightedAvgLoanAgeMos: int option
    /// Collateral asset type backing the pool (e.g. "ResidentialMortgage",
    /// "AutoLoan", "CreditCard", "StudentLoan", "LeveragedLoan").
    CollateralType: string option
    /// Pool or deal identifier (e.g. Fannie Mae pool number, CLO deal CUSIP).
    PoolIdentifier: string option
    /// Tranche class within a multi-class structure (e.g. "A1", "B", "M1", "Z", "IO", "PO").
    TrancheClass: string option
    /// Prepayment speed assumption used for pricing and average-life calculation.
    PrepaymentAssumption: PrepaymentModel option
    /// Estimated average life in years under the prepayment assumption.
    AverageLifeYears: decimal option
    /// True when this security receives only interest cash flows (IO strip or residual).
    IsInterestOnly: bool
    /// True when this security receives only principal cash flows (PO strip).
    IsPrincipalOnly: bool
    /// For IO strips: the outstanding notional balance of the underlying pool.
    NotionalBalance: decimal option
    /// Originator or guarantor name (e.g. "Fannie Mae", "Freddie Mac", "Ginnie Mae").
    Originator: string option
    /// Credit-enhancement level as a percentage of deal balance (subordination, OC, etc.).
    CreditEnhancementPct: decimal option
}

/// Calendar reference for instruments subject to multiple holiday calendars.
type CalendarRef = {
    CalendarId: string
    /// Purpose of this calendar (e.g. "Settlement", "Accrual", "Fixing", "Payment").
    CalendarPurpose: string
}

/// Multi-calendar module for complex instruments (e.g. cross-currency swaps).
type MultiCalendarTerms = {
    Calendars: CalendarRef list
}

type SecurityTermModules = {
    // --- Original 15 modules ---
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
    Issuer: IssuerRef option
    EquityBehavior: EquityBehaviorTerms option
    Fund: FundTerms option
    TradingParameters: TradingParams option
    // --- New modules (P2 / P3 expansion) ---
    CreditRating: CreditRatingTerms option
    DepositaryReceipt: DepositaryReceiptTerms option
    DividendSchedule: DividendScheduleTerms option
    Covenants: CovenantTerms option
    Esg: EsgTerms option
    Venue: VenueTerms option
    MultiCalendar: MultiCalendarTerms option
    // --- Structured / factorable bond module ---
    StructuredProduct: StructuredProductTerms option
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
        CreditRating = None
        DepositaryReceipt = None
        DividendSchedule = None
        Covenants = None
        Esg = None
        Venue = None
        MultiCalendar = None
        StructuredProduct = None
    }
