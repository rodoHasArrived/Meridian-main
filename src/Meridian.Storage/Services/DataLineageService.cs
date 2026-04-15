using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for tracking data lineage: provenance, transformations, and dependency graphs
/// across the entire storage system. Records where data came from, what transformations
/// were applied, and how data flows between storage tiers and formats.
/// </summary>
[ImplementsAdr("ADR-002", "Data lineage tracking for storage operations")]
public sealed class DataLineageService : IDataLineageService
{
    private readonly string _lineageStorePath;
    private readonly ILogger<DataLineageService> _logger;
    private readonly ConcurrentDictionary<string, LineageGraph> _graphs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public DataLineageService(string lineageStorePath, ILogger<DataLineageService> logger)
    {
        _lineageStorePath = lineageStorePath ?? throw new ArgumentNullException(nameof(lineageStorePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Load();
    }

    /// <inheritdoc />
    public void RecordIngestion(string filePath, IngestionRecord record)
    {
        PersistChange(() =>
        {
            var graph = _graphs.GetOrAdd(filePath, _ => new LineageGraph { FilePath = filePath });
            graph.Ingestions.Add(record);
            graph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordTransformation(string sourceFilePath, string targetFilePath, TransformationRecord record)
    {
        PersistChange(() =>
        {
            // Link source to target
            var sourceGraph = _graphs.GetOrAdd(sourceFilePath, _ => new LineageGraph { FilePath = sourceFilePath });
            sourceGraph.Downstream.Add(targetFilePath);
            sourceGraph.LastUpdatedUtc = DateTime.UtcNow;

            var targetGraph = _graphs.GetOrAdd(targetFilePath, _ => new LineageGraph { FilePath = targetFilePath });
            targetGraph.Upstream.Add(sourceFilePath);
            targetGraph.Transformations.Add(record);
            targetGraph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordMigration(string sourceFilePath, string targetFilePath, MigrationRecord record)
    {
        PersistChange(() =>
        {
            var sourceGraph = _graphs.GetOrAdd(sourceFilePath, _ => new LineageGraph { FilePath = sourceFilePath });
            sourceGraph.Migrations.Add(record);
            sourceGraph.Downstream.Add(targetFilePath);
            sourceGraph.LastUpdatedUtc = DateTime.UtcNow;

            var targetGraph = _graphs.GetOrAdd(targetFilePath, _ => new LineageGraph { FilePath = targetFilePath });
            targetGraph.Upstream.Add(sourceFilePath);
            targetGraph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordDeletion(string filePath, string reason)
    {
        PersistChange(() =>
        {
            if (!_graphs.TryGetValue(filePath, out var graph))
            {
                return false;
            }

            graph.DeletedAtUtc = DateTime.UtcNow;
            graph.DeletionReason = reason;
            graph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public LineageGraph? GetLineageGraph(string filePath)
    {
        return _graphs.TryGetValue(filePath, out var graph) ? graph : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUpstream(string filePath)
    {
        if (!_graphs.TryGetValue(filePath, out var graph))
            return Array.Empty<string>();

        var result = new HashSet<string>();
        CollectUpstream(filePath, result, maxDepth: 10);
        return result.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDownstream(string filePath)
    {
        if (!_graphs.TryGetValue(filePath, out var graph))
            return Array.Empty<string>();

        var result = new HashSet<string>();
        CollectDownstream(filePath, result, maxDepth: 10);
        return result.ToList();
    }

    /// <inheritdoc />
    public LineageReport GenerateReport()
    {
        var activeFiles = _graphs.Values.Where(g => g.DeletedAtUtc == null).ToList();
        var deletedFiles = _graphs.Values.Where(g => g.DeletedAtUtc != null).ToList();

        var sourceDistribution = activeFiles
            .SelectMany(g => g.Ingestions)
            .GroupBy(i => i.Provider)
            .ToDictionary(g => g.Key, g => g.Count());

        var transformationTypes = activeFiles
            .SelectMany(g => g.Transformations)
            .GroupBy(t => t.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new LineageReport(
            GeneratedAtUtc: DateTime.UtcNow,
            TotalTrackedFiles: _graphs.Count,
            ActiveFiles: activeFiles.Count,
            DeletedFiles: deletedFiles.Count,
            TotalIngestions: activeFiles.Sum(g => g.Ingestions.Count),
            TotalTransformations: activeFiles.Sum(g => g.Transformations.Count),
            TotalMigrations: activeFiles.Sum(g => g.Migrations.Count),
            SourceDistribution: sourceDistribution,
            TransformationTypes: transformationTypes);
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SaveToDiskAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void CollectUpstream(string filePath, HashSet<string> visited, int maxDepth)
    {
        if (maxDepth <= 0 || !visited.Add(filePath))
            return;

        if (_graphs.TryGetValue(filePath, out var graph))
        {
            foreach (var upstream in graph.Upstream)
            {
                CollectUpstream(upstream, visited, maxDepth - 1);
            }
        }
    }

    private void CollectDownstream(string filePath, HashSet<string> visited, int maxDepth)
    {
        if (maxDepth <= 0 || !visited.Add(filePath))
            return;

        if (_graphs.TryGetValue(filePath, out var graph))
        {
            foreach (var downstream in graph.Downstream)
            {
                CollectDownstream(downstream, visited, maxDepth - 1);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_lineageStorePath))
                return;

            var json = File.ReadAllText(_lineageStorePath);
            var data = JsonSerializer.Deserialize(json, DataLineageServiceJsonContext.Default.LineageStore);

            if (data?.Graphs != null)
            {
                foreach (var kvp in data.Graphs)
                {
                    _graphs[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load lineage data from {Path}", _lineageStorePath);
        }
    }

    private void PersistChange(Func<bool> mutate)
    {
        _saveLock.Wait();
        try
        {
            if (!mutate())
            {
                return;
            }

            SaveToDisk();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private LineageStore CreateStoreSnapshot()
    {
        return new LineageStore
        {
            Version = "1.0.0",
            UpdatedAtUtc = DateTime.UtcNow,
            Graphs = _graphs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private void SaveToDisk()
    {
        var dir = Path.GetDirectoryName(_lineageStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(), DataLineageServiceJsonContext.Default.LineageStore);
        AtomicFileWriter.Write(_lineageStorePath, json);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_lineageStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(), DataLineageServiceJsonContext.Default.LineageStore);
        await AtomicFileWriter.WriteAsync(_lineageStorePath, json, ct).ConfigureAwait(false);
    }

    internal sealed class LineageStore
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime UpdatedAtUtc { get; set; }
        public Dictionary<string, LineageGraph> Graphs { get; set; } = new();
    }
}

/// <summary>
/// Interface for data lineage tracking service.
/// </summary>
public interface IDataLineageService
{
    void RecordIngestion(string filePath, IngestionRecord record);
    void RecordTransformation(string sourceFilePath, string targetFilePath, TransformationRecord record);
    void RecordMigration(string sourceFilePath, string targetFilePath, MigrationRecord record);
    void RecordDeletion(string filePath, string reason);
    LineageGraph? GetLineageGraph(string filePath);
    IReadOnlyList<string> GetUpstream(string filePath);
    IReadOnlyList<string> GetDownstream(string filePath);
    LineageReport GenerateReport();
    Task SaveAsync(CancellationToken ct = default);
}

// Lineage types
public sealed class LineageGraph
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletionReason { get; set; }
    public List<string> Upstream { get; set; } = new();
    public List<string> Downstream { get; set; } = new();
    public List<IngestionRecord> Ingestions { get; set; } = new();
    public List<TransformationRecord> Transformations { get; set; } = new();
    public List<MigrationRecord> Migrations { get; set; } = new();
}

public sealed record IngestionRecord(
    DateTime TimestampUtc,
    string Provider,
    string Symbol,
    string EventType,
    long EventCount,
    string? ApiEndpoint = null,
    TimeSpan? Latency = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record TransformationRecord(
    DateTime TimestampUtc,
    string Type,
    string Description,
    string? Algorithm = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record MigrationRecord(
    DateTime TimestampUtc,
    string SourceTier,
    string TargetTier,
    string? CompressionChange = null,
    string? FormatChange = null,
    long BytesBefore = 0,
    long BytesAfter = 0);

public sealed record LineageReport(
    DateTime GeneratedAtUtc,
    int TotalTrackedFiles,
    int ActiveFiles,
    int DeletedFiles,
    int TotalIngestions,
    int TotalTransformations,
    int TotalMigrations,
    Dictionary<string, int> SourceDistribution,
    Dictionary<string, int> TransformationTypes);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DataLineageService.LineageStore))]
[JsonSerializable(typeof(LineageGraph))]
[JsonSerializable(typeof(Dictionary<string, LineageGraph>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IngestionRecord))]
[JsonSerializable(typeof(List<IngestionRecord>))]
[JsonSerializable(typeof(TransformationRecord))]
[JsonSerializable(typeof(List<TransformationRecord>))]
[JsonSerializable(typeof(MigrationRecord))]
[JsonSerializable(typeof(List<MigrationRecord>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class DataLineageServiceJsonContext : JsonSerializerContext
{
}
