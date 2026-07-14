namespace Meridian.Core.Resilience;

/// <summary>Circuit-breaker states.</summary>
public enum CircuitStatus
{
    /// <summary>Calls flow normally; consecutive failures are counted.</summary>
    Closed = 0,

    /// <summary>The breaker tripped; calls are rejected until the break duration elapses.</summary>
    Open = 1,

    /// <summary>The break elapsed; probe calls are allowed to test recovery.</summary>
    HalfOpen = 2,
}

/// <summary>Configuration for <see cref="CircuitBreaker"/>.</summary>
public sealed record CircuitBreakerOptions
{
    /// <summary>Consecutive failures that trip the breaker.</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>How long the breaker stays open before allowing half-open probes.</summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Minimum interval between half-open probes. Zero allows every call through while
    /// half-open (until a success closes or a failure re-opens the breaker).
    /// </summary>
    public TimeSpan HalfOpenProbeInterval { get; init; } = TimeSpan.Zero;
}

/// <summary>
/// Canonical consecutive-failure circuit breaker shared by the hand-rolled resilience sites
/// (subscription auto-resubscribe, composite sink fan-out). One state machine:
/// Closed → (threshold consecutive failures) → Open → (break elapses) → HalfOpen →
/// success closes / failure re-opens. The clock is injectable so call sites remain
/// deterministic under test, and the <see cref="StateChanged"/> hook lets each site keep
/// emitting its own metrics at the exact transition points it always had.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly object _lock = new();
    private readonly CircuitBreakerOptions _options;
    private readonly TimeProvider _clock;

    private CircuitStatus _status = CircuitStatus.Closed;
    private int _consecutiveFailures;
    private long _totalFailures;
    private long _tripCount;
    private DateTimeOffset _lastFailureTime;
    private DateTimeOffset? _openUntil;
    private DateTimeOffset _lastProbeTime;

    public CircuitBreaker(CircuitBreakerOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new CircuitBreakerOptions();
        if (_options.FailureThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.FailureThreshold, "FailureThreshold must be at least 1.");
        }

        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised inside the state lock on every transition, with (from, to). Handlers should be
    /// cheap and must not call back into the breaker.
    /// </summary>
    public event Action<CircuitStatus, CircuitStatus>? StateChanged;

    /// <summary>Effective status, evaluating an elapsed break as <see cref="CircuitStatus.HalfOpen"/>.</summary>
    public CircuitStatus Status
    {
        get
        {
            lock (_lock)
            {
                if (_status == CircuitStatus.Open && BreakElapsed(_clock.GetUtcNow()))
                {
                    return CircuitStatus.HalfOpen;
                }

                return _status;
            }
        }
    }

    /// <summary>Consecutive failures since the last success or reset.</summary>
    public int ConsecutiveFailures
    {
        get { lock (_lock) { return _consecutiveFailures; } }
    }

    /// <summary>Total failures recorded over the breaker's lifetime.</summary>
    public long TotalFailures
    {
        get { lock (_lock) { return _totalFailures; } }
    }

    /// <summary>Number of times the breaker has tripped open (including half-open re-trips).</summary>
    public long TripCount
    {
        get { lock (_lock) { return _tripCount; } }
    }

    /// <summary>Timestamp of the most recent failure (default when none recorded).</summary>
    public DateTimeOffset LastFailureTime
    {
        get { lock (_lock) { return _lastFailureTime; } }
    }

    /// <summary>Absolute instant the current break ends, or null when not open.</summary>
    public DateTimeOffset? OpenUntil
    {
        get { lock (_lock) { return _openUntil; } }
    }

    /// <summary>
    /// Gates a call. Closed always allows. Open allows the first call after the break elapses
    /// (transitioning to half-open); half-open allows probes subject to
    /// <see cref="CircuitBreakerOptions.HalfOpenProbeInterval"/> throttling.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();
            switch (_status)
            {
                case CircuitStatus.Closed:
                    return true;

                case CircuitStatus.Open when BreakElapsed(now):
                    Transition(CircuitStatus.HalfOpen);
                    _lastProbeTime = now;
                    return true;

                case CircuitStatus.Open:
                    return false;

                default: // HalfOpen
                    if (_options.HalfOpenProbeInterval > TimeSpan.Zero
                        && now - _lastProbeTime < _options.HalfOpenProbeInterval)
                    {
                        return false;
                    }

                    _lastProbeTime = now;
                    return true;
            }
        }
    }

    /// <summary>Records a success: resets the consecutive-failure count and closes the breaker.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            // Materialize an elapsed break first so StateChanged subscribers observe the full
            // Open → HalfOpen → Closed sequence even when the caller gated on the non-mutating
            // Status property instead of TryAcquire.
            MaterializeElapsedBreak(_clock.GetUtcNow());

            _consecutiveFailures = 0;
            _openUntil = null;
            if (_status != CircuitStatus.Closed)
            {
                Transition(CircuitStatus.Closed);
            }
        }
    }

    /// <summary>
    /// Records a failure: increments counters, re-opens a half-open breaker, and trips a closed
    /// breaker once the consecutive-failure threshold is reached.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();

            // A probe failure after the break elapsed must re-trip with a fresh break window and
            // fire the full Open → HalfOpen → Open transition sequence, even when the caller
            // gated on the non-mutating Status property instead of TryAcquire. A failure while
            // the break is still running (a call that slipped past the gate) keeps the breaker
            // open without counting an extra trip.
            MaterializeElapsedBreak(now);

            _consecutiveFailures++;
            _totalFailures++;
            _lastFailureTime = now;

            if (_status == CircuitStatus.HalfOpen
                || (_status == CircuitStatus.Closed && _consecutiveFailures >= _options.FailureThreshold))
            {
                Trip(now);
            }
        }
    }

    /// <summary>Manually closes the breaker and clears the consecutive-failure count.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
            if (_status != CircuitStatus.Closed)
            {
                Transition(CircuitStatus.Closed);
            }
        }
    }

    private void Trip(DateTimeOffset now)
    {
        _tripCount++;
        _openUntil = now + _options.BreakDuration;
        Transition(CircuitStatus.Open);
    }

    /// <summary>Turns a stored Open state whose break has elapsed into a real HalfOpen transition.</summary>
    private void MaterializeElapsedBreak(DateTimeOffset now)
    {
        if (_status == CircuitStatus.Open && BreakElapsed(now))
        {
            Transition(CircuitStatus.HalfOpen);
        }
    }

    private bool BreakElapsed(DateTimeOffset now) => _openUntil is { } until && now >= until;

    private void Transition(CircuitStatus to)
    {
        var from = _status;
        _status = to;
        if (from != to)
        {
            StateChanged?.Invoke(from, to);
        }
    }
}
