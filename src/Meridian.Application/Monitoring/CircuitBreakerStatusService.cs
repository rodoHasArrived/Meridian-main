using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Tracks circuit breaker state transitions and provides an observable dashboard
/// of all circuit breakers in the system. Allows operators to see which providers
/// are healthy (closed), degraded (half-open), or failing (open) at a glance.
/// </summary>
public sealed class CircuitBreakerStatusService
{
    private readonly ILogger _log = LoggingSetup.ForContext<CircuitBreakerStatusService>();
    private readonly ConcurrentDictionary<string, CircuitBreakerInfo> _breakers = new();

    /// <summary>
    /// Event raised when any circuit breaker changes state.
    /// </summary>
    public event Action<CircuitBreakerStateChange>? OnStateChanged;

    /// <summary>
    /// Registers or updates a circuit breaker's state.
    /// Call this from resilience policy callbacks (OnOpened, OnClosed, OnHalfOpened).
    /// </summary>
    /// <param name="name">Unique name identifying this circuit breaker.</param>
    /// <param name="newState">The new state being transitioned to.</param>
    /// <param name="lastError">Optional error message that caused the transition.</param>
    /// <param name="failureCount">Optional number of failures that triggered the transition.</param>
    /// <param name="breakDuration">Optional break duration (stored for cooldown computation when state is Open).</param>
    public void RecordStateTransition(
        string name,
        CircuitBreakerState newState,
        string? lastError = null,
        int? failureCount = null,
        TimeSpan? breakDuration = null)
    {
        var now = DateTimeOffset.UtcNow;
        CircuitBreakerState oldState = CircuitBreakerState.Closed;

        var info = _breakers.AddOrUpdate(
            name,
            _ => new CircuitBreakerInfo
            {
                Name = name,
                State = newState,
                LastStateChange = now,
                TripCount = newState == CircuitBreakerState.Open ? 1 : 0,
                LastError = lastError,
                BreakDuration = breakDuration
            },
            (_, existing) =>
            {
                oldState = existing.State;
                return new CircuitBreakerInfo
                {
                    Name = existing.Name,
                    State = newState,
                    LastStateChange = now,
                    TripCount = newState == CircuitBreakerState.Open ? existing.TripCount + 1 : existing.TripCount,
                    LastError = lastError ?? existing.LastError,
                    BreakDuration = breakDuration ?? existing.BreakDuration
                };
            });

        _log.Information(
            "CircuitBreaker {Name} transitioned from {OldState} to {NewState} after {FailureCount} failures",
            name, oldState, newState, failureCount ?? 0);

        try
        {
            OnStateChanged?.Invoke(new CircuitBreakerStateChange(name, newState, now, lastError));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in circuit breaker state change handler");
        }
    }

    /// <summary>
    /// Registers a circuit breaker so it appears in the dashboard even before it trips.
    /// </summary>
    public void Register(string name)
    {
        _breakers.TryAdd(name, new CircuitBreakerInfo
        {
            Name = name,
            State = CircuitBreakerState.Closed,
            LastStateChange = DateTimeOffset.UtcNow,
            TripCount = 0
        });
    }

    /// <summary>
    /// Gets the current status of all registered circuit breakers.
    /// </summary>
    public CircuitBreakerDashboard GetDashboard()
    {
        var now = DateTimeOffset.UtcNow;
        var breakers = _breakers.Values
            .OrderBy(b => b.Name)
            .Select(b =>
            {
                TimeSpan? cooldownRemaining = null;
                if (b.State == CircuitBreakerState.Open && b.BreakDuration.HasValue)
                {
                    var remaining = b.BreakDuration.Value - (now - b.LastStateChange);
                    if (remaining > TimeSpan.Zero)
                        cooldownRemaining = remaining;
                }

                return new CircuitBreakerStatus(
                    Name: b.Name,
                    State: b.State.ToString(),
                    LastStateChange: b.LastStateChange,
                    TripCount: b.TripCount,
                    LastError: b.LastError,
                    TimeSinceLastChange: now - b.LastStateChange,
                    CooldownRemaining: cooldownRemaining);
            })
            .ToList();

        var openCount = breakers.Count(b => b.State == nameof(CircuitBreakerState.Open));
        var halfOpenCount = breakers.Count(b => b.State == nameof(CircuitBreakerState.HalfOpen));

        var overallHealth = openCount > 0
            ? "Red"
            : halfOpenCount > 0
                ? "Yellow"
                : "Green";

        return new CircuitBreakerDashboard(
            OverallHealth: overallHealth,
            TotalBreakers: breakers.Count,
            OpenCount: openCount,
            HalfOpenCount: halfOpenCount,
            ClosedCount: breakers.Count - openCount - halfOpenCount,
            Breakers: breakers,
            Timestamp: now);
    }

    private sealed class CircuitBreakerInfo
    {
        public required string Name { get; init; }
        public CircuitBreakerState State { get; set; }
        public DateTimeOffset LastStateChange { get; set; }
        public int TripCount { get; set; }
        public string? LastError { get; set; }
        public TimeSpan? BreakDuration { get; set; }
    }
}

/// <summary>
/// Circuit breaker state enum.
/// </summary>
public enum CircuitBreakerState : byte
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Represents a circuit breaker state change event.
/// </summary>
public sealed record CircuitBreakerStateChange(
    string Name,
    CircuitBreakerState NewState,
    DateTimeOffset Timestamp,
    string? LastError);

/// <summary>
/// Status of a single circuit breaker.
/// </summary>
public sealed record CircuitBreakerStatus(
    string Name,
    string State,
    DateTimeOffset LastStateChange,
    int TripCount,
    string? LastError,
    TimeSpan TimeSinceLastChange,
    TimeSpan? CooldownRemaining);

/// <summary>
/// Dashboard view of all circuit breakers.
/// </summary>
public sealed record CircuitBreakerDashboard(
    string OverallHealth,
    int TotalBreakers,
    int OpenCount,
    int HalfOpenCount,
    int ClosedCount,
    IReadOnlyList<CircuitBreakerStatus> Breakers,
    DateTimeOffset Timestamp);
