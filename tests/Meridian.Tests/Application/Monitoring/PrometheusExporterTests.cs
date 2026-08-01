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
                     "mdc_events_per_second",
                     "mdc_drop_rate",
                     "mdc_historical_bars_per_second"
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
    public async Task GetPrometheusMetricsAsync_KeepsTheLegacyBlockUnchanged()
    {
        var handlers = CreateHandlers();

        var legacy = handlers.GetPrometheusMetrics();
        var exposition = await handlers.GetPrometheusMetricsAsync();

        exposition.Should().StartWith(legacy,
            because: "the registry is appended, so existing scrapers see unchanged leading content");
    }
}
