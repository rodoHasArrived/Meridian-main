using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class MaintenancePersistenceTests
{
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
}
