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
    public async Task ExecuteAsync_VersionsRerunsForSameContract()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-1", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        var first = await sut.ExecuteAsync(contract, CancellationToken.None);
        var second = await sut.ExecuteAsync(contract, CancellationToken.None);

        first.RunId.Should().Be("job-1-20260501");
        second.RunId.Should().Be("job-1-20260501-v2");
        first.RunSeriesId.Should().Be("job-1-20260501");
        second.RunSeriesId.Should().Be(first.RunSeriesId);
        first.RunAttemptOrdinal.Should().Be(1);
        second.RunAttemptOrdinal.Should().Be(2);
        second.PriorRunId.Should().Be(first.RunId);
        first.Sections.Select(s => s.Lineage.DatasetSnapshotId).Should().Equal(second.Sections.Select(s => s.Lineage.DatasetSnapshotId));
        first.Sections.Should().OnlyContain(s => s.Lineage.DatasetSnapshotId == s.DatasetSnapshotId && s.Lineage.ReconciliationCheckpointId == s.ReconciliationCheckpointId);
    }

    [Fact]
    public async Task ExecuteAsync_CapitalAccountManifestDoesNotInvokeLegacyProjectionSource()
    {
        var legacyProjectionSource = new RecordingPartnersCapitalSource();
        var sut = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            runStore: null,
            runNotifier: null,
            partnersCapitalSource: legacyProjectionSource);
        var asOf = new DateOnly(2026, 5, 31);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto("fund-alpha"),
            "period-2026-05",
            asOf,
            new ReportingLedgerBookSelectionDto(Guid.NewGuid(), "PRIMARY"),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: false,
            IncludeEvidenceAppendix: false);

        var manifest = await sut.ExecuteAsync(
            new ReportingJobContract(
                "capital-canonical",
                "capital-account-statement",
                asOf,
                ReportingRunTrigger.AdHoc,
                0,
                "alice",
                FixedNow,
                ResolvedParameters: parameters),
            CancellationToken.None);

        legacyProjectionSource.CaptureCount.Should().Be(0,
            "capital-account bytes are sourced later from the exact certified ledger report pack");
        manifest.CertifiedPartnersCapital.Should().BeNull();
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
    public async Task ExecuteAsync_BlocksSilentRegenerationOfReleasedManifest()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-restate", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);
        var released = await ExecuteAndReleaseAsync(sut, contract);

        var act = async () => await sut.ExecuteAsync(contract, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Released manifest*");
        sut.GetAudit(released.RunId).Should().Contain(e => e.Action == "RestatementBlocked");
        sut.GetManifest(released.RunId)!.Status.Should().Be(ReportingRunStatus.Released);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresRetryReasonForAuthorizedRestatement()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-restate", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);
        var released = await ExecuteAndReleaseAsync(sut, contract);

        var act = async () => await sut.ExecuteAsync(contract with { AllowRestatement = true }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a RetryReason*");
        sut.GetAudit(released.RunId).Should().Contain(e => e.Action == "RestatementBlocked");
    }

    [Fact]
    public async Task ExecuteAsync_AllowsAuditableRestatementWhenAuthorized()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var contract = new ReportingJobContract("job-restate", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);
        var released = await ExecuteAndReleaseAsync(sut, contract);

        var restatement = await sut.ExecuteAsync(
            contract with { AllowRestatement = true, RetryReason = "Q2 NAV correction" },
            CancellationToken.None);

        restatement.RunId.Should().Be("job-restate-20260501-v2");
        restatement.PriorRunId.Should().Be(released.RunId);
        sut.GetAudit(released.RunId).Should().Contain(e =>
            e.Action == "RestatementAuthorized" && e.Notes.Contains("Q2 NAV correction"));
    }

    private static async Task<ReportingOutputManifest> ExecuteAndReleaseAsync(
        ReportingOrchestrationService sut,
        ReportingJobContract contract)
    {
        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);
        await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.InReview, "bob", "Reviewer", "review", CancellationToken.None);
        await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Approved, "cora", "ComplianceOfficer", "approved", CancellationToken.None);
        await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Released, "dan", "OperationsLead", "release", CancellationToken.None);
        return sut.GetManifest(manifest.RunId)!;
    }

    [Fact]
    public async Task ExecuteAsync_DetectsReleasedSeriesHeadWhenGloballyCappedListingHidesIt()
    {
        // A durable store whose ListRuns() is globally capped and does NOT surface older runs.
        var store = new CappedListingRunStore();
        var contract = new ReportingJobContract("job-capped", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        // Release the series through one instance; the manifest lives in the store but is hidden by ListRuns().
        var seeding = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, store);
        var released = await ExecuteAndReleaseAsync(seeding, contract);

        // A fresh instance has no in-memory manifests, so it must resolve the released head from the
        // store by series-run-id probing rather than the capped listing.
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, store);
        var act = async () => await sut.ExecuteAsync(contract, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Released manifest*");
        // The released manifest must not have been overwritten by a regenerated v1.
        store.GetManifest(released.RunId)!.Status.Should().Be(ReportingRunStatus.Released);
    }

    [Fact]
    public async Task ExecuteAsync_StillRequiresAuthorizationAfterAFailedRestatementAttempt()
    {
        var store = new CappedListingRunStore();
        var contract = new ReportingJobContract("job-failretry", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        // v1 released.
        var seeding = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, store);
        var released = await ExecuteAndReleaseAsync(seeding, contract);

        // An authorized restatement whose render fails persists a Failed v2 at the absolute head.
        var failing = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new AlwaysFailingRenderer(), () => FixedNow, store);
        var restate = async () => await failing.ExecuteAsync(
            contract with { AllowRestatement = true, RetryReason = "attempt one" },
            CancellationToken.None);
        await restate.Should().ThrowAsync<InvalidOperationException>();

        // A later run without authorization must still be blocked: v1 remains the released report,
        // even though the failed v2 now sits at the absolute head of the series.
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, store);
        var act = async () => await sut.ExecuteAsync(contract, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Released manifest*");
        store.GetManifest(released.RunId)!.Status.Should().Be(ReportingRunStatus.Released);

        // An authorized restatement uses the released head as its prior basis (not the failed attempt),
        // so its lineage and grid diff compare against the released report being superseded.
        var authorized = await sut.ExecuteAsync(
            contract with { AllowRestatement = true, RetryReason = "attempt two" },
            CancellationToken.None);
        authorized.PriorRunId.Should().Be(released.RunId);
    }

    private sealed class CappedListingRunStore : IReportingRunStore
    {
        private readonly Dictionary<string, ReportingOutputManifest> manifests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<ReportingRunAuditEntry>> audits = new(StringComparer.OrdinalIgnoreCase);

        // Simulate a globally capped listing that omits older runs (e.g. many newer runs in other series).
        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) => [];

        public ReportingOutputManifest? GetManifest(string runId) => manifests.GetValueOrDefault(runId);

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) =>
            audits.TryGetValue(runId, out var entries) ? entries : [];

        public Task SaveAsync(ReportingOutputManifest manifest, IReadOnlyList<ReportingRunAuditEntry> auditTrail, CancellationToken ct = default)
        {
            manifests[manifest.RunId] = manifest;
            audits[manifest.RunId] = auditTrail;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteDueSchedulesAsync_FailsClosedOutsideHostedScheduleAuthority()
    {
        var sut = new ReportingOrchestrationService(new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var schedules = new[]
        {
            new ReportingScheduleContract(
                "sched-investor",
                "investor-monthly-statement",
                "0 8 1 * *",
                new DateOnly(2026, 5, 1),
                FixedNow.AddMinutes(-5),
                1,
                "scheduler")
        };

#pragma warning disable CS0618 // Verify the fail-closed behavior of the retained compatibility member.
        Func<Task> act = async () =>
            await sut.ExecuteDueSchedulesAsync(schedules, FixedNow, CancellationToken.None);
#pragma warning restore CS0618

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*host reporting-schedule adapter*");
        sut.GetManifest("sched-investor-20260501").Should().BeNull();
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
    public async Task ExecuteAsync_RetainsReportWriterLineDiffsForRerunAttempts()
    {
        var template = new ReportingTemplateMetadata(
            "writer-comparison-template",
            ReportingTemplateFamily.CustomReport,
            "Writer Comparison Template",
            "1.0.0",
            ["comparison"],
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            ReportWriterGrids:
            [
                new ReportWriterGridDefinitionDto(
                    "sector-pnl",
                    "Sector P&L",
                    ReportWriterGridKindDto.Detail,
                    RowFields: ["sector"],
                    Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")])
            ]);
        var sut = new ReportingOrchestrationService(new SingleTemplateCatalog(template), new DeterministicReportingSectionRenderer(), () => FixedNow);
        var first = await sut.ExecuteAsync(
            new ReportingJobContract(
                "writer-rerun",
                template.TemplateId,
                new DateOnly(2026, 5, 7),
                ReportingRunTrigger.AdHoc,
                0,
                "alice",
                FixedNow,
                DatasetRows:
                [
                    new Dictionary<string, string> { ["sector"] = "Technology", ["pnl"] = "10" },
                    new Dictionary<string, string> { ["sector"] = "Rates", ["pnl"] = "5" }
                ]),
            CancellationToken.None);

        var second = await sut.ExecuteAsync(
            new ReportingJobContract(
                "writer-rerun",
                template.TemplateId,
                new DateOnly(2026, 5, 7),
                ReportingRunTrigger.AdHoc,
                0,
                "alice",
                FixedNow.AddMinutes(2),
                DatasetRows:
                [
                    new Dictionary<string, string> { ["sector"] = "Technology", ["pnl"] = "12" },
                    new Dictionary<string, string> { ["sector"] = "Credit", ["pnl"] = "4" }
                ],
                RetryReason: "corrected portfolio marks"),
            CancellationToken.None);

        second.RunId.Should().Be("writer-rerun-20260507-v2");
        second.PriorRunId.Should().Be(first.RunId);
        second.RetryReason.Should().Be("corrected portfolio marks");
        var diff = second.ReportWriterGridDiffs.Should().ContainSingle().Subject;
        diff.GridId.Should().Be("sector-pnl");
        diff.ChangedRowCount.Should().Be(1);
        diff.AddedRowCount.Should().Be(1);
        diff.RemovedRowCount.Should().Be(1);
        diff.Rows.Should().Contain(row => row.RowKey == "Technology" && row.State == ReportWriterDiffRowStateDto.Changed);
        diff.Rows.Should().Contain(row => row.RowKey == "Credit" && row.State == ReportWriterDiffRowStateDto.Added);
        diff.Rows.Should().Contain(row => row.RowKey == "Rates" && row.State == ReportWriterDiffRowStateDto.Removed);
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

    [Fact]
    public async Task ExecuteAsync_NotifiesRunChangedWithRunId()
    {
        var notifier = new RecordingRunNotifier();
        var sut = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, runStore: null, runNotifier: notifier);
        var contract = new ReportingJobContract("job-notify", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        notifier.RunIds.Should().Contain(manifest.RunId);
    }

    [Fact]
    public async Task TransitionApprovalAsync_NotifiesRunChangedOnEachTransition()
    {
        var notifier = new RecordingRunNotifier();
        var sut = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, runStore: null, runNotifier: notifier);
        var contract = new ReportingJobContract("job-notify-2", "sec-13f-packet", new DateOnly(2026, 5, 2), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);
        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);
        notifier.RunIds.Clear();

        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.InReview, "bob", "Reviewer", "review", CancellationToken.None)).Should().BeTrue();
        (await sut.TransitionApprovalAsync(manifest.RunId, ReportingRunStatus.Approved, "cora", "ComplianceOfficer", "approved", CancellationToken.None)).Should().BeTrue();

        notifier.RunIds.Should().HaveCount(2).And.OnlyContain(id => id == manifest.RunId);
    }

    [Fact]
    public async Task ExecuteAsync_WithThrowingNotifier_StillPersistsAndDoesNotThrow()
    {
        // The notifier is best-effort — a throwing implementation must never surface on the
        // run-execution path. Reaching the assertions at all proves ExecuteAsync did not rethrow.
        var sut = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new DeterministicReportingSectionRenderer(), () => FixedNow, runStore: null, runNotifier: new ThrowingRunNotifier());
        var contract = new ReportingJobContract("job-throw", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        var manifest = await sut.ExecuteAsync(contract, CancellationToken.None);

        manifest.RunId.Should().Be("job-throw-20260501");
        sut.GetManifest(manifest.RunId).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NotifiesEvenWhenTheRunFails()
    {
        var notifier = new RecordingRunNotifier();
        var sut = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(), new AlwaysFailingRenderer(), () => FixedNow, runStore: null, runNotifier: notifier);
        var contract = new ReportingJobContract("job-fail-notify", "investor-monthly-statement", new DateOnly(2026, 5, 1), ReportingRunTrigger.AdHoc, 0, "alice", FixedNow);

        await sut.Awaiting(s => s.ExecuteAsync(contract, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();

        // The failed manifest is persisted (and therefore notified) before the throw propagates.
        notifier.RunIds.Should().Contain("job-fail-notify-20260501");
    }

    private sealed class RecordingRunNotifier : IReportingRunNotifier
    {
        public List<string> RunIds { get; } = new();

        public void NotifyRunChanged(string runId) => RunIds.Add(runId);
    }

    private sealed class ThrowingRunNotifier : IReportingRunNotifier
    {
        public void NotifyRunChanged(string runId) => throw new InvalidOperationException("notifier boom");
    }

    private sealed class RecordingPartnersCapitalSource : IReportingPartnersCapitalSource
    {
        public int CaptureCount { get; private set; }

        public Task<CertifiedPartnersCapitalProjection?> CaptureAsync(
            ReportingRunParametersDto parameters,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult<CertifiedPartnersCapitalProjection?>(null);
        }
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

    private sealed class SingleTemplateCatalog(ReportingTemplateMetadata template) : IReportingTemplateCatalog
    {
        public ReportingTemplateMetadata Get(string templateId) =>
            string.Equals(template.TemplateId, templateId, StringComparison.OrdinalIgnoreCase)
                ? template
                : throw new KeyNotFoundException($"Unknown reporting template '{templateId}'.");

        public IReadOnlyList<ReportingTemplateMetadata> ListTemplates() => [template];
    }
}
