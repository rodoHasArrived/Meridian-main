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
    /// <summary>Explicit reason or rationale for the action (e.g. "position-limit-exceeded").</summary>
    string? Reason = null,
    /// <summary>Explicit scope for the action (e.g. "AAPL:100" or "run:xyz/symbol:AAPL").</summary>
    string? Scope = null,
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
    private readonly Lock _initializationLock = new();
    private Task? _initializationTask;

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
    }

    /// <summary>
    /// Convenience constructor that accepts a root directory string directly.
    /// Useful for tests and simple host scenarios that do not need the full options object.
    /// </summary>
    public ExecutionAuditTrailService(
        string rootDirectory,
        ILogger<ExecutionAuditTrailService> logger)
        : this(new ExecutionAuditTrailOptions(rootDirectory), logger)
    {
    }

    /// <summary>
    /// Returns the most recent audit entries, newest first.
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return
                _entries
                    .OrderByDescending(static entry => entry.OccurredAt)
                    .Take(Math.Max(1, take))
                    .ToArray();
        }
    }

    /// <summary>
    /// The newest <paramref name="take"/> entries <em>plus</em> every retained entry at or after
    /// <paramref name="since"/>, newest first.
    /// <para>
    /// A count-bounded query and a time-bounded claim do not mix. A caller that says "a breach in
    /// the last hour holds this rule constrained" and then reads a fixed number of newest entries
    /// silently drops that breach as soon as enough unrelated activity follows it — which on an
    /// active desk happens well inside the hour, turning a live breach into a healthy rule and
    /// reopening whatever gate reads it. The count still bounds ordinary history; the window is
    /// what the caller actually reasons about, so both are honoured.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetRecentOrSinceAsync(
        int take,
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            var ordered = _entries.OrderByDescending(static entry => entry.OccurredAt).ToArray();
            var count = Math.Max(1, take);
            // Ordered newest-first, so everything inside the window is a prefix — except that a
            // misdated future entry sorts ahead of it. Counting forward past the window rather
            // than assuming a prefix keeps those from displacing genuine entries.
            var windowed = ordered.Count(entry => entry.OccurredAt >= since);
            return ordered.Take(Math.Max(count, windowed)).ToArray();
        }
    }

    /// <summary>
    /// Returns all retained audit entries in chronological order.
    /// </summary>
    public async Task<IReadOnlyList<ExecutionAuditEntry>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

        lock (_lock)
        {
            return _entries.ToArray();
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
        string? reason = null,
        string? scope = null,
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
            Reason: reason,
            Scope: scope,
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
        await EnsureInitialisedAsync(ct).ConfigureAwait(false);

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
        var initialisationTask = GetInitialisationTask();
        if (initialisationTask is not null)
        {
            await initialisationTask.ConfigureAwait(false);
        }

        await _wal.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitialisedAsync(CancellationToken ct)
    {
        Task initialisationTask;
        lock (_initializationLock)
        {
            _initializationTask ??= InitialiseAsync(CancellationToken.None);
            initialisationTask = _initializationTask;
        }

        await initialisationTask.WaitAsync(ct).ConfigureAwait(false);
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

    private Task? GetInitialisationTask()
    {
        lock (_initializationLock)
        {
            return _initializationTask;
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
