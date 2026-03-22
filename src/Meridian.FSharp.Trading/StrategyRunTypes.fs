namespace Meridian.FSharp.Trading

type StrategyCommand =
    | Start
    | Pause
    | Resume
    | Stop
    | Fail of reason: string
