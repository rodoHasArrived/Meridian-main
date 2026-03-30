using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="WebSocketProviderBase"/> lifecycle contract,
/// validated through a minimal stub implementation.
/// </summary>
public sealed class WebSocketProviderBaseTests
{
    // -----------------------------------------------------------------------
    // Stub implementation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Minimal concrete implementation used only in tests.
    /// Tracks which template methods were called so tests can assert ordering.
    /// </summary>
    private sealed class StubProvider : WebSocketProviderBase
    {
        public int BuildUriCallCount;
        public int AuthCallCount;
        public int HandleCallCount;
        public int ResubscribeCallCount;
        public List<string> HandledMessages { get; } = new();

        public StubProvider(
            string providerName = "stub",
            WebSocketConnectionConfig? config = null,
            int startId = 1)
            : base(providerName, config, startId) { }

        public override bool IsEnabled => true;
        public override string ProviderId => "stub";
        public override string ProviderDisplayName => "Stub";
        public override string ProviderDescription => "Test stub";
        public override int ProviderPriority => 99;
        public override ProviderCapabilities ProviderCapabilities => ProviderCapabilities.Streaming();

        public override int SubscribeTrades(SymbolConfig cfg) => Subscriptions.Subscribe(cfg.Symbol, "trades");
        public override void UnsubscribeTrades(int id) => Subscriptions.Unsubscribe(id);
        public override int SubscribeMarketDepth(SymbolConfig cfg) => Subscriptions.Subscribe(cfg.Symbol, "depth");
        public override void UnsubscribeMarketDepth(int id) => Subscriptions.Unsubscribe(id);

        protected override Uri BuildWebSocketUri()
        {
            BuildUriCallCount++;
            return new Uri("wss://stub.example.com/feed");
        }

        protected override Task AuthenticateAsync(CancellationToken ct)
        {
            AuthCallCount++;
            return Task.CompletedTask;
        }

        protected override Task HandleMessageAsync(string message)
        {
            HandleCallCount++;
            HandledMessages.Add(message);
            return Task.CompletedTask;
        }

        protected override Task ResubscribeAsync(CancellationToken ct)
        {
            ResubscribeCallCount++;
            return Task.CompletedTask;
        }

        // Expose protected helpers for testing
        public bool IsConnectedPublic => Connected;
        public SubscriptionManager SubscriptionsPublic => Subscriptions;
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Constructor_WithValidArgs_CreatesInstance()
    {
        await using var provider = new StubProvider();

        provider.Should().NotBeNull();
        provider.IsEnabled.Should().BeTrue();
        provider.ProviderId.Should().Be("stub");
    }

    [Fact]
    public async Task Constructor_WithCustomStartId_IsReflectedInSubscriptionRange()
    {
        await using var provider = new StubProvider(startId: 5000);
        var cfg = new SymbolConfig("AAPL");

        var id = provider.SubscribeTrades(cfg);

        id.Should().BeGreaterThanOrEqualTo(5000, "subscription IDs should start from the configured range");
    }

    // -----------------------------------------------------------------------
    // Subscription tracking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubscribeTrades_RegistersSymbolInSubscriptionManager()
    {
        await using var provider = new StubProvider();
        var cfg = new SymbolConfig("MSFT");

        var id = provider.SubscribeTrades(cfg);

        id.Should().BeGreaterThan(0);
        provider.SubscriptionsPublic.GetSymbolsByKind("trades").Should().Contain("MSFT");
    }

    [Fact]
    public async Task UnsubscribeTrades_RemovesSymbolFromSubscriptionManager()
    {
        await using var provider = new StubProvider();
        var cfg = new SymbolConfig("TSLA");
        var id = provider.SubscribeTrades(cfg);

        provider.UnsubscribeTrades(id);

        provider.SubscriptionsPublic.GetSymbolsByKind("trades").Should().NotContain("TSLA");
    }

    [Fact]
    public async Task SubscribeMarketDepth_RegistersSymbolUnderDepthKind()
    {
        await using var provider = new StubProvider();
        var cfg = new SymbolConfig("NVDA");

        var id = provider.SubscribeMarketDepth(cfg);

        id.Should().BeGreaterThan(0);
        provider.SubscriptionsPublic.GetSymbolsByKind("depth").Should().Contain("NVDA");
    }

    // -----------------------------------------------------------------------
    // Initial state
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Connected_BeforeConnect_IsFalse()
    {
        await using var provider = new StubProvider();

        provider.IsConnectedPublic.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Disconnect / Dispose without prior connect
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DisconnectAsync_WithoutConnect_DoesNotThrow()
    {
        await using var provider = new StubProvider();

        var act = async () => await provider.DisconnectAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithoutConnect_DoesNotThrow()
    {
        var provider = new StubProvider();

        var act = async () => await provider.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // IProviderMetadata defaults
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProviderCapabilities_Streaming_ReturnsTrue()
    {
        await using var provider = new StubProvider();

        provider.ProviderCapabilities.SupportsStreaming.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleSubscriptions_DifferentSymbols_AreTrackedIndependently()
    {
        await using var provider = new StubProvider();

        var id1 = provider.SubscribeTrades(new SymbolConfig("SPY"));
        var id2 = provider.SubscribeTrades(new SymbolConfig("QQQ"));

        id1.Should().NotBe(id2, "each subscription gets a unique ID");
        provider.SubscriptionsPublic.GetSymbolsByKind("trades")
            .Should().Contain(new[] { "SPY", "QQQ" });
    }

    [Fact]
    public async Task DisconnectAsync_ClearsSubscriptions()
    {
        await using var provider = new StubProvider();
        provider.SubscribeTrades(new SymbolConfig("SPY"));
        provider.SubscribeTrades(new SymbolConfig("AAPL"));

        // DisconnectAsync should clear the subscription manager (per base class implementation).
        // We call it without a prior ConnectAsync — it should not throw.
        await provider.DisconnectAsync();

        provider.SubscriptionsPublic.GetSymbolsByKind("trades")
            .Should().BeEmpty("DisconnectAsync clears subscriptions");
    }
}
