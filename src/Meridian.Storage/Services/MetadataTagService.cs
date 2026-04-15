using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, FileMetadataRecord> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _metadataStorePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public MetadataTagService(string metadataStorePath)
    {
        _metadataStorePath = metadataStorePath ?? throw new ArgumentNullException(nameof(metadataStorePath));
        Load();
    }

    /// <inheritdoc />
    public void SetTag(string filePath, string key, string value)
    {
        PersistChange(() =>
        {
            var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
            record.Tags[key] = value;
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public void SetTags(string filePath, IReadOnlyDictionary<string, string> tags)
    {
        PersistChange(() =>
        {
            var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
            foreach (var kvp in tags)
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
        if (_metadata.TryGetValue(filePath, out var record) && record.Tags.TryGetValue(key, out var value))
            return value;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAllTags(string filePath)
    {
        if (_metadata.TryGetValue(filePath, out var record))
            return record.Tags;
        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public bool RemoveTag(string filePath, string key)
    {
        var removed = false;
        PersistChange(() =>
        {
            if (!_metadata.TryGetValue(filePath, out var record))
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
        PersistChange(() =>
        {
            var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
            record.Lineage.Add(entry);
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<LineageEntry> GetLineage(string filePath)
    {
        if (_metadata.TryGetValue(filePath, out var record))
            return record.Lineage.AsReadOnly();
        return Array.Empty<LineageEntry>();
    }

    /// <inheritdoc />
    public void SetInsight(string filePath, string insightKey, DataInsight insight)
    {
        PersistChange(() =>
        {
            var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
            record.Insights[insightKey] = insight;
            record.LastModifiedUtc = DateTime.UtcNow;
            return true;
        });
    }

    /// <inheritdoc />
    public DataInsight? GetInsight(string filePath, string insightKey)
    {
        if (_metadata.TryGetValue(filePath, out var record) && record.Insights.TryGetValue(insightKey, out var insight))
            return insight;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DataInsight> GetAllInsights(string filePath)
    {
        if (_metadata.TryGetValue(filePath, out var record))
            return record.Insights;
        return new Dictionary<string, DataInsight>();
    }

    /// <inheritdoc />
    public void SetQualityScore(string filePath, double score, string? scoredBy = null)
    {
        PersistChange(() =>
        {
            var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
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
        return PersistChangeAsync(() =>
        {
            ApplyQualityAssessment(
                filePath,
                score,
                insight,
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

        if (assessments.Count == 0)
        {
            return Task.CompletedTask;
        }

        return PersistChangeAsync(() =>
        {
            foreach (var assessment in assessments)
            {
                ApplyQualityAssessment(
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
        if (_metadata.TryGetValue(filePath, out var record))
            return record.QualityScore;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SearchByTag(string key, string? valuePattern = null)
    {
        var results = new List<string>();
        foreach (var kvp in _metadata)
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
        return _metadata
            .Where(kvp => kvp.Value.QualityScore >= minScore && kvp.Value.QualityScore <= maxScore)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc />
    public FileMetadataRecord? GetFullMetadata(string filePath)
    {
        return _metadata.TryGetValue(filePath, out var record) ? record : null;
    }

    /// <inheritdoc />
    public void RemoveMetadata(string filePath)
    {
        PersistChange(() => _metadata.TryRemove(filePath, out _));
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

    private static FileMetadataRecord CreateDefaultRecord(string filePath)
    {
        return new FileMetadataRecord
        {
            FilePath = filePath,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_metadataStorePath))
                return;

            var json = File.ReadAllText(_metadataStorePath);
            var data = JsonSerializer.Deserialize(json, MetadataTagServiceJsonContext.Default.MetadataStore);

            if (data?.Records != null)
            {
                foreach (var kvp in data.Records)
                {
                    _metadata[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Expected I/O or parse errors on load - start fresh
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

    private async Task PersistChangeAsync(Func<bool> mutate, CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!mutate())
            {
                return;
            }

            await SaveToDiskAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void ApplyQualityAssessment(
        string filePath,
        double score,
        DataInsight insight,
        string? scoredBy,
        string qualityInsightKey)
    {
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        var persistedAtUtc = insight.ComputedAtUtc == default ? DateTime.UtcNow : insight.ComputedAtUtc;
        record.QualityScore = Math.Clamp(score, 0.0, 1.0);
        record.QualityScoredBy = scoredBy;
        record.QualityScoredAtUtc = persistedAtUtc;
        record.Insights[qualityInsightKey] = insight;
        record.LastModifiedUtc = persistedAtUtc;
    }

    private MetadataStore CreateStoreSnapshot()
    {
        return new MetadataStore
        {
            Version = "1.0.0",
            UpdatedAtUtc = DateTime.UtcNow,
            Records = _metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private void SaveToDisk()
    {
        var dir = Path.GetDirectoryName(_metadataStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(), MetadataTagServiceJsonContext.Default.MetadataStore);
        AtomicFileWriter.Write(_metadataStorePath, json);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_metadataStorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CreateStoreSnapshot(), MetadataTagServiceJsonContext.Default.MetadataStore);
        await AtomicFileWriter.WriteAsync(_metadataStorePath, json, ct).ConfigureAwait(false);
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
