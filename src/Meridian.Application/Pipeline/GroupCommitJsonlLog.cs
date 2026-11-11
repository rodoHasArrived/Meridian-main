using System.Collections.Concurrent;
using System.Diagnostics;
using Meridian.Storage.Archival;

namespace Meridian.Application.Pipeline;

/// <summary>Where a line handed to <see cref="GroupCommitJsonlLog"/> ended up.</summary>
internal enum JsonlLogAppendOutcome : byte
{
    /// <summary>The line is durably on disk.</summary>
    Written,

    /// <summary>The write did not complete; the line is held in memory and retried on the next append or flush.</summary>
    Retained,

    /// <summary>The line was discarded: the write failed without retention, or the log was over capacity.</summary>
    Lost
}

/// <summary>
/// Bounded, group-committing appender for a single JSONL log file.
/// </summary>
/// <remarks>
/// Callers enqueue a line and then take the write lock; whoever holds the lock appends everything
/// queued so far in one durable write. Under load many records therefore share one append and one
/// fsync instead of each rewriting the file, and a waiter whose line was already written by an
/// earlier holder returns without doing I/O. The number of lines held in memory is capped, so a
/// producer that outruns the disk loses (and counts) records instead of growing without bound.
/// With retention, a failed write is not re-attempted on every later append: retries wait out a
/// short back-off so an unwritable file is not hammered with the whole backlog per record.
/// </remarks>
internal sealed class GroupCommitJsonlLog : IAsyncDisposable
{
    private readonly string _path;
    private readonly int _maxPendingLines;
    private readonly bool _retainFailedWrites;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task> _appendLinesAsync;
    private readonly Action<Exception, int, bool> _onWriteFailed;
    private readonly long _retryBackoffTicks;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<PendingLine> _queue = new();

    // Lines whose write did not complete, oldest first. Guarded by _writeLock.
    private List<PendingLine> _retained = [];

    // Stopwatch timestamp before which appends do not retry retained lines. Guarded by _writeLock.
    private long _retryNotBefore;
    private long _pending;
    private long _written;
    private long _lost;

    /// <param name="path">The JSONL file to append to.</param>
    /// <param name="maxPendingLines">Most lines that may be queued or retained in memory at once.</param>
    /// <param name="retainFailedWrites">
    /// When true, lines from a failed write stay in memory and are retried by later appends and
    /// flushes; when false they are discarded and counted as lost.
    /// </param>
    /// <param name="onWriteFailed">Called once per failed write with the exception, the number of lines in it, and whether they were retained.</param>
    /// <param name="appendLinesAsync">Durable append primitive; defaults to <see cref="JsonlAppendWriter.AppendLinesAsync"/>.</param>
    /// <param name="retryBackoff">How long appends wait after a failed write before retrying retained lines; defaults to one second.</param>
    public GroupCommitJsonlLog(
        string path,
        int maxPendingLines,
        bool retainFailedWrites,
        Action<Exception, int, bool> onWriteFailed,
        Func<string, IReadOnlyList<string>, CancellationToken, Task>? appendLinesAsync = null,
        TimeSpan? retryBackoff = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingLines);
        ArgumentNullException.ThrowIfNull(onWriteFailed);

        _path = path;
        _maxPendingLines = maxPendingLines;
        _retainFailedWrites = retainFailedWrites;
        _onWriteFailed = onWriteFailed;
        _appendLinesAsync = appendLinesAsync ?? JsonlAppendWriter.AppendLinesAsync;
        _retryBackoffTicks = (long)((retryBackoff ?? TimeSpan.FromSeconds(1)).TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>Lines accepted but not yet durably written (queued or retained for retry).</summary>
    public long PendingCount => Interlocked.Read(ref _pending);

    /// <summary>Lines durably written.</summary>
    public long WrittenCount => Interlocked.Read(ref _written);

    /// <summary>Lines discarded without being written.</summary>
    public long LostCount => Interlocked.Read(ref _lost);

    /// <summary>
    /// Claims capacity for one line. Returns false, and counts the line as lost, when the log
    /// already holds its maximum number of unwritten lines. A successful reservation must be
    /// followed by <see cref="AppendReservedAsync"/>.
    /// </summary>
    public bool TryReserve()
    {
        if (TryClaimCapacity())
            return true;

        Interlocked.Increment(ref _lost);
        return false;
    }

    /// <summary>
    /// Like <see cref="TryReserve"/>, but when the log is full it first gives the retained lines
    /// another write attempt (honouring the retry back-off) and then claims the capacity that
    /// frees. Without this a full log would never recover on its own: every caller would be
    /// turned away before reaching the append that retries, so the backlog would only be written
    /// by an explicit flush or disposal even after the file became writable again.
    /// </summary>
    public async ValueTask<bool> TryReserveAfterRetryAsync(CancellationToken ct)
    {
        if (TryClaimCapacity())
            return true;

        await DrainAsync(retryNow: false, ct).ConfigureAwait(false);
        if (TryClaimCapacity())
            return true;

        Interlocked.Increment(ref _lost);
        return false;
    }

    private bool TryClaimCapacity()
    {
        if (Interlocked.Increment(ref _pending) <= _maxPendingLines)
            return true;

        Interlocked.Decrement(ref _pending);
        return false;
    }

    /// <summary>Counts a line the caller could not produce (for example a serialization failure) as lost.</summary>
    public void RecordLost() => Interlocked.Increment(ref _lost);

    /// <summary>Gives back a reservation whose line could not be produced, counting it as lost.</summary>
    public void AbandonReservation()
    {
        Interlocked.Decrement(ref _pending);
        Interlocked.Increment(ref _lost);
    }

    /// <summary>
    /// Queues a line reserved with <see cref="TryReserve"/> and waits until it has been written,
    /// retained, or lost. If <paramref name="ct"/> is cancelled first, the line stays queued for
    /// the next append, flush, or disposal and the cancellation propagates.
    /// </summary>
    public async ValueTask<JsonlLogAppendOutcome> AppendReservedAsync(string line, CancellationToken ct)
    {
        var pending = new PendingLine(line);
        _queue.Enqueue(pending);
        await DrainAsync(retryNow: false, ct).ConfigureAwait(false);
        return (JsonlLogAppendOutcome)Volatile.Read(ref pending.Outcome);
    }

    /// <summary>
    /// Writes everything queued or retained, ignoring any retry back-off. Returns the number of
    /// lines still unwritten.
    /// </summary>
    public async Task<long> FlushAsync(CancellationToken ct)
    {
        await DrainAsync(retryNow: true, ct).ConfigureAwait(false);
        return PendingCount;
    }

    private async Task DrainAsync(bool retryNow, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_retained.Count == 0 && _queue.IsEmpty)
                return;

            if (!retryNow && _retained.Count > 0 && Stopwatch.GetTimestamp() < _retryNotBefore)
            {
                // Still backing off after a failed write: hold new lines behind the retained
                // ones, in arrival order, without touching the file.
                while (_queue.TryDequeue(out var held))
                    _retained.Add(held);
                return;
            }

            // Retained lines go first so the file keeps arrival order across a failed write.
            var batch = _retained;
            _retained = [];
            while (_queue.TryDequeue(out var queued))
                batch.Add(queued);

            var lines = new string[batch.Count];
            for (var index = 0; index < batch.Count; index++)
                lines[index] = batch[index].Line;

            try
            {
                await _appendLinesAsync(_path, lines, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Cancellation is not a storage failure: keep the lines for the next attempt.
                var cancelled = ex is OperationCanceledException && ct.IsCancellationRequested;
                if (cancelled || _retainFailedWrites)
                {
                    _retained = batch;
                    Resolve(batch, JsonlLogAppendOutcome.Retained);
                }
                else
                {
                    Interlocked.Add(ref _pending, -batch.Count);
                    Interlocked.Add(ref _lost, batch.Count);
                    Resolve(batch, JsonlLogAppendOutcome.Lost);
                }

                if (cancelled)
                    throw;

                _retryNotBefore = Stopwatch.GetTimestamp() + _retryBackoffTicks;
                _onWriteFailed(ex, batch.Count, _retainFailedWrites);
                return;
            }

            Interlocked.Add(ref _pending, -batch.Count);
            Interlocked.Add(ref _written, batch.Count);
            Resolve(batch, JsonlLogAppendOutcome.Written);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void Resolve(List<PendingLine> batch, JsonlLogAppendOutcome outcome)
    {
        foreach (var line in batch)
            Volatile.Write(ref line.Outcome, (int)outcome);
    }

    /// <summary>
    /// Makes a final attempt to write everything still held, then discards what remains and
    /// counts it as lost. <see cref="LostCount"/> reflects the result afterwards.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DrainAsync(retryNow: true, CancellationToken.None).ConfigureAwait(false);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var abandoned = _retained.Count;
            _retained = [];
            while (_queue.TryDequeue(out _))
                abandoned++;

            if (abandoned > 0)
            {
                Interlocked.Add(ref _pending, -abandoned);
                Interlocked.Add(ref _lost, abandoned);
            }
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }

    private sealed class PendingLine(string line)
    {
        public readonly string Line = line;

        // A JsonlLogAppendOutcome; starts as Retained (accepted, not yet written).
        public int Outcome = (int)JsonlLogAppendOutcome.Retained;
    }
}
