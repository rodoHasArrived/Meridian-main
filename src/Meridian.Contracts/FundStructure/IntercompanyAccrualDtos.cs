using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Interpretation identity of the governed accrual calculation; a rule change needs a new version.</summary>
public static class IntercompanyAccrualRules
{
    public const string RuleVersion = "intercompany-accrual-v1";
    /// <summary>
    /// v1 plus floating-rate simple interest: each schedule period uses the approved fixing of its declared index,
    /// observed the declared lag before the period start, plus the declared spread. v1 windows stay v1.
    /// </summary>
    public const string FloatingRuleVersion = "intercompany-accrual-v2";
    public static bool IsSupported(string? ruleVersion) => ruleVersion is RuleVersion or FloatingRuleVersion;
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
    /// <summary>Approved rate fixings the server resolved for the declared indices; never caller-supplied.</summary>
    public IReadOnlyList<IntercompanyApprovedRateFixingDto> Fixings { get; init; } = [];
}

/// <summary>The approved fixing that set one floating slice's rate: <c>AnnualRate = IndexRate + SpreadBps / 10,000</c>.</summary>
public sealed record IntercompanyAccrualFixingPinDto(Guid FixingId, string IndexReference, DateOnly FixingDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal IndexRate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal SpreadBps,
    int FixingLagBusinessDays, string ValueDigest);

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
    /// <summary>Floating slices only (rule v2). Absent on fixed slices, so v1 evidence keeps its exact text.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntercompanyAccrualFixingPinDto? Fixing { get; init; }
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
