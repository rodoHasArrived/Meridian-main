namespace Meridian.Contracts.Workstation;

/// <summary>Server-assessed mark support for a single position and valuation date.</summary>
public sealed record MarkFreshnessAssessmentDto(
    string Symbol,
    Guid? SecurityId,
    string? FinancialAccountId,
    DateOnly ValuationDate,
    DateOnly? ObservedOn,
    int? AgeDays,
    string PolicyVersion,
    string Status,
    string? BlockReason);

/// <summary>Read-only impact preview; no draft, approval, schedule, or journal is retained.</summary>
public sealed record ValuationFreshnessPreviewDto(
    string PolicyVersion,
    int AssessedPositionCount,
    int BlockedPositionCount,
    int AffectedValuationCount,
    IReadOnlyList<MarkFreshnessAssessmentDto> Positions,
    DateTimeOffset EvaluatedAtUtc);
