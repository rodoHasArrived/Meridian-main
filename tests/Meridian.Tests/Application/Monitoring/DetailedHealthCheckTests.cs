using System.Reflection;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Microsoft.Extensions.Time.Testing;

namespace Meridian.Tests.Application.Monitoring;

public sealed class DetailedHealthCheckTests
{
    [Theory]
    [InlineData(500, DetailedHealthStatus.Healthy)]
    [InlineData(501, DetailedHealthStatus.Degraded)]
    [InlineData(1000, DetailedHealthStatus.Degraded)]
    [InlineData(1001, DetailedHealthStatus.Unhealthy)]
    public async Task LatencyThresholdsContributeToOverallStatus(double p99, DetailedHealthStatus expected)
    {
        using var health = new DetailedHealthCheck(new() { CheckIntervalSeconds = 3600 });
        health.RegisterProviders(latencyStats: () => new LatencyStatistics(1, 100, 1, 1, 1, p99, "SPY", "SPY", [], DateTimeOffset.UtcNow));

        var report = await health.GetDetailedHealthAsync();

        report.Status.Should().Be(expected);
        report.Checks.Should().ContainSingle(check => check.Status == expected);
    }

    [Fact]
    public async Task DependencyRecoveryReportsElapsedDowntimeBeforeResettingState()
    {
        var clock = new FakeTimeProvider();
        using var health = new DetailedHealthCheck(new() { CheckIntervalSeconds = 3600 }, clock);
        var healthy = false;
        health.RegisterDependency("provider", "HTTP", _ => Task.FromResult(healthy));
        DependencyRecoveredEvent? recovered = null;
        health.OnDependencyRecovered += value => recovered = value;
        (await health.GetDetailedHealthAsync()).Status.Should().Be(DetailedHealthStatus.Unhealthy);

        clock.Advance(TimeSpan.FromMinutes(7));
        healthy = true;
        (await health.GetDetailedHealthAsync()).Status.Should().Be(DetailedHealthStatus.Healthy);

        recovered.Should().NotBeNull();
        recovered!.Value.DowntimeDuration.Should().Be(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task ReportVersionComesFromTheBuiltAssembly()
    {
        using var health = new DetailedHealthCheck(new() { CheckIntervalSeconds = 3600 });
        var expected = typeof(DetailedHealthCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(DetailedHealthCheck).Assembly.GetName().Version?.ToString() ?? "unknown";

        (await health.GetDetailedHealthAsync()).Version.Should().Be(expected);
    }

}
