namespace Meridian.FSharp.Trading

open System
open System.Runtime.CompilerServices

[<CLIMutable>]
type StrategyTransitionDto = {
    PreviousState: string
    NextState: string
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

    static member private fromName (name: string) =
        match name with
        | null
        | "" -> StrategyLifecycleState.Registered
        | "Registered" -> StrategyLifecycleState.Registered
        | "WarmingUp" -> StrategyLifecycleState.WarmingUp
        | "Running" -> StrategyLifecycleState.Running
        | "Paused" -> StrategyLifecycleState.Paused
        | "Stopping" -> StrategyLifecycleState.Stopping
        | "Stopped" -> StrategyLifecycleState.Stopped
        | "Faulted" -> StrategyLifecycleState.Faulted "Faulted"
        | _ -> invalidArg (nameof name) $"Unsupported strategy state '{name}'."

    static member private toDto (result: TransitionResult) : StrategyTransitionDto =
        {
            PreviousState = StrategyLifecycleInterop.stateName result.PreviousState
            NextState = StrategyLifecycleInterop.stateName result.NextState
            IsValid = result.IsValid
            Reason = result.Reason |> Option.defaultValue String.Empty
            EmittedFacts = result.EmittedFacts |> List.toArray
        }

    static member EvaluateStart(currentState: string) =
        let command =
            match currentState with
            | "Paused" -> StrategyCommand.Resume
            | _ -> StrategyCommand.Start
        StrategyLifecycleTransitions.apply command (StrategyLifecycleInterop.fromName currentState)
        |> StrategyLifecycleInterop.toDto

    static member EvaluatePause(currentState: string) =
        StrategyLifecycleTransitions.apply StrategyCommand.Pause (StrategyLifecycleInterop.fromName currentState)
        |> StrategyLifecycleInterop.toDto

    static member EvaluateStop(currentState: string) =
        StrategyLifecycleTransitions.apply StrategyCommand.Stop (StrategyLifecycleInterop.fromName currentState)
        |> StrategyLifecycleInterop.toDto
