namespace Meridian.Application.Coordination;

public interface ICoordinationStore
{
    Task<LeaseAcquireResult> TryAcquireLeaseAsync(
        string resourceId,
        string instanceId,
        TimeSpan leaseTtl,
        TimeSpan takeoverDelay,
        CancellationToken ct = default);

    Task<bool> RenewLeaseAsync(
        string resourceId,
        string instanceId,
        TimeSpan leaseTtl,
        CancellationToken ct = default);

    Task<bool> ReleaseLeaseAsync(
        string resourceId,
        string instanceId,
        CancellationToken ct = default);

    Task<LeaseRecord?> GetLeaseAsync(string resourceId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaseRecord>> GetAllLeasesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetCorruptedLeaseFilesAsync(CancellationToken ct = default);

    string RootPath { get; }
}
