using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// <see cref="SecurityMasterWorkbenchOptions.RequireIndependentReviewer"/> checks for governed
/// field-edit revisions. Submission already refuses a reviewer equal to the submitter; with the
/// setting on, the drafter cannot be named as reviewer, and the submitter cannot approve.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandService
{
    /// <summary>
    /// The drafter is the revision's actor until it is submitted (each transition records its own
    /// actor), so submission is the last point the maker's identity can be compared. The gate only
    /// lets the assigned reviewer approve, so refusing the drafter here keeps the maker from approving.
    /// </summary>
    private void EnsureReviewerIsIndependentOfDrafter(string reviewer, SecurityMasterRevisionRecord draft)
    {
        if (_requireIndependentReviewer()
            && string.Equals(reviewer.Trim(), draft.Actor?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The reviewer must be independent from the actor who drafted the revision; self-approval is not permitted.",
                "request");
        }
    }

    /// <summary>
    /// The approving reviewer must differ from the submitter, who is the revision's actor while it is
    /// Submitted. This also covers revisions submitted before the setting was turned on.
    /// </summary>
    private void EnsureApproverIsIndependentOfSubmitter(ApproveSecurityMasterRevisionRequest request, SecurityMasterRevisionRecord submitted)
    {
        var approvingReviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? request.Actor : request.Reviewer;
        if (_requireIndependentReviewer()
            && string.Equals(approvingReviewer?.Trim(), submitted.Actor?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The approving reviewer must be independent from the actor who submitted the revision; self-approval is not permitted.",
                "request");
        }
    }
}
