using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Configuration for the durable execution audit trail.
/// </summary>
public sealed record ExecutionAuditTrailOptions(
    string RootDirectory,
    int InMemoryRetention = 1_000,
    WalSyncMode SyncMode = WalSyncMode.EveryWrite)
{
    public static ExecutionAuditTrailOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "execution", "audit"));

    public string WalDirectory => Path.Combine(RootDirectory, "wal");
}

/// <summary>
/// Durable audit record for live-execution operations, approvals, and control changes.
/// </summary>
public sealed record ExecutionAuditEntry(
    string AuditId,
    string Category,
    string Action,
    string Outcome,
    DateTimeOffset OccurredAt,
    string? Actor = null,
    string? BrokerName = null,
    string? OrderId = null,
    string? RunId = null,
    string? Symbol = null,
    string? CorrelationId = null,
    string? Message = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Durable execution audit trail backed by the platform WAL.
/// The payload volume is low compared with market-data paths, so we bias toward
/// explicit durability and traceability over raw throughput.
/// </summary>
public sealed class ExecutionAuditTrailService : IAsyncDisposable
{
    private const string AuditRecordType = "ExecutionAudit";

    private readonly WriteAheadLog _wal;
    private readonly ILogger<ExecutionAuditTrailService> _logger;
    private readonly int _inMemoryRetention;
    private readonly List<ExecutionAuditEntry> _entries = [];
    private readonly Lock _lock = new();

    public ExecutionAuditTrailService(
        ExecutionAuditTrailOptions? options,
        ILogger<ExecutionAuditTrailService> logger)
    {
        options ??= ExecutionAuditTrailOptions.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inMemoryRetention = Math.Max(100, options.InMemoryRetention);
        _wal = new WriteAheadLog(
            options.WalDirectory,
            new WalOptions
            {
                SyncMode = options.SyncMode,
                ArchiveAfterTruncate = false,
                MaxWalFileAge = TimeSpan.FromDays(1),
                MaxWalFileSizeBytes = 5 * 1024 * 1024,
                CorruptionMode = WalCorruptionMode.Alert
            });

        InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Returns the most recent audit entries, newest first.
    /// </summary>
    public Task<IReadOnlyList<ExecutionAuditEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ExecutionAuditEntry>>(
                _entries
                    .OrderByDescending(static entry => entry.OccurredAt)
                    .Take(Math.Max(1, take))
                    .ToArray());
        }
    }

    /// <summary>
    /// Returns all retained audit entries in chronological order.
    /// </summary>
    public Task<IReadOnlyList<ExecutionAuditEntry>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ExecutionAuditEntry>>(_entries.ToArray());
        }
    }

    /// <summary>
    /// Appends a new audit entry and returns the persisted record.
    /// </summary>
    public async Task<ExecutionAuditEntry> RecordAsync(
        string category,
        string action,
        string outcome,
        string? actor = null,
        string? brokerName = null,
        string? orderId = null,
        string? runId = null,
        string? symbol = null,
        string? correlationId = null,
        string? message = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var entry = new ExecutionAuditEntry(
            AuditId: $"audit-{Guid.NewGuid():N}",
            Category: category,
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            Actor: actor,
            BrokerName: brokerName,
            OrderId: orderId,
            RunId: runId,
            Symbol: symbol,
            CorrelationId: correlationId,
            Message: message,
            Metadata: metadata);

        await RecordAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }

    /// <summary>
    /// Appends a pre-built audit entry.
    /// </summary>
    public async Task RecordAsync(ExecutionAuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var json = JsonSerializer.Serialize(entry, ExecutionJsonContext.Default.ExecutionAuditEntry);
        await _wal.AppendAsync(json, AuditRecordType, ct).ConfigureAwait(false);

        lock (_lock)
        {
            _entries.Add(entry);
            TrimRetainedEntries();
        }

        _logger.LogInformation(
            "Execution audit {AuditId}: {Category}/{Action} {Outcome}",
            entry.AuditId,
            entry.Category,
            entry.Action,
            entry.Outcome);
    }

    public async ValueTask DisposeAsync()
    {
        await _wal.DisposeAsync().ConfigureAwait(false);
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        await _wal.InitializeAsync(ct).ConfigureAwait(false);

        await foreach (var record in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            if (!string.Equals(record.RecordType, AuditRecordType, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = record.DeserializePayload<string>();
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize(payload, ExecutionJsonContext.Default.ExecutionAuditEntry);
                if (entry is not null)
                {
                    lock (_lock)
                    {
                        _entries.Add(entry);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize execution audit payload from WAL.");
            }
        }

        lock (_lock)
        {
            _entries.Sort(static (left, right) => left.OccurredAt.CompareTo(right.OccurredAt));
            TrimRetainedEntries();
        }
    }

    private void TrimRetainedEntries()
    {
        if (_entries.Count <= _inMemoryRetention)
        {
            return;
        }

        _entries.RemoveRange(0, _entries.Count - _inMemoryRetention);
    }
}
