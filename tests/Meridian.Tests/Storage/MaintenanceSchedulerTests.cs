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
            var job = CreateJob(MaintenanceType.Backup);
            await scheduler.ScheduleAsync(job, new ScheduleOptions());

            scheduler.Start();
            for (var attempt = 0;
                 attempt < 100 && scheduler.GetPendingJobs().All(pending => pending.Id != job.Id);
                 attempt++)
                await Task.Delay(20);

            history.RunningAppendAttempts.Should().BeGreaterThan(0);
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
        ITierMigrationService? migration = null) =>
        new(
            config ?? new OperationalScheduleConfig(),
            files ?? Substitute.For<IFileMaintenanceService>(),
            migration ?? Substitute.For<ITierMigrationService>(),
            quality ?? Substitute.For<IDataQualityService>(),
            search,
            history ?? new FileOperationalCaseHistoryStore(_defaultHistoryRoot));

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

    private static OperationalScheduleConfig AlwaysOpenConfig() =>
        new(
            "Always open",
            [],
            [new MaintenanceWindow(
                "all-day",
                TimeSpan.Zero,
                TimeSpan.FromHours(23.99),
                [DateTimeOffset.Now.DayOfWeek],
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
