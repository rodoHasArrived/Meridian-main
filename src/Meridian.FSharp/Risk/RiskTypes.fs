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
}
