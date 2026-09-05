namespace Meridian.Contracts.Workstation;

/// <summary>The explicitly selected subject of a close decision. No dimension is inferred from a workflow.</summary>
public sealed record CloseReadinessScopeDto(
    string? FundProfileId,
    Guid? LedgerBookId,
    Guid? FundAccountId,
    string? EntityId,
    string? PeriodId);

/// <summary>A contributor's retained records and evaluation posture for the selected close scope.</summary>
public sealed record CloseReadinessContributionDto(
    string ContributorId,
    string Owner,
    string Status,
    DateTimeOffset? EvaluatedAtUtc,
    IReadOnlyList<string> RecordIds);

public sealed record CloseReadinessBlockerDto(
    string Code,
    string ContributorId,
    string Type,
    int Count,
    string Severity,
    string Owner,
    string Message,
    IReadOnlyList<string> RecordIds);

/// <summary>Shared server-owned close decision; incomplete evidence is always blocking.</summary>
public sealed record CloseReadinessProjectionDto(
    CloseReadinessScopeDto Scope,
    DateTimeOffset EvaluatedAtUtc,
    string Status,
    bool IsComplete,
    bool IsReadyToClose,
    IReadOnlyList<CloseReadinessContributionDto> Contributors,
    IReadOnlyList<CloseReadinessBlockerDto> Blockers);
