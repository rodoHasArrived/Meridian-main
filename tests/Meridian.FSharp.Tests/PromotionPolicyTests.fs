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
    }

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
