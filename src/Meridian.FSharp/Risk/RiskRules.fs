module Meridian.FSharp.Risk.RiskRules

open Meridian.Execution.Sdk
open Meridian.FSharp.Risk.RiskTypes

let positionLimit (ctx: RiskContext) : RiskDecision =
    match ctx.MaxPositionSize with
    | None -> Approve
    | Some maxPositionSize ->
        // Anything that is not a Buy is oriented as a sell, rather than matched exhaustively
        // against Sell alone. OrderSide is an enum over the wire and System.Text.Json accepts
        // undefined numeric values, so a third value can reach here — and it does not route as
        // "neither": AlpacaBrokerageGateway maps every non-Buy value to "sell". A neutral arm
        // would contribute zero to this projection, so a real short would pass the position
        // ceiling as though it changed nothing. Measure what routes.
        let signedQuantity =
            match ctx.Request.Side with
            | OrderSide.Buy -> ctx.Request.Quantity
            | _ -> -ctx.Request.Quantity

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
/// configured ceiling. Direction-aware: de-risking orders reduce the projection, and an
/// order that strictly reduces gross exposure is always permitted — when a market move or
/// a tightened threshold leaves the book already above the ceiling, the desk must still
/// be able to unwind incrementally rather than being locked out (and, at Critical
/// severity, tripping the breaker on its own de-risking attempts).
/// Missing exposure data or an unconfigured ceiling approves.
let grossExposureLimit (ctx: RiskContext) : RiskDecision =
    match ctx.PortfolioExposure, ctx.MaxGrossExposure with
    | Some grossExposure, Some maxGrossExposure when maxGrossExposure > 0m ->
        let currentSymbolAbs = ctx.SymbolExposure |> Option.defaultValue 0m
        let projectedSymbolAbs = projectedSymbolAbsoluteExposure ctx
        let projected = max 0m (grossExposure - currentSymbolAbs + projectedSymbolAbs)
        if projected > maxGrossExposure && projected >= grossExposure then
            Reject (sprintf "Gross exposure limit: projected %.2f exceeds %.2f ceiling" (float projected) (float maxGrossExposure))
        else
            Approve
    | _ -> Approve

/// Rejects an order that would concentrate a single symbol beyond the configured
/// percentage of portfolio value. Direction-aware: reducing orders lower the projected
/// concentration, and an order that strictly reduces the symbol's exposure is always
/// permitted so an oversized position can be unwound incrementally while still above the
/// cap. A measured nonpositive portfolio value fails closed for orders that do
/// not strictly reduce exposure: an exhausted book has no NAV left to allocate.
let symbolConcentration (ctx: RiskContext) : RiskDecision =
    match ctx.PortfolioValue, ctx.MaxSymbolConcentrationPercent with
    | Some portfolioValue, Some maxPercent when portfolioValue > 0m && maxPercent > 0m ->
        let currentSymbolAbs = ctx.SymbolExposure |> Option.defaultValue 0m
        let projected = projectedSymbolAbsoluteExposure ctx
        let projectedPercent = (projected / portfolioValue) * 100m
        if projectedPercent > maxPercent && projected >= currentSymbolAbs then
            Reject (sprintf "Concentration limit: %s at %.2f%% of portfolio value exceeds %.2f%% cap" ctx.Request.Symbol (float projectedPercent) (float maxPercent))
        else
            Approve
    | Some portfolioValue, Some maxPercent when portfolioValue <= 0m && maxPercent > 0m ->
        let currentSymbolAbs = ctx.SymbolExposure |> Option.defaultValue 0m
        let projected = projectedSymbolAbsoluteExposure ctx
        if projected >= currentSymbolAbs then
            Reject (sprintf "Concentration limit: %s cannot increase exposure while portfolio value is exhausted" ctx.Request.Symbol)
        else
            Approve
    | _ -> Approve

/// Signed distance of a price from its reference, as a percentage, saturating instead of
/// overflowing. Two individually valid decimals can produce a result decimal cannot hold —
/// decimal.MaxValue against a reference of 1 overflows the scaling, and against 0.1 the
/// *division itself* overflows before any scaling happens. Throwing there would surface as a
/// generic evaluation failure instead of the structured breach it plainly is, so the magnitude
/// is capped: anything that would overflow is astronomically past any band, and the cap yields
/// the same verdict.
///
/// The comparison is therefore made against `cap * reference` and never forms the quotient.
/// That product is representable exactly when the reference is at most 100, because
/// `cap * 100 = decimal.MaxValue`; above that the ratio cannot reach the cap at all, since the
/// numerator is itself bounded by MaxValue.
let private signedDeviationPercent (price: decimal) (reference: decimal) : decimal =
    let cap = System.Decimal.MaxValue / 100m
    let difference = price - reference
    if reference > 100m then
        (difference / reference) * 100m
    else
        let limit = cap * reference
        if difference > limit then System.Decimal.MaxValue
        elif difference < -limit then System.Decimal.MinValue
        else (difference / reference) * 100m

/// Blocks the two classic order-entry mistakes: a quantity far larger than the desk ever
/// intends to send in one order, and a price typed far through the market.
///
/// The price limb is deliberately *directional*. A resting buy far below the market and a
/// resting sell far above it are ordinary working orders, so only the aggressive side is
/// measured — paying above the reference on a buy, or selling below it on a sell. A
/// symmetric band would reject the entire resting book, which is why the deviation is
/// signed against the order's side rather than taken as an absolute.
///
/// Only a price the order intends to *trade* at is measured here. A stop price is a trigger,
/// not a trade price, and it sits away from the market by design — measuring it with this
/// orientation would reject every stop-loss. Wrong-side triggers are measured by
/// `fatFingerStopTrigger`, which mirrors the orientation; the caller decides which price to
/// supply to which, and this rule never sees a stop price.
///
/// Quantity is checked before price so the rejection names the mistake an operator is most
/// likely to have made, and both limbs approve when their threshold is unconfigured.
let fatFinger (ctx: RiskContext) : RiskDecision =
    let quantityBreach =
        match ctx.MaxOrderQuantity with
        | Some maxQuantity when maxQuantity > 0m && abs ctx.Request.Quantity > maxQuantity ->
            Some (
                sprintf
                    "Fat-finger quantity: %.2f on %s exceeds the %.2f per-order ceiling"
                    (float (abs ctx.Request.Quantity))
                    ctx.Request.Symbol
                    (float maxQuantity))
        | _ -> None

    match quantityBreach with
    | Some reason -> Reject reason
    | None ->
        match ctx.OrderPrice, ctx.ReferencePrice, ctx.MaxPriceDeviationPercent with
        | Some orderPrice, Some reference, Some maxDeviation when
            orderPrice > 0m && reference > 0m && maxDeviation > 0m ->
            let signedDeviation = signedDeviationPercent orderPrice reference
            // Positive means the order is priced aggressively against the operator: a buy
            // paying above the reference, or a sell hitting below it. A negative value is a
            // passive resting order and never breaches.
            // Non-Buy is oriented as a sell rather than matched against Sell alone: an undefined
            // enum value routes as a sell, and a neutral arm would return zero deviation and
            // approve any price at all — the band would simply not apply to it.
            let aggressiveDeviation =
                match ctx.Request.Side with
                | OrderSide.Buy -> signedDeviation
                | _ -> -signedDeviation

            if aggressiveDeviation > maxDeviation then
                Reject (
                    sprintf
                        "Fat-finger price: %s at %.4f is %.2f%% through the %.4f reference, beyond the %.2f%% band"
                        ctx.Request.Symbol
                        (float orderPrice)
                        (float aggressiveDeviation)
                        (float reference)
                        (float maxDeviation))
            else
                Approve
        | _ -> Approve

/// Blocks a stop whose trigger is on the wrong side of the market, which is not a stop at all:
/// it fires the moment it is accepted.
///
/// The orientation here is the exact *mirror* of `fatFinger`, and that is the whole point. A
/// limit is aggressive when a buy pays above the market or a sell hits below it. A trigger is
/// the opposite: `PaperOrderMatchingPolicy.IsStopTriggered` fires a buy once the market reaches
/// or passes *above* the stop and a sell once it reaches or falls *below* it, so a correctly
/// placed buy stop sits above the market and a correctly placed sell stop sits below it. A buy
/// stop typed beneath the market — $1 against a $100 book — is already crossed, and a stop-market
/// order that triggers immediately routes as an unbounded market order.
///
/// Distance is measured on the wrong side only, so an ordinary protective stop placed correctly
/// away from the market never breaches however far away it sits. The same configured band is
/// reused rather than inventing a second threshold: an operator who says "no price more than 10%
/// through the market" is stating one tolerance, and a trigger 10% the wrong way is the same
/// class of slip as a limit 10% through.
///
/// `OrderPrice` carries the stop price and `ReferencePrice` the current touch. The quantity limb
/// is not repeated — `fatFinger` owns it — so this evaluates the trigger alone.
let fatFingerStopTrigger (ctx: RiskContext) : RiskDecision =
    match ctx.OrderPrice, ctx.ReferencePrice, ctx.MaxPriceDeviationPercent with
    | Some stopPrice, Some reference, Some maxDeviation when
        stopPrice > 0m && reference > 0m && maxDeviation > 0m ->
        let signedDeviation = signedDeviationPercent stopPrice reference
        // Positive means the trigger is already crossed or heading that way: a buy stop below
        // the market, or a sell stop above it. A negative value is a correctly placed stop
        // waiting for the market to come to it, and never breaches.
        // Same orientation rule as the limbs above: non-Buy is a sell, so an undefined enum
        // value cannot present an already-crossed stop as a correctly placed one.
        let crossedDeviation =
            match ctx.Request.Side with
            | OrderSide.Buy -> -signedDeviation
            | _ -> signedDeviation

        if crossedDeviation > maxDeviation then
            Reject (
                sprintf
                    "Fat-finger stop trigger: %s stop at %.4f is %.2f%% on the wrong side of the %.4f market, beyond the %.2f%% band; it would fire immediately"
                    ctx.Request.Symbol
                    (float stopPrice)
                    (float crossedDeviation)
                    (float reference)
                    (float maxDeviation))
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
