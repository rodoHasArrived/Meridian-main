using System.Threading.Channels;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Streams every entry posted to a <see cref="global::Meridian.Ledger.Ledger"/> into an
/// <see cref="ILedgerJournalLog"/> in posting order. Posting stays synchronous: entries are handed to
/// a bounded channel and written by one background writer, so a run is not slowed by an fsync per
/// entry until the channel fills, and then posting waits for the writer to catch up.
/// </summary>
/// <remarks>
/// A write failure is not swallowed. Posts made after it fail with the original error, and
/// <see cref="CompleteAsync"/> rethrows it, so a run never finishes as if its books were saved.
/// </remarks>
public sealed class LedgerJournalStream : IAsyncDisposable
{
    private readonly ILedgerJournalLog _log;
    private readonly Channel<JournalEntry> _channel;
    private readonly Task _writer;
    private readonly CancellationTokenSource _abort = new();
    private volatile Exception? _failure;
    private int _completed;

    private LedgerJournalStream(ILedgerJournalLog log, int capacity)
    {
        _log = log;
        _channel = Channel.CreateBounded<JournalEntry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// Starts streaming <paramref name="ledger"/> into <paramref name="log"/>. The ledger must be
    /// empty so the log holds its complete history.
    /// </summary>
    public static LedgerJournalStream Attach(global::Meridian.Ledger.Ledger ledger, ILedgerJournalLog log, int capacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (ledger.JournalEntryCount != 0)
            throw new InvalidOperationException("A journal stream must be attached before the first posting so the log is complete.");

        var stream = new LedgerJournalStream(log, capacity);
        ledger.AddPostingObserver(stream.Enqueue);
        return stream;
    }

    /// <summary>The journal log entries are written to.</summary>
    public ILedgerJournalLog Log => _log;

    private void Enqueue(JournalEntry entry)
    {
        if (_failure is { } failure)
            throw new InvalidOperationException($"Ledger journal '{_log.ScopeId}' stopped accepting entries after a write failure.", failure);
        if (_channel.Writer.TryWrite(entry))
            return;

        // Channel full: apply backpressure to the posting thread rather than drop or buffer without bound.
        // A channel completed with an error throws that error here.
        _channel.Writer.WriteAsync(entry, _abort.Token).AsTask().GetAwaiter().GetResult();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_abort.Token).ConfigureAwait(false))
                await _log.AppendAsync(entry, _abort.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_abort.IsCancellationRequested)
        {
            _failure = ex;
            _channel.Writer.TryComplete(ex);
            throw;
        }
    }

    /// <summary>
    /// Waits until every posted entry is durable, then stops accepting entries.
    /// </summary>
    /// <exception cref="Exception">The first write failure, if any entry could not be stored.</exception>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _channel.Writer.TryComplete();
        await _writer.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the writer. Entries already written stay durable; entries still queued are not written,
    /// which matches a crash at the same point. Call <see cref="CompleteAsync"/> first for a clean finish.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _channel.Writer.TryComplete();
            _abort.Cancel();
        }

        try
        {
            await _writer.ConfigureAwait(false);
        }
        catch
        {
            // Failures are reported by CompleteAsync; disposal only releases resources.
        }

        _abort.Dispose();
        await _log.DisposeAsync().ConfigureAwait(false);
    }
}
