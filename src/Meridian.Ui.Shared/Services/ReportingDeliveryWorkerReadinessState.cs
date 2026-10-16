namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Process-local liveness receipt for the server-owned secure reporting distribution worker.
/// A snapshot observes its heartbeat, failure count, and initial-start window together.
/// </summary>
public sealed class ReportingDeliveryWorkerReadinessState
{
    private const int LifecycleNotStarted = 0;
    private const int LifecycleInitialStart = 1;
    private const int LifecycleOperational = 2;

    private readonly object _gate = new();
    private bool _ready;
    private int _consecutiveFailures;
    private int _lifecycleState;
    private DateTimeOffset? _lastSuccessfulCycleUtc;

    public bool IsReady => Capture().IsReady;

    public int ConsecutiveFailures => Capture().ConsecutiveFailures;

    internal bool IsInitialStartInProgress => Capture().IsInitialStartInProgress;

    public DateTimeOffset? LastSuccessfulCycleUtc => Capture().LastSuccessfulCycleUtc;

    public bool IsHealthy(DateTimeOffset nowUtc, TimeSpan maximumHeartbeatAge) =>
        Capture().IsHealthy(nowUtc, maximumHeartbeatAge);

    internal Snapshot Capture()
    {
        lock (_gate)
        {
            return new Snapshot(
                _ready,
                _consecutiveFailures,
                _lastSuccessfulCycleUtc,
                _lifecycleState == LifecycleInitialStart);
        }
    }

    internal void MarkStarting()
    {
        lock (_gate)
        {
            _ready = false;
            if (_lifecycleState == LifecycleNotStarted)
            {
                _lifecycleState = LifecycleInitialStart;
            }
        }
    }

    internal void MarkReady(DateTimeOffset? completedAtUtc = null)
    {
        lock (_gate)
        {
            _lastSuccessfulCycleUtc = (completedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            _consecutiveFailures = 0;
            _ready = true;
            _lifecycleState = LifecycleOperational;
        }
    }

    internal void MarkCycleFailed()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            _ready = false;
            _lifecycleState = LifecycleOperational;
        }
    }

    internal void MarkNotReady()
    {
        lock (_gate)
        {
            _ready = false;
            _lifecycleState = LifecycleOperational;
        }
    }

    internal readonly record struct Snapshot(
        bool IsReady,
        int ConsecutiveFailures,
        DateTimeOffset? LastSuccessfulCycleUtc,
        bool IsInitialStartInProgress)
    {
        internal bool IsHealthy(DateTimeOffset nowUtc, TimeSpan maximumHeartbeatAge) =>
            WorkerReadinessState.IsHealthy(
                IsReady,
                ConsecutiveFailures,
                LastSuccessfulCycleUtc,
                nowUtc,
                maximumHeartbeatAge);
    }
}
