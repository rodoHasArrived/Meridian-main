using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private static OperationsContinuityWorkflowDto RevisionApprovalDto(
        OperationsContinuityWorkflow workflow, OperationsContinuityWorkflowDto dto)
    {
        var approvalGates = dto.Gates.Where(static gate => gate.GateKey == OperationsGateKeyDto.Approval)
            .Select(static gate => gate with { DisplayName = "Security revision approval", NextActions = [] }).ToArray();
        var state = workflow.Approvals.LastOrDefault()?.Status ?? workflow.ApprovalState;
        return dto with
        {
            Status = OperationsWorkflowStatusDto.NotStarted,
            SubjectReviewState = state switch
            {
                OperationsApprovalStateDto.Pending => "Pending",
                OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned => "AwaitingReview",
                OperationsApprovalStateDto.Approved => "Approved",
                OperationsApprovalStateDto.Rejected => "Rejected",
                _ => "ReviewRequired"
            },
            Gates = approvalGates,
            CloseReadiness = null,
            ClosePackage = null,
            CloseChecklist = [],
            AccountingRecordSummary = null,
            LedgerPreview = null,
            BreakCases = [],
            ReconciliationLanes = [],
            DashboardSummary = null,
            EvidencePackages = [],
            ReviewedAutomation = null,
            ReportPackReadiness = new(false, null, "Report-pack close readiness does not apply to this revision approval.", []),
            Blockers = approvalGates.SelectMany(static gate => gate.Blockers).ToArray(),
            NextActions = []
        };
    }

    public async Task<OperationsTransitionResultDto> StartSecurityMasterRevisionWorkflowAsync(
        Guid workflowId, Guid revisionId, string actor, CancellationToken ct = default)
    {
        if (workflowId == Guid.Empty || revisionId == Guid.Empty || string.IsNullOrWhiteSpace(actor))
            return Failure("REVISION_WORKFLOW_ID_REQUIRED", "A prepared workflow identity, revision and authenticated actor are required.", []);
        var authority = _revisionWorkflowAuthority is null ? null
            : await _revisionWorkflowAuthority.ResolveAsync(revisionId, actor, ct).ConfigureAwait(false);
        if (authority is null || authority.WorkflowId != workflowId || authority.RevisionId != revisionId || authority.FundAccountId == Guid.Empty
            || authority.LedgerBookId == Guid.Empty || string.IsNullOrWhiteSpace(authority.PeriodId)
            || string.IsNullOrWhiteSpace(authority.Maker) || string.IsNullOrWhiteSpace(authority.EvidenceId)
            || authority.EvidenceSha256.Length != 64)
            return Failure("REVISION_WORKFLOW_AUTHORITY_REQUIRED", "Retained repair revision, maker, accounting scope and evidence could not be verified.", []);

        var retained = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        if (retained is not null)
        {
            if (retained.Purpose != OperationsWorkflowPurposeDto.SecurityMasterRevision
                || retained.SubjectRevisionId != revisionId || retained.SubjectMaker != authority.Maker
                || retained.SubjectEvidenceId != authority.EvidenceId || retained.SubjectEvidenceSha256 != authority.EvidenceSha256
                || retained.FundAccountId != authority.FundAccountId || retained.LedgerBookId != authority.LedgerBookId
                || retained.PeriodId != authority.PeriodId)
                return Failure("REVISION_WORKFLOW_ID_CONFLICT", "The prepared workflow identity already names another subject or scope.", []);
            return Success(await ToDtoAsync(retained, ct).ConfigureAwait(false));
        }
        if (_workflowStartCommitStore is null)
            return Failure("ATOMIC_WORKFLOW_START_UNAVAILABLE", "Revision workflows require atomic workflow and audit retention.", []);
        var now = DateTimeOffset.UtcNow;
        var workflow = OperationsContinuityWorkflow.Start(workflowId, authority.FundAccountId, authority.PeriodId,
            null, "security-master-revision", now, authority.LedgerBookId, authority);
        var audit = new OperationsWorkflowAuditDraft(workflowId, authority.FundAccountId, authority.PeriodId,
            "security-revision-workflow-started", OperationsWorkflowStatusDto.NotStarted,
            _statusDerivation.Derive(workflow), OperationsGateKeyDto.Approval,
            OperationsGateStatusDto.NotStarted, OperationsGateStatusDto.InProgress, actor,
            $"Retained revision {revisionId:D}; evidence {authority.EvidenceId}; sha256 {authority.EvidenceSha256}.",
            null, []);
        var committed = await _workflowStartCommitStore.CommitWorkflowStartAsync(workflow, audit, ct).ConfigureAwait(false);
        return Success(await ToDtoAsync(committed.Workflow, ct).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<OperationsEvidenceLinkDto>> ApprovalEvidenceAsync(
        Guid workflowId, string reference, IReadOnlyList<OperationsEvidenceLinkDto>? links, CancellationToken ct)
    {
        var workflow = await _repository.GetAsync(workflowId, ct).ConfigureAwait(false);
        return workflow?.Purpose == OperationsWorkflowPurposeDto.SecurityMasterRevision
            ? links ?? []
            : EnsureReportPackEvidence(reference, links);
    }
}
