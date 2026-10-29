using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;

namespace Meridian.Tests.Application.Subscriptions;

public sealed class SchedulingServiceReliabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-schedules-{Guid.NewGuid():N}");
    private string SchedulesPath => Path.Combine(_root, "schedules.json");

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("enable")]
    [InlineData("delete")]
    public async Task Mutation_PublicationFailurePropagatesAndRetainsPublishedState(string operation)
    {
        var store = Store();
        await using var service = new SchedulingService(store, SchedulesPath, null, (_, _) => Task.FromResult(1));
        var original = await service.CreateScheduleAsync(Request("original"));
        await service.ExecuteNowAsync(original.Id);
        var retained = File.ReadAllText(SchedulesPath);
        File.Move(SchedulesPath, SchedulesPath + ".retained");
        Directory.CreateDirectory(SchedulesPath);

        var failure = await Record.ExceptionAsync(async () =>
        {
            switch (operation)
            {
                case "create":
                    await service.CreateScheduleAsync(Request("new"));
                    break;
                case "update":
                    await service.UpdateScheduleAsync(original.Id, Request("changed"));
                    break;
                case "enable":
                    await service.SetScheduleEnabledAsync(original.Id, false);
                    break;
                case "delete":
                    await service.DeleteScheduleAsync(original.Id);
                    break;
            }
        });

        failure.Should().NotBeNull();
        service.GetAllSchedules().Should().ContainSingle().Which.Should().Be(original);
        service.GetExecutionStatus(original.Id).Should().NotBeNull();
        File.ReadAllText(SchedulesPath + ".retained").Should().Be(retained);

        Directory.Delete(SchedulesPath);
        File.Move(SchedulesPath + ".retained", SchedulesPath);
        await using var restarted = new SchedulingService(store, SchedulesPath);
        await restarted.InitializeAsync();
        restarted.GetAllSchedules().Should().ContainSingle().Which.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task ConcurrentMutations_PreserveEveryCommittedScheduleAcrossRestart()
    {
        var store = Store();
        await using var service = new SchedulingService(store, SchedulesPath);

        var created = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(index => service.CreateScheduleAsync(Request($"schedule-{index}"))));

        service.GetAllSchedules().Should().BeEquivalentTo(created);
        await using var restarted = new SchedulingService(store, SchedulesPath);
        await restarted.InitializeAsync();
        restarted.GetAllSchedules().Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task CancelledMutation_DoesNotChangeMemoryOrPersistence()
    {
        await using var service = new SchedulingService(Store(), SchedulesPath);
        var original = await service.CreateScheduleAsync(Request("original"));
        var retained = File.ReadAllText(SchedulesPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteScheduleAsync(original.Id, cts.Token));

        service.GetSchedule(original.Id).Should().Be(original);
        File.ReadAllText(SchedulesPath).Should().Be(retained);
    }

    [Fact]
    public async Task TimerAndManualRequests_ShareOneExecutionPerSchedule()
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        await using var service = new SchedulingService(Store(), SchedulesPath, null, async (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        });
        var schedule = await service.CreateScheduleAsync(Request("scheduled"));
        try
        {
            service.CheckSchedules(DateTimeOffset.UtcNow);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var manual = service.ExecuteNowAsync(schedule.Id);
            service.CheckSchedules(DateTimeOffset.UtcNow);
            calls.Should().Be(1);

            release.TrySetResult();
            (await manual.WaitAsync(TimeSpan.FromSeconds(5))).LastRunSuccess.Should().BeTrue();
            service.CheckSchedules(DateTimeOffset.UtcNow);
            calls.Should().Be(1, "the completed scheduled occurrence must remain suppressed");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task CancellingJoinedCaller_DoesNotCancelTheOriginalExecution()
    {
        var entered = Signal();
        var release = Signal();
        await using var service = new SchedulingService(Store(), SchedulesPath, null, async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        });
        var schedule = await service.CreateScheduleAsync(Request("manual"));
        var original = service.ExecuteNowAsync(schedule.Id);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            var joined = service.ExecuteNowAsync(schedule.Id, cts.Token);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joined);
            original.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            (await original.WaitAsync(TimeSpan.FromSeconds(5))).LastRunSuccess.Should().BeTrue();
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task DifferentSchedules_CanExecuteConcurrently()
    {
        var bothEntered = Signal();
        var release = Signal();
        var calls = 0;
        await using var service = new SchedulingService(Store(), SchedulesPath, null, async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
                bothEntered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        });
        var first = await service.CreateScheduleAsync(Request("first"));
        var second = await service.CreateScheduleAsync(Request("second"));
        try
        {
            var runs = Task.WhenAll(service.ExecuteNowAsync(first.Id), service.ExecuteNowAsync(second.Id));
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.TrySetResult();
            (await runs.WaitAsync(TimeSpan.FromSeconds(5))).Should().OnlyContain(status => status.LastRunSuccess);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task Dispose_CancelsAndJoinsExecutionsAndRejectsFurtherWork()
    {
        var entered = Signal();
        var cancelled = Signal();
        var releaseCleanup = Signal();
        await using var service = new SchedulingService(Store(), SchedulesPath, null, async (_, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return 1;
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await releaseCleanup.Task;
                throw;
            }
        });
        var schedule = await service.CreateScheduleAsync(Request("shutdown"));
        var running = service.ExecuteNowAsync(schedule.Id);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var firstDispose = service.DisposeAsync().AsTask();
            var secondDispose = service.DisposeAsync().AsTask();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstDispose.IsCompleted.Should().BeFalse();

            releaseCleanup.TrySetResult();
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ExecuteNowAsync(schedule.Id));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.CreateScheduleAsync(Request("late")));
            service.GetExecutionStatus(schedule.Id).Should().BeNull();
        }
        finally
        {
            releaseCleanup.TrySetResult();
        }
    }

    [Fact]
    public async Task Execution_UpdatesTheRealConfigurationStore()
    {
        var store = Store();
        await using var service = new SchedulingService(store, SchedulesPath);
        var schedule = await service.CreateScheduleAsync(Request("enable"));

        var result = await service.ExecuteNowAsync(schedule.Id);

        result.LastRunSuccess.Should().BeTrue();
        result.SymbolsAffected.Should().Be(1);
        store.Load().Symbols.Should().ContainSingle(symbol => symbol.Symbol == "AAPL" && symbol.SubscribeTrades);
    }

    private ConfigStore Store()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        if (!File.Exists(path))
            File.WriteAllText(path, "{}");
        return new ConfigStore(path);
    }

    private static CreateScheduleRequest Request(string name)
        => new(name, ["AAPL"], ScheduleAction.Enable,
            new ScheduleTiming(ScheduleType.Daily, DateTimeOffset.UtcNow.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
