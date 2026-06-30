using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// File-backed <see cref="IStatementReconciliationCheckpointStore"/> that persists statement
/// reconciliation checkpoints to a JSON snapshot using atomic writes. A checkpoint records the
/// last completed stage of an import/reconcile run; persisting it is what makes
/// <see cref="StatementReconciliationOrchestrator"/> resumable across process restarts and crashes,
/// which the in-memory store cannot guarantee.
/// </summary>
public sealed class FileStatementReconciliationCheckpointStore : IStatementReconciliationCheckpointStore
{
    private readonly string _snapshotPath;
    private readonly ILogger<FileStatementReconciliationCheckpointStore>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Dictionary<Guid, StatementReconciliationCheckpoint>? _checkpoints;

    public FileStatementReconciliationCheckpointStore(
        string dataRoot,
        ILogger<FileStatementReconciliationCheckpointStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger;

        // AtomicFileWriter.WriteAsync creates the destination directory, and the read path guards
        // on File.Exists, so no directory I/O is needed in the constructor.
        _snapshotPath = Path.Combine(dataRoot, "reconciliation", "statement-checkpoints.json");
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<StatementReconciliationCheckpoint?> GetAsync(Guid accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _checkpoints!.GetValueOrDefault(accountId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(StatementReconciliationCheckpoint checkpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _checkpoints![checkpoint.AccountId] = checkpoint;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_checkpoints is not null)
        {
            return;
        }

        if (!File.Exists(_snapshotPath))
        {
            _checkpoints = new Dictionary<Guid, StatementReconciliationCheckpoint>();
            return;
        }

        try
        {
            // FileShare.ReadWrite | Delete lets AtomicFileWriter's write-temp-then-rename replace the
            // snapshot concurrently without a sharing violation on Windows.
            await using var stream = new FileStream(
                _snapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var snapshot = await JsonSerializer.DeserializeAsync<CheckpointSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
            var loaded = snapshot?.Checkpoints ?? [];
            _checkpoints = loaded
                .GroupBy(static checkpoint => checkpoint.AccountId)
                // Snapshot is persisted newest-first, so First() keeps the most recent checkpoint per account.
                .ToDictionary(static group => group.Key, static group => group.First());
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Discarding corrupt statement reconciliation checkpoint snapshot at {Path}; starting empty.", _snapshotPath);
            _checkpoints = new Dictionary<Guid, StatementReconciliationCheckpoint>();
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new CheckpointSnapshot(
            _checkpoints!.Values
                .OrderByDescending(static checkpoint => checkpoint.UpdatedAtUtc)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }

    private sealed record CheckpointSnapshot(IReadOnlyList<StatementReconciliationCheckpoint> Checkpoints);
}
