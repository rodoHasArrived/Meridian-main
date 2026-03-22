module Meridian.FSharp.Promotion.PromotionPolicy

open Meridian.Backtesting.Sdk
open Meridian.FSharp.Promotion.PromotionTypes

let private missesByMargin (actual: decimal) (threshold: decimal) =
    threshold > 0m && actual >= threshold * 0.9m

let private missesByMarginFloat (actual: double) (threshold: double) =
    threshold > 0.0 && actual >= threshold * 0.9

let evaluate (result: BacktestResult) (minSharpeRatio: double) (maxAllowedDrawdownPercent: decimal) (minTotalReturn: decimal) : PromotionDecision =
    let metrics = result.Metrics

    let failures =
        [
            if metrics.SharpeRatio < minSharpeRatio then
                $"Sharpe ratio {metrics.SharpeRatio:F2} is below required {minSharpeRatio:F2}"
            if metrics.MaxDrawdownPercent > maxAllowedDrawdownPercent then
                $"Max drawdown {metrics.MaxDrawdownPercent:P2} exceeds allowed {maxAllowedDrawdownPercent:P2}"
            if metrics.TotalReturn < minTotalReturn then
                $"Total return {metrics.TotalReturn:P2} is below required {minTotalReturn:P2}"
        ]

    if List.isEmpty failures then
        Eligible
    else
        let nearMiss =
            (metrics.SharpeRatio < minSharpeRatio && missesByMarginFloat metrics.SharpeRatio minSharpeRatio)
            || (metrics.MaxDrawdownPercent > maxAllowedDrawdownPercent && metrics.MaxDrawdownPercent <= maxAllowedDrawdownPercent * 1.1m)
            || (metrics.TotalReturn < minTotalReturn && missesByMargin metrics.TotalReturn minTotalReturn)

        if nearMiss then ManualReview failures else Ineligible failures
