using FluentAssertions;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ReportingDeliveryReadinessRaceTests
{
    [Theory]
    [InlineData("ready", false)]
    [InlineData("starting", false)]
    [InlineData("failed", true)]
    [InlineData("stopped", true)]
    [InlineData("stale", true)]
    [InlineData("ready-invalid-options", true)]
    [InlineData("starting-invalid-options", true)]
    public void ScheduleGate_UsesOneDeliveryObservationAfterResolvingDependencies(
        string transition,
        bool expectDeliveryBlocker)
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var delivery = new ReportingDeliveryWorkerReadinessState();
        delivery.MarkStarting();
        var services = new ServiceCollection();
        services.AddSingleton(delivery);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddSingleton(SecureReportingDistributionOptions.Default with
        {
            WorkerId = "readiness-race-test",
            WorkerPollInterval = transition.EndsWith("invalid-options", StringComparison.Ordinal)
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromSeconds(1)
        });
        var transitions = 0;
        services.AddSingleton<IReportingRecipientDestinationResolver>(_ =>
        {
            // Evaluation resolves this dependency after its original delivery-health read.
            // Force the first-cycle transition at that boundary without scheduling or sleeping.
            transitions++;
            switch (transition)
            {
                case "ready":
                case "ready-invalid-options":
                    delivery.MarkReady(now);
                    break;
                case "failed":
                    delivery.MarkCycleFailed();
                    break;
                case "stopped":
                    delivery.MarkNotReady();
                    break;
                case "stale":
                    delivery.MarkReady(now.AddDays(-1));
                    break;
            }

            return Substitute.For<IReportingRecipientDestinationResolver>();
        });
        using var provider = services.BuildServiceProvider();
        var service = new ReportingDeploymentReadinessService(provider);

        var blockers = service.GetScheduleWorkerCycleBlockingReasons();

        transitions.Should().Be(1);
        blockers.Any(reason => reason.Contains(
                "secure distribution worker", StringComparison.Ordinal))
            .Should().Be(expectDeliveryBlocker);
        blockers.Should().Contain(reason => reason.Contains(
            "recipient destination directory", StringComparison.Ordinal),
            "delivery bootstrap must never remove unrelated deployment blockers");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
