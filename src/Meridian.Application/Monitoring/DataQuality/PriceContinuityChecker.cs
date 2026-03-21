using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Validates price continuity to detect gap moves, splits, or erroneous data.
/// Implements DQ-16: Price Continuity Checker.
/// </summary>
public sealed class PriceContinuityChecker : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<PriceContinuityChecker>();
    private readonly ConcurrentDictionary<string, PriceState> _priceStates = new();
    private readonly PriceContinuityConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Statistics
    private long _totalChecks;
    private long _totalDiscontinuities;
    private long _totalGapUps;
    private long _totalGapDowns;

    /// <summary>
    /// Event raised when a price discontinuity is detected.
    /// </summary>
    public event Action<PriceDiscontinuityEvent>? OnDiscontinuity;

    public PriceContinuityChecker(PriceContinuityConfig? config = null)
    {
        _config = config ?? PriceContinuityConfig.Default;
        _cleanupTimer = new Timer(
            CleanupStaleEntries,
            null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(30));

        _log.Information("PriceContinuityChecker initialized with gap threshold {GapPercent}%",
            _config.GapThresholdPercent);
    }

    /// <summary>
    /// Checks price continuity for a trade.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PriceContinuityResult CheckPrice(string symbol, decimal price, DateTimeOffset timestamp, string? provider = null)
    {
        if (_isDisposed)
            return PriceContinuityResult.Ok;
        if (price <= 0)
            return PriceContinuityResult.Ok;

        Interlocked.Increment(ref _totalChecks);

        var key = GetKey(symbol, provider);
        var state = _priceStates.GetOrAdd(key, _ => new PriceState(symbol, provider));

        return state.CheckPrice(price, timestamp, _config, OnDiscontinuityDetected);
    }

    /// <summary>
    /// Checks price continuity for a bar.
    /// </summary>
    public PriceContinuityResult CheckBar(
        string symbol,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        DateTimeOffset timestamp,
        string? provider = null)
    {
        if (_isDisposed)
            return PriceContinuityResult.Ok;

        Interlocked.Increment(ref _totalChecks);

        var key = GetKey(symbol, provider);
        var state = _priceStates.GetOrAdd(key, _ => new PriceState(symbol, provider));

        // Check open vs previous close
        var result = state.CheckPrice(open, timestamp, _config, OnDiscontinuityDetected);

        // Also validate bar internal consistency
        if (high < low || high < open || high < close || low > open || low > close)
        {
            var evt = new PriceDiscontinuityEvent(
                Symbol: symbol,
                Provider: provider,
                Type: DiscontinuityType.InvalidBar,
                PreviousPrice: state.LastPrice,
                CurrentPrice: close,
                ChangePercent: 0,
                Message: $"Invalid bar: O={open}, H={high}, L={low}, C={close}",
                Timestamp: timestamp,
                PreviousTimestamp: state.LastTimestamp);

            OnDiscontinuityDetected(evt);
            return PriceContinuityResult.InvalidBar;
        }

        // Update state with close price
        state.UpdatePrice(close, timestamp);
        return result;
    }

    /// <summary>
    /// Checks quote prices for continuity.
    /// </summary>
    public PriceContinuityResult CheckQuote(
        string symbol,
        decimal bidPrice,
        decimal askPrice,
        DateTimeOffset timestamp,
        string? provider = null)
    {
        if (_isDisposed)
            return PriceContinuityResult.Ok;
        if (bidPrice <= 0 || askPrice <= 0)
            return PriceContinuityResult.Ok;

        var midPrice = (bidPrice + askPrice) / 2;
        return CheckPrice(symbol, midPrice, timestamp, provider);
    }

    /// <summary>
    /// Gets statistics for price continuity checking.
    /// </summary>
    public PriceContinuityStatistics GetStatistics()
    {
        var symbolStats = _priceStates.Values
            .Select(s => s.GetStatistics())
            .OrderByDescending(s => s.DiscontinuityCount)
            .Take(20)
            .ToList();

        return new PriceContinuityStatistics(
            TotalChecks: Interlocked.Read(ref _totalChecks),
            TotalDiscontinuities: Interlocked.Read(ref _totalDiscontinuities),
            TotalGapUps: Interlocked.Read(ref _totalGapUps),
            TotalGapDowns: Interlocked.Read(ref _totalGapDowns),
            SymbolsTracked: _priceStates.Count,
            SymbolStatistics: symbolStats,
            CalculatedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Gets statistics for a specific symbol.
    /// </summary>
    public SymbolPriceStatistics? GetSymbolStatistics(string symbol, string? provider = null)
    {
        var key = GetKey(symbol, provider);
        return _priceStates.TryGetValue(key, out var state) ? state.GetStatistics() : null;
    }

    /// <summary>
    /// Gets symbols with the most discontinuities.
    /// </summary>
    public IReadOnlyList<(string Symbol, int Count)> GetTopDiscontinuitySymbols(int count = 10)
    {
        return _priceStates.Values
            .OrderByDescending(s => s.DiscontinuityCount)
            .Take(count)
            .Select(s => (s.Symbol, s.DiscontinuityCount))
            .ToList();
    }

    /// <summary>
    /// Resets tracking for a symbol.
    /// </summary>
    public void Reset(string symbol, string? provider = null)
    {
        var key = GetKey(symbol, provider);
        _priceStates.TryRemove(key, out _);
    }

    /// <summary>
    /// Resets all tracking.
    /// </summary>
    public void ResetAll()
    {
        _priceStates.Clear();
        Interlocked.Exchange(ref _totalChecks, 0);
        Interlocked.Exchange(ref _totalDiscontinuities, 0);
        Interlocked.Exchange(ref _totalGapUps, 0);
        Interlocked.Exchange(ref _totalGapDowns, 0);
    }

    private void OnDiscontinuityDetected(PriceDiscontinuityEvent evt)
    {
        Interlocked.Increment(ref _totalDiscontinuities);

        if (evt.ChangePercent > 0)
            Interlocked.Increment(ref _totalGapUps);
        else if (evt.ChangePercent < 0)
            Interlocked.Increment(ref _totalGapDowns);

        _log.Warning(
            "Price discontinuity for {Symbol}: {Type} - {ChangePercent:F2}% ({PreviousPrice} -> {CurrentPrice})",
            evt.Symbol, evt.Type, evt.ChangePercent, evt.PreviousPrice, evt.CurrentPrice);

        try
        {
            OnDiscontinuity?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in discontinuity event handler");
        }
    }

    private static string GetKey(string symbol, string? provider) =>
        provider != null ? $"{symbol.ToUpperInvariant()}:{provider}" : symbol.ToUpperInvariant();

    private void CleanupStaleEntries(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var keysToRemove = _priceStates
                .Where(kvp => kvp.Value.LastTimestamp < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _priceStates.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _log.Debug("Cleaned up {Count} stale price tracking entries", keysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during price continuity cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _priceStates.Clear();
    }

    /// <summary>
    /// Tracks price state for a single symbol.
    /// </summary>
    private sealed class PriceState
    {
        public string Symbol { get; }
        public string? Provider { get; }

        private readonly object _lock = new();
        private decimal _lastPrice;
        private decimal _minPrice = decimal.MaxValue;
        private decimal _maxPrice = decimal.MinValue;
        private DateTimeOffset _lastTimestamp;
        private int _checkCount;
        private int _discontinuityCount;
        private decimal _largestGapPercent;
        private bool _hasFirstPrice;

        public decimal LastPrice
        {
            get { lock (_lock) return _lastPrice; }
        }

        public DateTimeOffset LastTimestamp
        {
            get { lock (_lock) return _lastTimestamp; }
        }

        public int DiscontinuityCount
        {
            get { lock (_lock) return _discontinuityCount; }
        }

        public PriceState(string symbol, string? provider)
        {
            Symbol = symbol;
            Provider = provider;
        }

        public PriceContinuityResult CheckPrice(
            decimal price,
            DateTimeOffset timestamp,
            PriceContinuityConfig config,
            Action<PriceDiscontinuityEvent> onDiscontinuity)
        {
            lock (_lock)
            {
                _checkCount++;

                if (!_hasFirstPrice)
                {
                    _hasFirstPrice = true;
                    UpdatePriceUnsafe(price, timestamp);
                    return PriceContinuityResult.Ok;
                }

                // Check for price discontinuity
                var changePercent = _lastPrice != 0 ? (price - _lastPrice) / _lastPrice * 100 : 0;
                var absChange = Math.Abs(changePercent);

                PriceContinuityResult result;
                DiscontinuityType? discontinuityType = null;

                if (absChange >= config.LargeGapThresholdPercent)
                {
                    discontinuityType = changePercent > 0 ? DiscontinuityType.LargeGapUp : DiscontinuityType.LargeGapDown;
                    result = PriceContinuityResult.LargeGap;
                }
                else if (absChange >= config.GapThresholdPercent)
                {
                    discontinuityType = changePercent > 0 ? DiscontinuityType.GapUp : DiscontinuityType.GapDown;
                    result = PriceContinuityResult.Gap;
                }
                else
                {
                    result = PriceContinuityResult.Ok;
                }

                if (discontinuityType.HasValue)
                {
                    _discontinuityCount++;
                    if (absChange > Math.Abs(_largestGapPercent))
                    {
                        _largestGapPercent = changePercent;
                    }

                    var evt = new PriceDiscontinuityEvent(
                        Symbol: Symbol,
                        Provider: Provider,
                        Type: discontinuityType.Value,
                        PreviousPrice: _lastPrice,
                        CurrentPrice: price,
                        ChangePercent: changePercent,
                        Message: $"{discontinuityType}: {_lastPrice} -> {price} ({changePercent:F2}%)",
                        Timestamp: timestamp,
                        PreviousTimestamp: _lastTimestamp);

                    onDiscontinuity(evt);
                }

                UpdatePriceUnsafe(price, timestamp);
                return result;
            }
        }

        public void UpdatePrice(decimal price, DateTimeOffset timestamp)
        {
            lock (_lock)
            {
                UpdatePriceUnsafe(price, timestamp);
            }
        }

        private void UpdatePriceUnsafe(decimal price, DateTimeOffset timestamp)
        {
            _lastPrice = price;
            _lastTimestamp = timestamp;
            if (price < _minPrice)
                _minPrice = price;
            if (price > _maxPrice)
                _maxPrice = price;
        }

        public SymbolPriceStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new SymbolPriceStatistics(
                    Symbol: Symbol,
                    Provider: Provider,
                    CheckCount: _checkCount,
                    DiscontinuityCount: _discontinuityCount,
                    LastPrice: _lastPrice,
                    MinPrice: _minPrice == decimal.MaxValue ? 0 : _minPrice,
                    MaxPrice: _maxPrice == decimal.MinValue ? 0 : _maxPrice,
                    LargestGapPercent: _largestGapPercent,
                    LastUpdateTime: _lastTimestamp);
            }
        }
    }
}

/// <summary>
/// Configuration for price continuity checking.
/// </summary>
public sealed record PriceContinuityConfig
{
    /// <summary>
    /// Percentage change threshold to flag as a gap (default 5%).
    /// </summary>
    public decimal GapThresholdPercent { get; init; } = 5m;

    /// <summary>
    /// Percentage change threshold to flag as a large gap (default 15%).
    /// </summary>
    public decimal LargeGapThresholdPercent { get; init; } = 15m;

    /// <summary>
    /// Maximum time between prices before resetting baseline (default 24 hours).
    /// </summary>
    public TimeSpan MaxPriceAge { get; init; } = TimeSpan.FromHours(24);

    public static PriceContinuityConfig Default => new();
}

/// <summary>
/// Result of a price continuity check.
/// </summary>
public enum PriceContinuityResult : byte
{
    /// <summary>Price is within normal range.</summary>
    Ok,

    /// <summary>Price gap detected.</summary>
    Gap,

    /// <summary>Large price gap detected.</summary>
    LargeGap,

    /// <summary>Invalid bar data (high/low inconsistency).</summary>
    InvalidBar
}

/// <summary>
/// Type of price discontinuity.
/// </summary>
public enum DiscontinuityType : byte
{
    GapUp,
    GapDown,
    LargeGapUp,
    LargeGapDown,
    InvalidBar
}

/// <summary>
/// Event raised when a price discontinuity is detected.
/// </summary>
public readonly record struct PriceDiscontinuityEvent(
    string Symbol,
    string? Provider,
    DiscontinuityType Type,
    decimal PreviousPrice,
    decimal CurrentPrice,
    decimal ChangePercent,
    string Message,
    DateTimeOffset Timestamp,
    DateTimeOffset PreviousTimestamp
);

/// <summary>
/// Statistics for price continuity checking.
/// </summary>
public sealed record PriceContinuityStatistics(
    long TotalChecks,
    long TotalDiscontinuities,
    long TotalGapUps,
    long TotalGapDowns,
    int SymbolsTracked,
    IReadOnlyList<SymbolPriceStatistics> SymbolStatistics,
    DateTimeOffset CalculatedAt
);

/// <summary>
/// Price statistics for a single symbol.
/// </summary>
public sealed record SymbolPriceStatistics(
    string Symbol,
    string? Provider,
    int CheckCount,
    int DiscontinuityCount,
    decimal LastPrice,
    decimal MinPrice,
    decimal MaxPrice,
    decimal LargestGapPercent,
    DateTimeOffset LastUpdateTime
);
