using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Domain.Events;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Monitoring;

[Collection("PrometheusMetrics")]
public sealed class FreshnessMonitoringWorkflowTests
{
    [Fact]
    public async Task ClosedSessions_DoNotAddAgeSamplesToTheMarketHoursHistogram()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 16, 19, 59, 59, TimeSpan.Zero));
        const string symbol = "HISTOGRAM-CLOSED-SESSION";
        host.Publisher.TryPublish(Trade(symbol, host.Clock.GetUtcNow()));
        var openSnapshot = host.Monitor.GetSnapshot();
        Meridian.Application.Monitoring.PrometheusMetrics.UpdateSlaMetrics(openSnapshot);
        var before = await ExportHistogramCountAsync(symbol);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var closedSnapshot = host.Monitor.GetSnapshot();
        closedSnapshot.IsMarketOpen.Should().BeFalse();
        closedSnapshot.SymbolStatuses.Should().OnlyContain(status => status.State == SlaState.OutsideMarketHours);
        (await ExportHistogramCountAsync(symbol)).Should().Be(before);
    }

    private static async Task<double> ExportHistogramCountAsync(string symbol)
    {
        var handlers = new StatusEndpointHandlers(
            () => new MetricsSnapshot(), () => new PipelineStatistics(), () => Array.Empty<DepthIntegrityEvent>());
        var output = await handlers.GetPrometheusMetricsAsync();
        var prefix = $"mdc_sla_freshness_milliseconds_count{{symbol=\"{symbol}\"}} ";
        var count = output.Split('\n').Single(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return double.Parse(count[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task IdleWorkstation_DoesNotMonitorConfiguredSymbolsOrOverwriteActiveCollectorMetrics()
    {
        var now = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        await using var collector = new TestHost(now);
        collector.Publisher.TryPublish(Trade("SPY", now));
        collector.Clock.Advance(TimeSpan.FromSeconds(21));
        await AssertExportAsync(1, 0, 1);

        await using var workstation = new TestHost(now, [new SymbolConfig("IDLE")], synchronizeSubscriptions: false);
        workstation.Clock.Advance(TimeSpan.FromMinutes(1));
        workstation.Monitor.GetSymbolStatus("IDLE").Should().BeNull();
        workstation.Monitor.GetSnapshot().TotalSymbols.Should().Be(0);
        await AssertExportAsync(1, 0, 1);
    }

    [Fact]
    public async Task LiveIngress_PeriodicSnapshotsExportStallAndRecoveryWithoutMoreEvents()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero));
        var violations = new List<SlaViolationEvent>();
        var recoveries = new List<SlaRecoveryEvent>();
        host.Monitor.OnViolation += violations.Add;
        host.Monitor.OnRecovery += recoveries.Add;

        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow())).Should().BeTrue();
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertExportAsync(0, 100, 1);

        // There are no incoming events to drive the exporter during the stall.
        host.Clock.Advance(TimeSpan.FromSeconds(20));
        await AssertExportAsync(1, 0, 1);
        violations.Should().ContainSingle(v => v.Symbol == "SPY");

        // A connection heartbeat cannot hide stale prices for a subscribed symbol.
        host.Publisher.TryPublish(MarketEvent.Heartbeat(host.Clock.GetUtcNow(), "TEST") with { Symbol = "SPY" });
        host.Publisher.TryPublish(MarketEvent.HistoricalBar(host.Clock.GetUtcNow(), "SPY",
            new HistoricalBar("SPY", new DateOnly(2026, 10, 9), 100m, 101m, 99m, 100m, 100, "TEST"), "TEST"));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertExportAsync(1, 0, 1);
        violations.Should().HaveCount(1);

        host.Publisher.TryPublish(Quote("SPY", host.Clock.GetUtcNow())).Should().BeTrue();
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertExportAsync(0, 100, 1);
        recoveries.Should().ContainSingle(r => r.Symbol == "SPY");
        host.Monitor.CurrentViolations.Should().Be(0);
    }

    [Fact]
    public async Task ConfiguredFeedWithoutFirstEvent_ViolatesAfterGraceAndRecoversOnFirstReceipt()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero),
            [new SymbolConfig("SPY"), new SymbolConfig("DISABLED", SubscribeTrades: false, SubscribeDepth: false)]);
        var violations = new List<SlaViolationEvent>();
        var recoveries = new List<SlaRecoveryEvent>();
        host.Monitor.OnViolation += violations.Add;
        host.Monitor.OnRecovery += recoveries.Add;

        host.Clock.Advance(TimeSpan.FromSeconds(10));
        host.Monitor.GetSymbolStatus("SPY")!.State.Should().Be(SlaState.NoData);
        host.Monitor.GetSymbolStatus("DISABLED").Should().BeNull();
        await AssertExportAsync(0, 100, 1);

        host.Clock.Advance(TimeSpan.FromSeconds(11));
        await AssertExportAsync(1, 0, 1);
        violations.Should().ContainSingle(v => v.Symbol == "SPY");
        host.Monitor.GetSymbolStatus("SPY")!.LastEventTime.Should().Be(DateTimeOffset.MinValue);

        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow()));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertExportAsync(0, 100, 1);
        recoveries.Should().ContainSingle(r => r.Symbol == "SPY");
    }

    [Fact]
    public async Task ConfiguredFeedWithoutFirstEvent_ReceivesFreshGraceAtMarketOpen()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero),
            [new SymbolConfig("SPY")]);
        host.Clock.Advance(TimeSpan.FromDays(1));
        await AssertExportAsync(0, 100, 1);

        var mondayOpen = new DateTimeOffset(2026, 10, 19, 13, 30, 0, TimeSpan.Zero);
        host.Clock.Advance(mondayOpen - host.Clock.GetUtcNow());
        await AssertExportAsync(0, 100, 1);
        host.Monitor.GetSymbolStatus("SPY")!.State.Should().Be(SlaState.NoData);
        host.Clock.Advance(TimeSpan.FromSeconds(21));
        await AssertExportAsync(1, 0, 1);
    }

    [Fact]
    public async Task MarketClose_PeriodicSnapshotClearsActiveAlertUntilMarketReopens()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 16, 19, 59, 0, TimeSpan.Zero));
        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow()));
        host.Clock.Advance(TimeSpan.FromSeconds(21));
        await AssertExportAsync(1, 0, 1);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await AssertExportAsync(0, 100, 1);
        host.Monitor.GetSymbolStatus("SPY")!.State.Should().Be(SlaState.OutsideMarketHours);
        host.Monitor.CurrentViolations.Should().Be(0);

        host.Clock.Advance(TimeSpan.FromDays(1));
        await AssertExportAsync(0, 100, 1);
        host.Monitor.IsMarketOpen().Should().BeFalse();

        var mondayOpen = new DateTimeOffset(2026, 10, 19, 13, 30, 0, TimeSpan.Zero);
        host.Clock.Advance(mondayOpen - host.Clock.GetUtcNow());
        await AssertExportAsync(1, 0, 1);
        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow()));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertExportAsync(0, 100, 1);
    }

    [Fact]
    public async Task UnregisterSymbol_ClearsCurrentViolationMetrics()
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero));
        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow()));
        host.Clock.Advance(TimeSpan.FromSeconds(21));
        host.Monitor.CurrentViolations.Should().Be(1);

        host.Monitor.UnregisterSymbol("SPY");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        host.Monitor.CurrentViolations.Should().Be(0);
        await AssertExportAsync(0, 100, 0);
    }

    [Theory]
    [InlineData(17, 14)] // Saturday, during the normal weekday session.
    [InlineData(18, 14)] // Sunday.
    [InlineData(19, 12)] // Monday before open.
    public async Task ClosedSessions_DoNotRaiseFreshnessViolations(int day, int hour)
    {
        await using var host = new TestHost(new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.Zero));
        var violations = new List<SlaViolationEvent>();
        host.Monitor.OnViolation += violations.Add;
        host.Publisher.TryPublish(Trade("SPY", host.Clock.GetUtcNow()));
        host.Clock.Advance(TimeSpan.FromMinutes(5));

        await AssertExportAsync(0, 100, 1);
        violations.Should().BeEmpty();
    }

    private static async Task AssertExportAsync(int violations, int score, int symbols)
    {
        var handlers = new StatusEndpointHandlers(
            () => new MetricsSnapshot(), () => new PipelineStatistics(), () => Array.Empty<DepthIntegrityEvent>());
        var exposition = await handlers.GetPrometheusMetricsAsync();
        var lines = exposition.Split('\n').Select(line => line.Trim()).ToArray();
        lines.Should().Contain($"mdc_sla_violation_symbols {violations}");
        lines.Should().Contain($"mdc_sla_current_violations {violations}");
        lines.Should().Contain($"mdc_sla_freshness_score {score}");
        lines.Should().Contain($"mdc_sla_monitored_symbols {symbols}");
    }

    private static MarketEvent Trade(string symbol, DateTimeOffset now) => MarketEvent.Trade(
        now, symbol, new Trade(now, symbol, 100m, 10, AggressorSide.Buy, 1), "TEST");

    private static MarketEvent Quote(string symbol, DateTimeOffset now) => MarketEvent.BboQuote(
        now, symbol, new BboQuotePayload(now, symbol, 100m, 10, 101m, 10, 100.5m, 1m, 2), "TEST");

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-sla-{Guid.NewGuid():N}");
        private readonly ServiceProvider _provider;
        public ManualClock Clock { get; }
        public DataFreshnessSlaMonitor Monitor { get; }
        public IMarketEventPublisher Publisher { get; }

        public TestHost(DateTimeOffset now, SymbolConfig[]? symbols = null, bool synchronizeSubscriptions = true)
        {
            Clock = new ManualClock(now);
            Directory.CreateDirectory(_root);
            var configPath = Path.Combine(_root, "appsettings.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(new AppConfig(DataRoot: _root, Symbols: symbols), AppConfigJsonOptions.Write));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(new SlaConfig
            {
                DefaultFreshnessThresholdSeconds = 10,
                CheckIntervalSeconds = 1,
                SkipOutsideMarketHours = true,
                AlertCooldownSeconds = 0
            });
            services.AddSingleton(new StorageOptions { RootPath = _root });
            var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
            new ConfigurationFeatureRegistration().Register(services, options);
            new PipelineFeatureRegistration().Register(services, options);
            _provider = services.BuildServiceProvider();
            Publisher = _provider.GetRequiredService<IMarketEventPublisher>();
            Monitor = _provider.GetRequiredService<DataFreshnessSlaMonitor>();
            if (synchronizeSubscriptions && symbols is not null)
                Monitor.SynchronizeSymbols(symbols.Where(symbol => symbol.SubscribeTrades || symbol.SubscribeDepth)
                    .Select(symbol => symbol.Symbol));
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }

    // Exercise the actual periodic callback deterministically; no wall-clock sleeps or
    // reflection-based calls into the monitor's private compliance check are required.
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private ManualTimer? _timer;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new ManualTimer(this, callback, state, dueTime, period);
            return _timer;
        }

        public void Advance(TimeSpan amount)
        {
            now += amount;
            _timer?.Tick();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
        {
            private DateTimeOffset _due = clock.GetUtcNow() + dueTime;
            private TimeSpan _period = period;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = clock.GetUtcNow() + dueTime;
                _period = period;
                return !_disposed;
            }
            public void Tick()
            {
                if (!_disposed && clock.GetUtcNow() >= _due)
                {
                    _due = clock.GetUtcNow() + _period;
                    callback(state);
                }
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
