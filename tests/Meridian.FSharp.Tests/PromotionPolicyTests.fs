module Meridian.FSharp.Tests.PromotionPolicyTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Interop
open Meridian.FSharp.Promotion.PromotionPolicy

let private livePolicyInput hasOverride =
    {
        IsRunCompleted = true
        HasMetrics = true
        SharpeRatio = 1.4
        MaxDrawdownPercent = 0.08m
        TotalReturn = 0.18m
        MinSharpeRatio = 0.5
        MaxAllowedDrawdownPercent = 0.25m
        MinTotalReturn = 0.0m
        IsLiveTarget = true
        HasCompleteTrustEvidence = true
        HasFreshTrustEvidence = true
        IsLiveExecutionEnabled = true
        IsCircuitBreakerOpen = false
        HasConflictingOverride = false
        HasActiveLivePromotionOverride = hasOverride
        RequiredManualOverrideKind = "AllowLivePromotion"
        RequireWalkForwardEvidence = true
        HasWalkForwardEvidence = true
        OutOfSampleSharpeRatio = 1.0
        WalkForwardDegradationRatio = 0.8
        MinOutOfSampleSharpe = 0.0
        MinWalkForwardDegradationRatio = 0.5
        OutOfSampleMaxDrawdownPercent = 0.10m
        MaxOutOfSampleDrawdownPercent = 0.25m
    }

[<Fact>]
let ``Live promotion policy rejects excessive out-of-sample drawdown`` () =
    let input = { livePolicyInput true with OutOfSampleMaxDrawdownPercent = 0.40m }
    let decision = PromotionInterop.EvaluatePromotionPolicy(input)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_human_review"

    decision.Reasons
    |> Array.exists (fun reason -> reason.Contains("Out-of-sample max drawdown"))
    |> should equal true

[<Fact>]
let ``Live promotion policy requires walk-forward evidence when none is recorded`` () =
    let input = { livePolicyInput true with HasWalkForwardEvidence = false }
    let decision = PromotionInterop.EvaluatePromotionPolicy(input)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_human_review"
    decision.Reasons |> should contain "No walk-forward/out-of-sample evidence is recorded for this run."

[<Fact>]
let ``Live promotion policy rejects weak out-of-sample sharpe`` () =
    let input =
        { livePolicyInput true with
            OutOfSampleSharpeRatio = -0.3
            MinOutOfSampleSharpe = 0.0 }
    let decision = PromotionInterop.EvaluatePromotionPolicy(input)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_human_review"

[<Fact>]
let ``Live promotion policy rejects excessive walk-forward degradation`` () =
    let input = { livePolicyInput true with WalkForwardDegradationRatio = 0.2 }
    let decision = PromotionInterop.EvaluatePromotionPolicy(input)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_human_review"

[<Fact>]
let ``Promotion policy skips walk-forward gates when evidence is not required and absent`` () =
    let input =
        { livePolicyInput true with
            RequireWalkForwardEvidence = false
            HasWalkForwardEvidence = false }
    let decision = PromotionInterop.EvaluatePromotionPolicy(input)

    decision.Eligible |> should equal true

[<Fact>]
let ``Live promotion policy requires manual override when override is missing`` () =
    let decision = PromotionInterop.EvaluatePromotionPolicy(livePolicyInput false)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_manual_override"
    decision.RequiredManualOverrideKind |> should equal "AllowLivePromotion"
    decision.Reasons |> should contain "Paper -> Live promotion requires an active AllowLivePromotion manual override."

[<Fact>]
let ``Live promotion policy approves when override is active`` () =
    let decision = PromotionInterop.EvaluatePromotionPolicy(livePolicyInput true)

    decision.Eligible |> should equal true
    decision.Outcome |> should equal "approved"
    decision.Reasons.Length |> should equal 0
