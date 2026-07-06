namespace Meridian.FSharp.Ledger

open System

[<CLIMutable>]
type ReconciliationCaseTransitionInput =
    {
        LifecycleState: string
        QueueStatus: string
        Action: string
        Actor: string
        Reason: string
        EvidenceReferenceCount: int
        ReviewedBy: string
    }

[<CLIMutable>]
type ReconciliationCaseTransitionDecisionDto =
    {
        IsValid: bool
        Error: string
        ErrorCode: string
        NextLifecycleState: string
        NextQueueStatus: string
    }

[<CLIMutable>]
type ProviderLedgerAmountCheckDto =
    {
        IsMatched: bool
        Variance: decimal
        Reason: string
    }

module ReconciliationCaseWorkflow =

    let private fail message code input =
        {
            IsValid = false
            Error = message
            ErrorCode = code
            NextLifecycleState = input.LifecycleState
            NextQueueStatus = input.QueueStatus
        }

    let private success next status =
        {
            IsValid = true
            Error = String.Empty
            ErrorCode = "None"
            NextLifecycleState = next
            NextQueueStatus = status
        }

    let apply (input: ReconciliationCaseTransitionInput) =
        if String.IsNullOrWhiteSpace input.Actor then
            fail "Actor is required." "MissingActor" input
        elif String.IsNullOrWhiteSpace input.Reason then
            fail "Reason is required." "MissingReason" input
        elif input.EvidenceReferenceCount = 0 then
            fail "Evidence references are required." "MissingEvidence" input
        else
            let transition =
                match input.Action, input.LifecycleState with
                | "StartReview", "Open"
                | "StartReview", "Reopened" -> Some("Investigating", "InReview")
                | "RequestApproval", "Investigating"
                | "RequestApproval", "AwaitingEvidence" -> Some("Resolved", "Resolved")
                | "Approve", "Resolved" -> Some("SignedOff", "SignedOff")
                | "Post", "SignedOff" -> Some("SignedOff", "SignedOff")
                | "Reopen", "SignedOff" -> Some("Reopened", "Open")
                | "Supersede", state when state <> "Superseded" -> Some("Superseded", "Dismissed")
                | _ -> None

            // Reopen is only legal from SignedOff, which is the sole ("Reopen", _) arm above,
            // so any ("Reopen", state) that produced None is specifically an illegal reopen and
            // gets the more precise ReopenNotAllowed code instead of the generic IllegalTransition.
            match transition with
            | None when input.Action = "Reopen" ->
                fail "Only signed-off cases can be reopened." "ReopenNotAllowed" input
            | None -> fail "Illegal transition." "IllegalTransition" input
            | Some _ when input.Action = "Approve"
                          && String.Equals(input.ReviewedBy, input.Actor, StringComparison.OrdinalIgnoreCase) ->
                fail "Signer must differ from resolver." "DualReviewRequired" input
            | Some(next, status) -> success next status

module ProviderLedgerReconciliationRules =

    let evaluateAmountCheck label expectedAmount actualAmount tolerance =
        let variance = actualAmount - expectedAmount
        let absoluteTolerance = abs tolerance

        if abs variance <= absoluteTolerance then
            {
                IsMatched = true
                Variance = variance
                Reason = "Provider and internal ledger values are within tolerance."
            }
        else
            {
                IsMatched = false
                Variance = variance
                Reason = sprintf "%s variance %s exceeds tolerance %s." label (variance.ToString("N2")) (absoluteTolerance.ToString("N2"))
            }

[<Sealed>]
type ReconciliationCaseWorkflowInterop private () =

    static member Apply(input: ReconciliationCaseTransitionInput) =
        ReconciliationCaseWorkflow.apply input

    static member EvaluateProviderLedgerAmountCheck(label: string, expectedAmount: decimal, actualAmount: decimal, tolerance: decimal) =
        ProviderLedgerReconciliationRules.evaluateAmountCheck label expectedAmount actualAmount tolerance
