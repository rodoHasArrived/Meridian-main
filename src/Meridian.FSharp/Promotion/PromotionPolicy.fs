module Meridian.FSharp.Promotion.PromotionPolicy

open Meridian.Backtesting.Sdk
open Meridian.FSharp.Promotion.PromotionTypes

type PromotionPolicyInput =
    { IsRunCompleted: bool
      HasMetrics: bool
      SharpeRatio: double
      MaxDrawdownPercent: decimal
      TotalReturn: decimal
      MinSharpeRatio: double
      MaxAllowedDrawdownPercent: decimal
      MinTotalReturn: decimal
      IsLiveTarget: bool
      HasCompleteTrustEvidence: bool
      HasFreshTrustEvidence: bool
      IsLiveExecutionEnabled: bool
      IsCircuitBreakerOpen: bool
      HasConflictingOverride: bool
      HasActiveLivePromotionOverride: bool
      RequiredManualOverrideKind: string }

let private passRule ruleId owner evidence isOverride precedence =
    { RuleId = ruleId
      Owner = owner
      RequiredEvidence = evidence
      Passed = true
      BlockingReason = None
      IsOverrideRule = isOverride
      Precedence = precedence }

let private failRule ruleId owner evidence reason isOverride precedence =
    { RuleId = ruleId
      Owner = owner
      RequiredEvidence = evidence
      Passed = false
      BlockingReason = Some reason
      IsOverrideRule = isOverride
      Precedence = precedence }

let evaluatePolicy (input: PromotionPolicyInput) : PromotionDecision =
    let baseRules =
        [
            if input.IsRunCompleted then
                passRule RunCompleted StrategyResearch [ RunLifecycleEvidence ] false 10
            else
                failRule RunCompleted StrategyResearch [ RunLifecycleEvidence ] "Run has not completed yet." false 10

            if input.HasMetrics then
                passRule MetricsPresent StrategyResearch [ BacktestMetricsEvidence ] false 20
            else
                failRule MetricsPresent StrategyResearch [ BacktestMetricsEvidence ] "Run has no metrics available for evaluation." false 20

            if input.SharpeRatio >= input.MinSharpeRatio then
                passRule SharpeThreshold StrategyResearch [ BacktestMetricsEvidence ] false 30
            else
                failRule SharpeThreshold StrategyResearch [ BacktestMetricsEvidence ] $"Sharpe ratio {input.SharpeRatio:F2} is below required {input.MinSharpeRatio:F2}." false 30

            if input.MaxDrawdownPercent <= input.MaxAllowedDrawdownPercent then
                passRule DrawdownThreshold StrategyResearch [ BacktestMetricsEvidence ] false 40
            else
                failRule DrawdownThreshold StrategyResearch [ BacktestMetricsEvidence ] $"Max drawdown {input.MaxDrawdownPercent:P2} exceeds allowed {input.MaxAllowedDrawdownPercent:P2}." false 40

            if input.TotalReturn >= input.MinTotalReturn then
                passRule TotalReturnThreshold StrategyResearch [ BacktestMetricsEvidence ] false 50
            else
                failRule TotalReturnThreshold StrategyResearch [ BacktestMetricsEvidence ] $"Total return {input.TotalReturn:P2} is below required {input.MinTotalReturn:P2}." false 50
        ]

    let liveRules =
        if not input.IsLiveTarget then
            []
        else
            [
                if input.HasFreshTrustEvidence then
                    passRule TrustEvidenceFresh GovernanceOperations [ ProviderTrustSnapshotEvidence ] false 60
                else
                    failRule TrustEvidenceFresh GovernanceOperations [ ProviderTrustSnapshotEvidence ] "Provider trust evidence is stale." false 60

                if input.HasCompleteTrustEvidence then
                    passRule TrustEvidenceComplete GovernanceOperations [ ProviderTrustSnapshotEvidence ] false 70
                else
                    failRule TrustEvidenceComplete GovernanceOperations [ ProviderTrustSnapshotEvidence ] "Provider trust evidence is incomplete." false 70

                if input.IsLiveExecutionEnabled then
                    passRule BrokerageLiveEnabled GovernanceOperations [ BrokerageConfigurationEvidence ] true 80
                else
                    failRule BrokerageLiveEnabled GovernanceOperations [ BrokerageConfigurationEvidence ] "Live execution is not enabled in the brokerage configuration." true 80

                if not input.IsCircuitBreakerOpen then
                    passRule CircuitBreakerClosed ExecutionControls [ CircuitBreakerEvidence ] true 90
                else
                    failRule CircuitBreakerClosed ExecutionControls [ CircuitBreakerEvidence ] "Paper -> Live promotion is blocked while the execution circuit breaker is open." true 90

                if not input.HasConflictingOverride then
                    passRule ConflictingOverrideClear ExecutionControls [ ManualOverrideEvidence ] true 100
                else
                    failRule ConflictingOverrideClear ExecutionControls [ ManualOverrideEvidence ] "A conflicting manual override is active for this run or strategy." true 100

                if input.HasActiveLivePromotionOverride then
                    passRule LivePromotionOverrideActive ExecutionControls [ ManualOverrideEvidence ] true 110
                else
                    failRule LivePromotionOverrideActive ExecutionControls [ ManualOverrideEvidence ] $"Paper -> Live promotion requires an active {input.RequiredManualOverrideKind} manual override." true 110
            ]

    let rules = [ yield! baseRules; yield! liveRules ] |> List.sortBy _.Precedence
    let blockingReasons = rules |> List.choose _.BlockingReason
    let hasCoreMetricFailures =
        rules
        |> List.exists (fun rule ->
            not rule.Passed
            && not rule.IsOverrideRule
            && (rule.RuleId = SharpeThreshold || rule.RuleId = DrawdownThreshold || rule.RuleId = TotalReturnThreshold))
    let hasReadinessFailures =
        rules |> List.exists (fun rule -> not rule.Passed && (rule.RuleId = RunCompleted || rule.RuleId = MetricsPresent))
    let hasOverrideFailures = rules |> List.exists (fun rule -> not rule.Passed && rule.IsOverrideRule)

    let outcome =
        if hasReadinessFailures then
            Blocked
        elif hasCoreMetricFailures then
            RequiresHumanReview
        elif hasOverrideFailures then
            RequiresManualOverride input.RequiredManualOverrideKind
        else
            Approved

    let isEligible = outcome = Approved
    let summary =
        match outcome with
        | Approved -> "Meets all promotion policy gates."
        | RequiresHumanReview -> "Promotion policy failed one or more quantitative gates and requires human review."
        | RequiresManualOverride _ -> "Promotion policy requires an active manual override before approval."
        | Blocked -> "Promotion policy blocked due to missing run readiness evidence."

    { Outcome = outcome
      IsEligible = isEligible
      BlockingReasons = blockingReasons
      Rules = rules
      Summary = summary }
