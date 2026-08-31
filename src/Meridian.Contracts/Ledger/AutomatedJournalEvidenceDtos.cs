using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>Trust grade assigned to the retained evidence behind an automated journal draft.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomatedJournalEvidenceQualityDto>))]
public enum AutomatedJournalEvidenceQualityDto
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Persisted assessment of the source evidence used to prepare an automated journal draft.
/// A draft that requires investigation remains in the manual workbench but cannot enter the
/// human approval lifecycle until new evidence produces a satisfactory assessment.
/// </summary>
public sealed record AutomatedJournalEvidenceAssessmentDto(
    string AssessmentCode,
    decimal ConfidenceScore,
    AutomatedJournalEvidenceQualityDto Quality,
    bool RequiresInvestigation,
    string Summary,
    IReadOnlyList<string>? Reasons = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> Reasons { get; init; } = Reasons ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}
