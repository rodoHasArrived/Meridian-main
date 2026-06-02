namespace Meridian.FSharp.Operations

open System

[<CLIMutable>]
type ApprovalWorkflowInput = {
    LifecycleState: string
    Action: string
    Actor: string
    EvidenceCount: int
    ApprovedBy: string
    Reason: string
}

[<CLIMutable>]
type ApprovalWorkflowDecision = {
    IsValid: bool
    ErrorCode: string
    Error: string
    NextLifecycleState: string
    RequiresDualApproval: bool
}

[<RequireQualifiedAccess>]
module ApprovalWorkflowRules =

    let private fail reason code nextState =
        {
            IsValid = false
            ErrorCode = code
            Error = reason
            NextLifecycleState = nextState
            RequiresDualApproval = false
        }

    let private success nextState dualApproval =
        {
            IsValid = true
            ErrorCode = "None"
            Error = String.Empty
            NextLifecycleState = nextState
            RequiresDualApproval = dualApproval
        }

    let private illegalTransition inputState action =
        action, inputState

    let evaluate (input: ApprovalWorkflowInput) =
        if String.IsNullOrWhiteSpace input.Actor then
            fail "Actor is required." "MissingActor" input.LifecycleState
        elif String.IsNullOrWhiteSpace input.Reason then
            fail "Reason is required." "MissingReason" input.LifecycleState
        elif String.IsNullOrWhiteSpace input.Action then
            fail "Action is required." "MissingAction" input.LifecycleState
        else
            match input.LifecycleState.Trim(), input.Action.Trim() with
            | state, action when state = "Open" && action = "Submit" ->
                success "Submitted" false
            | state, action when state = "Open" && action = "Cancel" ->
                success "Cancelled" false
            | state, action when state = "Submitted" && action = "Review" ->
                if input.EvidenceCount < 1 then
                    fail "Review action requires evidence." "MissingEvidence" state
                else
                    success "UnderReview" false
            | state, action when state = "UnderReview" && action = "Reject" ->
                if input.EvidenceCount < 1 then
                    fail "Reject action requires evidence." "MissingEvidence" state
                else
                    success "Rejected" false
            | state, action when state = "UnderReview" && action = "Approve" ->
                if input.EvidenceCount < 2 then
                    fail "Approve action requires at least two evidence items." "MissingEvidence" state
                elif String.Equals(input.Actor, input.ApprovedBy, StringComparison.OrdinalIgnoreCase) then
                    fail "Approver must differ from submitter." "DualReviewRequired" state
                else
                    success "Approved" true
            | state, action when state = "Approved" && action = "Execute" ->
                if input.EvidenceCount < 2 then
                    fail "Execute action requires completed evidence." "MissingEvidence" state
                else
                    success "Executed" false
            | state, action when state = "Rejected" && action = "Reopen" ->
                success "Open" false
            | state, action when (state = "Executed" && action = "Archive") ->
                success "Archived" false
            | _ ->
                fail "Illegal transition." "IllegalTransition" input.LifecycleState

    let isIllegalTransition (state: string) (action: string) =
        let result =
            evaluate {
                LifecycleState = state
                Action = action
                Actor = "actor"
                EvidenceCount = 2
                ApprovedBy = "other"
                Reason = "test"
            }
        not result.IsValid && result.ErrorCode = "IllegalTransition"

[<Sealed>]
type ApprovalWorkflowInterop private () =

    static member Evaluate(input: ApprovalWorkflowInput) =
        ApprovalWorkflowRules.evaluate input

