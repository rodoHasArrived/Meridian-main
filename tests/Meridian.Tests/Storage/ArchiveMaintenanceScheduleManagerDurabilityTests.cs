using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Guards the persist-before-publish and restart-repair contract for archive schedules.
/// </summary>
public sealed class ArchiveMaintenanceScheduleManagerDurabilityTests : IDisposable
{
    private static readonly JsonSerializerOptions PersistedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-archive-schedule-tests",
        Guid.NewGuid().ToString("N"));

    public ArchiveMaintenanceScheduleManagerDurabilityTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task ScheduleReadsAndResults_AreDefensiveDeepClones()
    {
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(CreateSchedule("Daily health"));

        created.Name = "caller mutation";
        created.Options.ParallelOperations = 99;
        created.TargetPaths.Add("caller-path");
        created.Tags.Add("caller-tag");

        var firstRead = manager.GetSchedule(created.ScheduleId)!;
        firstRead.Name.Should().Be("Daily health");
        firstRead.Options.ParallelOperations.Should().Be(4);
        firstRead.TargetPaths.Should().BeEmpty();
        firstRead.Tags.Should().BeEmpty();

        firstRead.Name = "read mutation";
        firstRead.Options.ParallelOperations = 88;
        manager.GetAllSchedules().Single().Tags.Add("list mutation");

        var retained = manager.GetSchedule(created.ScheduleId)!;
        retained.Name.Should().Be("Daily health");
        retained.Options.ParallelOperations.Should().Be(4);
        retained.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateScheduleAsync_PreCanceled_PreservesPriorMemoryAndDiskState()
    {
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(CreateSchedule("Original schedule"));
        created.Name = "Canceled replacement";
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        Func<Task> act = () => manager.UpdateScheduleAsync(created, canceled.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(canceled.Token);
        manager.GetSchedule(created.ScheduleId)!.Name.Should().Be("Original schedule");
        (await ReadPersistedSchedulesAsync(GetSchedulesPath(_dataRoot)))
            .Should().ContainSingle().Which.Name.Should().Be("Original schedule");
    }

    [Fact]
    public async Task UpdateScheduleAsync_PersistenceFailure_PreservesPriorMemoryAndDiskState()
    {
        var manager = CreateManager(_dataRoot);
        var created = await manager.CreateScheduleAsync(CreateSchedule("Original schedule"));
        var maintenanceDirectory = GetMaintenanceDirectory(_dataRoot);
        var durableBackup = Path.Combine(_dataRoot, "maintenance-backup");
        Directory.Move(maintenanceDirectory, durableBackup);
        await File.WriteAllTextAsync(maintenanceDirectory, "not a directory");
        created.Name = "Failed replacement";

        Func<Task> act = () => manager.UpdateScheduleAsync(created);

        await act.Should().ThrowAsync<IOException>();
        manager.GetSchedule(created.ScheduleId)!.Name.Should().Be("Original schedule");
        (await ReadPersistedSchedulesAsync(Path.Combine(durableBackup, "schedules.json")))
            .Should().ContainSingle().Which.Name.Should().Be("Original schedule");
    }

    [Fact]
    public async Task Restart_LegacyPresetAndInvalidSchedule_AreDurablyRepairedWithoutRewritingCustomCron()
    {
        var legacyPreset = MaintenanceSchedulePresets.MonthlyCompression("Legacy monthly compression");
        legacyPreset.CronExpression = "0 1 1-7 * 0";
        var custom = new ArchiveMaintenanceSchedule
        {
            Name = "Custom POSIX schedule",
            Description = "Custom schedule intentionally using POSIX day-field OR semantics",
            CronExpression = "0 1 1-7 * 0",
            TaskType = MaintenanceTaskType.Compression,
            Priority = MaintenancePriority.Background,
            MaxDuration = TimeSpan.FromHours(6),
            Options = new MaintenanceTaskOptions
            {
                TargetCompressionCodec = "zstd",
                CompressionLevel = 19,
                RecompressExisting = true
            }
        };
        var invalid = CreateSchedule("Invalid retained schedule");
        invalid.CronExpression = "0 0 30 2 *";
        var invalidSettings = CreateSchedule("Invalid retained settings");
        invalidSettings.Options = null!;
        invalidSettings.TargetPaths = null!;
        invalidSettings.Tags = null!;

        Directory.CreateDirectory(GetMaintenanceDirectory(_dataRoot));
        await File.WriteAllTextAsync(
            GetSchedulesPath(_dataRoot),
            JsonSerializer.Serialize(
                new[] { legacyPreset, custom, invalid, invalidSettings },
                PersistedJsonOptions));

        var manager = CreateManager(_dataRoot);

        var migrated = manager.GetSchedule(legacyPreset.ScheduleId)!;
        migrated.CronExpression.Should().Be("0 1 * * 0#1");
        migrated.LastRepairReason.Should().Contain("first-Sunday");
        migrated.LastRepairedAt.Should().NotBeNull();

        var retainedCustom = manager.GetSchedule(custom.ScheduleId)!;
        retainedCustom.CronExpression.Should().Be("0 1 1-7 * 0");
        retainedCustom.LastRepairReason.Should().BeNull();

        var quarantined = manager.GetSchedule(invalid.ScheduleId)!;
        quarantined.Enabled.Should().BeFalse();
        quarantined.NextExecutionAt.Should().BeNull();
        quarantined.LastRepairReason.Should().Contain("no valid future occurrence");
        quarantined.LastRepairedAt.Should().NotBeNull();

        var normalizedQuarantine = manager.GetSchedule(invalidSettings.ScheduleId)!;
        normalizedQuarantine.Enabled.Should().BeFalse();
        normalizedQuarantine.Options.Should().NotBeNull();
        normalizedQuarantine.TargetPaths.Should().NotBeNull().And.BeEmpty();
        normalizedQuarantine.Tags.Should().NotBeNull().And.BeEmpty();
        normalizedQuarantine.LastRepairReason.Should().Contain("configuration is invalid");

        var persisted = await ReadPersistedSchedulesAsync(GetSchedulesPath(_dataRoot));
        persisted.Single(s => s.ScheduleId == legacyPreset.ScheduleId)
            .CronExpression.Should().Be("0 1 * * 0#1");
        persisted.Single(s => s.ScheduleId == custom.ScheduleId)
            .CronExpression.Should().Be("0 1 1-7 * 0");
        persisted.Single(s => s.ScheduleId == invalid.ScheduleId)
            .Enabled.Should().BeFalse();
        var persistedNormalized = persisted.Single(s => s.ScheduleId == invalidSettings.ScheduleId);
        persistedNormalized.Enabled.Should().BeFalse();
        persistedNormalized.Options.Should().NotBeNull();
        persistedNormalized.TargetPaths.Should().NotBeNull();
        persistedNormalized.Tags.Should().NotBeNull();

        var restarted = CreateManager(_dataRoot);
        restarted.GetSchedule(legacyPreset.ScheduleId)!.CronExpression.Should().Be("0 1 * * 0#1");
        restarted.GetSchedule(custom.ScheduleId)!.CronExpression.Should().Be("0 1 1-7 * 0");
        restarted.GetSchedule(invalid.ScheduleId)!.Enabled.Should().BeFalse();
        restarted.GetSchedule(invalidSettings.ScheduleId)!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task TwoManagers_ConcurrentDueClaim_CreatesOneDurableExecution()
    {
        var firstManager = CreateManager(_dataRoot);
        var schedule = await firstManager.CreateScheduleAsync(CreateSchedule("Cross-process claim"));
        var secondManager = CreateManager(_dataRoot);
        var asOf = schedule.NextExecutionAt!.Value.AddMinutes(1);

        var claims = await Task.WhenAll(
            firstManager.TryClaimDueScheduleAsync(
                schedule.ScheduleId,
                asOf,
                "manager-one",
                TimeSpan.FromMinutes(5)),
            secondManager.TryClaimDueScheduleAsync(
                schedule.ScheduleId,
                asOf,
                "manager-two",
                TimeSpan.FromMinutes(5)));

        claims.Count(claim => claim is not null).Should().Be(1);
        var winner = claims.Single(claim => claim is not null)!;
        var persisted = (await ReadPersistedSchedulesAsync(GetSchedulesPath(_dataRoot))).Single();
        persisted.PendingExecution.Should().NotBeNull();
        persisted.PendingExecution!.ExecutionId.Should().Be(winner.Execution.ExecutionId);
        persisted.Revision.Should().BeGreaterThan(schedule.Revision);
    }

    [Fact]
    public async Task TwoManagers_RevisionAwareUpdate_RejectsStaleReplacement()
    {
        var firstManager = CreateManager(_dataRoot);
        var created = await firstManager.CreateScheduleAsync(CreateSchedule("Revision guard"));
        var secondManager = CreateManager(_dataRoot);
        var stale = secondManager.GetSchedule(created.ScheduleId)!;
        await firstManager.SetScheduleEnabledAsync(created.ScheduleId, enabled: false);
        stale.Name = "Stale replacement";

        Func<Task> act = () => secondManager.UpdateScheduleAsync(stale);

        await act.Should().ThrowAsync<ArchiveMaintenanceScheduleConcurrencyException>();
        var retained = CreateManager(_dataRoot).GetSchedule(created.ScheduleId)!;
        retained.Enabled.Should().BeFalse();
        retained.Name.Should().Be("Revision guard");
    }

    [Fact]
    public async Task Restart_DispatchedClaim_ReleasesAndReusesSameExecutionIdentity()
    {
        var firstManager = CreateManager(_dataRoot);
        var schedule = await firstManager.CreateScheduleAsync(CreateSchedule("Restartable claim"));
        var asOf = schedule.NextExecutionAt!.Value.AddMinutes(1);
        var claimed = await firstManager.TryClaimDueScheduleAsync(
            schedule.ScheduleId,
            asOf,
            "manager-one",
            TimeSpan.FromMinutes(5));
        claimed.Should().NotBeNull();
        await firstManager.ReleaseExecutionForRetryAsync(
            schedule.ScheduleId,
            claimed!.Execution.ExecutionId,
            "manager-one",
            "simulated queue publication failure");

        var restarted = CreateManager(_dataRoot);
        var recovered = await restarted.TryLeasePendingExecutionAsync(
            schedule.ScheduleId,
            asOf.AddMinutes(1),
            "manager-two",
            TimeSpan.FromMinutes(5));

        recovered.Should().NotBeNull();
        recovered!.Execution.ExecutionId.Should().Be(claimed.Execution.ExecutionId);
        recovered.Execution.State.Should().Be(ArchiveMaintenanceClaimState.Dispatched);
    }

    [Fact]
    public async Task Restart_ManualClaim_PreservesEnabledScheduleCadence()
    {
        var firstManager = CreateManager(_dataRoot);
        var schedule = await firstManager.CreateScheduleAsync(CreateSchedule("Manual restart cadence"));
        var retainedNextExecution = schedule.NextExecutionAt;
        var claimed = await firstManager.TryClaimManualScheduleAsync(
            schedule.ScheduleId,
            DateTimeOffset.UtcNow,
            "manager-one",
            TimeSpan.FromMinutes(5));
        claimed.Should().NotBeNull();

        var restarted = CreateManager(_dataRoot);
        var retained = restarted.GetSchedule(schedule.ScheduleId)!;

        retained.Enabled.Should().BeTrue();
        retained.PendingExecution.Should().NotBeNull();
        retained.NextExecutionAt.Should().Be(retainedNextExecution,
            "loading a manual outbox claim must not consume the scheduled occurrence");
    }

    [Fact]
    public async Task Restart_ExpiredRunningClaim_IsQuarantinedWithoutReplay()
    {
        var firstManager = CreateManager(_dataRoot);
        var schedule = await firstManager.CreateScheduleAsync(CreateSchedule("Interrupted claim"));
        var asOf = schedule.NextExecutionAt!.Value.AddMinutes(1);
        var claimed = await firstManager.TryClaimDueScheduleAsync(
            schedule.ScheduleId,
            asOf,
            "manager-one",
            TimeSpan.FromMinutes(1));
        await firstManager.MarkExecutionRunningAsync(
            schedule.ScheduleId,
            claimed!.Execution.ExecutionId,
            asOf,
            "manager-one",
            TimeSpan.FromMinutes(1));

        var restarted = CreateManager(_dataRoot);
        var recovered = await restarted.TryLeasePendingExecutionAsync(
            schedule.ScheduleId,
            asOf.AddMinutes(2),
            "manager-two",
            TimeSpan.FromMinutes(5));

        recovered.Should().NotBeNull();
        recovered!.Execution.ExecutionId.Should().Be(claimed.Execution.ExecutionId);
        recovered.Execution.State.Should().Be(ArchiveMaintenanceClaimState.Interrupted);
        recovered.Execution.LastError.Should().Contain("outcome is ambiguous");
    }

    private static ArchiveMaintenanceScheduleManager CreateManager(string dataRoot)
    {
        return new ArchiveMaintenanceScheduleManager(
            NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
            dataRoot);
    }

    private static ArchiveMaintenanceSchedule CreateSchedule(string name)
    {
        return new ArchiveMaintenanceSchedule
        {
            Name = name,
            CronExpression = "0 3 * * *",
            TaskType = MaintenanceTaskType.HealthCheck,
            Options = new MaintenanceTaskOptions { ParallelOperations = 4 }
        };
    }

    private static async Task<List<ArchiveMaintenanceSchedule>> ReadPersistedSchedulesAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<ArchiveMaintenanceSchedule>>(json, PersistedJsonOptions)
            ?? throw new JsonException("Persisted archive schedules were null.");
    }

    private static string GetMaintenanceDirectory(string dataRoot)
    {
        return Path.Combine(dataRoot, ".maintenance");
    }

    private static string GetSchedulesPath(string dataRoot)
    {
        return Path.Combine(GetMaintenanceDirectory(dataRoot), "schedules.json");
    }
}
