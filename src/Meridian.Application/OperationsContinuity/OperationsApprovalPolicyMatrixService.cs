using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.OperationsContinuity;

public interface IOperationsApprovalPolicyMatrixService
{
    OperationsApprovalPolicyMatrixDto GetMatrix();
}

public sealed class OperationsApprovalPolicyMatrixService : IOperationsApprovalPolicyMatrixService
{
    private const string PolicyId = "operations-continuity-approval-policy";
    private const string Version = "2026.05";
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

    public OperationsApprovalPolicyMatrixDto GetMatrix() =>
        new(
            PolicyId,
            Version,
            GeneratedAtUtc,
            [
                Row(
                    "operations-continuity.submit-approval",
                    "Operations close",
                    "Submit workflow for approval",
                    OperationsGateKeyDto.Approval,
                    "Broker, Security Master, ledger, and reconciliation gates have no critical blockers.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: false,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack plus close checklist control approvals for passed prerequisite gates.",
                    "approval-submitted",
                    UiApiRoutes.OperationsContinuityApprovalSubmit,
                    "Critical"),
                Row(
                    "operations-continuity.approve",
                    "Operations close",
                    "Approve submitted workflow",
                    OperationsGateKeyDto.Approval,
                    "Workflow is submitted and requested reviewer matches the assigned reviewer.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 2,
                    requiresIndependentReviewer: true,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack plus two distinct approval-gate control approvals.",
                    "approval-approved",
                    UiApiRoutes.OperationsContinuityApprovalApprove,
                    "Critical"),
                Row(
                    "operations-continuity.reject",
                    "Operations close",
                    "Reject submitted workflow",
                    OperationsGateKeyDto.Approval,
                    "Workflow is submitted or reviewer-assigned and rejection metadata is present.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Assigned reviewer",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Reviewer, rationale, reason code, and linked evidence when available.",
                    "approval-rejected",
                    UiApiRoutes.OperationsContinuityApprovalReject,
                    "Error"),
                Row(
                    "operations-continuity.close",
                    "Operations close",
                    "Close approved workflow",
                    OperationsGateKeyDto.Approval,
                    "All gates are passed and close readiness is ready to close.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster),
                    "Accounting operator",
                    "Controller",
                    requiredDistinctApprovals: 2,
                    requiresIndependentReviewer: true,
                    requiresReportPack: true,
                    requiresChecklistControlApprovals: true,
                    "Ready report pack, close-readiness pass, and close checklist control approvals.",
                    "workflow-closed",
                    UiApiRoutes.OperationsContinuityClose,
                    "Critical"),
                Row(
                    "operations-continuity.reopen",
                    "Governed exception",
                    "Reopen closed workflow",
                    OperationsGateKeyDto.Approval,
                    "Closed workflow requires incident, justification, approval reference, and impact summary.",
                    nameof(UserPermission.AdminMaintenance),
                    "Governed administrator",
                    "Governance approver",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Incident evidence, governance approval reference, justification, and impact summary.",
                    "workflow-reopened",
                    UiApiRoutes.OperationsContinuityReopen,
                    "Critical"),
                Row(
                    "operations-continuity.security-master-override",
                    "Reference data control",
                    "Approve Security Master override",
                    OperationsGateKeyDto.SecurityMaster,
                    "A pending Security Master override request has policy reference, rationale, and expiration metadata.",
                    AnyPermission(UserPermission.AdminMaintenance, UserPermission.ModifySecurityMaster),
                    "Security Master operator",
                    "Security Master approver",
                    requiredDistinctApprovals: 1,
                    requiresIndependentReviewer: true,
                    requiresReportPack: false,
                    requiresChecklistControlApprovals: false,
                    "Override id, policy reference, rationale, expiration date, and linked evidence.",
                    "security-master-override-approved",
                    UiApiRoutes.OperationsContinuitySecurityMasterOverrideApprove,
                    "Error")
            ]);

    private static OperationsApprovalPolicyMatrixRowDto Row(
        string policyKey,
        string workflowArea,
        string action,
        OperationsGateKeyDto gate,
        string trigger,
        string requiredPermission,
        string submitterRole,
        string reviewerRole,
        int requiredDistinctApprovals,
        bool requiresIndependentReviewer,
        bool requiresReportPack,
        bool requiresChecklistControlApprovals,
        string evidenceRequirement,
        string auditEventType,
        string route,
        string severity) =>
        new(
            policyKey,
            workflowArea,
            action,
            gate,
            trigger,
            requiredPermission,
            submitterRole,
            reviewerRole,
            requiredDistinctApprovals,
            requiresIndependentReviewer,
            requiresReportPack,
            requiresChecklistControlApprovals,
            evidenceRequirement,
            auditEventType,
            route,
            severity);

    private static string AnyPermission(params UserPermission[] permissions) =>
        $"Any({string.Join(",", permissions.Select(static permission => permission.ToString()))})";
}
