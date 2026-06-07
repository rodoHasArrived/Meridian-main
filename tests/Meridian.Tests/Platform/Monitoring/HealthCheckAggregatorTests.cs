using FluentAssertions;
using Meridian.Core.Monitoring.Core;
using Meridian.Platform.Monitoring.Core;

namespace Meridian.Tests.Platform.Monitoring;

public sealed class HealthCheckAggregatorTests
{
    [Fact]
    public async Task CheckAllAsync_WithNoProviders_ReturnsUnknown()
    {
        var aggregator = new HealthCheckAggregator();

        var report = await aggregator.CheckAllAsync();

        report.OverallSeverity.Should().Be(HealthSeverity.Unknown);
        report.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAllAsync_UsesWorstProviderSeverity()
    {
        var aggregator = new HealthCheckAggregator();
        aggregator.Register(new StubHealthCheckProvider(
            "storage",
            HealthCheckResult.Healthy("storage", "ok", TimeSpan.Zero)));
        aggregator.Register(new StubHealthCheckProvider(
            "provider",
            HealthCheckResult.Unhealthy("provider", "offline", TimeSpan.Zero)));

        var report = await aggregator.CheckAllAsync();

        report.OverallSeverity.Should().Be(HealthSeverity.Unhealthy);
        report.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckAsync_WhenProviderTimesOut_ReturnsUnhealthy()
    {
        var aggregator = new HealthCheckAggregator(TimeSpan.FromMilliseconds(10));
        aggregator.Register(new BlockingHealthCheckProvider("blocked"));

        var result = await aggregator.CheckAsync("blocked");

        result.Should().NotBeNull();
        result!.Severity.Should().Be(HealthSeverity.Unhealthy);
        result.Message.Should().Contain("timed out");
    }

    private sealed class StubHealthCheckProvider : IHealthCheckProvider
    {
        private readonly HealthCheckResult _result;

        public StubHealthCheckProvider(string componentName, HealthCheckResult result)
        {
            ComponentName = componentName;
            _result = result;
        }

        public string ComponentName { get; }

        public Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class BlockingHealthCheckProvider : IHealthCheckProvider
    {
        public BlockingHealthCheckProvider(string componentName)
        {
            ComponentName = componentName;
        }

        public string ComponentName { get; }

        public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            return HealthCheckResult.Healthy(ComponentName, "unexpected", TimeSpan.Zero);
        }
    }
}
