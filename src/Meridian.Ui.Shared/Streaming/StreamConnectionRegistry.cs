using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Tracks the number of concurrent quote-stream connections per session and enforces a
/// per-session cap. A singleton hit concurrently by many request threads, so all counter
/// mutations are atomic (compare-and-swap on a <see cref="ConcurrentDictionary{TKey,TValue}"/>).
/// </summary>
public sealed class StreamConnectionRegistry
{
    private readonly int _maxPerSession;
    private readonly ConcurrentDictionary<string, int> _counts = new(System.StringComparer.Ordinal);

    public StreamConnectionRegistry(int maxConcurrentStreamsPerSession)
    {
        _maxPerSession = maxConcurrentStreamsPerSession > 0 ? maxConcurrentStreamsPerSession : 1;
    }

    /// <summary>
    /// Atomically reserves a stream slot for the session. Returns false when the session
    /// is already at its cap. An empty session id is treated as unscoped and never capped.
    /// </summary>
    public bool TryReserve(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return true;
        }

        while (true)
        {
            var current = _counts.GetOrAdd(sessionId, 0);
            if (current >= _maxPerSession)
            {
                return false;
            }

            if (_counts.TryUpdate(sessionId, current + 1, current))
            {
                return true;
            }

            // Lost the race with another thread — re-read and retry.
        }
    }

    /// <summary>Releases a previously reserved slot for the session.</summary>
    public void Release(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        while (true)
        {
            if (!_counts.TryGetValue(sessionId, out var current) || current <= 0)
            {
                return;
            }

            if (current == 1)
            {
                // Conditionally remove only if the value is still 1 (atomic).
                if (((ICollection<KeyValuePair<string, int>>)_counts).Remove(new KeyValuePair<string, int>(sessionId, 1)))
                {
                    return;
                }
            }
            else if (_counts.TryUpdate(sessionId, current - 1, current))
            {
                return;
            }

            // Lost the race — retry.
        }
    }

    /// <summary>Current number of active streams for the session.</summary>
    public int ActiveCount(string sessionId)
        => _counts.TryGetValue(sessionId, out var count) ? count : 0;

    /// <summary>Drops all accounting for a session (e.g. on logout / session expiry).</summary>
    public void CloseSession(string sessionId) => _counts.TryRemove(sessionId, out _);
}
