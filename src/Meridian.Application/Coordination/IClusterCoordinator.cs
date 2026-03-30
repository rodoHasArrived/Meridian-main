namespace Meridian.Application.Coordination;

/// <summary>Event data raised when this instance gains or loses the coordinator role.</summary>
public sealed class LeadershipChangedEventArgs(bool isLeader, string instanceId) : EventArgs
{
    /// <summary>Whether this instance now holds coordinator leadership.</summary>
    public bool IsLeader { get; } = isLeader;

    /// <summary>The instance identifier that changed leadership state.</summary>
    public string InstanceId { get; } = instanceId;
}

/// <summary>
/// Abstraction for the cluster-wide leader election service.
/// Only the elected coordinator instance manages partition assignments and
/// orchestrates cross-instance workflows such as rolling upgrades and
/// distributed backfill scheduling.
/// </summary>
public interface IClusterCoordinator
{
    /// <summary>Gets whether this instance currently holds the coordinator lease.</summary>
    bool IsLeader { get; }

    /// <summary>Gets the stable instance identifier for this node.</summary>
    string InstanceId { get; }

    /// <summary>
    /// Attempts to acquire (or confirm) the coordinator lease.
    /// Safe to call repeatedly; returns <c>false</c> if another healthy instance is already the leader.
    /// </summary>
    Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default);

    /// <summary>
    /// Voluntarily yields coordinator leadership so that another instance may take over.
    /// Used by <see cref="SplitBrainDetector"/> and rolling-upgrade orchestration.
    /// </summary>
    Task StepDownAsync(CancellationToken ct = default);

    /// <summary>Fires when this instance gains or loses the coordinator role.</summary>
    event EventHandler<LeadershipChangedEventArgs>? LeadershipChanged;
}
