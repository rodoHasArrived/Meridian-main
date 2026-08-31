using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Storage.Archival;

namespace Meridian.Wpf.Services;

/// <summary>
/// Represents a pending operation in the queue.
/// </summary>
public sealed class PendingOperation
{
    /// <summary>
    /// Gets or sets the unique identifier for the operation.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the operation payload. Payloads must be JSON-serializable — the queue is
    /// persisted across restarts, and a payload restored from disk is surfaced to its handler
    /// as a <see cref="JsonElement"/>.
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// Gets or sets when the operation was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retries before discarding.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Durable envelope for the persisted pending-operations queue.
/// </summary>
internal sealed class PendingOperationsEnvelope
{
    public int Version { get; set; } = PendingOperationsQueueService.CurrentEnvelopeVersion;
    public DateTimeOffset SavedAt { get; set; }
    public List<PersistedPendingOperation> Operations { get; set; } = [];
    public List<PendingOperationQuarantineRecord> QuarantinedOperations { get; set; } = [];
}

/// <summary>
/// JSON-serializable form of a <see cref="PendingOperation"/>.
/// </summary>
internal sealed class PersistedPendingOperation
{
    public string Id { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Payload-free audit record for a pending operation that cannot safely be replayed. The original
/// payload is intentionally omitted because retired reconciliation mutations may contain operator
/// notes or other sensitive casework data.
/// </summary>
public sealed record PendingOperationQuarantineRecord(
    string OperationId,
    string OperationType,
    DateTime CreatedAt,
    int RetryCount,
    int MaxRetries,
    DateTimeOffset QuarantinedAtUtc,
    int SourceEnvelopeVersion,
    string ReasonCode,
    string Reason);

/// <summary>
/// Service for managing a durable queue of pending operations: mutations that failed while the
/// backend was unreachable are enqueued here, persisted to local storage via
/// <see cref="AtomicFileWriter"/> so they survive shutdown and crashes, and replayed through
/// their registered handlers on startup and on reconnect.
/// Implements singleton pattern for application-wide operation queue management.
/// </summary>
public sealed class PendingOperationsQueueService
{
    internal const int CurrentEnvelopeVersion = 2;
    private const string FileName = "pending-operations.json";
    private const string RetiredOperationReasonCode = "retired-auth-context-sensitive-replay";
    private const string RetiredOperationReason =
        "Automatic reconciliation mutation replay was retired because the initiating authenticated session cannot be preserved.";
    private static readonly AsyncLocal<string?> FilePathOverride = new();
    private static readonly HashSet<string> RetiredOperationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "reconciliation.review-break",
        "reconciliation.resolve-break"
    };

    private static readonly Lazy<PendingOperationsQueueService> _instance =
        new(() => new PendingOperationsQueueService());

    private readonly ConcurrentQueue<PendingOperation> _queue = new();
    private readonly ConcurrentDictionary<string, PendingOperationQuarantineRecord> _quarantinedOperations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<object?, Task>> _handlers = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow;
    private bool _initialized;
    private volatile bool _persistenceSuppressed;

    /// <summary>
    /// Gets the singleton instance of the PendingOperationsQueueService.
    /// </summary>
    public static PendingOperationsQueueService Instance => _instance.Value;

    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets the number of pending operations in the queue.
    /// </summary>
    public int PendingCount => _queue.Count;

    /// <summary>
    /// Gets the number of payload-free quarantine records retained for operations that cannot be
    /// replayed safely.
    /// </summary>
    public int QuarantinedCount => _quarantinedOperations.Count;

    internal PendingOperationsQueueService(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Resolves the durable queue file below the shared Meridian local-application-data root.
    /// </summary>
    public static string GetDefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    internal static void SetFilePathOverrideForTests(string? filePath)
    {
        FilePathOverride.Value = filePath;
    }

    private static string GetFilePath() => FilePathOverride.Value ?? GetDefaultFilePath();

    /// <summary>
    /// Initializes the pending operations queue service, restoring any operations that were
    /// persisted by a previous session (clean shutdown or crash).
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _persistenceSuppressed = false;
        var migratedRetiredOperations = await RestorePersistedOperationsAsync().ConfigureAwait(false);
        if (migratedRetiredOperations)
        {
            await PersistAsync().ConfigureAwait(false);
        }

        _initialized = true;
    }

    /// <summary>
    /// Shuts down the pending operations queue service, persisting any still-pending operations
    /// to disk so they can be replayed by the next session before releasing the in-memory queue.
    /// Later persistence attempts (for example an enqueue-scheduled snapshot that loses the race
    /// with shutdown) are suppressed so they cannot overwrite the final snapshot with the
    /// cleared queue.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public async Task ShutdownAsync()
    {
        await _persistGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteSnapshotLockedAsync(default).ConfigureAwait(false);
            _persistenceSuppressed = true;
        }
        finally
        {
            _persistGate.Release();
        }

        _initialized = false;
        _queue.Clear();
    }

    /// <summary>
    /// Registers a handler for a specific operation type.
    /// </summary>
    /// <param name="operationType">The operation type to handle.</param>
    /// <param name="handler">The async handler that processes the operation payload.</param>
    public void RegisterHandler(string operationType, Func<object?, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[operationType] = handler;
    }

    /// <summary>
    /// Removes a handler for a specific operation type.
    /// </summary>
    /// <param name="operationType">The operation type to unregister.</param>
    public void UnregisterHandler(string operationType)
    {
        _handlers.TryRemove(operationType, out _);
    }

    /// <summary>
    /// Enqueues an operation for processing and schedules a snapshot of the queue to durable
    /// storage, so an enqueued operation survives a crash before the next clean shutdown.
    /// </summary>
    /// <param name="operation">The operation to enqueue.</param>
    public void Enqueue(PendingOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _queue.Enqueue(operation);
        _ = PersistAsync();
    }

    /// <summary>
    /// Enqueues an operation for processing.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <param name="payload">The operation payload (must be JSON-serializable).</param>
    public void Enqueue(string operationType, object? payload = null)
    {
        Enqueue(new PendingOperation
        {
            OperationType = operationType,
            Payload = payload
        });
    }

    /// <summary>
    /// Dequeues the next operation for processing.
    /// </summary>
    /// <returns>The next operation, or null if the queue is empty.</returns>
    public PendingOperation? Dequeue()
    {
        return _queue.TryDequeue(out var op) ? op : null;
    }

    /// <summary>
    /// Peeks at the next operation without removing it.
    /// </summary>
    /// <returns>The next operation, or null if the queue is empty.</returns>
    public PendingOperation? Peek()
    {
        return _queue.TryPeek(out var op) ? op : null;
    }

    /// <summary>
    /// Gets a snapshot of all pending operations.
    /// </summary>
    public IReadOnlyList<PendingOperation> GetAll()
    {
        return _queue.ToArray();
    }

    /// <summary>
    /// Gets a stable audit snapshot of operations moved out of the replay queue. Quarantine
    /// records contain metadata only and never expose the original operation payload.
    /// </summary>
    public IReadOnlyList<PendingOperationQuarantineRecord> GetQuarantinedOperations()
    {
        return _quarantinedOperations.Values
            .OrderBy(static record => record.QuarantinedAtUtc)
            .ThenBy(static record => record.OperationType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.OperationId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Processes all pending operations by dequeuing and executing their registered handlers.
    /// Operations that fail and have retries remaining are re-enqueued. Operations with no
    /// registered handler are kept in the queue so a handler registered later (or by the next
    /// session) can still process them. Explicitly retired authentication-sensitive operation
    /// types are moved to payload-free quarantine before handler lookup. The surviving queue and
    /// quarantine audit are persisted afterwards.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public async Task ProcessAllAsync(CancellationToken ct = default)
    {
        var count = _queue.Count;
        for (var i = 0; i < count; i++)
        {
            if (!_queue.TryDequeue(out var op))
                break;

            if (IsRetiredOperationType(op.OperationType))
            {
                Quarantine(op, CurrentEnvelopeVersion);
                continue;
            }

            if (!_handlers.TryGetValue(op.OperationType, out var handler))
            {
                _queue.Enqueue(op);
                continue;
            }

            try
            {
                await handler(op.Payload).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation (for example shutdown mid-replay) is not a failure of the
                // operation: put it back untouched and persist so the next reconnect or
                // session replays it, then let the cancellation propagate.
                _queue.Enqueue(op);
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (op.RetryCount < op.MaxRetries)
                {
                    op.RetryCount++;
                    _queue.Enqueue(op);
                }
            }
        }

        await PersistAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the current queue contents to durable storage using an atomic replace, so a
    /// crash mid-write can never corrupt the previously persisted queue.
    /// </summary>
    public async Task PersistAsync(CancellationToken ct = default)
    {
        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_persistenceSuppressed)
            {
                return;
            }

            await WriteSnapshotLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private async Task WriteSnapshotLockedAsync(CancellationToken ct)
    {
        try
        {
            var envelope = new PendingOperationsEnvelope
            {
                Version = CurrentEnvelopeVersion,
                SavedAt = _utcNow(),
                Operations = _queue
                    .Select(static op => new PersistedPendingOperation
                    {
                        Id = op.Id,
                        OperationType = op.OperationType,
                        Payload = SerializePayload(op.Payload),
                        CreatedAt = op.CreatedAt,
                        RetryCount = op.RetryCount,
                        MaxRetries = op.MaxRetries
                    })
                    .ToList(),
                QuarantinedOperations = GetQuarantinedOperations().ToList()
            };

            var json = JsonSerializer.Serialize(envelope, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint);
            await AtomicFileWriter.WriteAsync(GetFilePath(), json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Persistence is best-effort: a failed snapshot must never take down the
            // enqueue/replay path. The next mutation or shutdown retries the write.
            Trace.TraceWarning("Pending-operations queue persistence failed: {0}", ex.Message);
        }
    }

    private async Task<bool> RestorePersistedOperationsAsync()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path))
            {
                return false;
            }

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<PendingOperationsEnvelope>(
                json, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint);
            if (envelope is null)
            {
                return false;
            }

            foreach (var quarantined in envelope.QuarantinedOperations ?? [])
            {
                _quarantinedOperations.TryAdd(BuildQuarantineKey(
                    quarantined.OperationId,
                    quarantined.OperationType,
                    quarantined.CreatedAt), quarantined);
            }

            var migratedRetiredOperations = false;
            foreach (var persisted in envelope.Operations)
            {
                if (IsRetiredOperationType(persisted.OperationType))
                {
                    Quarantine(persisted, envelope.Version);
                    migratedRetiredOperations = true;
                    continue;
                }

                _queue.Enqueue(new PendingOperation
                {
                    Id = persisted.Id,
                    OperationType = persisted.OperationType,
                    Payload = persisted.Payload,
                    CreatedAt = persisted.CreatedAt,
                    RetryCount = persisted.RetryCount,
                    MaxRetries = persisted.MaxRetries
                });
            }

            return migratedRetiredOperations;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged snapshot must not block startup; recovery resumes with an empty queue.
            Trace.TraceWarning("Pending-operations queue restore failed: {0}", ex.Message);
            return false;
        }
    }

    private static bool IsRetiredOperationType(string operationType)
        => RetiredOperationTypes.Contains(operationType);

    private void Quarantine(PendingOperation operation, int sourceEnvelopeVersion)
    {
        Quarantine(
            operation.Id,
            operation.OperationType,
            operation.CreatedAt,
            operation.RetryCount,
            operation.MaxRetries,
            sourceEnvelopeVersion);
    }

    private void Quarantine(PersistedPendingOperation operation, int sourceEnvelopeVersion)
    {
        Quarantine(
            operation.Id,
            operation.OperationType,
            operation.CreatedAt,
            operation.RetryCount,
            operation.MaxRetries,
            sourceEnvelopeVersion);
    }

    private void Quarantine(
        string operationId,
        string operationType,
        DateTime createdAt,
        int retryCount,
        int maxRetries,
        int sourceEnvelopeVersion)
    {
        var record = new PendingOperationQuarantineRecord(
            operationId,
            operationType,
            createdAt,
            retryCount,
            maxRetries,
            _utcNow(),
            sourceEnvelopeVersion,
            RetiredOperationReasonCode,
            RetiredOperationReason);
        _quarantinedOperations.TryAdd(
            BuildQuarantineKey(operationId, operationType, createdAt),
            record);
    }

    private static string BuildQuarantineKey(
        string operationId,
        string operationType,
        DateTime createdAt)
        => $"{operationType}\u001f{operationId}\u001f{createdAt.Ticks}\u001f{(int)createdAt.Kind}";

    private static JsonElement? SerializePayload(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (payload is JsonElement element)
        {
            return element;
        }

        return JsonSerializer.SerializeToElement(payload, Meridian.Ui.Services.DesktopJsonOptions.Api);
    }
}
