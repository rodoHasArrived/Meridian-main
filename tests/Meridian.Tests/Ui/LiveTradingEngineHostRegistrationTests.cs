using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Execution.Adapters;
using Meridian.Execution.Live;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Host composition coverage for the live trading engine tap: published pipeline events must
/// keep flowing to the original publisher AND reach the engine's feed hub and price cache.
/// </summary>
public sealed class LiveTradingEngineHostRegistrationTests
{
    [Fact]
    public async Task AddLiveTradingEngine_TapsMarketEventPublisherWithoutReplacingIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var innerPublisher = new RecordingPublisher();
        services.AddSingleton<IMarketEventPublisher>(innerPublisher);

        services.AddLiveTradingEngine(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMarketEventPublisher>();
        var cache = provider.GetRequiredService<LiveMarketDataCache>();
        var hub = provider.GetRequiredService<LiveMarketEventHub>();

        using var subscriptionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstEventTask = ReadFirstEventAsync(hub, subscriptionCts.Token);

        // Publish until the (lazily attached) hub subscription observes an event.
        var sequence = 0L;
        while (!firstEventTask.IsCompleted && !subscriptionCts.IsCancellationRequested)
        {
            var trade = new Trade(DateTimeOffset.UtcNow, "AAPL", 187.5m, 100, AggressorSide.Buy, ++sequence);
            var evt = new MarketEvent(trade.Timestamp, "AAPL", MarketEventType.Trade, trade);
            publisher.TryPublish(in evt).Should().BeTrue();
            await Task.Delay(25);
        }

        innerPublisher.Published.Should().NotBeEmpty("the tap must not replace the storage pipeline publisher");
        cache.GetLastTrade("AAPL").Should().NotBeNull("the tap must keep the live price cache current");
        cache.GetLastTrade("AAPL")!.Price.Should().Be(187.5m);

        var received = await firstEventTask;
        received.Symbol.Should().Be("AAPL");
        received.Payload.Should().BeOfType<Trade>()
            .Which.Price.Should().Be(187.5m);
    }

    [Fact]
    public void AddLiveTradingEngine_WithoutPipelinePublisher_StillRegistersEngineSurfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLiveTradingEngine(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Meridian.Execution.Interfaces.ILiveFeedAdapter>()
            .Should().BeSameAs(provider.GetRequiredService<LiveMarketDataCache>());
        provider.GetRequiredService<ILiveMarketEventFeed>()
            .Should().BeSameAs(provider.GetRequiredService<LiveMarketEventHub>());
        provider.GetRequiredService<Meridian.Strategies.Live.ILiveStrategyCatalog>()
            .StrategyIds.Should().Contain("buy-and-hold");
    }

    private static async Task<LiveMarketEvent> ReadFirstEventAsync(LiveMarketEventHub hub, CancellationToken ct)
    {
        await foreach (var evt in hub.SubscribeAsync(["AAPL"], ct))
        {
            return evt;
        }

        throw new InvalidOperationException("The hub subscription completed without delivering an event.");
    }

    private sealed class RecordingPublisher : IMarketEventPublisher
    {
        public List<MarketEvent> Published { get; } = [];

        public bool TryPublish(in MarketEvent evt)
        {
            Published.Add(evt);
            return true;
        }
    }
}
