namespace Meridian.Strategies.Models;

/// <summary>Durable lifecycle transitions retained for a strategy run.</summary>
public enum StrategyRunLifecycleEventType : byte
{
    Started = 0,
    StartFailed = 1,
    Paused = 2,
    PauseFailed = 3,
    StopRequested = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    RecoveryAttempted = 8,
    StartRequested = 9,
    PauseRequested = 10,
    EvidencePersistenceFailed = 11,
    StopFailed = 12,

    /// <summary>
    /// The run was promoted and retained, but the live engine declined to activate it — most
    /// often because no live strategy source could resolve it. Not a failure: the run is intact
    /// and activates on a later attempt if the blocking condition changes.
    /// </summary>
    ActivationDeferred = 13,

    /// <summary>A retained promoted run began execution on its first engine session.</summary>
    Activated = 14,

    /// <summary>An open run or a new recovery attempt began execution after interruption.</summary>
    Recovered = 15,

    /// <summary>An operator explicitly resumed the same paused run.</summary>
    Resumed = 16,

    /// <summary>The host released execution while retaining an open run for recovery.</summary>
    Suspended = 17
}
