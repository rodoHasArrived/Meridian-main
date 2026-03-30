using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Coordination;

/// <summary>
/// Background service that maintains a single cluster coordinator via shared-storage lease competition.
/// Acquires the <c>leader/cluster-coordinator</c> lease on startup and renews it while running.
/// When the lease cannot be acquired or renewed, leadership is relinquished and the instance
/// transitions to follower mode.
/// </summary>
/// <remarks>
/// Registered as both <see cref="IClusterCoordinator"/> (singleton) and a hosted service
/// so that the election loop starts automatically with the application lifetime.
/// </remarks>
public sealed class ClusterCoordinatorService : BackgroundService, IClusterCoordinator
{
    internal const string CoordinatorLeaseId = "leader/cluster-coordinator";

    private readonly ILeaseManager _leaseManager;
    private readonly ILogger<ClusterCoordinatorService> _logger;

    // Volatile so IsLeader reads are visible across threads without a lock.
    private volatile bool _isLeader;

    /// <inheritdoc/>
    public bool IsLeader => _isLeader;

    /// <inheritdoc/>
    public string InstanceId => _leaseManager.InstanceId;

    /// <inheritdoc/>
    public event EventHandler<LeadershipChangedEventArgs>? LeadershipChanged;

    public ClusterCoordinatorService(
        ILeaseManager leaseManager,
        ILogger<ClusterCoordinatorService> logger)
    {
        _leaseManager = leaseManager;
        _logger = logger;
    }

    /// <summary>
    /// Runs the election loop: attempts to become leader every 5 seconds while coordination is enabled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ClusterCoordinatorService starting on instance {InstanceId}", InstanceId);

        if (!_leaseManager.Enabled)
        {
            _logger.LogDebug(
                "Coordination is disabled; ClusterCoordinatorService will not participate in leader election");
            return;
        }

        // Initial attempt on startup, then re-attempt on each interval.
        await TryBecomeLeaderAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                await TryBecomeLeaderAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ClusterCoordinatorService encountered an error during election tick on {InstanceId}",
                    InstanceId);
            }
        }

        // Release the coordinator lease gracefully on shutdown.
        await StepDownAsync(CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation(
            "ClusterCoordinatorService stopped on instance {InstanceId}", InstanceId);
    }

    /// <inheritdoc/>
    public async Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
    {
        if (!_leaseManager.Enabled)
            return false;

        // If we already hold the lease (renewed by LeaseManager's background loop), confirm.
        if (_leaseManager.HoldsLease(CoordinatorLeaseId))
        {
            SetLeadership(true);
            return true;
        }

        var result = await _leaseManager.TryAcquireAsync(CoordinatorLeaseId, ct)
            .ConfigureAwait(false);

        SetLeadership(result.Acquired);

        if (!result.Acquired)
        {
            _logger.LogDebug(
                "Instance {InstanceId} did not acquire coordinator lease. Current owner: {Owner}",
                InstanceId, result.CurrentOwner ?? "unknown");
        }

        return result.Acquired;
    }

    /// <inheritdoc/>
    public async Task StepDownAsync(CancellationToken ct = default)
    {
        if (!_isLeader && !_leaseManager.HoldsLease(CoordinatorLeaseId))
            return;

        await _leaseManager.ReleaseAsync(CoordinatorLeaseId, ct).ConfigureAwait(false);
        SetLeadership(false);

        _logger.LogWarning(
            "Coordinator {InstanceId} stepped down voluntarily", InstanceId);
    }

    private void SetLeadership(bool newValue)
    {
        var previous = _isLeader;
        _isLeader = newValue;

        if (newValue != previous)
        {
            _logger.LogInformation(
                "Coordinator leadership changed: {InstanceId} is now {Role}",
                InstanceId, newValue ? "LEADER" : "FOLLOWER");

            LeadershipChanged?.Invoke(this, new LeadershipChangedEventArgs(newValue, InstanceId));
        }
    }
}
