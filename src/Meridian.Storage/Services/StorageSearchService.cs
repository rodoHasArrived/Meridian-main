using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for searching and discovering data across storage.
/// Provides multi-level indexing and faceted search capabilities.
/// </summary>
public sealed class StorageSearchService : IStorageSearchService
{
    private readonly StorageOptions _options;
    private readonly ISourceRegistry? _sourceRegistry;
    private readonly JsonlStoragePolicy? _pathParser;
    private readonly ConcurrentDictionary<string, SymbolIndex> _symbolIndex = new();
    private readonly ConcurrentDictionary<string, DateIndex> _dateIndex = new();
    private readonly ConcurrentDictionary<string, FileMetadata> _fileMetadata = new();
    private DateTime _lastIndexUpdate = DateTime.MinValue;

    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".parquet" };

    public StorageSearchService(StorageOptions options, ISourceRegistry? sourceRegistry = null, JsonlStoragePolicy? pathParser = null)
    {
        _options = options;
        _sourceRegistry = sourceRegistry;
        // Use provided parser or create one with default options for path parsing
        _pathParser = pathParser ?? new JsonlStoragePolicy(options, sourceRegistry);
    }

    public async Task<SearchResult<FileSearchResult>> SearchFilesAsync(FileSearchQuery query, CancellationToken ct = default)
    {
        await EnsureIndexUpdatedAsync(ct);

        var results = new List<FileSearchResult>();
        var allFiles = GetAllDataFiles();

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();

            var metadata = await GetOrCreateMetadataAsync(file, ct);
            if (metadata == null)
                continue;

            // Apply filters
            if (query.Symbols?.Length > 0 && !query.Symbols.Contains(metadata.Symbol, StringComparer.OrdinalIgnoreCase))
                continue;

            if (query.Types?.Length > 0 && !query.Types.Any(t => t.ToString().Equals(metadata.EventType, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (query.Sources?.Length > 0 && !query.Sources.Contains(metadata.Source, StringComparer.OrdinalIgnoreCase))
                continue;

            if (query.From.HasValue && metadata.Date < query.From.Value)
                continue;

            if (query.To.HasValue && metadata.Date > query.To.Value)
                continue;

            if (query.MinSize.HasValue && metadata.SizeBytes < query.MinSize.Value)
                continue;

            if (query.MaxSize.HasValue && metadata.SizeBytes > query.MaxSize.Value)
                continue;

            if (query.MinQualityScore.HasValue && metadata.QualityScore < query.MinQualityScore.Value)
                continue;

            if (!string.IsNullOrEmpty(query.PathPattern))
            {
                var pattern = query.PathPattern.Replace("*", ".*").Replace("?", ".");
                if (!Regex.IsMatch(file, pattern, RegexOptions.IgnoreCase))
                    continue;
            }

            results.Add(new FileSearchResult(
                Path: file,
                Symbol: metadata.Symbol,
                EventType: metadata.EventType,
                Source: metadata.Source,
                Date: metadata.Date,
                SizeBytes: metadata.SizeBytes,
                EventCount: metadata.EventCount,
                QualityScore: metadata.QualityScore
            ));
        }

        // Sort
        results = query.SortBy switch
        {
            SortField.Date => query.Descending
                ? results.OrderByDescending(r => r.Date).ToList()
                : results.OrderBy(r => r.Date).ToList(),
            SortField.Size => query.Descending
                ? results.OrderByDescending(r => r.SizeBytes).ToList()
                : results.OrderBy(r => r.SizeBytes).ToList(),
            SortField.Symbol => query.Descending
                ? results.OrderByDescending(r => r.Symbol).ToList()
                : results.OrderBy(r => r.Symbol).ToList(),
            SortField.QualityScore => query.Descending
                ? results.OrderByDescending(r => r.QualityScore).ToList()
                : results.OrderBy(r => r.QualityScore).ToList(),
            _ => results.OrderByDescending(r => r.Date).ToList()
        };

        // Apply pagination
        var totalMatches = results.Count;
        var paged = results.Skip(query.Skip).Take(query.Take).ToList();

        return new SearchResult<FileSearchResult>(
            TotalMatches: totalMatches,
            Results: paged
        );
    }

    public async Task<SearchResult<EventSearchResult>> SearchEventsAsync(EventSearchQuery query, CancellationToken ct = default)
    {
        var results = new List<EventSearchResult>();

        // Find relevant files
        var fileQuery = new FileSearchQuery(
            Symbols: new[] { query.Symbol },
            Types: new[] { query.Type },
            From: query.From,
            To: query.To,
            Skip: 0,
            Take: 1000
        );

        var fileResults = await SearchFilesAsync(fileQuery, ct);

        foreach (var fileResult in fileResults.Results)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var evt in ReadEventsAsync(fileResult.Path, ct))
            {
                if (results.Count >= query.Limit)
                    break;

                // Apply event-level filters
                if (query.MinPrice.HasValue || query.MaxPrice.HasValue || query.MinVolume.HasValue || query.Side.HasValue)
                {
                    if (!MatchesEventFilter(evt, query))
                        continue;
                }

                if (query.SequenceFrom.HasValue && evt.Sequence < query.SequenceFrom.Value)
                    continue;

                if (query.SequenceTo.HasValue && evt.Sequence > query.SequenceTo.Value)
                    continue;

                results.Add(new EventSearchResult(
                    Timestamp: evt.Timestamp,
                    Symbol: evt.Symbol,
                    EventType: evt.Type.ToString(),
                    Sequence: evt.Sequence,
                    Source: evt.Source,
                    Payload: evt.Payload
                ));
            }

            if (results.Count >= query.Limit)
                break;
        }

        return new SearchResult<EventSearchResult>(
            TotalMatches: results.Count,
            Results: results
        );
    }

    public async Task<DataCatalog> DiscoverAsync(DiscoveryQuery query, CancellationToken ct = default)
    {
        await EnsureIndexUpdatedAsync(ct);

        var symbols = new Dictionary<string, StorageSymbolCatalogEntry>();
        var dateRange = (MinDate: DateTimeOffset.MaxValue, MaxDate: DateTimeOffset.MinValue);
        var sources = new HashSet<string>();
        var eventTypes = new HashSet<string>();
        long totalEvents = 0;
        long totalBytes = 0;

        foreach (var file in GetAllDataFiles())
        {
            ct.ThrowIfCancellationRequested();

            var metadata = await GetOrCreateMetadataAsync(file, ct);
            if (metadata == null)
                continue;

            // Update aggregations
            if (!symbols.ContainsKey(metadata.Symbol))
            {
                symbols[metadata.Symbol] = new StorageSymbolCatalogEntry(
                    Symbol: metadata.Symbol,
                    FirstDate: metadata.Date,
                    LastDate: metadata.Date,
                    TotalEvents: metadata.EventCount,
                    TotalBytes: metadata.SizeBytes,
                    Sources: new HashSet<string> { metadata.Source },
                    EventTypes: new HashSet<string> { metadata.EventType }
                );
            }
            else
            {
                var entry = symbols[metadata.Symbol];
                symbols[metadata.Symbol] = entry with
                {
                    FirstDate = entry.FirstDate < metadata.Date ? entry.FirstDate : metadata.Date,
                    LastDate = entry.LastDate > metadata.Date ? entry.LastDate : metadata.Date,
                    TotalEvents = entry.TotalEvents + metadata.EventCount,
                    TotalBytes = entry.TotalBytes + metadata.SizeBytes,
                    Sources = new HashSet<string>(entry.Sources) { metadata.Source },
                    EventTypes = new HashSet<string>(entry.EventTypes) { metadata.EventType }
                };
            }

            sources.Add(metadata.Source);
            eventTypes.Add(metadata.EventType);
            totalEvents += metadata.EventCount;
            totalBytes += metadata.SizeBytes;

            if (metadata.Date < dateRange.MinDate)
                dateRange.MinDate = metadata.Date;
            if (metadata.Date > dateRange.MaxDate)
                dateRange.MaxDate = metadata.Date;
        }

        return new DataCatalog(
            GeneratedAt: DateTimeOffset.UtcNow,
            RootPath: _options.RootPath,
            Symbols: symbols.Values.OrderBy(s => s.Symbol).ToList(),
            Sources: sources.OrderBy(s => s).ToList(),
            EventTypes: eventTypes.OrderBy(t => t).ToList(),
            DateRange: new DateRange(dateRange.MinDate, dateRange.MaxDate),
            TotalEvents: totalEvents,
            TotalBytes: totalBytes
        );
    }

    public async Task<FacetedSearchResult> SearchWithFacetsAsync(FacetedSearchQuery query, CancellationToken ct = default)
    {
        var fileResults = await SearchFilesAsync(new FileSearchQuery(
            Symbols: query.Symbols,
            Types: query.Types,
            Sources: query.Sources,
            From: query.From,
            To: query.To,
            Skip: 0,
            Take: 10000
        ), ct);

        // Build facets
        var facets = new Dictionary<string, Dictionary<string, int>>();

        // Date facet
        var byDate = fileResults.Results
            .GroupBy(r => r.Date.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());
        facets["by_date"] = byDate;

        // Symbol facet
        var bySymbol = fileResults.Results
            .GroupBy(r => r.Symbol)
            .ToDictionary(g => g.Key, g => g.Count());
        facets["by_symbol"] = bySymbol;

        // Event type facet
        var byType = fileResults.Results
            .GroupBy(r => r.EventType)
            .ToDictionary(g => g.Key, g => g.Count());
        facets["by_event_type"] = byType;

        // Source facet
        var bySource = fileResults.Results
            .GroupBy(r => r.Source)
            .ToDictionary(g => g.Key, g => g.Count());
        facets["by_source"] = bySource;

        // Hour facet (from date)
        var byHour = fileResults.Results
            .GroupBy(r => r.Date.Hour.ToString("D2"))
            .ToDictionary(g => g.Key, g => g.Count());
        facets["by_hour"] = byHour;

        return new FacetedSearchResult(
            TotalMatches: fileResults.TotalMatches,
            FileCount: fileResults.Results.Count,
            Facets: facets,
            Results: fileResults.Results.Take(query.MaxResults).ToList()
        );
    }

    public StorageQuery? ParseNaturalLanguageQuery(string naturalQuery)
    {
        var query = new StorageQueryBuilder();

        // Common patterns
        var symbolMatch = Regex.Match(naturalQuery, @"\b([A-Z]{1,5})\b");
        if (symbolMatch.Success)
        {
            query.AddSymbol(symbolMatch.Groups[1].Value);
        }

        // Date patterns
        if (Regex.IsMatch(naturalQuery, @"last\s+week", RegexOptions.IgnoreCase))
        {
            query.SetDateRange(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        }
        else if (Regex.IsMatch(naturalQuery, @"last\s+month", RegexOptions.IgnoreCase))
        {
            query.SetDateRange(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);
        }
        else if (Regex.IsMatch(naturalQuery, @"yesterday", RegexOptions.IgnoreCase))
        {
            var yesterday = DateTimeOffset.UtcNow.Date.AddDays(-1);
            query.SetDateRange(yesterday, yesterday.AddDays(1));
        }

        // Date specific: "January 15th", "on 2024-01-15"
        var dateMatch = Regex.Match(naturalQuery, @"(\d{4}-\d{2}-\d{2})");
        if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Groups[1].Value, out var date))
        {
            query.SetDateRange(date, date.AddDays(1));
        }

        // Event type patterns
        if (Regex.IsMatch(naturalQuery, @"\btrade[s]?\b", RegexOptions.IgnoreCase))
        {
            query.AddType(MarketEventType.Trade);
        }
        if (Regex.IsMatch(naturalQuery, @"\bquote[s]?\b|\bbbo\b", RegexOptions.IgnoreCase))
        {
            query.AddType(MarketEventType.BboQuote);
        }
        if (Regex.IsMatch(naturalQuery, @"\bdepth\b|\bl2\b|\bsnapshot[s]?\b", RegexOptions.IgnoreCase))
        {
            query.AddType(MarketEventType.L2Snapshot);
        }

        // Volume filter
        var volumeMatch = Regex.Match(naturalQuery, @"volume\s*(over|above|greater than|>)\s*(\d+[MK]?)", RegexOptions.IgnoreCase);
        if (volumeMatch.Success)
        {
            var volumeStr = volumeMatch.Groups[2].Value.ToUpper();
            var multiplier = volumeStr.EndsWith("M") ? 1_000_000 : volumeStr.EndsWith("K") ? 1_000 : 1;
            var volume = long.Parse(Regex.Replace(volumeStr, "[MK]", "")) * multiplier;
            query.SetMinVolume(volume);
        }

        return query.Build();
    }

    public async Task UpdateIndexAsync(string filePath, IndexUpdateType updateType, CancellationToken ct = default)
    {
        switch (updateType)
        {
            case IndexUpdateType.FileCreated:
            case IndexUpdateType.FileAppended:
                var metadata = await CreateMetadataAsync(filePath, ct);
                if (metadata != null)
                {
                    _fileMetadata[filePath] = metadata;
                    UpdateSymbolIndex(metadata);
                    UpdateDateIndex(metadata);
                }
                break;

            case IndexUpdateType.FileDeleted:
                _fileMetadata.TryRemove(filePath, out _);
                break;

            case IndexUpdateType.FileMoved:
                _fileMetadata.TryRemove(filePath, out _);
                break;
        }
    }

    public async Task RebuildIndexAsync(string[] paths, RebuildOptions options, CancellationToken ct = default)
    {
        _symbolIndex.Clear();
        _dateIndex.Clear();
        _fileMetadata.Clear();

        var allFiles = paths.Length > 0
            ? paths.SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                : File.Exists(p) ? new[] { p } : Array.Empty<string>())
            : GetAllDataFiles();

        var semaphore = new SemaphoreSlim(options.ParallelIndexers);

        var tasks = allFiles.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var metadata = await CreateMetadataAsync(file, ct);
                if (metadata != null)
                {
                    _fileMetadata[file] = metadata;
                    UpdateSymbolIndex(metadata);
                    UpdateDateIndex(metadata);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _lastIndexUpdate = DateTime.UtcNow;
    }

    private async Task EnsureIndexUpdatedAsync(CancellationToken ct)
    {
        if ((DateTime.UtcNow - _lastIndexUpdate) > TimeSpan.FromMinutes(5))
        {
            await RebuildIndexAsync(Array.Empty<string>(), new RebuildOptions(), ct);
        }
    }

    private IEnumerable<string> GetAllDataFiles()
    {
        if (!Directory.Exists(_options.RootPath))
            return Enumerable.Empty<string>();

        return Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<FileMetadata?> GetOrCreateMetadataAsync(string filePath, CancellationToken ct)
    {
        if (_fileMetadata.TryGetValue(filePath, out var cached))
            return cached;

        var metadata = await CreateMetadataAsync(filePath, ct);
        if (metadata != null)
        {
            _fileMetadata[filePath] = metadata;
        }
        return metadata;
    }

    private async Task<FileMetadata?> CreateMetadataAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return null;

            // Use centralized path parser if available
            var parsed = _pathParser?.TryParsePath(filePath);

            var symbol = parsed?.Symbol ?? "Unknown";
            var eventType = parsed?.EventType ?? "Unknown";
            var source = parsed?.Source ?? "Unknown";
            var date = parsed?.Date ?? new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

            // Fallback to heuristic parsing if parser returned unknown values
            if (symbol == "Unknown" || eventType == "Unknown")
            {
                var fallback = ParsePathFallback(filePath, fileInfo);
                if (symbol == "Unknown")
                    symbol = fallback.Symbol;
                if (eventType == "Unknown")
                    eventType = fallback.EventType;
                if (source == "Unknown" && fallback.Source != "Unknown")
                    source = fallback.Source;
            }

            // Count events
            long eventCount = 0;
            try
            {
                await foreach (var _ in File.ReadLinesAsync(filePath, ct))
                {
                    eventCount++;
                }
            }
            catch (IOException) { /* File may be inaccessible */ }

            return new FileMetadata(
                FilePath: filePath,
                Symbol: symbol,
                EventType: eventType,
                Source: source,
                Date: date,
                SizeBytes: fileInfo.Length,
                EventCount: eventCount,
                QualityScore: 1.0,
                CreatedAt: fileInfo.CreationTimeUtc,
                ModifiedAt: fileInfo.LastWriteTimeUtc
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fallback parser using heuristics when the policy-based parser fails.
    /// </summary>
    private static (string Symbol, string EventType, string Source) ParsePathFallback(string filePath, FileInfo fileInfo)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string symbol = "Unknown";
        string eventType = "Unknown";
        string source = "Unknown";

        foreach (var part in parts)
        {
            if (symbol == "Unknown" && Regex.IsMatch(part, @"^[A-Z]{1,5}$"))
                symbol = part;
            if (eventType == "Unknown" && Enum.TryParse<MarketEventType>(part, true, out var type))
                eventType = type.ToString();
            if (source == "Unknown")
            {
                var lowered = part.ToLowerInvariant();
                if (new[] { "alpaca", "ib", "polygon", "stooq", "yahoo", "tiingo", "finnhub" }.Contains(lowered))
                    source = part;
            }
        }

        return (symbol, eventType, source);
    }

    private void UpdateSymbolIndex(FileMetadata metadata)
    {
        _symbolIndex.AddOrUpdate(
            metadata.Symbol,
            _ => new SymbolIndex(metadata.Symbol, new List<string> { metadata.FilePath }),
            (_, existing) =>
            {
                existing.Files.Add(metadata.FilePath);
                return existing;
            });
    }

    private void UpdateDateIndex(FileMetadata metadata)
    {
        var dateKey = metadata.Date.ToString("yyyy-MM-dd");
        _dateIndex.AddOrUpdate(
            dateKey,
            _ => new DateIndex(dateKey, new List<string> { metadata.FilePath }),
            (_, existing) =>
            {
                existing.Files.Add(metadata.FilePath);
                return existing;
            });
    }

    private bool MatchesEventFilter(MarketEvent evt, EventSearchQuery query)
    {
        // Would need to inspect payload for price/volume/side filtering
        return true;
    }

    private async IAsyncEnumerable<MarketEvent> ReadEventsAsync(string filePath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!File.Exists(filePath))
            yield break;

        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            MarketEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<MarketEvent>(line);
            }
            catch (JsonException) { /* Skip malformed lines */ }

            if (evt != null)
                yield return evt;
        }
    }
}

/// <summary>
/// Interface for storage search service.
/// </summary>
public interface IStorageSearchService
{
    Task<SearchResult<FileSearchResult>> SearchFilesAsync(FileSearchQuery query, CancellationToken ct = default);
    Task<SearchResult<EventSearchResult>> SearchEventsAsync(EventSearchQuery query, CancellationToken ct = default);
    Task<DataCatalog> DiscoverAsync(DiscoveryQuery query, CancellationToken ct = default);
    Task<FacetedSearchResult> SearchWithFacetsAsync(FacetedSearchQuery query, CancellationToken ct = default);
    StorageQuery? ParseNaturalLanguageQuery(string naturalQuery);
    Task UpdateIndexAsync(string filePath, IndexUpdateType updateType, CancellationToken ct = default);
    Task RebuildIndexAsync(string[] paths, RebuildOptions options, CancellationToken ct = default);
}

// Query types
public sealed record FileSearchQuery(
    string[]? Symbols = null,
    MarketEventType[]? Types = null,
    string[]? Sources = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    long? MinSize = null,
    long? MaxSize = null,
    double? MinQualityScore = null,
    string? PathPattern = null,
    SortField SortBy = SortField.Date,
    bool Descending = true,
    int Skip = 0,
    int Take = 100
);

public sealed record EventSearchQuery(
    string Symbol,
    MarketEventType Type,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    long? MinVolume = null,
    AggressorSide? Side = null,
    long? SequenceFrom = null,
    long? SequenceTo = null,
    int Limit = 1000
);

public sealed record FacetedSearchQuery(
    string[]? Symbols = null,
    MarketEventType[]? Types = null,
    string[]? Sources = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int MaxResults = 100
);

public sealed record DiscoveryQuery;

public enum SortField : byte { Date, Size, Symbol, QualityScore }

// Result types
public sealed record SearchResult<T>(
    int TotalMatches,
    IReadOnlyList<T> Results
);

public sealed record FileSearchResult(
    string Path,
    string Symbol,
    string EventType,
    string Source,
    DateTimeOffset Date,
    long SizeBytes,
    long EventCount,
    double QualityScore
);

public sealed record EventSearchResult(
    DateTimeOffset Timestamp,
    string Symbol,
    string EventType,
    long Sequence,
    string Source,
    object? Payload
);

public sealed record FacetedSearchResult(
    int TotalMatches,
    int FileCount,
    Dictionary<string, Dictionary<string, int>> Facets,
    IReadOnlyList<FileSearchResult> Results
);

public sealed record DataCatalog(
    DateTimeOffset GeneratedAt,
    string RootPath,
    IReadOnlyList<StorageSymbolCatalogEntry> Symbols,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> EventTypes,
    DateRange DateRange,
    long TotalEvents,
    long TotalBytes
);

public sealed record StorageSymbolCatalogEntry(
    string Symbol,
    DateTimeOffset FirstDate,
    DateTimeOffset LastDate,
    long TotalEvents,
    long TotalBytes,
    HashSet<string> Sources,
    HashSet<string> EventTypes
);

public sealed record DateRange(DateTimeOffset Start, DateTimeOffset End);

// Index types
public sealed record SymbolIndex(string Symbol, List<string> Files);
public sealed record DateIndex(string Date, List<string> Files);

public sealed record FileMetadata(
    string FilePath,
    string Symbol,
    string EventType,
    string Source,
    DateTimeOffset Date,
    long SizeBytes,
    long EventCount,
    double QualityScore,
    DateTime CreatedAt,
    DateTime ModifiedAt
);

public enum IndexUpdateType : byte
{
    FileCreated,
    FileAppended,
    FileDeleted,
    FileMoved,
    MetadataChanged
}

public sealed record RebuildOptions(
    int ParallelIndexers = 4
);

// Query builder
public sealed record StorageQuery(
    string[]? Symbols,
    MarketEventType[]? Types,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string[]? Sources,
    long? MinVolume
);

public sealed class StorageQueryBuilder
{
    private readonly List<string> _symbols = new();
    private readonly List<MarketEventType> _types = new();
    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    private long? _minVolume;

    public void AddSymbol(string symbol) => _symbols.Add(symbol);
    public void AddType(MarketEventType type) => _types.Add(type);
    public void SetDateRange(DateTimeOffset from, DateTimeOffset to) { _from = from; _to = to; }
    public void SetMinVolume(long volume) => _minVolume = volume;

    public StorageQuery Build() => new(
        _symbols.Count > 0 ? _symbols.ToArray() : null,
        _types.Count > 0 ? _types.ToArray() : null,
        _from,
        _to,
        null,
        _minVolume
    );
}
