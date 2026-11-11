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
    public void Scenario_ScheduleDelete_RemovesTheRetainedDefinitionAndRejectsAStaleHost()
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
        firstStore.Upsert(BuildCurrentSchedule("file-schedule-delete"));
        firstStore.Upsert(BuildCurrentSchedule("file-schedule-keep"));
        var firstHost = CreateScheduleService(firstStore);
        var staleHost = CreateScheduleService(staleStore);

        try
        {
            firstHost.SetState(
                "file-schedule-delete",
                ReportingScheduleStateDto.Paused,
                accessContext);

            var staleDelete = () => staleHost.Delete("file-schedule-delete", accessContext);
            staleDelete.Should().Throw<ReportingScheduleConcurrencyException>(
                "a host holding an older revision must not delete a schedule another host changed");
            firstStore.Load().Should().HaveCount(2);

            staleHost.Delete("file-schedule-delete", accessContext)
                .ScheduleId
                .Should()
                .Be("file-schedule-delete");

            firstStore.Load().Should().ContainSingle(schedule =>
                schedule.ScheduleId == "file-schedule-keep");
            staleHost.ListSchedules(accessContext).Should().ContainSingle(schedule =>
                schedule.ScheduleId == "file-schedule-keep");
            var deleteAgain = () => staleHost.Delete("file-schedule-delete", accessContext);
            deleteAgain.Should().Throw<KeyNotFoundException>();
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
    public void Scenario_ScheduleDelete_IsRefusedWhileAnotherHostHoldsTheExecutionLease()
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
        var runningHostStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(path),
            NullLogger<FileReportingScheduleStore>.Instance);
        var deletingHostStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(path),
            NullLogger<FileReportingScheduleStore>.Instance);
        runningHostStore.Upsert(BuildCurrentSchedule("leased-elsewhere"));
        var retained = runningHostStore.Load().Should().ContainSingle().Subject;
        var deletingHost = CreateScheduleService(deletingHostStore);

        try
        {
            // Another host is mid-run: it holds the durable lease, and this host has no
            // in-process record of that.
            var otherHostLease = runningHostStore.TryClaimExecution(
                retained,
                "other-host",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(2));
            otherHostLease.Should().NotBeNull();

            var delete = () => deletingHost.Delete("leased-elsewhere", accessContext);

            delete.Should().Throw<ReportingScheduleExecutionLeaseException>();
            runningHostStore.Load().Should().ContainSingle(
                "a schedule another host is executing must not be deleted underneath it");

            runningHostStore.ReleaseExecutionLease("tenant-a", "company-a", "leased-elsewhere", otherHostLease!);
            deletingHost.Delete("leased-elsewhere", accessContext);
            runningHostStore.Load().Should().BeEmpty();
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
    public void Scenario_ScheduleDeleteAndStateChange_AreRefusedForACallerTheAccessPolicyExcludes()
    {
        // Same tenant and company, and allowed to manage reporting, but not a principal of the
        // schedule's restricted access policy (which admits only operator-a).
        var store = new SharedAtomicScheduleStore([BuildCurrentSchedule("restricted-schedule")]);
        var service = CreateScheduleService(store);
        var outsider = new ReportAccessQueryContext(
            ActorPrincipalId: "operator-b",
            CompanyId: "company-a",
            TenantId: "tenant-a",
            RequireBoundScope: true);
        var owner = outsider with { ActorPrincipalId = "operator-a" };
        service.ListSchedules(outsider).Should().BeEmpty("the schedule is not visible to this caller");

        var delete = () => service.Delete("restricted-schedule", outsider);
        var disable = () => service.SetState("restricted-schedule", ReportingScheduleStateDto.Disabled, outsider);

        delete.Should().Throw<UnauthorizedAccessException>();
        disable.Should().Throw<UnauthorizedAccessException>();
        store.Load().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "restricted-schedule"
            && schedule.State != ReportingScheduleStateDto.Disabled);

        service.Delete("restricted-schedule", owner);
        store.Load().Should().BeEmpty();
    }

    [Fact]
    public void Scenario_ScheduleDelete_LegacyUnscopedScheduleIsRefusedAndRetained()
    {
        var store = new SharedAtomicScheduleStore([BuildSchedule("legacy-unscoped", "operator-a")]);
        var service = CreateScheduleService(store);

        var delete = () => service.Delete("legacy-unscoped", accessContext: null);

        delete.Should().Throw<InvalidDataException>().WithMessage("*disable it instead*");
        store.Load().Should().ContainSingle();
    }

    [Fact]
    public async Task Scenario_DisabledSchedule_IsSkippedByTheWorkerUntilResumed()
    {
        var due = BuildSchedule("disabled-then-resumed", "operator-a") with
        {
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = FixedNow
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        service.SetState(due.ScheduleId, ReportingScheduleStateDto.Disabled);
        var whileDisabled = await service.RunDueForWorkerAsync(FixedNow, CancellationToken.None);
        service.SetState(due.ScheduleId, ReportingScheduleStateDto.Active);
        var afterResume = await service.RunDueForWorkerAsync(FixedNow, CancellationToken.None);

        whileDisabled.Result.Runs.Should().BeEmpty();
        afterResume.Result.Runs.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ReportingSchedulePeriodFrequencyDto.Monthly, 2026, 2, 28, 2026, 3, 10)]
    [InlineData(ReportingSchedulePeriodFrequencyDto.Quarterly, 2026, 4, 30, 2026, 5, 10)]
    [InlineData(ReportingSchedulePeriodFrequencyDto.Annually, 2027, 1, 31, 2027, 2, 10)]
    public async Task Scenario_PeriodCloseSchedule_AdvancesToTheNextPeriodEndAndDueOffset(
        ReportingSchedulePeriodFrequencyDto frequency,
        int asOfYear, int asOfMonth, int asOfDay,
        int dueYear, int dueMonth, int dueDay)
    {
        // January 2026 period, due 10 days after period end at 08:00 UTC. A cron expression
        // cannot express "as of period end, due N days later".
        var firstDue = new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("period-close", "operator-a") with
        {
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 1, 31),
            DueAtUtc = firstDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(frequency, DueDaysAfterPeriodEnd: 10)
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        var result = await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        result.Result.Runs.Should().ContainSingle();
        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(asOfYear, asOfMonth, asOfDay));
        advanced.DueAtUtc.Should().Be(new DateTimeOffset(dueYear, dueMonth, dueDay, 8, 0, 0, TimeSpan.Zero));
        advanced.PeriodCadence.Should().Be(due.PeriodCadence);
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_TakesItsAccountingPeriodFromTheLedgerNotTheCalendar()
    {
        // An April-start fiscal year: the period ending 31 May 2026 is fiscal 2027 period 02, so
        // a selector built from the calendar date ("2026-05") would name the wrong period.
        var april = new ReportingSchedulePeriodMatch(
            "11111111-1111-1111-1111-111111111111", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var may = new ReportingSchedulePeriodMatch(
            "22222222-2222-2222-2222-222222222222", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var resolver = new RecordingPeriodResolver(date =>
            date >= april.StartDate && date <= april.EndDate ? april
            : date >= may.StartDate && date <= may.EndDate ? may
            : null);
        var firstDue = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("fiscal-period-close", "operator-a") with
        {
            RunParameters = BuildCurrentSchedule("parameters-only").RunParameters,
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 4, 30),
            DueAtUtc = firstDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 10)
        };
        due = due with { RunParameters = due.RunParameters! with { PeriodId = "stale-selector" } };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer(), resolver);

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(2026, 5, 31));
        advanced.RunParameters!.AsOfDate.Should().Be(new DateOnly(2026, 5, 31));
        advanced.RunParameters.PeriodId.Should().Be(
            may.Selector,
            "the next period selector is the one the ledger returned for the next period");
        resolver.Dates.Should().Contain(new DateOnly(2026, 4, 30), "the run itself re-resolves its period first");
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_ClosesOnTheLedgerPeriodEndForNonCalendarPeriods()
    {
        // A 4-4-5 style book: after the period ending 28 February comes a five-week period,
        // 1 March to 4 April. Calendar arithmetic would close it on 31 March, four days early.
        var fiveWeeks = new ReportingSchedulePeriodMatch(
            "33333333-3333-3333-3333-333333333333", new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 4));
        var resolver = new RecordingPeriodResolver(date =>
            date >= fiveWeeks.StartDate && date <= fiveWeeks.EndDate ? fiveWeeks : null);
        var firstDue = new DateTimeOffset(2026, 3, 5, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("four-four-five", "operator-a") with
        {
            RunParameters = BuildCurrentSchedule("parameters-only").RunParameters,
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 2, 28),
            DueAtUtc = firstDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5)
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer(), resolver);

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(
            new DateOnly(2026, 4, 4),
            "the close date is the durable period's own end, not the calendar month end");
        advanced.RunParameters!.AsOfDate.Should().Be(new DateOnly(2026, 4, 4));
        advanced.RunParameters.PeriodId.Should().Be(fiveWeeks.Selector);
        advanced.DueAtUtc.Should().Be(
            new DateTimeOffset(2026, 4, 9, 8, 0, 0, TimeSpan.Zero),
            "the due date is the configured five days after the real period end");
    }

    [Fact]
    public async Task Scenario_QuarterlyPeriodCloseSchedule_ClosesOnTheFiscalQuarterEndTheLedgerNames()
    {
        // 4-4-5 calendar: after the quarter ending 2 May 2026 the next fiscal quarter ends
        // 1 August. Three calendar months on would be 2 August, inside the following period.
        var quarterEnd = new ReportingSchedulePeriodMatch(
            "44444444-4444-4444-4444-444444444444", new DateOnly(2026, 6, 28), new DateOnly(2026, 8, 1));
        var resolver = new RecordingPeriodResolver(
            _ => null,
            (currentEnd, frequency) =>
                currentEnd == new DateOnly(2026, 5, 2) && frequency == ReportingSchedulePeriodFrequencyDto.Quarterly
                    ? quarterEnd
                    : null);
        var firstDue = new DateTimeOffset(2026, 5, 12, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("fiscal-quarter-close", "operator-a") with
        {
            RunParameters = BuildCurrentSchedule("parameters-only").RunParameters,
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 5, 2),
            DueAtUtc = firstDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Quarterly, 10)
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer(), resolver);

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(2026, 8, 1));
        advanced.RunParameters!.PeriodId.Should().Be(quarterEnd.Selector);
        advanced.DueAtUtc.Should().Be(new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_FallsBackToTheCalendarWhenTheLedgerCannotNameAPeriod()
    {
        var firstDue = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("unopened-period", "operator-a") with
        {
            RunParameters = BuildCurrentSchedule("parameters-only").RunParameters,
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 4, 30),
            DueAtUtc = firstDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 10)
        };
        var retainedSelector = due.RunParameters!.PeriodId;
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(
            store,
            new CountingRenderer(),
            new RecordingPeriodResolver(_ => null));

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(2026, 5, 31));
        advanced.RunParameters!.PeriodId.Should().Be(
            retainedSelector,
            "a selector is never invented from the date when the ledger has no unique period");
    }

    private static ReportingScheduleRecordDto BuildCalendarFallbackCloseSchedule(string scheduleId) =>
        // Closed 28 February; the ledger could not name the next period, so the schedule moved
        // on to the calendar month end and remembered where it was.
        BuildSchedule(scheduleId, "operator-a") with
        {
            RunParameters = BuildCurrentSchedule("parameters-only").RunParameters,
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 3, 31),
            DueAtUtc = new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero),
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5),
            UnresolvedPeriodCloseAfter = new DateOnly(2026, 2, 28)
        };

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_ThatFellBackToTheCalendarIsCorrectedAndWaitsForItsRealDueDate()
    {
        // The ledger lookup fails while the 28 February close advances, then recovers. The real
        // next period is 1 March to 4 April, so the 31 March calendar date must not be run.
        ReportingSchedulePeriodMatch? nextPeriod = null;
        var resolver = new RecordingPeriodResolver(
            _ => null,
            (currentEnd, _) => currentEnd == new DateOnly(2026, 2, 28) ? nextPeriod : null);
        var firstDue = new DateTimeOffset(2026, 3, 5, 8, 0, 0, TimeSpan.Zero);
        var due = BuildCalendarFallbackCloseSchedule("ledger-outage-close") with
        {
            NextAsOfDate = new DateOnly(2026, 2, 28),
            DueAtUtc = firstDue,
            UnresolvedPeriodCloseAfter = null
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer(), resolver);

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var fallback = store.Load().Should().ContainSingle().Subject;
        fallback.NextAsOfDate.Should().Be(new DateOnly(2026, 3, 31));
        fallback.UnresolvedPeriodCloseAfter.Should().Be(new DateOnly(2026, 2, 28));
        fallback.RunCount.Should().Be(1);

        nextPeriod = new ReportingSchedulePeriodMatch(
            "55555555-5555-5555-5555-555555555555", new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 4));
        var early = await service.RunDueForWorkerAsync(fallback.DueAtUtc, CancellationToken.None);

        early.Result.Runs.Should().BeEmpty("the period ends 4 April, so the close is not due on 5 April");
        var corrected = store.Load().Should().ContainSingle().Subject;
        corrected.NextAsOfDate.Should().Be(new DateOnly(2026, 4, 4));
        corrected.DueAtUtc.Should().Be(new DateTimeOffset(2026, 4, 9, 8, 0, 0, TimeSpan.Zero));
        corrected.RunParameters!.PeriodId.Should().Be(nextPeriod.Selector);
        corrected.UnresolvedPeriodCloseAfter.Should().BeNull();
        corrected.RunCount.Should().Be(1);

        var onTime = await service.RunDueForWorkerAsync(corrected.DueAtUtc, CancellationToken.None);

        onTime.Result.Runs.Should().ContainSingle();
        store.Load().Should().ContainSingle().Which.RunCount.Should().Be(2);
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_CorrectionDoesNotSkipAPeriodThatEndsBeforeTheCalendarDate()
    {
        // The real next period is 1 to 28 March. The calendar date 31 March sits in the period
        // after it, so the correction must come from the last closed date, not from 31 March.
        var march = new ReportingSchedulePeriodMatch(
            "66666666-6666-6666-6666-666666666666", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 28));
        var april = new ReportingSchedulePeriodMatch(
            "77777777-7777-7777-7777-777777777777", new DateOnly(2026, 3, 29), new DateOnly(2026, 4, 25));
        var resolver = new RecordingPeriodResolver(
            date => date <= march.EndDate ? march : april,
            (currentEnd, _) => currentEnd == new DateOnly(2026, 2, 28) ? march
                : currentEnd == march.EndDate ? april
                : null);
        var due = BuildCalendarFallbackCloseSchedule("short-period-close");
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer(), resolver);

        var result = await service.RunDueForWorkerAsync(due.DueAtUtc, CancellationToken.None);

        result.Result.Runs.Should().ContainSingle(
            "the corrected close (28 March, due 2 April) is already due");
        resolver.Dates.Should().Contain(march.EndDate, "the run closed 28 March");
        store.AtomicUpsertCalls.Should().Be(
            2,
            "the correction is stored under the lease before the run starts, then the advance; a "
            + "store that renews leases by retained payload would otherwise drop the lease mid-run");
        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(april.EndDate);
        advanced.RunParameters!.PeriodId.Should().Be(april.Selector);
        advanced.UnresolvedPeriodCloseAfter.Should().BeNull();
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_RunsOnTheCalendarDateWhileTheLedgerStillCannotNameThePeriod()
    {
        var due = BuildCalendarFallbackCloseSchedule("still-unresolved-close");
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(
            store,
            new CountingRenderer(),
            new RecordingPeriodResolver(_ => null));

        var result = await service.RunDueForWorkerAsync(due.DueAtUtc, CancellationToken.None);

        result.Result.Runs.Should().ContainSingle();
        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(2026, 4, 30));
        advanced.UnresolvedPeriodCloseAfter.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task Scenario_PeriodCloseSchedule_ReturnsToItsConfiguredTimeAfterADaylightSavingGap()
    {
        // Configured for 02:30 New York. The 8 March run was moved to 03:00 EDT (07:00Z) because
        // 02:30 does not exist that day; the next run must go back to 02:30, not stay at 03:00.
        var gapShiftedDue = new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("gap-recovery", "operator-a") with
        {
            CronExpression = "30 2 8 * *",
            TimeZoneId = "America/New_York",
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 2, 28),
            DueAtUtc = gapShiftedDue,
            PeriodCadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 8)
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        await service.RunDueForWorkerAsync(gapShiftedDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.NextAsOfDate.Should().Be(new DateOnly(2026, 3, 31));
        advanced.DueAtUtc.Should().Be(
            new DateTimeOffset(2026, 4, 8, 6, 30, 0, TimeSpan.Zero),
            "8 April 02:30 EDT is the configured time; the gap adjustment applied to March only");
    }

    [Fact]
    public void Scenario_ScheduleUpsert_PeriodCadenceIsValidatedAndReplacedOnEachUpsert()
    {
        var store = new SharedAtomicScheduleStore([]);
        var service = CreateScheduleService(store);
        ReportingScheduleUpsertRequestDto Request(ReportingSchedulePeriodCadenceDto? cadence) => new(
            "period-cadence-lifecycle",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            2,
            "operator-a",
            State: ReportingScheduleStateDto.Draft,
            PeriodCadence: cadence);

        var invalid = () => service.Upsert(Request(
            new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, -1)));
        invalid.Should().Throw<ArgumentException>().WithMessage("*period cadence*");
        store.Load().Should().BeEmpty();

        var monthly = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5);
        service.Upsert(Request(monthly)).PeriodCadence.Should().Be(monthly);
        service.Upsert(Request(null)).PeriodCadence.Should().BeNull();
    }

    [Fact]
    public void Scenario_ScheduleUpsert_KeepsThePendingLedgerCorrectionUnlessTheReportDateChanges()
    {
        var cadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5);
        var asOf = new DateOnly(2026, 8, 31);
        var store = new SharedAtomicScheduleStore([]);
        var service = CreateScheduleService(store);
        ReportingScheduleUpsertRequestDto Request(DateOnly nextAsOf) => new(
            "pending-correction",
            "investor-monthly-statement",
            "0 8 1 * *",
            nextAsOf,
            FixedNow.AddDays(1),
            2,
            "operator-a",
            State: ReportingScheduleStateDto.Draft,
            PeriodCadence: cadence);
        var created = service.Upsert(Request(asOf));
        store.Save([created with { UnresolvedPeriodCloseAfter = new DateOnly(2026, 7, 31) }]);
        var reloaded = CreateScheduleService(store);

        reloaded.Upsert(Request(asOf)).UnresolvedPeriodCloseAfter.Should().Be(
            new DateOnly(2026, 7, 31),
            "an edit that leaves the report date alone must not lose the pending correction");
        reloaded.Upsert(Request(asOf.AddDays(-3))).UnresolvedPeriodCloseAfter.Should().BeNull(
            "an operator who sets the report date has decided it");
    }

    [Fact]
    public async Task Scenario_ScheduleWithTimeZone_AdvancesInLocalTimeAcrossDaylightSaving()
    {
        // 08:00 New York is 13:00 UTC in winter and 12:00 UTC once daylight time starts
        // (8 March 2026), so a UTC-pinned schedule would drift by an hour.
        var winterDue = new DateTimeOffset(2026, 3, 7, 13, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("new-york-daily", "operator-a") with
        {
            CronExpression = "0 8 * * *",
            TimeZoneId = "America/New_York",
            State = ReportingScheduleStateDto.Active,
            DueAtUtc = winterDue
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        var result = await service.RunDueForWorkerAsync(
            winterDue,
            CancellationToken.None);

        result.Result.Runs.Should().ContainSingle();
        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.DueAtUtc.Should().Be(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero));
        advanced.DueAtUtc.Offset.Should().Be(TimeSpan.Zero);
        advanced.TimeZoneId.Should().Be("America/New_York");
    }

    [Theory]
    // Spring forward: 19:30 New York is 00:30Z the next day in winter and 23:30Z the same day
    // in summer, so two consecutive local days share one UTC date.
    [InlineData("2026-03-08T00:30:00Z", "2026-03-07", "2026-03-08T23:30:00Z", "2026-03-08")]
    // Fall back: two consecutive local days are two UTC dates apart.
    [InlineData("2026-10-31T23:30:00Z", "2026-10-31", "2026-11-02T00:30:00Z", "2026-11-01")]
    public async Task Scenario_ScheduleWithTimeZone_AdvancesTheAsOfDateByLocalCalendarDays(
        string dueAtUtc, string asOf, string expectedDueAtUtc, string expectedAsOf)
    {
        var firstDue = DateTimeOffset.Parse(dueAtUtc, System.Globalization.CultureInfo.InvariantCulture);
        var due = BuildSchedule("new-york-evening", "operator-a") with
        {
            CronExpression = "30 19 * * *",
            TimeZoneId = "America/New_York",
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = DateOnly.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture),
            DueAtUtc = firstDue
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        await service.RunDueForWorkerAsync(firstDue, CancellationToken.None);

        var advanced = store.Load().Should().ContainSingle().Subject;
        advanced.DueAtUtc.Should().Be(
            DateTimeOffset.Parse(expectedDueAtUtc, System.Globalization.CultureInfo.InvariantCulture));
        advanced.NextAsOfDate.Should().Be(
            DateOnly.Parse(expectedAsOf, System.Globalization.CultureInfo.InvariantCulture),
            "the as-of date moves by days on the schedule's local calendar, not by UTC dates");
    }

    [Fact]
    public async Task Scenario_LastDayOfMonthSchedule_AdvancesToTheNextMonthEnd()
    {
        var januaryEnd = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        var due = BuildSchedule("month-end", "operator-a") with
        {
            CronExpression = "0 8 L * *",
            State = ReportingScheduleStateDto.Active,
            NextAsOfDate = new DateOnly(2026, 1, 31),
            DueAtUtc = januaryEnd
        };
        var store = new SharedAtomicScheduleStore([due]);
        var service = CreateRunnableScheduleService(store, new CountingRenderer());

        await service.RunDueForWorkerAsync(januaryEnd, CancellationToken.None);

        store.Load().Should().ContainSingle(schedule =>
            schedule.DueAtUtc == new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero)
            && schedule.NextAsOfDate == new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Scenario_ScheduleUpsert_UnknownTimeZoneFailsBeforePersistence()
    {
        var store = new SharedAtomicScheduleStore([]);
        var service = CreateScheduleService(store);

        var upsert = () => service.Upsert(new ReportingScheduleUpsertRequestDto(
            "unknown-zone",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            2,
            "operator-a",
            State: ReportingScheduleStateDto.Draft,
            TimeZoneId: "Mars/Olympus_Mons"));

        upsert.Should().Throw<ArgumentException>()
            .WithMessage("*time zone*");
        store.Load().Should().BeEmpty();
    }

    [Fact]
    public void Scenario_ScheduleUpsert_TimeZoneIsRetainedUntilExplicitlyResetToUtc()
    {
        var store = new SharedAtomicScheduleStore([]);
        var service = CreateScheduleService(store);
        ReportingScheduleUpsertRequestDto Request(string? timeZoneId) => new(
            "zone-lifecycle",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 8, 31),
            FixedNow.AddDays(1),
            2,
            "operator-a",
            State: ReportingScheduleStateDto.Draft,
            TimeZoneId: timeZoneId);

        service.Upsert(Request("Europe/London")).TimeZoneId.Should().Be("Europe/London");
        service.Upsert(Request(null)).TimeZoneId.Should().Be("Europe/London");
        service.Upsert(Request("UTC")).TimeZoneId.Should().BeNull();
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

    private sealed class RecordingPeriodResolver(
        Func<DateOnly, ReportingSchedulePeriodMatch?> resolve,
        Func<DateOnly, ReportingSchedulePeriodFrequencyDto, ReportingSchedulePeriodMatch?>? resolveNextClose = null)
        : IReportingSchedulePeriodResolver
    {
        public List<DateOnly> Dates { get; } = [];

        public Task<ReportingSchedulePeriodMatch?> ResolvePeriodAsync(
            ReportingRunParametersDto parameters,
            DateOnly date,
            CancellationToken ct = default)
        {
            Dates.Add(date);
            return Task.FromResult(resolve(date));
        }

        public Task<ReportingSchedulePeriodMatch?> ResolveNextClosePeriodAsync(
            ReportingRunParametersDto parameters,
            DateOnly currentPeriodEnd,
            ReportingSchedulePeriodFrequencyDto frequency,
            CancellationToken ct = default) =>
            Task.FromResult(resolveNextClose is null
                ? resolve(currentPeriodEnd.AddDays(1))
                : resolveNextClose(currentPeriodEnd, frequency));
    }

    private static ReportingScheduleService CreateRunnableScheduleService(
        IReportingScheduleStore store,
        IReportingSectionRenderer renderer,
        IReportingSchedulePeriodResolver? periodResolver = null)
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
                    AllowLegacyUnscopedDrafts: true)))
        {
            PeriodResolver = periodResolver
        };
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
