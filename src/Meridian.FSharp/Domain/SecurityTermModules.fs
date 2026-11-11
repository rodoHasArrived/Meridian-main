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
type DayCountConvention = Meridian.Contracts.SecurityMaster.DayCountConvention

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

/// A dated coupon-rate step: the annual rate payable from <c>EffectiveDate</c> until the next
/// entry's effective date (or maturity for the last entry). The term data that makes the
/// <see cref="BondSubclass.StepRate"/> and <see cref="BondSubclass.FixedToFloat"/> taxonomy
/// computable instead of a label.
type StepCouponEntry = {
    EffectiveDate: DateOnly
    Rate: decimal
}

[<RequireQualifiedAccess>]
type BondCouponStructure =
    | Fixed of rate: decimal * dayCount: DayCountConvention option
    | Floating of index: string * spreadBps: decimal option * capRate: decimal option * floorRate: decimal option * dayCount: DayCountConvention option
    | ZeroCoupon
    /// Step-rate coupon: a contractual dated rate schedule (step-up notes, step coupons around
    /// call dates). The schedule is the coupon — there is no single scalar rate.
    | Step of schedule: StepCouponEntry list * dayCount: DayCountConvention option
    /// Inflation-linked coupon (TIPS, linkers): a real rate accruing on index-adjusted principal.
    /// <c>indexName</c> names the reference index (e.g. "CPI-U"); <c>baseIndexValue</c> is the
    /// index level at issue and <c>indexRatio</c> the current index ratio applied to principal.
    | InflationLinked of realRate: decimal * indexName: string * baseIndexValue: decimal option * indexRatio: decimal option * dayCount: DayCountConvention option

/// A scheduled principal repayment (amortizing instalment, sinking-fund payment, or term-loan
/// instalment). Shared by bond and direct-loan terms.
type PrincipalPaymentEntry = {
    PaymentDate: DateOnly
    Amount: decimal
}

/// The declared conventions the contractual schedule kernel (ADR-025) builds an instrument's periods
/// with: the flat terms' <c>scheduleConventions</c> block (asset-class terms version 2 on bonds,
/// certificates of deposit and swap legs, 3 on structured credit). A node reads it from payloads
/// stamped at that version and does not write it until the contract step (ADR-024, readable before
/// writable). Text members are kept verbatim, as the payload carries them;
/// <c>ScheduleConventions.empty</c> means nothing is declared.
type ScheduleConventions = {
    BusinessDayConvention: string option
    /// Whether accrual period boundaries are adjusted for business days (payment dates always are,
    /// under <c>BusinessDayConvention</c>). None when the terms do not say.
    AccrualAdjusted: bool option
    /// The payment calendars by id, joined into one calendar when more than one is named.
    PaymentCalendars: string list
    PaymentLagBusinessDays: int option
    RollConvention: string option
    EndOfMonthRule: bool option
    FirstRegularDate: DateOnly option
    LastRegularDate: DateOnly option
    /// Schedule generation direction: Forward or Backward.
    Direction: string option
    StubPolicy: string option
    /// Whether a coupon rate is a percent or a fraction: Percent or Fraction.
    CouponRateUnit: string option
    /// How a regular coupon's cash amount is computed: Level (rate over frequency) or DayCount.
    CouponCashBasis: string option
}

[<RequireQualifiedAccess>]
module ScheduleConventions =
    /// No convention declared: what every record carries until a node writes the block.
    [<CompiledName("Empty")>]
    let empty : ScheduleConventions =
        { BusinessDayConvention = None
          AccrualAdjusted = None
          PaymentCalendars = []
          PaymentLagBusinessDays = None
          RollConvention = None
          EndOfMonthRule = None
          FirstRegularDate = None
          LastRegularDate = None
          Direction = None
          StubPolicy = None
          CouponRateUnit = None
          CouponCashBasis = None }

    /// Whether nothing is declared.
    [<CompiledName("IsEmpty")>]
    let isEmpty (conventions: ScheduleConventions) = conventions = empty

/// One declared call (a flat callSchedule entry: asset-class terms version 3 on bonds and
/// certificates of deposit, 4 on structured credit). Text members are kept verbatim; their meanings
/// are frozen in ADR-025's Applications.
type CallScheduleTerm = {
    /// The call date, or the start of an American or Bermudan call window. An entry without one is
    /// not a term and is never read.
    CallDate: DateOnly
    /// The last day of the call window, inclusive. Absent, the window runs to the day before the
    /// next call's date in date order, or to maturity for the last call (ADR-025).
    CallEndDate: DateOnly option
    /// European, American or Bermudan.
    ExerciseStyle: string option
    /// How often a Bermudan window's call dates recur (a coupon frequency spelling).
    ExerciseFrequency: string option
    /// The call price, in the unit priceBasis names.
    CallPrice: decimal option
    /// PercentOfPar or PercentOfAccretedValue.
    PriceBasis: string option
    /// Fixed or MakeWhole.
    PriceType: string option
    /// The make-whole spread over the benchmark, in basis points.
    MakeWholeSpreadBps: decimal option
    /// WholeOnly, ProRata or ByLot.
    PartialRedemption: string option
    /// Notice of the call falls at least this many days before the exercise date.
    MinimumNoticeDays: int option
    /// Notice of the call falls at most this many days before the exercise date.
    MaximumNoticeDays: int option
    /// Whether the notice counts are Business or Calendar days.
    NoticeDayType: string option
}

/// One declared holder put (a flat putSchedule entry), with the same versions as the call entry. A
/// mandatory tender is not a put entry; it stays MandatoryPutDate.
type PutScheduleTerm = {
    /// The put date, or the start of a put window. An entry without one is never read.
    PutDate: DateOnly
    /// The last day of the put window, inclusive. Absent, the window runs to the day before the
    /// next put's date in date order, or to maturity for the last put (ADR-025).
    PutEndDate: DateOnly option
    /// European, American or Bermudan.
    ExerciseStyle: string option
    /// How often a Bermudan window's put dates recur (a coupon frequency spelling).
    ExerciseFrequency: string option
    /// The put price, in the unit priceBasis names.
    PutPrice: decimal option
    /// PercentOfPar or PercentOfAccretedValue.
    PriceBasis: string option
    /// The holder's election falls at least this many days before the exercise date.
    MinimumNoticeDays: int option
    /// The holder's election falls at most this many days before the exercise date.
    MaximumNoticeDays: int option
    /// Whether the notice counts are Business or Calendar days.
    NoticeDayType: string option
}

/// The declared call and put schedules, in stored order; empty until declared.
type CallPutSchedules = { Calls: CallScheduleTerm list; Puts: PutScheduleTerm list }

[<RequireQualifiedAccess>]
module CallPutSchedules =
    /// Nothing declared: what every record carries until a node writes the schedules.
    [<CompiledName("Empty")>]
    let empty : CallPutSchedules = { Calls = []; Puts = [] }

    /// Whether neither a call nor a put is declared.
    [<CompiledName("IsEmpty")>]
    let isEmpty (schedules: CallPutSchedules) = schedules.Calls.IsEmpty && schedules.Puts.IsEmpty

[<RequireQualifiedAccess>]
module CallScheduleTerm =
    /// A call on the date with nothing else stated.
    let onDate (date: DateOnly) : CallScheduleTerm =
        { CallDate = date
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
          NoticeDayType = None }

[<RequireQualifiedAccess>]
module PutScheduleTerm =
    /// A put on the date with nothing else stated.
    let onDate (date: DateOnly) : PutScheduleTerm =
        { PutDate = date
          PutEndDate = None
          ExerciseStyle = None
          ExerciseFrequency = None
          PutPrice = None
          PriceBasis = None
          MinimumNoticeDays = None
          MaximumNoticeDays = None
          NoticeDayType = None }

/// One step of an amortizing or accreting leg notional: the notional in effect from this date
/// until the next step (or maturity).
type SwapNotionalStep = {
    EffectiveDate: DateOnly
    Notional: decimal
}

/// A floating-leg reset date with the index fixing observed on it, once known.
type SwapResetEntry = {
    ResetDate: DateOnly
    FixingRate: decimal option
}

/// A swap leg with the economics consumed by cash-flow projection. Optional terms keep
/// legacy legs readable and allow the projection to use security-level fallbacks.
type SwapLeg = {
    LegId: string option
    LegType: string
    Currency: string
    /// Pay or Receive from the holder's perspective. None leaves the leg unnetted.
    Direction: string option
    Index: string option
    FixedRate: decimal option
    SpreadBps: decimal option
    /// Observed index fixing used for flat-forward projection, without an implied forward curve.
    CurrentIndexRate: decimal option
    Notional: decimal option
    PaymentFrequency: string option
    DayCount: DayCountConvention option
    /// Whether principal exchanges are part of the leg's contractual terms.
    ExchangesPrincipal: bool
    /// Dated notional steps for an amortizing or accreting leg; empty for a constant notional.
    NotionalSchedule: SwapNotionalStep list
    /// Reset dates with their observed fixings for a floating leg; empty when not tracked.
    ResetSchedule: SwapResetEntry list
    /// The leg's declared schedule conventions (asset-class terms version 2); empty until declared.
    ScheduleConventions: ScheduleConventions
}

type Covenant = {
    CovenantType: string
    Threshold: string
    Notes: string option
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
    /// Auction Rate Security — rate resets at periodic auction; vendor pricing conventions may hold it at par.
    | AuctionRate
    // Loans belong in DirectLoan; securitized products belong in StructuredCredit.
    // Historical labels survive only through Other for tolerant record reconstruction.
    | Other of string

[<RequireQualifiedAccess>]
module BondSubclass =
    /// Canonical serialization label. Named cases use the case name; <c>Other</c> carries its raw
    /// label — serializing the union's ToString (<c>Other "X"</c>) made every unknown subclass
    /// re-wrap itself one level deeper per codec pass instead of round-tripping.
    let label (subclass: BondSubclass) =
        match subclass with
        | BondSubclass.Other name -> name
        | named -> string named

/// Exact bond classification facts, including historical unknown subclass labels.
type DebtClassificationTerms = { Subclass: BondSubclass; Seniority: string option }

/// Face amount and distinct legal/redemption dates; none is a redemption price or call-date alias.
type BondRedemptionTerms = {
    Par: decimal option
    LegalFinalMaturity: DateOnly option
    PreRefundDate: DateOnly option
    MandatoryPutDate: DateOnly option
}

[<RequireQualifiedAccess>]
module BondRedemptionTerms =
    let empty = { Par = None; LegalFinalMaturity = None; PreRefundDate = None; MandatoryPutDate = None }

/// Exact coupon structure shared by the domain and the flat codec (ADR-027 S3-E).
type BondCouponTerms = { Structure: BondCouponStructure; PaymentFrequency: PaymentFrequency option }

/// Contractual payments retain stored order and amounts.
type PrincipalScheduleTerms = { Payments: PrincipalPaymentEntry list }

type SwapLegTerms = { Legs: SwapLeg list }

/// Free-text legacy covenant thresholds are retained verbatim, separately from numeric ratios.
type LoanCovenantTerms = { Entries: Covenant list }

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

type CallScheduleEntry = {
    CallDt: DateOnly
    /// Call price as percentage of par (e.g. 102.5 means 102.5% of face value).
    CallPx: decimal option
    IsParCall: bool
    /// e.g. "American", "European", "Bermudan", "MakeWhole".
    CallType: string option
}

type PutScheduleEntry = {
    PutDt: DateOnly
    PutPx: decimal option
}

type EconomicCallTerms = {
    /// Filled from the class's own scalar call terms under the frozen fill (ADR-025): bond
    /// isCallable, certificate-of-deposit callableDate, preferred-equity callableDate. A class without
    /// scalar call terms (structured credit) sets it when a call is declared.
    IsCallable: bool
    /// The class's scalar first call date (bond callDate, certificate-of-deposit callableDate). A
    /// class without one (structured credit) leaves it None even when calls are declared.
    FirstCallDate: DateOnly option
    /// None for every flat class today: none carries a scalar call price. Entry prices are in
    /// CallSchedule.
    CallPrice: decimal option
    /// Full step-up call schedule, sorted ascending by CallDt by the fill (stably); the flat terms
    /// keep their stored order.
    CallSchedule: CallScheduleEntry list
    MakeWholeSpreadBps: decimal option
    IsPuttable: bool
    PutSchedule: PutScheduleEntry list
}

[<RequireQualifiedAccess>]
module EconomicCallTerms =
    /// Stored scalar facts only; declared schedules keep their original order in CallPutSchedules.
    [<CompiledName("Scalar")>]
    let scalar isCallable firstCallDate : EconomicCallTerms =
        { IsCallable = isCallable; FirstCallDate = firstCallDate; CallPrice = None; CallSchedule = []
          MakeWholeSpreadBps = None; IsPuttable = false; PutSchedule = [] }

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

[<RequireQualifiedAccess>]
module IssuerRef =
    let empty : IssuerRef =
        { IssuerName = None; InstitutionName = None; IssuerProgram = None; LeiCode = None
          UltimateParentName = None; IssuerSector = None; IssuerCountry = None }

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
    VotingRights: string option
    DistributionType: DistributionPolicy option
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
    UnderlyingIsin: string option
    UnderlyingCountry: string option
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

/// One dated factor retained by the typed economic module, independent of the legacy flat payload.
type EconomicFactorScheduleEntry = {
    AsOfDate: DateOnly
    Factor: decimal
}

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
    /// Trustee or servicer report reference the factor schedule was taken from (the record's
    /// free-text <c>factorSchedule</c> term). Evidence lineage, not an economic input: no cash-flow,
    /// accrual or accounting math reads it.
    FactorScheduleReference: string option
    /// Typed dated factor history; accounting never reads a second copy from legacy passthrough JSON.
    FactorScheduleEntries: EconomicFactorScheduleEntry list
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

// ---------------------------------------------------------------------------
// Inflation-linked and sinking-fund modules
// ---------------------------------------------------------------------------
// Accounting elections (amortization method, yield updates, cash-flow source) and PCI
// designations are lot- and account-level accounting choices, not security economics. They are
// carried on the lot (see OpenLotAmortization / the asset-accounting acquisition terms), never in
// the Security Master term modules.

/// Inflation index terms for TIPS, index-linked GILTs, and similar instruments.
/// Supports the adjusted-principal calculation: Base_units × Inflation_factor.
type InflationLinkedTerms = {
    /// Index series used to adjust principal (e.g. "CPI-U", "RPI", "HICPxT").
    InflationIndex: string option
    /// Reference index value at the original issue date.
    BaseIndexValue: decimal option
    /// Date of the base index value (usually issue date or original auction date).
    BaseDate: DateOnly option
    /// Most recently published factor: Current_index / Base_index.
    CurrentFactor: decimal option
    /// As-of date for the current factor.
    FactorDate: DateOnly option
    /// Minimum factor floor; 1.0 for US TIPS (principal cannot fall below original par).
    IndexFloor: decimal option
}

/// A single principal instalment in a sinking fund or scheduled-amortizing bond.
type SinkingFundEntry = {
    SinkDate: DateOnly
    /// Par amount retired on this date.
    Amount: decimal
}

/// Sinking fund schedule module for bonds with contractual principal instalments.
type SinkingFundTerms = {
    Schedule: SinkingFundEntry list
    /// Descriptive frequency label (e.g. "Annual", "SemiAnnual").
    SinkFrequency: string option
    /// True when instalments retire bonds pro-rata across all holders via lottery or open-market purchase.
    IsProRata: bool
}

// ---------------------------------------------------------------------------
// ADR-027 stage 3 (S3-E): module homes for the codec-only field complement
// ---------------------------------------------------------------------------

/// Exercise terms for instruments that carry a right: listed options, warrants, swaptions and
/// caps/floors/collars. The home for the strike, the right and the style that the flat codec
/// alone carried before S3-E.
type ExerciseTerms = {
    /// The right the holder owns, as the class spells it: "Put"/"Call" (option, warrant),
    /// "Payer"/"Receiver" (swaption), "Cap"/"Floor"/"Collar" (cap/floor).
    OptionRight: string option
    /// Strike as a price of the underlying (option, warrant).
    StrikePrice: decimal option
    /// Strike as a decimal rate (swaption); may be negative. Cap and floor strike rates stay on
    /// FloatingRateTerms.CapRate/FloorRate, where they bound the reference index.
    StrikeRate: decimal option
    /// "American", "European" or "Bermudan"; None leaves the style undeclared.
    ExerciseStyle: string option
    /// The security the right is exercised into, when it is a security-master record.
    UnderlyingSecurityId: SecurityId option
    /// Premium paid for the right, as an amount (cap/floor).
    PremiumAmount: decimal option
}

/// The currency pair of an FX or crypto instrument, with the agreed rate when the instrument
/// fixes one. The home for the pair fields the flat codec alone carried before S3-E.
type CurrencyPairTerms = {
    BaseCurrency: string option
    QuoteCurrency: string option
    /// Currency the net difference is paid in (non-deliverable forward).
    SettlementCurrency: string option
    /// Agreed forward rate, quote currency per unit of base currency.
    ForwardRate: decimal option
}

/// The amounts a contract is written on: the notional of a derivative, the face of one note,
/// and the committed amount of a commitment with its funded and unfunded split. The home for
/// the amounts the flat codec alone carried before S3-E. (A structured-credit tranche's original
/// face stays on StructuredProductTerms.NotionalBalance; swap leg notionals stay on the legs.)
type NotionalTerms = {
    /// Contract notional (FX forwards in the base currency; CDS, swaption, cap/floor).
    Notional: decimal option
    /// Face amount of one unit (structured note).
    Denomination: decimal option
    /// Total committed amount (private fund commitment, commitment or guarantee).
    CommittedAmount: decimal option
    FundedAmount: decimal option
    UnfundedAmount: decimal option
}

/// The governance envelope of a profile-backed custom asset together with the profile-governed
/// terms document it pins, carried opaquely exactly as CustomAssetTerms carries it: profile
/// fields are dynamic and governed by the approved profile version, so the module holds the
/// document rather than modelling per-profile shapes. The home for the envelope the flat codec
/// alone carried before S3-E.
type ProfileEnvelopeTerms = {
    /// Identifier of the approved custom-asset profile governing the record.
    CustomProfileId: string
    /// Version of the approved profile the record's fields were validated against.
    ProfileVersion: int
    /// The complete terms document (envelope, profileFields and any additional governed keys).
    TermsJson: string
}

/// Reference obligation and contractual protection elections of a CDS.
type CreditProtectionTerms = {
    ReferenceEntity: string
    ReferenceObligation: string option
    ProtectionSide: string
    Seniority: string option
    RestructuringClause: string option
    UpfrontPoints: decimal option
    RecoveryRateAssumption: decimal option
    IndexName: string option
}

/// Underlying-linked payoff of an issuer note; coupon and denomination have separate homes.
type StructuredPayoffTerms = {
    UnderlyingDescription: string
    PrincipalProtectionPct: decimal option
    ParticipationRate: decimal option
    ReturnCap: decimal option
    BarrierLevel: decimal option
}

/// Private-asset valuation facts and ownership restrictions, without inventing a mark when absent.
type PrivateAssetValuationTerms = {
    Vintage: int option
    NavDate: DateOnly option
    Lockup: string option
    OwnershipPercent: decimal option
    CostBasis: decimal option
    LatestValuation: decimal option
    TransferRestrictions: string option
    AddressOrMarket: string option
    AppraisalValue: decimal option
    ValuationDate: DateOnly option
    DebtStack: string option
}

/// Listed-contract series, settlement and delivery details retained independently of its right.
type ListingDetailTerms = {
    OptChainId: string option
    SettlementType: string option
    IsAdjusted: bool option
    LastTradingDt: DateOnly option
    RootSymbol: string option
    ContractMonth: string option
    FirstNoticeDt: DateOnly option
    DeliveryMonthDt: DateOnly option
    DeliveryLocationCode: string option
    IsRollTarget: bool option
    RollWindowDays: int option
}

/// Explicit unit of scalar rate fields, independent of schedule generation.
type RateConventionTerms = { RateUnit: string }

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

/// Exact equity capital terms, including the complete preferred/convertible discriminant.
/// These types are shared by the economic model and its flat codec; the economic summaries
/// in dividend/conversion modules remain projections rather than another source of truth.
type EquityCapitalTerms = {
    ShareClass: string option
    VotingRightsCat: VotingRightsCat option
    Classification: EquityClassification option
}

/// Commodity quantity and its declared unit; denomination is text, never monetary face.
type CommodityContractTerms = {
    CommodityType: string
    Denomination: string option
    ContractSize: decimal option
}

/// Network identity of a digital asset, independent of its quoted currency pair.
type DigitalAssetTerms = { Network: string option }

/// Descriptive underlying and exact leverage; leverage is not reconstructed from margin percent.
type UnderlyingExposureTerms = {
    UnderlyingAssetClass: string
    UnderlyingDescription: string option
    Leverage: decimal option
}

/// Contractual settlement method as declared, without assigning an implicit method.
type SettlementTerms = { SettlementType: string option }

/// Raw classification labels retained separately from canonical catalog classification.
type ClassificationDetailTerms = { Category: string; SubType: string option }

/// Fund-share category, NAV currency and distribution behavior.
type FundShareTerms = {
    FundType: string option
    NavCurrency: string option
    DistributionPolicy: DistributionPolicy option
    IsStableNav: bool option
}

/// Operational references only: no pricing or rate-setting authority is inferred from these names.
type ReferenceDataSourceTerms = { PricingSource: string option; FixingSource: string option }

/// Exact scalar interest quote and cadence; no unit conversion or vocabulary normalization.
type ContractualRateTerms = {
    CouponRate: decimal option
    FixedCouponBps: decimal option
    FeeRate: decimal option
    PaymentFrequency: string option
    DayCount: DayCountConvention option
}

/// The underlying swap of a swaption, distinct from the option's expiry dates.
type UnderlyingSwapTerms = {
    MaturityDate: DateOnly
    FloatingIndex: string option
    FixedLegPaymentFrequency: string option
    FixedLegDayCount: DayCountConvention option
}

[<RequireQualifiedAccess>]
type StructuredCreditKind =
    | AgencyMbs
    | NonAgencyMbs
    | CommercialMbs
    | AssetBacked
    | Clo
    | Cdo
    | Cmo
    | InterestOnly
    | PrincipalOnly
    | OtherStructuredCreditKind of string

[<RequireQualifiedAccess>]
module StructuredCreditKind =
    let asString kind =
        match kind with
        | StructuredCreditKind.AgencyMbs -> "AgencyMbs"
        | StructuredCreditKind.NonAgencyMbs -> "NonAgencyMbs"
        | StructuredCreditKind.CommercialMbs -> "CommercialMbs"
        | StructuredCreditKind.AssetBacked -> "AssetBacked"
        | StructuredCreditKind.Clo -> "Clo"
        | StructuredCreditKind.Cdo -> "Cdo"
        | StructuredCreditKind.Cmo -> "Cmo"
        | StructuredCreditKind.InterestOnly -> "InterestOnly"
        | StructuredCreditKind.PrincipalOnly -> "PrincipalOnly"
        | StructuredCreditKind.OtherStructuredCreditKind label -> label

    /// Parses the wire label. Governed labels match case-insensitively, as the taxonomy validator
    /// does, and canonicalise to the named case; an unrecognised label is kept verbatim as
    /// <c>OtherStructuredCreditKind</c>.
    let parse (label: string) =
        let trimmed = label.Trim()
        match trimmed.ToUpperInvariant() with
        | "AGENCYMBS" -> StructuredCreditKind.AgencyMbs
        | "NONAGENCYMBS" -> StructuredCreditKind.NonAgencyMbs
        | "COMMERCIALMBS" -> StructuredCreditKind.CommercialMbs
        | "ASSETBACKED" -> StructuredCreditKind.AssetBacked
        | "CLO" -> StructuredCreditKind.Clo
        | "CDO" -> StructuredCreditKind.Cdo
        | "CMO" -> StructuredCreditKind.Cmo
        | "INTERESTONLY" -> StructuredCreditKind.InterestOnly
        | "PRINCIPALONLY" -> StructuredCreditKind.PrincipalOnly
        | _ -> StructuredCreditKind.OtherStructuredCreditKind trimmed

/// Exact tranche contract terms that the pool analytics summary cannot express.
type TrancheContractTerms = {
    Kind: StructuredCreditKind option
    Seniority: string option
    AttachmentPoint: decimal option
    DetachmentPoint: decimal option
    ReinvestmentPeriodEnd: DateOnly option
    NonCallPeriodEnd: DateOnly option
}

/// Complete declared schedule source, including windows, notice rules and calendar conventions.
/// The older Call/Accrual modules remain compatible summaries, never a replacement for this source.
type DeclaredScheduleTerms = {
    Conventions: ScheduleConventions
    Exercises: CallPutSchedules
}

[<RequireQualifiedAccess>]
module DeclaredScheduleTerms =
    let empty = { Conventions = ScheduleConventions.empty; Exercises = CallPutSchedules.empty }

type SecurityTermModules = {
    DeclaredSchedule: DeclaredScheduleTerms option
    DebtClassification: DebtClassificationTerms option
    BondRedemption: BondRedemptionTerms option
    CommodityContract: CommodityContractTerms option
    DigitalAsset: DigitalAssetTerms option
    UnderlyingExposure: UnderlyingExposureTerms option
    Settlement: SettlementTerms option
    ClassificationDetail: ClassificationDetailTerms option
    FundShare: FundShareTerms option
    ReferenceDataSource: ReferenceDataSourceTerms option
    BondCoupon: BondCouponTerms option
    PrincipalSchedule: PrincipalScheduleTerms option
    SwapLegs: SwapLegTerms option
    LoanCovenants: LoanCovenantTerms option
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
    // --- Inflation-linked and sinking-fund modules ---
    /// Inflation index and factor data for TIPS, index-linked GILTs, and similar instruments.
    InflationLinked: InflationLinkedTerms option
    /// Contractual sinking fund or scheduled principal instalment schedule.
    SinkingFund: SinkingFundTerms option
    // --- ADR-027 stage 3 (S3-E) module homes ---
    /// Strike, right, style and underlying for options, warrants, swaptions and caps/floors.
    Exercise: ExerciseTerms option
    /// Base/quote (and settlement) currencies and the agreed rate for FX and crypto instruments.
    CurrencyPair: CurrencyPairTerms option
    /// Notional, denomination and commitment amounts.
    Notional: NotionalTerms option
    /// Profile pin and governed terms document of a profile-backed custom asset.
    ContractualRate: ContractualRateTerms option
    UnderlyingSwap: UnderlyingSwapTerms option
    RateConvention: RateConventionTerms option
    ProfileEnvelope: ProfileEnvelopeTerms option
    CreditProtection: CreditProtectionTerms option
    StructuredPayoff: StructuredPayoffTerms option
    PrivateAssetValuation: PrivateAssetValuationTerms option
    ListingDetail: ListingDetailTerms option
    EquityCapital: EquityCapitalTerms option
    TrancheContract: TrancheContractTerms option
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

    /// Inverse of label for the canonical labels; any other text is carried verbatim as
    /// OtherFrequency, matching the C# mapping's ToPaymentFrequencyOption.
    [<CompiledName("Parse")>]
    let parse (value: string) =
        match value.Trim() with
        | "Daily" -> Daily
        | "Weekly" -> Weekly
        | "Monthly" -> Monthly
        | "Quarterly" -> Quarterly
        | "SemiAnnual" -> SemiAnnual
        | "Annual" -> Annual
        | other -> OtherFrequency other

[<RequireQualifiedAccess>]
module DayCountConvention =

    [<CompiledName("Label")>]
    let label (convention: DayCountConvention) =
        Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel(convention)

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
module DividendFrequency =

    [<CompiledName("Label")>]
    let label = function
        | DividendFrequency.Annual -> "Annual"
        | DividendFrequency.SemiAnnual -> "SemiAnnual"
        | DividendFrequency.Quarterly -> "Quarterly"
        | DividendFrequency.Monthly -> "Monthly"
        | DividendFrequency.Irregular -> "Irregular"
        | DividendFrequency.NoDividend -> "NoDividend"

[<RequireQualifiedAccess>]
module CovenantKind =

    [<CompiledName("Label")>]
    let label = function
        | CovenantKind.FinancialMaintenance -> "FinancialMaintenance"
        | CovenantKind.Incurrence -> "Incurrence"
        | CovenantKind.Negative -> "Negative"
        | CovenantKind.Affirmative -> "Affirmative"

[<RequireQualifiedAccess>]
module SecurityTermModules =
    let empty = {
        DeclaredSchedule = None
        CommodityContract = None
        DigitalAsset = None
        UnderlyingExposure = None
        Settlement = None
        ClassificationDetail = None
        FundShare = None
        ReferenceDataSource = None
        DebtClassification = None
        BondRedemption = None
        BondCoupon = None
        PrincipalSchedule = None
        SwapLegs = None
        LoanCovenants = None
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
        InflationLinked = None
        SinkingFund = None
        Exercise = None
        CurrencyPair = None
        Notional = None
        ContractualRate = None
        UnderlyingSwap = None
        RateConvention = None
        ProfileEnvelope = None
        CreditProtection = None
        StructuredPayoff = None
        PrivateAssetValuation = None
        ListingDetail = None
        EquityCapital = None
        TrancheContract = None
    }
