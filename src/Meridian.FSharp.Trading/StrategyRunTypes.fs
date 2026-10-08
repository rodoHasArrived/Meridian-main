namespace Meridian.FSharp.Trading

type StrategyCommand =
    | Start
    | WarmupCompleted
    | Pause
    | Resume
    | Stop
    | StopCompleted
    | Fail of reason: string
