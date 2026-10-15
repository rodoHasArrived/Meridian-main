using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Domain.Events;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

[Collection("PrometheusResettableTotals")]
public sealed class LiveFreshnessWorkflowTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-freshness-{Guid.NewGuid():N}");
    private ServiceProvider? _provider;

    [Fact]
    public async Task ComposedIngress_StallRaisesViolationAndResumedEventsClearExportedMetrics()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));
        var (publisher, monitor) = CreatePipeline(clock);
        var violations = new List<SlaViolationEvent>();
        var recoveries = new List<SlaRecoveryEvent>();
        monitor.OnViolation += violations.Add;
        monitor.OnRecovery += recoveries.Add;

        // No monitored symbols must never be interpreted as a zero-score outage.
        (await MetricAsync("mdc_sla_monitored_symbols")).Should().Be(0);
        (await MetricAsync("mdc_sla_freshness_score")).Should().Be(100);
        publisher.TryPublish(TradeEvent(clock, "SPY")).Should().BeTrue();
        monitor.GetSymbolStatus("SPY")!.LastEventTime.Should().Be(clock.GetUtcNow());
        clock.Advance(TimeSpan.FromSeconds(1));
        (await MetricAsync("mdc_sla_monitored_symbols")).Should().Be(1);
        (await MetricAsync("mdc_sla_healthy_symbols")).Should().Be(1);

        // Timer ticks with no more events must still refresh the registry, including score zero.
        clock.Advance(TimeSpan.FromSeconds(30));
        violations.Should().ContainSingle(v => v.Symbol == "SPY");
        (await MetricAsync("mdc_sla_violation_symbols")).Should().Be(1);
        (await MetricAsync("mdc_sla_current_violations")).Should().Be(1);
        (await MetricAsync("mdc_sla_freshness_score")).Should().Be(0);
        (await MetricAsync("mdc_sla_violations_total")).Should().BeGreaterThanOrEqualTo(1);

        // A quote is also live activity, and recovery reaches /metrics by the next check.
        publisher.TryPublish(QuoteEvent(clock, "SPY")).Should().BeTrue();
        recoveries.Should().ContainSingle(r => r.Symbol == "SPY");
        monitor.CurrentViolations.Should().Be(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await MetricAsync("mdc_sla_violation_symbols")).Should().Be(0);
        (await MetricAsync("mdc_sla_current_violations")).Should().Be(0);
        (await MetricAsync("mdc_sla_healthy_symbols")).Should().Be(1);
        (await MetricAsync("mdc_sla_freshness_score")).Should().Be(100);
    }

    [Theory]
    [InlineData(15, 12)] // Weekday before open.
    [InlineData(15, 21)] // Weekday after close.
    [InlineData(17, 14)] // Saturday during the weekday session window.
    public async Task ComposedIngress_ClosedMarketSuppressesStallAlerts(int day, int hour)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.Zero));
        var (publisher, monitor) = CreatePipeline(clock);
        var violations = new List<SlaViolationEvent>();
        monitor.OnViolation += violations.Add;
        publisher.TryPublish(TradeEvent(clock, "SPY"));

        clock.Advance(TimeSpan.FromMinutes(1));

        violations.Should().BeEmpty();
        monitor.GetSymbolStatus("SPY")!.State.Should().Be(SlaState.OutsideMarketHours);
        (await MetricAsync("mdc_sla_violation_symbols")).Should().Be(0);
        (await MetricAsync("mdc_sla_freshness_score")).Should().Be(100);
    }

    [Fact]
    public async Task ComposedIngress_MarketCloseClearsExportedViolationWithoutAnEvent()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 15, 19, 59, 0, TimeSpan.Zero));
        var (publisher, monitor) = CreatePipeline(clock);
        publisher.TryPublish(TradeEvent(clock, "SPY"));
        clock.Advance(TimeSpan.FromSeconds(30));
        (await MetricAsync("mdc_sla_violation_symbols")).Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(1));

        monitor.GetSnapshot().IsMarketOpen.Should().BeFalse();
        monitor.CurrentViolations.Should().Be(0, "the SLA metrics endpoint must agree with the closed-session snapshot");
        (await MetricAsync("mdc_sla_violation_symbols")).Should().Be(0);
        (await MetricAsync("mdc_sla_current_violations")).Should().Be(0);
        (await MetricAsync("mdc_sla_freshness_score")).Should().Be(100);
    }

    private (IMarketEventPublisher, DataFreshnessSlaMonitor) CreatePipeline(ManualClock clock)
    {
        Directory.CreateDirectory(_root);
        var configPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new AppConfig(DataRoot: _root), AppConfigJsonOptions.Write));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new StorageOptions { RootPath = _root });
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 10,
            CheckIntervalSeconds = 1,
            AlertCooldownSeconds = 0,
            SkipOutsideMarketHours = true
        });
        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new PipelineFeatureRegistration().Register(services, options);
        _provider = services.BuildServiceProvider();
        return (_provider.GetRequiredService<IMarketEventPublisher>(), _provider.GetRequiredService<DataFreshnessSlaMonitor>());
    }

    private static MarketEvent TradeEvent(TimeProvider clock, string symbol)
    {
        var now = clock.GetUtcNow();
        return MarketEvent.Trade(now, symbol, new Trade(now, symbol, 100m, 10, AggressorSide.Buy, 1), "TEST");
    }

    private static MarketEvent QuoteEvent(TimeProvider clock, string symbol)
    {
        var now = clock.GetUtcNow();
        return MarketEvent.BboQuote(now, symbol, new BboQuotePayload(now, symbol, 99m, 10, 101m, 10, 100m, 2m, 1), "TEST");
    }

    private static async Task<double> MetricAsync(string name)
    {
        var handlers = new StatusEndpointHandlers(
            () => new MetricsSnapshot(), () => new PipelineStatistics(), () => Array.Empty<DepthIntegrityEvent>());
        var text = await handlers.GetPrometheusMetricsAsync();
        var line = text.Split('\n').Single(line => line.StartsWith(name + " ", StringComparison.Ordinal));
        return double.Parse(line[(name.Length + 1)..], CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A controllable timer runs the production timer callback synchronously when time advances.
    // Tests exercise registration/lifecycle rather than invoking private check methods.
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private ManualTimer? _timer;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _timer = new ManualTimer(this, callback, state, dueTime, period);
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            _timer?.Tick();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
        {
            private DateTimeOffset _due = clock.GetUtcNow() + dueTime;
            private bool _disposed;
            public void Tick()
            {
                if (!_disposed && clock.GetUtcNow() >= _due)
                {
                    _due = clock.GetUtcNow() + period;
                    callback(state);
                }
            }
            public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
            {
                _due = clock.GetUtcNow() + newDueTime;
                period = newPeriod;
                return !_disposed;
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
