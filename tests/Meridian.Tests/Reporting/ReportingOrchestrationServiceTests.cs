using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Reporting;

/// <summary>
/// Guards governed report-pack regeneration, schedule execution, lineage evidence, and approval gates for investor, SEC, and shadow NAV reporting runs.
/// </summary>
public sealed class ReportingOrchestrationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_IsDeterministicForSameContract()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-1", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        var first = await sut.ExecuteAsync(contract, CancellationToken.None);
        var second = await sut.ExecuteAsync(contract, CancellationToken.None);

        first.RunId.Should().Be(second.RunId);
        first.Artifacts.Should().Equal(second.Artifacts);
        first.Sections.Select(s => s.Hash).Should().Equal(second.Sections.Select(s => s.Hash));
        first.Sections.Should().OnlyContain(s => s.Lineage.DatasetSnapshotId == s.DatasetSnapshotId && s.Lineage.ReconciliationCheckpointId == s.ReconciliationCheckpointId);
    }

    [Fact]
    public async Task TransitionApprovalAsync_EnforcesGateAndRole()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-2", "sec-13f-packet", new DateOnly(2026, 5, 2), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);
        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Released, "bob", "Reviewer", "skip", CancellationToken.None)).Should().BeFalse();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.InReview, "bob", "Reviewer", "review", CancellationToken.None)).Should().BeTrue();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Approved, "cora", "ComplianceOfficer", "approved", CancellationToken.None)).Should().BeTrue();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Released, "dan", "OperationsLead", "release", CancellationToken.None)).Should().BeTrue();

        sut.GetManifest(manifest.RunId)!.Status.Should().Be(ReportingRunStatus.Released);
        sut.GetAudit(manifest.RunId).Should().Contain(e => e.Action == "ApprovalDenied" && e.Notes.Contains("target=Released"));
    }

    [Fact]
    public async Task ExecuteDueSchedulesAsync_OnlyRunsDueSchedulesInStableOrder()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var dueLater = FixedNow.AddMinutes(5);
        var schedules = new[]
        {
            new ReportingScheduleContract("sched-shadow", "shadow-nav-daily-pack", "0 22 * * 1-5", new DateOnly(2026, 5, 3), FixedNow.AddMinutes(-2), 1, "scheduler"),
            new ReportingScheduleContract("sched-investor", "investor-monthly-statement", "0 8 1 * *", new DateOnly(2026, 5, 1), FixedNow.AddMinutes(-5), 1, "scheduler"),
            new ReportingScheduleContract("sched-sec", "sec-13f-packet", "0 8 * * 5", new DateOnly(2026, 5, 8), dueLater, 1, "scheduler")
        };

        var generated = await sut.ExecuteDueSchedulesAsync(schedules, FixedNow, CancellationToken.None);

        generated.Select(m => m.RunId).Should().Equal("sched-investor-20260501", "sched-shadow-20260503");
        generated.Should().OnlyContain(m => m.Trigger == ReportingRunTrigger.Scheduled && m.ScheduleId != null);
        sut.GetAudit("sched-shadow-20260503").Should().ContainSingle(e => e.Action == "RunGenerated" && e.Notes.Contains("trigger=Scheduled"));
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTransientFailuresAndAuditsAttemptCount()
    {
        var renderer = new FailingOnceRenderer();
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), renderer, () => FixedNow);
        var contract = new ReportingJobContract("sched-1", "shadow-nav-daily-pack", new DateOnly(2026, 5, 3), ReportingRunTrigger.Scheduled, 1, "scheduler", FixedNow, "0 0 * * 1-5");

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        manifest.AttemptCount.Should().Be(2);
        sut.GetAudit(manifest.RunId).Select(a => a.Action).Should().ContainInOrder("RunRetry", "RunGenerated");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesReportAccessPolicyOnManifest()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var policy = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "investor-relations")]);
        var contract = new ReportingJobContract(
            "restricted-run",
            "investor-monthly-statement",
            new DateOnly(2026, 5, 1),
            ReportingRunTrigger.AdHoc,
            0,
            "alice",
            FixedNow,
            AccessPolicy: policy);

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        manifest.AccessPolicy.Should().NotBeNull();
        manifest.AccessPolicy!.Mode.Should().Be(ReportAccessModeDto.Restricted);
        manifest.AccessPolicy.Principals.Should().ContainSingle(principal =>
            principal.Kind == ReportAccessPrincipalKindDto.Group &&
            principal.PrincipalId == "investor-relations");
    }

    [Fact]
    public async Task ExecuteAsync_PersistsFailedManifestAfterRetriesExhausted()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new AlwaysFailingRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-fail", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 1, "alice", FixedNow);

        await sut.Awaiting(s => s.ExecuteAsync(contract, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();

        var failed = sut.GetManifest("job-fail-20260501");
        failed.Should().NotBeNull();
        failed!.Status.Should().Be(ReportingRunStatus.Failed);
        failed.AttemptCount.Should().Be(2);
        failed.FailureReason.Should().Be("renderer unavailable");
        sut.GetAudit(failed.RunId).Count(e => e.Action == "RunRetry" || e.Action == "RunFailed").Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAndTransitionApproval_PersistsManifestAndAuditTrailToRunStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "reporting-runs");
        var store = new FileReportingRunStore(new ReportingRunStoreOptions(root), NullLogger<FileReportingRunStore>.Instance);
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, store);
        var contract = new ReportingJobContract("job-persist", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.Scheduled, 0, "scheduler", FixedNow, ScheduleId: "sched-investor");

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.InReview, "reviewer", "Reviewer", "ready", CancellationToken.None)).Should().BeTrue();

        var reloaded = new FileReportingRunStore(new ReportingRunStoreOptions(root), NullLogger<FileReportingRunStore>.Instance);
        reloaded.GetManifest(manifest.RunId)!.Status.Should().Be(ReportingRunStatus.InReview);
        reloaded.GetManifest(manifest.RunId)!.ScheduleId.Should().Be("sched-investor");
        reloaded.GetAudit(manifest.RunId).Select(static entry => entry.Action).Should().ContainInOrder("RunGenerated", "ApprovalTransition");
        reloaded.ListRuns().Should().ContainSingle(run => run.Manifest.RunId == manifest.RunId);

        var restarted = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow.AddMinutes(1), reloaded);
        (await restarted.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Approved, "ops", "OperationsLead", "approved after restart", CancellationToken.None)).Should().BeTrue();
        reloaded.GetManifest(manifest.RunId)!.Status.Should().Be(ReportingRunStatus.Approved);
        reloaded.GetAudit(manifest.RunId).Select(static entry => entry.Action).Should().ContainInOrder("RunGenerated", "ApprovalTransition", "ApprovalTransition");
    }

    private sealed class FailingOnceRenderer : IReportingSectionRenderer
    {
        private readonly DeterministicReportingSectionRenderer inner = new();
        private bool failed;

        public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt)
        {
            if (!failed)
            {
                failed = true;
                throw new InvalidOperationException("dataset snapshot not sealed");
            }

            return inner.RenderSection(runId, contract, template, sectionId, attempt);
        }
    }

    private sealed class AlwaysFailingRenderer : IReportingSectionRenderer
    {
        public ReportingSectionManifest RenderSection(string runId, ReportingJobContract contract, ReportingTemplateMetadata template, string sectionId, int attempt)
            => throw new InvalidOperationException("renderer unavailable");
    }
}
