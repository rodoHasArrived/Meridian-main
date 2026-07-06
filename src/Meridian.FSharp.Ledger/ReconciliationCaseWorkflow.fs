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

    // ── Token vocabulary ─────────────────────────────────────────────────────
    // These literals are the wire vocabulary shared with the C# reconciliation enums. Each token
    // MUST equal a member name of the corresponding C# enum in Meridian.Contracts.Workstation:
    //   * action tokens        -> ReconciliationCaseTransitionAction
    //   * lifecycle-state tokens-> ReconciliationCaseLifecycleState
    //   * queue-status tokens   -> ReconciliationBreakQueueStatus
    // The C# boundary maps enums to these tokens via nameof(...) and the
    // ReconciliationCaseWorkflowVocabularyTests guard asserts parity, so a rename on either side is
    // caught by compilation or a failing test instead of silently misrouting a transition at runtime.

    [<Literal>] let ActionStartReview = "StartReview"
    [<Literal>] let ActionRequestApproval = "RequestApproval"
    [<Literal>] let ActionApprove = "Approve"
    [<Literal>] let ActionPost = "Post"
    [<Literal>] let ActionReopen = "Reopen"
    [<Literal>] let ActionSupersede = "Supersede"

    [<Literal>] let StateOpen = "Open"
    [<Literal>] let StateReopened = "Reopened"
    [<Literal>] let StateInvestigating = "Investigating"
    [<Literal>] let StateAwaitingEvidence = "AwaitingEvidence"
    [<Literal>] let StateResolved = "Resolved"
    [<Literal>] let StateSignedOff = "SignedOff"
    [<Literal>] let StateSuperseded = "Superseded"

    [<Literal>] let QueueOpen = "Open"
    [<Literal>] let QueueInReview = "InReview"
    [<Literal>] let QueueResolved = "Resolved"
    [<Literal>] let QueueSignedOff = "SignedOff"
    [<Literal>] let QueueDismissed = "Dismissed"

    // Error-code tokens (must match ReconciliationBreakQueueTransitionErrorCode member names).
    [<Literal>] let ErrorNone = "None"
    [<Literal>] let ErrorMissingActor = "MissingActor"
    [<Literal>] let ErrorMissingReason = "MissingReason"
    [<Literal>] let ErrorMissingEvidence = "MissingEvidence"
    [<Literal>] let ErrorIllegalTransition = "IllegalTransition"
    [<Literal>] let ErrorDualReviewRequired = "DualReviewRequired"
    [<Literal>] let ErrorReopenNotAllowed = "ReopenNotAllowed"

    /// Every action token the workflow understands.
    let recognizedActionTokens =
        [| ActionStartReview; ActionRequestApproval; ActionApprove; ActionPost; ActionReopen; ActionSupersede |]

    /// Every lifecycle-state token the workflow reads or produces.
    let recognizedLifecycleStateTokens =
        [| StateOpen; StateReopened; StateInvestigating; StateAwaitingEvidence; StateResolved; StateSignedOff; StateSuperseded |]

    /// Every queue-status token the workflow reads or produces.
    let recognizedQueueStatusTokens =
        [| QueueOpen; QueueInReview; QueueResolved; QueueSignedOff; QueueDismissed |]

    /// Every error-code token the workflow can emit.
    let recognizedErrorCodeTokens =
        [| ErrorNone; ErrorMissingActor; ErrorMissingReason; ErrorMissingEvidence; ErrorIllegalTransition; ErrorDualReviewRequired; ErrorReopenNotAllowed |]

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
            ErrorCode = ErrorNone
            NextLifecycleState = next
            NextQueueStatus = status
        }

    let apply (input: ReconciliationCaseTransitionInput) =
        if String.IsNullOrWhiteSpace input.Actor then
            fail "Actor is required." ErrorMissingActor input
        elif String.IsNullOrWhiteSpace input.Reason then
            fail "Reason is required." ErrorMissingReason input
        elif input.EvidenceReferenceCount = 0 then
            fail "Evidence references are required." ErrorMissingEvidence input
        else
            let transition =
                match input.Action, input.LifecycleState with
                | ActionStartReview, StateOpen
                | ActionStartReview, StateReopened -> Some(StateInvestigating, QueueInReview)
                | ActionRequestApproval, StateInvestigating
                | ActionRequestApproval, StateAwaitingEvidence -> Some(StateResolved, QueueResolved)
                | ActionApprove, StateResolved -> Some(StateSignedOff, QueueSignedOff)
                | ActionPost, StateSignedOff -> Some(StateSignedOff, QueueSignedOff)
                | ActionReopen, StateSignedOff -> Some(StateReopened, QueueOpen)
                | ActionSupersede, state when state <> StateSuperseded -> Some(StateSuperseded, QueueDismissed)
                | _ -> None

            // Reopen is only legal from SignedOff, which is the sole (ActionReopen, _) arm above,
            // so any (ActionReopen, state) that produced None is specifically an illegal reopen and
            // gets the more precise ReopenNotAllowed code instead of the generic IllegalTransition.
            match transition with
            | None when input.Action = ActionReopen ->
                fail "Only signed-off cases can be reopened." ErrorReopenNotAllowed input
            | None -> fail "Illegal transition." ErrorIllegalTransition input
            | Some _ when input.Action = ActionApprove
                          && String.Equals(input.ReviewedBy, input.Actor, StringComparison.OrdinalIgnoreCase) ->
                fail "Signer must differ from resolver." ErrorDualReviewRequired input
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

    /// Action tokens the workflow understands; must match ReconciliationCaseTransitionAction names.
    static member RecognizedActionTokens = ReconciliationCaseWorkflow.recognizedActionTokens

    /// Lifecycle-state tokens the workflow reads/produces; must match ReconciliationCaseLifecycleState names.
    static member RecognizedLifecycleStateTokens = ReconciliationCaseWorkflow.recognizedLifecycleStateTokens

    /// Queue-status tokens the workflow reads/produces; must match ReconciliationBreakQueueStatus names.
    static member RecognizedQueueStatusTokens = ReconciliationCaseWorkflow.recognizedQueueStatusTokens

    /// Error-code tokens the workflow can emit; must match ReconciliationBreakQueueTransitionErrorCode names.
    static member RecognizedErrorCodeTokens = ReconciliationCaseWorkflow.recognizedErrorCodeTokens

    static member EvaluateProviderLedgerAmountCheck(label: string, expectedAmount: decimal, actualAmount: decimal, tolerance: decimal) =
        ProviderLedgerReconciliationRules.evaluateAmountCheck label expectedAmount actualAmount tolerance
