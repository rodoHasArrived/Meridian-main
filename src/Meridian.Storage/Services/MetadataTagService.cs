using System.Collections.Concurrent;
using System.Text.Json;
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
    private DateTime _lastSaveTime = DateTime.MinValue;

    public MetadataTagService(string metadataStorePath)
    {
        _metadataStorePath = metadataStorePath ?? throw new ArgumentNullException(nameof(metadataStorePath));
        Load();
    }

    /// <inheritdoc />
    public void SetTag(string filePath, string key, string value)
    {
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        record.Tags[key] = value;
        record.LastModifiedUtc = DateTime.UtcNow;
        ScheduleSave();
    }

    /// <inheritdoc />
    public void SetTags(string filePath, IReadOnlyDictionary<string, string> tags)
    {
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        foreach (var kvp in tags)
        {
            record.Tags[kvp.Key] = kvp.Value;
        }
        record.LastModifiedUtc = DateTime.UtcNow;
        ScheduleSave();
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
        if (_metadata.TryGetValue(filePath, out var record))
        {
            var removed = record.Tags.Remove(key);
            if (removed)
            {
                record.LastModifiedUtc = DateTime.UtcNow;
                ScheduleSave();
            }
            return removed;
        }
        return false;
    }

    /// <inheritdoc />
    public void RecordLineage(string filePath, LineageEntry entry)
    {
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        record.Lineage.Add(entry);
        record.LastModifiedUtc = DateTime.UtcNow;
        ScheduleSave();
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
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        record.Insights[insightKey] = insight;
        record.LastModifiedUtc = DateTime.UtcNow;
        ScheduleSave();
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
        var record = _metadata.GetOrAdd(filePath, _ => CreateDefaultRecord(filePath));
        record.QualityScore = Math.Clamp(score, 0.0, 1.0);
        record.QualityScoredBy = scoredBy;
        record.QualityScoredAtUtc = DateTime.UtcNow;
        record.LastModifiedUtc = DateTime.UtcNow;
        ScheduleSave();
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
        if (_metadata.TryRemove(filePath, out _))
        {
            ScheduleSave();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_metadataStorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var data = new MetadataStore
            {
                Version = "1.0.0",
                UpdatedAtUtc = DateTime.UtcNow,
                Records = _metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await AtomicFileWriter.WriteAsync(_metadataStorePath, json, ct);
            _lastSaveTime = DateTime.UtcNow;
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
            var data = JsonSerializer.Deserialize<MetadataStore>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

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

    private void ScheduleSave()
    {
        // Debounce saves to at most once per 5 seconds
        if ((DateTime.UtcNow - _lastSaveTime) < TimeSpan.FromSeconds(5))
            return;

        _ = SaveInBackgroundAsync();
    }

    private async Task SaveInBackgroundAsync(CancellationToken ct = default)
    {
        try
        { await SaveAsync(); }
        catch (IOException) { /* Background save failure is non-critical */ }
        catch (UnauthorizedAccessException) { /* Permission issues during background save */ }
    }

    private sealed class MetadataStore
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
    Task SaveAsync(CancellationToken ct = default);
}

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
