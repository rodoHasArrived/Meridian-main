namespace Meridian.Contracts.Workstation;

/// <summary>Versioned observation captured with an actual canonical workflow commit.</summary>
public sealed record OperationsCloseChecklistSnapshotDto(
    int SchemaVersion, long WorkflowVersion, IReadOnlyList<OperationsCloseChecklistGateSnapshotDto> Gates);
public sealed record OperationsCloseChecklistGateSnapshotDto(
    string TaskId, OperationsGateKeyDto Gate, OperationsGateStatusDto Status);
public sealed record OperationsCloseChecklistObservationDto(
    Guid AuditId, DateTimeOffset RecordedAtUtc, long WorkflowVersion,
    int TotalTasks, int RemainingTasks, int BlockedTasks, int Segment);
public sealed record OperationsCloseChecklistHistoryDto(
    Guid WorkflowId, DateTimeOffset AsOf, string Status, string? Reason,
    DateTimeOffset? CoverageStart, DateTimeOffset? CoverageEnd, bool IsPartial,
    IReadOnlyList<Guid> MissingAuditIds, IReadOnlyList<OperationsCloseChecklistObservationDto> Observations)
{
    public IReadOnlyList<OperationsCloseChecklistVersionGapDto> VersionGaps { get; init; } = [];
}
public sealed record OperationsCloseChecklistVersionGapDto(long AfterVersion, long BeforeVersion, Guid BeforeAuditId);
