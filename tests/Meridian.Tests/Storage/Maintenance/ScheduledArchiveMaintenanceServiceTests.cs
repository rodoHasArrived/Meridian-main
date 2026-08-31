using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage.Maintenance;

/// <summary>
/// Guards the archive maintenance orchestrator. The failure modes under guard: silent
/// archive data loss from mis-gated retention/cleanup deletion (dry-run or unconfigured
/// deletion flags actually deleting files), and maintenance executions that vanish,
/// hang, or crash the scheduler without recorded evidence.
/// </summary>
public sealed class ScheduledArchiveMaintenanceServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "meridian-tests", $"archive-maintenance-{Guid.NewGuid():N}");
    private readonly string _storageRoot;
    private readonly Mock<IFileMaintenanceService> _fileMaintenance = new();
    private readonly Mock<ITierMigrationService> _tierMigration = new();
    private readonly ArchiveMaintenanceScheduleManager _scheduleManager;
    private readonly List<ScheduledArchiveMaintenanceService> _services = new();

    public ScheduledArchiveMaintenanceServiceTests()
    {
        _storageRoot = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_storageRoot);
        _scheduleManager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            Path.Combine(_tempRoot, "manager"));
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; leaked temp dirs are collected by the OS temp sweep.
        }
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_HealthCheckHealthy_CompletesAndReportsEvidence()
    {
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateHealthyReport(totalFiles: 5, totalBytes: 1000));
        var sut = CreateSut();
        var raisedEvents = new List<string>();
        sut.ExecutionStarted += (_, _) => raisedEvents.Add("started");
        sut.ExecutionCompleted += (_, _) => raisedEvents.Add("completed");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.HealthCheck, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        execution.ManualTrigger.Should().BeTrue();
        execution.FilesProcessed.Should().Be(5);
        execution.BytesProcessed.Should().Be(1000);
        execution.Result!.Success.Should().BeTrue();
        execution.CompletedAt.Should().NotBeNull();
        raisedEvents.Should().ContainInOrder("started", "completed");
        sut.CurrentExecution.Should().BeNull();
        _scheduleManager.ExecutionHistory.GetExecution(execution.ExecutionId)
            .Should().NotBeNull("the execution record is operator evidence and must be persisted")
            .And.Subject.As<MaintenanceExecution>().Status.Should().Be(MaintenanceExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_HealthCheckFindsCorruption_CompletesWithWarnings()
    {
        var issues = new[]
        {
            new HealthIssue(IssueSeverity.Critical, IssueType.ChecksumMismatch, "ticks/a.jsonl",
                Details: "checksum mismatch", RecommendedAction: "repair"),
            new HealthIssue(IssueSeverity.Critical, IssueType.TruncatedFile, "ticks/b.jsonl",
                Details: null, RecommendedAction: "truncate")
        };
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(
                "r-corrupt", DateTime.UtcNow, 25,
                new HealthSummary(TotalFiles: 10, TotalBytes: 4096, HealthyFiles: 8, WarningFiles: 0, CorruptedFiles: 2, OrphanedFiles: 0),
                issues,
                new HealthStatistics()));
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.HealthCheck, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.CompletedWithWarnings,
            "corrupted files must surface as a warning status, never as silent success");
        execution.Result!.IssuesFound.Should().Be(2);
        execution.Result.Issues.Should().HaveCount(2);
        execution.Result.Issues[0].Path.Should().Be("ticks/a.jsonl");
        execution.Result.Issues[0].IssueType.Should().Be(nameof(IssueType.ChecksumMismatch));
        execution.Result.Issues[0].Description.Should().Be("checksum mismatch");
        execution.Result.Issues[1].Description.Should().Be("truncate", "null Details falls back to the recommended action");
        execution.Result.Issues.Should().OnlyContain(i => !i.WasResolved);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TaskThrows_ReportsFailedWithoutThrowing()
    {
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk offline"));
        var sut = CreateSut();
        var failedRaised = false;
        var completedRaised = false;
        sut.ExecutionFailed += (_, _) => failedRaised = true;
        sut.ExecutionCompleted += (_, _) => completedRaised = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.HealthCheck, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("disk offline");
        failedRaised.Should().BeTrue();
        completedRaised.Should().BeFalse();
        _scheduleManager.ExecutionHistory.GetExecution(execution.ExecutionId)!
            .Status.Should().Be(MaintenanceExecutionStatus.Failed,
                "a failed run must leave failure evidence in the history");
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_UnsupportedTaskType_ReportsFailed()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync((MaintenanceTaskType)99, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("not supported");
        _fileMaintenance.VerifyNoOtherCalls();
        _tierMigration.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_NullTargetPaths_UsesStorageRootPath()
    {
        HealthCheckOptions? captured = null;
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HealthCheckOptions, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync(CreateHealthyReport());
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { ParallelOperations = 2 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.HealthCheck, options, targetPaths: null, ct: cts.Token);

        captured.Should().NotBeNull();
        captured!.Paths.Should().BeEquivalentTo(new[] { _storageRoot },
            "a maintenance run without explicit paths must default to the configured storage root");
        captured.ParallelChecks.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CleanupDryRun_DeletesNothing()
    {
        var orphanPath = WriteFile("orphan.dat", "orphaned-bytes");
        SetupOrphans(new OrphanedFile(orphanPath, "no manifest", 2048, DateTime.UtcNow.AddDays(-30)));
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions
        {
            DeleteOrphans = true,
            DryRun = true,
            DeleteTemporaryFiles = false
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Cleanup, options, ct: cts.Token);

        File.Exists(orphanPath).Should().BeTrue("dry-run must never touch data");
        execution.Result!.IssuesFound.Should().Be(1);
        execution.Result.IssuesResolved.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CleanupWithoutDeleteOrphansFlag_PreservesOrphans()
    {
        var orphanPath = WriteFile("orphan.dat", "orphaned-bytes");
        SetupOrphans(new OrphanedFile(orphanPath, "no manifest", 2048, DateTime.UtcNow.AddDays(-30)));
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { DryRun = false, DeleteTemporaryFiles = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Cleanup, options, ct: cts.Token);

        File.Exists(orphanPath).Should().BeTrue("orphan deletion must be explicitly opted into");
        execution.Result!.FilesSkipped.Should().Be(1);
        execution.Result.IssuesResolved.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CleanupDeleteOrphans_DeletesOnlyOrphansOlderThanAgeThreshold()
    {
        var oldPath = WriteFile("old-orphan.dat", "old");
        var youngPath = WriteFile("young-orphan.dat", "young");
        SetupOrphans(
            new OrphanedFile(oldPath, "no manifest", 2048, DateTime.UtcNow.AddDays(-10)),
            new OrphanedFile(youngPath, "no manifest", 512, DateTime.UtcNow.AddDays(-1)));
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions
        {
            DeleteOrphans = true,
            OrphanAgeDays = 7,
            DeleteTemporaryFiles = false
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Cleanup, options, ct: cts.Token);

        File.Exists(oldPath).Should().BeFalse("orphans past the age threshold are deletable");
        File.Exists(youngPath).Should().BeTrue("recent orphans must survive the age gate");
        execution.Result!.IssuesResolved.Should().Be(1);
        execution.Result.BytesSaved.Should().Be(2048);
        execution.LogMessages.Should().Contain(m => m.StartsWith("Deleted orphan:") && m.Contains(oldPath));
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CleanupTemporaryFiles_DeletesOnlyStaleTmpFiles()
    {
        var stalePath = WriteFile("stale.tmp", "stale");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddDays(-2));
        var freshPath = WriteFile("fresh.tmp", "fresh");
        var dataPath = WriteFile("keep.jsonl", "data");
        SetupOrphans();
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { DeleteTemporaryFiles = true, DeleteOrphans = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Cleanup, options, ct: cts.Token);

        File.Exists(stalePath).Should().BeFalse("temp files older than 24h are sweepable");
        File.Exists(freshPath).Should().BeTrue("recent temp files may still be in use");
        File.Exists(dataPath).Should().BeTrue("the temp sweep must only touch *.tmp files");
        execution.Result!.FilesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_RetentionEnforcement_DeletesOnlyExpiredJsonlFiles()
    {
        var expiredJsonl = WriteAgedFile("ticks/expired.jsonl", daysOld: 400);
        var expiredGz = WriteAgedFile("ticks/expired.jsonl.gz", daysOld: 400);
        var freshJsonl = WriteFile("ticks/fresh.jsonl", "fresh");
        var expiredParquet = WriteAgedFile("ticks/expired.parquet", daysOld: 400);
        var expectedBytes = new FileInfo(expiredJsonl).Length + new FileInfo(expiredGz).Length;
        var sut = CreateSut(retentionDays: null); // falls back to the 365-day default
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.RetentionEnforcement, ct: cts.Token);

        File.Exists(expiredJsonl).Should().BeFalse();
        File.Exists(expiredGz).Should().BeFalse();
        File.Exists(freshJsonl).Should().BeTrue("unexpired data must survive retention enforcement");
        File.Exists(expiredParquet).Should().BeTrue("retention only targets .jsonl/.jsonl.gz files");
        execution.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        execution.Result!.FilesProcessed.Should().Be(2);
        execution.Result.BytesSaved.Should().Be(expectedBytes);
        execution.LogMessages.Should().Contain(m => m.StartsWith("Deleted expired:"));
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_RetentionEnforcementDryRun_ReportsWithoutDeleting()
    {
        var expiredJsonl = WriteAgedFile("ticks/expired.jsonl", daysOld: 400);
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { DryRun = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.RetentionEnforcement, options, ct: cts.Token);

        File.Exists(expiredJsonl).Should().BeTrue("dry-run retention must never delete");
        execution.Result!.FilesProcessed.Should().Be(1, "dry-run still reports what it would delete");
        execution.Result.BytesSaved.Should().Be(new FileInfo(expiredJsonl).Length);
        execution.LogMessages.Should().Contain(m => m.StartsWith("[DRY RUN] Would delete:"));
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_RetentionEnforcementSkipCriticalData_PreservesCriticalPaths()
    {
        var criticalPath = WriteAgedFile("critical/expired.jsonl", daysOld: 400);
        var normalPath = WriteAgedFile("ticks/expired.jsonl", daysOld: 400);
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { SkipCriticalData = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.RetentionEnforcement, options, ct: cts.Token);

        File.Exists(criticalPath).Should().BeTrue("paths marked critical must survive retention");
        File.Exists(normalPath).Should().BeFalse();
        execution.Result!.FilesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_RetentionEnforcementOverrideRetentionDays_TakesPrecedence()
    {
        var midAged = WriteAgedFile("ticks/mid-aged.jsonl", daysOld: 60);
        var recent = WriteAgedFile("ticks/recent.jsonl", daysOld: 10);
        var sut = CreateSut(retentionDays: 1000);
        var options = new MaintenanceTaskOptions { OverrideRetentionDays = 30 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.RetentionEnforcement, options, ct: cts.Token);

        File.Exists(midAged).Should().BeFalse(
            "the per-task override must win over the storage-level retention setting");
        File.Exists(recent).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationEmptyPlan_SkipsMigration()
    {
        SetupMigrationPlan();
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { RunOnlyDuringMarketClosedHours = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        execution.Result!.Summary.Should().Contain("No files eligible");
        _tierMigration.Verify(
            m => m.MigrateAsync(It.IsAny<string>(), It.IsAny<StorageTier>(), It.IsAny<MigrationOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationDryRun_PlansWithoutMigrating()
    {
        SetupMigrationPlan(
            CreateAction("a.jsonl", sizeBytes: 100, ageDays: 30),
            CreateAction("b.jsonl", sizeBytes: 100, ageDays: 20));
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { DryRun = true, RunOnlyDuringMarketClosedHours = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        execution.Result!.FilesProcessed.Should().Be(2);
        execution.LogMessages.Should().Contain(m => m.StartsWith("[DRY RUN] Would migrate"));
        _tierMigration.Verify(
            m => m.MigrateAsync(It.IsAny<string>(), It.IsAny<StorageTier>(), It.IsAny<MigrationOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationRespectsMaxMigrationsPerRun_MigratesOldestFirst()
    {
        SetupMigrationPlan(
            CreateAction("young.jsonl", sizeBytes: 100, ageDays: 100),
            CreateAction("oldest.jsonl", sizeBytes: 100, ageDays: 300),
            CreateAction("older.jsonl", sizeBytes: 100, ageDays: 200));
        var migrated = SetupMigrationCapture(success: true);
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions
        {
            MaxMigrationsPerRun = 2,
            MaxMigrationBytesPerRun = null,
            ParallelOperations = 1,
            RunOnlyDuringMarketClosedHours = false
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        migrated.Select(m => m.SourcePath).Should().Equal("oldest.jsonl", "older.jsonl");
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationByteBudget_AlwaysMigratesFirstActionThenStops()
    {
        const long tenMb = 10L * 1024 * 1024;
        SetupMigrationPlan(
            CreateAction("oldest.jsonl", sizeBytes: tenMb, ageDays: 2),
            CreateAction("newer.jsonl", sizeBytes: tenMb, ageDays: 1));
        var migrated = SetupMigrationCapture(success: true);
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions
        {
            MaxMigrationBytesPerRun = 5L * 1024 * 1024,
            ParallelOperations = 1,
            RunOnlyDuringMarketClosedHours = false
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        migrated.Should().ContainSingle("the first action is always migrated even over budget, then the budget stops the rest")
            .Which.SourcePath.Should().Be("oldest.jsonl");
        execution.Result!.TotalFiles.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteMaintenanceAsync_TierMigrationDeleteSourceFlag_OnlyPropagatesWhenConfigured(bool deleteSource)
    {
        SetupMigrationPlan(CreateAction("a.jsonl", sizeBytes: 100, ageDays: 30));
        var migrated = SetupMigrationCapture(success: true);
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions
        {
            DeleteSourceAfterMigration = deleteSource,
            RunOnlyDuringMarketClosedHours = false
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        migrated.Should().ContainSingle();
        migrated[0].Options.DeleteSource.Should().Be(deleteSource,
            "source deletion during migration must fire only when explicitly configured");
        migrated[0].Options.VerifyChecksum.Should().Be(options.VerifyAfterMigration);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationFailures_ReportCompletedWithWarningsAndLogErrors()
    {
        SetupMigrationPlan(CreateAction("a.jsonl", sizeBytes: 100, ageDays: 30));
        _tierMigration
            .Setup(m => m.MigrateAsync(It.IsAny<string>(), It.IsAny<StorageTier>(), It.IsAny<MigrationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(
                Success: false, FilesProcessed: 1, FilesFailed: 1, BytesProcessed: 100,
                BytesSaved: 0, Duration: TimeSpan.Zero, Errors: new[] { "checksum mismatch" }));
        var sut = CreateSut();
        var options = new MaintenanceTaskOptions { RunOnlyDuringMarketClosedHours = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.CompletedWithWarnings);
        execution.Result!.FilesFailed.Should().Be(1);
        execution.Result.Success.Should().BeFalse();
        execution.LogMessages.Should().Contain("checksum mismatch");
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_TierMigrationMarketWindowNeverOpen_RunsMigration()
    {
        SetupMigrationPlan(CreateAction("a.jsonl", sizeBytes: 100, ageDays: 30));
        var migrated = SetupMigrationCapture(success: true);
        var sut = CreateSut();
        // Open == Close means the market window is empty, so IsMarketClosed is always
        // true — deterministic regardless of wall-clock time or weekend.
        var options = new MaintenanceTaskOptions
        {
            RunOnlyDuringMarketClosedHours = true,
            MarketOpenTime = TimeSpan.Zero,
            MarketCloseTime = TimeSpan.Zero
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.TierMigration, options, ct: cts.Token);

        migrated.Should().ContainSingle();
    }

    [Fact]
    public async Task TriggerScheduleAsync_UnknownScheduleId_ThrowsKeyNotFound()
    {
        var sut = CreateSut();

        var act = () => sut.TriggerScheduleAsync("no-such-schedule");

        (await act.Should().ThrowAsync<KeyNotFoundException>()).WithMessage("*no-such-schedule*");
        sut.QueuedExecutions.Should().Be(0);
    }

    [Fact]
    public async Task TriggerScheduleAsync_WithoutRunningExecutor_QueuesExecutionAndRecordsHistory()
    {
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);

        execution.ManualTrigger.Should().BeTrue();
        execution.ScheduleId.Should().Be(schedule.ScheduleId);
        execution.ScheduleName.Should().Be(schedule.Name);
        execution.TaskType.Should().Be(schedule.TaskType);
        execution.Status.Should().Be(MaintenanceExecutionStatus.Pending);
        sut.QueuedExecutions.Should().Be(1);
        sut.GetStatus().QueuedExecutions.Should().Be(1);
        _scheduleManager.ExecutionHistory.GetExecution(execution.ExecutionId).Should().NotBeNull();
    }

    [Fact]
    public async Task TriggerScheduleAsync_WhenExecutionAlreadyPending_RejectsDuplicate()
    {
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);
        Func<Task> act = () => sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has an execution queued or running*");
        sut.QueuedExecutions.Should().Be(1);
        _scheduleManager.ExecutionHistory.GetExecutionsForSchedule(schedule.ScheduleId)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task PollDueSchedulesAsync_BlockedExecutionAcrossRepeatedPolls_QueuesOnlyOnce()
    {
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var sut = CreateSut();
        var firstPollAt = DateTimeOffset.UtcNow.AddDays(2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.PollDueSchedulesAsync(firstPollAt, cts.Token);
        var claimedNextExecution = _scheduleManager.GetSchedule(schedule.ScheduleId)!.NextExecutionAt;

        await sut.PollDueSchedulesAsync(firstPollAt.AddDays(2), cts.Token);

        sut.QueuedExecutions.Should().Be(1,
            "one blocked queued execution owns the schedule across repeated polls");
        _scheduleManager.ExecutionHistory.GetExecutionsForSchedule(schedule.ScheduleId)
            .Should().ContainSingle();
        _scheduleManager.GetSchedule(schedule.ScheduleId)!.NextExecutionAt
            .Should().Be(claimedNextExecution,
                "a poll must not claim or advance a schedule that already has outstanding work");
    }

    [Fact]
    public async Task Restart_ExpiredDispatchedClaim_RequeuesSameDurableExecutionIdentity()
    {
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var firstService = CreateSut();
        var original = await firstService.TriggerScheduleAsync(schedule.ScheduleId);
        firstService.Dispose(); // Simulate a process exit that loses the in-memory channel.

        var restartedManager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            Path.Combine(_tempRoot, "manager"));
        var restartedService = CreateSut(manager: restartedManager);

        await restartedService.PollDueSchedulesAsync(
            DateTimeOffset.UtcNow.AddMinutes(6),
            CancellationToken.None);

        restartedService.QueuedExecutions.Should().Be(1);
        var pending = restartedManager.GetSchedule(schedule.ScheduleId)!.PendingExecution;
        pending.Should().NotBeNull();
        pending!.ExecutionId.Should().Be(original.ExecutionId);
        restartedManager.ExecutionHistory.GetExecutionsForSchedule(schedule.ScheduleId)
            .Should().ContainSingle(execution => execution.ExecutionId == original.ExecutionId);
    }

    [Fact]
    public async Task Restart_ExpiredRunningClaim_WithTerminalHistory_FinalizesKnownOutcomeWithoutReplay()
    {
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var claimedAt = schedule.NextExecutionAt!.Value.AddMinutes(1);
        var claimed = await _scheduleManager.TryClaimDueScheduleAsync(
            schedule.ScheduleId,
            claimedAt,
            "departed-host",
            TimeSpan.FromMinutes(1));
        claimed.Should().NotBeNull();
        (await _scheduleManager.MarkExecutionRunningAsync(
            schedule.ScheduleId,
            claimed!.Execution.ExecutionId,
            claimedAt,
            "departed-host",
            TimeSpan.FromMinutes(1))).Should().BeTrue();
        var completed = new MaintenanceExecution
        {
            ExecutionId = claimed.Execution.ExecutionId,
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            TaskType = schedule.TaskType,
            StartedAt = claimed.Execution.CreatedAt,
            CompletedAt = claimedAt.AddSeconds(30),
            Status = MaintenanceExecutionStatus.Completed
        };
        await _scheduleManager.ExecutionHistory.RecordExecutionAsync(completed);

        var restartedManager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            Path.Combine(_tempRoot, "manager"));
        var restartedService = CreateSut(manager: restartedManager);

        await restartedService.PollDueSchedulesAsync(
            claimedAt.AddMinutes(2),
            CancellationToken.None);

        restartedService.QueuedExecutions.Should().Be(0,
            "terminal retained evidence closes the durable claim without replaying side effects");
        restartedManager.ExecutionHistory.GetExecution(completed.ExecutionId)!.Status
            .Should().Be(MaintenanceExecutionStatus.Completed);
        var retained = restartedManager.GetSchedule(schedule.ScheduleId)!;
        retained.PendingExecution.Should().BeNull();
        retained.SuccessfulExecutions.Should().Be(1);
        retained.LastExecutionId.Should().Be(completed.ExecutionId);
    }

    [Fact]
    public async Task StartAsync_TriggeredSchedule_ExecutesAndUpdatesScheduleCounters()
    {
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateHealthyReport());
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var barrierSchedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck, s => s.Name = "barrier");
        var sut = CreateSut();
        var completions = new List<MaintenanceExecution>();
        var barrierTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.ExecutionCompleted += (_, e) =>
        {
            lock (completions)
            {
                completions.Add(e);
                if (completions.Count == 2)
                {
                    barrierTcs.TrySetResult();
                }
            }
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.StartAsync(cts.Token);
        var queued = await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);
        // The executor is single-reader and fully finishes an execution (including the
        // finally block that persists schedule counters) before reading the next one,
        // so a completed barrier execution proves the first one's counters are stored.
        await sut.TriggerScheduleAsync(barrierSchedule.ScheduleId, cts.Token);
        await barrierTcs.Task.WaitAsync(cts.Token);
        sut.IsRunning.Should().BeTrue();
        await sut.StopAsync(cts.Token);

        var completed = completions[0];
        completed.ExecutionId.Should().Be(queued.ExecutionId);
        completed.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        var retainedSchedule = _scheduleManager.GetSchedule(schedule.ScheduleId)!;
        retainedSchedule.ExecutionCount.Should().Be(1);
        retainedSchedule.SuccessfulExecutions.Should().Be(1);
        retainedSchedule.LastExecutionStatus.Should().Be(MaintenanceExecutionStatus.Completed);
        retainedSchedule.LastExecutionId.Should().Be(queued.ExecutionId);
    }

    [Fact]
    public async Task StartAsync_ExecutorSurvivesFailedExecution_ProcessesNextQueuedExecution()
    {
        _fileMaintenance
            .SetupSequence(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("first run exploded"))
            .ReturnsAsync(CreateHealthyReport());
        var schedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var sut = CreateSut();
        var failedTcs = new TaskCompletionSource<MaintenanceExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedTcs = new TaskCompletionSource<MaintenanceExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.ExecutionFailed += (_, e) => failedTcs.TrySetResult(e);
        sut.ExecutionCompleted += (_, e) => completedTcs.TrySetResult(e);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.StartAsync(cts.Token);
        await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);
        var failed = await failedTcs.Task.WaitAsync(cts.Token);
        await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);
        var completed = await completedTcs.Task.WaitAsync(cts.Token);
        await sut.StopAsync(cts.Token);

        failed.Status.Should().Be(MaintenanceExecutionStatus.Failed);
        failed.ErrorMessage.Should().Contain("first run exploded");
        completed.Status.Should().Be(MaintenanceExecutionStatus.Completed,
            "one failed execution must not take down the executor loop");
    }

    [Fact]
    public async Task CancelExecutionAsync_UnknownExecutionId_ReturnsFalse()
    {
        var sut = CreateSut();

        (await sut.CancelExecutionAsync("nope")).Should().BeFalse();
    }

    [Fact]
    public async Task CancelExecutionAsync_RunningManualExecution_StopsWorkAndReportsCancelled()
    {
        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockTcs = new TaskCompletionSource<HealthReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .Returns<HealthCheckOptions, CancellationToken>(async (_, token) =>
            {
                startedTcs.TrySetResult();
                return await blockTcs.Task.WaitAsync(token);
            });
        var sut = CreateSut();
        MaintenanceExecution? started = null;
        var failedRaised = false;
        sut.ExecutionStarted += (_, e) => started = e;
        sut.ExecutionFailed += (_, _) => failedRaised = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var runTask = sut.ExecuteMaintenanceAsync(MaintenanceTaskType.HealthCheck, ct: cts.Token);
        await startedTcs.Task.WaitAsync(cts.Token);
        (await sut.CancelExecutionAsync(started!.ExecutionId)).Should().BeTrue();
        var execution = await runTask.WaitAsync(cts.Token);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Cancelled);
        execution.ErrorMessage.Should().Be("Execution was cancelled");
        failedRaised.Should().BeTrue();
        sut.CurrentExecution.Should().BeNull();
        (await sut.CancelExecutionAsync(started.ExecutionId)).Should().BeFalse(
            "a finished execution is no longer cancellable");
    }

    [Fact]
    public async Task TriggerScheduleAsync_ExecutionExceedsScheduleMaxDuration_ReportsTimedOut()
    {
        var blockTcs = new TaskCompletionSource<HealthReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthCheckCalls = 0;
        _fileMaintenance
            .Setup(m => m.RunHealthCheckAsync(It.IsAny<HealthCheckOptions>(), It.IsAny<CancellationToken>()))
            .Returns<HealthCheckOptions, CancellationToken>(async (_, token) =>
                Interlocked.Increment(ref healthCheckCalls) == 1
                    ? await blockTcs.Task.WaitAsync(token) // blocks until MaxDuration cancels the run
                    : CreateHealthyReport());
        var schedule = await CreateScheduleAsync(
            MaintenanceTaskType.HealthCheck,
            s => s.MaxDuration = TimeSpan.FromMilliseconds(100));
        var barrierSchedule = await CreateScheduleAsync(MaintenanceTaskType.HealthCheck, s => s.Name = "barrier");
        var sut = CreateSut();
        var failedTcs = new TaskCompletionSource<MaintenanceExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var barrierTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.ExecutionFailed += (_, e) => failedTcs.TrySetResult(e);
        sut.ExecutionCompleted += (_, _) => barrierTcs.TrySetResult();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.StartAsync(cts.Token);
        await sut.TriggerScheduleAsync(schedule.ScheduleId, cts.Token);
        // Barrier execution: the single-reader executor only starts it after the timed-out
        // run's finally block has persisted the schedule's failure counters.
        await sut.TriggerScheduleAsync(barrierSchedule.ScheduleId, cts.Token);
        var failed = await failedTcs.Task.WaitAsync(cts.Token);
        await barrierTcs.Task.WaitAsync(cts.Token);
        await sut.StopAsync(cts.Token);

        failed.Status.Should().Be(MaintenanceExecutionStatus.TimedOut,
            "a run past the schedule's MaxDuration must be contained, not hang forever");
        failed.ErrorMessage.Should().Be("Execution timed out");
        _scheduleManager.GetSchedule(schedule.ScheduleId)!.FailedExecutions.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_AfterStartWhileIdle_CompletesCleanly()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.StartAsync(cts.Token);
        var act = () => sut.StopAsync(cts.Token);

        await act.Should().NotThrowAsync();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_BeforeStart_ReportsNotRunningWithScheduleSummary()
    {
        await CreateScheduleAsync(MaintenanceTaskType.HealthCheck);
        var sut = CreateSut();

        var status = sut.GetStatus();

        status.IsRunning.Should().BeFalse();
        status.QueuedExecutions.Should().Be(0);
        status.CurrentExecution.Should().BeNull();
        status.ActiveSchedules.Should().Be(1);
        status.NextScheduledExecution.Should().NotBeNull("an enabled cron schedule always has a next occurrence");
        status.Uptime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        status.TotalExecutionsToday.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Dispose_AfterStart_DoesNotThrow()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);

        var act = () =>
        {
            sut.Dispose();
            sut.Dispose();
        };

        act.Should().NotThrow();
    }

    private ScheduledArchiveMaintenanceService CreateSut(
        int? retentionDays = null,
        ArchiveMaintenanceScheduleManager? manager = null)
    {
        var service = new ScheduledArchiveMaintenanceService(
            NullLogger<ScheduledArchiveMaintenanceService>.Instance,
            manager ?? _scheduleManager,
            _fileMaintenance.Object,
            _tierMigration.Object,
            new StorageOptions { RootPath = _storageRoot, RetentionDays = retentionDays });
        _services.Add(service);
        return service;
    }

    private async Task<ArchiveMaintenanceSchedule> CreateScheduleAsync(
        MaintenanceTaskType taskType,
        Action<ArchiveMaintenanceSchedule>? configure = null)
    {
        var schedule = new ArchiveMaintenanceSchedule
        {
            Name = $"schedule-{taskType}",
            CronExpression = "0 3 * * *",
            TaskType = taskType
        };
        configure?.Invoke(schedule);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _scheduleManager.CreateScheduleAsync(schedule, cts.Token);
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_storageRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteAgedFile(string relativePath, int daysOld)
    {
        var path = WriteFile(relativePath, $"aged-{daysOld}-days");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
        return path;
    }

    private void SetupOrphans(params OrphanedFile[] orphans)
    {
        _fileMaintenance
            .Setup(m => m.FindOrphansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReport(DateTime.UtcNow, orphans, orphans.Sum(o => o.SizeBytes)));
    }

    private void SetupMigrationPlan(params PlannedMigrationAction[] actions)
    {
        _tierMigration
            .Setup(m => m.PlanMigrationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationPlan(
                DateTimeOffset.UtcNow,
                TimeSpan.FromDays(365),
                actions,
                actions.Sum(a => a.SizeBytes),
                TimeSpan.FromMinutes(1)));
    }

    private List<(string SourcePath, StorageTier Tier, MigrationOptions Options)> SetupMigrationCapture(bool success)
    {
        var captured = new List<(string, StorageTier, MigrationOptions)>();
        var gate = new object();
        _tierMigration
            .Setup(m => m.MigrateAsync(It.IsAny<string>(), It.IsAny<StorageTier>(), It.IsAny<MigrationOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, StorageTier, MigrationOptions, CancellationToken>((path, tier, options, _) =>
            {
                lock (gate)
                {
                    captured.Add((path, tier, options));
                }
            })
            .ReturnsAsync(new MigrationResult(
                Success: success, FilesProcessed: 1, FilesFailed: success ? 0 : 1,
                BytesProcessed: 100, BytesSaved: 50, Duration: TimeSpan.Zero,
                Errors: Array.Empty<string>()));
        return captured;
    }

    private static PlannedMigrationAction CreateAction(string sourcePath, long sizeBytes, int ageDays) => new(
        SourcePath: sourcePath,
        TargetTier: StorageTier.Cold,
        Reason: "age",
        SizeBytes: sizeBytes,
        FileAge: TimeSpan.FromDays(ageDays),
        EstimatedSavings: 0);

    private static HealthReport CreateHealthyReport(int totalFiles = 5, long totalBytes = 1000) => new(
        "r-healthy",
        DateTime.UtcNow,
        ScanDurationMs: 10,
        new HealthSummary(
            TotalFiles: totalFiles, TotalBytes: totalBytes, HealthyFiles: totalFiles,
            WarningFiles: 0, CorruptedFiles: 0, OrphanedFiles: 0),
        Array.Empty<HealthIssue>(),
        new HealthStatistics());
}
