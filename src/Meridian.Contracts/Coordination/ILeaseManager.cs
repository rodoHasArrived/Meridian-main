namespace Meridian.Contracts.Coordination;

public interface ILeaseManager
{
    bool Enabled { get; }
    string InstanceId { get; }

    Task<LeaseAcquireResult> TryAcquireAsync(string resourceId, CancellationToken ct = default);

    /// <summary>Acquires a unique run owner, including in single-instance mode.</summary>
    Task<ExecutionLeaseAcquireResult> TryAcquireExecutionAsync(string resourceId, CancellationToken ct = default)
        => throw new NotSupportedException("This lease manager does not support fenced execution.");

    Task<bool> RenewAsync(string resourceId, CancellationToken ct = default);

    Task<bool> ReleaseAsync(string resourceId, CancellationToken ct = default);

    bool HoldsLease(string resourceId);

    Task<CoordinationSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
