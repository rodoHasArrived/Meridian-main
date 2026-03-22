namespace Meridian.FSharp.Trading

type TransitionResult = {
    PreviousState: StrategyLifecycleState
    NextState: StrategyLifecycleState
    IsValid: bool
    Reason: string option
    EmittedFacts: string list
}

[<RequireQualifiedAccess>]
module StrategyLifecycleTransitions =

    let apply command state =
        let invalid reason =
            {
                PreviousState = state
                NextState = state
                IsValid = false
                Reason = Some reason
                EmittedFacts = []
            }

        let valid nextState facts =
            {
                PreviousState = state
                NextState = nextState
                IsValid = true
                Reason = None
                EmittedFacts = facts
            }

        match state, command with
        | StrategyLifecycleState.Registered, StrategyCommand.Start ->
            valid StrategyLifecycleState.WarmingUp [ "strategy-start-requested"; "strategy-warmup-entered" ]
        | StrategyLifecycleState.Stopped, StrategyCommand.Start ->
            valid StrategyLifecycleState.WarmingUp [ "strategy-restart-requested"; "strategy-warmup-entered" ]
        | StrategyLifecycleState.Paused, StrategyCommand.Resume ->
            valid StrategyLifecycleState.Running [ "strategy-resume-requested"; "strategy-running-entered" ]
        | StrategyLifecycleState.WarmingUp, StrategyCommand.Pause
        | StrategyLifecycleState.Running, StrategyCommand.Pause ->
            valid StrategyLifecycleState.Paused [ "strategy-paused" ]
        | StrategyLifecycleState.WarmingUp, StrategyCommand.Stop
        | StrategyLifecycleState.Running, StrategyCommand.Stop
        | StrategyLifecycleState.Stopping, StrategyCommand.Stop
        | StrategyLifecycleState.Paused, StrategyCommand.Stop ->
            valid StrategyLifecycleState.Stopped [ "strategy-stop-requested"; "strategy-stopped" ]
        | _, StrategyCommand.Fail reason ->
            valid (StrategyLifecycleState.Faulted reason) [ "strategy-faulted" ]
        | StrategyLifecycleState.Registered, StrategyCommand.Pause ->
            invalid "Cannot pause a strategy that has not been started."
        | StrategyLifecycleState.Registered, StrategyCommand.Resume ->
            invalid "Cannot resume a strategy that has not been started."
        | StrategyLifecycleState.Registered, StrategyCommand.Stop ->
            invalid "Cannot stop a strategy that has not been started."
        | StrategyLifecycleState.WarmingUp, StrategyCommand.Start
        | StrategyLifecycleState.Running, StrategyCommand.Start ->
            invalid "Strategy is already active."
        | StrategyLifecycleState.Paused, StrategyCommand.Start ->
            invalid "Paused strategies must resume instead of start."
        | StrategyLifecycleState.Stopped, StrategyCommand.Resume ->
            invalid "Stopped strategies must start again instead of resume."
        | StrategyLifecycleState.Stopping, StrategyCommand.Start
        | StrategyLifecycleState.Stopping, StrategyCommand.Resume ->
            invalid "Strategy is stopping and cannot accept new run commands."
        | StrategyLifecycleState.Stopping, StrategyCommand.Pause ->
            invalid "Strategy is already stopping."
        | StrategyLifecycleState.Stopped, StrategyCommand.Pause ->
            invalid "Cannot pause a stopped strategy."
        | StrategyLifecycleState.Stopped, StrategyCommand.Stop ->
            invalid "Strategy is already stopped."
        | StrategyLifecycleState.Faulted _, StrategyCommand.Start ->
            invalid "Faulted strategies must be re-created before starting."
        | StrategyLifecycleState.Faulted _, StrategyCommand.Resume ->
            invalid "Faulted strategies cannot resume."
        | StrategyLifecycleState.Faulted _, StrategyCommand.Pause ->
            invalid "Faulted strategies cannot pause."
        | StrategyLifecycleState.Faulted _, StrategyCommand.Stop ->
            valid StrategyLifecycleState.Stopped [ "faulted-strategy-stopped" ]
        | StrategyLifecycleState.Paused, StrategyCommand.Pause ->
            invalid "Strategy is already paused."
        | StrategyLifecycleState.WarmingUp, StrategyCommand.Resume
        | StrategyLifecycleState.Running, StrategyCommand.Resume ->
            invalid "Strategy is already running."
