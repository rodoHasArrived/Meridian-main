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
    StopFailed = 12
}
