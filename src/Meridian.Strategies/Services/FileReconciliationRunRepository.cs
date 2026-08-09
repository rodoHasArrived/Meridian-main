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
    private static readonly AsyncLocal<string?> ActiveLeaseCallbackPath = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<FileReconciliationRunRepository> _logger;
    private readonly string _mutationLockPath;

    public FileReconciliationRunRepository(
        string dataDirectory,
        ILogger<FileReconciliationRunRepository> logger)
        : base(GetSnapshotPath(dataDirectory), JsonOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(dataDirectory);
        _mutationLockPath = Path.GetFullPath(Path.Combine(dataDirectory, "reconciliation-runs.lock"));
    }

    public async Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        _ = await SaveWithFirstObservationContinuityAsync(detail, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDetail> SaveWithFirstObservationContinuityAsync(
        ReconciliationRunDetail detail,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.Summary.ReconciliationRunId);

        // Serialize the full read-normalize-write cycle across repository instances and processes,
        // such as the WPF and browser workstations sharing one data root.
        await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
        return await UpdateSnapshotAsync(snapshot =>
        {
            var normalized = ReconciliationRunContinuity.UpsertAndNormalize(
                ToUniqueRuns(snapshot),
                detail);
            var persistedDetail = normalized.Single(run =>
                string.Equals(
                    run.Summary.ReconciliationRunId,
                    detail.Summary.ReconciliationRunId,
                    StringComparison.Ordinal));
            return (new ReconciliationRunSnapshot(normalized), persistedDetail);
        }, ct).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteWithLatestForRunLeaseAsync<TResult>(
        string runId,
        Func<ReconciliationRunDetail?, CancellationToken, Task<TResult>> callback,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(callback);

        await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
        var latest = await ReadSnapshotAsync(
                snapshot => ToUniqueRuns(snapshot)
                    .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                    .OrderByDescending(static run => run.Summary.CreatedAt)
                    .FirstOrDefault(),
                ct)
            .ConfigureAwait(false);
        var previousLeaseCallbackPath = ActiveLeaseCallbackPath.Value;
        ActiveLeaseCallbackPath.Value = _mutationLockPath;
        try
        {
            return await callback(latest, ct).ConfigureAwait(false);
        }
        finally
        {
            ActiveLeaseCallbackPath.Value = previousLeaseCallbackPath;
        }
    }

    public async Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        return await ReadSnapshotAsync(
            snapshot => ToUniqueRuns(snapshot).FirstOrDefault(run =>
                string.Equals(
                    run.Summary.ReconciliationRunId,
                    reconciliationRunId,
                    StringComparison.Ordinal)), ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await ReadSnapshotAsync(snapshot => ToUniqueRuns(snapshot)
            .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(static run => run.Summary.CreatedAt)
            .FirstOrDefault(), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await ReadSnapshotAsync(snapshot => ToUniqueRuns(snapshot)
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

        return Path.Combine(dataDirectory, "reconciliation-runs.json");
    }

    private async Task<FileStream> AcquireMutationLeaseAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _mutationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<ReconciliationRunDetail> ToUniqueRuns(ReconciliationRunSnapshot snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var runs = new List<ReconciliationRunDetail>(snapshot.Runs.Count);
        foreach (var run in snapshot.Runs)
        {
            if (!string.IsNullOrWhiteSpace(run.Summary.ReconciliationRunId)
                && seen.Add(run.Summary.ReconciliationRunId))
            {
                // Snapshots are newest-first, so the first retained duplicate remains authoritative.
                runs.Add(run);
            }
        }

        return runs;
    }

    private void ThrowIfLeaseCallbackReentry()
    {
        var activePath = ActiveLeaseCallbackPath.Value;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(activePath, _mutationLockPath, pathComparison))
        {
            throw new InvalidOperationException(
                "A reconciliation repository lease callback cannot re-enter the repository.");
        }
    }

    /// <summary>Persisted snapshot shape. Public only because it parameterizes the base class.</summary>
    public sealed record ReconciliationRunSnapshot(IReadOnlyList<ReconciliationRunDetail> Runs);
}
