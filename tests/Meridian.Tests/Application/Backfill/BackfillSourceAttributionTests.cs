using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.Integration;
using Xunit;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Verifies that backfill publishes attribute every durable event to the inner provider
/// that actually served the bar (each provider stamps <c>HistoricalBar.Source</c> with its
/// own name), so a composite/failover fetch never erases the winning vendor by stamping
/// the aggregator's "composite" label into <c>MarketEvent.Source</c> and the disk layout.
/// </summary>
public sealed class BackfillSourceAttributionTests : IAsyncLifetime
{
    private InMemoryStorageSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new InMemoryStorageSink();
        _pipeline = new EventPipeline(_sink, capacity: 10_000, enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_CompositeProvider_StampsWinningInnerProviderNotComposite()
    {
        // The provider chain is named "composite" (the aggregator), but the bars it returns
        // carry the inner winner's stamp — exactly what CompositeHistoricalDataProvider
        // produces, since every inner provider stamps Source: Name on its bars.
        var provider = new StubProvider("composite", barSource: "alpaca");
        var service = new HistoricalBackfillService([provider]);

        var result = await service.RunAsync(
            new AppBackfillRequest("composite", ["SPY"], From: new DateOnly(2024, 1, 3), To: new DateOnly(2024, 1, 4)),
            _pipeline);

        result.Success.Should().BeTrue();
        _sink.StoredEvents.Should().NotBeEmpty();
        _sink.StoredEvents.Should().OnlyContain(evt => evt.Source == "alpaca",
            "the durable Source must name the vendor that served the data");
        _sink.StoredEvents.Should().NotContain(evt => evt.Source == "composite",
            "\"composite\" is an aggregator label, not a vendor, and must never reach the durable tape");
    }

    [Fact]
    public async Task RunAsync_BarWithoutSourceStamp_FallsBackToProviderName()
    {
        var provider = new StubProvider("stooq", barSource: "");
        var service = new HistoricalBackfillService([provider]);

        var result = await service.RunAsync(
            new AppBackfillRequest("stooq", ["SPY"], From: new DateOnly(2024, 1, 3), To: new DateOnly(2024, 1, 4)),
            _pipeline);

        result.Success.Should().BeTrue();
        _sink.StoredEvents.Should().NotBeEmpty();
        _sink.StoredEvents.Should().OnlyContain(evt => evt.Source == "stooq");
    }

    [Theory]
    [InlineData("alpaca", "composite", "alpaca")]
    [InlineData("yahoo", "yahoo", "yahoo")]
    [InlineData("", "stooq", "stooq")]
    [InlineData("   ", "stooq", "stooq")]
    [InlineData(null, "stooq", "stooq")]
    [InlineData("composite", "stooq", "stooq")]
    [InlineData("COMPOSITE", "stooq", "stooq")]
    public void ResolveEventSource_PrefersPerBarStampAndNeverKeepsComposite(
        string? barSource, string providerName, string expected)
    {
        HistoricalBackfillService.ResolveEventSource(barSource, providerName).Should().Be(expected);
    }

    private sealed class StubProvider : IHistoricalDataProvider
    {
        private readonly string _barSource;

        public StubProvider(string name, string barSource)
        {
            Name = DisplayName = name;
            _barSource = barSource;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            var sessionDate = from ?? new DateOnly(2024, 1, 3);
            IReadOnlyList<HistoricalBar> bars =
                [new HistoricalBar(symbol, sessionDate, 100m, 101m, 99m, 100.5m, 1_000, _barSource)];
            return Task.FromResult(bars);
        }
    }
}
