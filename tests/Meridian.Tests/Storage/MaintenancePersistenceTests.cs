using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class MaintenancePersistenceTests
{
    [Fact]
    public void MonthlyCompressionPreset_UsesExplicitFirstSundayExpression()
    {
        var schedule = MaintenanceSchedulePresets.MonthlyCompression("Monthly compression");

        schedule.CronExpression.Should().Be("0 1 * * 0#1");
        schedule.CalculateNextExecution(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .Should()
            .Be(new DateTimeOffset(2025, 1, 5, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task UpdateExecutionAsync_PersistsLatestHistoryBeforeReturning()
    {
        var dataRoot = CreateTempRoot();
        var history = new MaintenanceExecutionHistory(dataRoot);
        var execution = new MaintenanceExecution
        {
            ScheduleId = "schedule-1",
            ScheduleName = "Daily health",
            TaskType = MaintenanceTaskType.HealthCheck,
            Status = MaintenanceExecutionStatus.Pending
        };

        await history.RecordExecutionAsync(execution);

        execution.Status = MaintenanceExecutionStatus.Completed;
        execution.CompletedAt = execution.StartedAt.AddMinutes(5);

        await history.UpdateExecutionAsync(execution);

        var persisted = await ReadAsync<List<MaintenanceExecution>>(Path.Combine(dataRoot, ".maintenance", "history.json"));
        persisted.Should().ContainSingle(entry =>
            entry.ExecutionId == execution.ExecutionId &&
            entry.Status == MaintenanceExecutionStatus.Completed &&
            entry.CompletedAt == execution.CompletedAt);
    }

    [Fact]
    public async Task TwoHistoryInstances_ConcurrentRecords_MergeWithoutLastWriterLoss()
    {
        var dataRoot = CreateTempRoot();
        var firstHistory = new MaintenanceExecutionHistory(dataRoot);
        var secondHistory = new MaintenanceExecutionHistory(dataRoot);
        var first = new MaintenanceExecution
        {
            ScheduleId = "schedule-1",
            TaskType = MaintenanceTaskType.HealthCheck
        };
        var second = new MaintenanceExecution
        {
            ScheduleId = "schedule-2",
            TaskType = MaintenanceTaskType.Cleanup
        };

        await Task.WhenAll(
            firstHistory.RecordExecutionAsync(first),
            secondHistory.RecordExecutionAsync(second));

        var restarted = new MaintenanceExecutionHistory(dataRoot);
        restarted.GetRecentExecutions()
            .Select(execution => execution.ExecutionId)
            .Should().BeEquivalentTo(new[] { first.ExecutionId, second.ExecutionId });
    }

    [Fact]
    public async Task ExecutionHistoryReads_AreDefensiveDeepClones()
    {
        var dataRoot = CreateTempRoot();
        var history = new MaintenanceExecutionHistory(dataRoot);
        var execution = new MaintenanceExecution
        {
            ScheduleId = "schedule-clone",
            TaskType = MaintenanceTaskType.HealthCheck
        };
        execution.LogMessages.Add("retained");
        await history.RecordExecutionAsync(execution);

        var read = history.GetExecution(execution.ExecutionId)!;
        read.Status = MaintenanceExecutionStatus.Failed;
        read.LogMessages.Add("caller mutation");

        var retained = history.GetExecution(execution.ExecutionId)!;
        retained.Status.Should().Be(MaintenanceExecutionStatus.Pending);
        retained.LogMessages.Should().Equal("retained");
    }

    [Fact]
    public async Task UpdateExecutionAsync_PersistenceFailure_PreservesPriorMemoryAndDiskState()
    {
        var dataRoot = CreateTempRoot();
        var history = new MaintenanceExecutionHistory(dataRoot);
        var execution = new MaintenanceExecution
        {
            ScheduleId = "schedule-atomic-history",
            TaskType = MaintenanceTaskType.HealthCheck
        };
        await history.RecordExecutionAsync(execution);
        var maintenanceDirectory = Path.Combine(dataRoot, ".maintenance");
        var durableBackup = Path.Combine(dataRoot, "maintenance-backup");
        Directory.Move(maintenanceDirectory, durableBackup);
        await File.WriteAllTextAsync(maintenanceDirectory, "not a directory");
        execution.Status = MaintenanceExecutionStatus.Completed;
        execution.CompletedAt = DateTimeOffset.UtcNow;

        Func<Task> act = () => history.UpdateExecutionAsync(execution);

        await act.Should().ThrowAsync<IOException>();
        history.GetExecution(execution.ExecutionId)!.Status
            .Should().Be(MaintenanceExecutionStatus.Pending);
        var persisted = await ReadAsync<List<MaintenanceExecution>>(
            Path.Combine(durableBackup, "history.json"));
        persisted.Should().ContainSingle().Which.Status
            .Should().Be(MaintenanceExecutionStatus.Pending);
    }

    [Fact]
    public async Task UpdateScheduleAfterExecutionAsync_PersistsScheduleMetadataBeforeReturning()
    {
        var dataRoot = CreateTempRoot();
        var manager = new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            dataRoot);

        var schedule = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Daily health",
            CronExpression = "0 3 * * *",
            TaskType = MaintenanceTaskType.HealthCheck
        });

        var execution = new MaintenanceExecution
        {
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            TaskType = schedule.TaskType,
            Status = MaintenanceExecutionStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        };

        await manager.UpdateScheduleAfterExecutionAsync(schedule.ScheduleId, execution);

        var persisted = await ReadAsync<List<ArchiveMaintenanceSchedule>>(Path.Combine(dataRoot, ".maintenance", "schedules.json"));
        persisted.Should().ContainSingle(entry =>
            entry.ScheduleId == schedule.ScheduleId &&
            entry.LastExecutionId == execution.ExecutionId &&
            entry.LastExecutionStatus == MaintenanceExecutionStatus.Completed &&
            entry.ExecutionCount == 1 &&
            entry.SuccessfulExecutions == 1 &&
            entry.LastExecutedAt == execution.StartedAt);
    }

    [Fact]
    public async Task CreateScheduleAsync_EnabledImpossibleCron_IsRejectedWithoutPersistence()
    {
        var dataRoot = CreateTempRoot();
        var manager = CreateManager(dataRoot);
        var schedule = new ArchiveMaintenanceSchedule
        {
            Name = "Impossible health check",
            CronExpression = "0 0 30 2 *",
            Enabled = true
        };

        Func<Task> act = () => manager.CreateScheduleAsync(schedule);

        await act.Should().ThrowAsync<ArgumentException>();
        manager.GetSchedule(schedule.ScheduleId).Should().BeNull();
        File.Exists(GetSchedulesPath(dataRoot)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateScheduleAsync_EnabledImpossibleCron_PreservesStoredSchedule()
    {
        var dataRoot = CreateTempRoot();
        var manager = CreateManager(dataRoot);
        var created = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Daily health",
            CronExpression = "0 3 * * *"
        });
        var replacement = new ArchiveMaintenanceSchedule
        {
            ScheduleId = created.ScheduleId,
            Name = created.Name,
            CronExpression = "0 0 30 2 *",
            Enabled = true,
            TaskType = created.TaskType
        };

        Func<Task> act = () => manager.UpdateScheduleAsync(replacement);

        await act.Should().ThrowAsync<ArgumentException>();
        manager.GetSchedule(created.ScheduleId)!.CronExpression.Should().Be("0 3 * * *");
        var persisted = await ReadAsync<List<ArchiveMaintenanceSchedule>>(GetSchedulesPath(dataRoot));
        persisted.Should().ContainSingle().Which.CronExpression.Should().Be("0 3 * * *");
    }

    [Fact]
    public async Task SetScheduleEnabledAsync_ImpossibleDisabledCron_IsRejectedAndRemainsDisabled()
    {
        var dataRoot = CreateTempRoot();
        var manager = CreateManager(dataRoot);
        var created = await manager.CreateScheduleAsync(new ArchiveMaintenanceSchedule
        {
            Name = "Disabled impossible health check",
            CronExpression = "0 0 30 2 *",
            Enabled = false
        });

        Func<Task> act = () => manager.SetScheduleEnabledAsync(created.ScheduleId, enabled: true);

        await act.Should().ThrowAsync<ArgumentException>();
        var retained = manager.GetSchedule(created.ScheduleId);
        retained.Should().NotBeNull();
        retained!.Enabled.Should().BeFalse();
        retained.NextExecutionAt.Should().BeNull();
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        value.Should().NotBeNull();
        return value!;
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ArchiveMaintenanceScheduleManager CreateManager(string dataRoot)
    {
        return new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            dataRoot);
    }

    private static string GetSchedulesPath(string dataRoot)
    {
        return Path.Combine(dataRoot, ".maintenance", "schedules.json");
    }
}
