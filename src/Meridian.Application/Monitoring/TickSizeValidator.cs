using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Validates that prices conform to expected tick size increments.
/// Part of the data quality framework (DQ-13) to detect prices
/// that don't match the exchange's minimum price increment rules.
/// </summary>
/// <remarks>
/// Different securities have different tick sizes:
/// - Most US equities: $0.01 for prices &gt;= $1.00, $0.0001 for prices &lt; $1.00
/// - Futures/Forex: Various tick sizes depending on contract
/// This validator detects when prices fall on unexpected increments.
/// </remarks>
public sealed class TickSizeValidator : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<TickSizeValidator>();
    private readonly ConcurrentDictionary<string, TickSizeState> _symbolStates = new();
    private readonly ConcurrentDictionary<string, decimal> _customTickSizes = new();
    private readonly TickSizeValidatorConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Counters
    private long _totalViolationsDetected;
    private long _totalPricesProcessed;

    /// <summary>
    /// Event raised when a tick size violation is detected.
    /// </summary>
    public event Action<TickSizeViolationAlert>? OnViolation;

    public TickSizeValidator(TickSizeValidatorConfig? config = null)
    {
        _config = config ?? TickSizeValidatorConfig.Default;
        _cleanupTimer = new Timer(CleanupOldStates, null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _log.Information(
            "TickSizeValidator initialized with default tick size ${DefaultTickSize:F4}, sub-dollar ${SubDollarTickSize:F6}",
            _config.DefaultTickSize, _config.SubDollarTickSize);
    }

    /// <summary>
    /// Sets a custom tick size for a specific symbol.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="tickSize">The minimum tick size for this symbol.</param>
    public void SetTickSize(string symbol, decimal tickSize)
    {
        if (tickSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive.");

        _customTickSizes[symbol.ToUpperInvariant()] = tickSize;
        _log.Debug("Set custom tick size for {Symbol}: ${TickSize:F6}", symbol, tickSize);
    }

    /// <summary>
    /// Validates a trade price against expected tick size.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="price">The trade price.</param>
    /// <param name="provider">The data provider (optional).</param>
    /// <returns>True if the price violates tick size rules.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ValidateTrade(string symbol, decimal price, string? provider = null)
    {
        return ValidatePrice(symbol, price, TickSizePriceType.Trade, provider);
    }

    /// <summary>
    /// Validates quote prices against expected tick size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ValidateQuote(string symbol, decimal bidPrice, decimal askPrice, string? provider = null)
    {
        var bidViolation = ValidatePrice(symbol, bidPrice, TickSizePriceType.Bid, provider);
        var askViolation = ValidatePrice(symbol, askPrice, TickSizePriceType.Ask, provider);
        return bidViolation || askViolation;
    }

    /// <summary>
    /// Validates a bar's OHLC prices against expected tick size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ValidateBar(string symbol, decimal open, decimal high, decimal low, decimal close, string? provider = null)
    {
        var violations = ValidatePrice(symbol, open, TickSizePriceType.Open, provider);
        violations |= ValidatePrice(symbol, high, TickSizePriceType.High, provider);
        violations |= ValidatePrice(symbol, low, TickSizePriceType.Low, provider);
        violations |= ValidatePrice(symbol, close, TickSizePriceType.Close, provider);
        return violations;
    }

    /// <summary>
    /// Core tick size validation logic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ValidatePrice(string symbol, decimal price, TickSizePriceType priceType, string? provider)
    {
        if (_isDisposed || price <= 0)
            return false;

        Interlocked.Increment(ref _totalPricesProcessed);

        // Get the expected tick size for this symbol/price
        var tickSize = GetTickSize(symbol, price);

        // Check if price is on a valid tick
        var remainder = price % tickSize;
        if (remainder == 0)
        {
            return false; // Valid tick
        }

        // Allow small floating point tolerance
        var tolerance = tickSize * 0.001m; // 0.1% tolerance
        if (remainder <= tolerance || (tickSize - remainder) <= tolerance)
        {
            return false; // Within tolerance
        }

        // This is a tick size violation
        Interlocked.Increment(ref _totalViolationsDetected);

        var state = _symbolStates.GetOrAdd(symbol, _ => new TickSizeState());
        state.IncrementViolationCount();

        var now = DateTimeOffset.UtcNow;

        // Only alert if cooldown has passed
        if (state.CanAlert(now, _config.AlertCooldownMs))
        {
            var nearestValidLow = price - remainder;
            var nearestValidHigh = nearestValidLow + tickSize;

            var alert = new TickSizeViolationAlert(
                Symbol: symbol,
                Price: price,
                PriceType: priceType,
                ExpectedTickSize: tickSize,
                Remainder: remainder,
                NearestValidPriceLow: nearestValidLow,
                NearestValidPriceHigh: nearestValidHigh,
                Provider: provider,
                Timestamp: now,
                ViolationCount: state.TotalViolationCount
            );

            _log.Warning(
                "TICK SIZE VIOLATION: {Symbol} {PriceType}={Price:F6} not on ${TickSize:F6} increment (remainder ${Remainder:F6})",
                symbol, priceType, price, tickSize, remainder);

            state.RecordAlert(now);

            try
            {
                OnViolation?.Invoke(alert);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in tick size violation event handler for {Symbol}", symbol);
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the tick size for a symbol at a given price level.
    /// </summary>
    private decimal GetTickSize(string symbol, decimal price)
    {
        // Check for custom tick size first
        if (_customTickSizes.TryGetValue(symbol.ToUpperInvariant(), out var customTickSize))
        {
            return customTickSize;
        }

        // Use standard US equity tick size rules
        if (_config.UseSubDollarTicks && price < 1.00m)
        {
            return _config.SubDollarTickSize;
        }

        return _config.DefaultTickSize;
    }

    /// <summary>
    /// Gets statistics about tick size validation.
    /// </summary>
    public TickSizeValidatorStats GetStats()
    {
        var symbolStats = _symbolStates
            .Where(kvp => kvp.Value.TotalViolationCount > 0)
            .Select(kvp => new SymbolTickSizeStats(
                Symbol: kvp.Key,
                TotalViolations: kvp.Value.TotalViolationCount,
                LastViolationTime: kvp.Value.LastViolationTime
            ))
            .OrderByDescending(s => s.TotalViolations)
            .ToList();

        return new TickSizeValidatorStats(
            TotalPricesProcessed: Interlocked.Read(ref _totalPricesProcessed),
            TotalViolationsDetected: Interlocked.Read(ref _totalViolationsDetected),
            SymbolStats: symbolStats
        );
    }

    /// <summary>
    /// Gets the total count of tick size violations detected.
    /// </summary>
    public long TotalViolationsDetected => Interlocked.Read(ref _totalViolationsDetected);

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
                _log.Debug("Cleaned up {Count} inactive symbol states from tick size validator", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during tick size validator state cleanup");
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
    /// Per-symbol state for tick size tracking.
    /// </summary>
    private sealed class TickSizeState
    {
        private long _totalViolationCount;
        private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastViolationTime;
        private DateTimeOffset _lastActivityTime;

        public long TotalViolationCount => Interlocked.Read(ref _totalViolationCount);
        public DateTimeOffset LastViolationTime => _lastViolationTime;
        public DateTimeOffset LastActivityTime => _lastActivityTime;

        public void IncrementViolationCount()
        {
            Interlocked.Increment(ref _totalViolationCount);
            _lastViolationTime = DateTimeOffset.UtcNow;
            _lastActivityTime = _lastViolationTime;
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
/// Configuration for tick size validation.
/// </summary>
public sealed record TickSizeValidatorConfig
{
    /// <summary>
    /// Default tick size for prices >= $1.00.
    /// Standard US equity tick size is $0.01.
    /// </summary>
    public decimal DefaultTickSize { get; init; } = 0.01m;

    /// <summary>
    /// Tick size for sub-dollar prices (&lt; $1.00).
    /// Standard US equity sub-dollar tick size is $0.0001.
    /// </summary>
    public decimal SubDollarTickSize { get; init; } = 0.0001m;

    /// <summary>
    /// Whether to use different tick size for sub-dollar prices.
    /// </summary>
    public bool UseSubDollarTicks { get; init; } = true;

    /// <summary>
    /// Minimum time between alerts for the same symbol in milliseconds.
    /// </summary>
    public int AlertCooldownMs { get; init; } = 10000;

    public static TickSizeValidatorConfig Default => new();

    /// <summary>
    /// Configuration for futures/forex with custom tick sizes.
    /// </summary>
    public static TickSizeValidatorConfig FuturesForex => new()
    {
        DefaultTickSize = 0.0001m,
        UseSubDollarTicks = false,
        AlertCooldownMs = 5000
    };

    /// <summary>
    /// Lenient configuration that allows finer precision.
    /// </summary>
    public static TickSizeValidatorConfig Lenient => new()
    {
        DefaultTickSize = 0.001m,
        SubDollarTickSize = 0.00001m,
        AlertCooldownMs = 30000
    };
}

/// <summary>
/// Type of price being validated.
/// </summary>
public enum TickSizePriceType : byte
{
    Trade,
    Bid,
    Ask,
    Open,
    High,
    Low,
    Close,
    Mid
}

/// <summary>
/// Alert for a tick size violation.
/// </summary>
public readonly record struct TickSizeViolationAlert(
    string Symbol,
    decimal Price,
    TickSizePriceType PriceType,
    decimal ExpectedTickSize,
    decimal Remainder,
    decimal NearestValidPriceLow,
    decimal NearestValidPriceHigh,
    string? Provider,
    DateTimeOffset Timestamp,
    long ViolationCount
);

/// <summary>
/// Statistics for tick size validation.
/// </summary>
public readonly record struct TickSizeValidatorStats(
    long TotalPricesProcessed,
    long TotalViolationsDetected,
    IReadOnlyList<SymbolTickSizeStats> SymbolStats
);

/// <summary>
/// Per-symbol tick size statistics.
/// </summary>
public readonly record struct SymbolTickSizeStats(
    string Symbol,
    long TotalViolations,
    DateTimeOffset LastViolationTime
);
