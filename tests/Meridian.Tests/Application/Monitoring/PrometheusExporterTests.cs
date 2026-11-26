using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Tests for the Prometheus exposition served at <c>/metrics</c>.
/// </summary>
/// <remarks>
/// The endpoint used to hand-write ten series into a <see cref="System.Text.StringBuilder"/> and
/// never touch the prometheus-net default registry, so every <c>Metrics.Create*</c> declaration in
/// the solution was incremented at runtime and never scraped. Any alert or dashboard naming one of
/// those series could not fire regardless of system state. These tests pin both halves of the
/// exposition: the legacy series keep their exact names, and registry-declared instruments appear.
/// </remarks>
public sealed class PrometheusExporterTests
{
    private static StatusEndpointHandlers CreateHandlers() => new(
        () => new MetricsSnapshot(),
        () => new PipelineStatistics(),
        () => Array.Empty<DepthIntegrityEvent>());

    [Fact]
    public async Task GetPrometheusMetricsAsync_ServesTheLegacySeries()
    {
        var exposition = await CreateHandlers().GetPrometheusMetricsAsync();

        // Anything already scraping these must keep working; the registry is additive.
        foreach (var series in new[]
                 {
                     "mdc_published",
                     "mdc_dropped",
                     "mdc_integrity",
                     "mdc_trades",
                     "mdc_depth_updates",
                     "mdc_quotes",
                     "mdc_historical_bars",
                     "mdc_drop_rate"
                 })
        {
            exposition.Should().Contain($"# TYPE {series} ", because: $"{series} is a published legacy series");
        }
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_ServesTheDefaultRegistry()
    {
        var counter = Prometheus.Metrics.CreateCounter(
            "mdc_exporter_probe_total",
            "Probe series proving the default registry reaches the exposition.");
        counter.Inc();

        var exposition = await CreateHandlers().GetPrometheusMetricsAsync();

        exposition.Should().Contain("mdc_exporter_probe_total",
            because: "a declaration that never reaches /metrics cannot be alerted on");
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_ServesInstrumentationDeclaredByPrometheusMetrics()
    {
        // PrometheusMetrics declares its instruments in static initialisers, so the type has to be
        // touched before the registry contains them — the same ordering the host relies on.
        Meridian.Application.Monitoring.PrometheusMetrics.UpdateFromSnapshot();

        var exposition = await CreateHandlers().GetPrometheusMetricsAsync();

        exposition.Should().Contain("mdc_events_published_total",
            because: "the alert rules name the registry series, not the legacy hand-written ones");
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_DeclaresEachMetricFamilyExactlyOnce()
    {
        // The Prometheus text format allows one HELP line per family. A second one is a parse
        // error that makes Prometheus reject the *entire* scrape, so concatenating the legacy
        // block with a registry that registers the same family names took /metrics from
        // "serves too little" to "serves nothing" — strictly worse. Contains/StartsWith
        // assertions cannot see this; the shape of the whole document has to be checked.
        Meridian.Application.Monitoring.PrometheusMetrics.UpdateFromSnapshot();

        var exposition = await CreateHandlers().GetPrometheusMetricsAsync();

        var helpNames = exposition
            .Split('\n')
            .Where(line => line.StartsWith("# HELP ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', 4)[2])
            .ToList();
        var typeNames = exposition
            .Split('\n')
            .Where(line => line.StartsWith("# TYPE ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', 4)[2])
            .ToList();

        helpNames.Should().OnlyHaveUniqueItems(
            because: "a second HELP line for a family makes Prometheus reject the whole scrape");
        typeNames.Should().OnlyHaveUniqueItems(
            because: "a second TYPE line for a family makes Prometheus reject the whole scrape");
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_RefreshesSnapshotBackedInstrumentsOnTheScrape()
    {
        // PrometheusMetricsUpdater is constructed only by tests: no host or DI call site ever
        // built it, so UpdateFromSnapshot never ran in production and every gauge it feeds sat
        // at its initial zero. Served but never written reads exactly like a healthy system,
        // which is what the drop-rate, latency, circuit-breaker, and validation alerts would
        // have been reading. Collecting on the scrape is what makes those values real.
        var handlers = CreateHandlers();

        var exposition = await handlers.GetPrometheusMetricsAsync();

        // The families the updater feeds must be present, having been refreshed by the call.
        exposition.Should().Contain("mdc_events_published_total");
        exposition.Should().Contain("mdc_drop_rate_percent");
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_SurvivesBothWalCollectorRegistrations()
    {
        // WriteAheadLog and PrometheusMetrics each register mdc_wal_recovery_events_total and
        // mdc_wal_recovery_duration_seconds, with different help text in each. In the UI host
        // both initialise — EventPipeline.RecoverAsync touches WriteAheadLog, and the scrape
        // now touches PrometheusMetrics — so if prometheus-net rejects a conflicting
        // re-registration, /metrics throws instead of exporting anything.
        RuntimeHelpers.RunClassConstructor(typeof(Meridian.Storage.Archival.WriteAheadLog).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Meridian.Application.Monitoring.PrometheusMetrics).TypeHandle);

        var act = async () => await CreateHandlers().GetPrometheusMetricsAsync();

        await act.Should().NotThrowAsync(
            because: "both WAL collector sets initialise in the host, and a rejected registration "
                   + "would take the whole exposition down");

        var exposition = await CreateHandlers().GetPrometheusMetricsAsync();
        exposition.Split('\n')
            .Where(line => line.StartsWith("# HELP ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', 4)[2])
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_KeepsTheLegacyBlockUnchanged()
    {
        var handlers = CreateHandlers();

        var legacy = handlers.GetPrometheusMetrics();
        var exposition = await handlers.GetPrometheusMetricsAsync();

        exposition.Should().StartWith(legacy,
            because: "the registry is appended, so existing scrapers see unchanged leading content");
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_ExportsTheLivePipelineBacklogFromTheScrape()
    {
        // Queue fullness, faulted consumers and flush age used to reach only the /status JSON,
        // so no alert could watch the backlog that precedes drops. The scrape now reads them.
        var handlers = new StatusEndpointHandlers(
            () => new MetricsSnapshot(),
            () => new PipelineStatistics(
                PublishedCount: 100, DroppedCount: 0, ConsumedCount: 20, CurrentQueueSize: 80,
                PeakQueueSize: 90, QueueCapacity: 100, QueueUtilization: 80, AverageProcessingTimeUs: 1,
                TimeSinceLastFlush: TimeSpan.FromSeconds(12), Timestamp: DateTimeOffset.UtcNow),
            () => Array.Empty<DepthIntegrityEvent>());

        var exposition = await handlers.GetPrometheusMetricsAsync();

        foreach (var family in new[]
                 {
                     "mdc_pipeline_queue_depth", "mdc_pipeline_queue_capacity",
                     "mdc_pipeline_queue_utilization_ratio", "mdc_pipeline_peak_queue_depth",
                     "mdc_pipeline_seconds_since_last_flush", "mdc_pipeline_consumers_active",
                     "mdc_pipeline_consumers_faulted", "mdc_pipeline_consumer_failures_total"
                 })
        {
            exposition.Should().Contain($"{family}{{pipeline=\"live\"}}",
                because: $"{family} must be served for the live pipeline on every scrape");
        }
    }

    [Fact]
    public async Task UpdatePipelineMetrics_ExportsBacklogAndConsumerHealthValues()
    {
        // A unique label keeps parallel scrapes of the shared "live" series out of this assertion.
        var pipeline = "test-" + Guid.NewGuid().ToString("N");
        var statistics = new PipelineStatistics(
            PublishedCount: 1_000, DroppedCount: 0, ConsumedCount: 925, CurrentQueueSize: 75,
            PeakQueueSize: 95, QueueCapacity: 100, QueueUtilization: 75, AverageProcessingTimeUs: 1,
            TimeSinceLastFlush: TimeSpan.FromSeconds(42), Timestamp: DateTimeOffset.UtcNow,
            ConsumerCount: 2, ActiveConsumers: 1, FaultedConsumers: 1, ConsumerIterationFailures: 3);

        Meridian.Application.Monitoring.PrometheusMetrics.UpdatePipelineMetrics(statistics, pipeline);
        var series = await ReadSeriesAsync(pipeline);

        series["mdc_pipeline_queue_depth"].Should().Be(75);
        series["mdc_pipeline_queue_capacity"].Should().Be(100);
        series["mdc_pipeline_queue_utilization_ratio"].Should().Be(0.75,
            because: "the percentage from PipelineStatistics is exported as a 0-1 ratio");
        series["mdc_pipeline_peak_queue_depth"].Should().Be(95);
        series["mdc_pipeline_seconds_since_last_flush"].Should().Be(42);
        series["mdc_pipeline_consumers_active"].Should().Be(1);
        series["mdc_pipeline_consumers_faulted"].Should().Be(1);
        series["mdc_pipeline_consumer_failures_total"].Should().Be(3);
    }

    [Fact]
    public async Task UpdatePipelineMetrics_KeepsConsumerFailuresMonotonicAcrossAPipelineRebuild()
    {
        // A rebuilt pipeline starts its own failure count at zero; the exported counter must not
        // go backwards (rate() would read a reset as a huge negative spike or drop the sample).
        var pipeline = "test-" + Guid.NewGuid().ToString("N");

        Meridian.Application.Monitoring.PrometheusMetrics.UpdatePipelineMetrics(
            new PipelineStatistics() with { ConsumerIterationFailures = 4, QueueCapacity = 10 }, pipeline);
        Meridian.Application.Monitoring.PrometheusMetrics.UpdatePipelineMetrics(
            new PipelineStatistics() with { ConsumerIterationFailures = 1, QueueCapacity = 10 }, pipeline);

        (await ReadSeriesAsync(pipeline))["mdc_pipeline_consumer_failures_total"].Should().Be(5);
    }

    [Fact]
    public async Task GetPrometheusMetricsAsync_ExportsFreeAndTotalBytesForEachMonitoredPath()
    {
        var path = "/test-volume-" + Guid.NewGuid().ToString("N");
        var handlers = CreateHandlers();
        handlers.RegisterExtendedProviders(diskSpace: () =>
        [
            new Meridian.Platform.Diagnostics.DiskSpaceInfo(path, "/", TotalSpaceGb: 100, FreeSpaceGb: 1.5,
                UsedSpaceGb: 98.5, UsedPercent: 98.5, IsCritical: true, IsWarning: true)
        ]);

        var exposition = await handlers.GetPrometheusMetricsAsync();

        var lines = exposition.Split('\n');
        lines.Should().Contain($"mdc_disk_free_bytes{{path=\"{path}\"}} {1.5 * 1024 * 1024 * 1024}");
        lines.Should().Contain($"mdc_disk_total_bytes{{path=\"{path}\"}} {100L * 1024 * 1024 * 1024}");
    }

    private static async Task<Dictionary<string, double>> ReadSeriesAsync(string pipeline)
    {
        using var buffer = new MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer);
        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        var marker = $"{{pipeline=\"{pipeline}\"}} ";

        return text.Split('\n')
            .Where(line => line.Contains(marker, StringComparison.Ordinal))
            .ToDictionary(
                line => line[..line.IndexOf('{')],
                line => double.Parse(line[(line.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..],
                    System.Globalization.CultureInfo.InvariantCulture));
    }
}
