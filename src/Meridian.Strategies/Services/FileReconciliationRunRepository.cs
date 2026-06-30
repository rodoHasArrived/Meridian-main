using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
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
public sealed class FileReconciliationRunRepository : IReconciliationRunRepository
{
    private readonly string _snapshotPath;
    private readonly ILogger<FileReconciliationRunRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileReconciliationRunRepository(
        string dataDirectory,
        ILogger<FileReconciliationRunRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // AtomicFileWriter.WriteAsync creates the destination directory, and the read path guards
        // on File.Exists, so no directory I/O is needed in the constructor.
        _snapshotPath = Path.Combine(dataDirectory, "reconciliation-runs.json");
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.Summary.ReconciliationRunId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Read-modify-write the latest persisted snapshot so runs written by another process
            // (e.g. the WPF desktop and the browser workstation sharing a data root) are not dropped.
            var runs = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            runs[detail.Summary.ReconciliationRunId] = detail;
            await PersistSnapshotAsync(runs, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var runs = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            return runs.GetValueOrDefault(reconciliationRunId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var runs = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            return runs.Values
                .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                .OrderByDescending(static run => run.Summary.CreatedAt)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var runs = await ReadSnapshotAsync(ct).ConfigureAwait(false);
            return runs.Values
                .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                .OrderByDescending(static run => run.Summary.CreatedAt)
                .Select(static run => run.Summary)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, ReconciliationRunDetail>> ReadSnapshotAsync(CancellationToken ct)
    {
        if (!File.Exists(_snapshotPath))
        {
            return new Dictionary<string, ReconciliationRunDetail>(StringComparer.Ordinal);
        }

        try
        {
            // FileShare.ReadWrite | Delete lets AtomicFileWriter's write-temp-then-rename replace the
            // snapshot concurrently without a sharing violation on Windows.
            await using var stream = new FileStream(
                _snapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var snapshot = await JsonSerializer.DeserializeAsync<ReconciliationRunSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
            var loaded = snapshot?.Runs ?? [];
            return loaded
                .Where(static run => !string.IsNullOrWhiteSpace(run.Summary.ReconciliationRunId))
                .GroupBy(static run => run.Summary.ReconciliationRunId, StringComparer.Ordinal)
                // Snapshot is persisted newest-first, so First() keeps the most recent run per id.
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Discarding corrupt reconciliation run snapshot at {Path}; starting empty.", _snapshotPath);
            return new Dictionary<string, ReconciliationRunDetail>(StringComparer.Ordinal);
        }
    }

    private async Task PersistSnapshotAsync(Dictionary<string, ReconciliationRunDetail> runs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new ReconciliationRunSnapshot(
            runs.Values
                .OrderByDescending(static run => run.Summary.CreatedAt)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }

    private sealed record ReconciliationRunSnapshot(IReadOnlyList<ReconciliationRunDetail> Runs);
}
