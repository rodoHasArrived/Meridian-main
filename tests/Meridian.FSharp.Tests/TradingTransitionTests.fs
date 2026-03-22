module Meridian.FSharp.Tests.TradingTransitionTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Trading

[<Fact>]
let ``Start from registered enters warmup`` () =
    let result = StrategyLifecycleInterop.EvaluateStart("Registered")

    result.IsValid |> should equal true
    result.NextState |> should equal "WarmingUp"

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
