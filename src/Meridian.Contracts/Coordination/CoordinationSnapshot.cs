namespace Meridian.Application.Coordination;

public sealed record CoordinationSnapshot
{
    public bool Enabled { get; init; }
    public string Mode { get; init; } = "SingleInstance";
    public string InstanceId { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public int HeldLeaseCount { get; init; }
    public int SymbolLeaseCount { get; init; }
    public int ScheduleLeaseCount { get; init; }
    public int JobLeaseCount { get; init; }
    public int LeaderLeaseCount { get; init; }
    public int ConflictCount { get; init; }
    public int TakeoverCount { get; init; }
    public int RenewalFailureCount { get; init; }
    public int OrphanedLeaseCount { get; init; }
    public int CorruptedLeaseCount { get; init; }
    public IReadOnlyList<LeaseRecord> HeldLeases { get; init; } = Array.Empty<LeaseRecord>();
    public IReadOnlyList<string> OrphanedResources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CorruptedLeaseFiles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
