module Meridian.FSharp.Tests.ReconciliationCaseWorkflowTests

open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Ledger

let private transition state action =
    {
        LifecycleState = state
        QueueStatus = "Open"
        Action = action
        Actor = "reviewer"
        Reason = "reason"
        EvidenceReferenceCount = 1
        ReviewedBy = ""
    }

[<Fact>]
let ``Reconciliation case workflow allows open break review`` () =
    let decision = ReconciliationCaseWorkflowInterop.Apply(transition "Open" "StartReview")

    decision.IsValid |> should equal true
    decision.NextLifecycleState |> should equal "Investigating"
    decision.NextQueueStatus |> should equal "InReview"

[<Fact>]
let ``Reconciliation case workflow rejects missing actor before transition checks`` () =
    let decision =
        ReconciliationCaseWorkflowInterop.Apply(
            { transition "AwaitingApproval" "Approve" with
                Actor = "" })

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "MissingActor"

[<Fact>]
let ``Reconciliation case workflow requires distinct approver`` () =
    let decision =
        ReconciliationCaseWorkflowInterop.Apply(
            { transition "Resolved" "Approve" with
                Actor = "same"
                ReviewedBy = "same" })

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "DualReviewRequired"

[<Fact>]
let ``Provider ledger amount check applies absolute tolerance`` () =
    let matched = ReconciliationCaseWorkflowInterop.EvaluateProviderLedgerAmountCheck("Cash", 100m, 100.5m, -1m)
    let broken = ReconciliationCaseWorkflowInterop.EvaluateProviderLedgerAmountCheck("Cash", 100m, 102m, 1m)

    matched.IsMatched |> should equal true
    matched.Variance |> should equal 0.5m
    broken.IsMatched |> should equal false
    broken.Reason.Contains("exceeds tolerance") |> should equal true
