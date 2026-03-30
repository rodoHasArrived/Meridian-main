using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Daily quality record with symbol, date, and completeness score.
/// </summary>
public sealed record DailyQualityRecord(string Symbol, DateOnly Date, double CompletenessScore);

/// <summary>
/// Interface for persisting and retrieving daily quality records.
/// </summary>
public interface IQualityArchiveStore : IAsyncDisposable
{
    /// <summary>
    /// Records a daily quality score for a symbol.
    /// </summary>
    Task RecordAsync(string symbol, DateOnly date, double completenessScore, CancellationToken ct);

    /// <summary>
    /// Retrieves historical quality records for a symbol within a date range.
    /// </summary>
    Task<IReadOnlyList<DailyQualityRecord>> GetHistoryAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// Gets all tracked symbols.
    /// </summary>
    Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken ct);
}

/// <summary>
/// JSON file-backed archive store using %LOCALAPPDATA%\Meridian\quality-archive.json.
/// Stores data as Dictionary&lt;symbol -> Dictionary&lt;date string -> score&gt;&gt;.
/// Thread-safe using concurrent dictionary wrapper.
/// </summary>
public sealed class QualityArchiveStore : IQualityArchiveStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<QualityArchiveStore> _logger;
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, Dictionary<string, double>> _cache = new();
    private volatile bool _isDisposed;

    public QualityArchiveStore(ILogger<QualityArchiveStore> logger)
    {
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var meridianDir = Path.Combine(localAppData, "Meridian");
        Directory.CreateDirectory(meridianDir);
        _filePath = Path.Combine(meridianDir, "quality-archive.json");

        LoadFromDisk();
        _logger.LogInformation("QualityArchiveStore initialized with file: {FilePath}", _filePath);
    }

    /// <summary>
    /// Records a daily quality score, updating in-memory cache and persisting to disk.
    /// </summary>
    public Task RecordAsync(string symbol, DateOnly date, double completenessScore, CancellationToken ct)
    {
        ThrowIfDisposed();

        var normalizedSymbol = NormalizeSymbol(symbol);
        var dateKey = date.ToString("yyyy-MM-dd");

        _cache.AddOrUpdate(
            normalizedSymbol,
            _ => new Dictionary<string, double> { { dateKey, completenessScore } },
            (_, dict) =>
            {
                dict[dateKey] = completenessScore;
                return dict;
            }
        );

        return PersistToDiskAsync(ct);
    }

    /// <summary>
    /// Retrieves historical records for a symbol within a date range.
    /// </summary>
    public Task<IReadOnlyList<DailyQualityRecord>> GetHistoryAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ThrowIfDisposed();

        var normalizedSymbol = NormalizeSymbol(symbol);
        var result = new List<DailyQualityRecord>();

        if (_cache.TryGetValue(normalizedSymbol, out var dateScores))
        {
            foreach (var kvp in dateScores)
            {
                if (DateOnly.TryParse(kvp.Key, out var date) && date >= from && date <= to)
                {
                    result.Add(new DailyQualityRecord(normalizedSymbol, date, kvp.Value));
                }
            }
        }

        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return Task.FromResult<IReadOnlyList<DailyQualityRecord>>(result.AsReadOnly());
    }

    /// <summary>
    /// Gets all tracked symbols.
    /// </summary>
    public Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        var symbols = _cache.Keys.OrderBy(s => s).ToList();
        return Task.FromResult<IReadOnlyList<string>>(symbols.AsReadOnly());
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("Quality archive file does not exist yet: {FilePath}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogDebug("Quality archive file is empty");
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(json, s_jsonOptions);
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    _cache[kvp.Key] = kvp.Value;
                }
                _logger.LogInformation("Loaded {Count} symbols from quality archive", data.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading quality archive from disk");
        }
    }

    private async Task PersistToDiskAsync(CancellationToken ct)
    {
        try
        {
            var data = new Dictionary<string, Dictionary<string, double>>();
            foreach (var kvp in _cache)
            {
                data[kvp.Key] = kvp.Value;
            }

            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting quality archive to disk");
        }
    }

    private static string NormalizeSymbol(string symbol) => symbol.ToUpperInvariant();

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(QualityArchiveStore));
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await PersistToDiskAsync(CancellationToken.None).ConfigureAwait(false);
        _cache.Clear();
    }
}
