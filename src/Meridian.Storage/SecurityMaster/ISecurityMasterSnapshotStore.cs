using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Loads and saves versioned security snapshots used for event stream reconstruction.
/// </summary>
public interface ISecurityMasterSnapshotStore
{
    Task<SecuritySnapshotRecord?> LoadAsync(Guid securityId, CancellationToken ct = default);
    Task SaveAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default);
}
