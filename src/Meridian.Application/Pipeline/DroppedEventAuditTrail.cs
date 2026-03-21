using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain;
using Meridian.Domain.Events;
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
    private StreamWriter? _writer;
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

        var record = new
        {
            timestamp = DateTimeOffset.UtcNow,
            eventTimestamp = evt.Timestamp,
            eventType = evt.Type.ToString(),
            symbol = evt.EffectiveSymbol,
            sequence = evt.Sequence,
            source = evt.Source,
            reason
        };

        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var writer = EnsureWriter();
                var json = JsonSerializer.Serialize(record, MarketDataJsonContext.HighPerformanceOptions);
                await writer.WriteLineAsync(json).ConfigureAwait(false);
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
            AuditFilePath: Path.Combine(_auditPath, "dropped_events.jsonl"),
            Timestamp: DateTimeOffset.UtcNow);
    }

    private StreamWriter EnsureWriter()
    {
        if (_writer != null)
            return _writer;

        Directory.CreateDirectory(_auditPath);
        var filePath = Path.Combine(_auditPath, "dropped_events.jsonl");
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        _writer = new StreamWriter(stream) { AutoFlush = false };
        return _writer;
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
            if (_writer != null)
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
