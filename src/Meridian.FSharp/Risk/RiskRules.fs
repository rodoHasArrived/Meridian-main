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

/// Projected gross exposure of the order's symbol after the order executes.
/// Direction-aware: the current contribution-level gross is preserved and only the net
/// component moves with the order, so a sell that reduces a long (or a buy that covers a
/// short) shrinks the projection — including orders that cross through zero — while
/// offsetting long/short lots keep contributing their full gross. Falls back to the
/// additive worst case when the signed current position is unknown.
let projectedSymbolAbsoluteExposure (ctx: RiskContext) : decimal =
    let currentGross = ctx.SymbolExposure |> Option.defaultValue 0m
    match ctx.SignedSymbolExposure, ctx.SignedOrderNotional with
    | Some signedSymbol, Some signedOrder ->
        max 0m (currentGross - abs signedSymbol + abs (signedSymbol + signedOrder))
    | _ -> currentGross + (ctx.OrderNotional |> Option.defaultValue 0m)

/// Rejects an order whose notional would push the portfolio-wide gross exposure over the
/// configured ceiling. Direction-aware: de-risking orders reduce the projection.
/// Missing exposure data or an unconfigured ceiling approves.
let grossExposureLimit (ctx: RiskContext) : RiskDecision =
    match ctx.PortfolioExposure, ctx.MaxGrossExposure with
    | Some grossExposure, Some maxGrossExposure when maxGrossExposure > 0m ->
        let currentSymbolAbs = ctx.SymbolExposure |> Option.defaultValue 0m
        let projectedSymbolAbs = projectedSymbolAbsoluteExposure ctx
        let projected = max 0m (grossExposure - currentSymbolAbs + projectedSymbolAbs)
        if projected > maxGrossExposure then
            Reject (sprintf "Gross exposure limit: projected %.2f exceeds %.2f ceiling" (float projected) (float maxGrossExposure))
        else
            Approve
    | _ -> Approve

/// Rejects an order that would concentrate a single symbol beyond the configured
/// percentage of portfolio value. Direction-aware: reducing orders lower the projected
/// concentration. Requires a positive portfolio value to be meaningful.
let symbolConcentration (ctx: RiskContext) : RiskDecision =
    match ctx.PortfolioValue, ctx.MaxSymbolConcentrationPercent with
    | Some portfolioValue, Some maxPercent when portfolioValue > 0m && maxPercent > 0m ->
        let projected = projectedSymbolAbsoluteExposure ctx
        let projectedPercent = (projected / portfolioValue) * 100m
        if projectedPercent > maxPercent then
            Reject (sprintf "Concentration limit: %s at %.2f%% of portfolio value exceeds %.2f%% cap" ctx.Request.Symbol (float projectedPercent) (float maxPercent))
        else
            Approve
    | _ -> Approve

/// Gates per-order notional: above the hard ceiling rejects outright; inside the
/// escalation band the order is parked for governed approval instead of routed.
let orderNotional (ctx: RiskContext) : RiskDecision =
    match ctx.OrderNotional with
    | Some notional ->
        match ctx.MaxOrderNotional with
        | Some maxNotional when maxNotional > 0m && notional > maxNotional ->
            Reject (sprintf "Order notional limit: %.2f exceeds %.2f ceiling" (float notional) (float maxNotional))
        | _ ->
            match ctx.EscalateOrderNotional with
            | Some escalateAt when escalateAt > 0m && notional >= escalateAt ->
                Escalate (sprintf "Order notional %.2f is at or above the %.2f governed-approval band" (float notional) (float escalateAt))
            | _ -> Approve
    | None -> Approve
