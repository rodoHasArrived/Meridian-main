module Meridian.FSharp.Risk.RiskRules

open Meridian.Execution.Sdk
open Meridian.FSharp.Risk.RiskTypes

let positionLimit (ctx: RiskContext) : RiskDecision =
    match ctx.MaxPositionSize with
    | None -> Approve
    | Some maxPositionSize ->
        let signedQuantity =
            match ctx.Request.Side with
            | OrderSide.Buy -> ctx.Request.Quantity
            | OrderSide.Sell -> -ctx.Request.Quantity
            | _ -> 0m

        let projectedQuantity = ctx.CurrentPositionQuantity + signedQuantity
        if abs projectedQuantity > maxPositionSize then
            Reject $"Position limit exceeded: projected {projectedQuantity} > max {maxPositionSize} for {ctx.Request.Symbol}"
        else
            Approve

let drawdownCircuitBreaker (ctx: RiskContext) : RiskDecision =
    match ctx.PortfolioValue, ctx.InitialCapital, ctx.MaxDrawdownPercent with
    | Some portfolioValue, Some initialCapital, Some maxDrawdownPercent when initialCapital > 0m ->
        let drawdownPercent = ((initialCapital - portfolioValue) / initialCapital) * 100m
        if drawdownPercent >= maxDrawdownPercent then
            Reject (sprintf "Drawdown circuit breaker: %.2f%% drawdown exceeds %.2f%% threshold" (float drawdownPercent) (float maxDrawdownPercent))
        else
            Approve
    | _ -> Approve
