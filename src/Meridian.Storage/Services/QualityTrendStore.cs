using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Meridian.Storage.Services;

public interface IQualityTrendStore
{
    Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default);
    Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(string symbol, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken ct = default);
}

/// <summary>
/// Append-only JSONL trend store keyed by symbol/date/provider.
/// </summary>
public sealed class FileQualityTrendStore : IQualityTrendStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileQualityTrendStore(StorageOptions options)
    {
        var qualityDir = Path.Combine(options.RootPath, "quality");
        Directory.CreateDirectory(qualityDir);
        _filePath = Path.Combine(qualityDir, "trend-points.jsonl");
    }

    public async Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(point, JsonOptions);
        await _gate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_filePath, json + Environment.NewLine, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(string symbol, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<QualityTrendPoint>();

        var results = new List<QualityTrendPoint>();
        await _gate.WaitAsync(ct);
        try
        {
            var lines = await File.ReadAllLinesAsync(_filePath, ct);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var point = JsonSerializer.Deserialize<QualityTrendPoint>(line, JsonOptions);
                    if (point is null)
                        continue;

                    if (!string.Equals(point.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (point.ScoredAt < fromInclusive || point.ScoredAt > toInclusive)
                        continue;

                    results.Add(point);
                }
                catch (JsonException)
                {
                    // skip malformed historical entries to preserve append-only semantics
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return results.OrderBy(p => p.ScoredAt).ToArray();
    }
}

public sealed record QualityTrendPoint(
    string Symbol,
    DateOnly Date,
    string Provider,
    DateTimeOffset ScoredAt,
    double OverallScore,
    IReadOnlyDictionary<string, double> DimensionScores
);
