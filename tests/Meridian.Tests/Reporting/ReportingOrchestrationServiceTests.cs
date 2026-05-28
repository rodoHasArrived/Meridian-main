using FluentAssertions;
using Meridian.Application.Reporting;

namespace Meridian.Tests.Reporting;

public sealed class ReportingOrchestrationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_IsDeterministicForSameContract()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog());
        var contract = new ReportingJobContract("job-1", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", DateTimeOffset.UtcNow);

        var first = await sut.ExecuteAsync(contract, CancellationToken.None);
        var second = await sut.ExecuteAsync(contract, CancellationToken.None);

        first.RunId.Should().Be(second.RunId);
        first.Sections.Select(s => s.Hash).Should().Equal(second.Sections.Select(s => s.Hash));
    }

    [Fact]
    public async Task TransitionApprovalAsync_EnforcesGateAndRole()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog());
        var contract = new ReportingJobContract("job-2", "sec-13f-packet", new DateOnly(2026, 5, 2), ReportingRunTrigger.AdHoc, 0, "alice", DateTimeOffset.UtcNow);
        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Released, "bob", "Reviewer", "skip", CancellationToken.None)).Should().BeFalse();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.InReview, "bob", "Reviewer", "review", CancellationToken.None)).Should().BeTrue();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Approved, "cora", "ComplianceOfficer", "approved", CancellationToken.None)).Should().BeTrue();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Released, "dan", "OperationsLead", "release", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ProducesScheduledRunAudit()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog());
        var contract = new ReportingJobContract("sched-1", "shadow-nav-daily-pack", new DateOnly(2026, 5, 3), ReportingRunTrigger.Scheduled, 1, "scheduler", DateTimeOffset.UtcNow, "0 0 * * 1-5");

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);
        var audit = sut.GetAudit(manifest.RunId);

        audit.Should().ContainSingle(e => e.Action == "RunGenerated" && e.Notes.Contains("trigger=Scheduled"));
    }
}
