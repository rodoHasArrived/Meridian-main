using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterSnapshotStore
{
    Task<SecuritySnapshotRecord?> LoadAsync(Guid securityId, CancellationToken ct = default);
    Task SaveAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default);
}
