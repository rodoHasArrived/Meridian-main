using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.Tracing;
using Meridian.Tests.Infrastructure;
using Xunit;
using Coordinator = Meridian.Application.Backfill.BackfillCoordinator;
using Request = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// A backfill run must not disturb the live-feed metrics. The coordinator used to drive its
/// pipeline through the process-wide <see cref="Metrics"/> and call <c>Reset()</c> on it at the
/// start of every run: that zeroed the live rate, drop-rate and latency gauges, counted backfill
/// bars into <c>mdc_events_published_total</c> and could hide a stalled live feed from
/// <c>MeridianNoEventsPublished</c> for the length of the backfill.
/// </summary>
/// <remarks>
/// In the collection that serializes every class touching the static <see cref="Metrics"/>, so
/// the exact counter assertions below cannot race another test's publishes or resets.
/// </remarks>
[Collection("PrometheusResettableTotals")]
public sealed class BackfillMetricsIsolationTests : TempDirectoryTestBase
{
    private static readonly DateOnly Session = new(2024, 1, 3);

    [Fact]
    public async Task RunAsync_LeavesLiveMetricsUnresetAndUncounted_AndRecordsTheRunOutcome()
    {
        var config = new ConfigStore(Path.Combine(TestDataRoot, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: TestDataRoot,
            Backfill: new BackfillConfig(EnableSymbolResolution: false)));
        var registry = new ProviderRegistry();
        registry.Register(new SingleBarProvider());
        using var coordinator = new Coordinator(config, registry);

        // Live traffic already counted before the backfill starts.
        Metrics.Reset();
        for (var i = 0; i < 5; i++)
            Metrics.IncPublished();
        var succeededBefore = PrometheusMetrics.BackfillRunsValue(PrometheusMetrics.BackfillOutcome.Succeeded);

        var result = await coordinator.RunAsync(new Request("single", ["SPY"], Session, Session));

        result.Success.Should().BeTrue();
        result.BarsWritten.Should().Be(1);
        var live = Metrics.GetSnapshot();
        live.Published.Should().Be(5,
            "the backfill pipeline counts into run-scoped metrics: it neither resets the live total nor adds its bars to it");
        live.HistoricalBars.Should().Be(1,
            "HistoricalBackfillService counts each written bar once; the pipeline no longer counts it a second time");
        PrometheusMetrics.BackfillRunsValue(PrometheusMetrics.BackfillOutcome.Succeeded)
            .Should().Be(succeededBefore + 1);
    }

    [Fact]
    public void RunScopedEventMetrics_CountsAndResetsWithoutTouchingTheProcessWideMetrics()
    {
        Metrics.Reset();
        Metrics.IncPublished();
        var scoped = new RunScopedEventMetrics();

        scoped.IncPublished();
        scoped.IncPublished();
        scoped.IncDropped();
        scoped.IncHistoricalBars();

        scoped.Published.Should().Be(2);
        scoped.Dropped.Should().Be(1);
        scoped.DropRate.Should().BeApproximately(100d / 3, 0.0001);
        scoped.GetSnapshot().HistoricalBars.Should().Be(1);

        scoped.Reset();

        scoped.Published.Should().Be(0);
        scoped.GetSnapshot().Dropped.Should().Be(0);
        Metrics.Published.Should().Be(1, "resetting run-scoped metrics must leave the live counters alone");
    }

    private sealed class SingleBarProvider : IHistoricalDataProvider
    {
        public string Name => "single";
        public string DisplayName => "single";
        public string Description => string.Empty;
        public int Priority => 1;

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([new(symbol, Session, 10, 10, 10, 10, 10, Name)]);
    }
}
