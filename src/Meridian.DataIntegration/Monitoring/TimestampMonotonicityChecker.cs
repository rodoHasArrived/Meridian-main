using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.DataIntegration.Monitoring;

/// <summary>
/// Monitors event timestamps for monotonicity violations (out-of-order events).
/// Detects when events arrive with timestamps earlier than previously received events,
/// which can indicate data quality issues, clock drift, or provider problems.
/// </summary>
public sealed class TimestampMonotonicityChecker : IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StateRetentionWindow = TimeSpan.FromHours(24);

    private readonly ILogger _log = LoggingSetup.ForContext<TimestampMonotonicityChecker>();
    private readonly TimestampMonotonicityConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _cleanupTimer;
    private readonly TimestampMonotonicityCheckerTestHooks? _testHooks;
    private readonly object _lifecycleSync = new();
    private readonly object _callbackSync = new();
    // Callback reentrancy is stack/thread-local; it must not flow into unrelated child tasks.
    private readonly ThreadLocal<int> _operationDepth = new();
    private TimestampGeneration _generation = new();
    private int _activeOperations;
    private bool _disposeRequested;
    private bool _timerStopped;
    private bool _disposeCompleted;

    /// <summary>
    /// Event raised when a monotonicity violation is detected (timestamp going backwards).
    /// </summary>
    public event Action<MonotonicityViolation>? OnViolation;

    /// <summary>
    /// Event raised when a significant time gap is detected between events.
    /// </summary>
    public event Action<TimestampGapAlert>? OnTimeGap;

    public TimestampMonotonicityChecker(TimestampMonotonicityConfig? config = null)
        : this(config, TimeProvider.System)
    {
    }

    public TimestampMonotonicityChecker(
        TimestampMonotonicityConfig? config,
        TimeProvider timeProvider)
        : this(config, timeProvider, testHooks: null)
    {
    }

    internal TimestampMonotonicityChecker(
        TimestampMonotonicityConfig? config,
        TimeProvider timeProvider,
        TimestampMonotonicityCheckerTestHooks? testHooks)
    {
        _config = ValidateConfig(config ?? TimestampMonotonicityConfig.Default);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHooks = testHooks;
        _cleanupTimer = _timeProvider.CreateTimer(
            static state => ((TimestampMonotonicityChecker)state!).RunCleanup(),
            this,
            CleanupInterval,
            CleanupInterval);

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
        if (!TryEnterOperation())
            return false;

        try
        {
            var normalizedSymbol = NormalizeRequiredIdentity(symbol, nameof(symbol));
            var normalizedEventType = NormalizeRequiredIdentity(eventType, nameof(eventType));
            var key = new SymbolEventKey(normalizedSymbol, normalizedEventType);
            var generation = Volatile.Read(ref _generation);
            var now = _timeProvider.GetUtcNow();
            Interlocked.Increment(ref generation.TotalEventsChecked);

            TimestampObservation observation;
            while (true)
            {
                var state = generation.SymbolStates.GetOrAdd(
                    key,
                    _ => new SymbolTimestampState(symbol.Trim(), eventType.Trim()));
                if (state.TryObserve(timestamp, now, _config, out observation))
                    break;

                TryRemoveExact(generation.SymbolStates, key, state);
            }

            _testHooks?.ObservationCommittedBeforePublish?.Invoke();

            // First event for this symbol/type - nothing to compare
            if (observation.IsFirstEvent)
            {
                return false;
            }

            // Check for backwards timestamp (violation)
            if (observation.IsViolation)
            {
                Interlocked.Increment(ref generation.TotalViolations);

                var violation = new MonotonicityViolation(
                    Symbol: symbol,
                    EventType: eventType,
                    CurrentTimestamp: timestamp,
                    PreviousTimestamp: observation.PreviousTimestamp,
                    DeltaMs: observation.DeltaMs,
                    ConsecutiveViolations: observation.ConsecutiveViolations,
                    TotalViolations: observation.TotalViolations,
                    DetectedAt: now
                );

                // Only log if cooldown has passed
                if (observation.ShouldAlert)
                {
                    PublishViolationIfCurrentGeneration(generation, violation);
                }

                return true;
            }

            // Check for large time gaps (potential data loss)
            if (observation.IsGap)
            {
                Interlocked.Increment(ref generation.TotalGaps);

                var gapAlert = new TimestampGapAlert(
                    Symbol: symbol,
                    EventType: eventType,
                    GapStartTimestamp: observation.PreviousTimestamp,
                    GapEndTimestamp: timestamp,
                    GapDurationSeconds: observation.DeltaMs / 1000.0,
                    TotalGaps: observation.TotalGaps,
                    DetectedAt: now
                );

                if (observation.ShouldAlert)
                {
                    PublishGapIfCurrentGeneration(generation, gapAlert);
                }

                return true;
            }

            return false;
        }
        finally
        {
            ExitOperation();
        }
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
    /// Gets statistics about monotonicity checking. Overall counters cover the current lifetime
    /// generation (construction or the most recent reset); retained-state counters and symbol
    /// details cover only states that have not aged out during cleanup.
    /// </summary>
    public MonotonicityStats GetStats()
    {
        var generation = Volatile.Read(ref _generation);
        var symbolStats = new List<SymbolMonotonicityStats>();
        long retainedEvents = 0;
        long retainedViolations = 0;
        long retainedGaps = 0;

        foreach (var kvp in generation.SymbolStates)
        {
            var snapshot = kvp.Value.GetSnapshot();
            retainedEvents += snapshot.TotalEvents;
            retainedViolations += snapshot.TotalViolations;
            retainedGaps += snapshot.TotalGaps;

            if (snapshot.TotalViolations > 0 || snapshot.TotalGaps > 0)
            {
                symbolStats.Add(new SymbolMonotonicityStats(
                    Symbol: snapshot.Symbol,
                    EventType: snapshot.EventType,
                    TotalEvents: snapshot.TotalEvents,
                    TotalViolations: snapshot.TotalViolations,
                    TotalGaps: snapshot.TotalGaps,
                    LastViolationTime: snapshot.LastViolationTime,
                    LastEventTimestamp: snapshot.LastEventTimestamp
                ));
            }
        }

        return new MonotonicityStats(
            TotalEventsChecked: Interlocked.Read(ref generation.TotalEventsChecked),
            TotalViolations: Interlocked.Read(ref generation.TotalViolations),
            TotalGaps: Interlocked.Read(ref generation.TotalGaps),
            SymbolStats: symbolStats.OrderByDescending(s => s.TotalViolations).ToList())
        {
            RetainedStateEvents = retainedEvents,
            RetainedStateViolations = retainedViolations,
            RetainedStateGaps = retainedGaps
        };
    }

    /// <summary>
    /// Gets the total number of violations detected.
    /// </summary>
    public long TotalViolations
    {
        get
        {
            var generation = Volatile.Read(ref _generation);
            return Interlocked.Read(ref generation.TotalViolations);
        }
    }

    /// <summary>
    /// Gets the total number of time gaps detected.
    /// </summary>
    public long TotalGaps
    {
        get
        {
            var generation = Volatile.Read(ref _generation);
            return Interlocked.Read(ref generation.TotalGaps);
        }
    }

    /// <summary>
    /// Gets the total number of events checked.
    /// </summary>
    public long TotalEventsChecked
    {
        get
        {
            var generation = Volatile.Read(ref _generation);
            return Interlocked.Read(ref generation.TotalEventsChecked);
        }
    }

    /// <summary>
    /// Gets the violation rate as a percentage.
    /// </summary>
    public double ViolationRate
    {
        get
        {
            var generation = Volatile.Read(ref _generation);
            var total = Interlocked.Read(ref generation.TotalEventsChecked);
            if (total == 0)
                return 0;
            return (double)Interlocked.Read(ref generation.TotalViolations) / total * 100;
        }
    }

    /// <summary>
    /// Gets symbols with recent violations.
    /// </summary>
    public IReadOnlyList<string> GetSymbolsWithViolations(int minutesBack = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBack);
        var cutoff = SubtractMinutesClamped(_timeProvider.GetUtcNow(), minutesBack);
        var generation = Volatile.Read(ref _generation);
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in generation.SymbolStates)
        {
            var snapshot = kvp.Value.GetSnapshot();
            if (snapshot.LastViolationTime > cutoff)
            {
                symbols.Add(snapshot.Symbol);
            }
        }

        return symbols.ToList();
    }

    /// <summary>
    /// Resets statistics for all symbols.
    /// </summary>
    public void ResetStats()
    {
        if (!TryEnterOperation())
            return;

        try
        {
            // Close the old generation before waiting for callback publication. Checks committed
            // to it but not yet published observe the replacement and suppress their stale alert.
            Interlocked.Exchange(ref _generation, new TimestampGeneration());

            // External resets wait for a callback already in progress. A callback can reset
            // reentrantly because Monitor locks are reentrant; dispatch checks the generation
            // between subscribers so no later subscriber sees the retired alert.
            lock (_callbackSync)
            {
            }

            _log.Information("TimestampMonotonicityChecker statistics reset");
        }
        finally
        {
            ExitOperation();
        }
    }

    private static bool TryRemoveExact(
        ConcurrentDictionary<SymbolEventKey, SymbolTimestampState> states,
        SymbolEventKey key,
        SymbolTimestampState state)
    {
        return ((ICollection<KeyValuePair<SymbolEventKey, SymbolTimestampState>>)states)
            .Remove(new KeyValuePair<SymbolEventKey, SymbolTimestampState>(key, state));
    }

    private void PublishViolationIfCurrentGeneration(
        TimestampGeneration generation,
        MonotonicityViolation violation)
    {
        lock (_callbackSync)
        {
            if (!ReferenceEquals(generation, Volatile.Read(ref _generation)))
                return;

            _log.Warning("MONOTONICITY VIOLATION: {Symbol}:{EventType} timestamp went backwards by {DeltaMs:F2}ms " +
                "(current: {Current}, previous: {Previous})",
                violation.Symbol,
                violation.EventType,
                -violation.DeltaMs,
                violation.CurrentTimestamp.ToString("O"),
                violation.PreviousTimestamp.ToString("O"));

            var handlers = OnViolation;
            if (handlers is null)
                return;

            foreach (Action<MonotonicityViolation> handler in handlers.GetInvocationList())
            {
                if (!ReferenceEquals(generation, Volatile.Read(ref _generation)))
                    break;

                try
                {
                    handler(violation);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in monotonicity violation event handler");
                    break;
                }
            }
        }
    }

    private void PublishGapIfCurrentGeneration(
        TimestampGeneration generation,
        TimestampGapAlert gapAlert)
    {
        lock (_callbackSync)
        {
            if (!ReferenceEquals(generation, Volatile.Read(ref _generation)))
                return;

            _log.Information("TIME GAP detected: {Symbol}:{EventType} - {GapSeconds:F2}s gap between events",
                gapAlert.Symbol,
                gapAlert.EventType,
                gapAlert.GapDurationSeconds);

            var handlers = OnTimeGap;
            if (handlers is null)
                return;

            foreach (Action<TimestampGapAlert> handler in handlers.GetInvocationList())
            {
                if (!ReferenceEquals(generation, Volatile.Read(ref _generation)))
                    break;

                try
                {
                    handler(gapAlert);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in time gap event handler");
                    break;
                }
            }
        }
    }

    internal void RunCleanup()
    {
        if (!TryEnterOperation())
            return;

        try
        {
            var cutoff = SubtractClamped(_timeProvider.GetUtcNow(), StateRetentionWindow);
            var generation = Volatile.Read(ref _generation);
            var removedCount = 0;

            foreach (var kvp in generation.SymbolStates)
            {
                if (!kvp.Value.RetireIfInactive(cutoff))
                    continue;

                _testHooks?.StateRetiredBeforeRemoval?.Invoke();
                if (TryRemoveExact(generation.SymbolStates, kvp.Key, kvp.Value))
                {
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _log.Debug("Cleaned up {Count} inactive symbol states from monotonicity checker", removedCount);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during monotonicity checker state cleanup");
        }
        finally
        {
            ExitOperation();
        }
    }

    public void Dispose()
    {
        var stopTimer = false;
        lock (_lifecycleSync)
        {
            if (!_disposeRequested)
            {
                _disposeRequested = true;
                stopTimer = true;
            }
        }

        if (stopTimer)
        {
            _testHooks?.DisposeRequested?.Invoke();
            _cleanupTimer.Dispose();
            lock (_lifecycleSync)
            {
                _timerStopped = true;
                CompleteDisposeIfReadyUnderLock();
                Monitor.PulseAll(_lifecycleSync);
            }
        }

        if (_operationDepth.Value > 0)
            return;

        lock (_lifecycleSync)
        {
            while (!_disposeCompleted)
            {
                Monitor.Wait(_lifecycleSync);
            }
        }
    }

    private bool TryEnterOperation()
    {
        lock (_lifecycleSync)
        {
            if (_disposeRequested)
                return false;

            _activeOperations++;
            _operationDepth.Value++;
            return true;
        }
    }

    private void ExitOperation()
    {
        _operationDepth.Value--;
        lock (_lifecycleSync)
        {
            _activeOperations--;
            CompleteDisposeIfReadyUnderLock();
            Monitor.PulseAll(_lifecycleSync);
        }
    }

    private void CompleteDisposeIfReadyUnderLock()
    {
        if (_disposeCompleted || !_disposeRequested || !_timerStopped || _activeOperations != 0)
            return;

        Volatile.Read(ref _generation).SymbolStates.Clear();
        _disposeCompleted = true;
    }

    private static TimestampMonotonicityConfig ValidateConfig(TimestampMonotonicityConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.ToleranceMs,
            nameof(TimestampMonotonicityConfig.ToleranceMs));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.AlertCooldownMs,
            nameof(TimestampMonotonicityConfig.AlertCooldownMs));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.TimeGapThresholdSeconds,
            nameof(TimestampMonotonicityConfig.TimeGapThresholdSeconds));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.GapAlertCooldownMs,
            nameof(TimestampMonotonicityConfig.GapAlertCooldownMs));
        return config;
    }

    private static string NormalizeRequiredIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().ToUpperInvariant();
    }

    private static DateTimeOffset SubtractMinutesClamped(DateTimeOffset value, int minutes)
    {
        var availableMinutes = (value - DateTimeOffset.MinValue).TotalMinutes;
        return minutes >= availableMinutes ? DateTimeOffset.MinValue : value.AddMinutes(-minutes);
    }

    private static DateTimeOffset SubtractClamped(DateTimeOffset value, TimeSpan duration)
        => duration >= value - DateTimeOffset.MinValue
            ? DateTimeOffset.MinValue
            : value - duration;

    private sealed class TimestampGeneration
    {
        public ConcurrentDictionary<SymbolEventKey, SymbolTimestampState> SymbolStates { get; } = new();
        public long TotalEventsChecked;
        public long TotalViolations;
        public long TotalGaps;
    }

    /// <summary>
    /// Per-symbol timestamp tracking state.
    /// </summary>
    private sealed class SymbolTimestampState
    {
        private readonly object _sync = new();
        private readonly string _symbol;
        private readonly string _eventType;
        private DateTimeOffset _lastEventTimestamp = DateTimeOffset.MinValue;
        private DateTimeOffset _lastEventTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastGapAlertTime = DateTimeOffset.MinValue;
        private DateTimeOffset _lastViolationTime = DateTimeOffset.MinValue;
        private bool _hasEventTimestamp;
        private long _totalEvents;
        private long _totalViolations;
        private long _totalGaps;
        private int _consecutiveViolations;
        private bool _retired;

        public SymbolTimestampState(string symbol, string eventType)
        {
            _symbol = symbol;
            _eventType = eventType;
        }

        public bool TryObserve(
            DateTimeOffset timestamp,
            DateTimeOffset observedAt,
            TimestampMonotonicityConfig config,
            out TimestampObservation observation)
        {
            lock (_sync)
            {
                if (_retired)
                {
                    observation = default;
                    return false;
                }

                _totalEvents++;
                _lastEventTime = observedAt;

                var previousTimestamp = _lastEventTimestamp;
                if (!_hasEventTimestamp)
                {
                    _hasEventTimestamp = true;
                    _lastEventTimestamp = timestamp;
                    observation = new TimestampObservation(
                        PreviousTimestamp: DateTimeOffset.MinValue,
                        DeltaMs: 0,
                        IsFirstEvent: true,
                        IsViolation: false,
                        IsGap: false,
                        ShouldAlert: false,
                        ConsecutiveViolations: 0,
                        TotalViolations: _totalViolations,
                        TotalGaps: _totalGaps);
                    return true;
                }

                var timeDelta = (timestamp - previousTimestamp).TotalMilliseconds;

                // A late arrival must never move the comparison watermark backwards.
                if (timestamp > _lastEventTimestamp)
                {
                    _lastEventTimestamp = timestamp;
                }

                if (timeDelta < -(double)config.ToleranceMs)
                {
                    _totalViolations++;
                    _consecutiveViolations++;
                    _lastViolationTime = observedAt;

                    var shouldAlert = CooldownElapsed(observedAt, _lastAlertTime, config.AlertCooldownMs);
                    if (shouldAlert)
                    {
                        _lastAlertTime = observedAt;
                    }

                    observation = new TimestampObservation(
                        PreviousTimestamp: previousTimestamp,
                        DeltaMs: timeDelta,
                        IsFirstEvent: false,
                        IsViolation: true,
                        IsGap: false,
                        ShouldAlert: shouldAlert,
                        ConsecutiveViolations: _consecutiveViolations,
                        TotalViolations: _totalViolations,
                        TotalGaps: _totalGaps);
                    return true;
                }

                if (_consecutiveViolations > 0)
                {
                    _consecutiveViolations = 0;
                }

                var gapThresholdMilliseconds = (double)config.TimeGapThresholdSeconds * 1000d;
                if (config.DetectTimeGaps && timeDelta > gapThresholdMilliseconds)
                {
                    _totalGaps++;

                    var shouldAlert = CooldownElapsed(observedAt, _lastGapAlertTime, config.GapAlertCooldownMs);
                    if (shouldAlert)
                    {
                        _lastGapAlertTime = observedAt;
                    }

                    observation = new TimestampObservation(
                        PreviousTimestamp: previousTimestamp,
                        DeltaMs: timeDelta,
                        IsFirstEvent: false,
                        IsViolation: false,
                        IsGap: true,
                        ShouldAlert: shouldAlert,
                        ConsecutiveViolations: _consecutiveViolations,
                        TotalViolations: _totalViolations,
                        TotalGaps: _totalGaps);
                    return true;
                }

                observation = new TimestampObservation(
                    PreviousTimestamp: previousTimestamp,
                    DeltaMs: timeDelta,
                    IsFirstEvent: false,
                    IsViolation: false,
                    IsGap: false,
                    ShouldAlert: false,
                    ConsecutiveViolations: _consecutiveViolations,
                    TotalViolations: _totalViolations,
                    TotalGaps: _totalGaps);
                return true;
            }
        }

        public SymbolTimestampSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new SymbolTimestampSnapshot(
                    Symbol: _symbol,
                    EventType: _eventType,
                    LastEventTimestamp: _lastEventTimestamp,
                    LastEventTime: _lastEventTime,
                    LastViolationTime: _lastViolationTime,
                    TotalEvents: _totalEvents,
                    TotalViolations: _totalViolations,
                    TotalGaps: _totalGaps);
            }
        }

        public bool RetireIfInactive(DateTimeOffset cutoff)
        {
            lock (_sync)
            {
                if (_retired)
                    return true;
                if (_lastEventTime >= cutoff)
                    return false;

                _retired = true;
                return true;
            }
        }

        private static bool CooldownElapsed(
            DateTimeOffset observedAt,
            DateTimeOffset lastAlertTime,
            int cooldownMs)
        {
            return (observedAt - lastAlertTime).TotalMilliseconds >= cooldownMs;
        }
    }

    private readonly record struct SymbolEventKey(string Symbol, string EventType);

    private readonly record struct TimestampObservation(
        DateTimeOffset PreviousTimestamp,
        double DeltaMs,
        bool IsFirstEvent,
        bool IsViolation,
        bool IsGap,
        bool ShouldAlert,
        int ConsecutiveViolations,
        long TotalViolations,
        long TotalGaps);

    private readonly record struct SymbolTimestampSnapshot(
        string Symbol,
        string EventType,
        DateTimeOffset LastEventTimestamp,
        DateTimeOffset LastEventTime,
        DateTimeOffset LastViolationTime,
        long TotalEvents,
        long TotalViolations,
        long TotalGaps);
}

internal sealed class TimestampMonotonicityCheckerTestHooks
{
    public Action? ObservationCommittedBeforePublish { get; set; }
    public Action? StateRetiredBeforeRemoval { get; set; }
    public Action? DisposeRequested { get; set; }
}

/// <summary>
/// Configuration for timestamp monotonicity checking.
/// </summary>
public sealed record TimestampMonotonicityConfig
{
    /// <summary>
    /// Tolerance in milliseconds for timestamp variations. Must be non-negative.
    /// Timestamps within this tolerance of the previous timestamp are not flagged.
    /// </summary>
    public int ToleranceMs { get; init; } = 100;

    /// <summary>
    /// Minimum time between alerts for the same symbol/event type in milliseconds. Must be non-negative.
    /// </summary>
    public int AlertCooldownMs { get; init; } = 5000;

    /// <summary>
    /// Whether to detect large time gaps between events.
    /// </summary>
    public bool DetectTimeGaps { get; init; } = true;

    /// <summary>
    /// Threshold in seconds for detecting time gaps between events. Must be non-negative.
    /// </summary>
    public int TimeGapThresholdSeconds { get; init; } = 60;

    /// <summary>
    /// Minimum time between gap alerts in milliseconds. Must be non-negative.
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
/// <param name="TotalEventsChecked">Events checked since construction or the most recent reset.</param>
/// <param name="TotalViolations">Violations detected since construction or the most recent reset.</param>
/// <param name="TotalGaps">Gaps detected since construction or the most recent reset.</param>
/// <param name="SymbolStats">Details for currently retained states that have violations or gaps.</param>
public readonly record struct MonotonicityStats(
    long TotalEventsChecked,
    long TotalViolations,
    long TotalGaps,
    IReadOnlyList<SymbolMonotonicityStats> SymbolStats)
{
    /// <summary>Events represented by states still retained after inactivity cleanup.</summary>
    public long RetainedStateEvents { get; init; }

    /// <summary>Violations represented by states still retained after inactivity cleanup.</summary>
    public long RetainedStateViolations { get; init; }

    /// <summary>Gaps represented by states still retained after inactivity cleanup.</summary>
    public long RetainedStateGaps { get; init; }
}

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
