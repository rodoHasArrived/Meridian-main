using System.Collections.Concurrent;
using Meridian.Contracts.Coordination;
using Microsoft.Extensions.Logging;

namespace Meridian.Platform.Coordination;

public sealed partial class LeaseManager
{
    private readonly ConcurrentDictionary<ExecutionLease, byte> _executionLeases = new();

    public async Task<ExecutionLeaseAcquireResult> TryAcquireExecutionAsync(string resourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        _cts.Token.ThrowIfCancellationRequested();
        var owner = $"{InstanceId}/execution/{Guid.NewGuid():N}";
        var acquired = await _store.TryAcquireLeaseAsync(resourceId, owner,
            TimeSpan.FromSeconds(_config.LeaseTtlSeconds),
            TimeSpan.FromSeconds(_config.TakeoverDelaySeconds), ct).ConfigureAwait(false);
        if (!acquired.Acquired || acquired.Lease is null)
            return new ExecutionLeaseAcquireResult(null, acquired.CurrentOwner);

        ExecutionLease? execution = null;
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                execution = new ExecutionLease(this, acquired.Lease);
                _executionLeases.TryAdd(execution, 0);
            }
        }
        if (execution is not null)
            return new ExecutionLeaseAcquireResult(execution, acquired.CurrentOwner);

        // Store acquisition may finish concurrently with shutdown. Do not create a scope from
        // the disposed lifetime token or leave the newly acquired owner behind.
        try
        {
            await _store.ReleaseLeaseAsync(resourceId, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Late execution acquisition cleanup failed for {ResourceId}; ownership will expire", resourceId);
        }
        throw new ObjectDisposedException(nameof(LeaseManager));
    }

    private sealed class ExecutionLease : IExecutionLease
    {
        private readonly LeaseManager _manager;
        private readonly LeaseRecord _lease;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _lifetime;
        private readonly Task _renewal;
        private readonly object _disposeSync = new();
        private Task? _disposeTask;
        private int _disposed;
        private volatile bool _lost;

        public ExecutionLease(LeaseManager manager, LeaseRecord lease)
        {
            _manager = manager;
            _lease = lease;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(manager._cts.Token);
            _renewal = RenewLoopAsync();
        }

        public string ResourceId => _lease.ResourceId;
        public string OwnerId => _lease.InstanceId;

        public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (_lost || !await _manager._store.ExecuteUnderLeaseAsync(_lease, action, linked.Token).ConfigureAwait(false))
                {
                    _lost = true;
                    throw new ExecutionLeaseLostException(ResourceId);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task RenewLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _manager._config.RenewIntervalSeconds)));
            try
            {
                while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                {
                    await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    try
                    {
                        if (_lost)
                            return;
                        _lost = !await _manager._store.RenewLeaseAsync(ResourceId, _lease.InstanceId,
                            TimeSpan.FromSeconds(_manager._config.LeaseTtlSeconds), _lifetime.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _lost = true;
                _manager._log.LogWarning(ex, "Execution lease renewal failed for {ResourceId}", ResourceId);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposeSync)
                return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _manager._log.LogWarning(ex, "Execution cancellation callback failed for {ResourceId}; continuing cleanup", ResourceId);
            }
            await _renewal.ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _manager._store.ReleaseLeaseAsync(ResourceId, _lease.InstanceId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The run is already stopped. Failed cleanup must not replace its committed outcome;
                // renewal has ended, so the retained ownership will expire through the normal TTL.
                _manager._log.LogWarning(ex, "Execution lease release failed for {ResourceId}; ownership will expire", ResourceId);
            }
            finally
            {
                _manager._executionLeases.TryRemove(this, out _);
                _gate.Release();
                _lifetime.Dispose();
            }
        }
    }
}
