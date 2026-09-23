using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Application.Composition;

public sealed class TenantPostureHostedServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinalPosture_GatesConstructionAndEntireWorkerLifecycle(bool strict)
    {
        var counts = new WorkerCounts();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(counts);
        services.AddSingleton(TenantScopeEnforcementOptions.DeploymentBoundary);
        services.AddHostedService<TenantPostureHostedService<RecordingWorker>>();
        // Host customization occurs after the worker is registered, including factory options.
        services.AddSingleton(_ => strict ? TenantScopeEnforcementOptions.FailClosed : TenantScopeEnforcementOptions.DeploymentBoundary);
        await using (var provider = services.BuildServiceProvider())
        {
            var hosted = provider.GetRequiredService<IHostedService>();
            counts.Constructed.Should().Be(0);
            await hosted.StartAsync(CancellationToken.None);
            await hosted.StopAsync(CancellationToken.None);
            counts.Constructed.Should().Be(strict ? 0 : 1);
            counts.Started.Should().Be(strict ? 0 : 1);
            counts.Stopped.Should().Be(strict ? 0 : 1);
        }
        counts.Disposed.Should().Be(strict ? 0 : 1);
    }

    [Fact]
    public async Task WorkerFailure_RemainsVisibleToTheHostBackgroundMonitor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TenantScopeEnforcementOptions.DeploymentBoundary);
        services.AddHostedService<TenantPostureHostedService<FailingWorker>>();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();
        await hosted.StartAsync(CancellationToken.None);
        var monitored = hosted.Should().BeAssignableTo<BackgroundService>().Subject;
        var failure = () => monitored.ExecuteTask!;
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("worker failure");
        await hosted.StopAsync(CancellationToken.None);
    }

    public sealed class FailingWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken ct)
            => Task.FromException(new InvalidOperationException("worker failure"));
    }

    public sealed class WorkerCounts
    {
        public int Constructed;
        public int Started;
        public int Stopped;
        public int Disposed;
    }

    public sealed class RecordingWorker : BackgroundService
    {
        private readonly WorkerCounts _counts;
        public RecordingWorker(WorkerCounts counts) { _counts = counts; counts.Constructed++; }
        public override Task StartAsync(CancellationToken ct) { _counts.Started++; return base.StartAsync(ct); }
        public override Task StopAsync(CancellationToken ct) { _counts.Stopped++; return base.StopAsync(ct); }
        protected override Task ExecuteAsync(CancellationToken ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);
        public override void Dispose() { _counts.Disposed++; base.Dispose(); }
    }
}
