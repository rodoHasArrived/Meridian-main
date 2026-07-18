namespace Meridian.Contracts.Lifecycle;

public enum RuntimeLifecycleState
{
    Created,
    Bootstrapping,
    Validating,
    StartingHost,
    EvaluatingReadiness,
    Ready,
    Degraded,
    ShutdownRequested,
    Draining,
    Flushing,
    StoppingHost,
    Stopped,
    Failed
}

public enum RuntimeReadinessStatus
{
    Starting,
    Ready,
    Degraded,
    NotReady,
    Stopping,
    Failed
}

public enum LifecycleCheckRequirement
{
    Required,
    Degradable
}

public enum LifecycleCheckStatus
{
    Pending,
    Passing,
    Degraded,
    Failing,
    Skipped
}

public enum LifecycleShutdownReason
{
    Operator,
    Restart,
    Supervisor,
    HttpLocalShutdown,
    ConsoleCancel,
    ExternalCancellation,
    ProcessExit,
    StartupFailure
}

public enum LifecycleShutdownStage
{
    Requested,
    StopAcceptingWork,
    Draining,
    Flushing,
    PersistingReceipt,
    ReleasingHost,
    Completed,
    Failed
}

public enum LifecycleShutdownOutcome
{
    Pending,
    Succeeded,
    SucceededWithWarnings,
    TimedOut,
    Forced,
    Failed,
    Cancelled
}

public enum LifecycleDatabaseManagementMode
{
    Dedicated,
    External
}
