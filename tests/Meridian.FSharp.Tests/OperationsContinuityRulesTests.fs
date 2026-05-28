module Meridian.FSharp.Tests.OperationsContinuityRulesTests

open Xunit
open FsUnit.Xunit
open Meridian.Contracts.Workstation
open Meridian.FSharp.Operations

[<Fact>]
let ``Operations continuity status gives blocked gates highest precedence`` () =
    let status =
        OperationsContinuityInterop.DeriveStatus(
            false,
            OperationsBrokerIntakeStateDto.Normalized,
            OperationsSecurityMasterStateDto.ResolvedAllInstruments,
            OperationsLedgerPostingStateDto.Drafted,
            OperationsReconciliationStateDto.Pending,
            OperationsApprovalStateDto.Pending,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Blocked,
            OperationsGateStatusDto.InProgress,
            OperationsGateStatusDto.NotStarted,
            OperationsGateStatusDto.NotStarted)

    status |> should equal OperationsWorkflowStatusDto.Blocked

[<Fact>]
let ``Operations continuity status returns ready for close only after all gates pass and approval is approved`` () =
    let status =
        OperationsContinuityInterop.DeriveStatus(
            false,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Posted,
            OperationsReconciliationStateDto.Complete,
            OperationsApprovalStateDto.Approved,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed)

    status |> should equal OperationsWorkflowStatusDto.ReadyForClose

[<Fact>]
let ``Operations continuity status chooses highest active stage deterministically`` () =
    let status =
        OperationsContinuityInterop.DeriveStatus(
            false,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Validated,
            OperationsReconciliationStateDto.InReview,
            OperationsApprovalStateDto.Pending,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.InProgress,
            OperationsGateStatusDto.InProgress,
            OperationsGateStatusDto.NotStarted)

    status |> should equal OperationsWorkflowStatusDto.ReconciliationActive
