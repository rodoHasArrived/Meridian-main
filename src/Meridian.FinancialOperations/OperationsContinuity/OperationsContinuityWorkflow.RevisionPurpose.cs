using Meridian.Contracts.Workstation;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflow
{
    private static void EnsureRevisionAuthorityMatchesStart(SecurityMasterRevisionWorkflowAuthority? authority,
        Guid workflowId, Guid fundAccountId, Guid? ledgerBookId, string periodId)
    {
        if (authority is not null
            && (authority.WorkflowId != workflowId || authority.RevisionId == Guid.Empty
                || authority.FundAccountId != fundAccountId || authority.LedgerBookId != ledgerBookId
                || authority.PeriodId.Trim() != periodId.Trim() || string.IsNullOrWhiteSpace(authority.Maker)
                || string.IsNullOrWhiteSpace(authority.EvidenceId) || !Sha256Digest.IsCanonical(authority.EvidenceSha256)))
            throw new ArgumentException("The revision approval authority must match this exact workflow and accounting scope.");
    }

    [JsonIgnore]
    public string? SubjectReference => SubjectRevisionId is { } revisionId ? $"security-master-revision:{revisionId:D}" : null;

    private OperationsWorkflowBlockerDto? ValidateRevisionApprovalSubject(string reference, string rationale, string reviewer)
    {
        if (SubjectRevisionId is null || string.IsNullOrWhiteSpace(SubjectMaker)
            || string.IsNullOrWhiteSpace(SubjectEvidenceId) || SubjectEvidenceSha256?.Length != 64
            || reference != SubjectReference
            || !rationale.Contains($"[{SubjectReference}]", StringComparison.Ordinal))
            return CreateChecklistControlApprovalBlocker("REVISION_APPROVAL_EVIDENCE_REQUIRED",
                "Approval must name this exact revision and retain its maker and source evidence.");
        return string.Equals(SubjectMaker.Trim(), reviewer.Trim(), StringComparison.OrdinalIgnoreCase)
            ? CreateChecklistControlApprovalBlocker("APPROVAL_INDEPENDENT_REVIEWER_REQUIRED", "The revision maker cannot approve its repair.")
            : null;
    }

    internal bool SharesStartScope(OperationsContinuityWorkflow other)
        => Purpose == other.Purpose && (Purpose == OperationsWorkflowPurposeDto.SecurityMasterRevision
            ? SubjectRevisionId == other.SubjectRevisionId
            : FundAccountId == other.FundAccountId && string.Equals(PeriodId, other.PeriodId, StringComparison.OrdinalIgnoreCase)
                && (LedgerBookId is null || other.LedgerBookId is null || LedgerBookId == other.LedgerBookId));
}
