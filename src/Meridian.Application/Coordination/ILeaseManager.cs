namespace Meridian.Application.Coordination;

public interface ILeaseManager
{
    bool Enabled { get; }
    string InstanceId { get; }

    Task<LeaseAcquireResult> TryAcquireAsync(string resourceId, CancellationToken ct = default);

    Task<bool> RenewAsync(string resourceId, CancellationToken ct = default);

    Task<bool> ReleaseAsync(string resourceId, CancellationToken ct = default);

    bool HoldsLease(string resourceId);

    Task<CoordinationSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
