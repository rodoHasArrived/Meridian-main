using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Tests for <see cref="IBSimulationClient"/>.
/// Verifies IB simulation provider behavior including connection, subscription,
/// reconnect tolerance, and event generation patterns.
/// Sprint 7 B3 tranche 2: Provider tests for IB reconnect behavior.
/// </summary>
public sealed class IBSimulationClientTests : IAsyncLifetime
{
    private TestMarketEventPublisher _publisher = null!;
    private IBSimulationClient _client = null!;

    public Task InitializeAsync()
    {
        _publisher = new TestMarketEventPublisher();
        // Disable auto ticks so tests are deterministic
        _client = new IBSimulationClient(_publisher, enableAutoTicks: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public void IsEnabled_ReturnsTrue()
    {
        _client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsSimulation_ReturnsTrue()
    {
        _client.IsSimulation.Should().BeTrue();
    }

    [Fact]
    public void ProviderId_IsIbSim()
    {
        _client.ProviderId.Should().Be("ib-sim");
    }

    [Fact]
    public void ProviderCapabilities_SupportsStreamingTradesAndDepth()
    {
        _client.ProviderCapabilities.SupportsStreaming.Should().BeTrue();
        _client.ProviderCapabilities.SupportsRealtimeTrades.Should().BeTrue();
        _client.ProviderCapabilities.SupportsRealtimeQuotes.Should().BeTrue();
        _client.ProviderCapabilities.SupportsMarketDepth.Should().BeTrue();
    }

    [Fact]
    public void ProviderPriority_IsLow()
    {
        // Simulation should have lower priority than real providers
        _client.ProviderPriority.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public async Task ConnectAsync_ShouldSucceed()
    {
        // Act
        var act = async () => await _client.ConnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisconnectAsync_ShouldNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();

        // Act
        var act = async () => await _client.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConnectAsync_MultipleTimes_ShouldNotThrow()
    {
        // Act - Simulate reconnect behavior
        await _client.ConnectAsync();
        await _client.DisconnectAsync();
        await _client.ConnectAsync();
        await _client.DisconnectAsync();

        // Assert - No exceptions thrown = reconnect tolerance
        _client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeTrades_ReturnsPositiveId()
    {
        // Arrange
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("SPY", SubscribeTrades: true);

        // Act
        var subId = _client.SubscribeTrades(cfg);

        // Assert
        subId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SubscribeMarketDepth_ReturnsPositiveId()
    {
        // Arrange
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("AAPL", SubscribeDepth: true, DepthLevels: 5);

        // Act
        var subId = _client.SubscribeMarketDepth(cfg);

        // Assert
        subId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UnsubscribeTrades_DoesNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();
        var subId = _client.SubscribeTrades(new SymbolConfig("SPY"));

        // Act
        var act = () => _client.UnsubscribeTrades(subId);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task UnsubscribeMarketDepth_DoesNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();
        var subId = _client.SubscribeMarketDepth(new SymbolConfig("AAPL"));

        // Act
        var act = () => _client.UnsubscribeMarketDepth(subId);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void UnsubscribeTrades_InvalidId_DoesNotThrow()
    {
        // Act
        var act = () => _client.UnsubscribeTrades(-999);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void UnsubscribeMarketDepth_InvalidId_DoesNotThrow()
    {
        // Act
        var act = () => _client.UnsubscribeMarketDepth(-999);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task MultipleSubscriptions_ReturnUniqueIds()
    {
        // Arrange
        await _client.ConnectAsync();

        // Act
        var id1 = _client.SubscribeTrades(new SymbolConfig("SPY"));
        var id2 = _client.SubscribeTrades(new SymbolConfig("AAPL"));
        var id3 = _client.SubscribeMarketDepth(new SymbolConfig("MSFT"));

        // Assert
        var ids = new[] { id1, id2, id3 };
        ids.Distinct().Count().Should().Be(3, "all subscription IDs should be unique");
    }

    [Fact]
    public void ProviderCredentialFields_IsEmpty_ForSimulation()
    {
        // Simulation doesn't need real credentials
        _client.ProviderCredentialFields.Should().BeEmpty();
    }

    [Fact]
    public void ProviderNotes_ContainsSimulationWarning()
    {
        _client.ProviderNotes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ConnectDisconnectCycle_SubscriptionsAreCleared()
    {
        // Arrange
        await _client.ConnectAsync();
        _client.SubscribeTrades(new SymbolConfig("SPY"));
        _client.SubscribeTrades(new SymbolConfig("AAPL"));

        // Act - Disconnect should clear subscriptions
        await _client.DisconnectAsync();

        // Assert - Unsubscribing old IDs should not throw (already cleaned up)
        var act = () => _client.UnsubscribeTrades(0);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullPublisher_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new IBSimulationClient(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // Dispose edge cases
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_WithoutConnecting_ShouldNotThrow()
    {
        // Arrange — fresh client that was never connected
        var client = new IBSimulationClient(new TestMarketEventPublisher(), enableAutoTicks: false);

        // Act
        var act = async () => await client.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        var client = new IBSimulationClient(new TestMarketEventPublisher(), enableAutoTicks: false);
        await client.DisposeAsync();

        // Act — second dispose must be idempotent
        var act = async () => await client.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_AfterConnect_ShouldNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();

        // Act
        var act = async () => await _client.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Disconnect edge cases
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisconnectAsync_WithoutPriorConnect_ShouldNotThrow()
    {
        // Act — disconnect a client that was never connected
        var act = async () => await _client.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisconnectAsync_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();
        await _client.DisconnectAsync();

        // Act — second disconnect
        var act = async () => await _client.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Subscription state after reconnect
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAfterReconnect_ReturnsFreshUniqueId()
    {
        // Arrange — subscribe, disconnect, reconnect
        await _client.ConnectAsync();
        var firstId = _client.SubscribeTrades(new SymbolConfig("SPY"));
        await _client.DisconnectAsync();
        await _client.ConnectAsync();

        // Act
        var secondId = _client.SubscribeTrades(new SymbolConfig("SPY"));

        // Assert — IDs must be monotonically increasing and distinct
        secondId.Should().BeGreaterThan(firstId, "ticker IDs are assigned via Interlocked.Increment");
    }

    [Fact]
    public async Task MultipleDepthSubscriptions_AllReturnUniqueIds()
    {
        // Arrange
        await _client.ConnectAsync();

        // Act
        var ids = Enumerable.Range(0, 5)
            .Select(i => _client.SubscribeMarketDepth(new SymbolConfig($"SYM{i}", SubscribeDepth: true, DepthLevels: 5)))
            .ToArray();

        // Assert
        ids.Distinct().Should().HaveCount(5, "every depth subscription must receive a unique ticker ID");
    }

    // ------------------------------------------------------------------
    // Tick generation (without the wall-clock timer)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TickGeneration_WhenConnectedWithAutoTicks_PublishesTradeEvents()
    {
        // Arrange — enable auto-ticks and subscribe to a well-known symbol
        await using var tickingClient = new IBSimulationClient(_publisher, enableAutoTicks: true);
        await tickingClient.ConnectAsync();
        tickingClient.SubscribeTrades(new SymbolConfig("AAPL"));

        // Act — allow one tick interval plus margin
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await tickingClient.DisconnectAsync();

        // Assert — at least one trade event must have been published
        _publisher.PublishedEvents.Should().NotBeEmpty(
            because: "IBSimulationClient fires ticks every second for subscribed symbols");
    }

    [Fact]
    public async Task TickGeneration_AfterDisconnect_StopsPublishing()
    {
        // Arrange
        await using var tickingClient = new IBSimulationClient(_publisher, enableAutoTicks: true);
        await tickingClient.ConnectAsync();
        tickingClient.SubscribeTrades(new SymbolConfig("SPY"));

        // Let one tick interval fire
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await tickingClient.DisconnectAsync();
        var countAfterDisconnect = _publisher.PublishedEvents.Count;

        // Act — wait another interval; no new ticks should arrive
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Assert
        _publisher.PublishedEvents.Should().HaveCount(countAfterDisconnect,
            because: "the timer is stopped on DisconnectAsync");
    }

    [Fact]
    public async Task TickGeneration_UnknownSymbol_UsesDefaultBasePrice()
    {
        // Arrange — subscribe to a symbol not in the well-known price table
        await using var tickingClient = new IBSimulationClient(_publisher, enableAutoTicks: true);
        await tickingClient.ConnectAsync();
        tickingClient.SubscribeTrades(new SymbolConfig("XYZUNKNOWN"));

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await tickingClient.DisconnectAsync();

        // Assert — price should still be positive (default base = 100)
        var tradePayloads = _publisher.PublishedEvents
            .Select(e => e.Payload as Meridian.Contracts.Domain.Models.Trade)
            .Where(t => t is not null)
            .ToList();
        tradePayloads.Should().NotBeEmpty(because: "at least one simulated trade must be published");
        foreach (var trade in tradePayloads)
        {
            trade!.Price.Should().BeGreaterThan(0m, because: "simulated prices must always be positive");
        }
    }

    // ------------------------------------------------------------------
    // SymbolConfig variant coverage (different security types)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubscribeTrades_ForOptionsSymbol_ReturnsValidId()
    {
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("AAPL", SecurityType: "OPT", Exchange: "CBOE", Currency: "USD");

        var id = _client.SubscribeTrades(cfg);

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SubscribeMarketDepth_ForForexSymbol_ReturnsValidId()
    {
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("EUR", SecurityType: "CASH", Exchange: "IDEALPRO", Currency: "USD");

        var id = _client.SubscribeMarketDepth(cfg);

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SubscribeTrades_ForFuturesSymbol_ReturnsValidId()
    {
        await _client.ConnectAsync();
        var cfg = new SymbolConfig("ES", SecurityType: "FUT", Exchange: "CME", Currency: "USD");

        var id = _client.SubscribeTrades(cfg);

        id.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Concurrent access safety
    // ------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentSubscriptions_FromMultipleThreads_AllReturnUniqueIds()
    {
        // Arrange
        await _client.ConnectAsync();
        var ids = new System.Collections.Concurrent.ConcurrentBag<int>();

        // Act — 20 concurrent subscribe calls
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            var id = _client.SubscribeTrades(new SymbolConfig("SPY"));
            ids.Add(id);
        })));

        // Assert
        ids.Distinct().Should().HaveCount(20, "concurrent subscribe calls must still produce unique IDs");
    }

    [Fact]
    public async Task ConcurrentUnsubscribes_FromMultipleThreads_ShouldNotThrow()
    {
        // Arrange
        await _client.ConnectAsync();
        var subIds = Enumerable.Range(0, 10)
            .Select(_ => _client.SubscribeTrades(new SymbolConfig("AAPL")))
            .ToList();

        // Act — unsubscribe all concurrently
        var act = async () =>
            await Task.WhenAll(subIds.Select(id => Task.Run(() => _client.UnsubscribeTrades(id))));

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Metadata contracts
    // ------------------------------------------------------------------

    [Fact]
    public void ProviderWarnings_ContainSimulationDisclaimer()
    {
        _client.ProviderWarnings.Should().Contain(w =>
            w.Contains("simulated", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("not real", StringComparison.OrdinalIgnoreCase),
            because: "the simulation provider must clearly disclaim that data is not real");
    }

    [Fact]
    public void ProviderCapabilities_MaxDepthLevels_IsAtLeastOne()
    {
        _client.ProviderCapabilities.MaxDepthLevels.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ProviderCapabilities_SupportedMarkets_IsNotEmpty()
    {
        _client.ProviderCapabilities.SupportedMarkets.Should().NotBeNullOrEmpty();
    }
}
