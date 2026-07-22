using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportingDeliveryReleaseGateTests
{
    public static TheoryData<ReportingRunStatus> UnreleasedStatuses => new()
    {
        ReportingRunStatus.Draft,
        ReportingRunStatus.InReview,
        ReportingRunStatus.Approved,
        ReportingRunStatus.Failed
    };

    [Theory]
    [MemberData(nameof(UnreleasedStatuses))]
    public void DeliverReportingRun_UnreleasedRun_FailsWithoutPersistingAttempt(ReportingRunStatus status)
    {
        var sut = new ReportPackDeliveryService(new ReportPackWorkflowService());
        var manifest = new ReportingOutputManifest(
            "run-gated",
            "investor-monthly-statement",
            new DateOnly(2026, 6, 30),
            status,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.Scheduled);
        var target = new ReportingScheduleDeliveryTargetDto("investor-relations");

        var act = () => sut.DeliverReportingRun(manifest, target, "scheduler");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only Released runs may enter distribution*");
        sut.ListAttempts().Should().BeEmpty();
    }
}
