using Meridian.Contracts.Coordination;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Platform.Coordination;

/// <summary>
/// Background service that detects and heals split-brain conditions in the Meridian cluster.
/// A split-brain occurs when two instances simultaneously believe they hold the coordinator lease,
/// typically caused by a transient network partition or storage hiccup.
/// </summary>
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

    internal async Task DetectAndHealAsync(CancellationToken ct)
    {
        var allLeases = await _store.GetAllLeasesAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var coordinatorLeases = allLeases
            .Where(l => l.ResourceId.StartsWith(
                ClusterCoordinatorService.CoordinatorLeaseId,
                StringComparison.OrdinalIgnoreCase))
            .Where(l => l.ExpiresAtUtc > now)
            .ToList();

        if (coordinatorLeases.Count <= 1)
        {
            CheckForDoubleSubscribedSymbols(allLeases, now);
            return;
        }

        _logger.LogWarning(
            "SplitBrainDetected: {Count} active coordinator leases found. Instances: [{Instances}]",
            coordinatorLeases.Count,
            string.Join(", ", coordinatorLeases.Select(l => l.InstanceId)));

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
