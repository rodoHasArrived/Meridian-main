using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// <see cref="SecurityMasterWorkbenchOptions.RequireIndependentReviewer"/> checks for governed
/// field-edit revisions. Submission already refuses a reviewer equal to the submitter; with the
/// requirement in force (its setting on, or the Institutional governance profile, as
/// <see cref="ISecurityMasterGovernanceRequirements"/> reports), the drafter cannot be named as
/// reviewer, and the submitter cannot approve. Two kinds of revision always require independent
/// review, whatever the setting: a repair-batch revision, and a historical identifier window
/// proposal. The window proposal is the governed way to backdate an identifier, which an import
/// can never do, so its proposer can neither review nor approve it.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    private bool RequiresIndependentReview(SecurityMasterRevisionRecord revision)
        => _requireIndependentReviewer()
            || revision.RepairBatchId is not null
            || SecurityMasterHistoricalIdentifierWindow.IsWindowRevision(revision);

    /// <summary>
    /// The drafter is the revision's actor until it is submitted (each transition records its own
    /// actor), so submission is the last point the maker's identity can be compared. The gate only
    /// lets the assigned reviewer approve, so refusing the drafter here keeps the maker from approving.
    /// </summary>
    private void EnsureReviewerIsIndependentOfDrafter(string reviewer, SecurityMasterRevisionRecord draft)
    {
        if (RequiresIndependentReview(draft)
            && string.Equals(reviewer.Trim(), (draft.DraftedBy ?? draft.Actor)?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The reviewer must be independent from the actor who drafted the revision; self-approval is not permitted.",
                "request");
        }
    }

    /// <summary>
    /// The approving reviewer must differ from the submitter, who is the revision's actor while it is
    /// Submitted. This also covers revisions submitted before the setting was turned on. A window
    /// proposal also records its proposer in the payload, so a proposer who had another operator
    /// submit it still cannot approve it.
    /// </summary>
    private void EnsureApproverIsIndependentOfSubmitter(ApproveSecurityMasterRevisionRequest request, SecurityMasterRevisionRecord submitted)
    {
        if (!RequiresIndependentReview(submitted))
        {
            return;
        }

        var approvingReviewer = (string.IsNullOrWhiteSpace(request.Reviewer) ? request.Actor : request.Reviewer)?.Trim();
        var approvingActor = request.Actor?.Trim();
        var maker = new[]
        {
            (submitted.SubmittedBy ?? submitted.Actor)?.Trim(),
            submitted.DraftedBy?.Trim(),
            TryGetWindowProposer(submitted)?.Trim()
        };
        if (maker.Any(m => !string.IsNullOrEmpty(m)
            && (string.Equals(approvingReviewer, m, StringComparison.OrdinalIgnoreCase)
                || (SecurityMasterHistoricalIdentifierWindow.IsWindowRevision(submitted)
                    && string.Equals(approvingActor, m, StringComparison.OrdinalIgnoreCase)))))
        {
            throw new ArgumentException(
                "The approving reviewer must be independent from the actor who submitted the revision; self-approval is not permitted.",
                "request");
        }
    }
}
