namespace Meridian.FSharp.Trading

type StrategyLifecycleState =
    | Registered
    | WarmingUp
    | Running
    | Paused
    | Stopping
    | Stopped
    | Faulted of reason: string
