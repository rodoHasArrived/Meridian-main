using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Interpretation identity of the governed fixed management-fee calculation; a rule change needs a new version.</summary>
public static class IntercompanyFeeAccrualRules
{
    public const string RuleVersion = "intercompany-fee-v1";
}

/// <summary>Server-resolved facts only: approved profile revisions and their pinned legal revisions by day.</summary>
public sealed record IntercompanyFeeAccrualComputationInput(DateOnly AccrualFrom, DateOnly AccrualTo,
    IReadOnlyList<IntercompanyAccrualAuthoritySegment> Authority);

/// <summary>
/// One homogeneous fee slice: one legal revision, one profile revision, one schedule period. Its fraction is the
/// share of the declared fee it earns: covered days over the period's days (per period), or the declared day-count
/// fraction of a year (per annum).
/// </summary>
public sealed record IntercompanyFeeAccrualSliceDto(int Sequence, DateOnly From, DateOnly To,
    Guid AccountingArrangementRevisionId, int AgreementVersion, string AgreementPayloadHash,
    int SchedulePeriodSequence, DateOnly PeriodStart, DateOnly PeriodEnd, IntercompanyFixedFeeBasisDto Basis,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal DeclaredFee,
    string? DayCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Fraction,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal UnroundedAmount);

/// <summary>
/// A calculated fee is a proposal input, never a posting. <c>Unavailable</c> carries no amount: an undeclared basis,
/// a percentage fee or a day outside the declared schedule is refused rather than estimated.
/// </summary>
public sealed record IntercompanyFeeAccrualPreviewDto(string Status, string RuleVersion, Guid? AgreementId, string? Currency,
    DateOnly AccrualFrom, DateOnly AccrualTo,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Amount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? UnroundedAmount,
    string KernelVersion, IReadOnlyList<IntercompanyAccrualCalendarPinDto> Calendars,
    IReadOnlyList<IntercompanyFeeAccrualSliceDto> Slices, IReadOnlyList<string> Diagnostics)
{
    [JsonIgnore] public bool IsReady => Status == IntercompanyAccrualRules.StatusReady;
}

/// <summary>Pure fee calculation over server-resolved legal and profile facts.</summary>
public interface IIntercompanyFeeAccrualPreviewService
{
    IntercompanyFeeAccrualPreviewDto Preview(IntercompanyFeeAccrualComputationInput input);
}
