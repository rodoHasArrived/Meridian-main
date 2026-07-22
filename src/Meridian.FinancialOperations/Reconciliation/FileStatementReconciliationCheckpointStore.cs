using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// File-backed <see cref="IStatementReconciliationCheckpointStore"/> that persists statement
/// reconciliation checkpoints to a JSON snapshot using atomic writes. A checkpoint records the
/// last completed stage of an import/reconcile run; persisting it is what makes
/// <see cref="StatementReconciliationOrchestrator"/> resumable across process restarts and crashes,
/// which the in-memory store cannot guarantee. Every operation reads the current snapshot from disk
/// (no in-memory cache), so a checkpoint advanced by another process remains visible to reads.
/// </summary>
public sealed class FileStatementReconciliationCheckpointStore
    : JsonFileSnapshotStore<FileStatementReconciliationCheckpointStore.CheckpointSnapshot>,
      IStatementReconciliationCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<FileStatementReconciliationCheckpointStore>? _logger;

    public FileStatementReconciliationCheckpointStore(
        string dataRoot,
        ILogger<FileStatementReconciliationCheckpointStore>? logger = null)
        : base(GetSnapshotPath(dataRoot), JsonOptions)
    {
        _logger = logger;
    }

    public Task<StatementReconciliationCheckpoint?> GetAsync(Guid accountId, CancellationToken ct)
        => ReadSnapshotAsync(snapshot => ToCheckpointMap(snapshot).GetValueOrDefault(accountId), ct);

    public async Task UpsertAsync(StatementReconciliationCheckpoint checkpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        // Read-modify-write the latest persisted snapshot so checkpoints written by another
        // process are not dropped, shrinking the cross-process lost-update window.
        await UpdateSnapshotAsync(snapshot =>
        {
            var checkpoints = ToCheckpointMap(snapshot);
            checkpoints[checkpoint.AccountId] = checkpoint;
            return new CheckpointSnapshot(
                checkpoints.Values
                    .OrderByDescending(static candidate => candidate.UpdatedAtUtc)
                    .ToArray());
        }, ct).ConfigureAwait(false);
    }

    protected override CheckpointSnapshot CreateEmptySnapshot() => new([]);

    protected override CheckpointSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger?.LogWarning(exception, "Discarding corrupt statement reconciliation checkpoint snapshot at {Path}; starting empty.", SnapshotPath);
        return CreateEmptySnapshot();
    }

    private static string GetSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        // AtomicFileWriter.WriteAsync creates the destination directory, and the base load path
        // guards on File.Exists, so no directory I/O is needed in the constructor.
        return Path.Combine(dataRoot, "reconciliation", "statement-checkpoints.json");
    }

    private static Dictionary<Guid, StatementReconciliationCheckpoint> ToCheckpointMap(CheckpointSnapshot snapshot)
        => snapshot.Checkpoints
            .GroupBy(static checkpoint => checkpoint.AccountId)
            // Snapshot is persisted newest-first, so First() keeps the most recent checkpoint per account.
            .ToDictionary(static group => group.Key, static group => group.First());

    /// <summary>Persisted snapshot shape. Public only because it parameterizes the base class.</summary>
    public sealed record CheckpointSnapshot(IReadOnlyList<StatementReconciliationCheckpoint> Checkpoints);
}
