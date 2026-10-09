namespace Meridian.FSharp.Trading

open System
open System.Runtime.CompilerServices

[<CLIMutable>]
type StrategyTransitionDto = {
    PreviousState: string
    NextState: string
    PreviousFaultReason: string
    FaultReason: string
    IsValid: bool
    Reason: string
    EmittedFacts: string array
}

[<Sealed; Extension>]
type StrategyLifecycleInterop private () =

    static member private stateName state =
        match state with
        | StrategyLifecycleState.Registered -> "Registered"
        | StrategyLifecycleState.WarmingUp -> "WarmingUp"
        | StrategyLifecycleState.Running -> "Running"
        | StrategyLifecycleState.Paused -> "Paused"
        | StrategyLifecycleState.Stopping -> "Stopping"
        | StrategyLifecycleState.Stopped -> "Stopped"
        | StrategyLifecycleState.Faulted _ -> "Faulted"

    static member private faultReason state =
        match state with
        | StrategyLifecycleState.Faulted reason -> Option.ofObj reason |> Option.defaultValue String.Empty
        | _ -> String.Empty

    static member private fromName (name: string, currentFaultReason: string) =
        match name with
        | "Registered" -> Ok StrategyLifecycleState.Registered
        | "WarmingUp" -> Ok StrategyLifecycleState.WarmingUp
        | "Running" -> Ok StrategyLifecycleState.Running
        | "Paused" -> Ok StrategyLifecycleState.Paused
        | "Stopping" -> Ok StrategyLifecycleState.Stopping
        | "Stopped" -> Ok StrategyLifecycleState.Stopped
        | "Faulted" ->
            Ok (StrategyLifecycleState.Faulted (Option.ofObj currentFaultReason |> Option.defaultValue String.Empty))
        | _ when String.IsNullOrWhiteSpace name -> Error "A strategy lifecycle state is required."
        | _ -> Error $"Unsupported strategy state '{name}'."

    static member private toDto (result: TransitionResult) : StrategyTransitionDto =
        {
            PreviousState = StrategyLifecycleInterop.stateName result.PreviousState
            NextState = StrategyLifecycleInterop.stateName result.NextState
            PreviousFaultReason = StrategyLifecycleInterop.faultReason result.PreviousState
            FaultReason = StrategyLifecycleInterop.faultReason result.NextState
            IsValid = result.IsValid
            Reason = result.Reason |> Option.defaultValue String.Empty
            EmittedFacts = result.EmittedFacts |> List.toArray
        }

    static member private evaluate(command, currentState: string, currentFaultReason: string) =
        match StrategyLifecycleInterop.fromName(currentState, currentFaultReason) with
        | Ok state ->
            StrategyLifecycleTransitions.apply command state
            |> StrategyLifecycleInterop.toDto
        | Error reason ->
            let unchangedState = Option.ofObj currentState |> Option.defaultValue String.Empty
            {
                PreviousState = unchangedState
                NextState = unchangedState
                PreviousFaultReason = String.Empty
                FaultReason = String.Empty
                IsValid = false
                Reason = reason
                EmittedFacts = [||]
            }

    static member EvaluateStart(currentState: string, currentFaultReason: string) =
        let command =
            match currentState with
            | "Paused" -> StrategyCommand.Resume
            | _ -> StrategyCommand.Start
        StrategyLifecycleInterop.evaluate(command, currentState, currentFaultReason)

    static member EvaluateStart(currentState: string) =
        StrategyLifecycleInterop.EvaluateStart(currentState, String.Empty)

    static member EvaluateWarmupCompleted(currentState: string, currentFaultReason: string) =
        StrategyLifecycleInterop.evaluate(StrategyCommand.WarmupCompleted, currentState, currentFaultReason)

    static member EvaluateWarmupCompleted(currentState: string) =
        StrategyLifecycleInterop.EvaluateWarmupCompleted(currentState, String.Empty)

    static member EvaluatePause(currentState: string, currentFaultReason: string) =
        StrategyLifecycleInterop.evaluate(StrategyCommand.Pause, currentState, currentFaultReason)

    static member EvaluatePause(currentState: string) =
        StrategyLifecycleInterop.EvaluatePause(currentState, String.Empty)

    static member EvaluateStop(currentState: string, currentFaultReason: string) =
        StrategyLifecycleInterop.evaluate(StrategyCommand.Stop, currentState, currentFaultReason)

    static member EvaluateStop(currentState: string) =
        StrategyLifecycleInterop.EvaluateStop(currentState, String.Empty)

    static member EvaluateStopCompleted(currentState: string, currentFaultReason: string) =
        StrategyLifecycleInterop.evaluate(StrategyCommand.StopCompleted, currentState, currentFaultReason)

    static member EvaluateStopCompleted(currentState: string) =
        StrategyLifecycleInterop.EvaluateStopCompleted(currentState, String.Empty)

    static member EvaluateFail(currentState: string, currentFaultReason: string, failureReason: string) =
        StrategyLifecycleInterop.evaluate(StrategyCommand.Fail failureReason, currentState, currentFaultReason)

    static member EvaluateFail(currentState: string, failureReason: string) =
        StrategyLifecycleInterop.EvaluateFail(currentState, String.Empty, failureReason)
