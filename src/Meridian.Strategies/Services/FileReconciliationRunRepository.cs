using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

/// <summary>
/// File-backed <see cref="IReconciliationRunRepository"/> that persists reconciliation run
/// details to a JSON snapshot using atomic writes, so completed runs and their history survive
/// process restarts. Replaces <see cref="InMemoryReconciliationRunRepository"/> for production
/// workstation hosting while preserving identical query semantics. Every operation reads the
/// current snapshot from disk (no in-memory cache), so runs written by another process sharing the
/// same data directory remain visible to reads.
/// </summary>
public sealed class FileReconciliationRunRepository
    : JsonFileSnapshotStore<FileReconciliationRunRepository.ReconciliationRunSnapshot>,
      IReconciliationRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<FileReconciliationRunRepository> _logger;

    public FileReconciliationRunRepository(
        string dataDirectory,
        ILogger<FileReconciliationRunRepository> logger)
        : base(GetSnapshotPath(dataDirectory), JsonOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.Summary.ReconciliationRunId);

        // Read-modify-write the latest persisted snapshot so runs written by another process
        // (e.g. the WPF desktop and the browser workstation sharing a data root) are not dropped.
        await UpdateSnapshotAsync(snapshot =>
        {
            var runs = ToRunMap(snapshot);
            runs[detail.Summary.ReconciliationRunId] = detail;
            return new ReconciliationRunSnapshot(
                runs.Values
                    .OrderByDescending(static run => run.Summary.CreatedAt)
                    .ToArray());
        }, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        return await ReadSnapshotAsync(
            snapshot => ToRunMap(snapshot).GetValueOrDefault(reconciliationRunId), ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await ReadSnapshotAsync(snapshot => ToRunMap(snapshot).Values
            .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(static run => run.Summary.CreatedAt)
            .FirstOrDefault(), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await ReadSnapshotAsync(snapshot => ToRunMap(snapshot).Values
            .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(static run => run.Summary.CreatedAt)
            .Select(static run => run.Summary)
            .ToArray(), ct).ConfigureAwait(false);
    }

    protected override ReconciliationRunSnapshot CreateEmptySnapshot() => new([]);

    protected override ReconciliationRunSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Discarding corrupt reconciliation run snapshot at {Path}; starting empty.", SnapshotPath);
        return CreateEmptySnapshot();
    }

    private static string GetSnapshotPath(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // AtomicFileWriter.WriteAsync creates the destination directory, and the base load path
        // guards on File.Exists, so no directory I/O is needed in the constructor.
        return Path.Combine(dataDirectory, "reconciliation-runs.json");
    }

    private static Dictionary<string, ReconciliationRunDetail> ToRunMap(ReconciliationRunSnapshot snapshot)
        => snapshot.Runs
            .Where(static run => !string.IsNullOrWhiteSpace(run.Summary.ReconciliationRunId))
            .GroupBy(static run => run.Summary.ReconciliationRunId, StringComparer.Ordinal)
            // Snapshot is persisted newest-first, so First() keeps the most recent run per id.
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

    /// <summary>Persisted snapshot shape. Public only because it parameterizes the base class.</summary>
    public sealed record ReconciliationRunSnapshot(IReadOnlyList<ReconciliationRunDetail> Runs);
}
