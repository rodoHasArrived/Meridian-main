module Meridian.FSharp.Risk.RiskTypes

open Meridian.Execution.Sdk

type RiskDecision =
    | Approve
    | Reject of reason: string
    | Escalate of reason: string

[<CLIMutable>]
type RiskContext = {
    Request: OrderRequest
    CurrentPositionQuantity: decimal
    MaxPositionSize: decimal option
    PortfolioValue: decimal option
    InitialCapital: decimal option
    MaxDrawdownPercent: decimal option
    RecentOrderRate: decimal option
    PortfolioExposure: decimal option
    /// Current gross market exposure for the order's symbol across the aggregated portfolio.
    SymbolExposure: decimal option
    /// Resolved notional of the order under evaluation (quantity x reference price).
    OrderNotional: decimal option
    /// Portfolio-wide gross exposure ceiling.
    MaxGrossExposure: decimal option
    /// Single-symbol concentration ceiling as a percentage of portfolio value.
    MaxSymbolConcentrationPercent: decimal option
    /// Hard per-order notional ceiling.
    MaxOrderNotional: decimal option
    /// Per-order notional band above which the order escalates for governed approval.
    EscalateOrderNotional: decimal option
}
