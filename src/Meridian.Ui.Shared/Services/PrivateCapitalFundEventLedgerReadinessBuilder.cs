using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

internal sealed record PrivateCapitalFundEventLedgerReadinessProjection(
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string Label,
    string Reason,
    string NextAction,
    string? NextActionRoute);

internal static class PrivateCapitalFundEventLedgerReadinessBuilder
{
    public static PrivateCapitalFundEventLedgerReadinessProjection Build(
        ManualJournalEntryStatusDto approvalState,
        int evidenceLinkCount,
        bool isPostingReady,
        bool isReportReady,
        bool isPublished,
        int reportOutputCount,
        bool hasCriticalValidationIssues,
        string activityRoute,
        string evidenceRoute,
        string? approvalRoute,
        string? primaryReportRoute)
    {
        if (hasCriticalValidationIssues || approvalState is ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.NeedsFix)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.Blocked,
                approvalState == ManualJournalEntryStatusDto.Rejected ? "Approval rejected" : "Blocked",
                "Critical validation or approval state blocks posting, capital-account reliance, and report output.",
                "Repair fund event",
                approvalRoute ?? activityRoute);
        }

        if (evidenceLinkCount == 0)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
                "Evidence missing",
                "Retained source, approval, or settlement evidence is required before posting and report output.",
                "Attach retained evidence",
                evidenceRoute);
        }

        if (approvalState == ManualJournalEntryStatusDto.Draft)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending,
                "Approval pending",
                "Submit the fund-event journal for approval before posting or stakeholder report output.",
                "Submit approval",
                activityRoute);
        }

        if (!isPostingReady)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.PostingReview,
                "Posting review",
                "Approval and evidence exist, but the GL impact is not posting ready.",
                "Review ledger impact",
                activityRoute);
        }

        if (!isReportReady)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.ReportReview,
                reportOutputCount == 0 ? "Report output missing" : "Report review",
                "Ledger impact is ready, but the retained report output is not ready for stakeholder use.",
                "Prepare report output",
                primaryReportRoute ?? activityRoute);
        }

        if (isPublished)
        {
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.Published,
                "Published",
                "The event is linked to retained evidence, posting-ready ledger impact, capital-account movement, and published report output.",
                "Open published report",
                primaryReportRoute ?? evidenceRoute);
        }

        return new(
            PrivateCapitalFundEventLedgerReadinessDto.Ready,
            "Ready",
            "The event has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication.",
            "Review report output",
            primaryReportRoute ?? activityRoute);
    }
}
