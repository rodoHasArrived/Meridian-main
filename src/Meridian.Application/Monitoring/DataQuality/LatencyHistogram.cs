using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Tracks latency distributions and provides histogram data for visualization.
/// Supports per-symbol and per-provider tracking with percentile calculations.
/// </summary>
public sealed class LatencyHistogram : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<LatencyHistogram>();
    private readonly ConcurrentDictionary<string, LatencyTracker> _trackers = new();
    private readonly LatencyHistogramConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    public LatencyHistogram(LatencyHistogramConfig? config = null)
    {
        _config = config ?? LatencyHistogramConfig.Default;
        _cleanupTimer = new Timer(CleanupOldData, null,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

        _log.Information("LatencyHistogram initialized with {BucketCount} buckets",
            _config.BucketBoundaries.Length);
    }

    /// <summary>
    /// Records a latency measurement in milliseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string symbol, double latencyMs, string? provider = null)
    {
        if (_isDisposed)
            return;
        if (latencyMs < 0)
            return;

        var key = GetKey(symbol, provider);
        var tracker = _trackers.GetOrAdd(key, _ => new LatencyTracker(_config));
        tracker.Record(latencyMs);
    }

    /// <summary>
    /// Records latency from timestamps.
    /// </summary>
    public void RecordLatency(string symbol, DateTimeOffset eventTimestamp, DateTimeOffset receiveTimestamp, string? provider = null)
    {
        var latencyMs = (receiveTimestamp - eventTimestamp).TotalMilliseconds;
        RecordLatency(symbol, latencyMs, provider);
    }

    /// <summary>
    /// Gets the latency distribution for a symbol.
    /// </summary>
    public LatencyDistribution? GetDistribution(string symbol, string? provider = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var key = GetKey(symbol, provider);
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            return null;
        }

        return tracker.GetDistribution(symbol, provider, from, to);
    }

    /// <summary>
    /// Gets latency distributions for all tracked symbols.
    /// </summary>
    public IReadOnlyList<LatencyDistribution> GetAllDistributions()
    {
        return _trackers
            .Select(kvp =>
            {
                var parts = kvp.Key.Split(':');
                var symbol = parts[0];
                var provider = parts.Length > 1 ? parts[1] : null;
                return kvp.Value.GetDistribution(symbol, provider, null, null);
            })
            .Where(d => d != null)
            .Cast<LatencyDistribution>()
            .ToList();
    }

    /// <summary>
    /// Gets the histogram buckets for visualization.
    /// </summary>
    public IReadOnlyList<HistogramBucket> GetBuckets(string symbol, string? provider = null)
    {
        var key = GetKey(symbol, provider);
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            return Array.Empty<HistogramBucket>();
        }

        return tracker.GetBuckets();
    }

    /// <summary>
    /// Gets percentile latency value.
    /// </summary>
    public double GetPercentile(string symbol, double percentile, string? provider = null)
    {
        var key = GetKey(symbol, provider);
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            return 0;
        }

        return tracker.GetPercentile(percentile);
    }

    /// <summary>
    /// Gets overall latency statistics.
    /// </summary>
    public LatencyStatistics GetStatistics()
    {
        var allDistributions = GetAllDistributions();
        if (allDistributions.Count == 0)
        {
            return new LatencyStatistics(
                SymbolsTracked: 0,
                TotalSamples: 0,
                GlobalMeanMs: 0,
                GlobalP50Ms: 0,
                GlobalP90Ms: 0,
                GlobalP99Ms: 0,
                FastestSymbol: null,
                SlowestSymbol: null,
                DistributionsBySymbol: new Dictionary<string, double>(),
                CalculatedAt: DateTimeOffset.UtcNow
            );
        }

        var distributions = new Dictionary<string, double>();
        foreach (var dist in allDistributions)
        {
            distributions[$"{dist.Symbol}:{dist.Provider ?? "all"}"] = dist.MeanLatencyMs;
        }

        var ordered = allDistributions.OrderBy(d => d.MeanLatencyMs).ToList();

        return new LatencyStatistics(
            SymbolsTracked: allDistributions.Count,
            TotalSamples: allDistributions.Sum(d => d.SampleCount),
            GlobalMeanMs: allDistributions.Average(d => d.MeanLatencyMs),
            GlobalP50Ms: allDistributions.Average(d => d.P50LatencyMs),
            GlobalP90Ms: allDistributions.Average(d => d.P90LatencyMs),
            GlobalP99Ms: allDistributions.Average(d => d.P99LatencyMs),
            FastestSymbol: ordered.FirstOrDefault()?.Symbol,
            SlowestSymbol: ordered.LastOrDefault()?.Symbol,
            DistributionsBySymbol: distributions,
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets symbols with latency above threshold.
    /// </summary>
    public IReadOnlyList<(string Symbol, double P99Ms)> GetHighLatencySymbols(double thresholdMs)
    {
        return _trackers
            .Select(kvp =>
            {
                var parts = kvp.Key.Split(':');
                return (Symbol: parts[0], P99Ms: kvp.Value.GetPercentile(99));
            })
            .Where(x => x.P99Ms > thresholdMs)
            .OrderByDescending(x => x.P99Ms)
            .ToList();
    }

    /// <summary>
    /// Resets tracking for a symbol.
    /// </summary>
    public void Reset(string symbol, string? provider = null)
    {
        var key = GetKey(symbol, provider);
        _trackers.TryRemove(key, out _);
    }

    /// <summary>
    /// Resets all tracking.
    /// </summary>
    public void ResetAll()
    {
        _trackers.Clear();
        _log.Information("LatencyHistogram reset all tracking data");
    }

    private static string GetKey(string symbol, string? provider) =>
        provider != null ? $"{symbol.ToUpperInvariant()}:{provider}" : symbol.ToUpperInvariant();

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays);
            foreach (var tracker in _trackers.Values)
            {
                tracker.Cleanup(cutoff);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during latency histogram cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _trackers.Clear();
    }

    /// <summary>
    /// Tracks latency measurements with histogram bucketing.
    /// </summary>
    private sealed class LatencyTracker
    {
        private readonly object _lock = new();
        private readonly LatencyHistogramConfig _config;
        private readonly long[] _bucketCounts;
        private readonly List<LatencySample> _samples = new();
        private double _sum;
        private double _sumSquares;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private long _count;

        public LatencyTracker(LatencyHistogramConfig config)
        {
            _config = config;
            _bucketCounts = new long[config.BucketBoundaries.Length + 1];
        }

        public void Record(double latencyMs)
        {
            lock (_lock)
            {
                _count++;
                _sum += latencyMs;
                _sumSquares += latencyMs * latencyMs;
                _min = Math.Min(_min, latencyMs);
                _max = Math.Max(_max, latencyMs);

                // Find bucket
                var bucketIndex = 0;
                for (int i = 0; i < _config.BucketBoundaries.Length; i++)
                {
                    if (latencyMs <= _config.BucketBoundaries[i])
                    {
                        break;
                    }
                    bucketIndex++;
                }
                _bucketCounts[bucketIndex]++;

                // Store sample for percentile calculations
                _samples.Add(new LatencySample(DateTimeOffset.UtcNow, latencyMs));

                // Limit samples
                while (_samples.Count > _config.MaxSamples)
                {
                    _samples.RemoveAt(0);
                }
            }
        }

        public LatencyDistribution GetDistribution(string symbol, string? provider, DateTimeOffset? from, DateTimeOffset? to)
        {
            lock (_lock)
            {
                var samples = _samples.AsEnumerable();
                if (from.HasValue)
                    samples = samples.Where(s => s.Timestamp >= from.Value);
                if (to.HasValue)
                    samples = samples.Where(s => s.Timestamp <= to.Value);

                var sampleList = samples.ToList();
                if (sampleList.Count == 0)
                {
                    return new LatencyDistribution(
                        Symbol: symbol,
                        Provider: provider,
                        From: from ?? DateTimeOffset.MinValue,
                        To: to ?? DateTimeOffset.UtcNow,
                        SampleCount: 0,
                        MinLatencyMs: 0,
                        MaxLatencyMs: 0,
                        MeanLatencyMs: 0,
                        MedianLatencyMs: 0,
                        P50LatencyMs: 0,
                        P90LatencyMs: 0,
                        P95LatencyMs: 0,
                        P99LatencyMs: 0,
                        StandardDeviation: 0,
                        Buckets: GetBuckets(),
                        CalculatedAt: DateTimeOffset.UtcNow
                    );
                }

                var sorted = sampleList.OrderBy(s => s.LatencyMs).ToList();
                var mean = _count > 0 ? _sum / _count : 0;
                var variance = _count > 1 ? (_sumSquares - (_sum * _sum / _count)) / (_count - 1) : 0;
                var stdDev = Math.Sqrt(Math.Max(0, variance));

                return new LatencyDistribution(
                    Symbol: symbol,
                    Provider: provider,
                    From: from ?? sampleList.First().Timestamp,
                    To: to ?? DateTimeOffset.UtcNow,
                    SampleCount: sampleList.Count,
                    MinLatencyMs: Math.Round(_min, 3),
                    MaxLatencyMs: Math.Round(_max, 3),
                    MeanLatencyMs: Math.Round(mean, 3),
                    MedianLatencyMs: Math.Round(GetPercentileFromSorted(sorted, 50), 3),
                    P50LatencyMs: Math.Round(GetPercentileFromSorted(sorted, 50), 3),
                    P90LatencyMs: Math.Round(GetPercentileFromSorted(sorted, 90), 3),
                    P95LatencyMs: Math.Round(GetPercentileFromSorted(sorted, 95), 3),
                    P99LatencyMs: Math.Round(GetPercentileFromSorted(sorted, 99), 3),
                    StandardDeviation: Math.Round(stdDev, 3),
                    Buckets: GetBuckets(),
                    CalculatedAt: DateTimeOffset.UtcNow
                );
            }
        }

        public IReadOnlyList<HistogramBucket> GetBuckets()
        {
            lock (_lock)
            {
                var buckets = new List<HistogramBucket>();
                var total = Math.Max(1, _count);
                var boundaries = _config.BucketBoundaries;

                for (int i = 0; i <= boundaries.Length; i++)
                {
                    var lowerBound = i == 0 ? 0 : boundaries[i - 1];
                    var upperBound = i == boundaries.Length ? double.PositiveInfinity : boundaries[i];
                    var count = _bucketCounts[i];
                    var percentage = Math.Round((double)count / total * 100, 2);

                    buckets.Add(new HistogramBucket(lowerBound, upperBound, count, percentage));
                }

                return buckets;
            }
        }

        public double GetPercentile(double percentile)
        {
            lock (_lock)
            {
                if (_samples.Count == 0)
                    return 0;
                var sorted = _samples.OrderBy(s => s.LatencyMs).ToList();
                return GetPercentileFromSorted(sorted, percentile);
            }
        }

        public void Cleanup(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                _samples.RemoveAll(s => s.Timestamp < cutoff);
            }
        }

        private static double GetPercentileFromSorted(List<LatencySample> sorted, double percentile)
        {
            if (sorted.Count == 0)
                return 0;
            if (sorted.Count == 1)
                return sorted[0].LatencyMs;

            var index = (percentile / 100.0) * (sorted.Count - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            var fraction = index - lower;

            if (upper >= sorted.Count)
                upper = sorted.Count - 1;

            return sorted[lower].LatencyMs * (1 - fraction) + sorted[upper].LatencyMs * fraction;
        }
    }

    private readonly record struct LatencySample(DateTimeOffset Timestamp, double LatencyMs);
}

/// <summary>
/// Configuration for latency histogram.
/// </summary>
public sealed record LatencyHistogramConfig
{
    /// <summary>
    /// Bucket boundaries in milliseconds.
    /// </summary>
    public double[] BucketBoundaries { get; init; } = new double[]
    {
        0.1, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000
    };

    /// <summary>
    /// Maximum samples to retain for percentile calculations.
    /// </summary>
    public int MaxSamples { get; init; } = 100000;

    /// <summary>
    /// Days to retain data.
    /// </summary>
    public int RetentionDays { get; init; } = 7;

    public static LatencyHistogramConfig Default => new();
}

/// <summary>
/// Overall latency statistics.
/// </summary>
public sealed record LatencyStatistics(
    int SymbolsTracked,
    long TotalSamples,
    double GlobalMeanMs,
    double GlobalP50Ms,
    double GlobalP90Ms,
    double GlobalP99Ms,
    string? FastestSymbol,
    string? SlowestSymbol,
    Dictionary<string, double> DistributionsBySymbol,
    DateTimeOffset CalculatedAt
);
