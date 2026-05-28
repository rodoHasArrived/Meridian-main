module Meridian.FSharp.Tests.TradingReadinessRulesTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Operations

let private gate id status =
    { GateId = id; Status = status }

let private item tone evidence =
    { Tone = tone; EvidenceId = evidence }

[<Fact>]
let ``Trading readiness overall posture follows canonical gate precedence`` () =
    let status =
        TradingReadinessInterop.EvaluateOverallPosture(
            [|
                gate "session" "Ready"
                gate "replay" "Blocked"
                gate "promotion" "Ready"
            |])

    status |> should equal "Blocked"

[<Fact>]
let ``Trading readiness evidence summary scores ready gates and missing evidence`` () =
    let summary =
        TradingReadinessInterop.SummarizeEvidence(
            [| gate "session" "Ready"; gate "replay" "ReviewRequired"; gate "promotion" "Blocked" |],
            [| item "Warning" "ev-1"; item "Critical" "ev-2"; item "Critical" "ev-2" |])

    summary.Status |> should equal "Blocked"
    summary.ReadyGateCount |> should equal 1
    summary.TotalGateCount |> should equal 3
    summary.ScorePercent |> should equal 33
    summary.MissingEvidenceIds |> should equal [| "ev-1"; "ev-2" |]
