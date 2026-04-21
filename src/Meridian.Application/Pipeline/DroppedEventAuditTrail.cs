using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Audit trail for events dropped by the pipeline due to backpressure.
/// Logs dropped events to a separate audit file for gap-aware consumers.
/// </summary>
public sealed class DroppedEventAuditTrail : IAsyncDisposable
{
    private readonly string _auditPath;
    private readonly ILogger<DroppedEventAuditTrail> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<SymbolId, long> _dropCountsBySymbol = new();
    private long _totalDropped;
    private bool _disposed;

    public DroppedEventAuditTrail(
        string auditDirectory,
        ILogger<DroppedEventAuditTrail>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditDirectory);
        _auditPath = Path.Combine(auditDirectory, "_audit");
        _logger = logger ?? NullLogger<DroppedEventAuditTrail>.Instance;
    }

    /// <summary>Gets the total number of dropped events recorded.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

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

        var record = new DroppedEventRecord(
            Timestamp: DateTimeOffset.UtcNow,
            EventTimestamp: evt.Timestamp,
            EventType: evt.Type.ToString(),
            Symbol: evt.EffectiveSymbol,
            Sequence: evt.Sequence,
            Source: evt.Source,
            Reason: reason);

        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var json = JsonSerializer.Serialize(record, DroppedEventAuditTrailJsonContext.Default.DroppedEventRecord);
                await AtomicFileWriter.AppendLinesAsync(GetAuditFilePath(), [json], ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to write dropped event audit record for {Symbol}", evt.Symbol);
        }
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
            Timestamp: DateTimeOffset.UtcNow);
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

        // Acquire the write lock to ensure no concurrent write is in progress
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // No buffered writer state remains once AtomicFileWriter.AppendLinesAsync returns.
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }
}

/// <summary>
/// Statistics about dropped events for API exposure.
/// </summary>
public sealed record DroppedEventStatistics(
    long TotalDropped,
    IReadOnlyDictionary<string, long> DropsBySymbol,
    string AuditFilePath,
    DateTimeOffset Timestamp);

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
