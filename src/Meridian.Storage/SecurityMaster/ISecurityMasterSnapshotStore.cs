using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Loads and saves versioned security snapshots used for event stream reconstruction.
/// </summary>
public interface ISecurityMasterSnapshotStore
{
    Task<SecuritySnapshotRecord?> LoadAsync(Guid securityId, CancellationToken ct = default);
    Task SaveAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default);

    /// <summary>The securities that hold a retained snapshot, in ascending identifier order.</summary>
    Task<IReadOnlyList<Guid>> ListSecurityIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the payload of the retained snapshot only while it still holds
    /// <paramref name="snapshot"/>'s version, so a regeneration never overwrites a snapshot a
    /// concurrent write saved at a newer version. The timestamp is replaced with the payload.
    /// </summary>
    Task<SecuritySnapshotReplaceOutcome> ReplaceAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="ISecurityMasterSnapshotStore.ReplaceAsync"/>.</summary>
public enum SecuritySnapshotReplaceOutcome
{
    /// <summary>The retained payload differed and was replaced.</summary>
    Replaced,

    /// <summary>The retained payload already equals the replacement; nothing was written.</summary>
    Unchanged,

    /// <summary>The snapshot is gone or holds another version; nothing was written.</summary>
    VersionChanged
}
