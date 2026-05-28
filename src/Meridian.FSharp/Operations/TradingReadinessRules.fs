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

    let private canonicalGateOrder =
        [|
            "replay"
            "reconciliation"
            "audit-controls"
            "risk-rules"
            "promotion"
            "dk1-trust"
            "report-pack"
            "session"
            "brokerage-sync"
        |]

    let private sameText left right =
        String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    let private distinctOrdinalIgnoreCase (items: string seq) =
        let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        items
        |> Seq.filter (fun item -> not (String.IsNullOrWhiteSpace item) && seen.Add item)
        |> Seq.toArray

    let evaluateOverallPosture (gates: TradingAcceptanceGateFactDto array) =
        let ordered =
            canonicalGateOrder
            |> Array.choose (fun gateId ->
                gates
                |> Array.tryFind (fun gate -> sameText gate.GateId gateId))

        if ordered.Length = 0 then
            "Unknown"
        elif ordered |> Array.exists (fun gate -> sameText gate.Status "Blocked") then
            "Blocked"
        elif ordered |> Array.forall (fun gate -> sameText gate.Status "Ready") then
            "Ready"
        else
            "ReviewRequired"

    let summarizeEvidence (gates: TradingAcceptanceGateFactDto array) (workItems: TradingWorkItemEvidenceFactDto array) =
        let readyGateIds =
            gates
            |> Array.filter (fun gate -> sameText gate.Status "Ready")
            |> Array.map _.GateId

        let blockingGateIds =
            gates
            |> Array.filter (fun gate -> sameText gate.Status "Blocked")
            |> Array.map _.GateId

        let reviewGateIds =
            gates
            |> Array.filter (fun gate -> sameText gate.Status "ReviewRequired")
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
