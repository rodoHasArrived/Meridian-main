using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain;
using Meridian.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Audit trail for events dropped by the pipeline due to backpressure.
/// Logs dropped events to a separate audit file for gap-aware consumers.
/// </summary>
/// <remarks>
/// The pipeline records drops without awaiting them, and drops arrive fastest exactly when the
/// system is overloaded. Records are therefore appended in place, with concurrent callers
/// sharing one write, and at most <see cref="MaxPendingRecords"/> records wait for the disk at
/// once. Drops beyond that, and records whose write fails, are still counted in
/// <see cref="TotalDropped"/> and per symbol; only their audit line is skipped, which
/// <see cref="AuditRecordsNotWritten"/> reports.
/// </remarks>
public sealed class DroppedEventAuditTrail : IAsyncDisposable
{
    /// <summary>Most audit records that may wait for the disk at once.</summary>
    internal const int MaxPendingRecords = 10_000;

    private readonly string _auditPath;
    private readonly ILogger<DroppedEventAuditTrail> _logger;
    private readonly GroupCommitJsonlLog _log;
    private readonly ConcurrentDictionary<SymbolId, long> _dropCountsBySymbol = new();
    private long _totalDropped;
    private bool _disposed;

    public DroppedEventAuditTrail(
        string auditDirectory,
        ILogger<DroppedEventAuditTrail>? logger = null)
        : this(auditDirectory, logger, appendLinesAsync: null)
    {
    }

    /// <summary>Test seam: substitutes the durable append so slow or failing writes can be injected.</summary>
    internal DroppedEventAuditTrail(
        string auditDirectory,
        ILogger<DroppedEventAuditTrail>? logger,
        Func<string, IReadOnlyList<string>, CancellationToken, Task>? appendLinesAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditDirectory);
        _auditPath = Path.Combine(auditDirectory, "_audit");
        _logger = logger ?? NullLogger<DroppedEventAuditTrail>.Instance;
        _log = new GroupCommitJsonlLog(
            GetAuditFilePath(),
            MaxPendingRecords,
            retainFailedWrites: false,
            onWriteFailed: (ex, recordCount, _) => _logger.LogDebug(
                ex,
                "Failed to write {RecordCount} dropped event audit record(s) to {Path}",
                recordCount, GetAuditFilePath()),
            appendLinesAsync);
    }

    /// <summary>Gets the total number of dropped events recorded.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    /// <summary>
    /// Gets the number of dropped events with no line in the audit file, because the write
    /// failed or too many records were already waiting for the disk.
    /// </summary>
    public long AuditRecordsNotWritten => _log.LostCount;

    /// <summary>Gets drop counts keyed by symbol.</summary>
    public IReadOnlyDictionary<string, long> DropCountsBySymbol =>
        _dropCountsBySymbol.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value);

    /// <summary>
    /// Records a dropped event to the audit trail.
    /// </summary>
    public async ValueTask RecordDroppedEventAsync(
        MarketEvent evt,
        string reason,
        CancellationToken ct = default)
    {
        if (_disposed)
            return;

        Interlocked.Increment(ref _totalDropped);
        _dropCountsBySymbol.AddOrUpdate(new SymbolId(evt.EffectiveSymbol), 1, (_, count) => count + 1);

        // Checked before building the record so an overloaded trail costs the publisher nothing.
        if (!_log.TryReserve())
            return;

        string json;
        try
        {
            var record = new DroppedEventRecord(
                Timestamp: DateTimeOffset.UtcNow,
                EventTimestamp: evt.Timestamp,
                EventType: evt.Type.ToString(),
                Symbol: evt.EffectiveSymbol,
                Sequence: evt.Sequence,
                Source: evt.Source,
                Reason: reason);
            json = JsonSerializer.Serialize(record, DroppedEventAuditTrailJsonContext.Default.DroppedEventRecord);
        }
        catch (Exception ex)
        {
            _log.AbandonReservation();
            _logger.LogDebug(ex, "Failed to serialize dropped event audit record for {Symbol}", evt.Symbol);
            return;
        }

        await _log.AppendReservedAsync(json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a snapshot of drop statistics for the /api/quality/drops endpoint.
    /// </summary>
    public DroppedEventStatistics GetStatistics()
    {
        return new DroppedEventStatistics(
            TotalDropped: TotalDropped,
            DropsBySymbol: DropCountsBySymbol,
            AuditFilePath: GetAuditFilePath(),
            Timestamp: DateTimeOffset.UtcNow)
        {
            AuditRecordsNotWritten = AuditRecordsNotWritten
        };
    }

    private string GetAuditFilePath()
    {
        return Path.Combine(_auditPath, "dropped_events.jsonl");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Writes anything still queued and waits out any write in progress.
        await _log.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Statistics about dropped events for API exposure.
/// </summary>
public sealed record DroppedEventStatistics(
    long TotalDropped,
    IReadOnlyDictionary<string, long> DropsBySymbol,
    string AuditFilePath,
    DateTimeOffset Timestamp)
{
    /// <summary>Dropped events counted above that have no line in the audit file.</summary>
    public long AuditRecordsNotWritten { get; init; }
}

internal sealed record DroppedEventRecord(
    DateTimeOffset Timestamp,
    DateTimeOffset EventTimestamp,
    string EventType,
    string Symbol,
    long Sequence,
    string Source,
    string Reason);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DroppedEventRecord))]
internal sealed partial class DroppedEventAuditTrailJsonContext : JsonSerializerContext;
