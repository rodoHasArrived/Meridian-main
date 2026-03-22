module Meridian.FSharp.Risk.RiskEvaluation

open Meridian.FSharp.Risk.RiskTypes

let aggregate (decisions: RiskDecision seq) : RiskDecision =
    decisions
    |> Seq.tryFind (function | Approve -> false | _ -> true)
    |> Option.defaultValue Approve
