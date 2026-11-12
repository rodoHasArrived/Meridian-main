using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private static string RequireIndependentPostingApproval(
        PostPostingRuleJournalCandidateRequestDto request,
        string actor)
    {
        var preparer = RequireText(request.Candidate.Actor, nameof(request.Candidate.Actor));
        if (string.Equals(actor, preparer, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generated accounting posting candidates require approval by an operator independent from the candidate preparer.");
        }

        var approvalId = RequireText(request.ApprovalId, nameof(request.ApprovalId));
        PreparerAttestationApprovalBoundary.RequireOrdinaryReference(approvalId, nameof(request.ApprovalId));
        if (request.ApprovalEvidence.Any(evidence =>
            PreparerAttestationApprovalBoundary.IsReservedReference(evidence.EvidenceId)
            || PreparerAttestationApprovalBoundary.IsReservedReference(evidence.EvidenceUri)
            || PreparerAttestationApprovalBoundary.IsReservedReference(evidence.SourceReference)
            || PreparerAttestationApprovalBoundary.IsReservedReference(evidence.SubjectId)))
            throw new InvalidOperationException("A preparer attestation cannot supply ordinary posting approval evidence.");
        return approvalId;
    }
}
