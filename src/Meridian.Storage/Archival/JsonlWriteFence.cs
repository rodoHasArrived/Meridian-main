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
}
