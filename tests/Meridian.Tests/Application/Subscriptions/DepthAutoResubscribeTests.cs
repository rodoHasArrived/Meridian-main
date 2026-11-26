using FluentAssertions;
using Meridian.Application.Subscriptions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Application.Subscriptions;

/// <summary>
/// One depth sequence gap used to leave a symbol's L2 book stale until restart: nothing consumed
/// the integrity signal, and an unchanged-config <see cref="SubscriptionOrchestrator.ApplyAsync"/>
/// keeps the existing depth request and never resets the book. These tests cover the recovery
/// path: collector signal → <see cref="AutoResubscribePolicy"/> →
/// <see cref="SubscriptionOrchestrator.ResubscribeDepthAsync"/>.
/// </summary>
public sealed class DepthAutoResubscribeTests
{
    private const string Symbol = "SPY";

    [Fact]
    public void Collector_RaisesIntegrityDetected_OnFailureAndOnEveryUpdateWhileStale()
    {
        var collector = new MarketDepthCollector(new TestMarketEventPublisher(), requireExplicitSubscription: false);
        var seen = new List<DepthIntegrityKind>();
        collector.IntegrityDetected += evt => seen.Add(evt.Kind);

        collector.OnDepth(Depth(DepthOperation.Update, 0)); // update on an empty book → stale
        collector.OnDepth(Depth(DepthOperation.Insert, 0)); // rejected while stale

        seen.Should().Equal(DepthIntegrityKind.OutOfOrder, DepthIntegrityKind.Stale);
    }

    [Fact]
    public void Collector_SwallowsHandlerExceptions_SoIngestionContinues()
    {
        var collector = new MarketDepthCollector(new TestMarketEventPublisher(), requireExplicitSubscription: false);
        collector.IntegrityDetected += _ => throw new InvalidOperationException("handler failure");

        var act = () => collector.OnDepth(Depth(DepthOperation.Update, 0));

        act.Should().NotThrow();
        collector.IsSymbolStreamStale(Symbol).Should().BeTrue();
    }

    [Fact]
    public async Task ResubscribeDepth_CancelsOldRequest_ResetsBook_AndSubscribesAgain()
    {
        var (collector, client, orchestrator) = await CreateAsync();
        var originalId = orchestrator.DepthSubscriptions[Symbol];
        collector.OnDepth(Depth(DepthOperation.Update, 0));
        collector.IsSymbolStreamStale(Symbol).Should().BeTrue();

        var resubscribed = await orchestrator.ResubscribeDepthAsync(Symbol);

        resubscribed.Should().BeTrue();
        client.UnsubscribedDepthIds.Should().Equal(originalId);
        orchestrator.DepthSubscriptions[Symbol].Should().NotBe(originalId).And.BePositive();
        collector.IsSymbolStreamStale(Symbol).Should().BeFalse("the reset clears the stale flag");
        collector.OnDepth(Depth(DepthOperation.Insert, 0));
        collector.GetCurrentSnapshot(Symbol).Should().NotBeNull("the replayed book builds from empty");
    }

    [Fact]
    public async Task ResubscribeDepth_ForSymbolWithoutDepth_ChangesNothing()
    {
        var (_, client, orchestrator) = await CreateAsync(subscribeDepth: false);

        var resubscribed = await orchestrator.ResubscribeDepthAsync(Symbol);

        resubscribed.Should().BeFalse();
        client.UnsubscribedDepthIds.Should().BeEmpty();
        client.DepthSubscribeCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResubscribeDepth_WhenProviderRefuses_ThrowsAndRetriesOnNextAttempt()
    {
        var (_, client, orchestrator) = await CreateAsync();
        client.FailDepthSubscribe = true;

        var first = () => orchestrator.ResubscribeDepthAsync(Symbol);

        await first.Should().ThrowAsync<InvalidOperationException>();
        orchestrator.DepthSubscriptions[Symbol].Should().Be(-1, "the symbol stays owned with a failed marker");

        client.FailDepthSubscribe = false;
        (await orchestrator.ResubscribeDepthAsync(Symbol)).Should().BeTrue();
        orchestrator.DepthSubscriptions[Symbol].Should().BePositive();
    }

    [Fact]
    public async Task Policy_OnDepthIntegrityEvent_RecoversTheBook_ThenRateLimitsRepeats()
    {
        var (collector, client, orchestrator) = await CreateAsync();
        await using var policy = new AutoResubscribePolicy(orchestrator);
        var events = new List<DepthIntegrityEvent>();
        collector.IntegrityDetected += events.Add;
        collector.OnDepth(Depth(DepthOperation.Update, 0));
        collector.OnDepth(Depth(DepthOperation.Insert, 0));

        var first = await policy.OnDepthIntegrityEventAsync(events[0]);
        var repeat = await policy.OnDepthIntegrityEventAsync(events[1]);

        first.Should().BeTrue();
        repeat.Should().BeFalse("a repeat inside the cooldown is rate limited");
        client.DepthSubscribeCalls.Should().Be(2, "the initial subscribe plus one recovery");
        collector.IsSymbolStreamStale(Symbol).Should().BeFalse();
    }

    [Fact]
    public async Task Policy_OnDepthIntegrityEvent_ForSymbolNotHeldHere_IsSkipped()
    {
        var (collector, client, orchestrator) = await CreateAsync(subscribeDepth: false);
        await using var policy = new AutoResubscribePolicy(orchestrator);
        DepthIntegrityEvent? evt = null;
        collector.RegisterSubscription(Symbol);
        collector.IntegrityDetected += e => evt = e;
        collector.OnDepth(Depth(DepthOperation.Update, 0));

        var result = await policy.OnDepthIntegrityEventAsync(evt!);

        result.Should().BeFalse();
        client.DepthSubscribeCalls.Should().Be(0);
        policy.CurrentCircuitState.Should().Be(CircuitState.Closed, "a skip is not a failure");
    }

    private static async Task<(MarketDepthCollector Collector, FakeDepthClient Client, SubscriptionOrchestrator Orchestrator)> CreateAsync(
        bool subscribeDepth = true)
    {
        var publisher = new TestMarketEventPublisher();
        var collector = new MarketDepthCollector(publisher);
        var client = new FakeDepthClient();
        var orchestrator = new SubscriptionOrchestrator(collector, new TradeDataCollector(publisher), client, "fake");
        await orchestrator.ApplyAsync(new AppConfig(Symbols:
        [
            new SymbolConfig(Symbol, SubscribeTrades: false, SubscribeDepth: subscribeDepth, DepthLevels: 5)
        ]));
        return (collector, client, orchestrator);
    }

    private static MarketDepthUpdate Depth(DepthOperation operation, ushort position) => new(
        Timestamp: DateTimeOffset.UtcNow,
        Symbol: Symbol,
        Position: position,
        Operation: operation,
        Side: OrderBookSide.Bid,
        Price: 450m,
        Size: 100m,
        StreamId: "TEST",
        Venue: "ARCA",
        Source: "TEST");

    private sealed class FakeDepthClient : IMarketDataClient
    {
        private int _nextId = 100;

        public bool FailDepthSubscribe { get; set; }
        public int DepthSubscribeCalls { get; private set; }
        public List<int> UnsubscribedDepthIds { get; } = [];

        public bool IsEnabled => true;
        public bool IsConnected => true;
        public string ProviderId => "fake";
        public string ProviderDisplayName => "Fake depth client";
        public string ProviderDescription => "Test depth client";
        public int ProviderPriority => 0;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.Streaming(trades: true, depth: true);

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public int SubscribeMarketDepth(SymbolConfig cfg)
        {
            DepthSubscribeCalls++;
            if (FailDepthSubscribe)
                throw new IOException("simulated provider refusal");
            return Interlocked.Increment(ref _nextId);
        }

        public void UnsubscribeMarketDepth(int subscriptionId) => UnsubscribedDepthIds.Add(subscriptionId);
        public int SubscribeTrades(SymbolConfig cfg) => Interlocked.Increment(ref _nextId);
        public void UnsubscribeTrades(int subscriptionId) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
