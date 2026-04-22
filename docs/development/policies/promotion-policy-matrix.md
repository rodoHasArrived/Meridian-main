# Strategy Promotion Policy Matrix

This matrix defines the single promotion-gating domain policy used by `PromotionService` orchestration and the F# policy kernel.

## Scope

- Source services inventoried: `PromotionService`, `BacktestToLivePromoter`, and `ExecutionOperatorControlService`.
- Target transitions: `Backtest -> Paper` and `Paper -> Live`.
- Policy evaluation owner: F# kernel (`Meridian.FSharp.Promotion.PromotionPolicy`).
- Manual-override authorization and audit ownership: C# orchestration (`PromotionService` + `ExecutionOperatorControlService`).

## Rule Matrix

| Gate ID | Applies To | Owner | Required Evidence | Pass Condition | Failure Outcome | Override Precedence |
|---|---|---|---|---|---|---|
| `RunCompleted` | All promotions | Strategy Research | Run lifecycle evidence | Run has `EndedAt` | Blocked | None |
| `MetricsPresent` | All promotions | Strategy Research | Backtest metrics evidence | Run has metrics payload | Blocked | None |
| `SharpeThreshold` | All promotions | Strategy Research | Backtest metrics evidence | `SharpeRatio >= MinSharpeRatio` | Human review required | Cannot be bypassed |
| `DrawdownThreshold` | All promotions | Strategy Research | Backtest metrics evidence | `MaxDrawdownPercent <= MaxAllowedDrawdownPercent` | Human review required | Cannot be bypassed |
| `TotalReturnThreshold` | All promotions | Strategy Research | Backtest metrics evidence | `TotalReturn >= MinTotalReturn` | Human review required | Cannot be bypassed |
| `TrustEvidenceFresh` | Paper -> Live | Governance Operations | Provider trust snapshot evidence | Trust snapshot considered fresh | Manual override required | Cannot bypass stale evidence |
| `TrustEvidenceComplete` | Paper -> Live | Governance Operations | Provider trust snapshot evidence | Trust evidence complete for live decision | Manual override required | Cannot bypass missing evidence |
| `BrokerageLiveEnabled` | Paper -> Live | Governance Operations | Brokerage config evidence | `LiveExecutionEnabled = true` | Manual override required | High (before approval) |
| `CircuitBreakerClosed` | Paper -> Live | Execution Controls | Circuit breaker evidence | Circuit breaker is closed | Manual override required | High (before approval) |
| `ConflictingOverrideClear` | Paper -> Live | Execution Controls | Manual override evidence | No conflicting `ForceBlockOrders` override for run/strategy | Manual override required | Higher than allow override |
| `LivePromotionOverrideActive` | Paper -> Live | Execution Controls | Manual override evidence | Active `AllowLivePromotion` override is present | Manual override required | Required final override gate |

## Override Precedence Model

1. **Readiness gates** (`RunCompleted`, `MetricsPresent`) block immediately.
2. **Quantitative performance gates** (`SharpeThreshold`, `DrawdownThreshold`, `TotalReturnThreshold`) escalate to human review if failed.
3. **Live governance gates** require explicit operator controls for `Paper -> Live`.
4. **Conflicting overrides win** over allow-promotion intent.
5. **Allow-live override is mandatory** to proceed toward final approval.

## Edge Cases Covered by Scenario Tests

- Conflicting overrides (`AllowLivePromotion` and `ForceBlockOrders`) active together.
- Partial trust/control evidence (no control snapshot available).
- Stale trust evidence path for live promotion.
- Missing allow-live override during approval.

## Implementation References

- F# policy kernel: `src/Meridian.FSharp/Promotion/PromotionPolicy.fs`
- Policy domain types: `src/Meridian.FSharp/Promotion/PromotionTypes.fs`
- C# orchestrator integration: `src/Meridian.Strategies/Services/PromotionService.cs`
- Promotion adapter bridge: `src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs`
- Control authorization: `src/Meridian.Execution/Services/ExecutionOperatorControlService.cs`
