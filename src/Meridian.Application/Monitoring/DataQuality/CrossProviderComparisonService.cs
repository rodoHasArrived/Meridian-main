using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Compares data across multiple providers to identify discrepancies and recommend best sources.
/// </summary>
public sealed class CrossProviderComparisonService : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<CrossProviderComparisonService>();
    private readonly ConcurrentDictionary<string, ProviderDataTracker> _providerData = new();
    private readonly CrossProviderConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    /// <summary>
    /// Event raised when a significant discrepancy is detected.
    /// </summary>
    public event Action<ProviderDiscrepancy>? OnDiscrepancyDetected;

    public CrossProviderComparisonService(CrossProviderConfig? config = null)
    {
        _config = config ?? CrossProviderConfig.Default;
        _cleanupTimer = new Timer(CleanupOldData, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        _log.Information("CrossProviderComparisonService initialized with {PriceThreshold}% price threshold",
            _config.PriceDiscrepancyThresholdPercent);
    }

    /// <summary>
    /// Records a trade event from a provider for comparison.
    /// </summary>
    public void RecordTrade(
        string symbol,
        string provider,
        DateTimeOffset timestamp,
        decimal price,
        decimal volume,
        long? sequence = null)
    {
        if (_isDisposed)
            return;

        var key = GetKey(symbol, "Trade");
        var tracker = _providerData.GetOrAdd(key, _ => new ProviderDataTracker(symbol, "Trade"));
        tracker.RecordEvent(provider, timestamp, price, volume, sequence);

        // Check for cross-provider discrepancies
        CheckForDiscrepancies(tracker, timestamp);
    }

    /// <summary>
    /// Records a quote event from a provider for comparison.
    /// </summary>
    public void RecordQuote(
        string symbol,
        string provider,
        DateTimeOffset timestamp,
        decimal bidPrice,
        decimal askPrice,
        decimal bidSize,
        decimal askSize)
    {
        if (_isDisposed)
            return;

        var key = GetKey(symbol, "Quote");
        var tracker = _providerData.GetOrAdd(key, _ => new ProviderDataTracker(symbol, "Quote"));
        tracker.RecordQuote(provider, timestamp, bidPrice, askPrice, bidSize, askSize);

        CheckForQuoteDiscrepancies(tracker, timestamp);
    }

    /// <summary>
    /// Compares data from all providers for a symbol/date.
    /// </summary>
    public CrossProviderComparison Compare(string symbol, DateOnly date, string eventType = "Trade")
    {
        var key = GetKey(symbol, eventType);
        if (!_providerData.TryGetValue(key, out var tracker))
        {
            return CreateEmptyComparison(symbol, date, eventType);
        }

        var summaries = tracker.GetProviderSummaries(date);
        var discrepancies = tracker.GetDiscrepancies(date);

        // Determine recommended provider based on completeness and accuracy
        var recommended = summaries
            .OrderByDescending(s => s.CompletenessScore)
            .ThenByDescending(s => s.EventCount)
            .ThenBy(s => s.GapCount)
            .FirstOrDefault()?.Provider ?? "unknown";

        // Mark the recommended provider
        summaries = summaries.Select(s => s with { IsRecommended = s.Provider == recommended }).ToList();

        return new CrossProviderComparison(
            Symbol: symbol,
            Date: date,
            EventType: eventType,
            Providers: summaries,
            Discrepancies: discrepancies,
            RecommendedProvider: recommended,
            ComparedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets all providers tracking a symbol.
    /// </summary>
    public IReadOnlyList<string> GetProvidersForSymbol(string symbol, string eventType = "Trade")
    {
        var key = GetKey(symbol, eventType);
        if (!_providerData.TryGetValue(key, out var tracker))
        {
            return Array.Empty<string>();
        }
        return tracker.GetProviders();
    }

    /// <summary>
    /// Gets discrepancies for a date across all symbols.
    /// </summary>
    public IReadOnlyList<ProviderDiscrepancy> GetDiscrepanciesForDate(DateOnly date)
    {
        return _providerData.Values
            .SelectMany(t => t.GetDiscrepancies(date))
            .OrderByDescending(d => d.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets recent discrepancies.
    /// </summary>
    public IReadOnlyList<ProviderDiscrepancy> GetRecentDiscrepancies(int count = 100)
    {
        return _providerData.Values
            .SelectMany(t => t.GetRecentDiscrepancies(count))
            .OrderByDescending(d => d.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets provider comparison statistics.
    /// </summary>
    public ProviderComparisonStatistics GetStatistics()
    {
        var allDiscrepancies = _providerData.Values
            .SelectMany(t => t.GetRecentDiscrepancies(1000))
            .ToList();

        var providerCounts = _providerData.Values
            .SelectMany(t => t.GetProviders())
            .GroupBy(p => p)
            .ToDictionary(g => g.Key, g => g.Count());

        var discrepanciesBySeverity = allDiscrepancies
            .GroupBy(d => d.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        var discrepanciesByType = allDiscrepancies
            .GroupBy(d => d.DiscrepancyType)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ProviderComparisonStatistics(
            SymbolsTracked: _providerData.Count,
            ProvidersActive: providerCounts.Keys.Count,
            TotalDiscrepancies: allDiscrepancies.Count,
            DiscrepanciesBySeverity: discrepanciesBySeverity,
            DiscrepanciesByType: discrepanciesByType,
            MostDiscrepantProviderPairs: GetMostDiscrepantPairs(allDiscrepancies),
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    private void CheckForDiscrepancies(ProviderDataTracker tracker, DateTimeOffset timestamp)
    {
        var recentData = tracker.GetRecentDataByProvider(TimeSpan.FromSeconds(_config.ComparisonWindowSeconds));
        if (recentData.Count < 2)
            return;

        var providers = recentData.Keys.ToList();
        for (int i = 0; i < providers.Count - 1; i++)
        {
            for (int j = i + 1; j < providers.Count; j++)
            {
                var p1Data = recentData[providers[i]];
                var p2Data = recentData[providers[j]];

                if (p1Data.Count == 0 || p2Data.Count == 0)
                    continue;

                var latestP1 = p1Data[^1];
                var latestP2 = p2Data[^1];

                // Check price discrepancy
                if (latestP1.Price > 0 && latestP2.Price > 0)
                {
                    var priceDiff = Math.Abs(latestP1.Price - latestP2.Price);
                    var priceDiffPercent = priceDiff / Math.Max(latestP1.Price, latestP2.Price) * 100;

                    if ((double)priceDiffPercent > _config.PriceDiscrepancyThresholdPercent)
                    {
                        var discrepancy = new ProviderDiscrepancy(
                            Timestamp: timestamp,
                            DiscrepancyType: "PriceDifference",
                            Provider1: providers[i],
                            Provider2: providers[j],
                            Field: "Price",
                            Value1: latestP1.Price.ToString("F4"),
                            Value2: latestP2.Price.ToString("F4"),
                            Difference: (double)priceDiff,
                            Severity: ClassifyDiscrepancySeverity(priceDiffPercent)
                        );

                        tracker.RecordDiscrepancy(discrepancy);
                        RaiseDiscrepancyEvent(discrepancy);
                    }
                }
            }
        }
    }

    private void CheckForQuoteDiscrepancies(ProviderDataTracker tracker, DateTimeOffset timestamp)
    {
        var recentQuotes = tracker.GetRecentQuotesByProvider(TimeSpan.FromSeconds(_config.ComparisonWindowSeconds));
        if (recentQuotes.Count < 2)
            return;

        var providers = recentQuotes.Keys.ToList();
        for (int i = 0; i < providers.Count - 1; i++)
        {
            for (int j = i + 1; j < providers.Count; j++)
            {
                var q1List = recentQuotes[providers[i]];
                var q2List = recentQuotes[providers[j]];

                if (q1List.Count == 0 || q2List.Count == 0)
                    continue;

                var q1 = q1List[^1];
                var q2 = q2List[^1];

                // Check bid price discrepancy
                if (q1.BidPrice > 0 && q2.BidPrice > 0)
                {
                    var bidDiff = Math.Abs(q1.BidPrice - q2.BidPrice);
                    var bidDiffPercent = bidDiff / Math.Max(q1.BidPrice, q2.BidPrice) * 100;

                    if ((double)bidDiffPercent > _config.QuoteDiscrepancyThresholdPercent)
                    {
                        var discrepancy = new ProviderDiscrepancy(
                            Timestamp: timestamp,
                            DiscrepancyType: "BidPriceDifference",
                            Provider1: providers[i],
                            Provider2: providers[j],
                            Field: "BidPrice",
                            Value1: q1.BidPrice.ToString("F4"),
                            Value2: q2.BidPrice.ToString("F4"),
                            Difference: (double)bidDiff,
                            Severity: ClassifyDiscrepancySeverity(bidDiffPercent)
                        );

                        tracker.RecordDiscrepancy(discrepancy);
                        RaiseDiscrepancyEvent(discrepancy);
                    }
                }
            }
        }
    }

    private void RaiseDiscrepancyEvent(ProviderDiscrepancy discrepancy)
    {
        try
        {
            OnDiscrepancyDetected?.Invoke(discrepancy);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in discrepancy detected event handler");
        }
    }

    private static DiscrepancySeverity ClassifyDiscrepancySeverity(decimal differencePercent)
    {
        return differencePercent switch
        {
            < 0.5m => DiscrepancySeverity.Low,
            < 1.0m => DiscrepancySeverity.Medium,
            < 5.0m => DiscrepancySeverity.High,
            _ => DiscrepancySeverity.Critical
        };
    }

    private static IReadOnlyList<(string Pair, int Count)> GetMostDiscrepantPairs(List<ProviderDiscrepancy> discrepancies)
    {
        return discrepancies
            .GroupBy(d => $"{d.Provider1}/{d.Provider2}")
            .Select(g => (Pair: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();
    }

    private static CrossProviderComparison CreateEmptyComparison(string symbol, DateOnly date, string eventType)
    {
        return new CrossProviderComparison(
            Symbol: symbol,
            Date: date,
            EventType: eventType,
            Providers: Array.Empty<ProviderDataSummary>(),
            Discrepancies: Array.Empty<ProviderDiscrepancy>(),
            RecommendedProvider: "none",
            ComparedAt: DateTimeOffset.UtcNow
        );
    }

    private static string GetKey(string symbol, string eventType) => $"{symbol.ToUpperInvariant()}:{eventType}";

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays);
            foreach (var tracker in _providerData.Values)
            {
                tracker.Cleanup(cutoff);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during cross-provider comparison cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _providerData.Clear();
    }

    /// <summary>
    /// Tracks data from multiple providers for a single symbol/event type.
    /// </summary>
    private sealed class ProviderDataTracker
    {
        private readonly object _lock = new();
        private readonly string _symbol;
        private readonly string _eventType;
        private readonly Dictionary<string, List<ProviderDataPoint>> _dataByProvider = new();
        private readonly Dictionary<string, List<QuoteDataPoint>> _quotesByProvider = new();
        private readonly List<ProviderDiscrepancy> _discrepancies = new();

        public ProviderDataTracker(string symbol, string eventType)
        {
            _symbol = symbol;
            _eventType = eventType;
        }

        public void RecordEvent(string provider, DateTimeOffset timestamp, decimal price, decimal volume, long? sequence)
        {
            lock (_lock)
            {
                if (!_dataByProvider.TryGetValue(provider, out var list))
                {
                    list = new List<ProviderDataPoint>();
                    _dataByProvider[provider] = list;
                }

                list.Add(new ProviderDataPoint(timestamp, price, volume, sequence));

                // Keep only recent data
                while (list.Count > 10000)
                {
                    list.RemoveAt(0);
                }
            }
        }

        public void RecordQuote(string provider, DateTimeOffset timestamp, decimal bidPrice, decimal askPrice, decimal bidSize, decimal askSize)
        {
            lock (_lock)
            {
                if (!_quotesByProvider.TryGetValue(provider, out var list))
                {
                    list = new List<QuoteDataPoint>();
                    _quotesByProvider[provider] = list;
                }

                list.Add(new QuoteDataPoint(timestamp, bidPrice, askPrice, bidSize, askSize));

                while (list.Count > 10000)
                {
                    list.RemoveAt(0);
                }
            }
        }

        public void RecordDiscrepancy(ProviderDiscrepancy discrepancy)
        {
            lock (_lock)
            {
                _discrepancies.Add(discrepancy);
                while (_discrepancies.Count > 1000)
                {
                    _discrepancies.RemoveAt(0);
                }
            }
        }

        public IReadOnlyList<string> GetProviders()
        {
            lock (_lock)
            {
                return _dataByProvider.Keys.Union(_quotesByProvider.Keys).ToList();
            }
        }

        public Dictionary<string, List<ProviderDataPoint>> GetRecentDataByProvider(TimeSpan window)
        {
            var cutoff = DateTimeOffset.UtcNow - window;
            lock (_lock)
            {
                return _dataByProvider.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Where(p => p.Timestamp >= cutoff).ToList()
                );
            }
        }

        public Dictionary<string, List<QuoteDataPoint>> GetRecentQuotesByProvider(TimeSpan window)
        {
            var cutoff = DateTimeOffset.UtcNow - window;
            lock (_lock)
            {
                return _quotesByProvider.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Where(p => p.Timestamp >= cutoff).ToList()
                );
            }
        }

        public IReadOnlyList<ProviderDataSummary> GetProviderSummaries(DateOnly date)
        {
            lock (_lock)
            {
                var summaries = new List<ProviderDataSummary>();

                foreach (var (provider, data) in _dataByProvider)
                {
                    var dateData = data.Where(d => DateOnly.FromDateTime(d.Timestamp.UtcDateTime) == date).ToList();
                    if (dateData.Count == 0)
                        continue;

                    var firstEvent = dateData.Min(d => d.Timestamp);
                    var lastEvent = dateData.Max(d => d.Timestamp);
                    var coverage = lastEvent - firstEvent;

                    // Calculate gaps
                    var gapCount = 0;
                    for (int i = 1; i < dateData.Count; i++)
                    {
                        if ((dateData[i].Timestamp - dateData[i - 1].Timestamp).TotalSeconds > 60)
                        {
                            gapCount++;
                        }
                    }

                    var completeness = Math.Max(0, 1.0 - (gapCount * 0.05)); // 5% penalty per gap

                    summaries.Add(new ProviderDataSummary(
                        Provider: provider,
                        EventCount: dateData.Count,
                        FirstEvent: firstEvent,
                        LastEvent: lastEvent,
                        Coverage: coverage,
                        GapCount: gapCount,
                        CompletenessScore: Math.Round(completeness, 4),
                        Latency: 0, // Would need latency tracking
                        IsRecommended: false
                    ));
                }

                return summaries;
            }
        }

        public IReadOnlyList<ProviderDiscrepancy> GetDiscrepancies(DateOnly date)
        {
            lock (_lock)
            {
                return _discrepancies
                    .Where(d => DateOnly.FromDateTime(d.Timestamp.UtcDateTime) == date)
                    .ToList();
            }
        }

        public IReadOnlyList<ProviderDiscrepancy> GetRecentDiscrepancies(int count)
        {
            lock (_lock)
            {
                return _discrepancies
                    .OrderByDescending(d => d.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }

        public void Cleanup(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                foreach (var list in _dataByProvider.Values)
                {
                    list.RemoveAll(d => d.Timestamp < cutoff);
                }

                foreach (var list in _quotesByProvider.Values)
                {
                    list.RemoveAll(d => d.Timestamp < cutoff);
                }

                _discrepancies.RemoveAll(d => d.Timestamp < cutoff);
            }
        }
    }

    private readonly record struct ProviderDataPoint(
        DateTimeOffset Timestamp,
        decimal Price,
        decimal Volume,
        long? Sequence
    );

    private readonly record struct QuoteDataPoint(
        DateTimeOffset Timestamp,
        decimal BidPrice,
        decimal AskPrice,
        decimal BidSize,
        decimal AskSize
    );
}

/// <summary>
/// Configuration for cross-provider comparison.
/// </summary>
public sealed record CrossProviderConfig
{
    public double PriceDiscrepancyThresholdPercent { get; init; } = 0.5;
    public double QuoteDiscrepancyThresholdPercent { get; init; } = 0.5;
    public double VolumeDiscrepancyThresholdPercent { get; init; } = 10.0;
    public int ComparisonWindowSeconds { get; init; } = 5;
    public int RetentionDays { get; init; } = 7;

    public static CrossProviderConfig Default => new();
}

/// <summary>
/// Provider comparison statistics.
/// </summary>
public sealed record ProviderComparisonStatistics(
    int SymbolsTracked,
    int ProvidersActive,
    int TotalDiscrepancies,
    Dictionary<DiscrepancySeverity, int> DiscrepanciesBySeverity,
    Dictionary<string, int> DiscrepanciesByType,
    IReadOnlyList<(string Pair, int Count)> MostDiscrepantProviderPairs,
    DateTimeOffset CalculatedAt
);
