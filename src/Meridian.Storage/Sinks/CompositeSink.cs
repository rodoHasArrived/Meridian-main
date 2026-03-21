using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Health state of an individual storage sink within the composite.
/// </summary>
public enum SinkHealthState : byte
{
    /// <summary>Sink is operating normally.</summary>
    Healthy,

    /// <summary>Sink has experienced failures but is still receiving writes.</summary>
    Degraded,

    /// <summary>Sink circuit breaker is open; writes are being skipped.</summary>
    Failed
}

/// <summary>
/// Immutable snapshot of per-sink health information, suitable for metrics and diagnostics.
/// </summary>
public sealed record SinkHealth(
    string SinkType,
    int SinkIndex,
    SinkHealthState State,
    int ConsecutiveFailures,
    DateTimeOffset LastFailureTime,
    DateTimeOffset? CircuitResetTime,
    long TotalFailures
);

/// <summary>
/// Determines how the composite sink behaves when one or more child sinks fail.
/// </summary>
public enum FailurePolicy : byte
{
    /// <summary>Continue writing to remaining healthy sinks when one fails (default).</summary>
    ContinueOnPartialFailure,

    /// <summary>Throw immediately if any sink fails.</summary>
    FailOnAnyFailure
}

/// <summary>
/// Fans out events to multiple storage sinks, enabling multi-format storage
/// (e.g., JSONL + Parquet simultaneously) without modifying the EventPipeline.
/// Includes per-sink circuit breaker health tracking to avoid hammering
/// a persistently failing sink.
/// </summary>
public sealed class CompositeSink : IStorageSink
{
    private readonly IReadOnlyList<IStorageSink> _sinks;
    private readonly string[] _sinkTypeNames;
    private readonly ILogger<CompositeSink> _logger;
    private readonly int _maxConsecutiveFailures;
    private readonly TimeSpan _circuitResetTimeout;
    private readonly FailurePolicy _failurePolicy;
    private readonly TimeProvider _timeProvider;

    private long _appendFailures;

    /// <summary>
    /// Per-sink mutable health tracker. Indexed by sink position in <see cref="_sinks"/>.
    /// Access is synchronised via <see cref="Interlocked"/> and atomic field updates on
    /// the tracker instances.
    /// </summary>
    private readonly SinkCircuitState[] _circuitStates;

    public CompositeSink(
        IEnumerable<IStorageSink> sinks,
        ILogger<CompositeSink>? logger = null,
        int maxConsecutiveFailures = 5,
        TimeSpan? circuitResetTimeout = null,
        FailurePolicy failurePolicy = FailurePolicy.ContinueOnPartialFailure,
        TimeProvider? timeProvider = null)
    {
        _sinks = sinks?.ToList() ?? throw new ArgumentNullException(nameof(sinks));
        _logger = logger ?? NullLogger<CompositeSink>.Instance;
        _failurePolicy = failurePolicy;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_sinks.Count == 0)
            throw new ArgumentException("At least one sink must be provided.", nameof(sinks));

        if (maxConsecutiveFailures < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConsecutiveFailures), "Must be at least 1.");

        _maxConsecutiveFailures = maxConsecutiveFailures;
        _circuitResetTimeout = circuitResetTimeout ?? TimeSpan.FromSeconds(60);

        _sinkTypeNames = new string[_sinks.Count];
        _circuitStates = new SinkCircuitState[_sinks.Count];
        for (var i = 0; i < _sinks.Count; i++)
        {
            _sinkTypeNames[i] = _sinks[i].GetType().Name;
            _circuitStates[i] = new SinkCircuitState();
        }
    }

    /// <summary>Gets the number of underlying sinks.</summary>
    public int SinkCount => _sinks.Count;

    /// <summary>Gets the total number of individual sink append failures since startup.</summary>
    public long AppendFailures => Interlocked.Read(ref _appendFailures);

    /// <summary>Gets the configured failure policy.</summary>
    public FailurePolicy FailurePolicy => _failurePolicy;

    /// <summary>Gets the number of sinks currently in the <see cref="SinkHealthState.Healthy"/> state.</summary>
    public int HealthySinkCount => CountSinksByState(SinkHealthState.Healthy);

    /// <summary>Gets the number of sinks currently in the <see cref="SinkHealthState.Degraded"/> state.</summary>
    public int DegradedSinkCount => CountSinksByState(SinkHealthState.Degraded);

    /// <summary>Gets the number of sinks currently in the <see cref="SinkHealthState.Failed"/> state (circuit open).</summary>
    public int FailedSinkCount => CountSinksByState(SinkHealthState.Failed);

    /// <summary>Gets the total number of circuit breaker trip events across all sinks.</summary>
    public long TotalCircuitBreaks
    {
        get
        {
            long total = 0;
            for (var i = 0; i < _circuitStates.Length; i++)
            {
                total += _circuitStates[i].CircuitBreakCount;
            }
            return total;
        }
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();

        if (_sinks.Count == 1)
        {
            // Fast path: single sink — skip Task.WhenAll overhead.
            await AppendToSinkAsync(0, evt, now, ct).ConfigureAwait(false);
            return;
        }

        // Multi-sink: fan out in parallel. Independent sinks are written concurrently so that
        // total append latency equals max(sink latencies) rather than sum(sink latencies).
        var tasks = new Task[_sinks.Count];
        for (var i = 0; i < _sinks.Count; i++)
            tasks[i] = AppendToSinkAsync(i, evt, now, ct);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task AppendToSinkAsync(int i, MarketEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var state = _circuitStates[i];
        var effectiveState = GetEffectiveState(state, now);

        if (effectiveState == SinkHealthState.Failed)
        {
            // Circuit is open and reset timeout has not elapsed; skip this sink.
            _logger.LogDebug(
                "Skipping sink {SinkIndex}/{SinkCount} ({SinkType}) — circuit breaker open until {CircuitResetTime}",
                i + 1, _sinks.Count, _sinkTypeNames[i], state.CircuitResetTime);
            return;
        }

        var isHalfOpen = effectiveState == SinkHealthState.Degraded && state.IsCircuitHalfOpen(now, _circuitResetTimeout, _maxConsecutiveFailures);

        try
        {
            await _sinks[i].AppendAsync(evt, ct).ConfigureAwait(false);

            // Success: reset consecutive failures (full reset on healthy, or close the circuit on half-open).
            if (state.ConsecutiveFailures > 0)
            {
                if (isHalfOpen)
                {
                    _logger.LogInformation(
                        "Sink {SinkIndex}/{SinkCount} ({SinkType}) circuit breaker closed — write succeeded after reset timeout",
                        i + 1, _sinks.Count, _sinkTypeNames[i]);
                }

                state.RecordSuccess();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _appendFailures);
            state.RecordFailure(now);

            var newState = DetermineHealthState(state);

            if (newState == SinkHealthState.Failed && (!state.WasAlreadyTripped || isHalfOpen))
            {
                var resetTime = now + _circuitResetTimeout;
                state.TripCircuit(resetTime);

                _logger.LogError(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) circuit breaker OPENED after {ConsecutiveFailures} consecutive failures. " +
                    "Writes will be skipped until {CircuitResetTime}",
                    i + 1, _sinks.Count, _sinkTypeNames[i], state.ConsecutiveFailures, resetTime);
            }
            else
            {
                _logger.LogWarning(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) failed to append event for {Symbol} " +
                    "({ConsecutiveFailures}/{MaxConsecutiveFailures} consecutive failures)",
                    i + 1, _sinks.Count, _sinkTypeNames[i], evt.Symbol,
                    state.ConsecutiveFailures, _maxConsecutiveFailures);
            }

            if (_failurePolicy == FailurePolicy.FailOnAnyFailure)
            {
                throw new InvalidOperationException(
                    $"Sink {_sinkTypeNames[i]} failed and FailurePolicy is FailOnAnyFailure.", ex);
            }
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<Exception>? exceptions = null;
        var now = _timeProvider.GetUtcNow();

        for (var i = 0; i < _sinks.Count; i++)
        {
            var effectiveState = GetEffectiveState(_circuitStates[i], now);
            if (effectiveState == SinkHealthState.Failed)
            {
                _logger.LogDebug(
                    "Skipping flush for sink {SinkIndex}/{SinkCount} ({SinkType}) — circuit breaker open",
                    i + 1, _sinks.Count, _sinkTypeNames[i]);
                continue;
            }

            try
            {
                await _sinks[i].FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) failed to flush",
                    i + 1, _sinks.Count, _sinkTypeNames[i]);
                (exceptions ??= new List<Exception>()).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more sinks failed to flush.", exceptions);
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = 0; i < _sinks.Count; i++)
        {
            try
            {
                await _sinks[i].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) failed during disposal",
                    i + 1, _sinks.Count, _sinkTypeNames[i]);
            }
        }
    }

    /// <summary>
    /// Returns a health report for every registered sink, including circuit breaker state,
    /// failure counts, and reset times. Useful for Prometheus metrics and diagnostics endpoints.
    /// </summary>
    public IReadOnlyList<SinkHealth> GetSinkHealthReport()
    {
        var now = _timeProvider.GetUtcNow();
        var report = new SinkHealth[_sinks.Count];

        for (var i = 0; i < _sinks.Count; i++)
        {
            var state = _circuitStates[i];
            var effectiveState = GetEffectiveState(state, now);

            report[i] = new SinkHealth(
                SinkType: _sinkTypeNames[i],
                SinkIndex: i,
                State: effectiveState,
                ConsecutiveFailures: state.ConsecutiveFailures,
                LastFailureTime: state.LastFailureTime,
                CircuitResetTime: state.CircuitResetTime == DateTimeOffset.MinValue ? null : state.CircuitResetTime,
                TotalFailures: state.TotalFailures
            );
        }

        return report;
    }

    /// <summary>
    /// Determines the effective health state for a sink, taking the circuit reset timeout
    /// into account (half-open transitions back to <see cref="SinkHealthState.Degraded"/>).
    /// </summary>
    private SinkHealthState GetEffectiveState(SinkCircuitState state, DateTimeOffset now)
    {
        if (state.ConsecutiveFailures >= _maxConsecutiveFailures)
        {
            // Circuit was tripped; check if the reset timeout has elapsed (half-open).
            if (state.CircuitResetTime != DateTimeOffset.MinValue && now >= state.CircuitResetTime)
            {
                // Half-open: allow a single probe write, report as Degraded.
                return SinkHealthState.Degraded;
            }

            return SinkHealthState.Failed;
        }

        if (state.ConsecutiveFailures > 0)
        {
            return SinkHealthState.Degraded;
        }

        return SinkHealthState.Healthy;
    }

    /// <summary>
    /// Returns the health state purely from consecutive failure count
    /// (does not consider circuit reset timeout).
    /// </summary>
    private SinkHealthState DetermineHealthState(SinkCircuitState state)
    {
        if (state.ConsecutiveFailures >= _maxConsecutiveFailures)
            return SinkHealthState.Failed;

        if (state.ConsecutiveFailures > 0)
            return SinkHealthState.Degraded;

        return SinkHealthState.Healthy;
    }

    private int CountSinksByState(SinkHealthState targetState)
    {
        var now = _timeProvider.GetUtcNow();
        var count = 0;
        for (var i = 0; i < _circuitStates.Length; i++)
        {
            if (GetEffectiveState(_circuitStates[i], now) == targetState)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Mutable per-sink circuit breaker state. All mutations use atomic operations
    /// so that concurrent callers observe consistent values without taking locks.
    /// </summary>
    private sealed class SinkCircuitState
    {
        private int _consecutiveFailures;
        private long _totalFailures;
        private long _circuitBreakCount;
        private long _lastFailureTimeTicks;   // UTC ticks of DateTimeOffset
        private long _circuitResetTimeTicks;  // UTC ticks of DateTimeOffset

        /// <summary>True if the circuit is already in the tripped/open state (prevents duplicate log entries).</summary>
        private int _wasTripped; // 0 = not tripped, 1 = tripped

        public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);
        public long TotalFailures => Interlocked.Read(ref _totalFailures);
        public long CircuitBreakCount => Interlocked.Read(ref _circuitBreakCount);
        public DateTimeOffset LastFailureTime => new DateTimeOffset(Interlocked.Read(ref _lastFailureTimeTicks), TimeSpan.Zero);
        public DateTimeOffset CircuitResetTime => new DateTimeOffset(Interlocked.Read(ref _circuitResetTimeTicks), TimeSpan.Zero);
        public bool WasAlreadyTripped => Volatile.Read(ref _wasTripped) == 1;

        public void RecordFailure(DateTimeOffset now)
        {
            Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Increment(ref _totalFailures);
            Interlocked.Exchange(ref _lastFailureTimeTicks, now.UtcTicks);
        }

        public void RecordSuccess()
        {
            Volatile.Write(ref _consecutiveFailures, 0);
            Volatile.Write(ref _wasTripped, 0);
            Interlocked.Exchange(ref _circuitResetTimeTicks, DateTimeOffset.MinValue.UtcTicks);
        }

        public void TripCircuit(DateTimeOffset resetTime)
        {
            Interlocked.Exchange(ref _circuitResetTimeTicks, resetTime.UtcTicks);
            Volatile.Write(ref _wasTripped, 1);
            Interlocked.Increment(ref _circuitBreakCount);
        }


        private static DateTimeOffset ReadUtcTicks(ref long utcTicks)
        {
            var ticks = Interlocked.Read(ref utcTicks);
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        private static void WriteUtcTicks(ref long utcTicks, DateTimeOffset value)
        {
            Interlocked.Exchange(ref utcTicks, value.UtcTicks);
        }

        /// <summary>
        /// Returns true when the sink is in half-open state: the circuit was tripped
        /// (consecutive failures >= threshold) and the reset timeout has elapsed.
        /// </summary>
        public bool IsCircuitHalfOpen(DateTimeOffset now, TimeSpan resetTimeout, int threshold)
        {
            return ConsecutiveFailures >= threshold
                && CircuitResetTime != DateTimeOffset.MinValue
                && now >= CircuitResetTime;
        }
    }
}
