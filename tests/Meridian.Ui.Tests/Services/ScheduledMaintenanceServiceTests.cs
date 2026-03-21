using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ScheduledMaintenanceService"/> and its associated model types.
/// </summary>
public sealed class ScheduledMaintenanceServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = ScheduledMaintenanceService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = ScheduledMaintenanceService.Instance;
        var b = ScheduledMaintenanceService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Tasks property ──────────────────────────────────────────────

    [Fact]
    public void Tasks_ShouldNotBeNull()
    {
        var service = ScheduledMaintenanceService.Instance;
        service.Tasks.Should().NotBeNull();
    }

    [Fact]
    public void Tasks_ShouldContainDefaultTasks()
    {
        var service = ScheduledMaintenanceService.Instance;

        // The service initializes with 4 default tasks
        service.Tasks.Should().HaveCountGreaterThanOrEqualTo(4);
        service.Tasks.Should().Contain(t => t.Id == "daily-verification");
        service.Tasks.Should().Contain(t => t.Id == "weekly-optimization");
        service.Tasks.Should().Contain(t => t.Id == "monthly-audit");
        service.Tasks.Should().Contain(t => t.Id == "daily-cleanup");
    }

    [Fact]
    public void Tasks_DailyCleanup_ShouldBeDisabledByDefault()
    {
        var service = ScheduledMaintenanceService.Instance;
        var cleanup = service.Tasks.First(t => t.Id == "daily-cleanup");

        cleanup.IsEnabled.Should().BeFalse("cleanup is disabled by default for safety");
    }

    [Fact]
    public void Tasks_DailyVerification_ShouldBeEnabled()
    {
        var service = ScheduledMaintenanceService.Instance;
        var verification = service.Tasks.First(t => t.Id == "daily-verification");

        verification.IsEnabled.Should().BeTrue();
        verification.Name.Should().Be("Daily Verification");
        verification.TaskType.Should().Be(MaintenanceTaskType.Verification);
    }

    // ── ExecutionLog property ───────────────────────────────────────

    [Fact]
    public void ExecutionLog_ShouldNotBeNull()
    {
        var service = ScheduledMaintenanceService.Instance;
        service.ExecutionLog.Should().NotBeNull();
    }

    // ── IsSchedulerRunning property ─────────────────────────────────

    [Fact]
    public void IsSchedulerRunning_ByDefault_ShouldBeFalse()
    {
        // Fresh singleton - scheduler should not be running unless StartScheduler was called.
        // NOTE: since this is a singleton shared across tests, if StartScheduler was
        // previously called, we stop it first to ensure test isolation.
        var service = ScheduledMaintenanceService.Instance;
        service.StopScheduler();

        service.IsSchedulerRunning.Should().BeFalse();
    }

    // ── Start/Stop scheduler ────────────────────────────────────────

    [Fact]
    public void StartScheduler_ShouldSetIsSchedulerRunningToTrue()
    {
        var service = ScheduledMaintenanceService.Instance;

        service.StartScheduler();

        try
        {
            service.IsSchedulerRunning.Should().BeTrue();
        }
        finally
        {
            service.StopScheduler();
        }
    }

    [Fact]
    public void StopScheduler_ShouldSetIsSchedulerRunningToFalse()
    {
        var service = ScheduledMaintenanceService.Instance;

        service.StartScheduler();
        service.StopScheduler();

        service.IsSchedulerRunning.Should().BeFalse();
    }

    // ── MaintenanceTask model ───────────────────────────────────────

    [Fact]
    public void MaintenanceTask_DefaultValues_ShouldBeCorrect()
    {
        var task = new MaintenanceTask();

        task.Id.Should().BeEmpty();
        task.Name.Should().BeEmpty();
        task.Description.Should().BeEmpty();
        task.TaskType.Should().Be(MaintenanceTaskType.Verification);
        task.Schedule.Should().NotBeNull();
        task.Scope.Should().Be(MaintenanceScope.All);
        task.IsEnabled.Should().BeFalse();
        task.IsRunning.Should().BeFalse();
        task.LastRunStart.Should().BeNull();
        task.LastRunEnd.Should().BeNull();
        task.LastRunSuccess.Should().BeNull();
        task.LastRunMessage.Should().BeNull();
    }

    [Fact]
    public void MaintenanceTask_ShouldRunNow_WhenDisabled_ShouldReturnFalse()
    {
        var task = new MaintenanceTask { IsEnabled = false };
        task.ShouldRunNow(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void MaintenanceTask_ShouldRunNow_WhenRunning_ShouldReturnFalse()
    {
        var task = new MaintenanceTask { IsEnabled = true, IsRunning = true };
        task.ShouldRunNow(DateTime.UtcNow).Should().BeFalse();
    }

    // ── MaintenanceExecutionLog model ───────────────────────────────

    [Fact]
    public void MaintenanceExecutionLog_DefaultValues_ShouldBeCorrect()
    {
        var log = new MaintenanceExecutionLog();

        log.TaskId.Should().BeEmpty();
        log.TaskName.Should().BeEmpty();
        log.StartTime.Should().Be(default(DateTime));
        log.EndTime.Should().BeNull();
        log.Duration.Should().BeNull();
        log.Success.Should().BeFalse();
        log.Message.Should().BeEmpty();
        log.IsDryRun.Should().BeFalse();
        log.FilesProcessed.Should().Be(0);
        log.BytesSaved.Should().Be(0);
    }

    [Fact]
    public void MaintenanceExecutionLog_DurationText_WhenNull_ShouldReturnNA()
    {
        var log = new MaintenanceExecutionLog { Duration = null };
        log.DurationText.Should().Be("N/A");
    }

    [Fact]
    public void MaintenanceExecutionLog_DurationText_WhenSeconds_ShouldFormat()
    {
        var log = new MaintenanceExecutionLog { Duration = TimeSpan.FromSeconds(45) };
        log.DurationText.Should().Be("45s");
    }

    [Fact]
    public void MaintenanceExecutionLog_DurationText_WhenMinutes_ShouldFormat()
    {
        var log = new MaintenanceExecutionLog { Duration = TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)) };
        log.DurationText.Should().Be("5m 30s");
    }

    [Fact]
    public void MaintenanceExecutionLog_DurationText_WhenHours_ShouldFormat()
    {
        var log = new MaintenanceExecutionLog { Duration = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(15)) };
        log.DurationText.Should().Be("2h 15m");
    }

    // ── MaintenanceTaskType enum ────────────────────────────────────

    [Theory]
    [InlineData(MaintenanceTaskType.Verification)]
    [InlineData(MaintenanceTaskType.Optimization)]
    [InlineData(MaintenanceTaskType.Cleanup)]
    [InlineData(MaintenanceTaskType.FullAudit)]
    [InlineData(MaintenanceTaskType.Compression)]
    [InlineData(MaintenanceTaskType.Deduplication)]
    public void MaintenanceTaskType_AllValues_ShouldBeDefined(MaintenanceTaskType type)
    {
        Enum.IsDefined(typeof(MaintenanceTaskType), type).Should().BeTrue();
    }

    [Fact]
    public void MaintenanceTaskType_ShouldHaveSixValues()
    {
        Enum.GetValues<MaintenanceTaskType>().Should().HaveCount(6);
    }

    // ── ScheduleType enum ───────────────────────────────────────────

    [Theory]
    [InlineData(ScheduleType.Hourly)]
    [InlineData(ScheduleType.Daily)]
    [InlineData(ScheduleType.Weekly)]
    [InlineData(ScheduleType.Monthly)]
    public void ScheduleType_AllValues_ShouldBeDefined(ScheduleType type)
    {
        Enum.IsDefined(typeof(ScheduleType), type).Should().BeTrue();
    }

    // ── MaintenanceScope enum ───────────────────────────────────────

    [Theory]
    [InlineData(MaintenanceScope.All)]
    [InlineData(MaintenanceScope.HotTier)]
    [InlineData(MaintenanceScope.WarmTier)]
    [InlineData(MaintenanceScope.ColdTier)]
    [InlineData(MaintenanceScope.Last7Days)]
    [InlineData(MaintenanceScope.Last30Days)]
    [InlineData(MaintenanceScope.Custom)]
    public void MaintenanceScope_AllValues_ShouldBeDefined(MaintenanceScope scope)
    {
        Enum.IsDefined(typeof(MaintenanceScope), scope).Should().BeTrue();
    }

    // ── MaintenanceResult model ─────────────────────────────────────

    [Fact]
    public void MaintenanceResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new MaintenanceResult();

        result.TaskId.Should().BeEmpty();
        result.TaskName.Should().BeEmpty();
        result.EndTime.Should().BeNull();
        result.Duration.Should().BeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.Error.Should().BeNull();
        result.IsDryRun.Should().BeFalse();
        result.WasCancelled.Should().BeFalse();
        result.FilesProcessed.Should().Be(0);
        result.FilesSuccessful.Should().Be(0);
        result.FilesFailed.Should().Be(0);
        result.BytesSaved.Should().Be(0);
    }

    // ── MaintenanceTimingConfig model ───────────────────────────────

    [Fact]
    public void MaintenanceTimingConfig_DefaultValues_ShouldBeCorrect()
    {
        var config = new MaintenanceTimingConfig();

        config.ScheduleType.Should().Be(ScheduleType.Hourly);
        config.TimeOfDay.Should().Be(TimeSpan.Zero);
        config.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        config.DayOfMonth.Should().Be(1);
        config.MinuteOfHour.Should().Be(0);
    }

    // ── AddTask / RemoveTask ────────────────────────────────────────

    [Fact]
    public void AddTask_ShouldIncreaseTaskCount()
    {
        var service = ScheduledMaintenanceService.Instance;
        var initialCount = service.Tasks.Count;

        var task = new MaintenanceTask
        {
            Id = $"test-task-{Guid.NewGuid()}",
            Name = "Test Task",
            TaskType = MaintenanceTaskType.Verification
        };

        service.AddTask(task);

        try
        {
            service.Tasks.Count.Should().Be(initialCount + 1);
        }
        finally
        {
            service.RemoveTask(task.Id);
        }
    }

    [Fact]
    public void RemoveTask_WithValidId_ShouldReturnTrue()
    {
        var service = ScheduledMaintenanceService.Instance;
        var task = new MaintenanceTask
        {
            Id = $"test-remove-{Guid.NewGuid()}",
            Name = "Removable Task"
        };

        service.AddTask(task);
        var removed = service.RemoveTask(task.Id);

        removed.Should().BeTrue();
    }

    [Fact]
    public void RemoveTask_WithInvalidId_ShouldReturnFalse()
    {
        var service = ScheduledMaintenanceService.Instance;
        var removed = service.RemoveTask("non-existent-task-id");

        removed.Should().BeFalse();
    }
}
