using System.Collections.Concurrent;

namespace Meridian.Storage.FundStructure;

/// <summary>Local stores share gates by persistence identity; PostgreSQL uses database locks.</summary>
internal sealed class EntityLifecycleLeaseGate
{
    private readonly SemaphoreSlim _readersGate = new(1, 1);
    private readonly SemaphoreSlim _resource = new(1, 1);
    private int _readers;
    private static readonly ConcurrentDictionary<string, EntityLifecycleLeaseGate> Gates = new(StringComparer.Ordinal);

    internal static EntityLifecycleLeaseGate For(string key) => Gates.GetOrAdd(key, _ => new());

    internal async Task<IAsyncDisposable> EnterReadAsync(CancellationToken ct)
    {
        await _readersGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_readers == 0)
                await _resource.WaitAsync(ct).ConfigureAwait(false);
            _readers++;
        }
        finally { _readersGate.Release(); }
        return new Lease(async () =>
        {
            await _readersGate.WaitAsync().ConfigureAwait(false);
            try { if (--_readers == 0) _resource.Release(); }
            finally { _readersGate.Release(); }
        });
    }

    internal async Task<IAsyncDisposable> EnterWriteAsync(CancellationToken ct)
    {
        await _resource.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(() => { _resource.Release(); return ValueTask.CompletedTask; });
    }

    internal sealed class Lease(Func<ValueTask> release) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0 ? release() : ValueTask.CompletedTask;
    }

    internal static async Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
                leases.Add(await For(key).EnterReadAsync(ct).ConfigureAwait(false));
            return new Lease(async () => { for (var index = leases.Count - 1; index >= 0; index--) await leases[index].DisposeAsync().ConfigureAwait(false); });
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
