using System.Runtime.CompilerServices;
using System.Threading;

namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Thread-safe metrics for tracking auto-resubscription behavior.
/// Follows the same lock-free pattern as the core Metrics class.
/// </summary>
public static class ResubscriptionMetrics
{
    // Resubscription counters
    private static long _resubscribeAttempts;
    private static long _resubscribeSuccesses;
    private static long _resubscribeFailures;
    private static long _rateLimitedSkips;

    // Circuit breaker state tracking
    private static long _circuitBreakerOpens;
    private static long _circuitBreakerCloses;
    private static long _circuitBreakerHalfOpens;
    private static int _currentCircuitState; // 0=Closed, 1=Open, 2=HalfOpen

    // Per-symbol tracking (using simple counters for aggregate view)
    private static long _symbolsInCooldown;
    private static long _symbolsCircuitOpen;

    // Timing
    private static long _totalResubscribeTimeMs;
    private static long _lastResubscribeTimestamp;

    #region Counter Properties

    public static long ResubscribeAttempts => Interlocked.Read(ref _resubscribeAttempts);
    public static long ResubscribeSuccesses => Interlocked.Read(ref _resubscribeSuccesses);
    public static long ResubscribeFailures => Interlocked.Read(ref _resubscribeFailures);
    public static long RateLimitedSkips => Interlocked.Read(ref _rateLimitedSkips);

    public static long CircuitBreakerOpens => Interlocked.Read(ref _circuitBreakerOpens);
    public static long CircuitBreakerCloses => Interlocked.Read(ref _circuitBreakerCloses);
    public static long CircuitBreakerHalfOpens => Interlocked.Read(ref _circuitBreakerHalfOpens);

    public static CircuitState CurrentCircuitState => (CircuitState)Interlocked.CompareExchange(ref _currentCircuitState, 0, 0);

    public static long SymbolsInCooldown => Interlocked.Read(ref _symbolsInCooldown);
    public static long SymbolsCircuitOpen => Interlocked.Read(ref _symbolsCircuitOpen);

    public static long TotalResubscribeTimeMs => Interlocked.Read(ref _totalResubscribeTimeMs);
    public static long LastResubscribeTimestamp => Interlocked.Read(ref _lastResubscribeTimestamp);

    /// <summary>
    /// Success rate as a percentage (0-100).
    /// </summary>
    public static double SuccessRate
    {
        get
        {
            var total = ResubscribeAttempts;
            if (total == 0)
                return 100.0;
            return (double)ResubscribeSuccesses / total * 100;
        }
    }

    /// <summary>
    /// Average resubscription time in milliseconds.
    /// </summary>
    public static double AverageResubscribeTimeMs
    {
        get
        {
            var successes = ResubscribeSuccesses;
            if (successes == 0)
                return 0;
            return (double)TotalResubscribeTimeMs / successes;
        }
    }

    #endregion

    #region Increment Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeAttempt()
    {
        Interlocked.Increment(ref _resubscribeAttempts);
        Interlocked.Exchange(ref _lastResubscribeTimestamp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeSuccess(long elapsedMs = 0)
    {
        Interlocked.Increment(ref _resubscribeSuccesses);
        if (elapsedMs > 0)
            Interlocked.Add(ref _totalResubscribeTimeMs, elapsedMs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncResubscribeFailure() => Interlocked.Increment(ref _resubscribeFailures);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncRateLimitedSkip() => Interlocked.Increment(ref _rateLimitedSkips);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncCircuitBreakerOpen()
    {
        Interlocked.Increment(ref _circuitBreakerOpens);
        Interlocked.Exchange(ref _currentCircuitState, (int)CircuitState.Open);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncCircuitBreakerClose()
    {
        Interlocked.Increment(ref _circuitBreakerCloses);
        Interlocked.Exchange(ref _currentCircuitState, (int)CircuitState.Closed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncCircuitBreakerHalfOpen()
    {
        Interlocked.Increment(ref _circuitBreakerHalfOpens);
        Interlocked.Exchange(ref _currentCircuitState, (int)CircuitState.HalfOpen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetSymbolsInCooldown(long count) => Interlocked.Exchange(ref _symbolsInCooldown, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetSymbolsCircuitOpen(long count) => Interlocked.Exchange(ref _symbolsCircuitOpen, count);

    #endregion

    #region Snapshot

    /// <summary>
    /// Gets a point-in-time snapshot of all resubscription metrics.
    /// </summary>
    public static ResubscriptionMetricsSnapshot GetSnapshot()
    {
        return new ResubscriptionMetricsSnapshot(
            ResubscribeAttempts: ResubscribeAttempts,
            ResubscribeSuccesses: ResubscribeSuccesses,
            ResubscribeFailures: ResubscribeFailures,
            RateLimitedSkips: RateLimitedSkips,
            CircuitBreakerOpens: CircuitBreakerOpens,
            CircuitBreakerCloses: CircuitBreakerCloses,
            CircuitBreakerHalfOpens: CircuitBreakerHalfOpens,
            CurrentCircuitState: CurrentCircuitState,
            SymbolsInCooldown: SymbolsInCooldown,
            SymbolsCircuitOpen: SymbolsCircuitOpen,
            SuccessRate: SuccessRate,
            AverageResubscribeTimeMs: AverageResubscribeTimeMs,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _resubscribeAttempts, 0);
        Interlocked.Exchange(ref _resubscribeSuccesses, 0);
        Interlocked.Exchange(ref _resubscribeFailures, 0);
        Interlocked.Exchange(ref _rateLimitedSkips, 0);
        Interlocked.Exchange(ref _circuitBreakerOpens, 0);
        Interlocked.Exchange(ref _circuitBreakerCloses, 0);
        Interlocked.Exchange(ref _circuitBreakerHalfOpens, 0);
        Interlocked.Exchange(ref _currentCircuitState, 0);
        Interlocked.Exchange(ref _symbolsInCooldown, 0);
        Interlocked.Exchange(ref _symbolsCircuitOpen, 0);
        Interlocked.Exchange(ref _totalResubscribeTimeMs, 0);
        Interlocked.Exchange(ref _lastResubscribeTimestamp, 0);
    }

    #endregion
}

/// <summary>
/// Circuit breaker states.
/// </summary>
public enum CircuitState : byte
{
    /// <summary>Circuit is closed, allowing operations.</summary>
    Closed = 0,

    /// <summary>Circuit is open, rejecting operations.</summary>
    Open = 1,

    /// <summary>Circuit is half-open, testing recovery.</summary>
    HalfOpen = 2
}

/// <summary>
/// Immutable snapshot of resubscription metrics at a point in time.
/// </summary>
public readonly record struct ResubscriptionMetricsSnapshot(
    long ResubscribeAttempts,
    long ResubscribeSuccesses,
    long ResubscribeFailures,
    long RateLimitedSkips,
    long CircuitBreakerOpens,
    long CircuitBreakerCloses,
    long CircuitBreakerHalfOpens,
    CircuitState CurrentCircuitState,
    long SymbolsInCooldown,
    long SymbolsCircuitOpen,
    double SuccessRate,
    double AverageResubscribeTimeMs,
    DateTimeOffset Timestamp
);
