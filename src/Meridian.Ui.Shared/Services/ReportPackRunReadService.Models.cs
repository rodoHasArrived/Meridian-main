using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackRunReadService
{
    private static readonly ReportPackDistributionPolicy[] DistributionPolicies =
    [
        new(
            "board-reporting-committee",
            "Board reporting committee",
            "Board",
            "Board portal",
            "fund-controller",
            "/reporting/report-packs?recipient=board",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "investor-relations",
            "Investor relations",
            "Investor communications",
            "Investor portal",
            "investor-relations",
            "/reporting/report-packs?recipient=investor-relations",
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4)),
        new(
            "compliance-archive",
            "Compliance archive",
            "Compliance",
            "Retained evidence vault",
            "compliance-reviewer",
            "/reporting/evidence?subject=report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2)),
        new(
            "fund-operations",
            "Fund operations",
            "Operations",
            "Operations close packet",
            "fund-operations",
            "/accounting/report-pack",
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(2))
    ];

    public sealed record ReportPackDistributionPolicy(
        string DistributionId,
        string Recipient,
        string RecipientRole,
        string Channel,
        string Owner,
        string Route,
        TimeSpan ApprovalSla,
        TimeSpan PublicationSla,
        TimeSpan DeliverySla,
        TimeSpan CorrectionSla);

    private sealed record UnifiedReportingRun(
        WorkstationReportingRunPayload Payload,
        DateTimeOffset UpdatedAtUtc);

    private sealed record ReportingLineChangeCounts(int Changed, int Added, int Removed);

    private sealed record ScheduleDeliveryReadiness(
        bool IsReady,
        string Summary,
        IReadOnlyList<string> Blockers);
}
