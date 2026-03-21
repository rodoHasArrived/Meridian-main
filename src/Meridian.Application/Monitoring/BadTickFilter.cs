using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Filters and detects bad ticks (invalid market data points) using
/// multiple heuristics to identify data quality issues.
/// Part of the data quality framework (DQ-20).
/// </summary>
/// <remarks>
/// Bad ticks can include:
/// - Prices far outside normal trading range
/// - Zero or negative volumes on trades
/// - Impossible price movements within a time window
/// - Prices at obvious placeholder values (0.01, 9999.99, etc.)
/// </remarks>
public sealed class BadTickFilter : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<BadTickFilter>();
    private readonly ConcurrentDictionary<string, TickFilterState> _symbolStates = new();
    private readonly ConcurrentDictionary<string, LuldBand> _luldBands = new();
    private readonly ConcurrentDictionary<string, bool> _haltedSymbols = new();
    private readonly BadTickFilterConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Counters
    private long _totalBadTicksDetected;
    private long _totalTicksProcessed;

    /// <summary>
    /// Event raised when a bad tick is detected.
    /// </summary>
    public event Action<BadTickAlert>? OnBadTick;

    public BadTickFilter(BadTickFilterConfig? config = null)
    {
        _config = config ?? BadTickFilterConfig.Default;
        _cleanupTimer = new Timer(CleanupOldStates, null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _log.Information(
            "BadTickFilter initialized with max deviation {MaxDeviation}%, min price ${MinPrice:F4}",
            _config.MaxDeviationPercent, _config.MinValidPrice);
    }

    /// <summary>
    /// Filters a trade tick, returning true if it's bad.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="price">The trade price.</param>
    /// <param name="size">The trade size.</param>
    /// <param name="timestamp">The event timestamp.</param>
    /// <param name="provider">The data provider (optional).</param>
    /// <returns>True if the tick should be filtered (is bad).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBadTrade(string symbol, decimal price, decimal size, DateTimeOffset timestamp, string? provider = null)
    {
        if (_isDisposed)
            return false;

        Interlocked.Increment(ref _totalTicksProcessed);

        var reasons = new List<BadTickReason>();

        // Check for invalid price
        if (price <= 0)
        {
            reasons.Add(BadTickReason.NegativeOrZeroPrice);
        }
        else if (price < _config.MinValidPrice)
        {
            reasons.Add(BadTickReason.PriceBelowMinimum);
        }
        else if (price > _config.MaxValidPrice)
        {
            reasons.Add(BadTickReason.PriceAboveMaximum);
        }

        // Check for placeholder prices
        if (IsPlaceholderPrice(price))
        {
            reasons.Add(BadTickReason.PlaceholderPrice);
        }

        // Check for invalid size
        if (size <= 0 && _config.RequirePositiveSize)
        {
            reasons.Add(BadTickReason.InvalidSize);
        }

        // Check if symbol is halted
        if (_config.EnforceTradingHalts && _haltedSymbols.ContainsKey(symbol))
        {
            reasons.Add(BadTickReason.TradingHalted);
        }

        // Check LULD price bands (SEC Rule 201 / Reg NMS Plan)
        if (_config.EnforceLuldBands && _luldBands.TryGetValue(symbol, out var band) && price > 0)
        {
            if (price < band.LowerLimit || price > band.UpperLimit)
            {
                reasons.Add(BadTickReason.OutsideLuldBand);
            }
        }

        // Check for extreme deviation from recent prices
        var state = _symbolStates.GetOrAdd(symbol, _ => new TickFilterState());

        if (price > 0 && state.HasValidReference)
        {
            var deviation = Math.Abs((double)(price - state.ReferencePrice) / (double)state.ReferencePrice * 100);
            if (deviation > _config.MaxDeviationPercent)
            {
                reasons.Add(BadTickReason.ExtremeDeviation);
            }
        }

        if (reasons.Count > 0)
        {
            return RecordBadTick(symbol, price, size, timestamp, reasons, provider, state);
        }

        // Update reference price with good tick
        if (price > 0)
        {
            state.UpdateReference(price, timestamp);
        }

        return false;
    }

    /// <summary>
    /// Filters a quote tick, returning true if it's bad.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBadQuote(string symbol, decimal bidPrice, decimal askPrice, int bidSize, int askSize,
        DateTimeOffset timestamp, string? provider = null)
    {
        if (_isDisposed)
            return false;

        Interlocked.Increment(ref _totalTicksProcessed);

        var reasons = new List<BadTickReason>();

        // Check for invalid prices
        if (bidPrice <= 0 || askPrice <= 0)
        {
            reasons.Add(BadTickReason.NegativeOrZeroPrice);
        }

        // Check for crossed market (bid > ask)
        if (bidPrice > askPrice && bidPrice > 0 && askPrice > 0)
        {
            reasons.Add(BadTickReason.CrossedMarket);
        }

        // Check for placeholder prices
        if (IsPlaceholderPrice(bidPrice) || IsPlaceholderPrice(askPrice))
        {
            reasons.Add(BadTickReason.PlaceholderPrice);
        }

        // Check for invalid sizes
        if ((bidSize < 0 || askSize < 0) && _config.RequirePositiveSize)
        {
            reasons.Add(BadTickReason.InvalidSize);
        }

        // Check for extreme deviation
        var state = _symbolStates.GetOrAdd(symbol, _ => new TickFilterState());
        var midPrice = (bidPrice + askPrice) / 2;

        if (midPrice > 0 && state.HasValidReference)
        {
            var deviation = Math.Abs((double)(midPrice - state.ReferencePrice) / (double)state.ReferencePrice * 100);
            if (deviation > _config.MaxDeviationPercent)
            {
                reasons.Add(BadTickReason.ExtremeDeviation);
            }
        }

        // Check for abnormal spread
        if (bidPrice > 0 && askPrice > 0)
        {
            var spreadPercent = (double)((askPrice - bidPrice) / midPrice * 100);
            if (spreadPercent > _config.MaxSpreadPercent)
            {
                reasons.Add(BadTickReason.AbnormalSpread);
            }
        }

        if (reasons.Count > 0)
        {
            return RecordBadTick(symbol, midPrice, 0, timestamp, reasons, provider, state);
        }

        // Update reference with good quote mid-price
        if (midPrice > 0)
        {
            state.UpdateReference(midPrice, timestamp);
        }

        return false;
    }

    /// <summary>
    /// Checks if a price is a common placeholder value.
    /// </summary>
    private static bool IsPlaceholderPrice(decimal price)
    {
        // Common placeholder patterns used by various data providers
        if (price == 0.01m || price == 0.001m || price == 0.0001m)
            return true;
        if (price == 9999m || price == 9999.99m || price == 99999.99m || price == 999999.99m)
            return true;
        if (price == 1234.56m || price == 12345.67m)
            return true; // Sequential placeholders

        // Valid round numbers that are NOT placeholders
        if (price == 1m || price == 10m || price == 100m || price == 1000m || price == 10000m)
            return false;
        if (price == 0.1m || price == 0.5m || price == 5m || price == 50m || price == 500m)
            return false;

        // Check for repeating digit patterns that often indicate bad/test data
        if (IsRepeatingDigitPattern(price))
            return true;

        return false;
    }

    /// <summary>
    /// Detects repeating digit patterns like 111.11, 222.22, 333.33, etc.
    /// These are commonly used as placeholder or test values.
    /// </summary>
    private static bool IsRepeatingDigitPattern(decimal price)
    {
        // Common repeating digit placeholders
        decimal[] repeatingPatterns =
        [
            11.11m, 22.22m, 33.33m, 44.44m, 55.55m, 66.66m, 77.77m, 88.88m, 99.99m,
            111.11m, 222.22m, 333.33m, 444.44m, 555.55m, 666.66m, 777.77m, 888.88m, 999.99m,
            1111.11m, 2222.22m, 3333.33m, 4444.44m, 5555.55m, 6666.66m, 7777.77m, 8888.88m, 9999.99m,
            11111.11m, 22222.22m, 33333.33m, 44444.44m, 55555.55m, 66666.66m, 77777.77m, 88888.88m, 99999.99m
        ];

        foreach (var pattern in repeatingPatterns)
        {
            if (price == pattern)
                return true;
        }

        // Check for all-same-digit patterns in the string representation
        // This catches patterns we might have missed
        var priceStr = price.ToString("F2");
        var digitsOnly = new string(priceStr.Where(char.IsDigit).ToArray());

        // If all digits are the same (e.g., "11111", "22222")
        if (digitsOnly.Length >= 4 && digitsOnly.Distinct().Count() == 1)
        {
            return true;
        }

        return false;
    }

    private bool RecordBadTick(string symbol, decimal price, decimal size, DateTimeOffset timestamp,
        List<BadTickReason> reasons, string? provider, TickFilterState state)
    {
        Interlocked.Increment(ref _totalBadTicksDetected);
        state.IncrementBadCount();

        var now = DateTimeOffset.UtcNow;

        // Only alert if cooldown has passed
        if (state.CanAlert(now, _config.AlertCooldownMs))
        {
            var alert = new BadTickAlert(
                Symbol: symbol,
                Price: price,
                Size: size,
                Timestamp: timestamp,
                Reasons: reasons.ToArray(),
                ReferencePrice: state.ReferencePrice,
                Provider: provider,
                DetectedAt: now,
                BadTickCountInSession: state.TotalBadCount
            );

            _log.Warning(
                "BAD TICK: {Symbol} price={Price:F4} size={Size} reasons=[{Reasons}] ref={ReferencePrice:F4} from {Provider}",
                symbol, price, size, string.Join(", ", reasons), state.ReferencePrice, provider ?? "unknown");

            state.RecordAlert(now);

            try
            {
                OnBadTick?.Invoke(alert);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in bad tick event handler for {Symbol}", symbol);
            }
        }

        return true;
    }

    /// <summary>
    /// Gets statistics about bad tick detection.
    /// </summary>
    public BadTickFilterStats GetStats()
    {
        var symbolStats = _symbolStates
            .Where(kvp => kvp.Value.TotalBadCount > 0)
            .Select(kvp => new SymbolBadTickStats(
                Symbol: kvp.Key,
                TotalBadTicks: kvp.Value.TotalBadCount,
                LastBadTickTime: kvp.Value.LastBadTickTime
            ))
            .OrderByDescending(s => s.TotalBadTicks)
            .ToList();

        return new BadTickFilterStats(
            TotalTicksProcessed: Interlocked.Read(ref _totalTicksProcessed),
            TotalBadTicksDetected: Interlocked.Read(ref _totalBadTicksDetected),
            FilterRate: CalculateFilterRate(),
            SymbolStats: symbolStats
        );
    }

    private double CalculateFilterRate()
    {
        var total = Interlocked.Read(ref _totalTicksProcessed);
        var bad = Interlocked.Read(ref _totalBadTicksDetected);
        return total > 0 ? (double)bad / total * 100 : 0;
    }

    /// <summary>
    /// Gets the total count of bad ticks detected.
    /// </summary>
    public long TotalBadTicksDetected => Interlocked.Read(ref _totalBadTicksDetected);

    /// <summary>
    /// Manually sets a reference price for a symbol.
    /// Useful when initializing with known good data.
    /// </summary>
    public void SetReferencePrice(string symbol, decimal price)
    {
        var state = _symbolStates.GetOrAdd(symbol, _ => new TickFilterState());
        state.UpdateReference(price, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Updates the LULD (Limit Up/Limit Down) price band for a symbol.
    /// Called when LULD band updates are received from the exchange feed.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="lowerLimit">The lower price limit.</param>
    /// <param name="upperLimit">The upper price limit.</param>
    /// <param name="referencePrice">The reference price used to calculate the band.</param>
    /// <param name="expiresAt">When this band expires (optional; bands refresh every 5 minutes during regular hours).</param>
    public void UpdateLuldBand(string symbol, decimal lowerLimit, decimal upperLimit,
        decimal referencePrice, DateTimeOffset? expiresAt = null)
    {
        if (lowerLimit <= 0 || upperLimit <= 0 || lowerLimit >= upperLimit)
        {
            _log.Warning("Invalid LULD band for {Symbol}: lower={Lower}, upper={Upper}",
                symbol, lowerLimit, upperLimit);
            return;
        }

        _luldBands[symbol] = new LuldBand(lowerLimit, upperLimit, referencePrice,
            expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5));

        _log.Debug("LULD band updated for {Symbol}: [{Lower:F2}, {Upper:F2}] ref={Ref:F2}",
            symbol, lowerLimit, upperLimit, referencePrice);
    }

    /// <summary>
    /// Removes the LULD band for a symbol (e.g., after market close).
    /// </summary>
    public void ClearLuldBand(string symbol) => _luldBands.TryRemove(symbol, out _);

    /// <summary>
    /// Marks a symbol as halted (trading paused). Trades during halt are flagged.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="reason">Halt reason (for logging).</param>
    public void SetHalted(string symbol, string? reason = null)
    {
        _haltedSymbols[symbol] = true;
        _log.Information("Trading halt set for {Symbol}: {Reason}", symbol, reason ?? "unknown");
    }

    /// <summary>
    /// Removes the trading halt for a symbol (trading resumed).
    /// </summary>
    public void ClearHalt(string symbol)
    {
        if (_haltedSymbols.TryRemove(symbol, out _))
        {
            _log.Information("Trading halt cleared for {Symbol}", symbol);
        }
    }

    /// <summary>
    /// Gets whether a symbol is currently halted.
    /// </summary>
    public bool IsHalted(string symbol) => _haltedSymbols.ContainsKey(symbol);

    /// <summary>
    /// Gets the current LULD band for a symbol, if any.
    /// </summary>
    public LuldBand? GetLuldBand(string symbol) =>
        _luldBands.TryGetValue(symbol, out var band) ? band : null;

    private void CleanupOldStates(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var toRemove = _symbolStates
                .Where(kvp => kvp.Value.LastActivityTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var symbol in toRemove)
            {
                _symbolStates.TryRemove(symbol, out _);
            }

            if (toRemove.Count > 0)
            {
                _log.Debug("Cleaned up {Count} inactive symbol states from bad tick filter", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during bad tick filter state cleanup");
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
    /// Per-symbol state for tick filtering.
    /// </summary>
    private sealed class TickFilterState
    {
        private decimal _referencePrice;
        private long _totalBadCount;
        private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastBadTickTime;
        private DateTimeOffset _lastActivityTime;
        private bool _hasValidReference;

        public decimal ReferencePrice => _referencePrice;
        public bool HasValidReference => _hasValidReference;
        public long TotalBadCount => Interlocked.Read(ref _totalBadCount);
        public DateTimeOffset LastBadTickTime => _lastBadTickTime;
        public DateTimeOffset LastActivityTime => _lastActivityTime;

        public void UpdateReference(decimal price, DateTimeOffset time)
        {
            _referencePrice = price;
            _hasValidReference = true;
            _lastActivityTime = time;
        }

        public void IncrementBadCount()
        {
            Interlocked.Increment(ref _totalBadCount);
            _lastBadTickTime = DateTimeOffset.UtcNow;
            _lastActivityTime = _lastBadTickTime;
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
/// Configuration for bad tick filtering.
/// </summary>
public sealed record BadTickFilterConfig
{
    /// <summary>
    /// Maximum percentage deviation from reference price.
    /// Prices deviating more than this are flagged.
    /// Default is 50% (very lenient for volatile stocks).
    /// </summary>
    public double MaxDeviationPercent { get; init; } = 50.0;

    /// <summary>
    /// Minimum valid price. Prices below this are flagged.
    /// </summary>
    public decimal MinValidPrice { get; init; } = 0.0001m;

    /// <summary>
    /// Maximum valid price. Prices above this are flagged.
    /// </summary>
    public decimal MaxValidPrice { get; init; } = 1000000m;

    /// <summary>
    /// Maximum spread percentage for quotes.
    /// </summary>
    public double MaxSpreadPercent { get; init; } = 50.0;

    /// <summary>
    /// Whether to require positive size for trades.
    /// </summary>
    public bool RequirePositiveSize { get; init; } = true;

    /// <summary>
    /// Whether to enforce LULD (Limit Up/Limit Down) price bands.
    /// When enabled, trades outside the current LULD band are flagged as bad ticks.
    /// Bands must be set via <see cref="BadTickFilter.UpdateLuldBand"/>.
    /// </summary>
    public bool EnforceLuldBands { get; init; } = true;

    /// <summary>
    /// Whether to flag trades that arrive while a symbol is halted.
    /// Halts must be set via <see cref="BadTickFilter.SetHalted"/>.
    /// </summary>
    public bool EnforceTradingHalts { get; init; } = true;

    /// <summary>
    /// Minimum time between alerts for the same symbol in milliseconds.
    /// </summary>
    public int AlertCooldownMs { get; init; } = 5000;

    public static BadTickFilterConfig Default => new();

    /// <summary>
    /// Strict configuration for high-quality data requirements.
    /// </summary>
    public static BadTickFilterConfig Strict => new()
    {
        MaxDeviationPercent = 10.0,
        MinValidPrice = 0.01m,
        MaxValidPrice = 100000m,
        MaxSpreadPercent = 10.0,
        AlertCooldownMs = 1000
    };

    /// <summary>
    /// Lenient configuration for volatile markets.
    /// </summary>
    public static BadTickFilterConfig Lenient => new()
    {
        MaxDeviationPercent = 100.0,
        MinValidPrice = 0.0001m,
        MaxValidPrice = 10000000m,
        MaxSpreadPercent = 100.0,
        AlertCooldownMs = 30000
    };
}

/// <summary>
/// Reasons why a tick was flagged as bad.
/// </summary>
public enum BadTickReason : byte
{
    NegativeOrZeroPrice,
    PriceBelowMinimum,
    PriceAboveMaximum,
    PlaceholderPrice,
    InvalidSize,
    ExtremeDeviation,
    CrossedMarket,
    AbnormalSpread,
    OutsideLuldBand,
    TradingHalted
}

/// <summary>
/// Alert for a bad tick condition.
/// </summary>
public readonly record struct BadTickAlert(
    string Symbol,
    decimal Price,
    decimal Size,
    DateTimeOffset Timestamp,
    BadTickReason[] Reasons,
    decimal ReferencePrice,
    string? Provider,
    DateTimeOffset DetectedAt,
    long BadTickCountInSession
);

/// <summary>
/// Statistics for bad tick filtering.
/// </summary>
public readonly record struct BadTickFilterStats(
    long TotalTicksProcessed,
    long TotalBadTicksDetected,
    double FilterRate,
    IReadOnlyList<SymbolBadTickStats> SymbolStats
);

/// <summary>
/// Per-symbol bad tick statistics.
/// </summary>
public readonly record struct SymbolBadTickStats(
    string Symbol,
    long TotalBadTicks,
    DateTimeOffset LastBadTickTime
);

/// <summary>
/// Represents a LULD (Limit Up/Limit Down) price band for a symbol.
/// Under SEC Rule 201 / Reg NMS Plan, trades outside these bands should be rejected.
/// Bands are recalculated every 5 minutes during regular trading hours using
/// the average reference price of the prior 5-minute period.
/// </summary>
/// <remarks>
/// Price band percentages vary by tier:
/// - Tier 1 (S&amp;P 500, Russell 1000, select ETPs): 5% for prices &gt;$3, lesser of $0.15 or 75% for prices &lt;=$3
/// - Tier 2 (other NMS stocks): 10% for prices &gt;$3, lesser of $0.15 or 75% for prices &lt;=$3
/// - During first/last 15 minutes of trading: bands are doubled
/// </remarks>
public readonly record struct LuldBand(
    decimal LowerLimit,
    decimal UpperLimit,
    decimal ReferencePrice,
    DateTimeOffset ExpiresAt
);
