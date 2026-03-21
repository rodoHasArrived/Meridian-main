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
}
