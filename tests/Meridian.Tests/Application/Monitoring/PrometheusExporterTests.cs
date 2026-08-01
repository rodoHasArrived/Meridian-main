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
}
