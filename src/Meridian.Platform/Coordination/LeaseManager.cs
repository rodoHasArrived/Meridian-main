using System.Collections.Concurrent;
using Meridian.Contracts.Coordination;
using Meridian.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Platform.Coordination;

/// <summary>
/// Platform-owned lease manager with automatic renewal and operator diagnostics.
/// </summary>
public sealed partial class LeaseManager : ILeaseManager, IAsyncDisposable
{
    private readonly CoordinationConfig _config;
    private readonly ICoordinationStore _store;
    private readonly ILogger<LeaseManager> _log;
    private readonly ConcurrentDictionary<string, LeaseRecord> _heldLeases = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _renewalTask;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private int _conflictCount;
    private int _takeoverCount;
    private int _renewalFailureCount;

    public LeaseManager(CoordinationConfig config, ICoordinationStore store, ILogger<LeaseManager>? log = null)
    {
        _config = config;
        _store = store;
        _log = log ?? NullLogger<LeaseManager>.Instance;

        if (Enabled)
            _renewalTask = RunRenewalLoopAsync(_cts.Token);
    }

    public bool Enabled => _config.IsSharedStorageEnabled;

    public string InstanceId => _config.GetResolvedInstanceId();

    public async Task<LeaseAcquireResult> TryAcquireAsync(string resourceId, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return new LeaseAcquireResult(
                true,
                false,
                new LeaseRecord(resourceId, InstanceId, 1, DateTimeOffset.UtcNow, DateTimeOffset.MaxValue, DateTimeOffset.UtcNow),
                null,
                null,
                null);
        }

        var result = await _store.TryAcquireLeaseAsync(
            resourceId,
            InstanceId,
            TimeSpan.FromSeconds(_config.LeaseTtlSeconds),
            TimeSpan.FromSeconds(_config.TakeoverDelaySeconds),
            ct).ConfigureAwait(false);

        if (result.Acquired && result.Lease is not null)
        {
            _heldLeases[resourceId] = result.Lease;
            if (result.TakenOver)
            {
                Interlocked.Increment(ref _takeoverCount);
                _log.LogWarning("Took over expired lease for {ResourceId} from {PreviousOwner}", resourceId, result.CurrentOwner);
            }
        }
        else
        {
            Interlocked.Increment(ref _conflictCount);
            _log.LogDebug(
                "Lease conflict for {ResourceId}; owner={Owner}, expires={Expiry}",
                resourceId,
                result.CurrentOwner,
                result.CurrentExpiryUtc);
        }

        return result;
    }

    public async Task<bool> RenewAsync(string resourceId, CancellationToken ct = default)
    {
        if (!Enabled)
            return true;

        var renewed = await _store.RenewLeaseAsync(
            resourceId,
            InstanceId,
            TimeSpan.FromSeconds(_config.LeaseTtlSeconds),
            ct).ConfigureAwait(false);

        if (!renewed)
        {
            _heldLeases.TryRemove(resourceId, out _);
            Interlocked.Increment(ref _renewalFailureCount);
            _log.LogWarning("Failed to renew lease for {ResourceId}", resourceId);
            return false;
        }

        var refreshed = await _store.GetLeaseAsync(resourceId, ct).ConfigureAwait(false);
        if (refreshed is not null)
            _heldLeases[resourceId] = refreshed;

        return true;
    }

    public async Task<bool> ReleaseAsync(string resourceId, CancellationToken ct = default)
    {
        _heldLeases.TryRemove(resourceId, out _);
        if (!Enabled)
            return true;

        return await _store.ReleaseLeaseAsync(resourceId, InstanceId, ct).ConfigureAwait(false);
    }

    public bool HoldsLease(string resourceId)
        => !Enabled || _heldLeases.ContainsKey(resourceId);

    public async Task<CoordinationSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var executionOwners = _executionLeases.Keys.Select(lease => lease.OwnerId).ToHashSet(StringComparer.Ordinal);
        var allLeases = Enabled || executionOwners.Count > 0
            ? await _store.GetAllLeasesAsync(ct).ConfigureAwait(false)
            : Array.Empty<LeaseRecord>();
        var held = _heldLeases.Values.Concat(allLeases.Where(lease => executionOwners.Contains(lease.InstanceId)))
            .OrderBy(lease => lease.ResourceId, StringComparer.OrdinalIgnoreCase).ToList();
        var corrupted = Enabled
            ? await _store.GetCorruptedLeaseFilesAsync(ct).ConfigureAwait(false)
            : Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        var orphaned = allLeases
            .Where(l => l.ExpiresAtUtc < now)
            .Select(l => l.ResourceId)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CoordinationSnapshot
        {
            Enabled = Enabled,
            Mode = _config.Mode.ToString(),
            InstanceId = InstanceId,
            RootPath = _store.RootPath,
            HeldLeaseCount = held.Count,
            SymbolLeaseCount = CountByPrefix(held, "symbols/"),
            ScheduleLeaseCount = CountByPrefix(held, "schedules/"),
            JobLeaseCount = CountByPrefix(held, "jobs/"),
            LeaderLeaseCount = CountByPrefix(held, "leader/"),
            ConflictCount = _conflictCount,
            TakeoverCount = _takeoverCount,
            RenewalFailureCount = _renewalFailureCount,
            OrphanedLeaseCount = orphaned.Count,
            CorruptedLeaseCount = corrupted.Count,
            HeldLeases = held,
            OrphanedResources = orphaned,
            CorruptedLeaseFiles = corrupted,
            CapturedAtUtc = now
        };
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Lease-manager shutdown callback failed; continuing ownership cleanup");
        }

        if (_renewalTask is not null)
        {
            try
            {
                await _renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during disposal.
            }
        }

        foreach (var execution in _executionLeases.Keys)
            await execution.DisposeAsync().ConfigureAwait(false);

        foreach (var resourceId in _heldLeases.Keys.ToArray())
        {
            try
            {
                await ReleaseAsync(resourceId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed releasing lease for {ResourceId} during disposal", resourceId);
            }
        }

        _cts.Dispose();
    }

    private async Task RunRenewalLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _config.RenewIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            foreach (var resourceId in _heldLeases.Keys.ToArray())
            {
                try
                {
                    await RenewAsync(resourceId, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _heldLeases.TryRemove(resourceId, out _);
                    Interlocked.Increment(ref _renewalFailureCount);
                    _log.LogWarning(ex, "Unexpected renewal failure for {ResourceId}", resourceId);
                }
            }
        }
    }

    private static int CountByPrefix(IEnumerable<LeaseRecord> leases, string prefix)
        => leases.Count(l => l.ResourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
