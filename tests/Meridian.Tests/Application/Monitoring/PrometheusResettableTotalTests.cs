using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

using PlatformMetrics = Meridian.Platform.Tracing.Metrics;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Tests that resettable source totals reach Prometheus as monotonic counters.
/// </summary>
/// <remarks>
/// <see cref="BackfillCoordinator"/> calls <c>Reset()</c> on the process-wide
/// <see cref="PlatformMetrics"/> snapshot, zeroing every total. A Prometheus counter cannot
/// decrease, so mirroring the raw total with <c>IncTo</c> froze the exported series at its
/// pre-reset value until the fresh total climbed back past it. Across that window
/// <c>increase(mdc_events_published_total[10m])</c> reads zero, which is precisely the condition
/// <c>MeridianNoEventsPublished</c> treats as a stalled feed — so starting a backfill on a
/// healthy pipeline would page a responder.
/// </remarks>
[Collection("PrometheusResettableTotals")]
public sealed class PrometheusResettableTotalTests
{
    [Fact]
    public void UpdateFromSnapshot_AfterAReset_KeepsThePublishedCounterIncreasing()
    {
        PlatformMetrics.Reset();
        PrometheusMetrics.UpdateFromSnapshot();
        var start = PrometheusMetrics.PublishedEventsValue;

        for (var i = 0; i < 500; i++)
            PlatformMetrics.IncPublished();
        PrometheusMetrics.UpdateFromSnapshot();

        var beforeReset = PrometheusMetrics.PublishedEventsValue;
        beforeReset.Should().BeApproximately(start + 500, 0.001);

        // An operator starts a backfill: the source total goes to zero underneath us.
        PlatformMetrics.Reset();
        PrometheusMetrics.UpdateFromSnapshot();

        PrometheusMetrics.PublishedEventsValue.Should().BeApproximately(beforeReset, 0.001,
            because: "a reset publishes nothing, so the exported total must hold, not rewind");

        // The feed keeps publishing after the reset. Those events must show up as an increase,
        // not be swallowed while the fresh total climbs back to the old high-water mark.
        for (var i = 0; i < 10; i++)
            PlatformMetrics.IncPublished();
        PrometheusMetrics.UpdateFromSnapshot();

        PrometheusMetrics.PublishedEventsValue.Should().BeApproximately(beforeReset + 10, 0.001,
            because: "post-reset publishing must move the counter, or rate() reads zero on a "
                   + "healthy feed and MeridianNoEventsPublished pages for a working pipeline");
    }

    [Fact]
    public void UpdateFromSnapshot_AcrossRepeatedResets_NeverDecreases()
    {
        PlatformMetrics.Reset();
        PrometheusMetrics.UpdateFromSnapshot();

        var previous = PrometheusMetrics.PublishedEventsValue;

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 25; i++)
                PlatformMetrics.IncPublished();
            PrometheusMetrics.UpdateFromSnapshot();

            var afterPublishing = PrometheusMetrics.PublishedEventsValue;
            afterPublishing.Should().BeApproximately(previous + 25, 0.001);

            PlatformMetrics.Reset();
            PrometheusMetrics.UpdateFromSnapshot();

            PrometheusMetrics.PublishedEventsValue.Should().BeApproximately(afterPublishing, 0.001);
            previous = afterPublishing;
        }
    }
}
