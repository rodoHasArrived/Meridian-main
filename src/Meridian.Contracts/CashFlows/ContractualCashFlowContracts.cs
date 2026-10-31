using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.CashFlows;

[JsonConverter(typeof(JsonStringEnumConverter<CashFlowExerciseStyle>))]
public enum CashFlowExerciseStyle { European, American, Bermudan }
[JsonConverter(typeof(JsonStringEnumConverter<CashFlowExerciseSide>))]
public enum CashFlowExerciseSide { Call, Put }
[JsonConverter(typeof(JsonStringEnumConverter<CashFlowPrepaymentMethod>))]
public enum CashFlowPrepaymentMethod { Cpr, Smm, Psa, Amount }
[JsonConverter(typeof(JsonStringEnumConverter<CashFlowRecastPolicy>))]
public enum CashFlowRecastPolicy { PreservePayments, Recast }
[JsonConverter(typeof(JsonStringEnumConverter<CashFlowAmortization>))]
public enum CashFlowAmortization { Bullet, ExplicitPrincipal, EqualPrincipal, LevelPayment }
[JsonConverter(typeof(JsonStringEnumConverter<MakeWholeFormula>))]
public enum MakeWholeFormula { BenchmarkCurvePvV1, BenchmarkYieldPvV1 }

/// <summary>Versioned calculation contract. Rates are fractions, prices are clean percentages.</summary>
public sealed record ContractualCashFlowContract(
    DateOnly IssueDate,
    DateOnly MaturityDate,
    string Currency,
    IReadOnlyList<ContractualCashFlowStream> Streams,
    IReadOnlyList<ContractualExerciseEntry>? Exercises = null,
    int Version = 1);

/// <summary>Explicit periods replace generation for this stream; no contract date is inferred.</summary>
public sealed record ContractualCashFlowStream(
    string StreamId,
    decimal Principal,
    int PaymentMonths,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayCountConvention>))] DayCountConvention DayCount,
    decimal? FixedRate,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CashFlowLegDirection>))] CashFlowLegDirection Direction = CashFlowLegDirection.Receive,
    bool ExchangesPrincipal = true,
    ScheduleConventionTerms? Conventions = null,
    IReadOnlyList<SchedulePeriod>? CouponPeriods = null,
    IReadOnlyList<StructuredPrincipalScheduleEntry>? PrincipalSchedule = null,
    IReadOnlyList<StructuredCashFlowNotionalStep>? NotionalSchedule = null,
    IReadOnlyList<StructuredStepCouponEntry>? RateSteps = null,
    IReadOnlyList<ContractualCouponAmount>? CouponAmounts = null,
    string? IndexName = null,
    decimal SpreadBps = 0m,
    IReadOnlyList<StructuredCashFlowResetFixing>? Resets = null,
    decimal? Floor = null,
    decimal? Cap = null,
    decimal PikFraction = 0m,
    CashFlowAmortization Amortization = CashFlowAmortization.Bullet,
    bool PrepaymentAllowed = false,
    decimal PrepaymentPenaltyRate = 0m,
    decimal? OpeningBalance = null,
    DateOnly? OpeningBalanceDate = null);

public sealed record ContractualCouponAmount(DateOnly PaymentDate, decimal Amount);

/// <summary>Companion to the frozen call/put entry, identified by side and unadjusted start.</summary>
public sealed record ContractualExerciseEntry(
    CashFlowExerciseSide Side,
    DateOnly StartDate,
    DateOnly? EndDate,
    CashFlowExerciseStyle Style,
    decimal? CleanPrice,
    string StreamId,
    int? ExerciseMonths = null,
    string? PartialRedemption = null,
    int? MinimumNoticeDays = null,
    int? MaximumNoticeDays = null,
    string? NoticeDayType = null,
    string PriceBasis = "PercentOfPar",
    decimal? AccretedValue = null,
    ContractualMakeWholeTerms? MakeWhole = null);

public sealed record ContractualMakeWholeTerms(
    MakeWholeFormula Formula,
    string BenchmarkKey,
    decimal SpreadBps,
    int CompoundsPerYear,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayCountConvention>))] DayCountConvention DayCount,
    DateOnly? ParCallDate = null,
    string AccruedInterestTreatment = "CleanFloorAddAccrued");

public sealed record SelectedCashFlowExercise(
    CashFlowExerciseSide Side,
    DateOnly EntryStartDate,
    string StreamId,
    DateOnly ExerciseDate,
    DateOnly NoticeDate,
    decimal Amount,
    bool HypotheticalNotice = true,
    string? NoticeEvidenceId = null,
    IReadOnlyList<decimal>? SelectedLotAmounts = null);

/// <summary>Intervals are [StartDate, EndDate); Amount assumptions pay on StartDate.</summary>
public sealed record CashFlowPrepaymentAssumption(
    string StreamId,
    DateOnly StartDate,
    DateOnly EndDate,
    CashFlowPrepaymentMethod Method,
    decimal Value,
    DateOnly? SeasoningOrigin = null,
    CashFlowRecastPolicy Policy = CashFlowRecastPolicy.PreservePayments);

public sealed record CashFlowCurvePin(string BenchmarkKey, Guid CurveId, Guid CurveVersionId);

/// <summary>ContractOverride is explicitly hypothetical; it never changes authoritative terms.</summary>
public sealed record ContractualCashFlowRequest(
    DateOnly ValuationDate,
    DateTimeOffset KnowledgeCutoff,
    DateOnly? Horizon = null,
    StructuredCashFlowScenario RateScenario = StructuredCashFlowScenario.Base,
    IReadOnlyList<SelectedCashFlowExercise>? Exercises = null,
    IReadOnlyList<CashFlowPrepaymentAssumption>? Prepayments = null,
    IReadOnlyList<CashFlowCurvePin>? CurvePins = null,
    ContractualCashFlowContract? ContractOverride = null,
    string EngineVersion = ContractualCashFlowVersions.Engine);

public static class ContractualCashFlowVersions
{
    public const string Engine = "contractual-cashflow-v1";
    public const string Mapping = "contractual-cashflow-mapping-v1";
    public const string Prepayment = "survival-cpr-smm-psa-v1";
}

public sealed record CashFlowCalculationBlocker(string Code, string Message, string? StreamId = null);
public sealed record CashFlowNoticeWindow(DateOnly? Earliest, DateOnly? Latest);
/// <summary>American windows are represented as a predicate over the named calendar, not daily rows.</summary>
public sealed record CashFlowExerciseEligibility(
    CashFlowExerciseSide Side, DateOnly EntryStartDate, string StreamId,
    DateOnly WindowEnd, CashFlowExerciseStyle Style, IReadOnlyList<DateOnly> Dates,
    string? BusinessDayCalendarId);
public sealed record CashFlowExerciseCalculation(
    SelectedCashFlowExercise Selection, CashFlowNoticeWindow NoticeWindow,
    decimal DirtyPresentValue, decimal AccruedInterest, decimal CleanFloor,
    decimal DirtyConsideration, decimal Premium);
public sealed record ContractualCashFlowEvent(
    string EventId, string StreamId, string? PeriodId, DateOnly Date,
    DateOnly? AccrualStart, DateOnly? AccrualEnd,
    decimal OpeningBalance, decimal CashInterest, decimal CapitalizedInterest,
    decimal ScheduledPrincipal, decimal ForecastPrepayment, decimal RedeemedPrincipal,
    decimal RedemptionPremium, decimal PrepaymentPenalty, decimal ClosingBalance,
    CashFlowLegDirection Direction)
{
    public decimal NetCash => (Direction == CashFlowLegDirection.Pay ? -1m : 1m)
        * (CashInterest + ScheduledPrincipal + ForecastPrepayment + RedeemedPrincipal + RedemptionPremium + PrepaymentPenalty);
}
public sealed record CashFlowCalendarSnapshot(
    string CalendarId, string ContentHash, DateOnly CoverageStart, DateOnly CoverageEnd,
    IReadOnlyList<DateOnly> NonBusinessDays,
    DateOnly? SourceCoverageStart = null, DateOnly? SourceCoverageEnd = null);
public sealed record CashFlowCurveSnapshot(SecurityCurveDefinitionDto Definition, SecurityCurveVersionDto Version);
public sealed record ContractualCashFlowEvidence(
    string EngineVersion, string MappingVersion, string PrepaymentVersion, string KernelVersion,
    string InputHash, string OutputHash, string SourceTermsHash, string SourceVersion,
    bool HasContractOverride, IReadOnlyList<CashFlowCalendarSnapshot> Calendars,
    IReadOnlyList<CashFlowCurveSnapshot> Curves, IReadOnlyList<string> Warnings);
public sealed record ContractualCashFlowResult(
    IReadOnlyList<ContractualCashFlowEvent> Events,
    IReadOnlyList<CashFlowExerciseEligibility> ExerciseEligibility,
    IReadOnlyList<CashFlowExerciseCalculation> ExerciseCalculations,
    IReadOnlyList<CashFlowCalculationBlocker> Blockers,
    ContractualCashFlowEvidence Evidence)
{
    public ContractualCashFlowContract? ResolvedContract { get; init; }
    public bool IsPostable => false;
    public bool IsCalculated => Blockers.Count == 0;
}

/// <summary>Complete immutable replay packet; no live term/calendar/curve lookup is needed.</summary>
public sealed record ContractualCashFlowRun(
    Guid RunId, string SubjectKind, Guid SubjectId, DateTimeOffset RecordedAt,
    ContractualCashFlowContract Contract, ContractualCashFlowRequest Request,
    ContractualCashFlowResult Result,
    string Actor = "");

public sealed record CreateContractualCashFlowRunRequest(Guid RunId, ContractualCashFlowRequest Projection);

public interface IContractualCashFlowRunStore
{
    Task<ContractualCashFlowRun?> GetAsync(string subjectKind, Guid subjectId, Guid runId, CancellationToken ct = default);
    Task<ContractualCashFlowRun> SaveAsync(ContractualCashFlowRun run, CancellationToken ct = default);
}

public interface IContractualCashFlowPreviewService
{
    Task<ContractualCashFlowResult?> PreviewSecurityAsync(Guid securityId, ContractualCashFlowRequest request, CancellationToken ct = default);
    Task<ContractualCashFlowResult?> PreviewLoanAsync(Guid loanId, ContractualCashFlowRequest request, CancellationToken ct = default);
}
