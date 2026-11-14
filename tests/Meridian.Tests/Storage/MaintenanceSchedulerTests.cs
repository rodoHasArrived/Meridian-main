using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Storage;
using Meridian.Storage.Operations;
using Meridian.Storage.Services;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed class MaintenanceSchedulerTests : IDisposable
{
    private readonly string _defaultHistoryRoot = Path.Combine(
        Path.GetTempPath(),
        "Meridian.Tests",
        "MaintenanceScheduler",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_defaultHistoryRoot))
            Directory.Delete(_defaultHistoryRoot, recursive: true);
    }

    [Theory]
    [InlineData("2026-07-13T21:00:00Z", false)] // 17:00 EDT
    [InlineData("2026-07-14T01:00:00Z", true)]  // Monday 21:00 EDT
    [InlineData("2026-11-10T02:00:00Z", true)]  // Monday 21:00 EST
    public async Task CanRunNowAsync_UsesConfiguredTimeZone(string utcNow, bool expectedAllowed)
    {
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Monday),
            clock: new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        var decision = await scheduler.CanRunNowAsync(MaintenanceType.Backup, new ResourceRequirements());

        decision.Allowed.Should().Be(expectedAllowed);
    }

    [Theory]
    [InlineData("2026-11-14T06:00:00Z", true)]  // Saturday 01:00 belongs to Friday
    [InlineData("2026-11-13T06:00:00Z", false)] // Friday 01:00 belongs to Thursday
    [InlineData("2026-11-14T08:00:00Z", false)] // End is exclusive
    public async Task CanRunNowAsync_OvernightWindowUsesItsStartingWeekday(string utcNow, bool expectedAllowed)
    {
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Friday),
            clock: new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        var decision = await scheduler.CanRunNowAsync(MaintenanceType.Backup, new ResourceRequirements());

        decision.Allowed.Should().Be(expectedAllowed);
    }

    [Fact]
    public async Task FindNextWindowAsync_AfterMidnightRejectsJobsLongerThanRemainingWindow()
    {
        var now = DateTimeOffset.Parse("2026-11-10T06:00:00Z"); // Tuesday 01:00 EST
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Monday), clock: new FixedTimeProvider(now));

        var fitting = await scheduler.FindNextWindowAsync(
            MaintenanceType.Backup, TimeSpan.FromHours(2), new ResourceRequirements());
        var tooLong = await scheduler.FindNextWindowAsync(
            MaintenanceType.Backup, TimeSpan.FromHours(3), new ResourceRequirements());

        fitting!.Start.Should().Be(now);
        fitting.End.Should().Be(DateTimeOffset.Parse("2026-11-10T08:00:00Z"));
        tooLong!.Start.Should().Be(DateTimeOffset.Parse("2026-11-17T02:00:00Z"));
    }

    [Fact]
    public async Task GetStateAsync_ReportsActualNextWindowDate()
    {
        var now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Monday), clock: new FixedTimeProvider(now));

        var state = await scheduler.GetStateAsync();
        var decision = await scheduler.CanRunNowAsync(MaintenanceType.Backup, new ResourceRequirements());

        state.NextMaintenanceWindowStart.Should().Be(DateTimeOffset.Parse("2026-11-17T02:00:00Z"));
        decision.WaitTime.Should().Be(state.NextMaintenanceWindowStart - now);
    }

    [Theory]
    [InlineData("2026-03-08T02:00:00Z", 5)] // Saturday 21:00 EST through Sunday 03:00 EDT
    [InlineData("2026-11-01T01:00:00Z", 7)] // Saturday 21:00 EDT through Sunday 03:00 EST
    public async Task FindNextWindowAsync_UsesBoundaryOffsetsAcrossDaylightSavingChanges(string utcNow, int elapsedHours)
    {
        var now = DateTimeOffset.Parse(utcNow);
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Saturday), clock: new FixedTimeProvider(now));

        var slot = await scheduler.FindNextWindowAsync(
            MaintenanceType.Backup, TimeSpan.Zero, new ResourceRequirements());

        (slot!.End - slot.Start).Should().Be(TimeSpan.FromHours(elapsedHours));
    }

    [Theory]
    [InlineData("2026-11-01T05:45:00Z", false)] // First 01:45 is before the resolved 01:30 EST start
    [InlineData("2026-11-01T06:45:00Z", true)]
    public async Task CanRunNowAsync_RepeatedHourUsesSameBoundariesAsFutureSlots(string utcNow, bool allowed)
    {
        var config = OvernightConfig(DayOfWeek.Sunday) with
        {
            MaintenanceWindows = [new MaintenanceWindow(
                "repeated-hour", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(150),
                [DayOfWeek.Sunday], [], Limits: new ResourceLimits())]
        };
        await using var scheduler = CreateScheduler(
            config: config, clock: new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        var decision = await scheduler.CanRunNowAsync(MaintenanceType.Backup, new ResourceRequirements());
        var slot = await scheduler.FindNextWindowAsync(MaintenanceType.Backup, TimeSpan.Zero, new ResourceRequirements());

        decision.Allowed.Should().Be(allowed);
        slot!.Start.Should().Be(DateTimeOffset.Parse(allowed ? "2026-11-01T06:45:00Z" : "2026-11-01T06:30:00Z"));
        slot.End.Should().Be(DateTimeOffset.Parse("2026-11-01T07:30:00Z"));
    }

    [Fact]
    public async Task Scheduler_FutureJobDoesNotBlockDueJobBehindIt()
    {
        var now = DateTimeOffset.Parse("2026-11-10T06:00:00Z");
        await using var scheduler = CreateScheduler(
            config: OvernightConfig(DayOfWeek.Monday), clock: new FixedTimeProvider(now));
        var future = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.FromHours(3) };
        var due = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.Zero };
        await scheduler.ScheduleAsync(future, new ScheduleOptions());
        await scheduler.ScheduleAsync(due, new ScheduleOptions());

        scheduler.Start();
        await WaitUntilAsync(() => scheduler.GetJobStatus(due.Id) is not null, "the due job must be processed");

        scheduler.GetPendingJobs().Should().ContainSingle(job => job.Id == future.Id);
        scheduler.GetJobStatus(future.Id).Should().BeNull();
    }

    [Fact]
    public async Task Scheduler_DelayedJobMustStillFitInRemainingWindow()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-11-10T02:00:00Z"));
        await using var scheduler = CreateScheduler(config: OvernightConfig(DayOfWeek.Monday), clock: clock);
        var delayed = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.FromHours(2) };
        await scheduler.ScheduleAsync(delayed, new ScheduleOptions());
        clock.Now = DateTimeOffset.Parse("2026-11-10T07:00:00Z"); // Tuesday 02:00 EST

        // Start processes the queue synchronously until its polling delay when no
        // admission is attempted; the delayed job must remain queued for a later window.
        scheduler.Start();

        scheduler.GetPendingJobs().Should().ContainSingle(job => job.Id == delayed.Id);
        scheduler.GetRunningJobs().Should().BeEmpty();
        scheduler.GetJobStatus(delayed.Id).Should().BeNull();
    }

    [Fact]
    public async Task Scheduler_ReservesConcurrencyBeforeRunningHistoryWriteCompletes()
    {
        var history = new DelayedRunningHistoryStore(_defaultHistoryRoot);
        await using var scheduler = CreateScheduler(
            history: history,
            config: AlwaysOpenConfig() with
            {
                MaintenanceWindows = [AlwaysOpenConfig().MaintenanceWindows[0] with { MaxConcurrentJobs = 1 }]
            });
        var first = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.Zero };
        var second = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.Zero };
        await scheduler.ScheduleAsync(first, new ScheduleOptions());
        await scheduler.ScheduleAsync(second, new ScheduleOptions());

        try
        {
            scheduler.Start();
            await WaitUntilAsync(() => history.RunningAppendAttempts > 0, "the first job must reach admission");

            history.RunningAppendAttempts.Should().Be(1);
            scheduler.GetRunningJobs().Should().ContainSingle(job => job.Id == first.Id);
            scheduler.GetPendingJobs().Should().ContainSingle(job => job.Id == second.Id);
        }
        finally
        {
            history.Release.TrySetResult();
        }
    }

    [Fact]
    public void ComputeInputHash_IsCollisionSafeAndIncludesMaterialSettings()
    {
        var baseline = CreateJob(MaintenanceType.IndexRebuild, ["a,b", "c"]) with
        {
            Id = "hash-job",
            Parameters = new Dictionary<string, object>
            {
                ["batchSize"] = 50,
                ["labels"] = new[] { "north", "east" }
            }
        };
        var delimiterCollision = baseline with { TargetPaths = ["a", "b,c"] };
        var requirementChanged = baseline with
        {
            Requirements = baseline.Requirements with { RequiresExclusiveLock = true }
        };
        var parameterChanged = baseline with
        {
            Parameters = new Dictionary<string, object>
            {
                ["labels"] = new[] { "north", "east" },
                ["batchSize"] = 51
            }
        };
        var reorderedEquivalent = baseline with
        {
            Parameters = new Dictionary<string, object>
            {
                ["labels"] = new[] { "north", "east" },
                ["batchSize"] = 50
            }
        };

        MaintenanceScheduler.ComputeInputHash(baseline).Should().NotBe(
            MaintenanceScheduler.ComputeInputHash(delimiterCollision));
        MaintenanceScheduler.ComputeInputHash(baseline).Should().NotBe(
            MaintenanceScheduler.ComputeInputHash(requirementChanged));
        MaintenanceScheduler.ComputeInputHash(baseline).Should().NotBe(
            MaintenanceScheduler.ComputeInputHash(parameterChanged));
        MaintenanceScheduler.ComputeInputHash(baseline).Should().Be(
            MaintenanceScheduler.ComputeInputHash(reorderedEquivalent));
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuild_InvokesSearchServiceAndReturnsVerifiedSuccess()
    {
        var search = Substitute.For<IStorageSearchService>();
        search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(VerifiedIndexRebuild()));
        await using var scheduler = CreateScheduler(search);
        var job = CreateJob(MaintenanceType.IndexRebuild);

        var status = await scheduler.ExecuteJobAsync(job);

        await search.Received(1).RebuildIndexAsync(
            Arg.Is<string[]>(paths => paths.SequenceEqual(job.TargetPaths)),
            Arg.Any<RebuildOptions>(),
            Arg.Any<CancellationToken>());
        status.Status.Should().Be(JobStatus.Completed);
        status.Outcome.Should().NotBeNull();
        status.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        status.Outcome.Postconditions.Should().Contain(postcondition =>
            postcondition.Code == "index-rebuild-all-inputs-indexed" &&
            postcondition.State == OperationPostconditionState.Satisfied);
        status.Outcome.Postconditions.Should().Contain(postcondition =>
            postcondition.Code == "index-rebuild-readback-verified" &&
            postcondition.State == OperationPostconditionState.Satisfied);
        var evidenceKinds = status.Outcome.Evidence.Select(item => item.Kind).ToArray();
        evidenceKinds.Should().Contain("index-snapshot-before");
        evidenceKinds.Should().Contain("index-snapshot-after");
        evidenceKinds.Should().Contain("index-snapshot-readback");
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
        scheduler.GetJobStatus(job.Id).Should().Be(status);
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuildWithoutVerificationReceipt_ReturnsBlocked()
    {
        var search = Substitute.For<IStorageSearchService>();
        search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IndexRebuildVerification>(null!));
        await using var scheduler = CreateScheduler(search);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild));

        status.Status.Should().Be(JobStatus.Blocked);
        status.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        status.Message.Should().Contain("no verification receipt");
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuildWithMismatchedReadback_ReturnsFailedWithProof()
    {
        var search = Substitute.For<IStorageSearchService>();
        search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(MismatchedIndexRebuild()));
        await using var scheduler = CreateScheduler(search);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild));

        status.Status.Should().Be(JobStatus.Failed);
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        status.Outcome.Postconditions.Should().ContainSingle(postcondition =>
            postcondition.Code == "index-rebuild-readback-verified" &&
            postcondition.State == OperationPostconditionState.NotSatisfied);
        status.Outcome.Evidence.Should().Contain(item => item.Kind == "index-snapshot-readback");
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_WithoutCaseHistoryStore_ReturnsBlockedWithoutExecuting()
    {
        var search = Substitute.For<IStorageSearchService>();
        await using var scheduler = new MaintenanceScheduler(
            new OperationalScheduleConfig(),
            Substitute.For<IFileMaintenanceService>(),
            Substitute.For<ITierMigrationService>(),
            Substitute.For<IDataQualityService>(),
            search,
            caseHistoryStore: null);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild));

        status.Status.Should().Be(JobStatus.Blocked);
        status.AdmissionRetained.Should().BeFalse();
        status.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        status.Outcome.Recovery.Should().ContainSingle(action => action.Guidance.Contains("IOperationalCaseHistoryStore"));
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
        await search.DidNotReceiveWithAnyArgs().RebuildIndexAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuildWithoutSearchService_ReturnsBlocked()
    {
        await using var scheduler = CreateScheduler();

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild));

        status.Status.Should().Be(JobStatus.Blocked);
        status.Progress.Should().Be(0);
        status.Outcome.Should().NotBeNull();
        status.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        status.Outcome.Recovery.Should().ContainSingle();
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuildWithoutExplicitTarget_ReturnsBlockedWithoutCallingSearch()
    {
        var search = Substitute.For<IStorageSearchService>();
        await using var scheduler = CreateScheduler(search);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild, []));

        status.Status.Should().Be(JobStatus.Blocked);
        status.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        await search.DidNotReceiveWithAnyArgs().RebuildIndexAsync(default!, default!, default);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_UnsupportedOperation_ReturnsBlockedInsteadOfClaimingCompletion()
    {
        await using var scheduler = CreateScheduler();

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.Backup));

        status.Status.Should().Be(JobStatus.Blocked);
        status.Message.Should().Contain("no registered executor");
        status.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_IndexRebuildException_ReturnsFailedWithRecovery()
    {
        var search = Substitute.For<IStorageSearchService>();
        search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IndexRebuildVerification>(new IOException("index unavailable")));
        await using var scheduler = CreateScheduler(search);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.IndexRebuild));

        status.Status.Should().Be(JobStatus.Failed);
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        status.Outcome.Issues.Should().ContainSingle(issue => issue.ExceptionType == typeof(IOException).FullName);
        status.Outcome.Recovery.Should().ContainSingle();
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_WhenExecutionIsCancelledAfterAdmission_PersistsTerminalFailureReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "MaintenanceCancellation", Guid.NewGuid().ToString("N"));
        try
        {
            using var cts = new CancellationTokenSource();
            var search = Substitute.For<IStorageSearchService>();
            search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cts.Cancel();
                    return Task.FromCanceled<IndexRebuildVerification>(cts.Token);
                });
            var historyStore = new FileOperationalCaseHistoryStore(root);
            await using var scheduler = CreateScheduler(search, historyStore);
            var job = CreateJob(MaintenanceType.IndexRebuild);
            await scheduler.ScheduleAsync(job, new ScheduleOptions());

            var status = await scheduler.ExecuteJobAsync(job, cts.Token);

            status.Status.Should().Be(JobStatus.Failed);
            status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
            status.Outcome.Issues.Should().ContainSingle(issue => issue.ExceptionType == typeof(TaskCanceledException).FullName);
            scheduler.GetRunningJobs().Should().BeEmpty();
            scheduler.GetJobStatus(job.Id).Should().Be(status);
            var history = await historyStore.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = job.Id,
                CaseType = "maintenance-job"
            });
            history.Select(record => record.EventType).Should().Equal(
                "maintenance.scheduled",
                "maintenance.running",
                "maintenance.terminal.failed");
            history[^1].TerminalOutcome.Should().BeEquivalentTo(status.Outcome);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteJobAsync_QualityScoringPartialFailure_ReturnsCompletedWithWarnings()
    {
        var quality = Substitute.For<IDataQualityService>();
        quality.GenerateReportAsync(Arg.Any<QualityReportOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QualityReport(attempted: 2, succeeded: 1, failed: 1)));
        await using var scheduler = CreateScheduler(quality: quality);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.QualityScoring));

        status.Status.Should().Be(JobStatus.Completed);
        status.Outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        status.Outcome.Recovery.Should().ContainSingle();
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_QualityScoringTotalFailure_ReturnsFailed()
    {
        var quality = Substitute.For<IDataQualityService>();
        quality.GenerateReportAsync(Arg.Any<QualityReportOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QualityReport(attempted: 1, succeeded: 0, failed: 1)));
        await using var scheduler = CreateScheduler(quality: quality);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.QualityScoring));

        status.Status.Should().Be(JobStatus.Failed);
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        status.Progress.Should().Be(0);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_QualityScoringCancellationAfterAdmission_ReturnsFailedReceipt()
    {
        using var cts = new CancellationTokenSource();
        var quality = Substitute.For<IDataQualityService>();
        quality.GenerateReportAsync(Arg.Any<QualityReportOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromCanceled<DataQualityReport>(cts.Token);
            });
        await using var scheduler = CreateScheduler(quality: quality);
        var job = CreateJob(MaintenanceType.QualityScoring);

        var status = await scheduler.ExecuteJobAsync(job, cts.Token);

        status.Status.Should().Be(JobStatus.Failed);
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        scheduler.GetJobStatus(job.Id).Should().Be(status);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_CompactionWithMergeOrDeletionErrors_ReturnsFailed()
    {
        var files = Substitute.For<IFileMaintenanceService>();
        files.DefragmentAsync(Arg.Any<DefragOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DefragResult(
                FilesProcessed: 2,
                FilesCreated: 1,
                BytesBefore: 20,
                BytesAfter: 30,
                CompressionImprovement: -50,
                Duration: TimeSpan.FromSeconds(1),
                MergeGroupsAttempted: 1,
                MergeGroupsSucceeded: 0,
                FilesDeleted: 1,
                Errors: ["source deletion failed"])));
        await using var scheduler = CreateScheduler(files: files);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.Compaction));

        status.Status.Should().Be(JobStatus.Failed);
        status.Message.Should().Contain("source deletion failed");
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJobAsync_TierMigrationWithUnsuccessfulResult_ReturnsFailed()
    {
        var migration = Substitute.For<ITierMigrationService>();
        migration.PlanMigrationAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MigrationPlan(
                DateTimeOffset.UtcNow,
                TimeSpan.FromDays(1),
                [new PlannedMigrationAction("data/input.jsonl", StorageTier.Cold, "age", 10, TimeSpan.FromDays(30), 1)],
                10,
                TimeSpan.FromSeconds(1))));
        migration.MigrateAsync(
                Arg.Any<string>(),
                Arg.Any<StorageTier>(),
                Arg.Any<MigrationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MigrationResult(
                Success: false,
                FilesProcessed: 0,
                FilesFailed: 1,
                BytesProcessed: 0,
                BytesSaved: 0,
                Duration: TimeSpan.FromSeconds(1),
                Errors: ["checksum verification failed"])));
        await using var scheduler = CreateScheduler(migration: migration);

        var status = await scheduler.ExecuteJobAsync(CreateJob(MaintenanceType.TierMigration));

        status.Status.Should().Be(JobStatus.Failed);
        status.Message.Should().Contain("checksum verification failed");
        status.Outcome!.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(status.Outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task ScheduleAsync_ReturnsIdentifierUsedByExecutionHistory()
    {
        await using var scheduler = CreateScheduler();
        var job = CreateJob(MaintenanceType.Backup);

        var scheduled = await scheduler.ScheduleAsync(job, new ScheduleOptions());

        scheduled.Id.Should().Be(job.Id);
        scheduled.Job.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task ScheduledAndTerminalHistory_SurvivesNewSchedulerAndStoreInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "MaintenanceHistory", Guid.NewGuid().ToString("N"));
        try
        {
            var search = Substitute.For<IStorageSearchService>();
            search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(VerifiedIndexRebuild()));
            var job = CreateJob(MaintenanceType.IndexRebuild);
            await using (var first = CreateScheduler(search, new FileOperationalCaseHistoryStore(root)))
            {
                await first.ScheduleAsync(job, new ScheduleOptions());
                var terminal = await first.ExecuteJobAsync(job);
                terminal.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
            }

            var restartedStore = new FileOperationalCaseHistoryStore(root);
            await using var restarted = CreateScheduler(search, restartedStore);
            var restoredState = await restarted.GetStateAsync();
            var restoredStatus = restarted.GetJobStatus(job.Id);
            var history = await restartedStore.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = job.Id,
                CaseType = "maintenance-job"
            });

            restoredState.PendingJobs.Should().BeEmpty();
            restoredStatus.Should().NotBeNull();
            restoredStatus!.Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
            history.Select(record => record.EventType).Should().Equal(
                "maintenance.scheduled",
                "maintenance.running",
                "maintenance.terminal.succeeded");
            history[^1].TerminalOutcome.Should().NotBeNull();
            VerifiedOperationOutcomeValidator.Validate(history[^1].TerminalOutcome!).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScheduledJob_WhenRunningAdmissionCannotBeRetained_IsObservedAndRequeued()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "MaintenanceAdmission", Guid.NewGuid().ToString("N"));
        try
        {
            var history = new FailRunningHistoryStore(root);
            await using var scheduler = CreateScheduler(history: history, config: AlwaysOpenConfig());
            // No work can start when admission persistence fails. A zero-duration estimate
            // keeps this admission test due immediately even in the final minutes of the day.
            var job = CreateJob(MaintenanceType.Backup) with { EstimatedDuration = TimeSpan.Zero };
            var scheduled = await scheduler.ScheduleAsync(job, new ScheduleOptions());
            scheduled.ScheduledStart.Should().BeOnOrBefore(DateTimeOffset.UtcNow);

            scheduler.Start();

            // ScheduleAsync already placed the job in the pending queue, so waiting for pending
            // membership exits on the first iteration with the scheduler having done nothing yet.
            // Wait for the admission attempt itself, which is the behaviour under test.
            await WaitUntilAsync(
                () => history.RunningAppendAttempts > 0,
                "the scheduler must attempt to retain the running transition before admitting a job");

            // The requeue happens after that append fails, so let the queue settle rather than
            // sampling it mid-transition.
            await WaitUntilAsync(
                () => scheduler.GetPendingJobs().Any(pending => pending.Id == job.Id)
                    && scheduler.GetRunningJobs().Count == 0,
                "a job whose running transition cannot be retained must be requeued, not left running");

            scheduler.GetPendingJobs().Should().ContainSingle(pending => pending.Id == job.Id);
            scheduler.GetRunningJobs().Should().BeEmpty();
            scheduler.GetJobStatus(job.Id).Should().BeNull();
            var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = job.Id,
                CaseType = "maintenance-job"
            });
            retained.Select(record => record.EventType).Should().Equal("maintenance.scheduled");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteJobAsync_ConcurrentStaleAdmission_IsBlockedBeforeDuplicateExternalWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "MaintenanceCas", Guid.NewGuid().ToString("N"));
        try
        {
            var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var search = Substitute.For<IStorageSearchService>();
            search.RebuildIndexAsync(Arg.Any<string[]>(), Arg.Any<RebuildOptions>(), Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    executionStarted.TrySetResult();
                    await releaseExecution.Task;
                    return VerifiedIndexRebuild();
                });
            await using var first = CreateScheduler(search, new FileOperationalCaseHistoryStore(root));
            await using var stale = CreateScheduler(search, new FileOperationalCaseHistoryStore(root));
            var job = CreateJob(MaintenanceType.IndexRebuild);

            var firstExecution = first.ExecuteJobAsync(job);
            await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var staleStatus = await stale.ExecuteJobAsync(job);

            staleStatus.Status.Should().Be(JobStatus.Blocked);
            staleStatus.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
            await search.Received(1).RebuildIndexAsync(
                Arg.Any<string[]>(),
                Arg.Any<RebuildOptions>(),
                Arg.Any<CancellationToken>());
            releaseExecution.TrySetResult();
            (await firstExecution).Outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private MaintenanceScheduler CreateScheduler(
        IStorageSearchService? search = null,
        IOperationalCaseHistoryStore? history = null,
        IDataQualityService? quality = null,
        OperationalScheduleConfig? config = null,
        IFileMaintenanceService? files = null,
        ITierMigrationService? migration = null,
        TimeProvider? clock = null) =>
        new(
            config ?? new OperationalScheduleConfig(),
            files ?? Substitute.For<IFileMaintenanceService>(),
            migration ?? Substitute.For<ITierMigrationService>(),
            quality ?? Substitute.For<IDataQualityService>(),
            search,
            history ?? new FileOperationalCaseHistoryStore(_defaultHistoryRoot),
            clock);

    private static OperationalScheduleConfig OvernightConfig(DayOfWeek startingDay) =>
        new(
            "Overnight",
            [],
            [new MaintenanceWindow(
                "overnight", TimeSpan.FromHours(21), TimeSpan.FromHours(3), [startingDay], [],
                Limits: new ResourceLimits())],
            [],
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DelayedRunningHistoryStore(string root) : IOperationalCaseHistoryStore
    {
        private readonly FileOperationalCaseHistoryStore _inner = new(root);
        private int _runningAppendAttempts;
        public int RunningAppendAttempts => Volatile.Read(ref _runningAppendAttempts);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.EventType == "maintenance.running")
            {
                Interlocked.Increment(ref _runningAppendAttempts);
                await Release.Task.WaitAsync(cancellationToken);
            }

            return await _inner.AppendAsync(request, cancellationToken);
        }

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(query, cancellationToken);
    }

    private static DataQualityReport QualityReport(int attempted, int succeeded, int failed) =>
        new(
            DateTimeOffset.UtcNow,
            succeeded,
            succeeded == 0 ? 0 : 0.9,
            new Dictionary<string, double>(),
            [],
            [])
        {
            FilesAttempted = attempted,
            FilesSucceeded = succeeded,
            FilesFailed = failed,
            Issues = failed == 0
                ? []
                : [new DataQualityReportIssue("input.jsonl", "input unavailable", typeof(IOException).FullName)]
        };

    private static IndexRebuildVerification VerifiedIndexRebuild()
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var before = new IndexSnapshot(1, new string('a', 64), capturedAtUtc);
        var after = new IndexSnapshot(2, new string('b', 64), capturedAtUtc);
        var readback = new IndexSnapshot(2, new string('b', 64), capturedAtUtc);
        return new IndexRebuildVerification(before, after, readback, DiscoveredFileCount: 2);
    }

    private static IndexRebuildVerification MismatchedIndexRebuild()
    {
        var verified = VerifiedIndexRebuild();
        return verified with
        {
            Readback = new IndexSnapshot(
                verified.Readback.IndexedFileCount,
                new string('c', 64),
                verified.Readback.CapturedAtUtc)
        };
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or the deadline passes, then asserts it so
    /// a genuine failure still reports through FluentAssertions rather than as a timeout.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }

    private static OperationalScheduleConfig AlwaysOpenConfig() =>
        new(
            "Always open",
            [],
            [new MaintenanceWindow(
                "all-day",
                TimeSpan.Zero,
                TimeSpan.FromDays(1),
                Enum.GetValues<DayOfWeek>(),
                [],
                MaxConcurrentJobs: 4,
                Limits: new ResourceLimits())],
            [],
            TimeZoneInfo.Utc);

    private static MaintenanceJob CreateJob(MaintenanceType type, string[]? targetPaths = null) =>
        new(
            $"job-{Guid.NewGuid():N}",
            type,
            JobPriority.Normal,
            $"Run {type}",
            new ResourceRequirements(),
            TimeSpan.FromMinutes(5),
            targetPaths ?? ["data"]);

    private sealed class FailRunningHistoryStore(string root) : IOperationalCaseHistoryStore
    {
        private readonly FileOperationalCaseHistoryStore _inner = new(root);
        private int _runningAppendAttempts;

        public int RunningAppendAttempts => Volatile.Read(ref _runningAppendAttempts);

        public ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.EventType == "maintenance.running")
            {
                Interlocked.Increment(ref _runningAppendAttempts);
                return ValueTask.FromException<OperationalCaseHistoryRecord>(
                    new IOException("running history unavailable"));
            }

            return _inner.AppendAsync(request, cancellationToken);
        }

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(query, cancellationToken);
    }
}
