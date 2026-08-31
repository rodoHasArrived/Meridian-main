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
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Action<string, string> _writeFile;
    private readonly Func<string, string, CancellationToken, Task> _writeFileAsync;
    private LineageState _state = LineageState.CreateEmpty();

    public DataLineageService(string lineageStorePath, ILogger<DataLineageService> logger)
        : this(
            lineageStorePath,
            logger,
            static (path, content) => AtomicFileWriter.Write(path, content),
            static (path, content, ct) => AtomicFileWriter.WriteAsync(path, content, ct))
    {
    }

    internal DataLineageService(
        string lineageStorePath,
        ILogger<DataLineageService> logger,
        Action<string, string> writeFile,
        Func<string, string, CancellationToken, Task> writeFileAsync)
    {
        _lineageStorePath = lineageStorePath ?? throw new ArgumentNullException(nameof(lineageStorePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writeFile = writeFile ?? throw new ArgumentNullException(nameof(writeFile));
        _writeFileAsync = writeFileAsync ?? throw new ArgumentNullException(nameof(writeFileAsync));
        Volatile.Write(ref _state, Load());
    }

    /// <inheritdoc />
    public void RecordIngestion(string filePath, IngestionRecord record)
    {
        var ownedRecord = CloneIngestion(record);
        PersistChange(candidate =>
        {
            var graph = GetOrAddGraph(candidate, filePath);
            graph.Ingestions.Add(ownedRecord);
            graph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordTransformation(string sourceFilePath, string targetFilePath, TransformationRecord record)
    {
        var ownedRecord = CloneTransformation(record);
        PersistChange(candidate =>
        {
            // Link source to target
            var sourceGraph = GetOrAddGraph(candidate, sourceFilePath);
            sourceGraph.Downstream.Add(targetFilePath);
            sourceGraph.LastUpdatedUtc = DateTime.UtcNow;

            var targetGraph = GetOrAddGraph(candidate, targetFilePath);
            targetGraph.Upstream.Add(sourceFilePath);
            targetGraph.Transformations.Add(ownedRecord);
            targetGraph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordMigration(string sourceFilePath, string targetFilePath, MigrationRecord record)
    {
        var ownedRecord = CloneMigration(record);
        PersistChange(candidate =>
        {
            var sourceGraph = GetOrAddGraph(candidate, sourceFilePath);
            sourceGraph.Migrations.Add(ownedRecord);
            sourceGraph.Downstream.Add(targetFilePath);
            sourceGraph.LastUpdatedUtc = DateTime.UtcNow;

            var targetGraph = GetOrAddGraph(candidate, targetFilePath);
            targetGraph.Upstream.Add(sourceFilePath);
            targetGraph.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void RecordDeletion(string filePath, string reason)
    {
        PersistChange(candidate =>
        {
            if (!candidate.Graphs.TryGetValue(filePath, out var graph))
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
        var state = Volatile.Read(ref _state);
        return state.Graphs.TryGetValue(filePath, out var graph) ? CloneGraph(graph) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUpstream(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (!state.Graphs.ContainsKey(filePath))
            return Array.Empty<string>();

        var result = new HashSet<string>();
        CollectUpstream(state, filePath, result, maxDepth: 10);
        return result.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDownstream(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (!state.Graphs.ContainsKey(filePath))
            return Array.Empty<string>();

        var result = new HashSet<string>();
        CollectDownstream(state, filePath, result, maxDepth: 10);
        return result.ToList();
    }

    /// <inheritdoc />
    public LineageReport GenerateReport()
    {
        var state = Volatile.Read(ref _state);
        var activeFiles = state.Graphs.Values.Where(g => g.DeletedAtUtc == null).ToList();
        var deletedFiles = state.Graphs.Values.Where(g => g.DeletedAtUtc != null).ToList();

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
            TotalTrackedFiles: state.Graphs.Count,
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
            var candidate = CloneState(Volatile.Read(ref _state));
            await SaveToDiskAsync(candidate, ct).ConfigureAwait(false);
            Volatile.Write(ref _state, candidate);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private static void CollectUpstream(
        LineageState state,
        string filePath,
        HashSet<string> visited,
        int maxDepth)
    {
        if (maxDepth <= 0 || !visited.Add(filePath))
            return;

        if (state.Graphs.TryGetValue(filePath, out var graph))
        {
            foreach (var upstream in graph.Upstream)
            {
                CollectUpstream(state, upstream, visited, maxDepth - 1);
            }
        }
    }

    private static void CollectDownstream(
        LineageState state,
        string filePath,
        HashSet<string> visited,
        int maxDepth)
    {
        if (maxDepth <= 0 || !visited.Add(filePath))
            return;

        if (state.Graphs.TryGetValue(filePath, out var graph))
        {
            foreach (var downstream in graph.Downstream)
            {
                CollectDownstream(state, downstream, visited, maxDepth - 1);
            }
        }
    }

    private LineageState Load()
    {
        var state = LineageState.CreateEmpty();

        try
        {
            if (!File.Exists(_lineageStorePath))
                return state;

            var json = File.ReadAllText(_lineageStorePath);
            var data = JsonSerializer.Deserialize(json, DataLineageServiceJsonContext.Default.LineageStore);

            if (data?.Graphs != null)
            {
                foreach (var kvp in data.Graphs)
                {
                    state.Graphs[kvp.Key] = CloneGraph(kvp.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load lineage data from {Path}", _lineageStorePath);
        }

        return state;
    }

    private void PersistChange(Func<LineageState, bool> mutate)
    {
        // Bounded wait so persistence never blocks a thread-pool thread indefinitely.
        if (!_saveLock.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException(
                "Data lineage persistence lock timed out before the mutation could be committed.");
        }

        try
        {
            var candidate = CloneState(Volatile.Read(ref _state));
            if (!mutate(candidate))
            {
                return;
            }

            SaveToDisk(candidate);
            Volatile.Write(ref _state, candidate);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private static LineageStore CreateStoreSnapshot(LineageState state)
    {
        return new LineageStore
        {
            Version = "1.0.0",
            UpdatedAtUtc = DateTime.UtcNow,
            Graphs = state.Graphs
        };
    }

    private void SaveToDisk(LineageState state)
    {
        var dir = Path.GetDirectoryName(_lineageStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(state), DataLineageServiceJsonContext.Default.LineageStore);
        _writeFile(_lineageStorePath, json);
    }

    private async Task SaveToDiskAsync(LineageState state, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_lineageStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(state), DataLineageServiceJsonContext.Default.LineageStore);
        await _writeFileAsync(_lineageStorePath, json, ct).ConfigureAwait(false);
    }

    private static LineageGraph GetOrAddGraph(LineageState state, string filePath)
    {
        if (!state.Graphs.TryGetValue(filePath, out var graph))
        {
            graph = new LineageGraph { FilePath = filePath };
            state.Graphs[filePath] = graph;
        }

        return graph;
    }

    private static LineageState CloneState(LineageState state)
    {
        var clone = LineageState.CreateEmpty();
        foreach (var graph in state.Graphs)
        {
            clone.Graphs[graph.Key] = CloneGraph(graph.Value);
        }

        return clone;
    }

    private static LineageGraph CloneGraph(LineageGraph graph)
    {
        return new LineageGraph
        {
            FilePath = graph.FilePath,
            CreatedAtUtc = graph.CreatedAtUtc,
            LastUpdatedUtc = graph.LastUpdatedUtc,
            DeletedAtUtc = graph.DeletedAtUtc,
            DeletionReason = graph.DeletionReason,
            Upstream = graph.Upstream?.ToList() ?? [],
            Downstream = graph.Downstream?.ToList() ?? [],
            Ingestions = graph.Ingestions?.Select(CloneIngestion).ToList() ?? [],
            Transformations = graph.Transformations?.Select(CloneTransformation).ToList() ?? [],
            Migrations = graph.Migrations?.Select(CloneMigration).ToList() ?? []
        };
    }

    private static IngestionRecord CloneIngestion(IngestionRecord record)
        => record with { Parameters = CloneDictionary(record.Parameters) };

    private static TransformationRecord CloneTransformation(TransformationRecord record)
        => record with { Parameters = CloneDictionary(record.Parameters) };

    private static MigrationRecord CloneMigration(MigrationRecord record) => record with { };

    private static IReadOnlyDictionary<string, string>? CloneDictionary(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values == null)
        {
            return null;
        }

        var comparer = values is Dictionary<string, string> dictionary
            ? dictionary.Comparer
            : StringComparer.Ordinal;
        return new Dictionary<string, string>(values, comparer);
    }

    private sealed class LineageState
    {
        private LineageState()
        {
        }

        public Dictionary<string, LineageGraph> Graphs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static LineageState CreateEmpty() => new();
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
