using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Coordination;

/// <summary>
/// Background service that detects and heals split-brain conditions in the Meridian cluster.
/// A split-brain occurs when two instances simultaneously believe they hold the coordinator lease —
/// typically caused by a transient network partition or storage hiccup.
/// </summary>
/// <remarks>
/// <para>
/// Every 5 seconds this service reads all active coordinator leases from the shared store.
/// If exactly one (or zero) coordinator lease is found, the cluster is healthy and a secondary
/// check scans for symbols subscribed by multiple instances simultaneously.
/// </para>
/// <para>
/// When two or more coordinator leases are detected, the instance with the lexicographically
/// lower <see cref="IClusterCoordinator.InstanceId"/> yields its lease by calling
/// <see cref="IClusterCoordinator.StepDownAsync"/>. This deterministic tiebreak guarantees
/// convergence without requiring any additional external coordination.
/// </para>
/// </remarks>
public sealed class SplitBrainDetector : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly ICoordinationStore _store;
    private readonly IClusterCoordinator _coordinator;
    private readonly ILogger<SplitBrainDetector> _logger;

    public SplitBrainDetector(
        ICoordinationStore store,
        IClusterCoordinator coordinator,
        ILogger<SplitBrainDetector> logger)
    {
        _store = store;
        _coordinator = coordinator;
        _logger = logger;
    }

    /// <summary>Runs the split-brain detection loop until the application stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "SplitBrainDetector started on {InstanceId}", _coordinator.InstanceId);

        using var timer = new PeriodicTimer(CheckInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                await DetectAndHealAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SplitBrainDetector encountered an error on {InstanceId}", _coordinator.InstanceId);
            }
        }

        _logger.LogDebug(
            "SplitBrainDetector stopped on {InstanceId}", _coordinator.InstanceId);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    internal async Task DetectAndHealAsync(CancellationToken ct)
    {
        var allLeases = await _store.GetAllLeasesAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        // Active coordinator leases (expired ones are stale and will be cleaned up by LeaseManager).
        var coordinatorLeases = allLeases
            .Where(l => l.ResourceId.StartsWith(
                ClusterCoordinatorService.CoordinatorLeaseId,
                StringComparison.OrdinalIgnoreCase))
            .Where(l => l.ExpiresAtUtc > now)
            .ToList();

        if (coordinatorLeases.Count <= 1)
        {
            // Healthy single-leader state — run the secondary subscription check only.
            CheckForDoubleSubscribedSymbols(allLeases, now);
            return;
        }

        // ── Split-brain detected ──────────────────────────────────────────
        _logger.LogWarning(
            "SplitBrainDetected: {Count} active coordinator leases found. Instances: [{Instances}]",
            coordinatorLeases.Count,
            string.Join(", ", coordinatorLeases.Select(l => l.InstanceId)));

        // Deterministic resolution: the instance with the lexicographically lowest InstanceId yields.
        var yieldingInstanceId = coordinatorLeases
            .Select(l => l.InstanceId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .First();

        if (string.Equals(_coordinator.InstanceId, yieldingInstanceId, StringComparison.Ordinal)
            && _coordinator.IsLeader)
        {
            _logger.LogWarning(
                "SplitBrainResolved: Instance {InstanceId} yielding coordinator lease " +
                "(lowest InstanceId loses tie-break)",
                _coordinator.InstanceId);

            await _coordinator.StepDownAsync(ct).ConfigureAwait(false);
        }
    }

    private void CheckForDoubleSubscribedSymbols(
        IReadOnlyList<LeaseRecord> allLeases,
        DateTimeOffset now)
    {
        // A symbol is double-subscribed when two different instances hold an active lease
        // for the same symbols/* resource ID simultaneously.
        var doubleSubscribed = allLeases
            .Where(l => l.ResourceId.StartsWith("symbols/", StringComparison.OrdinalIgnoreCase))
            .Where(l => l.ExpiresAtUtc > now)
            .GroupBy(l => l.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(l => l.InstanceId)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Count() > 1)
            .ToList();

        if (doubleSubscribed.Count > 0)
        {
            _logger.LogWarning(
                "SplitBrainDetected: {Count} symbol(s) are subscribed by multiple instances simultaneously: [{Symbols}]",
                doubleSubscribed.Count,
                string.Join(", ", doubleSubscribed.Select(g => g.Key)));
        }
    }
}
