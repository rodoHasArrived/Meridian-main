module Meridian.FSharp.Promotion.PromotionTypes

type PromotionRuleId =
    | RunCompleted
    | MetricsPresent
    | SharpeThreshold
    | DrawdownThreshold
    | TotalReturnThreshold
    | TrustEvidenceFresh
    | TrustEvidenceComplete
    | BrokerageLiveEnabled
    | CircuitBreakerClosed
    | ConflictingOverrideClear
    | LivePromotionOverrideActive

type RuleOwner =
    | StrategyResearch
    | GovernanceOperations
    | ExecutionControls

type PromotionEvidenceKind =
    | RunLifecycleEvidence
    | BacktestMetricsEvidence
    | ProviderTrustSnapshotEvidence
    | BrokerageConfigurationEvidence
    | CircuitBreakerEvidence
    | ManualOverrideEvidence

type RuleEvaluation =
    { RuleId: PromotionRuleId
      Owner: RuleOwner
      RequiredEvidence: PromotionEvidenceKind list
      Passed: bool
      BlockingReason: string option
      IsOverrideRule: bool
      Precedence: int }

type PromotionDecisionOutcome =
    | Approved
    | RequiresManualOverride of kind: string
    | RequiresHumanReview
    | Blocked

type PromotionDecision =
    { Outcome: PromotionDecisionOutcome
      IsEligible: bool
      BlockingReasons: string list
      Rules: RuleEvaluation list
      Summary: string }
