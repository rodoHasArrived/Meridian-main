using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using IReportingScheduleStore = Meridian.Reporting.IReportingScheduleStore;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards reporting reads and schedule mutations when multiple tenants and workstation hosts share
/// one operational reporting store.
/// </summary>
public sealed class ReportingOperationalConcurrencyTests
{
    private const string CertifiedAccountingPeriodId = "66666666-6666-6666-6666-666666666666";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scenario_RestrictedRunFloodAndCrossTenantDuplicate_AuthorizedRunRemainsVisible()
    {
        var accessContext = new ReportAccessQueryContext(
            ActorPrincipalId: "operator-a",
            CompanyId: "company-a",
            TenantId: "tenant-a",
            RequireBoundScope: true);
        var workflow = new ReportPackWorkflowService();
        var workflowRecord = workflow.Create(
            "fund-a",
            "account-a",
            "2026-07",
            new VersionedReportTemplateIdDto("investor-monthly-statement", 1),
            "operator-a",
            accessContext: accessContext);
        var sharedRunId = workflowRecord.ReportId.ToString("D");
        var snapshots = Enumerable.Range(1, 201)
            .Select(index => BuildRestrictedSnapshot(
                $"restricted-run-{index:D3}",
                "tenant-a",
                "company-a",
                "fund-a",
                "2026-07",
                workflowRecord.AccessPolicySnapshotHash!))
            .Append(BuildSnapshot(
                sharedRunId,
                "tenant-b",
                "company-a",
                "fund-a",
                "2026-07",
                workflowRecord.AccessPolicySnapshotHash!))
            .Append(BuildSnapshot(
                sharedRunId,
                "tenant-a",
                "company-a",
                "fund-a",
                "2026-07",
                workflowRecord.AccessPolicySnapshotHash!))
            .ToArray();
        var runStore = new TenantAwareCappedRunStore(snapshots);
        var service = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            runStore,
            workflow);

        var payload = service.BuildPayload(accessContext);

        payload.RecentRuns.Should().Contain(run => run.RunId == sharedRunId);
        payload.RecentRuns
            .Single(run => run.RunId == $"report-pack:{sharedRunId}")
            .NextActions
            .Should()
            .ContainSingle(action => action.Kind == "governed-run" && action.IsEnabled);
        runStore.CompanyScopedListCalls.Should().Be(2);
        runStore.ScopedListCalls.Should().Be(0);
        runStore.ScopedLookupCalls.Should().Be(1);
        runStore.UnscopedListCalls.Should().Be(0);
        runStore.UnscopedLookupCalls.Should().Be(0);
    }

    [Fact]
    public async Task Scenario_FileStoreCompanyListingFiltersBeforeLimit()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            nameof(ReportingOperationalConcurrencyTests),
            Guid.NewGuid().ToString("N"));
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var target = BuildCertifiedSnapshot(
            "authorized-run",
            "tenant-a",
            "company-a",
            "fund-a",
            CertifiedAccountingPeriodId,
            new string('a', 64));

        try
        {
            await store.SaveAsync(target.Manifest, target.AuditTrail);
            var foreign = BuildCertifiedSnapshot(
                "other-company-run",
                "tenant-a",
                "company-b",
                "fund-b",
                CertifiedAccountingPeriodId,
                new string('b', 64));
            await store.SaveAsync(foreign.Manifest, foreign.AuditTrail);

            store.ListRuns(1)
                .Should()
                .NotContain(snapshot => snapshot.Manifest.RunId == target.Manifest.RunId);
            store.ListRuns("tenant-a", "company-a", 1)
                .Should()
                .ContainSingle(snapshot => snapshot.Manifest.RunId == target.Manifest.RunId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scenario_TwoFileRunStores_StaleReplicaCannotOverwriteReleasedRunOrAudit()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            nameof(ReportingOperationalConcurrencyTests),
            Guid.NewGuid().ToString("N"));
        var firstStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var staleStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var firstHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            firstStore);
        var staleHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow.AddMinutes(1),
            staleStore);

        try
        {
            var run = await firstHost.ExecuteAsync(
                new ReportingJobContract(
                    "file-cas-run",
                    "shadow-nav-daily-pack",
                    new DateOnly(2026, 7, 26),
                    ReportingRunTrigger.AdHoc,
                    0,
                    "creator",
                    FixedNow),
                CancellationToken.None);
            staleHost.GetManifest(run.RunId)!.Status.Should().Be(ReportingRunStatus.Draft);

            (await firstHost.TransitionApprovalAsync(
                run.RunId,
                ReportingRunStatus.InReview,
                "reviewer",
                "Reviewer",
                "reviewed",
                CancellationToken.None)).Should().BeTrue();
            (await firstHost.TransitionApprovalAsync(
                run.RunId,
                ReportingRunStatus.Approved,
                "operations",
                "OperationsLead",
                "approved",
                CancellationToken.None)).Should().BeTrue();
            (await firstHost.TransitionApprovalAsync(
                run.RunId,
                ReportingRunStatus.Released,
                "operations",
                "OperationsLead",
                "released",
                CancellationToken.None)).Should().BeTrue();

            var staleWrite = () => staleHost.TransitionApprovalAsync(
                run.RunId,
                ReportingRunStatus.InReview,
                "stale-reviewer",
                "Reviewer",
                "stale transition",
                CancellationToken.None);
            await staleWrite.Should().ThrowAsync<ReportingRunConcurrencyException>();

            firstStore.GetManifest(run.RunId)!.Status.Should().Be(ReportingRunStatus.Released);
            var retainedAudit = firstStore.GetAudit(run.RunId);
            retainedAudit.Select(entry => entry.Action)
                .Should()
                .ContainInOrder(
                    "RunGenerated",
                    "ApprovalTransition",
                    "ApprovalTransition",
                    "ApprovalTransition");
            retainedAudit
                .Should()
                .Contain(entry => entry.Actor == "reviewer")
                .And
                .NotContain(entry => entry.Actor == "stale-reviewer");
            staleHost.GetManifest(run.RunId)!.Status.Should().Be(ReportingRunStatus.Released);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scenario_TwoHostsCreatingSameScopedRun_OnlyDurableClaimOwnerRenders()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            nameof(ReportingOperationalConcurrencyTests),
            Guid.NewGuid().ToString("N"));
        var firstStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var secondStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var firstRenderer = new BlockingRenderer();
        var secondRenderer = new CountingRenderer();
        var firstHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            firstRenderer,
            () => FixedNow,
            firstStore);
        var secondHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            secondRenderer,
            () => FixedNow.AddMinutes(1),
            secondStore);
        var contract = BuildCertifiedContract("durable-create-claim");

        try
        {
            var first = Task.Run(
                () => firstHost.ExecuteAsync(contract, CancellationToken.None));
            await firstRenderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var second = () => secondHost.ExecuteAsync(
                contract,
                CancellationToken.None);
            await second.Should()
                .ThrowAsync<ReportingRunCreateClaimException>()
                .WithMessage("*another durable owner*");
            secondRenderer.RenderCount.Should().Be(0);

            firstRenderer.Release.TrySetResult(true);
            var retained = await first;
            retained.Status.Should().Be(ReportingRunStatus.Draft);
            firstStore.GetManifest("tenant-a", retained.RunId)!.Status
                .Should().Be(ReportingRunStatus.Draft);
            firstStore.GetAudit("tenant-a", retained.RunId)
                .Select(static entry => entry.Action)
                .Should().Equal("RunGenerated");
        }
        finally
        {
            firstRenderer.Release.TrySetResult(true);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scenario_FallbackCreateCollision_ReturnsWinnerWithoutFailedOverwrite()
    {
        var store = new ControllableRunStore();
        var firstHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            store);
        var secondHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow.AddMinutes(1),
            store);
        var contract = BuildCertifiedContract("fallback-create-collision");
        store.BlockNextWrite();

        var first = firstHost.ExecuteAsync(contract, CancellationToken.None);
        await store.WaitForBlockedWriteAsync();
        var winner = await secondHost.ExecuteAsync(contract, CancellationToken.None);
        store.ReleaseBlockedWrite();
        var observed = await first;

        observed.Should().BeEquivalentTo(winner);
        ((IReportingRunStore)store).GetManifest("tenant-a", winner.RunId)!.Status
            .Should().Be(ReportingRunStatus.Draft);
        ((IReportingRunStore)store).GetAudit("tenant-a", winner.RunId)
            .Select(static entry => entry.Action)
            .Should().Equal("RunGenerated");
    }

    [Fact]
    public async Task Scenario_FallbackCreateCollision_DifferentCertifiedRequestFailsClosed()
    {
        var store = new ControllableRunStore();
        var firstHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            store);
        var secondHost = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow.AddMinutes(1),
            store);
        var firstContract = BuildCertifiedContract(
            "fallback-create-request-mismatch");
        var winningContract = firstContract with
        {
            BrandingThemeId = "different-certified-request"
        };
        store.BlockNextWrite();

        var first = firstHost.ExecuteAsync(firstContract, CancellationToken.None);
        await store.WaitForBlockedWriteAsync();
        var winner = await secondHost.ExecuteAsync(
            winningContract,
            CancellationToken.None);
        store.ReleaseBlockedWrite();
        var losingRequest = async () => await first;

        await losingRequest.Should()
            .ThrowAsync<ReportingRunCreateClaimException>()
            .WithMessage("*different certified request*");
        winner.BrandingThemeId.Should().Be("different-certified-request");
        ((IReportingRunStore)store).GetManifest("tenant-a", winner.RunId)!.BrandingThemeId
            .Should().Be("different-certified-request");
        ((IReportingRunStore)store).GetAudit("tenant-a", winner.RunId)
            .Select(static entry => entry.Action)
            .Should().Equal("RunGenerated");
    }

    [Fact]
    public async Task Scenario_ConcurrentApprovalCalls_AreSerializedBeforeAuditMutation()
    {
        var store = new ControllableRunStore();
        var service = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            store);
        var run = await service.ExecuteAsync(
            new ReportingJobContract(
                "concurrent-approval-run",
                "shadow-nav-daily-pack",
                new DateOnly(2026, 7, 26),
                ReportingRunTrigger.AdHoc,
                0,
                "creator",
                FixedNow),
            CancellationToken.None);
        store.BlockNextWrite();

        var first = service.TransitionApprovalAsync(
            run.RunId,
            ReportingRunStatus.InReview,
            "reviewer-a",
            "Reviewer",
            "first",
            CancellationToken.None);
        await store.WaitForBlockedWriteAsync();
        var second = service.TransitionApprovalAsync(
            run.RunId,
            ReportingRunStatus.InReview,
            "reviewer-b",
            "Reviewer",
            "second",
            CancellationToken.None);
        await Task.Yield();

        store.SaveCallCount.Should().Be(
            2,
            "the second lifecycle operation must not enter persistence while the first is blocked");
        store.ReleaseBlockedWrite();
        (await first).Should().BeTrue();
        (await second).Should().BeFalse();
        store.GetAudit(run.RunId).Select(entry => entry.Action)
            .Should()
            .Equal("RunGenerated", "ApprovalTransition", "ApprovalDenied");
    }

    [Fact]
    public async Task Scenario_DurableWriteFailure_RestoresManifestAndAuditCache()
    {
        var store = new ControllableRunStore();
        var service = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            store);
        var run = await service.ExecuteAsync(
            new ReportingJobContract(
                "failed-approval-write",
                "shadow-nav-daily-pack",
                new DateOnly(2026, 7, 26),
                ReportingRunTrigger.AdHoc,
                0,
                "creator",
                FixedNow),
            CancellationToken.None);
        store.FailNextWrite();

        var failed = () => service.TransitionApprovalAsync(
            run.RunId,
            ReportingRunStatus.InReview,
            "reviewer",
            "Reviewer",
            "must roll back",
            CancellationToken.None);
        await failed.Should().ThrowAsync<IOException>();

        service.GetManifest(run.RunId)!.Status.Should().Be(ReportingRunStatus.Draft);
        service.GetAudit(run.RunId).Select(entry => entry.Action)
            .Should()
            .Equal("RunGenerated");
        store.GetManifest(run.RunId)!.Status.Should().Be(ReportingRunStatus.Draft);
    }

    [Fact]
    public void Scenario_StaleScheduleHostUpdates_OtherHostsRowIsNotLost()
    {
        var store = new SharedAtomicScheduleStore(
        [
            BuildSchedule("schedule-a", "operator-a")
        ]);
        var firstHost = CreateScheduleService(store);
        var staleSecondHost = CreateScheduleService(store);

        firstHost.Upsert(new ReportingScheduleUpsertRequestDto(
            "schedule-b",
            "investor-monthly-statement",
            "0 9 1 * *",
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            2,
            "operator-b",
            State: ReportingScheduleStateDto.Draft));
        staleSecondHost.SetState(
            "schedule-a",
            ReportingScheduleStateDto.Disabled);

        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "schedule-a"
            && schedule.State == ReportingScheduleStateDto.Disabled);
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "schedule-b"
            && schedule.State == ReportingScheduleStateDto.Draft);
        store.AtomicUpsertCalls.Should().Be(2);
        store.LegacySaveCalls.Should().Be(0);
    }

    [Fact]
    public void Scenario_ScheduleUpsertWithFutureRetainedRevision_AdvancesMonotonically()
    {
        var retained = BuildSchedule("future-revision", "operator-a") with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        var store = new SharedAtomicScheduleStore([retained]);
        var service = CreateScheduleService(store);

        var updated = service.Upsert(new ReportingScheduleUpsertRequestDto(
            retained.ScheduleId,
            retained.TemplateId,
            retained.CronExpression,
            retained.NextAsOfDate,
            retained.DueAtUtc,
            retained.MaxRetries,
            retained.RequestedBy,
            State: retained.State));

        updated.UpdatedAtUtc.Should().BeAfter(retained.UpdatedAtUtc);
        updated.UpdatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == retained.ScheduleId
            && schedule.UpdatedAtUtc == updated.UpdatedAtUtc);
        store.AtomicUpsertCalls.Should().Be(1);
        store.LegacySaveCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-cron")]
    [InlineData("0 0 30 2 *")]
    public void Scenario_ScheduleUpsert_InvalidOrImpossibleCronFailsBeforePersistence(
        string cronExpression)
    {
        var store = new SharedAtomicScheduleStore([]);
        var service = CreateScheduleService(store);

        var upsert = () => service.Upsert(new ReportingScheduleUpsertRequestDto(
            "invalid-cron",
            "investor-monthly-statement",
            cronExpression,
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            2,
            "operator-a",
            State: ReportingScheduleStateDto.Draft));

        upsert.Should().Throw<ArgumentException>()
            .WithMessage("*cron expression*");
        store.Load().Should().BeEmpty();
        store.AtomicUpsertCalls.Should().Be(0);
    }

    [Fact]
    public void Scenario_RetainedInvalidCronFailsClosedDuringServiceConstruction()
    {
        var store = new SharedAtomicScheduleStore(
        [
            BuildSchedule("retained-invalid-cron", "operator-a") with
            {
                CronExpression = "not-a-cron"
            }
        ]);

        var create = () => CreateScheduleService(store);

        create.Should().Throw<InvalidDataException>()
            .WithMessage("*invalid cron expression*");
    }

    [Fact]
    public void Scenario_ScheduleAtMaximumRevision_FailsClosedWithoutPoisoningRetainedState()
    {
        var retained = BuildSchedule("maximum-revision", "operator-a") with
        {
            UpdatedAtUtc = DateTimeOffset.MaxValue.AddTicks(-1)
        };
        var store = new SharedAtomicScheduleStore([retained]);
        var service = CreateScheduleService(store);

        var update = () => service.SetState(
            retained.ScheduleId,
            ReportingScheduleStateDto.Disabled);

        update.Should().Throw<InvalidDataException>()
            .WithMessage("*cannot advance to DateTimeOffset.MaxValue*");
        service.ListSchedules().Should().ContainSingle(schedule =>
            schedule.ScheduleId == retained.ScheduleId
            && schedule.State == retained.State
            && schedule.UpdatedAtUtc == retained.UpdatedAtUtc);
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == retained.ScheduleId
            && schedule.State == retained.State
            && schedule.UpdatedAtUtc == retained.UpdatedAtUtc);
        store.AtomicUpsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task Scenario_InFlightScheduleRun_CannotOverwriteAConcurrentStateChange()
    {
        var retained = BuildSchedule("in-flight-run", "operator-a") with
        {
            State = ReportingScheduleStateDto.Active
        };
        var store = new SharedAtomicScheduleStore([retained]);
        var catalog = new DefaultReportingTemplateCatalog();
        var evaluator = new ControllableReadyEvaluator();
        var service = new ReportingScheduleService(
            new ReportingOrchestrationService(
                catalog,
                new DeterministicReportingSectionRenderer(),
                () => FixedNow),
            store,
            readinessService: new ReportingRunReadinessService(
                catalog,
                dependencyEvaluator: evaluator,
                utcNow: () => FixedNow,
                options: new ReportingRunReadinessOptions(
                    AllowLegacyUnscopedDrafts: true)));
        var runTask = service.RunNowAsync(
            retained.ScheduleId,
            retained.RequestedBy,
            CancellationToken.None);

        try
        {
            await evaluator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.SetState(
                retained.ScheduleId,
                ReportingScheduleStateDto.Disabled);
        }
        finally
        {
            evaluator.Release.TrySetResult(true);
        }

        var completeRun = async () => await runTask;
        await completeRun.Should().ThrowAsync<ReportingScheduleConcurrencyException>();
        service.ListSchedules().Should().ContainSingle(schedule =>
            schedule.ScheduleId == retained.ScheduleId
            && schedule.State == ReportingScheduleStateDto.Disabled
            && schedule.RunCount == 0);
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == retained.ScheduleId
            && schedule.State == ReportingScheduleStateDto.Disabled
            && schedule.RunCount == 0);
    }

    [Fact]
    public async Task Scenario_TwoScheduleHosts_RefreshAndLeaseBeforeGeneration()
    {
        var store = new SharedAtomicScheduleStore([]);
        var firstRenderer = new BlockingRenderer();
        var secondRenderer = new CountingRenderer();
        var firstHost = CreateRunnableScheduleService(store, firstRenderer);
        var secondHost = CreateRunnableScheduleService(store, secondRenderer);
        var due = BuildSchedule("multi-host-due", "operator-a") with
        {
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = FixedNow.AddMinutes(-1)
        };
        store.Upsert(due);

        var first = Task.Run(
            () => firstHost.RunDueForWorkerAsync(
                FixedNow,
                CancellationToken.None));
        await firstRenderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var losingPoll = await secondHost.RunDueForWorkerAsync(
            FixedNow,
            CancellationToken.None);
        losingPoll.Result.Runs.Should().BeEmpty();
        secondRenderer.RenderCount.Should().Be(0);

        firstRenderer.Release.TrySetResult(true);
        var winner = await first;
        winner.Result.Runs.Should().ContainSingle();
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == due.ScheduleId
            && schedule.RunCount == 1
            && schedule.DueAtUtc > FixedNow);
    }

    [Fact]
    public async Task Scenario_MinuteCadenceSchedule_AdvancesWithSharedCronParserInUtc()
    {
        var due = BuildSchedule("minute-cadence", "operator-a") with
        {
            CronExpression = "*/15 * * * *",
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = FixedNow
        };
        var store = new SharedAtomicScheduleStore([due]);
        var renderer = new CountingRenderer();
        var service = CreateRunnableScheduleService(store, renderer);

        var result = await service.RunDueForWorkerAsync(
            FixedNow,
            CancellationToken.None);

        result.Result.Runs.Should().ContainSingle();
        renderer.RenderCount.Should().BeGreaterThan(0);
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == due.ScheduleId
            && schedule.DueAtUtc == FixedNow.AddMinutes(15)
            && schedule.NextAsOfDate == due.NextAsOfDate);
    }

    [Fact]
    public void Scenario_ScheduleLease_ExpiresWithFencingAgainstLateOwner()
    {
        var schedule = BuildCurrentSchedule("lease-expiry") with
        {
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = FixedNow
        };
        var store = new SharedAtomicScheduleStore([schedule]);
        var first = store.TryClaimExecution(
            schedule,
            "host-a",
            FixedNow,
            TimeSpan.FromMinutes(1));

        first.Should().NotBeNull();
        store.TryClaimExecution(
                schedule,
                "host-b",
                FixedNow.AddSeconds(59),
                TimeSpan.FromMinutes(1))
            .Should().BeNull();
        var reclaimed = store.TryClaimExecution(
            schedule,
            "host-b",
            FixedNow.AddMinutes(1),
            TimeSpan.FromMinutes(1));

        reclaimed.Should().NotBeNull();
        reclaimed!.LeaseVersion.Should().Be(first!.LeaseVersion + 1);
        var staleCompletion = () => store.UpsertClaimedExecution(
            schedule with
            {
                RunCount = 1,
                UpdatedAtUtc = FixedNow.AddMinutes(1)
            },
            schedule.UpdatedAtUtc,
            first);
        staleCompletion.Should().Throw<ReportingScheduleExecutionLeaseException>();
        store.Load().Should().ContainSingle().Which.RunCount.Should().Be(0);
        store.ReleaseExecutionLease(
            schedule.TenantId!,
            schedule.CompanyId!,
            schedule.ScheduleId,
            first);
        store.RenewExecutionLease(
                schedule,
                reclaimed,
                FixedNow.AddMinutes(1).AddSeconds(30),
                TimeSpan.FromMinutes(1))
            .Should().NotBeNull(
                "the expired owner's fenced release must not clear the replacement lease");
    }

    [Fact]
    public async Task Scenario_UnreadyReportingDeployment_RejectsAllScheduleMutationsAndExecution()
    {
        var retained = BuildSchedule("deployment-blocked", "operator-a") with
        {
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = FixedNow
        };
        var store = new SharedAtomicScheduleStore([retained]);
        var service = new ReportingScheduleService(
            new ReportingOrchestrationService(
                new DefaultReportingTemplateCatalog(),
                new DeterministicReportingSectionRenderer(),
                () => FixedNow),
            store,
            deliveryService: null,
            governedTemplateCatalog: null,
            datasetSourceService: null,
            readinessService: null,
            certificationService: null,
            governanceCoordinator: null,
            destinationResolver: null,
            deploymentReadinessService:
                new FixedDeploymentReadinessService(isReady: false));

        var upsert = () => service.Upsert(new ReportingScheduleUpsertRequestDto(
            "blocked-create",
            "investor-monthly-statement",
            "0 9 1 * *",
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            0,
            "operator-a"));
        upsert.Should().Throw<InvalidOperationException>()
            .WithMessage("*deployment is ready*");
        var stateChange = () => service.SetState(
            retained.ScheduleId,
            ReportingScheduleStateDto.Paused);
        stateChange.Should().Throw<InvalidOperationException>()
            .WithMessage("*deployment is ready*");
        var runNow = () => service.RunNowAsync(
            retained.ScheduleId,
            retained.RequestedBy,
            CancellationToken.None);
        await runNow.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*deployment is ready*");
        var runDue = () => service.RunDueForWorkerAsync(
            FixedNow,
            CancellationToken.None);
        await runDue.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*deployment is ready*");
        store.Load().Should().ContainSingle().Which.Should().Be(retained);
    }

    [Fact]
    public void Scenario_TwoFileScheduleServices_ConflictRefreshesThenRetrySucceeds()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            nameof(ReportingOperationalConcurrencyTests),
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "reporting-schedules.json");
        var accessContext = new ReportAccessQueryContext(
            ActorPrincipalId: "operator-a",
            CompanyId: "company-a",
            TenantId: "tenant-a",
            RequireBoundScope: true);
        Directory.CreateDirectory(root);
        var firstStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(path),
            NullLogger<FileReportingScheduleStore>.Instance);
        var staleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(path),
            NullLogger<FileReportingScheduleStore>.Instance);
        firstStore.Upsert(BuildCurrentSchedule("file-schedule-cas"));
        var exactRetry = staleStore.Load().Should().ContainSingle().Subject;
        var retryWrite = () => staleStore.Upsert(exactRetry);
        retryWrite.Should().NotThrow(
            "a JSON round-trip must not turn an exact content retry into a conflict");
        var firstHost = CreateScheduleService(firstStore);
        var staleHost = CreateScheduleService(staleStore);

        try
        {
            firstHost.SetState(
                "file-schedule-cas",
                ReportingScheduleStateDto.Paused,
                accessContext);

            var staleWrite = () => staleHost.SetState(
                "file-schedule-cas",
                ReportingScheduleStateDto.Disabled,
                accessContext);
            staleWrite.Should().Throw<ReportingScheduleConcurrencyException>();
            staleHost.ListSchedules(accessContext)
                .Should()
                .ContainSingle(schedule =>
                    schedule.ScheduleId == "file-schedule-cas"
                    && schedule.State == ReportingScheduleStateDto.Paused);

            staleHost.SetState(
                    "file-schedule-cas",
                    ReportingScheduleStateDto.Disabled,
                    accessContext)
                .State
                .Should()
                .Be(ReportingScheduleStateDto.Disabled);
            firstStore.Load()
                .Should()
                .ContainSingle(schedule =>
                    schedule.ScheduleId == "file-schedule-cas"
                    && schedule.State == ReportingScheduleStateDto.Disabled);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileScheduleStore_PreservesLegacyInterfaceCompatibility()
    {
#pragma warning disable CS0618
        var legacyType = typeof(Meridian.Ui.Shared.Services.IReportingScheduleStore);
        legacyType
            .IsAssignableFrom(typeof(FileReportingScheduleStore))
            .Should()
            .BeTrue();
        typeof(ReportingScheduleService)
            .GetConstructors()
            .Should()
            .Contain(constructor =>
                constructor.GetParameters().Length > 1
                && constructor.GetParameters()[1].ParameterType == legacyType);
#pragma warning restore CS0618
    }

    private static ReportingScheduleService CreateScheduleService(
        IReportingScheduleStore store) =>
        new(
            new ReportingOrchestrationService(
                new DefaultReportingTemplateCatalog(),
                new DeterministicReportingSectionRenderer(),
                () => FixedNow),
            store);

    private static ReportingScheduleService CreateRunnableScheduleService(
        IReportingScheduleStore store,
        IReportingSectionRenderer renderer)
    {
        var catalog = new DefaultReportingTemplateCatalog();
        return new ReportingScheduleService(
            new ReportingOrchestrationService(
                catalog,
                renderer,
                () => FixedNow),
            store,
            readinessService: new ReportingRunReadinessService(
                catalog,
                dependencyEvaluator: new AlwaysReadyEvaluator(),
                utcNow: () => FixedNow,
                options: new ReportingRunReadinessOptions(
                    AllowLegacyUnscopedDrafts: true)));
    }

    private static ReportingRunSnapshot BuildSnapshot(
        string runId,
        string tenantId,
        string companyId,
        string fundId,
        string periodId,
        string policyHash) =>
        new(
            new ReportingOutputManifest(
                runId,
                "investor-monthly-statement",
                new DateOnly(2026, 7, 31),
                ReportingRunStatus.Draft,
                ImmutableArray<ReportingSectionManifest>.Empty,
                ImmutableArray<string>.Empty,
                1,
                ReportingRunTrigger.AdHoc,
                OperationalScope: new ReportingOperationalScope(
                    tenantId,
                    $"organization-{tenantId}",
                    companyId,
                    fundId,
                    "book-a",
                    periodId),
                ImmutableAccessScope: new ReportingAccessScope(
                    $"policy-{tenantId}",
                    "1",
                    ReportingGovernanceAccessMode.CompanyWide,
                    OwnerPrincipalId: null,
                    AllowOwnerAccess: false,
                    Principals: ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                    PolicyHash: policyHash)),
            [],
            FixedNow);

    private static ReportingRunSnapshot BuildCertifiedSnapshot(
        string runId,
        string tenantId,
        string companyId,
        string fundId,
        string periodId,
        string policyHash)
    {
        var scope = new ReportingOperationalScope(
            tenantId,
            $"organization-{tenantId}",
            companyId,
            fundId,
            "book-a",
            periodId);
        var access = new ReportingAccessScope(
            $"policy-{tenantId}",
            "1",
            ReportingGovernanceAccessMode.CompanyWide,
            "operator",
            AllowOwnerAccess: true,
            Principals: ImmutableArray.Create(
                new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    "operator")),
            PolicyHash: policyHash);
        var asOfDate = new DateOnly(2026, 7, 31);
        var template = new VersionedReportTemplateIdDto(
            "investor-monthly-statement",
            1);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(fundId),
            periodId,
            asOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: scope.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var parametersCanonicalJson = JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = fundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId,
            asOfDate = asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = parameters.AccountingBasis.ToString(),
            presentationCurrency = parameters.PresentationCurrency,
            consolidationLevel = parameters.ConsolidationLevel.ToString(),
            outputFormat = parameters.OutputFormat.ToString(),
            finality = parameters.Finality.ToString(),
            includeSupportingSchedules = parameters.IncludeSupportingSchedules,
            includeEvidenceAppendix = parameters.IncludeEvidenceAppendix,
            templateParameters = new Dictionary<string, string>()
        });
        var parametersHash = ComputeSha256(parametersCanonicalJson);
        const string sourceCheckpointId = "checkpoint-reporting-concurrency";
        var sourceCheckpointHash = new string('c', 64);
        const string reconciliationCheckpointId = "reconciliation-reporting-concurrency";
        var reconciliationCheckpointHash = new string('f', 64);
        var readiness = new ReportingRunReadinessDto(
            "readiness-reporting-concurrency",
            FixedNow.AddMinutes(-1),
            template,
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "accounting-close",
                    "Accounting close",
                    ReportingRunReadinessStatusDto.Ready,
                    "Exact close evidence is retained.",
                    0,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    EvidenceReferences: ["evidence-reconciliation"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('d', 64));
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account"] = "cash",
                ["amount"] = "100.00"
            });
        var snapshotHash = ComputeSha256(JsonSerializer.Serialize(new
        {
            template = new { template.Name, template.Version },
            scope,
            access,
            parametersHash,
            sourceCheckpointId,
            sourceCheckpointHash,
            reconciliationId = reconciliationCheckpointId,
            reconciliationHash = reconciliationCheckpointHash,
            readinessHash = readiness.EvidenceHash,
            certifiedDatasetHash = FileReportingRunStore.ComputeCertifiedRowsHash(rows)
        }));
        var snapshot = new ReportingCertifiedSnapshotScope(
            tenantId,
            scope.OrganizationId,
            companyId,
            fundId,
            scope.BookId,
            periodId,
            $"snapshot-{runId}",
            snapshotHash,
            reconciliationCheckpointId,
            FixedNow,
            SourceCheckpointId: sourceCheckpointId,
            SourceCheckpointHash: sourceCheckpointHash,
            ReconciliationCheckpointHash: reconciliationCheckpointHash,
            ParametersCanonicalJson: parametersCanonicalJson,
            ParametersHash: parametersHash);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "LedgerJournal",
            $"ledger-journal-{tenantId}",
            tenantId,
            scope.OrganizationId,
            companyId,
            fundId,
            scope.BookId,
            periodId,
            ReportingAccountingBasisDto.Gaap.ToString(),
            asOfDate,
            FixedNow,
            42,
            1,
            rows.Length,
            sourceCheckpointId,
            sourceCheckpointHash,
            FixedNow,
            ImmutableArray.Create(
                $"reporting-source-checkpoint:{sourceCheckpointId}:{sourceCheckpointHash}",
                "evidence-source"));
        var manifest = new ReportingOutputManifest(
            runId,
            template.Name,
            asOfDate,
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            RunSeriesId: runId,
            ResolvedTemplate: template,
            ResolvedParameters: parameters,
            Readiness: readiness,
            OperationalScope: scope,
            ImmutableAccessScope: access,
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: source,
            CertifiedDatasetRows: rows);
        return new ReportingRunSnapshot(manifest, [], FixedNow);
    }

    private static ReportingJobContract BuildCertifiedContract(string jobId)
    {
        var certified = BuildCertifiedSnapshot(
            runId: $"{jobId}-seed",
            tenantId: "tenant-a",
            companyId: "company-a",
            fundId: "fund-a",
            periodId: CertifiedAccountingPeriodId,
            policyHash: new string('a', 64)).Manifest;
        return new ReportingJobContract(
            jobId,
            certified.TemplateId,
            certified.AsOfDate,
            ReportingRunTrigger.AdHoc,
            MaxRetries: 0,
            RequestedBy: "operator",
            RequestedAtUtc: FixedNow,
            DatasetRows: certified.CertifiedDatasetRows,
            ResolvedTemplate: certified.ResolvedTemplate,
            ResolvedParameters: certified.ResolvedParameters,
            Readiness: certified.Readiness,
            OperationalScope: certified.OperationalScope,
            ImmutableAccessScope: certified.ImmutableAccessScope,
            CertifiedSnapshot: certified.CertifiedSnapshot,
            AuthoritativeSource: certified.AuthoritativeSource);
    }

    private static ReportingRunSnapshot BuildRestrictedSnapshot(
        string runId,
        string tenantId,
        string companyId,
        string fundId,
        string periodId,
        string policyHash)
    {
        var snapshot = BuildSnapshot(
            runId,
            tenantId,
            companyId,
            fundId,
            periodId,
            policyHash);
        return snapshot with
        {
            Manifest = snapshot.Manifest with
            {
                ImmutableAccessScope = new ReportingAccessScope(
                    $"restricted-policy-{runId}",
                    "1",
                    ReportingGovernanceAccessMode.Restricted,
                    "different-operator",
                    AllowOwnerAccess: true,
                    Principals: ImmutableArray.Create(
                        new ReportingAccessPrincipalScope(
                            ReportingAccessPrincipalKind.User,
                            "different-operator")),
                    PolicyHash: policyHash)
            }
        };
    }

    private static ReportingScheduleRecordDto BuildSchedule(
        string scheduleId,
        string requestedBy) =>
        new(
            scheduleId,
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 7, 31),
            FixedNow.AddDays(1),
            2,
            requestedBy,
            ReportingScheduleStateDto.Draft,
            FixedNow,
            FixedNow);

    private static ReportingScheduleRecordDto BuildCurrentSchedule(string scheduleId)
    {
        var access = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            OwnerPrincipalId: "operator-a",
            Principals:
            [
                new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.User,
                    "operator-a")
            ],
            CompanyId: "company-a",
            AllowOwnerAccess: true);
        return new ReportingScheduleRecordDto(
            scheduleId,
            "shadow-nav-daily-pack",
            "0 8 * * *",
            new DateOnly(2026, 7, 31),
            FixedNow.AddDays(1),
            1,
            "operator-a",
            ReportingScheduleStateDto.Draft,
            FixedNow.AddDays(-1),
            FixedNow.AddDays(-1),
            Template: new VersionedReportTemplateIdDto("shadow-nav-daily-pack", 1),
            RunParameters: new ReportingRunParametersDto(
                new ReportingRunScopeDto("fund-a"),
                "2026-07",
                new DateOnly(2026, 7, 31),
                new ReportingLedgerBookSelectionDto(LedgerBookCode: "book-a"),
                ReportingAccountingBasisDto.Gaap,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Draft,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true),
            TenantId: "tenant-a",
            CompanyId: "company-a",
            AccessPolicySnapshot: access,
            AccessPolicySnapshotHash:
                ReportingScheduleService.ComputeAccessPolicySnapshotHash(access),
            DeliveryTargetsSnapshotHash: null);
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class TenantAwareCappedRunStore(
        IReadOnlyList<ReportingRunSnapshot> snapshots)
        : IReportingRunStore
    {
        public int ScopedListCalls { get; private set; }

        public int CompanyScopedListCalls { get; private set; }

        public int ScopedLookupCalls { get; private set; }

        public int UnscopedListCalls { get; private set; }

        public int UnscopedLookupCalls { get; private set; }

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25)
        {
            UnscopedListCalls++;
            return snapshots.Take(Math.Min(Math.Clamp(limit, 1, 200), 200)).ToArray();
        }

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(
            string tenantId,
            int limit = 25)
        {
            ScopedListCalls++;
            return snapshots
                .Where(snapshot => string.Equals(
                    snapshot.Manifest.OperationalScope?.TenantId,
                    tenantId,
                    StringComparison.Ordinal))
                .Take(Math.Clamp(limit, 1, 200))
                .ToArray();
        }

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(
            string tenantId,
            string? companyId,
            int limit = 25) =>
            ListRuns(tenantId, companyId, offset: 0, limit: limit);

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(
            string tenantId,
            string? companyId,
            int offset,
            int limit)
        {
            CompanyScopedListCalls++;
            return snapshots
                .Where(snapshot => string.Equals(
                        snapshot.Manifest.OperationalScope?.TenantId,
                        tenantId,
                        StringComparison.Ordinal)
                    && (string.IsNullOrWhiteSpace(companyId)
                        || string.Equals(
                            snapshot.Manifest.OperationalScope?.CompanyId,
                            companyId,
                            StringComparison.Ordinal)))
                .Skip(offset)
                .Take(Math.Clamp(limit, 1, 200))
                .ToArray();
        }

        public ReportingOutputManifest? GetManifest(string runId)
        {
            UnscopedLookupCalls++;
            return snapshots.FirstOrDefault(snapshot => string.Equals(
                snapshot.Manifest.RunId,
                runId,
                StringComparison.OrdinalIgnoreCase))?.Manifest;
        }

        public ReportingOutputManifest? GetManifest(string tenantId, string runId)
        {
            ScopedLookupCalls++;
            return snapshots.SingleOrDefault(snapshot =>
                string.Equals(
                    snapshot.Manifest.OperationalScope?.TenantId,
                    tenantId,
                    StringComparison.Ordinal)
                && string.Equals(
                    snapshot.Manifest.RunId,
                    runId,
                    StringComparison.OrdinalIgnoreCase))?.Manifest;
        }

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) => [];

        public Task SaveAsync(
            ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ControllableRunStore : IReportingRunStore
    {
        private readonly object _gate = new();
        private ReportingRunSnapshot? _snapshot;
        private TaskCompletionSource<bool>? _blockedWriteEntered;
        private TaskCompletionSource<bool>? _blockedWriteRelease;
        private int _blockNextWrite;
        private int _failNextWrite;
        private int _saveCallCount;

        public int SaveCallCount => Volatile.Read(ref _saveCallCount);

        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25)
        {
            lock (_gate)
            {
                return _snapshot is null ? [] : [_snapshot];
            }
        }

        public ReportingOutputManifest? GetManifest(string runId)
        {
            lock (_gate)
            {
                return _snapshot is not null
                    && string.Equals(
                        _snapshot.Manifest.RunId,
                        runId,
                        StringComparison.OrdinalIgnoreCase)
                        ? _snapshot.Manifest
                        : null;
            }
        }

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
        {
            lock (_gate)
            {
                return _snapshot is not null
                    && string.Equals(
                        _snapshot.Manifest.RunId,
                        runId,
                        StringComparison.OrdinalIgnoreCase)
                        ? _snapshot.AuditTrail.ToArray()
                        : [];
            }
        }

        public string? GetRevision(string runId)
        {
            lock (_gate)
            {
                return _snapshot is null
                    ? null
                    : ReportingRunStoreRevision.Compute(
                        _snapshot.Manifest,
                        _snapshot.AuditTrail);
            }
        }

        public Task SaveAsync(
            ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            CancellationToken ct = default) =>
            SaveAsync(manifest, auditTrail, expectedRevision: null, ct: ct);

        public async Task SaveAsync(
            ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            string? expectedRevision,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _saveCallCount);
            if (Interlocked.Exchange(ref _blockNextWrite, 0) == 1)
            {
                _blockedWriteEntered!.TrySetResult(true);
                await _blockedWriteRelease!.Task.WaitAsync(ct);
            }
            if (Interlocked.Exchange(ref _failNextWrite, 0) == 1)
            {
                throw new IOException("simulated durable write failure");
            }

            lock (_gate)
            {
                var currentRevision = _snapshot is null
                    ? null
                    : ReportingRunStoreRevision.Compute(
                        _snapshot.Manifest,
                        _snapshot.AuditTrail);
                var candidateRevision = ReportingRunStoreRevision.Compute(
                    manifest,
                    auditTrail);
                if (currentRevision is null && expectedRevision is not null)
                {
                    throw ReportingRunConcurrencyException.ForMissing(
                        tenantId: null,
                        runId: manifest.RunId,
                        expectedRevision: expectedRevision);
                }
                if (currentRevision is not null
                    && expectedRevision is null
                    && !ReportingRunStoreRevision.Matches(
                        currentRevision,
                        candidateRevision))
                {
                    throw ReportingRunConcurrencyException.ForConflict(
                        tenantId: null,
                        runId: manifest.RunId,
                        expectedRevision: null,
                        actualRevision: currentRevision);
                }
                if (currentRevision is not null
                    && expectedRevision is not null
                    && !ReportingRunStoreRevision.Matches(
                        currentRevision,
                        expectedRevision))
                {
                    throw ReportingRunConcurrencyException.ForConflict(
                        tenantId: null,
                        runId: manifest.RunId,
                        expectedRevision: expectedRevision,
                        actualRevision: currentRevision);
                }

                _snapshot = new ReportingRunSnapshot(
                    manifest,
                    auditTrail.ToArray(),
                    FixedNow);
            }
        }

        public void BlockNextWrite()
        {
            _blockedWriteEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _blockedWriteRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _blockNextWrite, 1);
        }

        public Task WaitForBlockedWriteAsync() =>
            _blockedWriteEntered!.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void ReleaseBlockedWrite() =>
            _blockedWriteRelease!.TrySetResult(true);

        public void FailNextWrite() =>
            Volatile.Write(ref _failNextWrite, 1);
    }

    private sealed class SharedAtomicScheduleStore(
        IReadOnlyList<ReportingScheduleRecordDto> schedules)
        : IReportingScheduleStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ReportingScheduleRecordDto> _schedules =
            schedules.ToDictionary(BuildKey, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReportingScheduleExecutionLease> _leases =
            new(StringComparer.OrdinalIgnoreCase);

        public int AtomicUpsertCalls { get; private set; }

        public int LegacySaveCalls { get; private set; }

        public IReadOnlyList<ReportingScheduleRecordDto> Load()
        {
            lock (_gate)
            {
                return _schedules.Values.ToArray();
            }
        }

        public void Save(IReadOnlyList<ReportingScheduleRecordDto> updated)
        {
            lock (_gate)
            {
                LegacySaveCalls++;
                _schedules.Clear();
                foreach (var schedule in updated)
                {
                    _schedules[BuildKey(schedule)] = schedule;
                }
            }
        }

        public void Upsert(ReportingScheduleRecordDto schedule) =>
            Upsert(schedule, expectedUpdatedAtUtc: null);

        public void Upsert(
            ReportingScheduleRecordDto schedule,
            DateTimeOffset? expectedUpdatedAtUtc)
        {
            lock (_gate)
            {
                _schedules.TryGetValue(BuildKey(schedule), out var current);
                if (current is null && expectedUpdatedAtUtc is not null)
                {
                    throw ReportingScheduleConcurrencyException.ForMissing(
                        schedule,
                        expectedUpdatedAtUtc.Value);
                }
                if (current is not null
                    && expectedUpdatedAtUtc is null
                    && current != schedule)
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(
                        current,
                        expectedUpdatedAtUtc: null);
                }
                if (current is not null
                    && expectedUpdatedAtUtc is not null
                    && current.UpdatedAtUtc != expectedUpdatedAtUtc.Value)
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(
                        current,
                        expectedUpdatedAtUtc.Value);
                }

                AtomicUpsertCalls++;
                _schedules[BuildKey(schedule)] = schedule;
            }
        }

        public ReportingScheduleExecutionLease? TryClaimExecution(
            ReportingScheduleRecordDto schedule,
            string leaseOwner,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration)
        {
            lock (_gate)
            {
                var key = BuildKey(schedule);
                if (!_schedules.TryGetValue(key, out var current)
                    || current.UpdatedAtUtc != schedule.UpdatedAtUtc)
                {
                    return null;
                }

                if (_leases.TryGetValue(key, out var retained)
                    && retained.LeaseExpiresAtUtc > nowUtc)
                {
                    return null;
                }

                var lease = new ReportingScheduleExecutionLease(
                    leaseOwner,
                    nowUtc.Add(leaseDuration),
                    retained is null ? 1 : retained.LeaseVersion + 1);
                _leases[key] = lease;
                return lease;
            }
        }

        public ReportingScheduleExecutionLease? RenewExecutionLease(
            ReportingScheduleRecordDto schedule,
            ReportingScheduleExecutionLease lease,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration)
        {
            lock (_gate)
            {
                var key = BuildKey(schedule);
                if (!_leases.TryGetValue(key, out var retained)
                    || retained.LeaseOwner != lease.LeaseOwner
                    || retained.LeaseVersion != lease.LeaseVersion
                    || retained.LeaseExpiresAtUtc <= nowUtc)
                {
                    return null;
                }

                retained = retained with
                {
                    LeaseExpiresAtUtc = nowUtc.Add(leaseDuration)
                };
                _leases[key] = retained;
                return retained;
            }
        }

        public void ReleaseExecutionLease(
            string tenantId,
            string companyId,
            string scheduleId,
            ReportingScheduleExecutionLease lease)
        {
            lock (_gate)
            {
                var key = $"{tenantId.Trim()}/{companyId.Trim()}/{scheduleId.Trim()}";
                if (_leases.TryGetValue(key, out var retained)
                    && retained.LeaseOwner == lease.LeaseOwner
                    && retained.LeaseVersion == lease.LeaseVersion)
                {
                    _leases.Remove(key);
                }
            }
        }

        public void UpsertClaimedExecution(
            ReportingScheduleRecordDto schedule,
            DateTimeOffset expectedUpdatedAtUtc,
            ReportingScheduleExecutionLease lease)
        {
            lock (_gate)
            {
                var key = BuildKey(schedule);
                if (!_leases.TryGetValue(key, out var retained)
                    || retained.LeaseOwner != lease.LeaseOwner
                    || retained.LeaseVersion != lease.LeaseVersion)
                {
                    throw new ReportingScheduleExecutionLeaseException(
                        schedule.TenantId ?? string.Empty,
                        schedule.CompanyId ?? string.Empty,
                        schedule.ScheduleId,
                        "The test reporting schedule execution lease was superseded.");
                }

                Upsert(schedule, expectedUpdatedAtUtc);
            }
        }

        private static string BuildKey(ReportingScheduleRecordDto schedule) =>
            $"{schedule.TenantId?.Trim()}/{schedule.CompanyId?.Trim()}/{schedule.ScheduleId.Trim()}";
    }

    private sealed class ControllableReadyEvaluator : IReportingRunReadinessDependencyEvaluator
    {
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
            ReportingRunRequestDto request,
            ReportingTemplateMetadata template,
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext? accessContext,
            CancellationToken ct = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(ct);
            return
            [
                new ReportingRunReadinessCheckDto(
                    "controlled-ready",
                    "Controlled dependency readiness",
                    ReportingRunReadinessStatusDto.Ready,
                    "The dependency evaluator was released by the concurrency test.",
                    0,
                    BlocksDraft: false,
                    BlocksFinal: false)
            ];
        }
    }

    private sealed class AlwaysReadyEvaluator : IReportingRunReadinessDependencyEvaluator
    {
        public Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
            ReportingRunRequestDto request,
            ReportingTemplateMetadata template,
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext? accessContext,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportingRunReadinessCheckDto>>(
            [
                new ReportingRunReadinessCheckDto(
                    "multi-host-ready",
                    "Multi-host dependency readiness",
                    ReportingRunReadinessStatusDto.Ready,
                    "All deterministic test dependencies are ready.",
                    0,
                    BlocksDraft: false,
                    BlocksFinal: false)
            ]);
    }

    private sealed class FixedDeploymentReadinessService(bool isReady)
        : IReportingDeploymentReadinessService
    {
        public ReportingDeploymentCapabilityDto Evaluate() =>
            new(
                IsReady: isReady,
                Components: [],
                BlockingReasons: isReady ? [] : ["Reporting schema is incomplete."]);
    }

    private sealed class CountingRenderer : IReportingSectionRenderer
    {
        private readonly DeterministicReportingSectionRenderer _inner = new();
        private int _renderCount;

        public int RenderCount => Volatile.Read(ref _renderCount);

        public ReportingSectionManifest RenderSection(
            string runId,
            ReportingJobContract contract,
            ReportingTemplateMetadata template,
            string sectionId,
            int attempt)
        {
            Interlocked.Increment(ref _renderCount);
            return _inner.RenderSection(
                runId,
                contract,
                template,
                sectionId,
                attempt);
        }
    }

    private sealed class BlockingRenderer : IReportingSectionRenderer
    {
        private readonly DeterministicReportingSectionRenderer _inner = new();
        private int _entered;

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReportingSectionManifest RenderSection(
            string runId,
            ReportingJobContract contract,
            ReportingTemplateMetadata template,
            string sectionId,
            int attempt)
        {
            if (Interlocked.Exchange(ref _entered, 1) == 0)
            {
                Entered.TrySetResult(true);
                Release.Task.GetAwaiter().GetResult();
            }

            return _inner.RenderSection(
                runId,
                contract,
                template,
                sectionId,
                attempt);
        }
    }
}
