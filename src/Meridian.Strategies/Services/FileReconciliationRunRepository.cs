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
/// workstation hosting while preserving identical query semantics.
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

    private Dictionary<string, ReconciliationRunDetail>? _runs;

    public FileReconciliationRunRepository(
        string dataDirectory,
        ILogger<FileReconciliationRunRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(dataDirectory);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _runs![detail.Summary.ReconciliationRunId] = detail;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _runs!.GetValueOrDefault(reconciliationRunId);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _runs!.Values
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _runs!.Values
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

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_runs is not null)
        {
            return;
        }

        if (!File.Exists(_snapshotPath))
        {
            _runs = new Dictionary<string, ReconciliationRunDetail>(StringComparer.Ordinal);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync<ReconciliationRunSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
            var loaded = snapshot?.Runs ?? [];
            _runs = loaded
                .Where(static run => !string.IsNullOrWhiteSpace(run.Summary.ReconciliationRunId))
                .GroupBy(static run => run.Summary.ReconciliationRunId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Discarding corrupt reconciliation run snapshot at {Path}; starting empty.", _snapshotPath);
            _runs = new Dictionary<string, ReconciliationRunDetail>(StringComparer.Ordinal);
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new ReconciliationRunSnapshot(
            _runs!.Values
                .OrderByDescending(static run => run.Summary.CreatedAt)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }

    private sealed record ReconciliationRunSnapshot(IReadOnlyList<ReconciliationRunDetail> Runs);
}
