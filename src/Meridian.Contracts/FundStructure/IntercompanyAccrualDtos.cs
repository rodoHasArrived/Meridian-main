using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Interpretation identity of the governed accrual calculation; a rule change needs a new version.</summary>
public static class IntercompanyAccrualRules
{
    public const string RuleVersion = "intercompany-accrual-v1";
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
    IReadOnlyList<IntercompanyAccrualPrincipalSegmentDto> Principal);

/// <summary>One homogeneous slice: one legal revision, one profile revision, one schedule period, one principal.</summary>
public sealed record IntercompanyAccrualSliceDto(int Sequence, DateOnly From, DateOnly To,
    Guid AccountingArrangementRevisionId, int AgreementVersion, string AgreementPayloadHash, int SchedulePeriodSequence,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Principal,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal AnnualRate,
    string DayCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal YearFraction,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal UnroundedAmount,
    string PrincipalEvidenceReference);

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
