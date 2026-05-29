namespace Meridian.Ledger;

/// <summary>
/// Lifecycle state for a governed automated journal draft.
/// </summary>
public enum AutomatedJournalApprovalStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Posted,
}
