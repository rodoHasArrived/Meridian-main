using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Core.Serialization;
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
/// unnecessary file-system operations when all events pass validation. Records are appended
/// in place and flushed to disk before <see cref="RecordAsync"/> returns; concurrent callers
/// share one append so the file stays well-formed and no call rewrites existing records.
/// <para>
/// A dead-letter record can be the last copy of an event, so a failed write does not discard
/// it: the record is held in memory (up to <see cref="MaxRetainedRecords"/>) and retried on
/// a later record (after a short back-off), on <see cref="FlushAsync"/>, and on disposal. Records that still cannot be
/// written are counted in <see cref="TotalLost"/> and logged as errors.
/// </para>
/// </remarks>
[ImplementsAdr("ADR-007", "Dead-letter persistence for events rejected by the validation gate")]
public sealed class DeadLetterSink : IEventQuarantineSink, IAsyncDisposable
{
    /// <summary>Most records held in memory while the dead-letter file cannot be written.</summary>
    internal const int MaxRetainedRecords = 10_000;

    private readonly string _deadLetterDirectory;
    private readonly ILogger<DeadLetterSink> _logger;
    private readonly GroupCommitJsonlLog _log;
    private readonly ConcurrentDictionary<SymbolId, long> _rejectedCountsBySymbol = new();
    private long _totalRejected;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Initialises a new <see cref="DeadLetterSink"/>.
    /// </summary>
    /// <param name="dataRoot">Base data directory. A <c>_dead_letter/</c> subdirectory will be created beneath it.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public DeadLetterSink(
        string dataRoot,
        ILogger<DeadLetterSink>? logger = null)
        : this(dataRoot, logger, appendLinesAsync: null)
    {
    }

    /// <summary>Test seam: substitutes the durable append so write failures can be injected.</summary>
    internal DeadLetterSink(
        string dataRoot,
        ILogger<DeadLetterSink>? logger,
        Func<string, IReadOnlyList<string>, CancellationToken, Task>? appendLinesAsync,
        TimeSpan? retryBackoff = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _deadLetterDirectory = Path.Combine(dataRoot, "_dead_letter");
        _logger = logger ?? NullLogger<DeadLetterSink>.Instance;
        _log = new GroupCommitJsonlLog(
            GetDeadLetterFilePath(),
            MaxRetainedRecords,
            retainFailedWrites: true,
            onWriteFailed: (ex, recordCount, _) => _logger.LogError(
                ex,
                "Failed to write {RecordCount} dead-letter record(s) to {Path}; they are retained in memory and will be retried",
                recordCount, GetDeadLetterFilePath()),
            appendLinesAsync,
            retryBackoff);
    }

    /// <summary>Gets the total number of rejected events recorded so far.</summary>
    public long TotalRejected => Interlocked.Read(ref _totalRejected);

    /// <summary>Gets the number of rejected events durably written to the dead-letter file.</summary>
    public long TotalPersisted => _log.WrittenCount;

    /// <summary>Gets the number of rejected events held in memory awaiting a successful write.</summary>
    public long PendingRetryCount => _log.PendingCount;

    /// <summary>Gets the number of rejected events that could not be written and were discarded.</summary>
    public long TotalLost => _log.LostCount;

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

        string json;
        try
        {
            var record = new DeadLetterRecord(
                RejectedAtUtc: DateTimeOffset.UtcNow,
                EventTimestamp: evt.Timestamp,
                EventType: evt.Type.ToString(),
                Symbol: evt.EffectiveSymbol,
                Sequence: evt.Sequence,
                Source: evt.Source,
                SchemaVersion: evt.SchemaVersion,
                ValidationErrors: errors.ToArray(),
                Event: evt);
            json = JsonSerializer.Serialize(record, JsonOptions);
        }
        catch (Exception ex)
        {
            _log.RecordLost();
            _logger.LogError(
                ex,
                "Dead-letter record lost for {Symbol} sequence {Sequence}: it could not be serialized",
                evt.EffectiveSymbol, evt.Sequence);
            return;
        }

        if (!_log.TryReserve())
        {
            // The file has been unwritable long enough to fill the retry buffer.
            _logger.LogError(
                "Dead-letter record lost for {Symbol} sequence {Sequence}: {RetainedCount} records are already awaiting a successful write",
                evt.EffectiveSymbol, evt.Sequence, MaxRetainedRecords);
            return;
        }

        // Cancellation propagates without logging; this is expected during shutdown. The record
        // stays queued and is written by the next record, flush, or disposal.
        var outcome = await _log.AppendReservedAsync(json, ct).ConfigureAwait(false);

        if (outcome == JsonlLogAppendOutcome.Written)
        {
            _logger.LogWarning(
                "Event rejected and sent to dead letter: Symbol={Symbol}, Type={EventType}, Sequence={Sequence}, Errors={ErrorCount}",
                evt.EffectiveSymbol, evt.Type, evt.Sequence, errors.Count);
        }
        else
        {
            _logger.LogError(
                "Dead-letter record for {Symbol} sequence {Sequence} is not yet on disk; it is held in memory for retry",
                evt.EffectiveSymbol, evt.Sequence);
        }
    }

    /// <summary>
    /// Writes any dead-letter records still held in memory after an earlier failed write.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_disposed)
            return;

        await _log.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a snapshot of dead-letter statistics.
    /// </summary>
    public DeadLetterStatistics GetStatistics()
    {
        return new DeadLetterStatistics(
            TotalRejected: TotalRejected,
            RejectedBySymbol: RejectedCountsBySymbol,
            DeadLetterFilePath: GetDeadLetterFilePath(),
            Timestamp: DateTimeOffset.UtcNow)
        {
            TotalPersisted = TotalPersisted,
            PendingRetry = PendingRetryCount,
            TotalLost = TotalLost
        };
    }

    private string GetDeadLetterFilePath()
    {
        return Path.Combine(_deadLetterDirectory, "rejected_events.jsonl");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(MarketDataJsonContext.HighPerformanceOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                DeadLetterJsonContext.Default,
                MarketDataJsonContext.Default)
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Final attempt to write anything still held after an earlier failed write.
        await _log.DisposeAsync().ConfigureAwait(false);

        if (TotalLost > 0)
        {
            _logger.LogError(
                "Dead-letter sink disposed with {LostCount} rejected event(s) that could not be written to {Path}",
                TotalLost, GetDeadLetterFilePath());
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
    string[] ValidationErrors,
    MarketEvent Event);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DeadLetterRecord))]
internal sealed partial class DeadLetterJsonContext : JsonSerializerContext;

/// <summary>
/// Statistics about rejected events for API exposure.
/// </summary>
public sealed record DeadLetterStatistics(
    long TotalRejected,
    IReadOnlyDictionary<string, long> RejectedBySymbol,
    string DeadLetterFilePath,
    DateTimeOffset Timestamp)
{
    /// <summary>Rejected events durably written to the dead-letter file.</summary>
    public long TotalPersisted { get; init; }

    /// <summary>Rejected events held in memory awaiting a successful write.</summary>
    public long PendingRetry { get; init; }

    /// <summary>Rejected events that could not be written and were discarded.</summary>
    public long TotalLost { get; init; }
}
