namespace Meridian.FSharp.Operations

open Meridian.Contracts.Workstation

[<CLIMutable>]
type OperationsContinuityStatusFacts =
    {
        IsClosed: bool
        BrokerIntakeState: OperationsBrokerIntakeStateDto
        SecurityMasterState: OperationsSecurityMasterStateDto
        LedgerPostingState: OperationsLedgerPostingStateDto
        ReconciliationState: OperationsReconciliationStateDto
        ApprovalState: OperationsApprovalStateDto
        BrokerIngestGateStatus: OperationsGateStatusDto
        SecurityMasterGateStatus: OperationsGateStatusDto
        LedgerPostingGateStatus: OperationsGateStatusDto
        ReconciliationGateStatus: OperationsGateStatusDto
        ApprovalGateStatus: OperationsGateStatusDto
    }

module OperationsContinuityRules =

    let deriveStatus (facts: OperationsContinuityStatusFacts) =
        let gates =
            [|
                facts.BrokerIngestGateStatus
                facts.SecurityMasterGateStatus
                facts.LedgerPostingGateStatus
                facts.ReconciliationGateStatus
                facts.ApprovalGateStatus
            |]

        if gates |> Array.exists ((=) OperationsGateStatusDto.Blocked) then
            OperationsWorkflowStatusDto.Blocked
        elif facts.IsClosed then
            OperationsWorkflowStatusDto.Closed
        elif gates |> Array.forall ((=) OperationsGateStatusDto.Passed)
             && facts.ApprovalState = OperationsApprovalStateDto.Approved then
            OperationsWorkflowStatusDto.ReadyForClose
        elif gates |> Array.exists ((=) OperationsGateStatusDto.ReviewRequired) then
            if facts.ReconciliationGateStatus = OperationsGateStatusDto.ReviewRequired then
                OperationsWorkflowStatusDto.ReconciliationActive
            else
                OperationsWorkflowStatusDto.ApprovalPending
        elif facts.BrokerIngestGateStatus = OperationsGateStatusDto.Passed
             && facts.SecurityMasterGateStatus = OperationsGateStatusDto.Passed
             && facts.LedgerPostingGateStatus = OperationsGateStatusDto.Passed
             && facts.ReconciliationGateStatus = OperationsGateStatusDto.Passed
             && facts.ApprovalGateStatus <> OperationsGateStatusDto.Passed then
            OperationsWorkflowStatusDto.ApprovalPending
        elif facts.ApprovalGateStatus = OperationsGateStatusDto.InProgress
             || facts.ApprovalState = OperationsApprovalStateDto.Submitted
             || facts.ApprovalState = OperationsApprovalStateDto.ReviewerAssigned then
            OperationsWorkflowStatusDto.ApprovalPending
        elif facts.ReconciliationGateStatus = OperationsGateStatusDto.InProgress
             || facts.ReconciliationState = OperationsReconciliationStateDto.AutoMatched
             || facts.ReconciliationState = OperationsReconciliationStateDto.ExceptionsOpen
             || facts.ReconciliationState = OperationsReconciliationStateDto.InReview then
            OperationsWorkflowStatusDto.ReconciliationActive
        elif facts.LedgerPostingGateStatus = OperationsGateStatusDto.InProgress
             || facts.LedgerPostingState = OperationsLedgerPostingStateDto.Drafted
             || facts.LedgerPostingState = OperationsLedgerPostingStateDto.Validated then
            OperationsWorkflowStatusDto.LedgerPostingDraft
        elif facts.SecurityMasterGateStatus = OperationsGateStatusDto.InProgress
             || facts.SecurityMasterState = OperationsSecurityMasterStateDto.ResolvedAllInstruments
             || facts.SecurityMasterState = OperationsSecurityMasterStateDto.OverridesRequested
             || facts.SecurityMasterState = OperationsSecurityMasterStateDto.OverridesApproved then
            OperationsWorkflowStatusDto.SecurityMasterValidation
        elif facts.BrokerIngestGateStatus = OperationsGateStatusDto.InProgress
             || facts.BrokerIntakeState = OperationsBrokerIntakeStateDto.Imported
             || facts.BrokerIntakeState = OperationsBrokerIntakeStateDto.Normalized
             || facts.BrokerIntakeState = OperationsBrokerIntakeStateDto.MatchedToInternalRun then
            OperationsWorkflowStatusDto.CollectingBrokerData
        else
            OperationsWorkflowStatusDto.NotStarted

[<Sealed>]
type OperationsContinuityInterop private () =

    static member DeriveStatus(
        isClosed: bool,
        brokerIntakeState: OperationsBrokerIntakeStateDto,
        securityMasterState: OperationsSecurityMasterStateDto,
        ledgerPostingState: OperationsLedgerPostingStateDto,
        reconciliationState: OperationsReconciliationStateDto,
        approvalState: OperationsApprovalStateDto,
        brokerIngestGateStatus: OperationsGateStatusDto,
        securityMasterGateStatus: OperationsGateStatusDto,
        ledgerPostingGateStatus: OperationsGateStatusDto,
        reconciliationGateStatus: OperationsGateStatusDto,
        approvalGateStatus: OperationsGateStatusDto) =
        OperationsContinuityRules.deriveStatus
            {
                IsClosed = isClosed
                BrokerIntakeState = brokerIntakeState
                SecurityMasterState = securityMasterState
                LedgerPostingState = ledgerPostingState
                ReconciliationState = reconciliationState
                ApprovalState = approvalState
                BrokerIngestGateStatus = brokerIngestGateStatus
                SecurityMasterGateStatus = securityMasterGateStatus
                LedgerPostingGateStatus = ledgerPostingGateStatus
                ReconciliationGateStatus = reconciliationGateStatus
                ApprovalGateStatus = approvalGateStatus
            }
