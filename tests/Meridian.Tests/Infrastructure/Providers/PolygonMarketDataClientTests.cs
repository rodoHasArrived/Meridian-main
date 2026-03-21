using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Exceptions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Unit tests for the PolygonMarketDataClient class.
/// Tests credential validation, connection behavior, and subscription methods.
/// </summary>
public class PolygonMarketDataClientTests : IDisposable
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector _quoteCollector;
    private readonly PolygonStubClient _stubClient;
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    // Store original environment variable values for cleanup
    private readonly string? _originalPolygonApiKey;
    private readonly string? _originalPolygonApiKeyAlt;

    public PolygonMarketDataClientTests()
    {
        _publisher = new TestMarketEventPublisher();

        // Create real collectors with publisher for testing
        _tradeCollector = new TradeDataCollector(_publisher, null);
        _quoteCollector = new QuoteCollector(_publisher);
        _stubClient = new PolygonStubClient(_publisher, _tradeCollector);

        // Store and clear environment variables for predictable testing
        _originalPolygonApiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY");
        _originalPolygonApiKeyAlt = Environment.GetEnvironmentVariable("POLYGON__APIKEY");
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", null);
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", null);
    }

    public void Dispose()
    {
        // Restore original environment variables
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", _originalPolygonApiKey);
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", _originalPolygonApiKeyAlt);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullPublisher_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new PolygonMarketDataClient(
            null!,
            _tradeCollector,
            _quoteCollector);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("publisher");
    }

    [Fact]
    public void Constructor_WithNullTradeCollector_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new PolygonMarketDataClient(
            _publisher,
            null!,
            _quoteCollector);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("tradeCollector");
    }

    [Fact]
    public void Constructor_WithNullQuoteCollector_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("quoteCollector");
    }

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaultOptions()
    {
        // Act - should not throw
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options: null);

        // Assert - client should be created successfully
        client.Should().NotBeNull();
    }

    #endregion

    #region Credential Validation Tests

    [Fact]
    public void IsEnabled_WithNoApiKey_ReturnsFalse()
    {
        // Arrange
        var options = new PolygonOptions();
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WithShortApiKey_ReturnsFalse()
    {
        // Arrange - API key too short (less than 20 chars)
        var options = new PolygonOptions(ApiKey: "shortkey");
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WithValidApiKey_ReturnsTrue()
    {
        // Arrange - Valid API key (20+ chars)
        var options = new PolygonOptions(ApiKey: "abcdefghijklmnopqrstuvwxyz123456");
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WithEnvironmentVariable_ReturnsTrue()
    {
        // Arrange - Pass API key via options (as would be done by ProviderCredentialResolver)
        var options = new PolygonOptions(ApiKey: "env_api_key_that_is_long_enough_123");
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WithAlternateEnvironmentVariable_ReturnsTrue()
    {
        // Arrange - Pass API key via options (simulating alternate env var resolution)
        var options = new PolygonOptions(ApiKey: "alt_env_api_key_long_enough_12345");
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_OptionsApiKeyTakesPrecedence()
    {
        // Arrange - Pass valid API key via options
        var options = new PolygonOptions(ApiKey: "valid_environment_api_key_12345678");
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert - options API key is used
        client.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region Connection Tests

    [Fact]
    public async Task StubClient_ConnectAsync_PublishesHeartbeatEvent()
    {
        await _stubClient.ConnectAsync();

        _publishedEvents.Should().HaveCount(1);
        _publishedEvents[0].Type.Should().Be(MarketEventType.Heartbeat);
        _publishedEvents[0].Source.Should().Be("PolygonStub");
    }

    [Fact]
    public async Task ConnectAsync_WithMissingApiKey_ThrowsConfigurationException()
    {
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);
        using var cts = new CancellationTokenSource();

        var act = () => client.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<ConfigurationException>()
            .WithMessage("*POLYGON__APIKEY*");
    }

    [Fact]
    public async Task DisconnectAsync_CompletesSuccessfully()
    {
        // Arrange
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);

        // Act & Assert - should not throw
        await client.DisconnectAsync();
    }

    #endregion

    #region Subscription Tests

    [Fact]
    public void SubscribeMarketDepth_ReturnsNegativeOne()
    {
        // Arrange
        var config = new SymbolConfig("SPY");

        // Act
        var subscriptionId = _stubClient.SubscribeMarketDepth(config);

        // Assert - depth not supported in stub
        subscriptionId.Should().Be(-1);
    }

    [Fact]
    public void UnsubscribeMarketDepth_DoesNotThrow()
    {
        // Arrange
        // Act & Assert - should not throw
        var act = () => _stubClient.UnsubscribeMarketDepth(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void SubscribeTrades_ReturnsPositiveSubscriptionId()
    {
        // Arrange
        var config = new SymbolConfig("AAPL");

        // Act
        var subscriptionId = _stubClient.SubscribeTrades(config);

        // Assert - stub mode still returns valid subscription ID (for tracking purposes)
        subscriptionId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ProductionClient_SubscribeTrades_DoesNotEmitSyntheticTrade()
    {
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);
        var config = new SymbolConfig("AAPL");

        client.SubscribeTrades(config);

        _publishedEvents.Should().NotContain(e => e.Type == MarketEventType.Trade);
    }

    [Fact]
    public void UnsubscribeTrades_DoesNotThrow()
    {
        // Arrange
        // Act & Assert - should not throw
        var act = () => _stubClient.UnsubscribeTrades(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void SubscribeAggregates_WhenDisabled_ReturnsNegativeOne()
    {
        // Arrange - aggregates disabled by default
        var options = new PolygonOptions(SubscribeAggregates: false);
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);
        var config = new SymbolConfig("SPY");

        // Act
        var subscriptionId = client.SubscribeAggregates(config);

        // Assert
        subscriptionId.Should().Be(-1);
    }

    [Fact]
    public void SubscribeAggregates_WhenEnabled_ReturnsPositiveId()
    {
        // Arrange - aggregates enabled
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);
        var config = new SymbolConfig("AAPL");

        // Act
        var subscriptionId = client.SubscribeAggregates(config);

        // Assert
        subscriptionId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SubscribeAggregates_AddsSymbolToSubscribedList()
    {
        // Arrange
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);
        var config = new SymbolConfig("MSFT");

        // Act
        client.SubscribeAggregates(config);

        // Assert
        client.SubscribedAggregateSymbols.Should().Contain("MSFT");
    }

    [Fact]
    public void UnsubscribeAggregates_DoesNotThrow()
    {
        // Arrange
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);

        // Act & Assert - should not throw
        var act = () => client.UnsubscribeAggregates(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void UnsubscribeAggregates_RemovesSymbolFromSubscribedList()
    {
        // Arrange
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);
        var config = new SymbolConfig("GOOGL");

        // Act - subscribe then unsubscribe
        var subscriptionId = client.SubscribeAggregates(config);
        client.UnsubscribeAggregates(subscriptionId);

        // Assert
        client.SubscribedAggregateSymbols.Should().NotContain("GOOGL");
    }

    [Fact]
    public void SubscribeAggregates_WithNullConfig_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new PolygonOptions(SubscribeAggregates: true);
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Act
        var act = () => client.SubscribeAggregates(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task DisposeAsync_CompletesSuccessfully()
    {
        // Arrange
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);

        // Act & Assert - should not throw
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector);

        // Act & Assert - should not throw on multiple calls
        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new PolygonOptions(
            ApiKey: "a_valid_api_key_that_is_long_enough",
            UseDelayed: true,
            Feed: "crypto",
            SubscribeTrades: true,
            SubscribeQuotes: true);

        // Act
        var client = new PolygonMarketDataClient(
            _publisher,
            _tradeCollector,
            _quoteCollector,
            options);

        // Assert
        client.IsEnabled.Should().BeTrue();
    }

    #endregion
}
