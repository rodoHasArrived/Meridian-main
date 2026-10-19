namespace Meridian.Storage.Archival;

/// <summary>
/// In-process coordination between JSONL writers and compression for one physical path.
/// Writers hold a shared lease while their append stream is open; compression takes the
/// path exclusively for its whole verify-publish-delete window. A writer reopening a path
/// during that window waits, then appends to a fresh source after the original is gone, so
/// no event can land in a source that compression is about to delete. Other processes are
/// not covered; compression's quiet period and unchanged-source checks remain for those.
/// </summary>
internal static class JsonlWriteFence
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Dictionary<string, Entry> Entries = new(PathComparer);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, AppendGate> AppendGates = new(PathComparer);

    /// <summary>
    /// Serializes copy-on-write appends to one published archive across sink instances. Each
    /// append copies the whole archive and replaces it, so two concurrent appenders would each
    /// copy the same bytes and the last replace would drop the other's member. Waiting honours
    /// <paramref name="ct"/>; once acquired the lease is held through commit and checksum
    /// republication and is released only by disposal.
    /// </summary>
    public static async ValueTask<IDisposable> EnterArchiveAppendAsync(string archivePath, CancellationToken ct)
    {
        var key = Path.GetFullPath(archivePath);
        AppendGate gate;
        lock (Gate)
        {
            if (!AppendGates.TryGetValue(key, out gate!))
                AppendGates[key] = gate = new AppendGate();
            gate.References++;
        }
        try
        {
            await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ReleaseAppendGate(key, gate, entered: false);
            throw;
        }
        return new AppendLease(key, gate);
    }

    private static void ReleaseAppendGate(string key, AppendGate gate, bool entered)
    {
        if (entered)
            gate.Semaphore.Release();
        lock (Gate)
        {
            if (--gate.References == 0)
                AppendGates.Remove(key);
        }
    }

    public static async ValueTask<IDisposable> EnterWriteAsync(string path, CancellationToken ct)
    {
        var key = Path.GetFullPath(path);
        while (true)
        {
            Task released;
            lock (Gate)
            {
                var entry = GetOrAdd(key);
                if (!entry.Exclusive)
                {
                    entry.Writers++;
                    return new Lease(key, exclusive: false);
                }
                released = entry.Released.Task;
            }
            await released.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    public static bool TryEnterExclusive(string path, out IDisposable lease)
    {
        var key = Path.GetFullPath(path);
        lock (Gate)
        {
            var entry = GetOrAdd(key);
            if (entry.Exclusive || entry.Writers > 0)
            {
                lease = null!;
                return false;
            }
            entry.Exclusive = true;
            entry.Released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lease = new Lease(key, exclusive: true);
            return true;
        }
    }

    private static Entry GetOrAdd(string key)
    {
        if (!Entries.TryGetValue(key, out var entry))
            Entries[key] = entry = new Entry();
        return entry;
    }

    private static void Release(string key, bool exclusive)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var entry))
                return;
            if (exclusive)
            {
                entry.Exclusive = false;
                entry.Released.TrySetResult();
            }
            else
            {
                entry.Writers--;
            }
            if (!entry.Exclusive && entry.Writers == 0)
                Entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        public int Writers;
        public bool Exclusive;
        public TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Lease(string key, bool exclusive) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Release(key, exclusive);
        }
    }

    private sealed class AppendGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    private sealed class AppendLease(string key, AppendGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ReleaseAppendGate(key, gate, entered: true);
        }
    }
}
