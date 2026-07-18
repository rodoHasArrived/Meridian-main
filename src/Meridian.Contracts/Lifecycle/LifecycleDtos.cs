namespace Meridian.Contracts.Lifecycle;

public sealed record RuntimeLifecycleCheckDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LifecycleCheckRequirement Requirement { get; init; }
    public required LifecycleCheckStatus Status { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public double DurationMilliseconds { get; init; }
}

public sealed record RuntimeLifecycleSnapshotDto
{
    public required string SessionId { get; init; }
    public required RuntimeLifecycleState State { get; init; }
    public required RuntimeReadinessStatus Readiness { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset StateChangedAtUtc { get; init; }
    public required string ActivePhase { get; init; }
    public required bool AcceptingWork { get; init; }
    public required bool ShutdownRequested { get; init; }
    public string? ShutdownReason { get; init; }
    public string? ActiveShutdownOperationId { get; init; }
    public int? ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public int? Port { get; init; }
    public string? ConfigPath { get; init; }
    public double UptimeSeconds { get; init; }
    public IReadOnlyList<RuntimeLifecycleCheckDto> Checks { get; init; } = [];
}

public sealed record LifecycleShutdownRequestDto
{
    public LifecycleShutdownReason Reason { get; init; } = LifecycleShutdownReason.Operator;
    public string? Detail { get; init; }
    public string? RequestedBy { get; init; }
}

public sealed record LifecycleShutdownAcceptedDto
{
    public required bool Accepted { get; init; }
    public required string OperationId { get; init; }
    public required string OperationUri { get; init; }
    public required RuntimeLifecycleState State { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record LifecycleShutdownStageDto
{
    public required LifecycleShutdownStage Stage { get; init; }
    public required LifecycleShutdownOutcome Outcome { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? Message { get; init; }
}

public sealed record LifecycleShutdownOperationDto
{
    public required string OperationId { get; init; }
    public required LifecycleShutdownReason Reason { get; init; }
    public string? Detail { get; init; }
    public string? RequestedBy { get; init; }
    public required LifecycleShutdownStage CurrentStage { get; init; }
    public required LifecycleShutdownOutcome Outcome { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public IReadOnlyList<LifecycleShutdownStageDto> Stages { get; init; } = [];
}

public sealed record LifecycleShutdownParticipantReceiptDto
{
    public required string ParticipantId { get; init; }
    public required LifecycleShutdownStage Stage { get; init; }
    public required LifecycleShutdownOutcome Outcome { get; init; }
    public required bool Critical { get; init; }
    public required double DurationMilliseconds { get; init; }
    public string? Message { get; init; }
}

public sealed record LifecycleShutdownReceiptDto
{
    public required string SessionId { get; init; }
    public required string OperationId { get; init; }
    public required LifecycleShutdownReason Reason { get; init; }
    public required LifecycleShutdownOutcome Outcome { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required bool ForcedTermination { get; init; }
    public IReadOnlyList<LifecycleShutdownParticipantReceiptDto> Participants { get; init; } = [];
}

public sealed record LifecycleSessionReceiptDto
{
    public required string SessionId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required LifecycleShutdownOutcome Outcome { get; init; }
    public LifecycleShutdownReceiptDto? HostReceipt { get; init; }
    public LifecycleShutdownOutcome? DatabaseOutcome { get; init; }
    public required bool HostForced { get; init; }
    public required bool DatabaseForced { get; init; }
    public string? Message { get; init; }
}
