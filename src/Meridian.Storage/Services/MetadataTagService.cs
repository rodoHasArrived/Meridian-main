using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing rich metadata tags, insights, and lineage tracking on stored data files.
/// Provides a flexible key-value tagging system with typed metadata, hierarchical taxonomy support,
/// and file-level insights derived from content analysis.
/// </summary>
public sealed class MetadataTagService : IMetadataTagService
{
    private readonly string _metadataStorePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Action<string, string> _writeFile;
    private readonly Func<string, string, CancellationToken, Task> _writeFileAsync;
    private MetadataState _state = MetadataState.CreateEmpty();

    public MetadataTagService(string metadataStorePath)
        : this(
            metadataStorePath,
            static (path, content) => AtomicFileWriter.Write(path, content),
            static (path, content, ct) => AtomicFileWriter.WriteAsync(path, content, ct))
    {
    }

    internal MetadataTagService(
        string metadataStorePath,
        Action<string, string> writeFile,
        Func<string, string, CancellationToken, Task> writeFileAsync)
    {
        _metadataStorePath = metadataStorePath ?? throw new ArgumentNullException(nameof(metadataStorePath));
        _writeFile = writeFile ?? throw new ArgumentNullException(nameof(writeFile));
        _writeFileAsync = writeFileAsync ?? throw new ArgumentNullException(nameof(writeFileAsync));
        Volatile.Write(ref _state, Load());
    }

    /// <inheritdoc />
    public void SetTag(string filePath, string key, string value)
    {
        PersistChange(candidate =>
        {
            var record = GetOrAddRecord(candidate, filePath);
            record.Tags[key] = value;
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void SetTags(string filePath, IReadOnlyDictionary<string, string> tags)
    {
        var ownedTags = tags.ToArray();
        PersistChange(candidate =>
        {
            var record = GetOrAddRecord(candidate, filePath);
            foreach (var kvp in ownedTags)
            {
                record.Tags[kvp.Key] = kvp.Value;
            }

            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public string? GetTag(string filePath, string key)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record) && record.Tags.TryGetValue(key, out var value))
            return value;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAllTags(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record))
            return CloneDictionary(record.Tags);
        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public bool RemoveTag(string filePath, string key)
    {
        var removed = false;
        PersistChange(candidate =>
        {
            if (!candidate.Records.TryGetValue(filePath, out var record))
            {
                return false;
            }

            removed = record.Tags.Remove(key);
            if (!removed)
            {
                return false;
            }

            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });

        return removed;
    }

    /// <inheritdoc />
    public void RecordLineage(string filePath, LineageEntry entry)
    {
        var ownedEntry = CloneLineageEntry(entry);
        PersistChange(candidate =>
        {
            var record = GetOrAddRecord(candidate, filePath);
            record.Lineage.Add(ownedEntry);
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<LineageEntry> GetLineage(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record))
            return record.Lineage.Select(CloneLineageEntry).ToArray();
        return Array.Empty<LineageEntry>();
    }

    /// <inheritdoc />
    public void SetInsight(string filePath, string insightKey, DataInsight insight)
    {
        var ownedInsight = CloneInsight(insight);
        PersistChange(candidate =>
        {
            var record = GetOrAddRecord(candidate, filePath);
            record.Insights[insightKey] = ownedInsight;
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public DataInsight? GetInsight(string filePath, string insightKey)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record) && record.Insights.TryGetValue(insightKey, out var insight))
            return CloneInsight(insight);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DataInsight> GetAllInsights(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record))
        {
            return record.Insights.ToDictionary(
                entry => entry.Key,
                entry => CloneInsight(entry.Value),
                record.Insights.Comparer);
        }

        return new Dictionary<string, DataInsight>();
    }

    /// <inheritdoc />
    public void SetQualityScore(string filePath, double score, string? scoredBy = null)
    {
        PersistChange(candidate =>
        {
            var record = GetOrAddRecord(candidate, filePath);
            record.QualityScore = Math.Clamp(score, 0.0, 1.0);
            record.QualityScoredBy = scoredBy;
            record.QualityScoredAtUtc = DateTime.UtcNow;
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public Task SetQualityAssessmentAsync(
        string filePath,
        double score,
        DataInsight insight,
        string? scoredBy = null,
        CancellationToken ct = default)
    {
        var ownedInsight = CloneInsight(insight);
        return PersistChangeAsync(candidate =>
        {
            ApplyQualityAssessment(
                candidate,
                filePath,
                score,
                ownedInsight,
                scoredBy,
                qualityInsightKey: "quality_assessment");
            return true;
        }, ct);
    }

    /// <inheritdoc />
    public Task SetQualityAssessmentsAsync(
        IReadOnlyCollection<QualityAssessmentMetadataUpdate> assessments,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        var ownedAssessments = assessments
            .Select(CloneQualityAssessment)
            .ToArray();

        if (ownedAssessments.Length == 0)
        {
            return Task.CompletedTask;
        }

        return PersistChangeAsync(candidate =>
        {
            foreach (var assessment in ownedAssessments)
            {
                ApplyQualityAssessment(
                    candidate,
                    assessment.FilePath,
                    assessment.Score,
                    assessment.Insight,
                    assessment.ScoredBy,
                    assessment.InsightKey);
            }

            return true;
        }, ct);
    }

    /// <inheritdoc />
    public double? GetQualityScore(string filePath)
    {
        var state = Volatile.Read(ref _state);
        if (state.Records.TryGetValue(filePath, out var record))
            return record.QualityScore;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SearchByTag(string key, string? valuePattern = null)
    {
        var state = Volatile.Read(ref _state);
        var results = new List<string>();
        foreach (var kvp in state.Records)
        {
            if (kvp.Value.Tags.TryGetValue(key, out var value))
            {
                if (valuePattern == null || value.Contains(valuePattern, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(kvp.Key);
                }
            }
        }
        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SearchByQualityScore(double minScore, double maxScore = 1.0)
    {
        var state = Volatile.Read(ref _state);
        return state.Records
            .Where(kvp => kvp.Value.QualityScore >= minScore && kvp.Value.QualityScore <= maxScore)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc />
    public FileMetadataRecord? GetFullMetadata(string filePath)
    {
        var state = Volatile.Read(ref _state);
        return state.Records.TryGetValue(filePath, out var record) ? CloneRecord(record) : null;
    }

    /// <inheritdoc />
    public void RemoveMetadata(string filePath)
    {
        PersistChange(candidate => candidate.Records.Remove(filePath));
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

    private static FileMetadataRecord CreateDefaultRecord(string filePath)
    {
        return new FileMetadataRecord
        {
            FilePath = filePath,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
    }

    private MetadataState Load()
    {
        var state = MetadataState.CreateEmpty();

        try
        {
            if (!File.Exists(_metadataStorePath))
                return state;

            var json = File.ReadAllText(_metadataStorePath);
            var data = JsonSerializer.Deserialize(json, MetadataTagServiceJsonContext.Default.MetadataStore);

            if (data?.Records != null)
            {
                foreach (var kvp in data.Records)
                {
                    state.Records[kvp.Key] = CloneRecord(kvp.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Expected I/O or parse errors on load - start fresh with empty in-memory state.
            System.Diagnostics.Trace.TraceWarning(
                "MetadataTagService: failed to load metadata from {0}: {1}", _metadataStorePath, ex.Message);
        }

        return state;
    }

    private void PersistChange(Func<MetadataState, bool> mutate)
    {
        // A metadata mutation is not successful unless the durable snapshot is written.
        if (!_saveLock.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Metadata persistence lock timed out before the mutation could be committed.");

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

    private async Task PersistChangeAsync(Func<MetadataState, bool> mutate, CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidate = CloneState(Volatile.Read(ref _state));
            if (!mutate(candidate))
            {
                return;
            }

            await SaveToDiskAsync(candidate, ct).ConfigureAwait(false);
            Volatile.Write(ref _state, candidate);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void ApplyQualityAssessment(
        MetadataState state,
        string filePath,
        double score,
        DataInsight insight,
        string? scoredBy,
        string qualityInsightKey)
    {
        var record = GetOrAddRecord(state, filePath);
        var persistedAtUtc = insight.ComputedAtUtc == default ? DateTime.UtcNow : insight.ComputedAtUtc;
        record.QualityScore = Math.Clamp(score, 0.0, 1.0);
        record.QualityScoredBy = scoredBy;
        record.QualityScoredAtUtc = persistedAtUtc;
        record.Insights[qualityInsightKey] = insight;
        record.LastModifiedUtc = persistedAtUtc;
    }

    private static MetadataStore CreateStoreSnapshot(MetadataState state)
    {
        return new MetadataStore
        {
            Version = "1.0.0",
            UpdatedAtUtc = DateTime.UtcNow,
            Records = state.Records
        };
    }

    private void SaveToDisk(MetadataState state)
    {
        var dir = Path.GetDirectoryName(_metadataStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(state), MetadataTagServiceJsonContext.Default.MetadataStore);
        _writeFile(_metadataStorePath, json);
    }

    private async Task SaveToDiskAsync(MetadataState state, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_metadataStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(state), MetadataTagServiceJsonContext.Default.MetadataStore);
        await _writeFileAsync(_metadataStorePath, json, ct).ConfigureAwait(false);
    }

    private static FileMetadataRecord GetOrAddRecord(MetadataState state, string filePath)
    {
        if (!state.Records.TryGetValue(filePath, out var record))
        {
            record = CreateDefaultRecord(filePath);
            state.Records[filePath] = record;
        }

        return record;
    }

    private static MetadataState CloneState(MetadataState state)
    {
        var clone = MetadataState.CreateEmpty();
        foreach (var record in state.Records)
        {
            clone.Records[record.Key] = CloneRecord(record.Value);
        }

        return clone;
    }

    private static FileMetadataRecord CloneRecord(FileMetadataRecord record)
    {
        return new FileMetadataRecord
        {
            FilePath = record.FilePath,
            CreatedUtc = record.CreatedUtc,
            LastModifiedUtc = record.LastModifiedUtc,
            Tags = CloneDictionary(record.Tags),
            Lineage = (record.Lineage ?? []).Select(CloneLineageEntry).ToList(),
            Insights = CloneInsights(record.Insights),
            QualityScore = record.QualityScore,
            QualityScoredBy = record.QualityScoredBy,
            QualityScoredAtUtc = record.QualityScoredAtUtc
        };
    }

    private static Dictionary<string, string> CloneDictionary(Dictionary<string, string>? values)
        => values == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(values, values.Comparer);

    private static Dictionary<string, DataInsight> CloneInsights(
        Dictionary<string, DataInsight>? insights)
    {
        if (insights == null)
        {
            return new Dictionary<string, DataInsight>();
        }

        return insights.ToDictionary(
            entry => entry.Key,
            entry => CloneInsight(entry.Value),
            insights.Comparer);
    }

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

    private static LineageEntry CloneLineageEntry(LineageEntry entry)
        => entry with { Parameters = CloneDictionary(entry.Parameters) };

    private static DataInsight CloneInsight(DataInsight insight) => insight with { };

    private static QualityAssessmentMetadataUpdate CloneQualityAssessment(
        QualityAssessmentMetadataUpdate assessment)
        => assessment with { Insight = CloneInsight(assessment.Insight) };

    private sealed class MetadataState
    {
        private MetadataState()
        {
        }

        public Dictionary<string, FileMetadataRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static MetadataState CreateEmpty() => new();
    }

    internal sealed class MetadataStore
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime UpdatedAtUtc { get; set; }
        public Dictionary<string, FileMetadataRecord> Records { get; set; } = new();
    }
}

/// <summary>
/// Interface for metadata tagging and lineage tracking on stored data files.
/// </summary>
public interface IMetadataTagService
{
    void SetTag(string filePath, string key, string value);
    void SetTags(string filePath, IReadOnlyDictionary<string, string> tags);
    string? GetTag(string filePath, string key);
    IReadOnlyDictionary<string, string> GetAllTags(string filePath);
    bool RemoveTag(string filePath, string key);
    void RecordLineage(string filePath, LineageEntry entry);
    IReadOnlyList<LineageEntry> GetLineage(string filePath);
    void SetInsight(string filePath, string insightKey, DataInsight insight);
    DataInsight? GetInsight(string filePath, string insightKey);
    IReadOnlyDictionary<string, DataInsight> GetAllInsights(string filePath);
    void SetQualityScore(string filePath, double score, string? scoredBy = null);
    double? GetQualityScore(string filePath);
    IReadOnlyList<string> SearchByTag(string key, string? valuePattern = null);
    IReadOnlyList<string> SearchByQualityScore(double minScore, double maxScore = 1.0);
    FileMetadataRecord? GetFullMetadata(string filePath);
    void RemoveMetadata(string filePath);
    Task SetQualityAssessmentAsync(string filePath, double score, DataInsight insight, string? scoredBy = null, CancellationToken ct = default);
    Task SetQualityAssessmentsAsync(IReadOnlyCollection<QualityAssessmentMetadataUpdate> assessments, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

public sealed record QualityAssessmentMetadataUpdate(
    string FilePath,
    double Score,
    DataInsight Insight,
    string? ScoredBy = null,
    string InsightKey = "quality_assessment");

/// <summary>
/// Full metadata record for a stored data file.
/// </summary>
public sealed class FileMetadataRecord
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<LineageEntry> Lineage { get; set; } = new();
    public Dictionary<string, DataInsight> Insights { get; set; } = new();
    public double QualityScore { get; set; } = 1.0;
    public string? QualityScoredBy { get; set; }
    public DateTime? QualityScoredAtUtc { get; set; }
}

/// <summary>
/// A lineage entry tracking the provenance of data.
/// </summary>
public sealed record LineageEntry(
    DateTime TimestampUtc,
    string Operation,
    string? SourcePath,
    string? SourceProvider,
    string? TransformationType,
    string? Description,
    IReadOnlyDictionary<string, string>? Parameters = null
);

/// <summary>
/// An insight derived from data content analysis.
/// </summary>
public sealed record DataInsight(
    string Category,
    string Description,
    double? NumericValue,
    string? Unit,
    DateTime ComputedAtUtc,
    InsightSeverity Severity = InsightSeverity.Info
);

/// <summary>
/// Severity level for data insights.
/// </summary>
public enum InsightSeverity : byte
{
    Info,
    Notable,
    Warning,
    Critical
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MetadataTagService.MetadataStore))]
[JsonSerializable(typeof(FileMetadataRecord))]
[JsonSerializable(typeof(Dictionary<string, FileMetadataRecord>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, DataInsight>))]
[JsonSerializable(typeof(LineageEntry))]
[JsonSerializable(typeof(List<LineageEntry>))]
[JsonSerializable(typeof(DataInsight))]
internal sealed partial class MetadataTagServiceJsonContext : JsonSerializerContext
{
}
