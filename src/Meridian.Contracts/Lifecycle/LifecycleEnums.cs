using System.Text.Json.Serialization;

namespace Meridian.Contracts.Lifecycle;

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeLifecycleState>))]
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

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeReadinessStatus>))]
public enum RuntimeReadinessStatus
{
    Starting,
    Ready,
    Degraded,
    NotReady,
    Stopping,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleCheckRequirement>))]
public enum LifecycleCheckRequirement
{
    Required,
    Degradable
}

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleCheckStatus>))]
public enum LifecycleCheckStatus
{
    Pending,
    Passing,
    Degraded,
    Failing,
    Skipped
}

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleShutdownReason>))]
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

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleShutdownStage>))]
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

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleShutdownOutcome>))]
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

[JsonConverter(typeof(JsonStringEnumConverter<LifecycleDatabaseManagementMode>))]
public enum LifecycleDatabaseManagementMode
{
    Dedicated,
    External
}
