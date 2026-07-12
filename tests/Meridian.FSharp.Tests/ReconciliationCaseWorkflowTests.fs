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
let ``Reconciliation case workflow rejects missing reason before transition checks`` () =
    let decision =
        ReconciliationCaseWorkflowInterop.Apply(
            { transition "Open" "StartReview" with
                Reason = "" })

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "MissingReason"

[<Fact>]
let ``Reconciliation case workflow rejects missing evidence before transition checks`` () =
    let decision =
        ReconciliationCaseWorkflowInterop.Apply(
            { transition "Open" "StartReview" with
                EvidenceReferenceCount = 0 })

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "MissingEvidence"

[<Theory>]
[<InlineData("Open", "StartReview", "Investigating", "InReview")>]
[<InlineData("Reopened", "StartReview", "Investigating", "InReview")>]
[<InlineData("Investigating", "RequestApproval", "Resolved", "Resolved")>]
[<InlineData("AwaitingEvidence", "RequestApproval", "Resolved", "Resolved")>]
[<InlineData("Resolved", "Approve", "SignedOff", "SignedOff")>]
[<InlineData("SignedOff", "Post", "SignedOff", "SignedOff")>]
[<InlineData("SignedOff", "Reopen", "Reopened", "Open")>]
[<InlineData("Open", "Supersede", "Superseded", "Dismissed")>]
[<InlineData("Investigating", "Supersede", "Superseded", "Dismissed")>]
[<InlineData("SignedOff", "Supersede", "Superseded", "Dismissed")>]
let ``Reconciliation case workflow accepts every legal transition`` (state: string) (action: string) (expectedNext: string) (expectedStatus: string) =
    let decision = ReconciliationCaseWorkflowInterop.Apply(transition state action)

    decision.IsValid |> should equal true
    decision.ErrorCode |> should equal "None"
    decision.NextLifecycleState |> should equal expectedNext
    decision.NextQueueStatus |> should equal expectedStatus

[<Theory>]
[<InlineData("Open", "Approve")>]
[<InlineData("Open", "RequestApproval")>]
[<InlineData("Open", "Post")>]
[<InlineData("Investigating", "Approve")>]
[<InlineData("Investigating", "Post")>]
[<InlineData("Resolved", "Post")>]
[<InlineData("Resolved", "StartReview")>]
[<InlineData("Superseded", "StartReview")>]
[<InlineData("Superseded", "Supersede")>]
let ``Reconciliation case workflow rejects illegal transitions`` (state: string) (action: string) =
    let decision = ReconciliationCaseWorkflowInterop.Apply(transition state action)

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "IllegalTransition"
    decision.NextLifecycleState |> should equal state

[<Theory>]
[<InlineData("Open")>]
[<InlineData("Reopened")>]
[<InlineData("Investigating")>]
[<InlineData("AwaitingEvidence")>]
[<InlineData("Resolved")>]
[<InlineData("Superseded")>]
let ``Reconciliation case workflow blocks reopen from any state except signed off`` (state: string) =
    let decision = ReconciliationCaseWorkflowInterop.Apply(transition state "Reopen")

    decision.IsValid |> should equal false
    decision.ErrorCode |> should equal "ReopenNotAllowed"
    decision.NextLifecycleState |> should equal state

[<Fact>]
let ``Provider ledger amount check applies absolute tolerance`` () =
    let matched = ReconciliationCaseWorkflowInterop.EvaluateProviderLedgerAmountCheck("Cash", 100m, 100.5m, -1m)
    let broken = ReconciliationCaseWorkflowInterop.EvaluateProviderLedgerAmountCheck("Cash", 100m, 102m, 1m)

    matched.IsMatched |> should equal true
    matched.Variance |> should equal 0.5m
    broken.IsMatched |> should equal false
    broken.Reason.Contains("exceeds tolerance") |> should equal true
