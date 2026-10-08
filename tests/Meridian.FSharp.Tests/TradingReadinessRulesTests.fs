module Meridian.FSharp.Tests.TradingReadinessRulesTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Operations

let private gate id status =
    { GateId = id; Status = status }

let private item tone evidence =
    { Tone = tone; EvidenceId = evidence }

[<Fact>]
let ``Trading readiness blocked gate overrides ready gates`` () =
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

[<Theory>]
[<InlineData("broker-execution-reconciliation", "Blocked")>]
[<InlineData("broker-execution-reconciliation", "ReviewRequired")>]
[<InlineData("brokerage-portfolio-recovery", "Blocked")>]
[<InlineData("brokerage-portfolio-recovery", "ReviewRequired")>]
[<InlineData("future-gate", "Blocked")>]
let ``Every supplied gate contributes to posture and evidence`` (gateId: string) (status: string) =
    let gates = [| gate "session" "Ready"; gate gateId status |]
    let summary = TradingReadinessInterop.SummarizeEvidence(gates, [||])

    TradingReadinessInterop.EvaluateOverallPosture(gates) |> should equal status
    summary.Status |> should equal status
    summary.TotalGateCount |> should equal 2
    summary.ReadyGateCount |> should equal 1
    summary.ScorePercent |> should equal 50
    summary.ReadyGateIds |> should equal [| "session" |]
    if status = "Blocked" then
        summary.BlockingGateIds |> should equal [| gateId |]
        summary.ReviewGateIds |> should equal [||]
    else
        summary.BlockingGateIds |> should equal [||]
        summary.ReviewGateIds |> should equal [| gateId |]

[<Fact>]
let ``Ready gate with a new ID can satisfy readiness`` () =
    TradingReadinessInterop.EvaluateOverallPosture([| gate "future-gate" "Ready" |])
    |> should equal "Ready"

[<Theory>]
[<InlineData("Blocked")>]
[<InlineData("ReviewRequired")>]
let ``Conflicting duplicate gates cannot mask nonready evidence`` (status: string) =
    let gates = [| gate "session" "Ready"; gate "SESSION" status |]
    for supplied in [| gates; Array.rev gates |] do
        let summary = TradingReadinessInterop.SummarizeEvidence(supplied, [||])
        TradingReadinessInterop.EvaluateOverallPosture(supplied) |> should equal status
        summary.Status |> should equal status
        summary.ReadyGateCount |> should equal 1
        summary.TotalGateCount |> should equal 2
        summary.ScorePercent |> should equal 50

[<Theory>]
[<InlineData(null)>]
[<InlineData("")>]
[<InlineData(" ")>]
[<InlineData("Unknown")>]
[<InlineData("unsupported")>]
let ``Missing and unsupported statuses require review`` (status: string) =
    let gates = [| gate "session" "Ready"; gate "future-gate" status |]
    let summary = TradingReadinessInterop.SummarizeEvidence(gates, [||])
    TradingReadinessInterop.EvaluateOverallPosture(gates) |> should equal "ReviewRequired"
    summary.Status |> should equal "ReviewRequired"
    summary.ReviewGateIds |> should equal [| "future-gate" |]
    summary.ReadyGateCount |> should equal 1
    summary.ScorePercent |> should equal 50

[<Fact>]
let ``Blocked dominates unsupported status and gate comparison ignores casing`` () =
    let gates = [| gate "future-gate" "Unknown"; gate "session" "bLoCkEd"; gate "replay" "rEaDy" |]
    let summary = TradingReadinessInterop.SummarizeEvidence(gates, [||])
    summary.Status |> should equal "Blocked"
    summary.BlockingGateIds |> should equal [| "session" |]
    summary.ReviewGateIds |> should equal [| "future-gate" |]
    summary.ReadyGateIds |> should equal [| "replay" |]

[<Fact>]
let ``No supplied gate facts remain unknown with zero completeness`` () =
    for gates in [| [||]; null |] do
        let summary = TradingReadinessInterop.SummarizeEvidence(gates, null)
        TradingReadinessInterop.EvaluateOverallPosture(gates) |> should equal "Unknown"
        summary.Status |> should equal "Unknown"
        summary.TotalGateCount |> should equal 0
        summary.ScorePercent |> should equal 0
        summary.MissingEvidenceIds |> should equal [||]
