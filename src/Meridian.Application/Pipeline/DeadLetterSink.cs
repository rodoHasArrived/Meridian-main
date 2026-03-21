using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Persists events that failed validation to a JSONL dead-letter file for later inspection
/// and replay. Each line contains the original event together with the validation errors
/// that caused rejection.
/// </summary>
/// <remarks>
/// The dead-letter directory is created lazily on the first rejected event to avoid
/// unnecessary file-system operations when all events pass validation. A
/// <see cref="SemaphoreSlim"/> serialises writes so the file remains well-formed even
/// when multiple callers record rejections concurrently.
/// </remarks>
[ImplementsAdr("ADR-007", "Dead-letter persistence for events rejected by the validation gate")]
public sealed class DeadLetterSink : IAsyncDisposable
{
    private readonly string _deadLetterDirectory;
    private readonly ILogger<DeadLetterSink> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<SymbolId, long> _rejectedCountsBySymbol = new();

    private StreamWriter? _writer;
    private long _totalRejected;
    private bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="DeadLetterSink"/>.
    /// </summary>
    /// <param name="dataRoot">Base data directory. A <c>_dead_letter/</c> subdirectory will be created beneath it.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public DeadLetterSink(
        string dataRoot,
        ILogger<DeadLetterSink>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _deadLetterDirectory = Path.Combine(dataRoot, "_dead_letter");
        _logger = logger ?? NullLogger<DeadLetterSink>.Instance;
    }

    /// <summary>Gets the total number of rejected events recorded so far.</summary>
    public long TotalRejected => Interlocked.Read(ref _totalRejected);

    /// <summary>Gets rejection counts keyed by effective symbol.</summary>
    public IReadOnlyDictionary<string, long> RejectedCountsBySymbol =>
        _rejectedCountsBySymbol.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value);

    /// <summary>
    /// Records a rejected event and its validation errors to the dead-letter file.
    /// </summary>
    /// <param name="evt">The market event that failed validation.</param>
    /// <param name="errors">The validation error descriptions.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask RecordAsync(
        MarketEvent evt,
        IReadOnlyList<string> errors,
        CancellationToken ct = default)
    {
        if (_disposed)
            return;

        Interlocked.Increment(ref _totalRejected);
        _rejectedCountsBySymbol.AddOrUpdate(new SymbolId(evt.EffectiveSymbol), 1, (_, count) => count + 1);

        var record = new DeadLetterRecord(
            RejectedAtUtc: DateTimeOffset.UtcNow,
            EventTimestamp: evt.Timestamp,
            EventType: evt.Type.ToString(),
            Symbol: evt.EffectiveSymbol,
            Sequence: evt.Sequence,
            Source: evt.Source,
            SchemaVersion: evt.SchemaVersion,
            ValidationErrors: errors,
            Event: evt);

        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var writer = EnsureWriter();
                var json = JsonSerializer.Serialize(record, MarketDataJsonContext.HighPerformanceOptions);
                await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            _logger.LogWarning(
                "Event rejected and sent to dead letter: Symbol={Symbol}, Type={EventType}, Sequence={Sequence}, Errors={ErrorCount}",
                evt.EffectiveSymbol, evt.Type, evt.Sequence, errors.Count);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation without logging; this is expected during shutdown.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write dead-letter record for {Symbol} sequence {Sequence}",
                evt.EffectiveSymbol, evt.Sequence);
        }
    }

    /// <summary>
    /// Flushes any buffered dead-letter records to disk.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_disposed || _writer is null)
            return;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Returns a snapshot of dead-letter statistics.
    /// </summary>
    public DeadLetterStatistics GetStatistics()
    {
        return new DeadLetterStatistics(
            TotalRejected: TotalRejected,
            RejectedBySymbol: RejectedCountsBySymbol,
            DeadLetterFilePath: Path.Combine(_deadLetterDirectory, "rejected_events.jsonl"),
            Timestamp: DateTimeOffset.UtcNow);
    }

    private StreamWriter EnsureWriter()
    {
        if (_writer is not null)
            return _writer;

        Directory.CreateDirectory(_deadLetterDirectory);
        var filePath = Path.Combine(_deadLetterDirectory, "rejected_events.jsonl");
        var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        _writer = new StreamWriter(stream) { AutoFlush = false };

        _logger.LogInformation(
            "Dead-letter sink initialised at {FilePath}",
            filePath);

        return _writer;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }

        if (TotalRejected > 0)
        {
            _logger.LogInformation(
                "Dead-letter sink disposed. Total rejected events: {TotalRejected} across {SymbolCount} symbol(s)",
                TotalRejected, _rejectedCountsBySymbol.Count);
        }
    }
}

/// <summary>
/// A single dead-letter record combining the rejected event with its validation errors.
/// Serialised as one JSONL line in the dead-letter file.
/// </summary>
internal sealed record DeadLetterRecord(
    DateTimeOffset RejectedAtUtc,
    DateTimeOffset EventTimestamp,
    string EventType,
    string Symbol,
    long Sequence,
    string Source,
    byte SchemaVersion,
    IReadOnlyList<string> ValidationErrors,
    MarketEvent Event);

/// <summary>
/// Statistics about rejected events for API exposure.
/// </summary>
public sealed record DeadLetterStatistics(
    long TotalRejected,
    IReadOnlyDictionary<string, long> RejectedBySymbol,
    string DeadLetterFilePath,
    DateTimeOffset Timestamp);
