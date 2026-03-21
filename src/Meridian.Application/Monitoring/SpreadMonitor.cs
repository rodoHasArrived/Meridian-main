using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Monitors bid-ask spreads and alerts on abnormally wide spreads.
/// Part of the data quality framework (QW-7) to detect liquidity
/// issues, stale quotes, or data quality problems.
/// </summary>
/// <remarks>
/// Tracks spread statistics per symbol and fires alerts when spreads
/// exceed configurable thresholds (absolute or relative to price).
/// </remarks>
public sealed class SpreadMonitor : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<SpreadMonitor>();
    private readonly ConcurrentDictionary<string, SpreadState> _symbolStates = new();
    private readonly SpreadMonitorConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Counters
    private long _totalWideSpreadEvents;
    private long _totalQuotesProcessed;

    /// <summary>
    /// Event raised when a wide spread is detected.
    /// </summary>
    public event Action<WideSpreadAlert>? OnWideSpread;

    public SpreadMonitor(SpreadMonitorConfig? config = null)
    {
        _config = config ?? SpreadMonitorConfig.Default;
        _cleanupTimer = new Timer(CleanupOldStates, null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _log.Information(
            "SpreadMonitor initialized with threshold {ThresholdBps}bps / {ThresholdPercent}%, max absolute ${MaxAbsolute:F4}",
            _config.WideSpreadThresholdBps, _config.WideSpreadThresholdPercent, _config.MaxAbsoluteSpread);
    }

    /// <summary>
    /// Processes a quote and monitors the spread.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="bidPrice">The bid price.</param>
    /// <param name="askPrice">The ask price.</param>
    /// <param name="provider">The data provider (optional).</param>
    /// <returns>True if a wide spread was detected.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ProcessQuote(string symbol, decimal bidPrice, decimal askPrice, string? provider = null)
    {
        if (_isDisposed)
            return false;
        if (bidPrice <= 0 || askPrice <= 0)
            return false;

        Interlocked.Increment(ref _totalQuotesProcessed);

        var state = _symbolStates.GetOrAdd(symbol, _ => new SpreadState());
        var now = DateTimeOffset.UtcNow;

        var spread = askPrice - bidPrice;
        var midPrice = (bidPrice + askPrice) / 2;
        var spreadBps = midPrice > 0 ? (double)(spread / midPrice * 10000) : 0;
        var spreadPercent = midPrice > 0 ? (double)(spread / midPrice * 100) : 0;

        // Update statistics
        state.UpdateStats(spread, spreadBps);

        // Check for wide spread condition
        var isWide = false;
        var reason = string.Empty;

        if (spreadBps >= _config.WideSpreadThresholdBps)
        {
            isWide = true;
            reason = $"Spread {spreadBps:F1}bps exceeds threshold {_config.WideSpreadThresholdBps}bps";
        }
        else if (spreadPercent >= _config.WideSpreadThresholdPercent)
        {
            isWide = true;
            reason = $"Spread {spreadPercent:F2}% exceeds threshold {_config.WideSpreadThresholdPercent}%";
        }
        else if (_config.MaxAbsoluteSpread > 0 && spread >= _config.MaxAbsoluteSpread)
        {
            isWide = true;
            reason = $"Spread ${spread:F4} exceeds max absolute ${_config.MaxAbsoluteSpread:F4}";
        }

        if (isWide)
        {
            Interlocked.Increment(ref _totalWideSpreadEvents);
            state.IncrementWideCount();

            // Only alert if cooldown has passed
            if (state.CanAlert(now, _config.AlertCooldownMs))
            {
                var alert = new WideSpreadAlert(
                    Symbol: symbol,
                    BidPrice: bidPrice,
                    AskPrice: askPrice,
                    Spread: spread,
                    SpreadBps: spreadBps,
                    SpreadPercent: spreadPercent,
                    Reason: reason,
                    AverageSpreadBps: state.AverageSpreadBps,
                    Provider: provider,
                    Timestamp: now,
                    ConsecutiveWideCount: state.ConsecutiveWideCount
                );

                _log.Warning(
                    "WIDE SPREAD: {Symbol} bid={BidPrice:F4} ask={AskPrice:F4} spread={SpreadBps:F1}bps ({SpreadPercent:F3}%) - {Reason}",
                    symbol, bidPrice, askPrice, spreadBps, spreadPercent, reason);

                state.RecordAlert(now);

                try
                {
                    OnWideSpread?.Invoke(alert);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in wide spread event handler for {Symbol}", symbol);
                }

                return true;
            }
        }
        else
        {
            state.ResetConsecutiveWideCount();
        }

        return false;
    }

    /// <summary>
    /// Gets the current spread statistics for a symbol.
    /// </summary>
    public SpreadSnapshot? GetSpreadSnapshot(string symbol)
    {
        if (!_symbolStates.TryGetValue(symbol, out var state))
            return null;

        return new SpreadSnapshot(
            Symbol: symbol,
            CurrentSpreadBps: state.LastSpreadBps,
            AverageSpreadBps: state.AverageSpreadBps,
            MinSpreadBps: state.MinSpreadBps,
            MaxSpreadBps: state.MaxSpreadBps,
            TotalQuotes: state.TotalQuotes,
            WideSpreadCount: state.TotalWideCount,
            LastUpdateTime: state.LastUpdateTime
        );
    }

    /// <summary>
    /// Gets spread snapshots for all tracked symbols.
    /// </summary>
    public IReadOnlyList<SpreadSnapshot> GetAllSpreadSnapshots()
    {
        return _symbolStates.Select(kvp => new SpreadSnapshot(
            Symbol: kvp.Key,
            CurrentSpreadBps: kvp.Value.LastSpreadBps,
            AverageSpreadBps: kvp.Value.AverageSpreadBps,
            MinSpreadBps: kvp.Value.MinSpreadBps,
            MaxSpreadBps: kvp.Value.MaxSpreadBps,
            TotalQuotes: kvp.Value.TotalQuotes,
            WideSpreadCount: kvp.Value.TotalWideCount,
            LastUpdateTime: kvp.Value.LastUpdateTime
        )).ToList();
    }

    /// <summary>
    /// Gets statistics about spread monitoring.
    /// </summary>
    public SpreadMonitorStats GetStats()
    {
        var symbolStats = _symbolStates
            .Where(kvp => kvp.Value.TotalWideCount > 0)
            .Select(kvp => new SymbolSpreadStats(
                Symbol: kvp.Key,
                TotalWideSpreadEvents: kvp.Value.TotalWideCount,
                AverageSpreadBps: kvp.Value.AverageSpreadBps,
                MaxSpreadBps: kvp.Value.MaxSpreadBps,
                LastWideSpreadTime: kvp.Value.LastWideSpreadTime
            ))
            .OrderByDescending(s => s.TotalWideSpreadEvents)
            .ToList();

        return new SpreadMonitorStats(
            TotalQuotesProcessed: Interlocked.Read(ref _totalQuotesProcessed),
            TotalWideSpreadEvents: Interlocked.Read(ref _totalWideSpreadEvents),
            SymbolStats: symbolStats
        );
    }

    /// <summary>
    /// Gets the total count of wide spread events detected.
    /// </summary>
    public long TotalWideSpreadEvents => Interlocked.Read(ref _totalWideSpreadEvents);

    private void CleanupOldStates(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var toRemove = _symbolStates
                .Where(kvp => kvp.Value.LastUpdateTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var symbol in toRemove)
            {
                _symbolStates.TryRemove(symbol, out _);
            }

            if (toRemove.Count > 0)
            {
                _log.Debug("Cleaned up {Count} inactive symbol states from spread monitor", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during spread monitor state cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _symbolStates.Clear();
    }

    /// <summary>
    /// Per-symbol state for spread tracking.
    /// </summary>
    private sealed class SpreadState
    {
        private long _totalQuotes;
        private long _totalWideCount;
        private int _consecutiveWideCount;
        private double _lastSpreadBps;
        private double _sumSpreadBps;
        private double _minSpreadBps = double.MaxValue;
        private double _maxSpreadBps;
        private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastWideSpreadTime;
        private DateTimeOffset _lastUpdateTime;

        public long TotalQuotes => Interlocked.Read(ref _totalQuotes);
        public long TotalWideCount => Interlocked.Read(ref _totalWideCount);
        public int ConsecutiveWideCount => _consecutiveWideCount;
        public double LastSpreadBps => _lastSpreadBps;
        public double MinSpreadBps => _minSpreadBps == double.MaxValue ? 0 : _minSpreadBps;
        public double MaxSpreadBps => _maxSpreadBps;
        public DateTimeOffset LastWideSpreadTime => _lastWideSpreadTime;
        public DateTimeOffset LastUpdateTime => _lastUpdateTime;

        public double AverageSpreadBps
        {
            get
            {
                var count = Interlocked.Read(ref _totalQuotes);
                return count > 0 ? _sumSpreadBps / count : 0;
            }
        }

        public void UpdateStats(decimal spread, double spreadBps)
        {
            Interlocked.Increment(ref _totalQuotes);
            _lastSpreadBps = spreadBps;
            _sumSpreadBps += spreadBps;
            _lastUpdateTime = DateTimeOffset.UtcNow;

            if (spreadBps < _minSpreadBps)
                _minSpreadBps = spreadBps;
            if (spreadBps > _maxSpreadBps)
                _maxSpreadBps = spreadBps;
        }

        public void IncrementWideCount()
        {
            Interlocked.Increment(ref _totalWideCount);
            Interlocked.Increment(ref _consecutiveWideCount);
            _lastWideSpreadTime = DateTimeOffset.UtcNow;
        }

        public void ResetConsecutiveWideCount()
        {
            _consecutiveWideCount = 0;
        }

        public bool CanAlert(DateTimeOffset now, int cooldownMs)
        {
            return (now - _lastAlertTime).TotalMilliseconds >= cooldownMs;
        }

        public void RecordAlert(DateTimeOffset time)
        {
            _lastAlertTime = time;
        }
    }
}

/// <summary>
/// Configuration for spread monitoring.
/// </summary>
public sealed record SpreadMonitorConfig
{
    /// <summary>
    /// Spread threshold in basis points (1 bps = 0.01%).
    /// Default is 100 bps (1%).
    /// </summary>
    public double WideSpreadThresholdBps { get; init; } = 100.0;

    /// <summary>
    /// Spread threshold as a percentage of mid-price.
    /// Default is 1%.
    /// </summary>
    public double WideSpreadThresholdPercent { get; init; } = 1.0;

    /// <summary>
    /// Maximum absolute spread in price units.
    /// Set to 0 to disable absolute threshold.
    /// </summary>
    public decimal MaxAbsoluteSpread { get; init; } = 0;

    /// <summary>
    /// Minimum time between alerts for the same symbol in milliseconds.
    /// </summary>
    public int AlertCooldownMs { get; init; } = 10000;

    public static SpreadMonitorConfig Default => new();

    /// <summary>
    /// Configuration for liquid large-cap stocks.
    /// </summary>
    public static SpreadMonitorConfig LargeCap => new()
    {
        WideSpreadThresholdBps = 10.0,
        WideSpreadThresholdPercent = 0.1,
        AlertCooldownMs = 5000
    };

    /// <summary>
    /// Configuration for small-cap or illiquid stocks.
    /// </summary>
    public static SpreadMonitorConfig SmallCap => new()
    {
        WideSpreadThresholdBps = 500.0,
        WideSpreadThresholdPercent = 5.0,
        AlertCooldownMs = 30000
    };
}

/// <summary>
/// Alert for a wide spread condition.
/// </summary>
public readonly record struct WideSpreadAlert(
    string Symbol,
    decimal BidPrice,
    decimal AskPrice,
    decimal Spread,
    double SpreadBps,
    double SpreadPercent,
    string Reason,
    double AverageSpreadBps,
    string? Provider,
    DateTimeOffset Timestamp,
    int ConsecutiveWideCount
);

/// <summary>
/// Snapshot of spread statistics for a symbol.
/// </summary>
public readonly record struct SpreadSnapshot(
    string Symbol,
    double CurrentSpreadBps,
    double AverageSpreadBps,
    double MinSpreadBps,
    double MaxSpreadBps,
    long TotalQuotes,
    long WideSpreadCount,
    DateTimeOffset LastUpdateTime
);

/// <summary>
/// Statistics for spread monitoring.
/// </summary>
public readonly record struct SpreadMonitorStats(
    long TotalQuotesProcessed,
    long TotalWideSpreadEvents,
    IReadOnlyList<SymbolSpreadStats> SymbolStats
);

/// <summary>
/// Per-symbol spread statistics.
/// </summary>
public readonly record struct SymbolSpreadStats(
    string Symbol,
    long TotalWideSpreadEvents,
    double AverageSpreadBps,
    double MaxSpreadBps,
    DateTimeOffset LastWideSpreadTime
);
