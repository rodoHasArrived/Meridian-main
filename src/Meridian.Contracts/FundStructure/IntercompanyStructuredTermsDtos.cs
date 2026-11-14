using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyRateBasisDto>))]
public enum IntercompanyRateBasisDto { Fixed, Floating }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyCompoundingDto>))]
public enum IntercompanyCompoundingDto { Simple, Monthly, Quarterly, SemiAnnual, Annual, Daily }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyFrequencyDto>))]
public enum IntercompanyFrequencyDto { Monthly, Quarterly, SemiAnnual, Annual, AtMaturity }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyDayCountDto>))]
public enum IntercompanyDayCountDto
{
    Actual360, Actual365, Thirty360, Business252, ActualActualIsda,
    ThirtyE360, ThirtyE360Isda, ActualActualIcma, Nl365, Actual36525
}

public sealed record IntercompanyScheduleDto(DateOnly StartDate, DateOnly EndDate,
    IntercompanyFrequencyDto Frequency, string CalendarId, BusinessDayConvention BusinessDayConvention,
    int SettlementBusinessDays = 0);
public sealed record IntercompanyAllocationDto(Guid EntityId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Percent);
public sealed record IntercompanyGuaranteeTermsDto(string BeneficiaryReference,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? LimitAmount = null);

/// <summary>Versioned declarations. Presence does not authorize accrual, posting, settlement or FX conversion.</summary>
public sealed record IntercompanyStructuredTermsDto(
    IntercompanyScheduleDto? Schedule = null, IntercompanyRateBasisDto? RateBasis = null,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? FixedAnnualRate = null,
    string? FloatingIndexReference = null,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? SpreadBps = null,
    IntercompanyDayCountDto? DayCount = null, IntercompanyCompoundingDto? Compounding = null,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? FixedFeeAmount = null,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? AnnualFeeRate = null,
    string? CalculationBaseReference = null, IReadOnlyList<IntercompanyAllocationDto>? Allocations = null,
    string? AllocationDriverReference = null, IntercompanyGuaranteeTermsDto? Guarantee = null, int SchemaVersion = 1);

public sealed record IntercompanyStructuredTermsPreviewRequestDto(Guid AgreementId, int? AgreementVersion = null, Guid? ProposalId = null);
public sealed record IntercompanyDatePeriodDto(int Sequence, DateOnly AccrualStart, DateOnly AccrualEnd, DateOnly PaymentDate);
public sealed record IntercompanyStructuredTermsPreviewDto(string Status, string TermsFormat, string SubjectHash,
    string KernelVersion, string? CalendarId, string? CalendarHash,
    IReadOnlyList<IntercompanyDatePeriodDto> Periods, IReadOnlyList<string> Diagnostics);

/// <summary>A pure date preview over server-resolved legal facts; no ledger or journal dependency.</summary>
public interface IIntercompanyStructuredTermsPreviewService
{
    IntercompanyStructuredTermsPreviewDto Preview(IntercompanyAgreementDraftDto draft, string subjectHash);
}
