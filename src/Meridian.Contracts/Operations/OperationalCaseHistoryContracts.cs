namespace Meridian.Contracts.Operations;

public sealed record OperationalCaseStateTransition
{
    public string? PreviousState { get; init; }
    public required string CurrentState { get; init; }
    public required DateTimeOffset TransitionedAtUtc { get; init; }
}

public sealed record OperationalCaseAssignment
{
    public string? PreviousAssigneeId { get; init; }
    public string? AssigneeId { get; init; }
    public required string AssignedBy { get; init; }
    public required DateTimeOffset AssignedAtUtc { get; init; }
}

public sealed record OperationalCaseRetry
{
    public required int Attempt { get; init; }
    public required DateTimeOffset AttemptedAtUtc { get; init; }
    public required string Reason { get; init; }
}

public sealed record OperationalCaseException
{
    public required string ExceptionType { get; init; }
    public required string Message { get; init; }
    public string? StackTrace { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

public sealed record OperationalCaseApproval
{
    public required string ApprovalId { get; init; }
    public required string Decision { get; init; }
    public required string DecidedBy { get; init; }
    public required DateTimeOffset DecidedAtUtc { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

public sealed record OperationalCaseRecoveryAttempt
{
    public required string RecoveryActionId { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required string Result { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public IReadOnlyList<string> ArtifactIds { get; init; } = [];
}

public sealed record OperationalCaseHistoryAppendRequest
{
    public required string CaseId { get; init; }
    public required string CaseType { get; init; }
    public required string HistoryEventId { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string ActorId { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
    public required string InputHashSha256 { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public OperationalCaseStateTransition? Transition { get; init; }
    public OperationalCaseAssignment? Assignment { get; init; }
    public IReadOnlyList<OperationalCaseRetry> Retries { get; init; } = [];
    public IReadOnlyList<OperationalCaseException> Exceptions { get; init; } = [];
    public IReadOnlyList<OperationalCaseApproval> Approvals { get; init; } = [];
    public IReadOnlyList<OperationArtifactReference> Artifacts { get; init; } = [];
    public IReadOnlyList<OperationEvidenceReference> Evidence { get; init; } = [];
    public IReadOnlyList<OperationalCaseRecoveryAttempt> RecoveryAttempts { get; init; } = [];
    public VerifiedOperationOutcome? TerminalOutcome { get; init; }
}

public sealed record OperationalCaseHistoryRecord
{
    public required string CaseId { get; init; }
    public required string CaseType { get; init; }
    public required string HistoryEventId { get; init; }
    public required string EventType { get; init; }
    public required long Sequence { get; init; }
    public string? PreviousRecordHashSha256 { get; init; }
    public required string RecordHashSha256 { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required DateTimeOffset PersistedAtUtc { get; init; }
    public required string ActorId { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
    public required string InputHashSha256 { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public OperationalCaseStateTransition? Transition { get; init; }
    public OperationalCaseAssignment? Assignment { get; init; }
    public IReadOnlyList<OperationalCaseRetry> Retries { get; init; } = [];
    public IReadOnlyList<OperationalCaseException> Exceptions { get; init; } = [];
    public IReadOnlyList<OperationalCaseApproval> Approvals { get; init; } = [];
    public IReadOnlyList<OperationArtifactReference> Artifacts { get; init; } = [];
    public IReadOnlyList<OperationEvidenceReference> Evidence { get; init; } = [];
    public IReadOnlyList<OperationalCaseRecoveryAttempt> RecoveryAttempts { get; init; } = [];
    public VerifiedOperationOutcome? TerminalOutcome { get; init; }
}

public sealed record OperationalCaseHistoryQuery
{
    public string? CaseId { get; init; }
    public string? CaseType { get; init; }
    public long? AfterSequence { get; init; }
}
