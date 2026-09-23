using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ActivityFeedService"/> — activity logging, filtering,
/// event notification, and model behavior.
/// </summary>
public sealed class ActivityFeedServiceTests
{
    private static ActivityFeedService CreateService() => ActivityFeedService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = ActivityFeedService.Instance;
        var b = ActivityFeedService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Constructor_UsesResolvedDataRootForActivityLogPath()
    {
        using var fixture = new PathFixture("mdc-activity-path");
        var service = new ActivityFeedService(new FixedConfigService(
            fixture.ConfigPath,
            new AppConfigDto { DataRoot = "retained-data" }));

        var path = GetPrivateField<string>(service, "_activityLogPath");

        path.Should().Be(Path.Combine(fixture.RootPath, "retained-data", "_logs", "activity_log.json"));
    }

    [Fact]
    public void Constructor_UsesConfigFileWithoutInvokingAsyncLoad()
    {
        using var fixture = new PathFixture("mdc-activity-file");
        File.WriteAllText(
            fixture.ConfigPath,
            """
            {
              "dataRoot": "retained-data"
            }
            """);

        var configService = new ThrowingLoadConfigService(fixture.ConfigPath);
        var service = new ActivityFeedService(configService);

        var path = GetPrivateField<string>(service, "_activityLogPath");

        configService.LoadConfigCallCount.Should().Be(0);
        path.Should().Be(Path.Combine(fixture.RootPath, "retained-data", "_logs", "activity_log.json"));
    }

    // ── AddActivity ──────────────────────────────────────────────────

    [Fact]
    public void AddActivity_ShouldAssignIdWhenNull()
    {
        var svc = CreateService();
        var item = new ActivityItem
        {
            Title = "TestAutoId-" + Guid.NewGuid()
        };

        svc.AddActivity(item);

        item.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddActivity_ShouldPreserveExplicitId()
    {
        var svc = CreateService();
        var explicitId = Guid.NewGuid().ToString();
        var item = new ActivityItem
        {
            Id = explicitId,
            Title = "ExplicitId-" + Guid.NewGuid()
        };

        svc.AddActivity(item);

        item.Id.Should().Be(explicitId);
    }

    [Fact]
    public void AddActivity_ShouldAssignTimestampWhenDefault()
    {
        var svc = CreateService();
        var before = DateTime.UtcNow;
        var item = new ActivityItem
        {
            Title = "TimestampDefault-" + Guid.NewGuid(),
            Timestamp = default
        };

        svc.AddActivity(item);

        item.Timestamp.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void AddActivity_ShouldRaiseActivityAddedEvent()
    {
        var svc = CreateService();
        ActivityItem? received = null;
        svc.ActivityAdded += (_, args) => received = args;

        var item = new ActivityItem
        {
            Title = "EventTest-" + Guid.NewGuid(),
            Type = ActivityType.Info
        };
        svc.AddActivity(item);

        received.Should().NotBeNull();
        received!.Title.Should().Be(item.Title);
    }

    [Fact]
    public void AddActivity_ShouldAppearInActivitiesCollection()
    {
        var svc = CreateService();
        var title = "CollectionTest-" + Guid.NewGuid();
        var item = new ActivityItem
        {
            Title = title,
            Type = ActivityType.Info
        };

        svc.AddActivity(item);

        svc.Activities.Should().Contain(a => a.Title == title);
    }

    // ── LogActivityAsync ─────────────────────────────────────────────

    [Fact]
    public async Task LogActivityAsync_ShouldAddActivityWithCorrectType()
    {
        var svc = CreateService();
        var title = "LogAsync-" + Guid.NewGuid();

        await svc.LogActivityAsync(ActivityType.SymbolAdded, title, symbol: "SPY");

        svc.Activities.Should().Contain(a =>
            a.Title == title && a.Type == ActivityType.SymbolAdded && a.Symbol == "SPY");
    }

    [Fact]
    public async Task LogActivityAsync_ShouldPopulateMetadata()
    {
        var svc = CreateService();
        var title = "MetadataTest-" + Guid.NewGuid();
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        await svc.LogActivityAsync(ActivityType.Info, title, metadata: metadata);

        var activity = svc.Activities.FirstOrDefault(a => a.Title == title);
        activity.Should().NotBeNull();
        activity!.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public async Task MergeLoadedActivities_ShouldPreserveNewerInMemoryActivities()
    {
        using var fixture = new PathFixture("mdc-activity-merge");
        var svc = new ActivityFeedService(new FixedConfigService(fixture.ConfigPath, null));
        var inMemoryTitle = "InMemory-" + Guid.NewGuid();

        await svc.LogActivityAsync(ActivityType.Info, inMemoryTitle);

        var persistedItems = Enumerable.Range(0, 100)
            .Select(index => new ActivityItem
            {
                Id = $"persisted-{index}",
                Title = $"Persisted-{index}",
                Type = ActivityType.Info,
                Timestamp = DateTime.UtcNow.AddMinutes(-(index + 1))
            });

        svc.MergeLoadedActivities(persistedItems);

        svc.Activities.Should().HaveCount(100);
        svc.Activities.First().Title.Should().Be(inMemoryTitle);
        svc.Activities.Should().Contain(activity => activity.Title == inMemoryTitle);
    }

    // ── Convenience Logging ──────────────────────────────────────────

    [Fact]
    public async Task LogCollectorStatusAsync_Connected_ShouldLogStartEvent()
    {
        var svc = CreateService();

        await svc.LogCollectorStatusAsync(true, "Alpaca");

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.CollectorStarted && a.Provider == "Alpaca");
    }

    [Fact]
    public async Task LogCollectorStatusAsync_Disconnected_ShouldLogStopEvent()
    {
        var svc = CreateService();

        await svc.LogCollectorStatusAsync(false);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.CollectorStopped);
    }

    [Fact]
    public async Task LogBackfillAsync_Success_ShouldLogCompletedEvent()
    {
        var svc = CreateService();

        await svc.LogBackfillAsync(new[] { "SPY", "AAPL" }, "Stooq", true, 5000);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.BackfillCompleted && a.Provider == "Stooq");
    }

    [Fact]
    public async Task LogBackfillAsync_Failure_ShouldLogFailedEvent()
    {
        var svc = CreateService();

        await svc.LogBackfillAsync(new[] { "INVALID" }, "Stooq", false, 0);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.BackfillFailed);
    }

    [Fact]
    public async Task LogSymbolChangeAsync_Added_ShouldLogSymbolAdded()
    {
        var svc = CreateService();

        await svc.LogSymbolChangeAsync("TSLA", true);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.SymbolAdded && a.Symbol == "TSLA");
    }

    [Fact]
    public async Task LogSymbolChangeAsync_Removed_ShouldLogSymbolRemoved()
    {
        var svc = CreateService();

        await svc.LogSymbolChangeAsync("TSLA", false);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.SymbolRemoved && a.Symbol == "TSLA");
    }

    [Fact]
    public async Task LogDataQualityEventAsync_ShouldLogQualityIssue()
    {
        var svc = CreateService();

        await svc.LogDataQualityEventAsync("SPY", "Gap detected", "Warning");

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.DataQualityIssue && a.Symbol == "SPY");
    }

    [Fact]
    public async Task LogStorageEventAsync_ShouldLogStorageEvent()
    {
        var svc = CreateService();

        await svc.LogStorageEventAsync("Archive completed", 1024);

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.StorageEvent);
    }

    [Fact]
    public async Task LogProviderConnectionAsync_Connected_ShouldLogConnectedEvent()
    {
        var svc = CreateService();

        await svc.LogProviderConnectionAsync("Polygon", true, "Connection established");

        svc.Activities.Should().Contain(a =>
            a.Type == ActivityType.ProviderConnected && a.Provider == "Polygon");
    }

    // ── Filtering ────────────────────────────────────────────────────

    [Fact]
    public async Task GetActivitiesByType_ShouldFilterCorrectly()
    {
        var svc = CreateService();
        await svc.LogActivityAsync(ActivityType.ExportCompleted, "FilterTypeTest-" + Guid.NewGuid());

        var exports = svc.GetActivitiesByType(ActivityType.ExportCompleted);

        exports.Should().OnlyContain(a => a.Type == ActivityType.ExportCompleted);
    }

    [Fact]
    public async Task GetActivitiesForSymbol_ShouldFilterCaseInsensitive()
    {
        var svc = CreateService();
        var uniqueSymbol = "FILTER" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        await svc.LogActivityAsync(ActivityType.SymbolAdded, "SymFilter-" + Guid.NewGuid(), symbol: uniqueSymbol);

        var results = svc.GetActivitiesForSymbol(uniqueSymbol.ToLowerInvariant());

        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetActivitiesSince_ShouldFilterByTime()
    {
        var svc = CreateService();
        var cutoff = DateTime.UtcNow.AddSeconds(-1);
        await svc.LogActivityAsync(ActivityType.Info, "TimeFilter-" + Guid.NewGuid());

        var results = svc.GetActivitiesSince(cutoff);

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(a => a.Timestamp >= cutoff);
    }

    // ── ActivityItem Model ───────────────────────────────────────────

    [Fact]
    public void ActivityItem_ShouldHaveDefaultValues()
    {
        var item = new ActivityItem();
        item.Id.Should().BeEmpty();
        item.Title.Should().BeEmpty();
        item.Description.Should().BeNull();
        item.Symbol.Should().BeNull();
        item.Provider.Should().BeNull();
        item.Metadata.Should().BeNull();
    }

    [Theory]
    [InlineData(ActivityType.CollectorStarted, "Success")]
    [InlineData(ActivityType.CollectorStopped, "Neutral")]
    [InlineData(ActivityType.ProviderError, "Error")]
    [InlineData(ActivityType.DataQualityIssue, "Warning")]
    [InlineData(ActivityType.StorageEvent, "Info")]
    public void ActivityItem_ColorCategory_ShouldReturnCorrectCategory(ActivityType type, string expected)
    {
        var item = new ActivityItem { Type = type };
        item.ColorCategory.Should().Be(expected);
    }

    [Fact]
    public void ActivityItem_Icon_ShouldReturnNonEmptyString()
    {
        foreach (var type in Enum.GetValues<ActivityType>())
        {
            var item = new ActivityItem { Type = type };
            item.Icon.Should().NotBeNullOrEmpty($"Icon for {type} should not be empty");
        }
    }

    [Fact]
    public void ActivityItem_RelativeTime_JustNow_ShouldReturnJustNow()
    {
        var item = new ActivityItem { Timestamp = DateTime.UtcNow };
        item.RelativeTime.Should().Be("Just now");
    }

    [Fact]
    public void ActivityItem_RelativeTime_MinutesAgo_ShouldReturnMinuteFormat()
    {
        var item = new ActivityItem { Timestamp = DateTime.UtcNow.AddMinutes(-5) };
        item.RelativeTime.Should().EndWith("m ago");
    }

    [Fact]
    public void ActivityItem_RelativeTime_HoursAgo_ShouldReturnHourFormat()
    {
        var item = new ActivityItem { Timestamp = DateTime.UtcNow.AddHours(-3) };
        item.RelativeTime.Should().EndWith("h ago");
    }

    [Fact]
    public void ActivityItem_RelativeTime_DaysAgo_ShouldReturnDayFormat()
    {
        var item = new ActivityItem { Timestamp = DateTime.UtcNow.AddDays(-2) };
        item.RelativeTime.Should().EndWith("d ago");
    }

    [Fact]
    public void ActivityItem_RelativeTime_OlderThan7Days_ShouldReturnDateFormat()
    {
        var item = new ActivityItem { Timestamp = DateTime.UtcNow.AddDays(-10) };
        item.RelativeTime.Should().MatchRegex(@"[A-Z][a-z]{2} \d+");
    }

    // ── ActivityType Enum ────────────────────────────────────────────

    [Fact]
    public void ActivityType_ShouldHaveExpectedValues()
    {
        Enum.GetValues<ActivityType>().Should().HaveCountGreaterThanOrEqualTo(20);
    }

    [Theory]
    [InlineData(ActivityType.CollectorStarted)]
    [InlineData(ActivityType.BackfillCompleted)]
    [InlineData(ActivityType.DataQualityIssue)]
    [InlineData(ActivityType.ExportCompleted)]
    [InlineData(ActivityType.StorageEvent)]
    public void ActivityType_AllValues_ShouldBeDefined(ActivityType type)
    {
        Enum.IsDefined(typeof(ActivityType), type).Should().BeTrue();
    }

    // ── AddServerEventIfNew ──────────────────────────────────────────

    [Fact]
    public void AddServerEventIfNew_NewItem_ShouldReturnTrueAndAddToFeed()
    {
        var svc = CreateService();
        var id = "server:new-" + Guid.NewGuid();
        var item = new ActivityItem { Id = id, Title = "Server Error", Type = ActivityType.DataQualityIssue };

        var added = svc.AddServerEventIfNew(item);

        added.Should().BeTrue();
        svc.Activities.Should().Contain(a => a.Id == id);
    }

    [Fact]
    public void AddServerEventIfNew_DuplicateId_ShouldReturnFalseAndNotDuplicate()
    {
        var svc = CreateService();
        var id = "server:dup-" + Guid.NewGuid();
        var item1 = new ActivityItem { Id = id, Title = "First" };
        var item2 = new ActivityItem { Id = id, Title = "Second" };

        svc.AddServerEventIfNew(item1);
        var addedDup = svc.AddServerEventIfNew(item2);

        addedDup.Should().BeFalse();
        svc.Activities.Count(a => a.Id == id).Should().Be(1);
    }

    [Fact]
    public void AddServerEventIfNew_ItemWithoutId_ShouldAssignIdAndAdd()
    {
        var svc = CreateService();
        var item = new ActivityItem { Title = "NoId-" + Guid.NewGuid() };

        var added = svc.AddServerEventIfNew(item);

        added.Should().BeTrue();
        item.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddServerEventIfNew_ShouldRaiseActivityAddedEvent()
    {
        var svc = CreateService();
        ActivityItem? received = null;
        svc.ActivityAdded += (_, args) => received = args;
        var id = "server:evt-" + Guid.NewGuid();
        var item = new ActivityItem { Id = id, Title = "Event" };

        svc.AddServerEventIfNew(item);

        received.Should().NotBeNull();
        received!.Id.Should().Be(id);
    }

    [Fact]
    public async Task MergeLoadedActivities_ShouldSeedServerEventDeduplication()
    {
        using var fixture = new PathFixture("mdc-activity-server-seed");
        const string id = "server:loaded-event";
        var dataRoot = Path.Combine(fixture.RootPath, "retained-data");
        var activityLogDirectory = Path.Combine(dataRoot, "_logs");
        Directory.CreateDirectory(activityLogDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(activityLogDirectory, "activity_log.json"),
            JsonSerializer.Serialize(
                new[]
                {
                    new ActivityItem
                    {
                        Id = id,
                        Title = "Persisted server event",
                        Type = ActivityType.DataQualityIssue
                    }
                },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var svc = new ActivityFeedService(new FixedConfigService(
            fixture.ConfigPath,
            new AppConfigDto { DataRoot = "retained-data" }));

        await WaitForActivityAsync(svc, id);

        var duplicateAdded = svc.AddServerEventIfNew(new ActivityItem
        {
            Id = id,
            Title = "Duplicate server event",
            Type = ActivityType.DataQualityIssue
        });

        duplicateAdded.Should().BeFalse();
        svc.Activities.Count(activity => activity.Id == id).Should().Be(1);
    }

    [Fact]
    public async Task AddServerEventIfNew_ConcurrentDuplicateIds_ShouldAddExactlyOnce()
    {
        using var fixture = new PathFixture("mdc-activity-concurrent-dedup");
        await using var svc = new ActivityFeedService(
            new FixedConfigService(fixture.ConfigPath, null),
            (_, _, _) => Task.CompletedTask);
        await svc.Initialization;
        var id = "server:concurrent-" + Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            svc.AddServerEventIfNew(new ActivityItem
            {
                Id = id,
                Title = $"Concurrent event {index}",
                Type = ActivityType.Warning
            }))));

        results.Should().ContainSingle(added => added);
        svc.Activities.Count(activity => activity.Id == id).Should().Be(1);
    }

    [Fact]
    public async Task PersistenceWorker_ConcurrentLogs_ShouldSerializeWritesAndCommitLatestSnapshot()
    {
        using var fixture = new PathFixture("mdc-activity-ordered-writes");
        SeedEmptyPrimaryActivityLog(fixture);
        var payloads = new ConcurrentQueue<string>();
        var activeWriters = 0;
        var maximumActiveWriters = 0;

        async Task PersistAsync(string _, string payload, CancellationToken ct)
        {
            var active = Interlocked.Increment(ref activeWriters);
            UpdateMaximum(ref maximumActiveWriters, active);
            try
            {
                await Task.Delay(5, ct);
                payloads.Enqueue(payload);
            }
            finally
            {
                Interlocked.Decrement(ref activeWriters);
            }
        }

        await using var svc = new ActivityFeedService(
            new FixedConfigService(fixture.ConfigPath, null),
            PersistAsync);
        await svc.Initialization;

        var titles = Enumerable.Range(0, 32)
            .Select(index => $"ordered-{index}-{Guid.NewGuid():N}")
            .ToArray();
        await Task.WhenAll(titles.Select(title =>
            svc.LogActivityAsync(ActivityType.Info, title)));

        maximumActiveWriters.Should().Be(1);
        payloads.Count.Should().BeInRange(1, titles.Length);
        var latest = JsonSerializer.Deserialize<List<ActivityItem>>(
            payloads.Last(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        latest.Should().NotBeNull();
        latest!.Select(activity => activity.Title).Should().Contain(titles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceWorker_SlowStorageCoalescesPendingWorkAndCompletesAllWaiters(bool failPending)
    {
        using var fixture = new PathFixture("mdc-activity-coalescing");
        SeedEmptyPrimaryActivityLog(fixture);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        string? latest = null;
        async Task PersistAsync(string _, string json, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref writes);
            if (attempt == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            if (attempt == 2 && failPending)
                throw new IOException("pending snapshot failed");
            latest = json;
        }
        await using var service = new ActivityFeedService(new FixedConfigService(fixture.ConfigPath, null), PersistAsync);
        await service.Initialization;
        service.AddActivity(new ActivityItem { Title = "first" });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            for (var i = 0; i < 1000; i++)
                service.AddActivity(new ActivityItem { Title = $"sync-{i}" });
            var waiters = Enumerable.Range(0, 20).Select(i => service.LogActivityAsync(ActivityType.Info, $"awaited-{i}")).ToArray();
            waiters.Should().OnlyContain(task => !task.IsCompleted);
            release.TrySetResult();
            if (failPending)
            {
                foreach (var waiter in waiters)
                    await Assert.ThrowsAsync<IOException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10));
                latest.Should().Contain("sync-999").And.Contain("awaited-19");
            }
            writes.Should().Be(2);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task LogActivityAsync_PersistenceFailure_ShouldReachCallerAndFailureEvent()
    {
        using var fixture = new PathFixture("mdc-activity-write-failure");
        SeedEmptyPrimaryActivityLog(fixture);
        var writeAttempt = 0;
        ActivityFeedPersistenceFailedEventArgs? failure = null;

        Task PersistAsync(string _, string __, CancellationToken ___)
        {
            if (Interlocked.Increment(ref writeAttempt) == 1)
            {
                throw new IOException("activity persistence unavailable");
            }

            return Task.CompletedTask;
        }

        await using var svc = new ActivityFeedService(
            new FixedConfigService(fixture.ConfigPath, null),
            PersistAsync);
        svc.PersistenceFailed += (_, args) => failure = args;
        await svc.Initialization;

        var act = () => svc.LogActivityAsync(ActivityType.Warning, "Durability failure");

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("activity persistence unavailable");
        svc.LastPersistenceError.Should().BeOfType<IOException>();
        failure.Should().NotBeNull();
        failure!.Path.Should().EndWith("activity_log.json");
        failure.Exception.Should().BeSameAs(svc.LastPersistenceError);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDrainQueuedSynchronousActivity()
    {
        using var fixture = new PathFixture("mdc-activity-dispose-drain");
        SeedEmptyPrimaryActivityLog(fixture);
        var writeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloads = new ConcurrentQueue<string>();

        async Task PersistAsync(string _, string payload, CancellationToken ct)
        {
            writeEntered.TrySetResult();
            await releaseWrite.Task.WaitAsync(ct);
            payloads.Enqueue(payload);
        }

        var svc = new ActivityFeedService(
            new FixedConfigService(fixture.ConfigPath, null),
            PersistAsync);
        await svc.Initialization;
        var title = "drained-" + Guid.NewGuid().ToString("N");
        svc.AddActivity(new ActivityItem { Title = title });
        await writeEntered.Task;

        var disposeTask = svc.DisposeAsync().AsTask();
        disposeTask.IsCompleted.Should().BeFalse();
        releaseWrite.TrySetResult();
        await disposeTask;

        var latest = JsonSerializer.Deserialize<List<ActivityItem>>(
            payloads.Last(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        latest.Should().ContainSingle(activity => activity.Title == title);
    }

    private static void SeedEmptyPrimaryActivityLog(PathFixture fixture)
    {
        var activityLogDirectory = Path.Combine(fixture.RootPath, "data", "_logs");
        Directory.CreateDirectory(activityLogDirectory);
        // Presence prevents process-wide legacy fallback; JSON null avoids an initialization
        // persistence request so each custom writer's first invocation remains test-owned.
        File.WriteAllText(Path.Combine(activityLogDirectory, "activity_log.json"), "null");
    }

    private static async Task WaitForActivityAsync(ActivityFeedService service, string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (service.Activities.Any(activity => activity.Id == id))
            {
                return;
            }

            await Task.Delay(25);
        }

        service.Activities.Should().Contain(activity => activity.Id == id);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
            {
                return;
            }
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull();
        return (T)field!.GetValue(instance)!;
    }
}
