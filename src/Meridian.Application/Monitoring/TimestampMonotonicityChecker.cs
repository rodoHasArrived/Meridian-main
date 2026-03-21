using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Monitors event timestamps for monotonicity violations (out-of-order events).
/// Detects when events arrive with timestamps earlier than previously received events,
/// which can indicate data quality issues, clock drift, or provider problems.
/// </summary>
public sealed class TimestampMonotonicityChecker : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<TimestampMonotonicityChecker>();
    private readonly ConcurrentDictionary<string, SymbolTimestampState> _symbolStates = new();
    private readonly TimestampMonotonicityConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Global counters
    private long _totalEventsChecked;
    private long _totalViolations;
    private long _totalWarnings;

    /// <summary>
    /// Event raised when a monotonicity violation is detected (timestamp going backwards).
    /// </summary>
    public event Action<MonotonicityViolation>? OnViolation;

    /// <summary>
    /// Event raised when a significant time gap is detected between events.
    /// </summary>
    public event Action<TimestampGapAlert>? OnTimeGap;

    public TimestampMonotonicityChecker(TimestampMonotonicityConfig? config = null)
    {
        _config = config ?? TimestampMonotonicityConfig.Default;
        _cleanupTimer = new Timer(CleanupOldStates, null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _log.Information("TimestampMonotonicityChecker initialized with tolerance {ToleranceMs}ms, gap threshold {GapSeconds}s",
            _config.ToleranceMs, _config.TimeGapThresholdSeconds);
    }

    /// <summary>
    /// Checks an event timestamp for monotonicity. Call this for every event received.
    /// </summary>
    /// <param name="symbol">The symbol ticker.</param>
    /// <param name="eventType">Type of event (trade, quote, depth, etc.).</param>
    /// <param name="timestamp">The event timestamp from the data provider.</param>
    /// <returns>True if a violation or warning was detected.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckTimestamp(string symbol, string eventType, DateTimeOffset timestamp)
    {
        if (_isDisposed)
            return false;

        Interlocked.Increment(ref _totalEventsChecked);

        var key = $"{symbol}:{eventType}";
        var state = _symbolStates.GetOrAdd(key, _ => new SymbolTimestampState());
        var now = DateTimeOffset.UtcNow;

        // Get the previous timestamp
        var lastTimestamp = state.LastEventTimestamp;

        // Record this event
        state.RecordEvent(timestamp);

        // First event for this symbol/type - nothing to compare
        if (lastTimestamp == DateTimeOffset.MinValue)
        {
            return false;
        }

        // Calculate the time difference
        var timeDelta = (timestamp - lastTimestamp).TotalMilliseconds;

        // Check for backwards timestamp (violation)
        if (timeDelta < -_config.ToleranceMs)
        {
            Interlocked.Increment(ref _totalViolations);
            state.IncrementViolationCount();

            var violation = new MonotonicityViolation(
                Symbol: symbol,
                EventType: eventType,
                CurrentTimestamp: timestamp,
                PreviousTimestamp: lastTimestamp,
                DeltaMs: timeDelta,
                ConsecutiveViolations: state.ConsecutiveViolations,
                TotalViolations: state.TotalViolations,
                DetectedAt: now
            );

            // Only log if cooldown has passed
            if (state.CanAlert(now, _config.AlertCooldownMs))
            {
                _log.Warning("MONOTONICITY VIOLATION: {Symbol}:{EventType} timestamp went backwards by {DeltaMs:F2}ms " +
                    "(current: {Current}, previous: {Previous})",
                    symbol, eventType, -timeDelta, timestamp.ToString("O"), lastTimestamp.ToString("O"));

                state.RecordAlert(now);

                try
                {
                    OnViolation?.Invoke(violation);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in monotonicity violation event handler");
                }
            }

            return true;
        }

        // Reset consecutive violations on normal event
        if (state.ConsecutiveViolations > 0 && timeDelta >= 0)
        {
            state.ResetConsecutiveViolations();
        }

        // Check for large time gaps (potential data loss)
        if (_config.DetectTimeGaps && timeDelta > _config.TimeGapThresholdSeconds * 1000)
        {
            Interlocked.Increment(ref _totalWarnings);
            state.IncrementGapCount();

            var gapAlert = new TimestampGapAlert(
                Symbol: symbol,
                EventType: eventType,
                GapStartTimestamp: lastTimestamp,
                GapEndTimestamp: timestamp,
                GapDurationSeconds: timeDelta / 1000.0,
                TotalGaps: state.TotalGaps,
                DetectedAt: now
            );

            if (state.CanAlertGap(now, _config.GapAlertCooldownMs))
            {
                _log.Information("TIME GAP detected: {Symbol}:{EventType} - {GapSeconds:F2}s gap between events",
                    symbol, eventType, timeDelta / 1000.0);

                state.RecordGapAlert(now);

                try
                {
                    OnTimeGap?.Invoke(gapAlert);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in time gap event handler");
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks an event timestamp using a Unix timestamp in milliseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckTimestamp(string symbol, string eventType, long timestampMs)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
        return CheckTimestamp(symbol, eventType, timestamp);
    }

    /// <summary>
    /// Gets statistics about monotonicity checking.
    /// </summary>
    public MonotonicityStats GetStats()
    {
        var symbolStats = new List<SymbolMonotonicityStats>();

        foreach (var kvp in _symbolStates)
        {
            var parts = kvp.Key.Split(':');
            var symbol = parts[0];
            var eventType = parts.Length > 1 ? parts[1] : "unknown";

            if (kvp.Value.TotalViolations > 0 || kvp.Value.TotalGaps > 0)
            {
                symbolStats.Add(new SymbolMonotonicityStats(
                    Symbol: symbol,
                    EventType: eventType,
                    TotalEvents: kvp.Value.TotalEvents,
                    TotalViolations: kvp.Value.TotalViolations,
                    TotalGaps: kvp.Value.TotalGaps,
                    LastViolationTime: kvp.Value.LastViolationTime,
                    LastEventTimestamp: kvp.Value.LastEventTimestamp
                ));
            }
        }

        return new MonotonicityStats(
            TotalEventsChecked: Interlocked.Read(ref _totalEventsChecked),
            TotalViolations: Interlocked.Read(ref _totalViolations),
            TotalGaps: Interlocked.Read(ref _totalWarnings),
            SymbolStats: symbolStats.OrderByDescending(s => s.TotalViolations).ToList()
        );
    }

    /// <summary>
    /// Gets the total number of violations detected.
    /// </summary>
    public long TotalViolations => Interlocked.Read(ref _totalViolations);

    /// <summary>
    /// Gets the total number of time gaps detected.
    /// </summary>
    public long TotalGaps => Interlocked.Read(ref _totalWarnings);

    /// <summary>
    /// Gets the total number of events checked.
    /// </summary>
    public long TotalEventsChecked => Interlocked.Read(ref _totalEventsChecked);

    /// <summary>
    /// Gets the violation rate as a percentage.
    /// </summary>
    public double ViolationRate
    {
        get
        {
            var total = Interlocked.Read(ref _totalEventsChecked);
            if (total == 0)
                return 0;
            return (double)Interlocked.Read(ref _totalViolations) / total * 100;
        }
    }

    /// <summary>
    /// Gets symbols with recent violations.
    /// </summary>
    public IReadOnlyList<string> GetSymbolsWithViolations(int minutesBack = 60)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-minutesBack);
        var symbols = new HashSet<string>();

        foreach (var kvp in _symbolStates)
        {
            if (kvp.Value.LastViolationTime > cutoff)
            {
                var symbol = kvp.Key.Split(':')[0];
                symbols.Add(symbol);
            }
        }

        return symbols.ToList();
    }

    /// <summary>
    /// Resets statistics for all symbols.
    /// </summary>
    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalEventsChecked, 0);
        Interlocked.Exchange(ref _totalViolations, 0);
        Interlocked.Exchange(ref _totalWarnings, 0);
        _symbolStates.Clear();

        _log.Information("TimestampMonotonicityChecker statistics reset");
    }

    private void CleanupOldStates(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var toRemove = new List<string>();

            foreach (var kvp in _symbolStates)
            {
                if (kvp.Value.LastEventTime < cutoff)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _symbolStates.TryRemove(key, out _);
            }

            if (toRemove.Count > 0)
            {
                _log.Debug("Cleaned up {Count} inactive symbol states from monotonicity checker", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during monotonicity checker state cleanup");
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
    /// Per-symbol timestamp tracking state.
    /// </summary>
    private sealed class SymbolTimestampState
    {
        private long _lastEventTimestampTicks = DateTimeOffset.MinValue.UtcTicks;
        private long _lastEventTimeTicks = DateTimeOffset.MinValue.UtcTicks;
        private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastGapAlertTime = DateTimeOffset.MinValue;
        private long _lastViolationTimeTicks = DateTimeOffset.MinValue.UtcTicks;
        private long _totalEvents;
        private long _totalViolations;
        private long _totalGaps;
        private int _consecutiveViolations;

        public DateTimeOffset LastEventTimestamp => new DateTimeOffset(new DateTime(Volatile.Read(ref _lastEventTimestampTicks), DateTimeKind.Utc));
        public DateTimeOffset LastEventTime => new DateTimeOffset(new DateTime(Volatile.Read(ref _lastEventTimeTicks), DateTimeKind.Utc));
        public DateTimeOffset LastViolationTime => new DateTimeOffset(new DateTime(Volatile.Read(ref _lastViolationTimeTicks), DateTimeKind.Utc));
        public long TotalEvents => Interlocked.Read(ref _totalEvents);
        public long TotalViolations => Interlocked.Read(ref _totalViolations);
        public long TotalGaps => Interlocked.Read(ref _totalGaps);
        public int ConsecutiveViolations => Volatile.Read(ref _consecutiveViolations);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordEvent(DateTimeOffset timestamp)
        {
            Volatile.Write(ref _lastEventTimestampTicks, timestamp.UtcTicks);
            Volatile.Write(ref _lastEventTimeTicks, DateTimeOffset.UtcNow.UtcTicks);
            Interlocked.Increment(ref _totalEvents);
        }

        public void IncrementViolationCount()
        {
            Interlocked.Increment(ref _totalViolations);
            Interlocked.Increment(ref _consecutiveViolations);
            Volatile.Write(ref _lastViolationTimeTicks, DateTimeOffset.UtcNow.UtcTicks);
        }

        public void IncrementGapCount()
        {
            Interlocked.Increment(ref _totalGaps);
        }

        public void ResetConsecutiveViolations()
        {
            Interlocked.Exchange(ref _consecutiveViolations, 0);
        }

        public bool CanAlert(DateTimeOffset now, int cooldownMs)
        {
            return (now - _lastAlertTime).TotalMilliseconds >= cooldownMs;
        }

        public bool CanAlertGap(DateTimeOffset now, int cooldownMs)
        {
            return (now - _lastGapAlertTime).TotalMilliseconds >= cooldownMs;
        }

        public void RecordAlert(DateTimeOffset time)
        {
            _lastAlertTime = time;
        }

        public void RecordGapAlert(DateTimeOffset time)
        {
            _lastGapAlertTime = time;
        }
    }
}

/// <summary>
/// Configuration for timestamp monotonicity checking.
/// </summary>
public sealed record TimestampMonotonicityConfig
{
    /// <summary>
    /// Tolerance in milliseconds for timestamp variations.
    /// Timestamps within this tolerance of the previous timestamp are not flagged.
    /// </summary>
    public int ToleranceMs { get; init; } = 100;

    /// <summary>
    /// Minimum time between alerts for the same symbol/event type in milliseconds.
    /// </summary>
    public int AlertCooldownMs { get; init; } = 5000;

    /// <summary>
    /// Whether to detect large time gaps between events.
    /// </summary>
    public bool DetectTimeGaps { get; init; } = true;

    /// <summary>
    /// Threshold in seconds for detecting time gaps between events.
    /// </summary>
    public int TimeGapThresholdSeconds { get; init; } = 60;

    /// <summary>
    /// Minimum time between gap alerts in milliseconds.
    /// </summary>
    public int GapAlertCooldownMs { get; init; } = 30000;

    public static TimestampMonotonicityConfig Default => new();
}

/// <summary>
/// Alert for a timestamp monotonicity violation (event arrived with earlier timestamp).
/// </summary>
public readonly record struct MonotonicityViolation(
    string Symbol,
    string EventType,
    DateTimeOffset CurrentTimestamp,
    DateTimeOffset PreviousTimestamp,
    double DeltaMs,
    int ConsecutiveViolations,
    long TotalViolations,
    DateTimeOffset DetectedAt
);

/// <summary>
/// Alert for a large time gap between events.
/// </summary>
public readonly record struct TimestampGapAlert(
    string Symbol,
    string EventType,
    DateTimeOffset GapStartTimestamp,
    DateTimeOffset GapEndTimestamp,
    double GapDurationSeconds,
    long TotalGaps,
    DateTimeOffset DetectedAt
);

/// <summary>
/// Overall statistics for monotonicity checking.
/// </summary>
public readonly record struct MonotonicityStats(
    long TotalEventsChecked,
    long TotalViolations,
    long TotalGaps,
    IReadOnlyList<SymbolMonotonicityStats> SymbolStats
);

/// <summary>
/// Per-symbol monotonicity statistics.
/// </summary>
public readonly record struct SymbolMonotonicityStats(
    string Symbol,
    string EventType,
    long TotalEvents,
    long TotalViolations,
    long TotalGaps,
    DateTimeOffset LastViolationTime,
    DateTimeOffset LastEventTimestamp
);
