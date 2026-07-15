using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
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
        public void RecordActivityPublic() => RecordActivity();
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

    [Fact]
    public async Task RateLimitDiagnostics_ExposeStreamingSurfaceAsExplicitlyUnavailable()
    {
        await using var provider = new StubProvider();

        var source = provider.Should().BeAssignableTo<IProviderRateLimitDiagnosticsSource>().Subject;
        var snapshot = source.GetRateLimitDiagnosticsSnapshot();

        snapshot.ProviderId.Should().Be("stub");
        snapshot.Surface.Should().Be(ProviderRateLimitSurfaces.Streaming);
        snapshot.StateAvailable.Should().BeFalse();
        snapshot.IsRateLimited.Should().BeFalse();
        snapshot.Reason.Should().Be("runtime-diagnostics-unavailable");
    }

    [Fact]
    public void AlpacaAndPolygon_InheritStreamingRateLimitDiagnosticsContract()
    {
        typeof(IProviderRateLimitDiagnosticsSource).IsAssignableFrom(typeof(AlpacaMarketDataClient))
            .Should().BeTrue();
        typeof(IProviderRateLimitDiagnosticsSource).IsAssignableFrom(typeof(PolygonMarketDataClient))
            .Should().BeTrue();
    }

    [Fact]
    public async Task RateLimitEndpoint_IncludesUnavailableWebSocketStreamingSurfaceWithoutCounters()
    {
        await using var provider = new StubProvider();
        await using var registry = new ProviderRegistry();
        registry.Register(provider);
        var observedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        var response = ProviderExtendedEndpoints.CreateRateLimitsResponse(
            registry,
            Array.Empty<IDataSource>(),
            observedAt);

        var row = response.Providers.Should().ContainSingle(item =>
            item.Provider == "stub" && item.Surface == ProviderRateLimitSurfaces.Streaming).Subject;
        row.StateAvailable.Should().BeFalse();
        row.RequestsInWindow.Should().BeNull();
        row.RemainingRequests.Should().BeNull();
        row.UsageRatio.Should().BeNull();
        row.IsRateLimited.Should().BeFalse();
        row.Reason.Should().Be("runtime-diagnostics-unavailable");
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

    [Fact]
    public async Task SubscribeOrGetExisting_DuplicateSymbolAndKind_ReturnsExistingId()
    {
        await using var provider = new StubProvider();

        var firstId = provider.SubscriptionsPublic.SubscribeOrGetExisting(" spy ", " trades ", "watchlist");
        var secondId = provider.SubscriptionsPublic.SubscribeOrGetExisting("SPY", "trades", "strategy");

        secondId.Should().Be(firstId, "reconnect recovery should not duplicate an active upstream subscription");
        provider.SubscriptionsPublic.Count.Should().Be(1);
        provider.SubscriptionsPublic.GetSubscription(firstId)!.SourceOfRequest.Should().Be("watchlist");
    }

    [Fact]
    public async Task ValidateSubscriptionRequest_DetectsDuplicateAndMissingFields()
    {
        await using var provider = new StubProvider();
        var existingId = provider.SubscriptionsPublic.Subscribe("MSFT", "quotes");

        var duplicate = provider.SubscriptionsPublic.ValidateSubscriptionRequest(" msft ", "quotes");
        var missingSymbol = provider.SubscriptionsPublic.ValidateSubscriptionRequest(" ", "quotes");
        var missingKind = provider.SubscriptionsPublic.ValidateSubscriptionRequest("MSFT", " ");

        duplicate.IsValid.Should().BeTrue();
        duplicate.IsDuplicate.Should().BeTrue();
        duplicate.ExistingSubscriptionId.Should().Be(existingId);
        duplicate.NormalizedSymbol.Should().Be("msft");
        missingSymbol.IsValid.Should().BeFalse();
        missingSymbol.FailureReason.Should().Contain("Symbol");
        missingKind.IsValid.Should().BeFalse();
        missingKind.FailureReason.Should().Contain("kind");
    }

    [Fact]
    public async Task SubscriptionDiagnostics_TrackMessagesErrorsRecoveryAndStaleness()
    {
        await using var provider = new StubProvider();
        var now = DateTimeOffset.UtcNow;
        var id = provider.SubscriptionsPublic.Subscribe("QQQ", "trades", "strategy-run");

        provider.SubscriptionsPublic.RecordMessageReceived(id, now.AddMinutes(-10)).Should().BeTrue();
        provider.SubscriptionsPublic.GetStaleSubscriptions(TimeSpan.FromMinutes(5), now)
            .Should().ContainSingle(s => s.Id == id);

        provider.SubscriptionsPublic.RecordSubscriptionError(id, "provider rejected subscription").Should().BeTrue();
        provider.SubscriptionsPublic.RecordRecoveryAttempt(id, "recovering after reconnect").Should().BeTrue();

        var subscription = provider.SubscriptionsPublic.GetSubscription(id);
        subscription.Should().NotBeNull();
        subscription!.Status.Should().Be(SubscriptionStatus.Recovering);
        subscription.RecoveryAttempts.Should().Be(1);
        subscription.LastError.Should().Be("recovering after reconnect");

        var snapshot = provider.SubscriptionsPublic.GetSnapshot();
        snapshot.RecoveringSubscriptions.Should().Be(1);
        snapshot.FailedSubscriptions.Should().Be(0);
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

    [Fact]
    public async Task ConnectionDiagnosticsSource_BeforeConnect_ExposesProviderLifecycleSnapshot()
    {
        await using var provider = new StubProvider(providerName: "stub-diagnostics");
        var diagnosticsSource = (IProviderConnectionDiagnosticsSource)provider;

        var snapshot = diagnosticsSource.GetConnectionDiagnosticsSnapshot();

        snapshot.ProviderName.Should().Be("stub-diagnostics");
        snapshot.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Configured);
        snapshot.IsConnected.Should().BeFalse();
        snapshot.IsReconnecting.Should().BeFalse();
        snapshot.LastError.Should().BeNull();
    }

    [Fact]
    public async Task ConnectionDiagnosticsSource_ForwardsLifecycleChangeEvents()
    {
        await using var provider = new StubProvider();
        var diagnosticsSource = (IProviderConnectionDiagnosticsSource)provider;
        var observedStates = new List<ProviderConnectionLifecycleState>();

        diagnosticsSource.ConnectionDiagnosticsChanged += snapshot => observedStates.Add(snapshot.LifecycleState);

        await provider.DisconnectAsync();

        observedStates.Should().Contain(ProviderConnectionLifecycleState.Disconnecting);
        observedStates.Should().Contain(ProviderConnectionLifecycleState.Disconnected);
    }

    [Fact]
    public async Task RecordActivity_UpdatesProviderLevelHeartbeatDiagnostics()
    {
        await using var provider = new StubProvider();
        var diagnosticsSource = (IProviderConnectionDiagnosticsSource)provider;

        provider.RecordActivityPublic();

        var snapshot = diagnosticsSource.GetConnectionDiagnosticsSnapshot();
        snapshot.LastHeartbeatReceivedAt.Should().NotBeNull();
        snapshot.LastMessageReceivedAt.Should().BeNull("manual provider heartbeat activity is not a market-data payload");
    }

    [Fact]
    public async Task ConnectionDiagnosticsSource_IncludesSubscriptionHealthCounts()
    {
        await using var provider = new StubProvider();
        var diagnosticsSource = (IProviderConnectionDiagnosticsSource)provider;
        var now = DateTimeOffset.UtcNow;
        var activeId = provider.SubscriptionsPublic.Subscribe("SPY", "trades");
        var failedId = provider.SubscriptionsPublic.Subscribe("QQQ", "quotes");
        var recoveringId = provider.SubscriptionsPublic.Subscribe("IWM", "depth");

        provider.SubscriptionsPublic.RecordMessageReceived(activeId, now.AddSeconds(-5)).Should().BeTrue();
        provider.SubscriptionsPublic.RecordSubscriptionError(failedId, "provider rejected subscription").Should().BeTrue();
        provider.SubscriptionsPublic.RecordRecoveryAttempt(recoveringId, "recovering after reconnect").Should().BeTrue();

        var snapshot = diagnosticsSource.GetConnectionDiagnosticsSnapshot();

        snapshot.ActiveSubscriptions.Should().Be(3);
        snapshot.FailedSubscriptions.Should().Be(1);
        snapshot.RecoveringSubscriptions.Should().Be(1);
        snapshot.LastSubscriptionMessageAt.Should().Be(now.AddSeconds(-5));
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
