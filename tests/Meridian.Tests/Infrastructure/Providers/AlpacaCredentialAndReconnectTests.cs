using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Tests for <see cref="AlpacaMarketDataClient"/> credential validation and
/// reconnection behavior.
/// Sprint 7 B3 tranche 2: Provider tests for Alpaca credential-refresh behavior.
/// </summary>
public sealed class AlpacaCredentialAndReconnectTests
{
    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, quoteCollector, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyKeyId_ThrowsArgumentException()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions { KeyId = "", SecretKey = "valid-secret" };

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptySecretKey_ThrowsArgumentException()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions { KeyId = "valid-key", SecretKey = "" };

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhitespaceCredentials_ThrowsArgumentException()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions { KeyId = "   ", SecretKey = "   " };

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ValidCredentials_DoesNotThrow()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ValidCredentials_ClientIsEnabled()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };

        // Act
        var client = new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        client.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ProviderMetadata_HasCorrectValues()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };
        var client = new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        client.ProviderId.Should().Be("alpaca");
        client.ProviderDisplayName.Should().Contain("Alpaca");
        client.ProviderPriority.Should().BeLessThan(50, "Alpaca should have relatively high priority");
        client.ProviderCapabilities.SupportsStreaming.Should().BeTrue();
        client.ProviderCapabilities.SupportsRealtimeTrades.Should().BeTrue();
        client.ProviderCapabilities.SupportsRealtimeQuotes.Should().BeTrue();
        client.ProviderCapabilities.SupportsMarketDepth.Should().BeFalse("Alpaca doesn't support full L2");
    }

    [Fact]
    public void ProviderCapabilities_RateLimits_AreConfigured()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };
        var client = new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Assert
        client.ProviderCapabilities.MaxRequestsPerWindow.Should().BeGreaterThan(0);
        client.ProviderCapabilities.RateLimitWindow.Should().NotBeNull();
        client.ProviderCapabilities.MinRequestDelay.Should().NotBeNull();
    }

    [Fact]
    public async Task ConnectAsync_WithFakeCredentials_ThrowsOrCompletesGracefully()
    {
        // Arrange - Client with valid format but non-real credentials
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKFAKEKEY12345",
            SecretKey = "fake-secret-key-not-real"
        };
        var client = new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Act - Connect will try WebSocket, which should fail gracefully
        // with fake credentials (no live server)
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(cts.Token);

            // If connect completes (e.g., WebSocket opens but auth fails later),
            // that's still an acceptable test outcome
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException or InvalidOperationException)
        {
            // Expected - connection to Alpaca WebSocket will fail with fake credentials
            // or timeout, which demonstrates the client handles it gracefully
        }

        // Assert - Dispose should not throw
        var disposeAct = async () => await client.DisposeAsync();
        await disposeAct.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithoutConnect_DoesNotThrow()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };
        var client = new AlpacaMarketDataClient(tradeCollector, quoteCollector, opts);

        // Act
        var act = async () => await client.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void NullQuoteCollector_ThrowsArgumentNullException()
    {
        // Arrange
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher);
        var opts = new AlpacaOptions
        {
            KeyId = "AKTEST12345678",
            SecretKey = "test-secret-key-value-12345"
        };

        // Act
        var act = () => new AlpacaMarketDataClient(tradeCollector, null!, opts);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
