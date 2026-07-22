using Meridian.Core.Resilience;
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
    /// Per-sink circuit breaker (shared <see cref="CircuitBreaker"/> primitive), indexed by
    /// sink position in <see cref="_sinks"/>.
    /// </summary>
    private readonly CircuitBreaker[] _breakers;

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

        var breakerOptions = new CircuitBreakerOptions
        {
            FailureThreshold = _maxConsecutiveFailures,
            BreakDuration = _circuitResetTimeout,
        };

        _sinkTypeNames = new string[_sinks.Count];
        _breakers = new CircuitBreaker[_sinks.Count];
        for (var i = 0; i < _sinks.Count; i++)
        {
            _sinkTypeNames[i] = _sinks[i].GetType().Name;
            _breakers[i] = new CircuitBreaker(breakerOptions, _timeProvider);
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
            for (var i = 0; i < _breakers.Length; i++)
            {
                total += _breakers[i].TripCount;
            }
            return total;
        }
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        if (_sinks.Count == 1)
        {
            // Fast path: single sink — skip Task.WhenAll overhead.
            await AppendToSinkAsync(0, evt, ct).ConfigureAwait(false);
            return;
        }

        // Multi-sink: fan out in parallel. Independent sinks are written concurrently so that
        // total append latency equals max(sink latencies) rather than sum(sink latencies).
        var tasks = new Task[_sinks.Count];
        for (var i = 0; i < _sinks.Count; i++)
            tasks[i] = AppendToSinkAsync(i, evt, ct);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task AppendToSinkAsync(int i, MarketEvent evt, CancellationToken ct)
    {
        var breaker = _breakers[i];
        var status = breaker.Status;

        if (status == CircuitStatus.Open)
        {
            // Circuit is open and reset timeout has not elapsed; skip this sink.
            _logger.LogDebug(
                "Skipping sink {SinkIndex}/{SinkCount} ({SinkType}) — circuit breaker open until {CircuitResetTime}",
                i + 1, _sinks.Count, _sinkTypeNames[i], breaker.OpenUntil);
            return;
        }

        var isHalfOpen = status == CircuitStatus.HalfOpen;

        try
        {
            await _sinks[i].AppendAsync(evt, ct).ConfigureAwait(false);

            // Success: reset consecutive failures (full reset on degraded, or close the circuit on half-open).
            if (breaker.ConsecutiveFailures > 0)
            {
                if (isHalfOpen)
                {
                    _logger.LogInformation(
                        "Sink {SinkIndex}/{SinkCount} ({SinkType}) circuit breaker closed — write succeeded after reset timeout",
                        i + 1, _sinks.Count, _sinkTypeNames[i]);
                }

                breaker.RecordSuccess();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _appendFailures);
            var tripsBefore = breaker.TripCount;
            breaker.RecordFailure();

            if (breaker.TripCount > tripsBefore)
            {
                _logger.LogError(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) circuit breaker OPENED after {ConsecutiveFailures} consecutive failures. " +
                    "Writes will be skipped until {CircuitResetTime}",
                    i + 1, _sinks.Count, _sinkTypeNames[i], breaker.ConsecutiveFailures, breaker.OpenUntil);
            }
            else
            {
                _logger.LogWarning(ex,
                    "Sink {SinkIndex}/{SinkCount} ({SinkType}) failed to append event for {Symbol} " +
                    "({ConsecutiveFailures}/{MaxConsecutiveFailures} consecutive failures)",
                    i + 1, _sinks.Count, _sinkTypeNames[i], evt.Symbol,
                    breaker.ConsecutiveFailures, _maxConsecutiveFailures);
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

        for (var i = 0; i < _sinks.Count; i++)
        {
            if (_breakers[i].Status == CircuitStatus.Open)
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
        var report = new SinkHealth[_sinks.Count];

        for (var i = 0; i < _sinks.Count; i++)
        {
            var breaker = _breakers[i];

            report[i] = new SinkHealth(
                SinkType: _sinkTypeNames[i],
                SinkIndex: i,
                State: GetEffectiveState(breaker),
                ConsecutiveFailures: breaker.ConsecutiveFailures,
                LastFailureTime: breaker.LastFailureTime,
                CircuitResetTime: breaker.OpenUntil,
                TotalFailures: breaker.TotalFailures
            );
        }

        return report;
    }

    /// <summary>
    /// Maps the breaker status onto the sink health enum. <see cref="SinkHealthState.Degraded"/>
    /// covers both a half-open circuit (probe allowed) and a below-threshold failing sink.
    /// </summary>
    private static SinkHealthState GetEffectiveState(CircuitBreaker breaker) => breaker.Status switch
    {
        CircuitStatus.Open => SinkHealthState.Failed,
        CircuitStatus.HalfOpen => SinkHealthState.Degraded,
        _ => breaker.ConsecutiveFailures > 0 ? SinkHealthState.Degraded : SinkHealthState.Healthy,
    };

    private int CountSinksByState(SinkHealthState targetState)
    {
        var count = 0;
        for (var i = 0; i < _breakers.Length; i++)
        {
            if (GetEffectiveState(_breakers[i]) == targetState)
                count++;
        }
        return count;
    }
}
