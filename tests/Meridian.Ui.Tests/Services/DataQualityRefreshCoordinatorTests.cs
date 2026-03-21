using FluentAssertions;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class DataQualityRefreshCoordinatorTests
{
    [Fact]
    public async Task StartAsync_ShouldRunInitialRefresh_AndStartScheduler()
    {
        var scheduler = new FakeRefreshScheduler();
        var calls = 0;
        var sut = new DataQualityRefreshCoordinator(
            scheduler,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await sut.StartAsync(TimeSpan.FromSeconds(5));

        calls.Should().Be(1);
        scheduler.StartCalls.Should().Be(1);
        sut.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_ShouldNotScheduleTwice()
    {
        var scheduler = new FakeRefreshScheduler();
        var calls = 0;
        var sut = new DataQualityRefreshCoordinator(
            scheduler,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await sut.StartAsync(TimeSpan.FromSeconds(5));
        await sut.StartAsync(TimeSpan.FromSeconds(5));

        calls.Should().Be(1);
        scheduler.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_ShouldInvokeRefreshWithoutScheduler()
    {
        var scheduler = new FakeRefreshScheduler();
        var calls = 0;
        var sut = new DataQualityRefreshCoordinator(
            scheduler,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await sut.RefreshAsync();

        calls.Should().Be(1);
        scheduler.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ScheduledCallback_ShouldUseWrappedRefreshLogic()
    {
        var scheduler = new FakeRefreshScheduler();
        var calls = 0;
        var sut = new DataQualityRefreshCoordinator(
            scheduler,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await sut.StartAsync(TimeSpan.FromSeconds(5));
        await scheduler.TriggerAsync();

        calls.Should().Be(2);
    }

    [Fact]
    public async Task RefreshFailure_ShouldFlowToErrorHandler()
    {
        var scheduler = new FakeRefreshScheduler();
        var failures = new List<Exception>();
        var expected = new InvalidOperationException("boom");
        var sut = new DataQualityRefreshCoordinator(
            scheduler,
            _ => Task.FromException(expected),
            failures.Add);

        await sut.RefreshAsync();

        failures.Should().ContainSingle()
            .Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Stop_ShouldStopScheduler()
    {
        var scheduler = new FakeRefreshScheduler();
        var sut = new DataQualityRefreshCoordinator(scheduler, _ => Task.CompletedTask);
        await sut.StartAsync(TimeSpan.FromSeconds(5));

        sut.Stop();

        scheduler.StopCalls.Should().Be(1);
        sut.IsStarted.Should().BeFalse();
    }

    private sealed class FakeRefreshScheduler : IRefreshScheduler
    {
        private Func<CancellationToken, Task>? _callback;

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start(TimeSpan interval, Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            _callback = callback;
        }

        public void Stop()
        {
            StopCalls++;
            _callback = null;
        }

        public void Dispose()
        {
            Stop();
        }

        public Task TriggerAsync(CancellationToken cancellationToken = default)
            => _callback is null ? Task.CompletedTask : _callback(cancellationToken);
    }
}
