namespace Meridian.FSharp.Trading

[<RequireQualifiedAccess>]
module PromotionReadiness =

    let isTerminal state =
        match state with
        | StrategyLifecycleState.Stopped
        | StrategyLifecycleState.Faulted _ -> true
        | _ -> false
