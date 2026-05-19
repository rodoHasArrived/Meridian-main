using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityValidationSeverityDto>))]
public enum SecurityValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record SecurityEvidenceLinkDto(
    string EvidenceKind,
    string EvidenceId,
    string? Route,
    string? Summary);

public sealed record SecurityValidationIssueDto(
    SecurityValidationSeverityDto Severity,
    string Code,
    string Title,
    string Message,
    IReadOnlyList<string> AffectedFields,
    string SuggestedAction,
    IReadOnlyList<SecurityEvidenceLinkDto> EvidenceLinks);

public sealed record SecurityValidationReportDto(
    Guid? SecurityId,
    string Scope,
    DateTimeOffset EvaluatedAtUtc,
    bool HasBlockingIssues,
    int CriticalIssueCount,
    int ErrorIssueCount,
    IReadOnlyList<SecurityValidationIssueDto> Issues);
