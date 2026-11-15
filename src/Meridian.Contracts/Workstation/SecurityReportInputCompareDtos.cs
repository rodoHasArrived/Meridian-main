using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Workstation;

public sealed record SecurityReportRateValueDto(string Field, string RawValue, string? Unit,
    string? NormalizedFraction, string? Reason);
public sealed record SecurityRetainedReportInputDto(Guid JournalEntryId, string ReceiptSha256,
    SecurityDetailDto Security, string ModelVersion, JsonElement CalculationContext,
    JsonElement Source, IReadOnlyList<SecurityReportRateValueDto> Rates, string ChangeProvenance)
{
    public SecurityReportChangeAttributionDto? Attribution { get; init; }
}
public sealed record SecurityReportChangeAttributionDto(long SecurityVersion, string? EventActor,
    DateTimeOffset? EventRecordedAt, Guid? RevisionId, string? DraftedBy, string? SubmittedBy,
    string? ApprovedBy, DateTimeOffset? ApprovedAt, string? ApprovalId, string? EvidenceRoute,
    IReadOnlyList<string> Gaps);
public sealed record SecurityReportInputCompareRunDto(string RunId, DateOnly ReportAsOf,
    DateTimeOffset SourceCutoffUtc, DateTimeOffset ReleasedAtUtc,
    string? SourceCheckpointHash, IReadOnlyList<SecurityRetainedReportInputDto> Inputs,
    IReadOnlyList<string> Gaps);
public sealed record SecurityReportInputCompareDto(Guid SecurityId, SecurityDetailDto? CurrentCanonical,
    IReadOnlyList<SecurityReportRateValueDto> CurrentRates,
    IReadOnlyList<SecurityReportInputCompareRunDto> Reports, IReadOnlyList<string> Gaps);
public sealed record SecurityReportInputLinkDto(Guid SecurityId, Guid JournalEntryId, bool ExactInputAvailable);
