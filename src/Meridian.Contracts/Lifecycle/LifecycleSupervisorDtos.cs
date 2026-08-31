namespace Meridian.Contracts.Lifecycle;

public sealed record LifecycleSupervisorManifestDto
{
    public int SchemaVersion { get; init; } = 1;
    public LifecycleDatabaseManagementMode DatabaseMode { get; init; } = LifecycleDatabaseManagementMode.Dedicated;
    public string HostRelativePath { get; init; } = Path.Combine("host", "Meridian.exe");
    public string? ConfigPath { get; init; }
    public string? DataRoot { get; init; }
    public int? HttpPort { get; init; }
    public int DatabasePort { get; init; } = 54329;
    public string? PostgreSqlBinPath { get; init; }
    public string? ExternalConnectionStringEnvironmentVariable { get; init; }
    public int StartupTimeoutSeconds { get; init; } = 60;
    public int ShutdownTimeoutSeconds { get; init; } = 45;
    public int DatabaseTimeoutSeconds { get; init; } = 60;
}

public sealed record LifecycleOwnedProcessDto
{
    public required int ProcessId { get; init; }
    public required string ExecutablePath { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
}

public sealed record LifecycleDatabaseIdentityDto
{
    public required LifecycleDatabaseManagementMode Mode { get; init; }
    public int? ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public string? DataDirectory { get; init; }
    public required int Port { get; init; }
}

public sealed record LifecycleSupervisorStatusDto
{
    public required bool Running { get; init; }
    public required string PipeName { get; init; }
    public required string ManifestPath { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public int? HttpPort { get; init; }
    public LifecycleOwnedProcessDto? Host { get; init; }
    public LifecycleDatabaseIdentityDto? Database { get; init; }
    public RuntimeLifecycleSnapshotDto? HostLifecycle { get; init; }
    public LifecycleSessionReceiptDto? LatestSessionReceipt { get; init; }
    public string? Message { get; init; }
}

public sealed record LifecycleSupervisorMessageDto
{
    public required string Command { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public string? SessionId { get; init; }
    public string? Reason { get; init; }
    public string? Detail { get; init; }
    public LifecycleSupervisorStatusDto? Status { get; init; }
    public RuntimeLifecycleSnapshotDto? Lifecycle { get; init; }
    public LifecycleShutdownAcceptedDto? ShutdownAccepted { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}
