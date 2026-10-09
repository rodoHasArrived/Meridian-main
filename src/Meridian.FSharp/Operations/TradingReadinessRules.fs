namespace Meridian.FSharp.Operations

open System
open System.Collections.Generic

[<CLIMutable>]
type TradingAcceptanceGateFactDto =
    {
        GateId: string
        Status: string
    }

[<CLIMutable>]
type TradingWorkItemEvidenceFactDto =
    {
        Tone: string
        EvidenceId: string
    }

[<CLIMutable>]
type TradingEvidenceCompletenessDto =
    {
        Status: string
        ReadyGateCount: int
        TotalGateCount: int
        CriticalWorkItemCount: int
        WarningWorkItemCount: int
        ScorePercent: int
        BlockingGateIds: string array
        ReviewGateIds: string array
        MissingEvidenceIds: string array
        ReadyGateIds: string array
    }

module TradingReadinessRules =

    let private sameText left right =
        String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    let private arrayOrEmpty (values: 'T array) =
        if isNull values then [||] else values

    // Missing or unsupported statuses require review and must never authorize readiness.
    let private gateStatus (gate: TradingAcceptanceGateFactDto) =
        if sameText gate.Status "Blocked" then "Blocked"
        elif sameText gate.Status "Ready" then "Ready"
        else "ReviewRequired"

    let private distinctOrdinalIgnoreCase (items: string seq) =
        let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        items
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item) && seen.Add item)
        |> Seq.toArray

    let evaluateOverallPosture (gates: TradingAcceptanceGateFactDto array) =
        let gates = arrayOrEmpty gates

        if gates.Length = 0 then
            "Unknown"
        elif gates |> Array.exists (fun gate -> gateStatus gate = "Blocked") then
            "Blocked"
        elif gates |> Array.forall (fun gate -> gateStatus gate = "Ready") then
            "Ready"
        else
            "ReviewRequired"

    let summarizeEvidence (gates: TradingAcceptanceGateFactDto array) (workItems: TradingWorkItemEvidenceFactDto array) =
        let gates = arrayOrEmpty gates
        let workItems = arrayOrEmpty workItems

        let readyGateIds =
            gates
            |> Array.filter (fun gate -> gateStatus gate = "Ready")
            |> Array.map _.GateId

        let blockingGateIds =
            gates
            |> Array.filter (fun gate -> gateStatus gate = "Blocked")
            |> Array.map _.GateId

        let reviewGateIds =
            gates
            |> Array.filter (fun gate -> gateStatus gate = "ReviewRequired")
            |> Array.map _.GateId

        let criticalCount =
            workItems
            |> Array.filter (fun item -> sameText item.Tone "Critical")
            |> Array.length

        let warningCount =
            workItems
            |> Array.filter (fun item -> sameText item.Tone "Warning")
            |> Array.length

        let totalGateCount = gates.Length
        let readyGateCount = readyGateIds.Length
        let scorePercent =
            if totalGateCount = 0 then
                0
            else
                decimal readyGateCount * 100m / decimal totalGateCount
                |> fun value -> Math.Round(value, MidpointRounding.AwayFromZero)
                |> int

        {
            Status = evaluateOverallPosture gates
            ReadyGateCount = readyGateCount
            TotalGateCount = totalGateCount
            CriticalWorkItemCount = criticalCount
            WarningWorkItemCount = warningCount
            ScorePercent = scorePercent
            BlockingGateIds = blockingGateIds
            ReviewGateIds = reviewGateIds
            MissingEvidenceIds =
                workItems
                |> Array.filter (fun item -> sameText item.Tone "Warning" || sameText item.Tone "Critical")
                |> Array.map _.EvidenceId
                |> distinctOrdinalIgnoreCase
            ReadyGateIds = readyGateIds
        }

[<Sealed>]
type TradingReadinessInterop private () =

    static member EvaluateOverallPosture(gates: TradingAcceptanceGateFactDto array) =
        TradingReadinessRules.evaluateOverallPosture gates

    static member SummarizeEvidence(gates: TradingAcceptanceGateFactDto array, workItems: TradingWorkItemEvidenceFactDto array) =
        TradingReadinessRules.summarizeEvidence gates workItems
