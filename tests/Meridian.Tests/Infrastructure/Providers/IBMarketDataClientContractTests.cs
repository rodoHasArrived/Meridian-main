using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Resilience;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="IBMarketDataClient"/>, the top-level IB streaming facade.
/// <para>
/// In non-IBAPI builds the facade delegates to <see cref="IBSimulationClient"/>.
/// This test validates the <em>outer</em> facade — its metadata, subscription wiring,
/// and disposal contract — regardless of whether the IBAPI package is present.
/// The separate <see cref="IBSimulationClientContractTests"/> covers the inner
/// simulation client in isolation.
/// </para>
/// </summary>
public sealed class IBMarketDataClientContractTests : MarketDataClientContractTests<IBMarketDataClient>
{
    protected override IBMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var depthCollector = new MarketDepthCollector(publisher);
        return new IBMarketDataClient(publisher, tradeCollector, depthCollector);
    }
}

/// <summary>
/// Verifies the IB facade reports honest connection diagnostics: lifecycle transitions
/// track real connect/disconnect calls, and simulation builds are labelled as such.
/// </summary>
public sealed class IBMarketDataClientDiagnosticsTests
{
    [Fact]
    public void NonVendorBuild_DoesNotClaimLiveInteractiveBrokersCapability()
    {
#if !IBAPI_VENDOR
        IBMarketDataClient.IsSimulationBuild.Should().BeTrue();
        var descriptor = Meridian.Infrastructure.Adapters.Core.ProviderCapabilityDescriptorCatalog.Descriptors
            .Single(static value => value.ProviderId == "ibkr");
        descriptor.ExecutionMode.Should().Be(Meridian.Infrastructure.Adapters.Core.IBProviderCapabilityExecutionMode.SimulationWhenVendorSdkUnavailable);
#endif
    }

    [Fact]
    public async Task Diagnostics_TrackConnectAndDisconnectHonestly()
    {
        var publisher = new TestMarketEventPublisher();
        await using var client = new IBMarketDataClient(
            publisher,
            new TradeDataCollector(publisher, null),
            new MarketDepthCollector(publisher));

        var initial = client.GetConnectionDiagnosticsSnapshot();
        initial.IsConnected.Should().BeFalse();
        initial.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Configured);

        await client.ConnectAsync();
        var connected = client.GetConnectionDiagnosticsSnapshot();
        connected.IsConnected.Should().BeTrue();
        connected.LastConnectedAt.Should().NotBeNull();
        if (client.IsSimulation)
        {
            connected.ProviderName.Should().ContainEquivalentOf("simulation",
                "operators must be able to tell synthetic data from a live TWS connection");
        }

        await client.DisconnectAsync();
        var disconnected = client.GetConnectionDiagnosticsSnapshot();
        disconnected.IsConnected.Should().BeFalse();
        disconnected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        disconnected.LastDisconnectedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StreamingRateLimitDiagnostics_TrackRequestsAndPacingViolations()
    {
        var publisher = new TestMarketEventPublisher();
        await using var client = new IBMarketDataClient(
            publisher,
            new TradeDataCollector(publisher, null),
            new MarketDepthCollector(publisher));
        var diagnostics = client.Should().BeAssignableTo<IProviderRateLimitDiagnosticsSource>().Subject;

        client.SubscribeTrades(new Meridian.Contracts.Configuration.SymbolConfig("AAPL"));
        var afterRequest = diagnostics.GetRateLimitDiagnosticsSnapshot();
        afterRequest.Surface.Should().Be(ProviderRateLimitSurfaces.Streaming);
        afterRequest.RequestsInWindow.Should().BeGreaterThan(0);

        client.RecordPacingViolation(TimeSpan.FromSeconds(10));
        var limited = diagnostics.GetRateLimitDiagnosticsSnapshot();
        limited.IsRateLimited.Should().BeTrue();
        limited.Reason.Should().Be("provider-response");
        limited.ResetAt.Should().NotBeNull();
    }
}

/// <summary>
/// Runs against the maintained IB API smoke stub when EnableIbApiSmoke=true. The
/// enabled slice requires the runtime callback surface so a simulation build cannot pass
/// as reconnect evidence. The default build has no callback surface and exits early.
/// </summary>
public sealed class IBMarketDataClientRuntimeReconnectTests
{
    [Fact]
    public async Task RuntimeConnectionLoss_ReplaysEveryLiveSubscriptionInsideReconnectTransaction()
    {
        var connectionClosed = typeof(EnhancedIBConnectionManager).GetMethod("connectionClosed");
#if IBAPI_SMOKE
        connectionClosed.Should().NotBeNull(
            "EnableIbApiSmoke=true must exercise the IB API runtime callback surface");
#else
        if (connectionClosed is null)
            return;
#endif

        var publisher = new TestMarketEventPublisher();
        var router = new IBCallbackRouter(
            new MarketDepthCollector(publisher),
            new TradeDataCollector(publisher, null),
            new QuoteCollector(publisher));
        using var manager = new EnhancedIBConnectionManager(
            router,
            enableHeartbeat: false);

        var requestCount = 0;
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestEvent = typeof(EnhancedIBConnectionManager).GetEvent("StreamingRequestSent");
        var restoredEvent = typeof(EnhancedIBConnectionManager).GetEvent("ConnectionRestored");
        var diagnosticsMethod = typeof(EnhancedIBConnectionManager)
            .GetMethod("GetConnectionDiagnosticsSnapshot");

        requestEvent.Should().NotBeNull();
        restoredEvent.Should().NotBeNull();
        diagnosticsMethod.Should().NotBeNull();

        EventHandler requestHandler = (_, _) => Interlocked.Increment(ref requestCount);
        EventHandler restoredHandler = (_, _) => restored.TrySetResult();
        requestEvent!.AddEventHandler(manager, requestHandler);
        restoredEvent!.AddEventHandler(manager, restoredHandler);

        try
        {
            await manager.ConnectAsync();
            manager.SubscribeTrades(new Meridian.Contracts.Configuration.SymbolConfig("AAPL"));
            manager.SubscribeMarketDepth(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
            requestCount.Should().Be(2);

            connectionClosed!.Invoke(manager, null);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));

            requestCount.Should().Be(4,
                "the live adapter must replay each retained trade/depth request exactly once after reconnect");
            var snapshot = diagnosticsMethod!.Invoke(manager, null)
                .Should().BeOfType<WebSocketConnectionDiagnostics>().Subject;
            snapshot.IsConnected.Should().BeTrue();
            snapshot.ReconnectAttempts.Should().Be(1);
            snapshot.ActiveSubscriptions.Should().Be(2);
        }
        finally
        {
            requestEvent.RemoveEventHandler(manager, requestHandler);
            restoredEvent.RemoveEventHandler(manager, restoredHandler);
        }
    }

    [Fact]
    public async Task UnsubscribeMarketDepth_StopsRoutingLateCallbacksForTheCancelledTicker()
    {
        var connectionClosed = typeof(EnhancedIBConnectionManager).GetMethod("connectionClosed");
#if IBAPI_SMOKE
        connectionClosed.Should().NotBeNull(
            "EnableIbApiSmoke=true must exercise the IB API runtime callback surface");
#else
        if (connectionClosed is null)
            return;
#endif

        var publisher = new TestMarketEventPublisher();
        var depth = new MarketDepthCollector(publisher, requireExplicitSubscription: false);
        var router = new IBCallbackRouter(depth, new TradeDataCollector(publisher, null), new QuoteCollector(publisher));
        using var manager = new EnhancedIBConnectionManager(router, enableHeartbeat: false);
        await manager.ConnectAsync();

        var tickerId = manager.SubscribeMarketDepth(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
        manager.UnsubscribeMarketDepth(tickerId);
        depth.ResetSymbolStream("MSFT");
        router.UpdateMktDepth(tickerId, 0, 0, 0, 400.10, 100);

        depth.GetCurrentSnapshot("MSFT").Should().BeNull("TWS can deliver callbacks after cancelMktDepth");
    }

    [Fact]
    public async Task RuntimeConnectionLoss_ResetsDepthBooksBeforeReplay()
    {
        var connectionClosed = typeof(EnhancedIBConnectionManager).GetMethod("connectionClosed");
#if IBAPI_SMOKE
        connectionClosed.Should().NotBeNull(
            "EnableIbApiSmoke=true must exercise the IB API runtime callback surface");
#else
        if (connectionClosed is null)
            return;
#endif

        var publisher = new TestMarketEventPublisher();
        var depth = new MarketDepthCollector(publisher, requireExplicitSubscription: false);
        var router = new IBCallbackRouter(depth, new TradeDataCollector(publisher, null), new QuoteCollector(publisher));
        using var manager = new EnhancedIBConnectionManager(router, enableHeartbeat: false);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoredEvent = typeof(EnhancedIBConnectionManager).GetEvent("ConnectionRestored");
        restoredEvent.Should().NotBeNull();
        EventHandler restoredHandler = (_, _) => restored.TrySetResult();
        restoredEvent!.AddEventHandler(manager, restoredHandler);

        try
        {
            await manager.ConnectAsync();
            var tickerId = manager.SubscribeMarketDepth(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
            router.UpdateMktDepth(tickerId, 0, 0, 0, 400.10, 100);

            connectionClosed!.Invoke(manager, null);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
            router.UpdateMktDepth(tickerId, 0, 0, 0, 400.10, 100); // TWS replays the book from position 0

            depth.GetCurrentSnapshot("MSFT")!.Bids.Should().ContainSingle(
                "the replayed level must replace, not duplicate, the one held before the drop");
        }
        finally
        {
            restoredEvent.RemoveEventHandler(manager, restoredHandler);
        }
    }

    [Fact]
    public async Task ConnectionLoss_UnregistersTickersAndClearsDepthBooks_UntilReplay()
    {
        var connectionClosed = typeof(EnhancedIBConnectionManager).GetMethod("connectionClosed");
#if IBAPI_SMOKE
        connectionClosed.Should().NotBeNull(
            "EnableIbApiSmoke=true must exercise the IB API runtime callback surface");
#else
        if (connectionClosed is null)
            return;
#endif

        var publisher = new TestMarketEventPublisher();
        var depth = new MarketDepthCollector(publisher, requireExplicitSubscription: false);
        var router = new IBCallbackRouter(depth, new TradeDataCollector(publisher, null), new QuoteCollector(publisher));
        using var manager = new EnhancedIBConnectionManager(router, enableAutoReconnect: false, enableHeartbeat: false);
        await manager.ConnectAsync();
        var tickerId = manager.SubscribeMarketDepth(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
        router.UpdateMktDepth(tickerId, 0, 0, 0, 400.10, 100);

        connectionClosed!.Invoke(manager, null);
        router.UpdateMktDepth(tickerId, 0, 0, 0, 399.00, 50); // a straggler from the dead session

        depth.GetCurrentSnapshot("MSFT").Should().BeNull(
            "while the link is down the book is cleared, not left frozen, and nothing routes into it");
    }

    [Fact]
    public async Task Reconnect_RoutesOnlyRetainedSubscriptions()
    {
        var connectionClosed = typeof(EnhancedIBConnectionManager).GetMethod("connectionClosed");
#if IBAPI_SMOKE
        connectionClosed.Should().NotBeNull(
            "EnableIbApiSmoke=true must exercise the IB API runtime callback surface");
#else
        if (connectionClosed is null)
            return;
#endif

        var publisher = new TestMarketEventPublisher();
        var depth = new MarketDepthCollector(publisher, requireExplicitSubscription: false);
        var router = new IBCallbackRouter(depth, new TradeDataCollector(publisher, null), new QuoteCollector(publisher));
        using var manager = new EnhancedIBConnectionManager(router, enableHeartbeat: false);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoredEvent = typeof(EnhancedIBConnectionManager).GetEvent("ConnectionRestored");
        restoredEvent.Should().NotBeNull();
        EventHandler restoredHandler = (_, _) => restored.TrySetResult();
        restoredEvent!.AddEventHandler(manager, restoredHandler);

        try
        {
            await manager.ConnectAsync();
            var retained = manager.SubscribeMarketDepth(new Meridian.Contracts.Configuration.SymbolConfig("MSFT"));
            router.RegisterDepthTicker(9_999, "STRAY"); // a registration with no retained subscription

            connectionClosed!.Invoke(manager, null);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
            router.UpdateMktDepth(retained, 0, 0, 0, 400.10, 100);
            router.UpdateMktDepth(9_999, 0, 0, 0, 10.00, 1);

            depth.GetCurrentSnapshot("MSFT").Should().NotBeNull("the retained subscription is replayed and routes again");
            depth.GetCurrentSnapshot("STRAY").Should().BeNull("reconnect leaves only retained subscriptions routable");
        }
        finally
        {
            restoredEvent.RemoveEventHandler(manager, restoredHandler);
        }
    }
}
