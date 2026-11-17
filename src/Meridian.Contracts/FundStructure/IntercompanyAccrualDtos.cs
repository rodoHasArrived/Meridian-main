using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Interpretation identity of the governed accrual calculation; a rule change needs a new version.</summary>
public static class IntercompanyAccrualRules
{
    public const string RuleVersion = "intercompany-accrual-v1";
    /// <summary>
    /// v1 plus periodic compounding: each schedule period's base is posted principal plus the accrued-interest
    /// balance posted on the loan-interest profile at the period start, once every earlier day has a posted accrual.
    /// </summary>
    public const string CompoundingRuleVersion = "intercompany-accrual-v3";
    public static bool IsSupported(string? ruleVersion) => ruleVersion is RuleVersion or CompoundingRuleVersion;
    public const string StatusReady = "Ready";
    public const string StatusUnavailable = "Unavailable";
    /// <summary>A single request never spans more than one year of daily authority resolution.</summary>
    public const int MaximumWindowDays = 366;
}

/// <summary>One approved accounting-profile revision and its pinned legal revision over <c>[From, To)</c>.</summary>
public sealed record IntercompanyAccrualAuthoritySegment(DateOnly From, DateOnly To,
    Guid AccountingArrangementId, Guid AccountingArrangementRevisionId, IntercompanyAgreementRevisionDto LegalRevision);

/// <summary>Outstanding principal over <c>[From, To)</c> with the retained source that established it.</summary>
public sealed record IntercompanyAccrualPrincipalSegmentDto(DateOnly From, DateOnly To,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Principal,
    string EvidenceReference);

/// <summary>Server-resolved facts only; no caller-supplied rate, day count or amount participates.</summary>
public sealed record IntercompanyAccrualComputationInput(DateOnly AccrualFrom, DateOnly AccrualTo,
    IReadOnlyList<IntercompanyAccrualAuthoritySegment> Authority,
    IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> Principal)
{
    /// <summary>Compounding loans: posted accrued-interest balance of the loan-interest profile by day, from the schedule start.</summary>
    public IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> AccruedInterest { get; init; } = [];
    /// <summary>Compounding loans: windows of posted, unreversed accruals of the loan-interest profile in this book.</summary>
    public IReadOnlyList<IntercompanyAccrualWindowDto> PostedAccrualWindows { get; init; } = [];
    /// <summary>Compounding loans: effective date of the first posted principal movement, where accrual coverage must start.</summary>
    public DateOnly? FirstPrincipalDate { get; init; }
}

/// <summary>One posted accrual's half-open window.</summary>
public sealed record IntercompanyAccrualWindowDto(DateOnly From, DateOnly To);

/// <summary>One homogeneous slice: one legal revision, one profile revision, one schedule period, one principal.</summary>
public sealed record IntercompanyAccrualSliceDto(int Sequence, DateOnly From, DateOnly To,
    Guid AccountingArrangementRevisionId, int AgreementVersion, string AgreementPayloadHash, int SchedulePeriodSequence,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Principal,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal AnnualRate,
    string DayCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal YearFraction,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal UnroundedAmount,
    string PrincipalEvidenceReference)
{
    /// <summary>
    /// Compounding slices only (rule v3): the posted accrued interest added to principal at the period start. The
    /// slice earns <c>(Principal + CompoundedInterest) x AnnualRate x YearFraction</c>. Absent on simple slices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public decimal? CompoundedInterest { get; init; }
}

public sealed record IntercompanyAccrualCalendarPinDto(string CalendarId, string ContentHash);

/// <summary>
/// A calculated amount is a proposal input, never a posting. <c>Unavailable</c> carries no amount: a partial
/// window, an undeclared term or a missing authority is refused rather than estimated.
/// </summary>
public sealed record IntercompanyAccrualPreviewDto(string Status, string RuleVersion, Guid? AgreementId, string? Currency,
    DateOnly AccrualFrom, DateOnly AccrualTo,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Amount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? UnroundedAmount,
    string KernelVersion, IReadOnlyList<IntercompanyAccrualCalendarPinDto> Calendars,
    IReadOnlyList<IntercompanyAccrualSliceDto> Slices, IReadOnlyList<string> Diagnostics)
{
    [JsonIgnore] public bool IsReady => Status == IntercompanyAccrualRules.StatusReady;
}

/// <summary>Pure amount calculation over server-resolved legal, profile and principal facts.</summary>
public interface IIntercompanyAccrualPreviewService
{
    IntercompanyAccrualPreviewDto Preview(IntercompanyAccrualComputationInput input);
}
