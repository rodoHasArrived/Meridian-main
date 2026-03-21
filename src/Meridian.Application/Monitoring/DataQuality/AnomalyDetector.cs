using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Detects anomalies in market data including price spikes, volume outliers,
/// stale data, and other data quality issues. Uses statistical methods for detection.
/// </summary>
public sealed class AnomalyDetector : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<AnomalyDetector>();
    private readonly ConcurrentDictionary<string, SymbolStatistics> _symbolStats = new();
    private readonly ConcurrentDictionary<string, List<DataAnomaly>> _anomalies = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private readonly ConcurrentDictionary<string, int> _symbolStaleThresholds = new();
    private readonly AnomalyDetectionConfig _config;
    private readonly Timer _cleanupTimer;
    private readonly Timer _staleCheckTimer;
    private volatile bool _isDisposed;
    private long _totalAnomaliesDetected;
    private long _anomalyIdCounter;

    /// <summary>
    /// Event raised when an anomaly is detected.
    /// </summary>
    public event Action<DataAnomaly>? OnAnomalyDetected;

    public AnomalyDetector(AnomalyDetectionConfig? config = null)
    {
        _config = config ?? AnomalyDetectionConfig.Default;
        _cleanupTimer = new Timer(CleanupOldData, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        _staleCheckTimer = new Timer(CheckForStaleData, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        _log.Information("AnomalyDetector initialized with price spike threshold: {PriceThreshold}%, " +
            "volume spike multiplier: {VolumeSpikeMultiplier}x, volume drop threshold: {VolumeDropMultiplier}x",
            _config.PriceSpikeThresholdPercent, _config.VolumeSpikeThresholdMultiplier, _config.VolumeDropThresholdMultiplier);
    }

    /// <summary>
    /// Registers a liquidity profile for a symbol, adjusting the stale data threshold
    /// so that illiquid instruments do not generate false stale-data anomalies.
    /// </summary>
    public void RegisterSymbolLiquidity(string symbol, LiquidityProfile profile)
    {
        var thresholds = LiquidityProfileProvider.GetThresholds(profile);
        _symbolStaleThresholds[symbol.ToUpperInvariant()] = thresholds.StaleDataThresholdSeconds;
        _log.Debug("Registered liquidity profile {Profile} for {Symbol} (stale threshold: {Threshold}s)",
            profile, symbol, thresholds.StaleDataThresholdSeconds);
    }

    /// <summary>
    /// Processes a trade event for anomaly detection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DataAnomaly? ProcessTrade(
        string symbol,
        DateTimeOffset timestamp,
        decimal price,
        decimal volume,
        string? provider = null)
    {
        if (_isDisposed)
            return null;
        if (price <= 0)
            return null;

        var stats = _symbolStats.GetOrAdd(symbol.ToUpperInvariant(),
            _ => new SymbolStatistics(symbol, _config.MinSamplesForStatistics));

        var anomaly = stats.RecordTrade(timestamp, price, volume, _config);
        if (anomaly != null)
        {
            anomaly = anomaly with { Provider = provider, Id = GenerateAnomalyId() };
            RecordAnomaly(anomaly);
            return anomaly;
        }

        return null;
    }

    /// <summary>
    /// Processes a quote event for anomaly detection.
    /// </summary>
    public DataAnomaly? ProcessQuote(
        string symbol,
        DateTimeOffset timestamp,
        decimal bidPrice,
        decimal askPrice,
        string? provider = null)
    {
        if (_isDisposed)
            return null;
        if (bidPrice <= 0 || askPrice <= 0)
            return null;

        var stats = _symbolStats.GetOrAdd(symbol.ToUpperInvariant(),
            _ => new SymbolStatistics(symbol, _config.MinSamplesForStatistics));

        DataAnomaly? anomaly = null;

        // Check for crossed market
        if (bidPrice > askPrice)
        {
            anomaly = CreateAnomaly(
                symbol, timestamp, AnomalyType.CrossedMarket, AnomalySeverity.Error,
                $"Crossed market: bid {bidPrice:F4} > ask {askPrice:F4}",
                (double)askPrice, (double)bidPrice, (double)((bidPrice - askPrice) / askPrice * 100));
        }
        // Check for wide spread
        else if (_config.EnableSpreadAnomalies && stats.HasEnoughSamples)
        {
            var spread = askPrice - bidPrice;
            var spreadPercent = spread / ((bidPrice + askPrice) / 2) * 100;

            if (spreadPercent > (decimal)_config.SpreadThresholdPercent)
            {
                anomaly = CreateAnomaly(
                    symbol, timestamp, AnomalyType.SpreadWide, AnomalySeverity.Warning,
                    $"Wide spread: {spreadPercent:F2}% (threshold: {_config.SpreadThresholdPercent}%)",
                    _config.SpreadThresholdPercent, (double)spreadPercent, (double)spreadPercent);
            }
        }

        if (anomaly != null)
        {
            anomaly = anomaly with { Provider = provider, Id = GenerateAnomalyId() };
            RecordAnomaly(anomaly);
        }

        stats.RecordQuote(timestamp, bidPrice, askPrice);
        return anomaly;
    }

    /// <summary>
    /// Gets all anomalies for a symbol.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetAnomalies(string symbol, int count = 100)
    {
        var key = symbol.ToUpperInvariant();
        if (!_anomalies.TryGetValue(key, out var list))
        {
            return Array.Empty<DataAnomaly>();
        }

        lock (list)
        {
            return list.OrderByDescending(a => a.Timestamp).Take(count).ToList();
        }
    }

    /// <summary>
    /// Gets all anomalies for a date.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetAnomaliesForDate(DateOnly date)
    {
        return _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .Where(a => DateOnly.FromDateTime(a.Timestamp.UtcDateTime) == date)
            .OrderByDescending(a => a.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets recent anomalies across all symbols.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetRecentAnomalies(int count = 100)
    {
        return _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets anomalies by type.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetAnomaliesByType(AnomalyType type, int count = 100)
    {
        return _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .Where(a => a.Type == type)
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets anomalies by severity.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetAnomaliesBySeverity(AnomalySeverity severity, int count = 100)
    {
        return _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .Where(a => a.Severity >= severity)
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Acknowledges an anomaly (marks it as reviewed).
    /// </summary>
    public bool AcknowledgeAnomaly(string anomalyId)
    {
        foreach (var list in _anomalies.Values)
        {
            lock (list)
            {
                var index = list.FindIndex(a => a.Id == anomalyId);
                if (index >= 0)
                {
                    list[index] = list[index] with { IsAcknowledged = true };
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets unacknowledged anomalies.
    /// </summary>
    public IReadOnlyList<DataAnomaly> GetUnacknowledgedAnomalies(int count = 100)
    {
        return _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets anomaly detection statistics.
    /// </summary>
    public AnomalyStatistics GetStatistics()
    {
        var allAnomalies = _anomalies.Values
            .SelectMany(list =>
            {
                lock (list)
                { return list.ToList(); }
            })
            .ToList();

        var byType = allAnomalies
            .GroupBy(a => a.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var bySeverity = allAnomalies
            .GroupBy(a => a.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        var bySymbol = allAnomalies
            .GroupBy(a => a.Symbol)
            .Select(g => (Symbol: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        return new AnomalyStatistics(
            TotalAnomalies: Interlocked.Read(ref _totalAnomaliesDetected),
            AnomaliesByType: byType,
            AnomaliesBySeverity: bySeverity,
            SymbolsWithMostAnomalies: bySymbol,
            UnacknowledgedCount: allAnomalies.Count(a => !a.IsAcknowledged),
            AnomaliesLast24Hours: allAnomalies.Count(a => a.Timestamp > DateTimeOffset.UtcNow.AddHours(-24)),
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets symbols currently marked as stale (using per-symbol thresholds when registered).
    /// </summary>
    public IReadOnlyList<string> GetStaleSymbols()
    {
        var now = DateTimeOffset.UtcNow;
        return _symbolStats
            .Where(kvp =>
            {
                if (kvp.Value.LastEventTime == DateTimeOffset.MinValue)
                    return false;
                var staleThreshold = _symbolStaleThresholds.TryGetValue(kvp.Key, out var perSymbol)
                    ? perSymbol
                    : _config.StaleDataThresholdSeconds;
                return (now - kvp.Value.LastEventTime).TotalSeconds >= staleThreshold;
            })
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Resets statistics for a symbol.
    /// </summary>
    public void ResetSymbol(string symbol)
    {
        var key = symbol.ToUpperInvariant();
        _symbolStats.TryRemove(key, out _);
        _anomalies.TryRemove(key, out _);
    }

    /// <summary>
    /// Total anomalies detected.
    /// </summary>
    public long TotalAnomaliesDetected => Interlocked.Read(ref _totalAnomaliesDetected);

    private void RecordAnomaly(DataAnomaly anomaly)
    {
        // Check alert cooldown
        var alertKey = $"{anomaly.Symbol}:{anomaly.Type}";
        if (_lastAlertTimes.TryGetValue(alertKey, out var lastAlert))
        {
            if ((DateTimeOffset.UtcNow - lastAlert).TotalSeconds < _config.AlertCooldownSeconds)
            {
                return;
            }
        }

        Interlocked.Increment(ref _totalAnomaliesDetected);
        _lastAlertTimes[alertKey] = DateTimeOffset.UtcNow;

        var list = _anomalies.GetOrAdd(anomaly.Symbol, _ => new List<DataAnomaly>());
        lock (list)
        {
            list.Add(anomaly);
            while (list.Count > 1000)
            {
                list.RemoveAt(0);
            }
        }

        // Log based on severity
        switch (anomaly.Severity)
        {
            case AnomalySeverity.Critical:
                _log.Error("CRITICAL ANOMALY: {Symbol} - {Type}: {Description}",
                    anomaly.Symbol, anomaly.Type, anomaly.Description);
                break;
            case AnomalySeverity.Error:
                _log.Warning("ANOMALY: {Symbol} - {Type}: {Description}",
                    anomaly.Symbol, anomaly.Type, anomaly.Description);
                break;
            default:
                _log.Information("Anomaly detected: {Symbol} - {Type}: {Description}",
                    anomaly.Symbol, anomaly.Type, anomaly.Description);
                break;
        }

        try
        {
            OnAnomalyDetected?.Invoke(anomaly);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in anomaly detected event handler");
        }
    }

    private DataAnomaly CreateAnomaly(
        string symbol,
        DateTimeOffset timestamp,
        AnomalyType type,
        AnomalySeverity severity,
        string description,
        double expected,
        double actual,
        double deviationPercent,
        double zScore = 0)
    {
        return new DataAnomaly(
            Id: "", // Will be set by caller
            Timestamp: timestamp,
            Symbol: symbol,
            Type: type,
            Severity: severity,
            Description: description,
            ExpectedValue: expected,
            ActualValue: actual,
            DeviationPercent: Math.Round(deviationPercent, 2),
            ZScore: Math.Round(zScore, 2),
            Provider: null,
            IsAcknowledged: false,
            DetectedAt: DateTimeOffset.UtcNow
        );
    }

    private string GenerateAnomalyId()
    {
        var id = Interlocked.Increment(ref _anomalyIdCounter);
        return $"ANM-{DateTimeOffset.UtcNow:yyyyMMdd}-{id:D6}";
    }

    private void CheckForStaleData(object? state)
    {
        if (_isDisposed)
            return;
        if (!_config.EnableStaleDataDetection)
            return;

        try
        {
            foreach (var kvp in _symbolStats)
            {
                var stats = kvp.Value;
                if (stats.LastEventTime == DateTimeOffset.MinValue || stats.IsMarkedStale)
                    continue;

                // Use per-symbol stale threshold if registered, otherwise global config
                var staleThreshold = _symbolStaleThresholds.TryGetValue(kvp.Key, out var perSymbol)
                    ? perSymbol
                    : _config.StaleDataThresholdSeconds;

                var timeSinceLastEvent = DateTimeOffset.UtcNow - stats.LastEventTime;
                if (timeSinceLastEvent.TotalSeconds >= staleThreshold)
                {
                    var anomaly = CreateAnomaly(
                        kvp.Key, DateTimeOffset.UtcNow, AnomalyType.StaleData, AnomalySeverity.Warning,
                        $"No data received for {timeSinceLastEvent.TotalSeconds:F0}s (threshold: {staleThreshold}s)",
                        staleThreshold, timeSinceLastEvent.TotalSeconds,
                        (timeSinceLastEvent.TotalSeconds / staleThreshold - 1) * 100);

                    anomaly = anomaly with { Id = GenerateAnomalyId() };
                    RecordAnomaly(anomaly);
                    stats.MarkAsStale();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during stale data check");
        }
    }

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            foreach (var list in _anomalies.Values)
            {
                lock (list)
                {
                    list.RemoveAll(a => a.Timestamp < cutoff);
                }
            }

            // Clean up alert cooldown times
            var alertCutoff = DateTimeOffset.UtcNow.AddHours(-1);
            var keysToRemove = _lastAlertTimes
                .Where(kvp => kvp.Value < alertCutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _lastAlertTimes.TryRemove(key, out _);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during anomaly detector cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _staleCheckTimer.Dispose();
        _symbolStats.Clear();
        _anomalies.Clear();
    }

    /// <summary>
    /// Per-symbol statistics for anomaly detection.
    /// </summary>
    private sealed class SymbolStatistics
    {
        private readonly object _lock = new();
        private readonly string _symbol;
        private readonly int _minSamples;
        private readonly Queue<decimal> _priceHistory = new();
        private readonly Queue<decimal> _volumeHistory = new();
        private decimal _priceSum;
        private decimal _priceSumSquares;
        private decimal _volumeSum;
        private decimal _volumeSumSquares;
        private decimal _lastPrice;
        private DateTimeOffset _lastPriceTime = DateTimeOffset.MinValue;
        private const int MaxHistorySize = 1000;

        public DateTimeOffset LastEventTime { get; private set; } = DateTimeOffset.MinValue;
        public bool IsMarkedStale { get; private set; }
        public bool HasEnoughSamples => _priceHistory.Count >= _minSamples;

        public SymbolStatistics(string symbol, int minSamples)
        {
            _symbol = symbol;
            _minSamples = minSamples;
        }

        public DataAnomaly? RecordTrade(DateTimeOffset timestamp, decimal price, decimal volume, AnomalyDetectionConfig config)
        {
            lock (_lock)
            {
                LastEventTime = timestamp;
                IsMarkedStale = false;

                DataAnomaly? anomaly = null;

                // Check for price spike if we have enough history
                if (config.EnablePriceAnomalies && _priceHistory.Count >= _minSamples)
                {
                    var mean = _priceSum / _priceHistory.Count;
                    var deviationPercent = Math.Abs((price - mean) / mean * 100);

                    // Check deviation percent first (works even with zero stdDev for stable prices)
                    if (deviationPercent > (decimal)config.PriceSpikeThresholdPercent)
                    {
                        var type = price > mean ? AnomalyType.PriceSpike : AnomalyType.PriceDrop;
                        var severity = deviationPercent > (decimal)config.PriceSpikeThresholdPercent * 2
                            ? AnomalySeverity.Critical
                            : AnomalySeverity.Error;

                        anomaly = new DataAnomaly(
                            Id: "",
                            Timestamp: timestamp,
                            Symbol: _symbol,
                            Type: type,
                            Severity: severity,
                            Description: $"{type}: price {price:F4} deviates {deviationPercent:F2}% from mean {mean:F4}",
                            ExpectedValue: (double)mean,
                            ActualValue: (double)price,
                            DeviationPercent: Math.Round((double)deviationPercent, 2),
                            ZScore: 0, // Z-score not calculated when stdDev is 0
                            Provider: null,
                            IsAcknowledged: false,
                            DetectedAt: DateTimeOffset.UtcNow
                        );
                    }
                    // Also check z-score if stdDev > 0 and no deviation anomaly detected
                    else
                    {
                        var variance = (_priceSumSquares / _priceHistory.Count) - (mean * mean);
                        var stdDev = (decimal)Math.Sqrt(Math.Max(0, (double)variance));

                        if (stdDev > 0)
                        {
                            var zScore = (price - mean) / stdDev;

                            if (Math.Abs(zScore) > (decimal)config.ZScoreThreshold)
                            {
                                var type = price > mean ? AnomalyType.PriceSpike : AnomalyType.PriceDrop;
                                var severity = Math.Abs(zScore) > (decimal)config.ZScoreThreshold * 2
                                    ? AnomalySeverity.Critical
                                    : AnomalySeverity.Warning;

                                anomaly = new DataAnomaly(
                                    Id: "",
                                    Timestamp: timestamp,
                                    Symbol: _symbol,
                                    Type: type,
                                    Severity: severity,
                                    Description: $"{type}: price {price:F4} deviates {deviationPercent:F2}% from mean {mean:F4} (z-score: {zScore:F2})",
                                    ExpectedValue: (double)mean,
                                    ActualValue: (double)price,
                                    DeviationPercent: Math.Round((double)deviationPercent, 2),
                                    ZScore: Math.Round((double)zScore, 2),
                                    Provider: null,
                                    IsAcknowledged: false,
                                    DetectedAt: DateTimeOffset.UtcNow
                                );
                            }
                        }
                    }

                    // Check for rapid price change only if no price spike/drop was detected
                    // (price spikes take priority as they're more significant)
                    if (anomaly == null && _lastPrice > 0 && _lastPriceTime != DateTimeOffset.MinValue)
                    {
                        var timeDelta = (timestamp - _lastPriceTime).TotalSeconds;
                        if (timeDelta <= config.RapidChangeWindowSeconds && timeDelta > 0)
                        {
                            var changePercent = Math.Abs((price - _lastPrice) / _lastPrice * 100);
                            if (changePercent > (decimal)config.RapidChangeThresholdPercent)
                            {
                                anomaly = new DataAnomaly(
                                    Id: "",
                                    Timestamp: timestamp,
                                    Symbol: _symbol,
                                    Type: AnomalyType.RapidPriceChange,
                                    Severity: AnomalySeverity.Warning,
                                    Description: $"Rapid price change: {changePercent:F2}% in {timeDelta:F1}s",
                                    ExpectedValue: (double)_lastPrice,
                                    ActualValue: (double)price,
                                    DeviationPercent: Math.Round((double)changePercent, 2),
                                    ZScore: 0,
                                    Provider: null,
                                    IsAcknowledged: false,
                                    DetectedAt: DateTimeOffset.UtcNow
                                );
                            }
                        }
                    }
                }

                // Check for volume spike or volume drop (only if no price anomaly detected)
                // Price anomalies are generally more critical than volume anomalies
                if (anomaly == null && config.EnableVolumeAnomalies && _volumeHistory.Count >= _minSamples && volume > 0)
                {
                    var meanVolume = _volumeSum / _volumeHistory.Count;
                    if (meanVolume > 0)
                    {
                        var volumeMultiplier = volume / meanVolume;
                        if (volumeMultiplier > (decimal)config.VolumeSpikeThresholdMultiplier)
                        {
                            anomaly = new DataAnomaly(
                                Id: "",
                                Timestamp: timestamp,
                                Symbol: _symbol,
                                Type: AnomalyType.VolumeSpike,
                                Severity: volumeMultiplier > (decimal)config.VolumeSpikeThresholdMultiplier * 2
                                    ? AnomalySeverity.Error : AnomalySeverity.Warning,
                                Description: $"Volume spike: {volume:F0} is {volumeMultiplier:F1}x average ({meanVolume:F0})",
                                ExpectedValue: (double)meanVolume,
                                ActualValue: (double)volume,
                                DeviationPercent: Math.Round((double)((volumeMultiplier - 1) * 100), 2),
                                ZScore: 0,
                                Provider: null,
                                IsAcknowledged: false,
                                DetectedAt: DateTimeOffset.UtcNow
                            );
                        }
                        else if (volumeMultiplier < (decimal)config.VolumeDropThresholdMultiplier)
                        {
                            // Detect abnormally low volume (potential liquidity issues)
                            var dropPercent = (1 - volumeMultiplier) * 100;
                            anomaly = new DataAnomaly(
                                Id: "",
                                Timestamp: timestamp,
                                Symbol: _symbol,
                                Type: AnomalyType.VolumeDrop,
                                Severity: volumeMultiplier < (decimal)config.VolumeDropThresholdMultiplier / 2
                                    ? AnomalySeverity.Error : AnomalySeverity.Warning,
                                Description: $"Volume drop: {volume:F0} is only {volumeMultiplier:P1} of average ({meanVolume:F0})",
                                ExpectedValue: (double)meanVolume,
                                ActualValue: (double)volume,
                                DeviationPercent: Math.Round((double)dropPercent, 2),
                                ZScore: 0,
                                Provider: null,
                                IsAcknowledged: false,
                                DetectedAt: DateTimeOffset.UtcNow
                            );
                        }
                    }
                }

                // Update running statistics
                AddPrice(price);
                AddVolume(volume);
                _lastPrice = price;
                _lastPriceTime = timestamp;

                return anomaly;
            }
        }

        public void RecordQuote(DateTimeOffset timestamp, decimal bidPrice, decimal askPrice)
        {
            lock (_lock)
            {
                LastEventTime = timestamp;
                IsMarkedStale = false;
                var midPrice = (bidPrice + askPrice) / 2;
                AddPrice(midPrice);
            }
        }

        public void MarkAsStale()
        {
            lock (_lock)
            {
                IsMarkedStale = true;
            }
        }

        private void AddPrice(decimal price)
        {
            _priceHistory.Enqueue(price);
            _priceSum += price;
            _priceSumSquares += price * price;

            while (_priceHistory.Count > MaxHistorySize)
            {
                var removed = _priceHistory.Dequeue();
                _priceSum -= removed;
                _priceSumSquares -= removed * removed;
            }
        }

        private void AddVolume(decimal volume)
        {
            _volumeHistory.Enqueue(volume);
            _volumeSum += volume;
            _volumeSumSquares += volume * volume;

            while (_volumeHistory.Count > MaxHistorySize)
            {
                var removed = _volumeHistory.Dequeue();
                _volumeSum -= removed;
                _volumeSumSquares -= removed * removed;
            }
        }
    }
}

/// <summary>
/// Anomaly detection statistics.
/// </summary>
public sealed record AnomalyStatistics(
    long TotalAnomalies,
    Dictionary<AnomalyType, int> AnomaliesByType,
    Dictionary<AnomalySeverity, int> AnomaliesBySeverity,
    IReadOnlyList<(string Symbol, int Count)> SymbolsWithMostAnomalies,
    int UnacknowledgedCount,
    int AnomaliesLast24Hours,
    DateTimeOffset CalculatedAt
);
