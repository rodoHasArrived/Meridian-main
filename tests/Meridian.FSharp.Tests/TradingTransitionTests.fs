module Meridian.FSharp.Tests.TradingTransitionTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Trading

let private failureReason = "Market data provider disconnected."

let private commands =
    [ StrategyCommand.Start
      StrategyCommand.WarmupCompleted
      StrategyCommand.Pause
      StrategyCommand.Resume
      StrategyCommand.Stop
      StrategyCommand.StopCompleted
      StrategyCommand.Fail failureReason ]

[<Fact>]
let ``Every state command pair preserves lifecycle invariants`` () =
    let faulted = StrategyLifecycleState.Faulted failureReason
    let expectedTransitions =
        [ StrategyLifecycleState.Registered,
          [ Some StrategyLifecycleState.WarmingUp; None; None; None; None; None; Some faulted ]
          StrategyLifecycleState.WarmingUp,
          [ None; Some StrategyLifecycleState.Running; None; None; Some StrategyLifecycleState.Stopping; None; Some faulted ]
          StrategyLifecycleState.Running,
          [ None; None; Some StrategyLifecycleState.Paused; None; Some StrategyLifecycleState.Stopping; None; Some faulted ]
          StrategyLifecycleState.Paused,
          [ None; None; None; Some StrategyLifecycleState.Running; Some StrategyLifecycleState.Stopping; None; Some faulted ]
          StrategyLifecycleState.Stopping,
          [ None; None; None; None; None; Some StrategyLifecycleState.Stopped; Some faulted ]
          StrategyLifecycleState.Stopped,
          [ Some StrategyLifecycleState.WarmingUp; None; None; None; None; None; Some faulted ]
          faulted,
          [ None; None; None; None; Some StrategyLifecycleState.Stopping; None; Some faulted ] ]

    for state, expectedStates in expectedTransitions do
        for command, expectedState in List.zip commands expectedStates do
            let result = StrategyLifecycleTransitions.apply command state
            let context = $"State {state}, command {command}."
            Assert.True(result.PreviousState = state, context)

            match expectedState with
            | Some expected ->
                Assert.True(result.IsValid, context)
                Assert.True(result.NextState = expected, context)
                Assert.True(Option.isNone result.Reason, context)
                Assert.NotEmpty(result.EmittedFacts)
            | None ->
                Assert.False(result.IsValid, context)
                Assert.True(result.NextState = state, context)
                Assert.True(result.Reason |> Option.exists (System.String.IsNullOrWhiteSpace >> not), context)
                Assert.Empty(result.EmittedFacts)

[<Fact>]
let ``Start from registered enters warmup`` () =
    let result = StrategyLifecycleInterop.EvaluateStart("Registered")

    result.IsValid |> should equal true
    result.NextState |> should equal "WarmingUp"
    result.EmittedFacts |> Array.toList |> should equal [ "strategy-start-requested"; "strategy-warmup-entered" ]
    result.PreviousFaultReason |> should equal ""
    result.FaultReason |> should equal ""

[<Fact>]
let ``Pause from registered is invalid`` () =
    let result = StrategyLifecycleInterop.EvaluatePause("Registered")

    result.IsValid |> should equal false
    result.Reason.Contains("Cannot pause") |> should equal true

[<Fact>]
let ``Start from paused is treated as resume`` () =
    let result = StrategyLifecycleInterop.EvaluateStart("Paused")

    result.IsValid |> should equal true
    result.NextState |> should equal "Running"

[<Fact>]
let ``A strategy can run only after warmup completes`` () =
    let started = StrategyLifecycleTransitions.apply StrategyCommand.Start StrategyLifecycleState.Registered
    let paused = StrategyLifecycleTransitions.apply StrategyCommand.Pause started.NextState
    let resumed = StrategyLifecycleTransitions.apply StrategyCommand.Resume paused.NextState
    let warmed = StrategyLifecycleTransitions.apply StrategyCommand.WarmupCompleted resumed.NextState

    paused.IsValid |> should equal false
    paused.NextState |> should equal StrategyLifecycleState.WarmingUp
    resumed.IsValid |> should equal false
    resumed.NextState |> should equal StrategyLifecycleState.WarmingUp
    warmed.IsValid |> should equal true
    warmed.NextState |> should equal StrategyLifecycleState.Running
    warmed.EmittedFacts |> should equal [ "strategy-warmup-completed"; "strategy-running-entered" ]

[<Fact>]
let ``Pausing and resuming a warmed strategy preserves running state`` () =
    let paused = StrategyLifecycleTransitions.apply StrategyCommand.Pause StrategyLifecycleState.Running
    let resumed = StrategyLifecycleTransitions.apply StrategyCommand.Resume paused.NextState

    paused.IsValid |> should equal true
    paused.NextState |> should equal StrategyLifecycleState.Paused
    paused.EmittedFacts |> should equal [ "strategy-paused" ]
    resumed.IsValid |> should equal true
    resumed.NextState |> should equal StrategyLifecycleState.Running
    resumed.EmittedFacts |> should equal [ "strategy-resume-requested"; "strategy-running-entered" ]

[<Fact>]
let ``Stop remains in progress until cleanup completes`` () =
    for state in [ StrategyLifecycleState.WarmingUp; StrategyLifecycleState.Running; StrategyLifecycleState.Paused ] do
        let stopping = StrategyLifecycleTransitions.apply StrategyCommand.Stop state
        let repeated = StrategyLifecycleTransitions.apply StrategyCommand.Stop stopping.NextState
        let prematureStart = StrategyLifecycleTransitions.apply StrategyCommand.Start stopping.NextState
        let staleWarmup = StrategyLifecycleTransitions.apply StrategyCommand.WarmupCompleted stopping.NextState
        let stopped = StrategyLifecycleTransitions.apply StrategyCommand.StopCompleted stopping.NextState
        let restarted = StrategyLifecycleTransitions.apply StrategyCommand.Start stopped.NextState

        stopping.IsValid |> should equal true
        stopping.NextState |> should equal StrategyLifecycleState.Stopping
        stopping.EmittedFacts |> should equal [ "strategy-stop-requested"; "strategy-stopping-entered" ]
        repeated.IsValid |> should equal false
        prematureStart.IsValid |> should equal false
        staleWarmup.IsValid |> should equal false
        stopped.IsValid |> should equal true
        stopped.NextState |> should equal StrategyLifecycleState.Stopped
        stopped.EmittedFacts |> should equal [ "strategy-stopped" ]
        restarted.IsValid |> should equal true
        restarted.NextState |> should equal StrategyLifecycleState.WarmingUp
        restarted.EmittedFacts |> should equal [ "strategy-restart-requested"; "strategy-warmup-entered" ]

[<Fact>]
let ``Fault recovery requires successful cleanup before restarting`` () =
    let failed = StrategyLifecycleTransitions.apply (StrategyCommand.Fail failureReason) StrategyLifecycleState.Running
    let prematureStart = StrategyLifecycleTransitions.apply StrategyCommand.Start failed.NextState
    let prematureCompletion = StrategyLifecycleTransitions.apply StrategyCommand.StopCompleted failed.NextState
    let stopping = StrategyLifecycleTransitions.apply StrategyCommand.Stop failed.NextState
    let cleanupFailed = StrategyLifecycleTransitions.apply (StrategyCommand.Fail "Cleanup failed.") stopping.NextState
    let retryStart = StrategyLifecycleTransitions.apply StrategyCommand.Start cleanupFailed.NextState
    let retryStop = StrategyLifecycleTransitions.apply StrategyCommand.Stop cleanupFailed.NextState
    let stopped = StrategyLifecycleTransitions.apply StrategyCommand.StopCompleted retryStop.NextState
    let restarted = StrategyLifecycleTransitions.apply StrategyCommand.Start stopped.NextState
    let running = StrategyLifecycleTransitions.apply StrategyCommand.WarmupCompleted restarted.NextState

    failed.NextState |> should equal (StrategyLifecycleState.Faulted failureReason)
    failed.EmittedFacts |> should equal [ "strategy-faulted" ]
    prematureStart.IsValid |> should equal false
    prematureStart.Reason |> should equal (Some "Faulted strategies must be stopped successfully before restarting.")
    prematureCompletion.IsValid |> should equal false
    stopping.NextState |> should equal StrategyLifecycleState.Stopping
    cleanupFailed.NextState |> should equal (StrategyLifecycleState.Faulted "Cleanup failed.")
    retryStart.IsValid |> should equal false
    retryStop.IsValid |> should equal true
    stopped.IsValid |> should equal true
    restarted.IsValid |> should equal true
    running.IsValid |> should equal true
    running.NextState |> should equal StrategyLifecycleState.Running

[<Fact>]
let ``Blank failures never mutate state or emit facts`` () =
    let states =
        [ StrategyLifecycleState.Registered; StrategyLifecycleState.WarmingUp; StrategyLifecycleState.Running
          StrategyLifecycleState.Paused; StrategyLifecycleState.Stopping; StrategyLifecycleState.Stopped
          StrategyLifecycleState.Faulted failureReason ]

    for state in states do
        for reason in [ null; ""; " "; "\t\n" ] do
            let result = StrategyLifecycleTransitions.apply (StrategyCommand.Fail reason) state
            result.IsValid |> should equal false
            result.PreviousState |> should equal state
            result.NextState |> should equal state
            result.Reason |> should equal (Some "A strategy failure must include a nonblank reason.")
            Assert.Empty(result.EmittedFacts)

[<Fact>]
let ``Interop completion signals use the same kernel transitions`` () =
    let started = StrategyLifecycleInterop.EvaluateStart("Registered")
    let running = StrategyLifecycleInterop.EvaluateWarmupCompleted(started.NextState)
    let stopping = StrategyLifecycleInterop.EvaluateStop(running.NextState)
    let stopped = StrategyLifecycleInterop.EvaluateStopCompleted(stopping.NextState)

    running.IsValid |> should equal true
    running.NextState |> should equal "Running"
    running.EmittedFacts |> Array.toList |> should equal [ "strategy-warmup-completed"; "strategy-running-entered" ]
    stopping.IsValid |> should equal true
    stopping.NextState |> should equal "Stopping"
    stopping.EmittedFacts |> Array.toList |> should equal [ "strategy-stop-requested"; "strategy-stopping-entered" ]
    stopped.IsValid |> should equal true
    stopped.NextState |> should equal "Stopped"
    stopped.EmittedFacts |> Array.toList |> should equal [ "strategy-stopped" ]

[<Fact>]
let ``All interop methods reject missing or unsupported states without throwing`` () =
    let methods: (string * (string -> StrategyTransitionDto)) list =
        [ "Start", fun state -> StrategyLifecycleInterop.EvaluateStart(state)
          "Start with fault", fun state -> StrategyLifecycleInterop.EvaluateStart(state, failureReason)
          "Pause", fun state -> StrategyLifecycleInterop.EvaluatePause(state)
          "Pause with fault", fun state -> StrategyLifecycleInterop.EvaluatePause(state, failureReason)
          "Stop", fun state -> StrategyLifecycleInterop.EvaluateStop(state)
          "Stop with fault", fun state -> StrategyLifecycleInterop.EvaluateStop(state, failureReason)
          "Warmup completed", fun state -> StrategyLifecycleInterop.EvaluateWarmupCompleted(state)
          "Warmup completed with fault", fun state -> StrategyLifecycleInterop.EvaluateWarmupCompleted(state, failureReason)
          "Stop completed", fun state -> StrategyLifecycleInterop.EvaluateStopCompleted(state)
          "Stop completed with fault", fun state -> StrategyLifecycleInterop.EvaluateStopCompleted(state, failureReason)
          "Fail", fun state -> StrategyLifecycleInterop.EvaluateFail(state, failureReason)
          "Fail with prior fault", fun state -> StrategyLifecycleInterop.EvaluateFail(state, "Prior failure.", failureReason) ]

    for name, evaluate in methods do
        for state in [ null; ""; " "; "\t\n"; "Unknown"; "running"; " Running"; "Running "; "FAULTED" ] do
            let result = evaluate state
            let expectedState = if isNull state then "" else state
            let context = $"Method {name}, state '{state}'."
            Assert.False(result.IsValid, context)
            Assert.True(result.PreviousState = expectedState, context)
            Assert.True(result.NextState = expectedState, context)
            Assert.False(System.String.IsNullOrWhiteSpace result.Reason, context)
            result.PreviousFaultReason |> should equal ""
            result.FaultReason |> should equal ""
            Assert.Empty(result.EmittedFacts)

[<Fact>]
let ``Invalid faulted interop transitions retain the original diagnostic`` () =
    let results =
        [ StrategyLifecycleInterop.EvaluateStart("Faulted", failureReason)
          StrategyLifecycleInterop.EvaluatePause("Faulted", failureReason)
          StrategyLifecycleInterop.EvaluateWarmupCompleted("Faulted", failureReason)
          StrategyLifecycleInterop.EvaluateStopCompleted("Faulted", failureReason) ]

    for result in results do
        result.IsValid |> should equal false
        result.PreviousState |> should equal "Faulted"
        result.NextState |> should equal "Faulted"
        result.PreviousFaultReason |> should equal failureReason
        result.FaultReason |> should equal failureReason
        Assert.Empty(result.EmittedFacts)

[<Fact>]
let ``Interop records both prior and replacement fault diagnostics`` () =
    let failed = StrategyLifecycleInterop.EvaluateFail("Running", failureReason)
    let replaced = StrategyLifecycleInterop.EvaluateFail(failed.NextState, failed.FaultReason, "Cleanup failed.")
    let stopping = StrategyLifecycleInterop.EvaluateStop(replaced.NextState, replaced.FaultReason)
    let stopped = StrategyLifecycleInterop.EvaluateStopCompleted(stopping.NextState, stopping.FaultReason)

    failed.IsValid |> should equal true
    failed.PreviousFaultReason |> should equal ""
    failed.FaultReason |> should equal failureReason
    failed.Reason |> should equal ""
    replaced.IsValid |> should equal true
    replaced.PreviousFaultReason |> should equal failureReason
    replaced.FaultReason |> should equal "Cleanup failed."
    stopping.IsValid |> should equal true
    stopping.PreviousFaultReason |> should equal "Cleanup failed."
    stopping.FaultReason |> should equal ""
    stopped.IsValid |> should equal true
    stopped.FaultReason |> should equal ""

[<Fact>]
let ``Interop preserves fault diagnostics when a failure reason is invalid`` () =
    for reason in [ null; ""; " "; "\t\n" ] do
        let result = StrategyLifecycleInterop.EvaluateFail("Faulted", failureReason, reason)
        result.IsValid |> should equal false
        result.PreviousState |> should equal "Faulted"
        result.NextState |> should equal "Faulted"
        result.PreviousFaultReason |> should equal failureReason
        result.FaultReason |> should equal failureReason
        Assert.Empty(result.EmittedFacts)

[<Fact>]
let ``Legacy faulted methods do not fabricate a fault reason`` () =
    let rejected = StrategyLifecycleInterop.EvaluateStart("Faulted")
    let stopping = StrategyLifecycleInterop.EvaluateStop("Faulted")
    let missingReason = StrategyLifecycleInterop.EvaluatePause("Faulted", null)

    rejected.IsValid |> should equal false
    rejected.PreviousFaultReason |> should equal ""
    rejected.FaultReason |> should equal ""
    stopping.IsValid |> should equal true
    stopping.PreviousFaultReason |> should equal ""
    stopping.FaultReason |> should equal ""
    missingReason.IsValid |> should equal false
    missingReason.PreviousFaultReason |> should equal ""
    missingReason.FaultReason |> should equal ""

[<Fact>]
let ``Fault detail supplied for a healthy state is not attached to the transition`` () =
    let result = StrategyLifecycleInterop.EvaluateStart("Registered", failureReason)
    result.IsValid |> should equal true
    result.PreviousFaultReason |> should equal ""
    result.FaultReason |> should equal ""

[<Fact>]
let ``Stopping is not terminal until cleanup finishes`` () =
    PromotionReadiness.isTerminal StrategyLifecycleState.Stopping |> should equal false
    PromotionReadiness.isTerminal StrategyLifecycleState.Stopped |> should equal true
    PromotionReadiness.isTerminal (StrategyLifecycleState.Faulted failureReason) |> should equal true
