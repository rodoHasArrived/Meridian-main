using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Tracks and provides latency histograms per data provider.
/// Implements PROV-11: Provider Latency Histogram.
/// </summary>
public sealed class ProviderLatencyService : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ProviderLatencyService>();
    private readonly ConcurrentDictionary<string, ProviderLatencyTracker> _providers = new();
    private readonly ProviderLatencyConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    public ProviderLatencyService(ProviderLatencyConfig? config = null)
    {
        _config = config ?? ProviderLatencyConfig.Default;
        _cleanupTimer = new Timer(
            CleanupOldData,
            null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));

        _log.Information("ProviderLatencyService initialized");
    }

    /// <summary>
    /// Records a latency measurement for a provider.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string provider, double latencyMs, string? symbol = null)
    {
        if (_isDisposed || string.IsNullOrEmpty(provider))
            return;
        if (latencyMs < 0)
            return;

        // Normalize provider name to lowercase for consistent API output
        var normalizedProvider = provider.ToLowerInvariant();
        var tracker = _providers.GetOrAdd(normalizedProvider, p => new ProviderLatencyTracker(p, _config));
        tracker.Record(latencyMs, symbol);
    }

    /// <summary>
    /// Records latency from timestamps.
    /// </summary>
    public void RecordLatency(string provider, DateTimeOffset eventTime, DateTimeOffset receiveTime, string? symbol = null)
    {
        var latencyMs = (receiveTime - eventTime).TotalMilliseconds;
        RecordLatency(provider, latencyMs, symbol);
    }

    /// <summary>
    /// Gets the latency histogram for a specific provider.
    /// </summary>
    public ProviderLatencyHistogram? GetHistogram(string provider)
    {
        var normalizedProvider = provider.ToLowerInvariant();
        return _providers.TryGetValue(normalizedProvider, out var tracker) ? tracker.GetHistogram() : null;
    }

    /// <summary>
    /// Gets latency histograms for all providers.
    /// </summary>
    public IReadOnlyList<ProviderLatencyHistogram> GetAllHistograms()
    {
        return _providers.Values
            .Select(t => t.GetHistogram())
            .OrderBy(h => h.Provider)
            .ToList();
    }

    /// <summary>
    /// Gets a summary comparison of all providers.
    /// </summary>
    public ProviderLatencySummary GetSummary()
    {
        var histograms = GetAllHistograms();
        if (histograms.Count == 0)
        {
            return new ProviderLatencySummary(
                Providers: Array.Empty<ProviderLatencyStats>(),
                FastestProvider: null,
                SlowestProvider: null,
                GlobalP50Ms: 0,
                GlobalP95Ms: 0,
                GlobalP99Ms: 0,
                TotalSamples: 0,
                CalculatedAt: DateTimeOffset.UtcNow);
        }

        var stats = histograms.Select(h => new ProviderLatencyStats(
            Provider: h.Provider,
            SampleCount: h.SampleCount,
            MeanMs: h.MeanMs,
            P50Ms: h.P50Ms,
            P95Ms: h.P95Ms,
            P99Ms: h.P99Ms,
            MinMs: h.MinMs,
            MaxMs: h.MaxMs,
            LastUpdateTime: h.LastUpdateTime
        )).ToList();

        var ordered = stats.OrderBy(s => s.P50Ms).ToList();

        return new ProviderLatencySummary(
            Providers: stats,
            FastestProvider: ordered.FirstOrDefault()?.Provider,
            SlowestProvider: ordered.LastOrDefault()?.Provider,
            GlobalP50Ms: Math.Round(stats.Average(s => s.P50Ms), 3),
            GlobalP95Ms: Math.Round(stats.Average(s => s.P95Ms), 3),
            GlobalP99Ms: Math.Round(stats.Average(s => s.P99Ms), 3),
            TotalSamples: stats.Sum(s => s.SampleCount),
            CalculatedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Gets providers with latency above threshold.
    /// </summary>
    public IReadOnlyList<ProviderLatencyStats> GetHighLatencyProviders(double thresholdMs)
    {
        return GetAllHistograms()
            .Where(h => h.P95Ms > thresholdMs)
            .Select(h => new ProviderLatencyStats(
                Provider: h.Provider,
                SampleCount: h.SampleCount,
                MeanMs: h.MeanMs,
                P50Ms: h.P50Ms,
                P95Ms: h.P95Ms,
                P99Ms: h.P99Ms,
                MinMs: h.MinMs,
                MaxMs: h.MaxMs,
                LastUpdateTime: h.LastUpdateTime
            ))
            .OrderByDescending(s => s.P95Ms)
            .ToList();
    }

    /// <summary>
    /// Resets tracking for a provider.
    /// </summary>
    public void Reset(string provider)
    {
        var normalizedProvider = provider.ToLowerInvariant();
        _providers.TryRemove(normalizedProvider, out _);
    }

    /// <summary>
    /// Resets all tracking.
    /// </summary>
    public void ResetAll()
    {
        _providers.Clear();
    }

    /// <summary>
    /// Serializes the summary to JSON.
    /// </summary>
    public string ToJson()
    {
        var summary = GetSummary();
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var toRemove = new List<string>();

            foreach (var kvp in _providers)
            {
                kvp.Value.Cleanup(_config.RetentionHours);

                // Evict providers whose sample window is now empty (no activity
                // within the retention period) to prevent unbounded dictionary growth.
                if (kvp.Value.IsEmpty)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _providers.TryRemove(key, out _);
            }

            if (toRemove.Count > 0)
            {
                _log.Debug("Evicted {Count} inactive provider(s) from latency tracking", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during provider latency cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _providers.Clear();
    }

    /// <summary>
    /// Tracks latency for a single provider.
    /// </summary>
    private sealed class ProviderLatencyTracker
    {
        private readonly string _provider;
        private readonly ProviderLatencyConfig _config;
        private readonly object _lock = new();
        private readonly long[] _bucketCounts;
        private readonly List<LatencySample> _samples = new();
        private readonly ConcurrentDictionary<string, int> _symbolCounts = new();

        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private long _count;
        private DateTimeOffset _lastUpdateTime = DateTimeOffset.UtcNow;

        public ProviderLatencyTracker(string provider, ProviderLatencyConfig config)
        {
            _provider = provider;
            _config = config;
            _bucketCounts = new long[config.BucketBoundaries.Length + 1];
        }

        public void Record(double latencyMs, string? symbol)
        {
            lock (_lock)
            {
                _count++;
                _sum += latencyMs;
                _min = Math.Min(_min, latencyMs);
                _max = Math.Max(_max, latencyMs);
                _lastUpdateTime = DateTimeOffset.UtcNow;

                // Find bucket
                var bucketIndex = 0;
                for (int i = 0; i < _config.BucketBoundaries.Length; i++)
                {
                    if (latencyMs <= _config.BucketBoundaries[i])
                        break;
                    bucketIndex++;
                }
                _bucketCounts[bucketIndex]++;

                // Store sample for percentile calculations
                _samples.Add(new LatencySample(DateTimeOffset.UtcNow, latencyMs));

                // Limit samples
                while (_samples.Count > _config.MaxSamplesPerProvider)
                {
                    _samples.RemoveAt(0);
                }

                // Track symbol count
                if (!string.IsNullOrEmpty(symbol))
                {
                    _symbolCounts.AddOrUpdate(symbol.ToUpperInvariant(), 1, (_, c) => c + 1);
                }
            }
        }

        public ProviderLatencyHistogram GetHistogram()
        {
            lock (_lock)
            {
                var buckets = new List<LatencyBucket>();
                var boundaries = _config.BucketBoundaries;

                for (int i = 0; i <= boundaries.Length; i++)
                {
                    var lowerBound = i == 0 ? 0 : boundaries[i - 1];
                    var upperBound = i == boundaries.Length ? double.PositiveInfinity : boundaries[i];
                    var count = _bucketCounts[i];
                    var percentage = _count > 0 ? Math.Round((double)count / _count * 100, 2) : 0;

                    buckets.Add(new LatencyBucket(
                        LowerBoundMs: lowerBound,
                        UpperBoundMs: upperBound,
                        Count: count,
                        Percentage: percentage));
                }

                var sorted = _samples.OrderBy(s => s.LatencyMs).ToList();
                var mean = _count > 0 ? _sum / _count : 0;

                return new ProviderLatencyHistogram(
                    Provider: _provider,
                    SampleCount: _count,
                    MeanMs: Math.Round(mean, 3),
                    MinMs: Math.Round(_min == double.MaxValue ? 0 : _min, 3),
                    MaxMs: Math.Round(_max == double.MinValue ? 0 : _max, 3),
                    P50Ms: Math.Round(GetPercentile(sorted, 50), 3),
                    P75Ms: Math.Round(GetPercentile(sorted, 75), 3),
                    P90Ms: Math.Round(GetPercentile(sorted, 90), 3),
                    P95Ms: Math.Round(GetPercentile(sorted, 95), 3),
                    P99Ms: Math.Round(GetPercentile(sorted, 99), 3),
                    Buckets: buckets,
                    SymbolCount: _symbolCounts.Count,
                    LastUpdateTime: _lastUpdateTime);
            }
        }

        public void Cleanup(int retentionHours)
        {
            lock (_lock)
            {
                var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours);
                _samples.RemoveAll(s => s.Timestamp < cutoff);
            }
        }

        /// <summary>Returns true when the sample window is empty after a cleanup pass.</summary>
        public bool IsEmpty
        {
            get { lock (_lock) { return _samples.Count == 0; } }
        }

        private static double GetPercentile(List<LatencySample> sorted, double percentile)
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

        private readonly record struct LatencySample(DateTimeOffset Timestamp, double LatencyMs);
    }
}

/// <summary>
/// Configuration for provider latency tracking.
/// </summary>
public sealed record ProviderLatencyConfig
{
    /// <summary>
    /// Bucket boundaries in milliseconds for histogram.
    /// </summary>
    public double[] BucketBoundaries { get; init; } = new double[]
    {
        1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000
    };

    /// <summary>
    /// Maximum samples to retain per provider.
    /// </summary>
    public int MaxSamplesPerProvider { get; init; } = 50000;

    /// <summary>
    /// Hours to retain data.
    /// </summary>
    public int RetentionHours { get; init; } = 24;

    public static ProviderLatencyConfig Default => new();
}

/// <summary>
/// Latency histogram for a single provider.
/// </summary>
public sealed record ProviderLatencyHistogram(
    string Provider,
    long SampleCount,
    double MeanMs,
    double MinMs,
    double MaxMs,
    double P50Ms,
    double P75Ms,
    double P90Ms,
    double P95Ms,
    double P99Ms,
    IReadOnlyList<LatencyBucket> Buckets,
    int SymbolCount,
    DateTimeOffset LastUpdateTime
);

/// <summary>
/// A single bucket in the latency histogram.
/// </summary>
public sealed record LatencyBucket(
    double LowerBoundMs,
    double UpperBoundMs,
    long Count,
    double Percentage
);

/// <summary>
/// Summary statistics for a provider.
/// </summary>
public sealed record ProviderLatencyStats(
    string Provider,
    long SampleCount,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MinMs,
    double MaxMs,
    DateTimeOffset LastUpdateTime
);

/// <summary>
/// Summary comparison of all providers.
/// </summary>
public sealed record ProviderLatencySummary(
    IReadOnlyList<ProviderLatencyStats> Providers,
    string? FastestProvider,
    string? SlowestProvider,
    double GlobalP50Ms,
    double GlobalP95Ms,
    double GlobalP99Ms,
    long TotalSamples,
    DateTimeOffset CalculatedAt
);
