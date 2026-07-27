using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Guards schedule durability when cancellation or filesystem failures interrupt operator changes.
/// </summary>
public sealed class BackfillScheduleManagerDurabilityTests : IDisposable
{
    private static readonly JsonSerializerOptions PersistedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-backfill-schedule-tests",
        Guid.NewGuid().ToString("N"));

    public BackfillScheduleManagerDurabilityTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task LoadSchedulesAsync_PreCanceled_RethrowsCancellationAndRemainsRetryable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seedManager = CreateManager(_dataRoot);
        var seeded = await seedManager.CreateScheduleAsync(
            CreateSchedule("Daily close recovery"),
            timeout.Token);
        var manager = CreateManager(_dataRoot);
        using var canceled = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();

        Func<Task> act = () => manager.LoadSchedulesAsync(canceled.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(canceled.Token);

        await manager.LoadSchedulesAsync(timeout.Token);
        manager.GetSchedule(seeded.ScheduleId).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadSchedulesAsync_DeletionTombstone_DoesNotResurrectDeletedSchedule()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var schedule = CreateSchedule("Deleted close recovery");
        var schedulesDirectory = GetSchedulesDirectory(_dataRoot);
        Directory.CreateDirectory(schedulesDirectory);
        var tombstonePath = Path.Combine(
            schedulesDirectory,
            $"deleted_{schedule.ScheduleId}_{Guid.NewGuid():N}.schedule-tombstone");
        await File.WriteAllTextAsync(
            tombstonePath,
            JsonSerializer.Serialize(schedule),
            timeout.Token);
        var manager = CreateManager(_dataRoot);

        await manager.LoadSchedulesAsync(timeout.Token);

        manager.GetSchedule(schedule.ScheduleId).Should().BeNull();
        manager.GetAllSchedules().Should().BeEmpty();
        File.Exists(tombstonePath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateScheduleAsync_PreCanceled_DoesNotExposeOrPersistSchedule()
    {
        var manager = CreateManager(_dataRoot);
        var schedule = CreateSchedule("Canceled close recovery");
        var createdEvents = 0;
        manager.ScheduleCreated += (_, _) => createdEvents++;
        using var canceled = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();

        Func<Task> act = () => manager.CreateScheduleAsync(schedule, canceled.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(canceled.Token);
        manager.GetSchedule(schedule.ScheduleId).Should().BeNull();
        File.Exists(GetSchedulePath(_dataRoot, schedule.ScheduleId)).Should().BeFalse();
        createdEvents.Should().Be(0);
    }

    [Fact]
    public async Task CreateScheduleAsync_PersistenceFailure_DoesNotExposeSchedule()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var blockedDataRoot = Path.Combine(_dataRoot, "blocked-root");
        await File.WriteAllTextAsync(blockedDataRoot, "not a directory", timeout.Token);
        var manager = CreateManager(blockedDataRoot);
        var schedule = CreateSchedule("Blocked close recovery");
        var createdEvents = 0;
        manager.ScheduleCreated += (_, _) => createdEvents++;

        Func<Task> act = () => manager.CreateScheduleAsync(schedule, timeout.Token);

        await act.Should().ThrowAsync<IOException>();
        manager.GetSchedule(schedule.ScheduleId).Should().BeNull();
        createdEvents.Should().Be(0);
    }

    [Fact]
    public async Task UpdateScheduleAsync_PreCanceled_PreservesPriorMemoryAndDiskState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(
            CreateSchedule("Original close recovery"),
            timeout.Token);
        created.Name = "Canceled replacement";
        var updatedEvents = 0;
        manager.ScheduleUpdated += (_, _) => updatedEvents++;
        using var canceled = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();

        Func<Task> act = () => manager.UpdateScheduleAsync(created, canceled.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(canceled.Token);
        manager.GetSchedule(created.ScheduleId)!.Name.Should().Be("Original close recovery");
        (await ReadPersistedScheduleAsync(_dataRoot, created.ScheduleId, timeout.Token))
            .Name.Should().Be("Original close recovery");
        updatedEvents.Should().Be(0);
    }

    [Fact]
    public async Task UpdateScheduleAsync_PersistenceFailure_PreservesPriorMemoryAndDiskState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(
            CreateSchedule("Original weekly recovery"),
            timeout.Token);
        var schedulesDirectory = GetSchedulesDirectory(_dataRoot);
        var durableBackup = Path.Combine(_dataRoot, "_durable_schedule_backup");
        Directory.Move(schedulesDirectory, durableBackup);
        await File.WriteAllTextAsync(schedulesDirectory, "not a directory", timeout.Token);
        created.Name = "Failed replacement";
        var updatedEvents = 0;
        manager.ScheduleUpdated += (_, _) => updatedEvents++;

        Func<Task> act = () => manager.UpdateScheduleAsync(created, timeout.Token);

        await act.Should().ThrowAsync<IOException>();
        manager.GetSchedule(created.ScheduleId)!.Name.Should().Be("Original weekly recovery");
        (await ReadPersistedScheduleAsync(
                durableBackup,
                created.ScheduleId,
                timeout.Token,
                isScheduleDirectory: true))
            .Name.Should().Be("Original weekly recovery");
        updatedEvents.Should().Be(0);
    }

    [Fact]
    public async Task DeleteScheduleAsync_DiskDeleteFailure_PreservesMemoryAndPropagatesFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(
            CreateSchedule("Protected close recovery"),
            timeout.Token);
        var schedulePath = GetSchedulePath(_dataRoot, created.ScheduleId);
        File.Delete(schedulePath);
        Directory.CreateDirectory(schedulePath);
        var deletedEvents = 0;
        manager.ScheduleDeleted += (_, _) => deletedEvents++;

        Func<Task> act = () => manager.DeleteScheduleAsync(created.ScheduleId, timeout.Token);

        var exception = await act.Should().ThrowAsync<Exception>();
        (exception.Which is IOException or UnauthorizedAccessException).Should().BeTrue();
        manager.GetSchedule(created.ScheduleId).Should().NotBeNull();
        Directory.Exists(schedulePath).Should().BeTrue();
        deletedEvents.Should().Be(0);
    }

    [Fact]
    public async Task RecordExecutionAsync_StaleInFlightCloneAfterDelete_DoesNotResurrectSchedule()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var historyPath = Path.Combine(_dataRoot, "execution-history.json");
        var manager = CreateManager(
            _dataRoot,
            new BackfillExecutionHistory(historyPath));
        var created = await manager.CreateScheduleAsync(
            CreateSchedule("Deleted in-flight recovery"),
            timeout.Token);
        var inFlightSchedule = manager.GetSchedule(created.ScheduleId);
        inFlightSchedule.Should().NotBeNull();
        var execution = new BackfillExecutionLog
        {
            ScheduleId = created.ScheduleId,
            ScheduleName = created.Name,
            JobId = "job-completed-after-delete",
            Status = ExecutionStatus.Completed
        };

        var deleted = await manager.DeleteScheduleAsync(created.ScheduleId, timeout.Token);
        await manager.RecordExecutionAsync(
            inFlightSchedule!,
            execution,
            timeout.Token);

        deleted.Should().BeTrue();
        manager.GetSchedule(created.ScheduleId).Should().BeNull();
        manager.ExecutionHistory.GetExecutionsForSchedule(created.ScheduleId)
            .Should().ContainSingle()
            .Which.ExecutionId.Should().Be(execution.ExecutionId);
        File.Exists(GetSchedulePath(_dataRoot, created.ScheduleId)).Should().BeFalse();

        var reloaded = CreateManager(_dataRoot);
        await reloaded.LoadSchedulesAsync(timeout.Token);
        reloaded.GetSchedule(created.ScheduleId).Should().BeNull();
        reloaded.GetAllSchedules().Should().BeEmpty();
        var reloadedHistory = new BackfillExecutionHistory(historyPath);
        reloadedHistory.GetExecutionsForSchedule(created.ScheduleId)
            .Should().ContainSingle()
            .Which.ExecutionId.Should().Be(execution.ExecutionId);
    }

    [Fact]
    public async Task ScheduleLifecycle_Success_PersistsBeforePublishingEachCommittedState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manager = CreateManager(_dataRoot);
        var createdEvents = 0;
        var updatedEvents = 0;
        var deletedEvents = 0;
        manager.ScheduleCreated += (_, _) => createdEvents++;
        manager.ScheduleUpdated += (_, _) => updatedEvents++;
        manager.ScheduleDeleted += (_, _) => deletedEvents++;

        var created = await manager.CreateScheduleAsync(
            CreateSchedule("Daily close recovery"),
            timeout.Token);

        manager.GetSchedule(created.ScheduleId)!.Name.Should().Be("Daily close recovery");
        (await ReadPersistedScheduleAsync(_dataRoot, created.ScheduleId, timeout.Token))
            .Name.Should().Be("Daily close recovery");

        created.Name = "Daily close recovery - revised";
        var updated = await manager.UpdateScheduleAsync(created, timeout.Token);

        manager.GetSchedule(updated.ScheduleId)!.Name.Should().Be("Daily close recovery - revised");
        (await ReadPersistedScheduleAsync(_dataRoot, updated.ScheduleId, timeout.Token))
            .Name.Should().Be("Daily close recovery - revised");

        var deleted = await manager.DeleteScheduleAsync(updated.ScheduleId, timeout.Token);

        deleted.Should().BeTrue();
        manager.GetSchedule(updated.ScheduleId).Should().BeNull();
        File.Exists(GetSchedulePath(_dataRoot, updated.ScheduleId)).Should().BeFalse();
        Directory.GetFiles(
                GetSchedulesDirectory(_dataRoot),
                "*.schedule-tombstone")
            .Should().BeEmpty();
        createdEvents.Should().Be(1);
        updatedEvents.Should().Be(1);
        deletedEvents.Should().Be(1);
    }

    private static BackfillScheduleManager CreateManager(
        string dataRoot,
        BackfillExecutionHistory? executionHistory = null)
    {
        return new BackfillScheduleManager(
            NullLogger<BackfillScheduleManager>.Instance,
            dataRoot,
            executionHistory);
    }

    private static BackfillSchedule CreateSchedule(string name)
    {
        return new BackfillSchedule
        {
            Name = name,
            CronExpression = "0 2 * * *",
            Symbols = ["AAPL", "MSFT"]
        };
    }

    private static async Task<BackfillSchedule> ReadPersistedScheduleAsync(
        string root,
        string scheduleId,
        CancellationToken ct,
        bool isScheduleDirectory = false)
    {
        var path = isScheduleDirectory
            ? Path.Combine(root, $"schedule_{scheduleId}.json")
            : GetSchedulePath(root, scheduleId);
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<BackfillSchedule>(json, PersistedJsonOptions)
            ?? throw new JsonException($"Persisted schedule '{scheduleId}' was null.");
    }

    private static string GetSchedulePath(string dataRoot, string scheduleId)
    {
        return Path.Combine(
            GetSchedulesDirectory(dataRoot),
            $"schedule_{scheduleId}.json");
    }

    private static string GetSchedulesDirectory(string dataRoot)
    {
        return Path.Combine(dataRoot, "_backfill_schedules");
    }
}
