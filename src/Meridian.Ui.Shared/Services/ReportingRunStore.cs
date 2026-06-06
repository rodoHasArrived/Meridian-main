using System.Text.Json;
using Meridian.Reporting;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingRunStoreOptions(string RootDirectory);

public sealed class FileReportingRunStore : IReportingRunStore
{
    private const string SnapshotFileName = "reporting-runs.json";

    private readonly ReportingRunStoreOptions _options;
    private readonly ILogger<FileReportingRunStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportingRunStore(
        ReportingRunStoreOptions options,
        ILogger<FileReportingRunStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.RootDirectory);
        Directory.CreateDirectory(_options.RootDirectory);
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25)
    {
        var runs = LoadSnapshot().Runs;
        return runs
            .OrderByDescending(static run => run.UpdatedAtUtc)
            .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    public ReportingOutputManifest? GetManifest(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return LoadSnapshot().Runs
            .FirstOrDefault(run => string.Equals(run.Manifest.RunId, runId, StringComparison.OrdinalIgnoreCase))
            ?.Manifest;
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return LoadSnapshot().Runs
            .FirstOrDefault(run => string.Equals(run.Manifest.RunId, runId, StringComparison.OrdinalIgnoreCase))
            ?.AuditTrail ?? [];
    }

    public async Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = LoadSnapshot().Runs
                .Where(run => !string.Equals(run.Manifest.RunId, manifest.RunId, StringComparison.OrdinalIgnoreCase))
                .Append(new ReportingRunSnapshot(manifest, auditTrail.ToArray(), DateTimeOffset.UtcNow))
                .OrderByDescending(static run => run.UpdatedAtUtc)
                .ThenBy(static run => run.Manifest.RunId, StringComparer.Ordinal)
                .ToArray();
            var snapshot = new ReportingRunStoreSnapshot(current);
            await AtomicFileWriter
                .WriteAsync(SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ReportingRunStoreSnapshot LoadSnapshot()
    {
        if (!File.Exists(SnapshotPath))
        {
            return new ReportingRunStoreSnapshot([]);
        }

        try
        {
            var json = File.ReadAllText(SnapshotPath);
            return JsonSerializer.Deserialize<ReportingRunStoreSnapshot>(json, _jsonOptions) ?? new ReportingRunStoreSnapshot([]);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to load reporting run snapshot from {SnapshotPath}.", SnapshotPath);
            return new ReportingRunStoreSnapshot([]);
        }
    }

    private string SnapshotPath => Path.Combine(_options.RootDirectory, SnapshotFileName);

    private sealed record ReportingRunStoreSnapshot(IReadOnlyList<ReportingRunSnapshot> Runs);
}
