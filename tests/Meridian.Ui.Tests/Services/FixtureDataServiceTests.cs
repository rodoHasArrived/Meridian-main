using FluentAssertions;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="FixtureDataService"/> mock data generation.
/// </summary>
public sealed class FixtureDataServiceTests
{
    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        // Act
        var instance = FixtureDataService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = FixtureDataService.Instance;
        var instance2 = FixtureDataService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void GetMockStatusResponse_ReturnsValidConnectedStatus()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var status = service.GetMockStatusResponse();

        // Assert
        status.Should().NotBeNull();
        status.IsConnected.Should().BeTrue();
        status.Uptime.Should().BeGreaterThan(TimeSpan.Zero);
        status.Metrics.Should().NotBeNull();
        status.Metrics!.Published.Should().BeGreaterThan(0);
        status.Pipeline.Should().NotBeNull();
        status.Pipeline!.CurrentQueueSize.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetMockDisconnectedStatus_ReturnsValidDisconnectedStatus()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var status = service.GetMockDisconnectedStatus();

        // Assert
        status.Should().NotBeNull();
        status.IsConnected.Should().BeFalse();
        status.Uptime.Should().Be(TimeSpan.Zero);
        status.Metrics.Should().BeNull();
        status.Pipeline.Should().BeNull();
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    public void GetMockTradeData_ReturnsValidTradeForSymbol(string symbol)
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var trade = service.GetMockTradeData(symbol);

        // Assert
        trade.Should().NotBeNull();
        trade.Symbol.Should().Be(symbol);
        trade.Price.Should().BeGreaterThan(0);
        trade.Size.Should().BeGreaterThan(0);
        trade.Aggressor.Should().NotBeNullOrEmpty();
        trade.Venue.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    public void GetMockQuoteData_ReturnsValidQuoteForSymbol(string symbol)
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var quote = service.GetMockQuoteData(symbol);

        // Assert
        quote.Should().NotBeNull();
        quote.Symbol.Should().Be(symbol);
        quote.BidPrice.Should().BeGreaterThan(0);
        quote.AskPrice.Should().BeGreaterThan(quote.BidPrice, "Ask should be greater than Bid");
        quote.BidSize.Should().BeGreaterThan(0);
        quote.AskSize.Should().BeGreaterThan(0);
        quote.Spread.Should().Be(quote.AskPrice - quote.BidPrice);
        quote.MidPrice.Should().Be((quote.BidPrice + quote.AskPrice) / 2);
    }

    [Fact]
    public void GetMockBackfillHealth_ReturnsValidHealthStatus()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var health = service.GetMockBackfillHealth();

        // Assert
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.Providers.Should().NotBeNull();
        health.Providers.Should().HaveCountGreaterThan(0);
        health.Providers.Should().ContainKey("Alpaca");
        health.Providers!["Alpaca"].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void GetMockSymbols_ReturnsNonEmptySymbolList()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var symbols = service.GetMockSymbols();

        // Assert
        symbols.Should().NotBeNull();
        symbols.Should().NotBeEmpty();
        symbols.Should().Contain("SPY");
        symbols.Should().Contain("AAPL");
        symbols.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("SPY", 5)]
    [InlineData("AAPL", 10)]
    [InlineData("MSFT", 20)]
    public void GetMockTradesResponse_ReturnsRequestedNumberOfTrades(string symbol, int count)
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var response = service.GetMockTradesResponse(symbol, count);

        // Assert
        response.Should().NotBeNull();
        response.Symbol.Should().Be(symbol);
        response.Count.Should().Be(count);
        response.Trades.Should().HaveCount(count);
    }

    [Fact]
    public void GetMockTradesResponse_TradesHaveSequentialPrices()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var response = service.GetMockTradesResponse("SPY", 5);

        // Assert
        var trades = response.Trades.ToList();
        for (int i = 1; i < trades.Count; i++)
        {
            trades[i].Price.Should().BeGreaterThan(trades[i - 1].Price,
                "prices should increase in mock data");
        }
    }

    [Fact]
    public void GetMockTradesResponse_TradesHaveChronologicalTimestamps()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var response = service.GetMockTradesResponse("AAPL", 5);

        // Assert
        var trades = response.Trades.ToList();
        for (int i = 1; i < trades.Count; i++)
        {
            trades[i].Timestamp.Should().BeAfter(trades[i - 1].Timestamp,
                "timestamps should be chronological");
        }
    }

    [Fact]
    public async Task SimulateNetworkDelayAsync_CompletesWithinReasonableTime()
    {
        // Arrange
        var service = FixtureDataService.Instance;
        var startTime = DateTimeOffset.UtcNow;

        // Act
        await service.SimulateNetworkDelayAsync();
        var elapsed = DateTimeOffset.UtcNow - startTime;

        // Assert
        elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(40), "should have some delay");
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(700), "should not delay too long even on busy CI runners");
    }

    [Fact]
    public void GetMockStatusResponse_MetricsShowRealisticValues()
    {
        // Arrange
        var service = FixtureDataService.Instance;

        // Act
        var status = service.GetMockStatusResponse();

        // Assert
        status.Metrics.Should().NotBeNull();
        status.Metrics!.Published.Should().BeGreaterThan(status.Metrics.Dropped,
            "published should exceed dropped events");
        status.Metrics.DropRate.Should().BeInRange(0, 1,
            "drop rate should be a percentage");
        status.Metrics.EventsPerSecond.Should().BeGreaterThan(0);
        status.Metrics.Trades.Should().BeGreaterThan(0);
    }

    // ── Scenario switching ───────────────────────────────────────────────────

    [Fact]
    public void SetScenario_ChangesActiveScenario()
    {
        // Arrange
        var service = FixtureDataService.Instance;
        service.SetScenario(FixtureScenario.Connected); // reset to known state

        // Act
        service.SetScenario(FixtureScenario.Disconnected);

        // Assert
        service.ActiveScenario.Should().Be(FixtureScenario.Disconnected);

        // Cleanup
        service.SetScenario(FixtureScenario.Connected);
    }

    [Fact]
    public void SetScenario_FiresScenarioChangedEvent()
    {
        // Arrange
        var service = FixtureDataService.Instance;
        service.SetScenario(FixtureScenario.Connected);
        FixtureScenario? capturedScenario = null;
        service.ScenarioChanged += (_, s) => capturedScenario = s;

        // Act
        service.SetScenario(FixtureScenario.Error);

        // Assert
        capturedScenario.Should().Be(FixtureScenario.Error);

        // Cleanup
        service.SetScenario(FixtureScenario.Connected);
    }

    [Fact]
    public void SetScenario_SameScenario_DoesNotFireEvent()
    {
        // Arrange
        var service = FixtureDataService.Instance;
        service.SetScenario(FixtureScenario.Connected);
        var eventFired = false;
        service.ScenarioChanged += (_, _) => eventFired = true;

        // Act – set to the same scenario
        service.SetScenario(FixtureScenario.Connected);

        // Assert
        eventFired.Should().BeFalse("no change means no event");
    }

    [Theory]
    [InlineData(FixtureScenario.Connected, FixtureScenario.Disconnected)]
    [InlineData(FixtureScenario.Disconnected, FixtureScenario.Degraded)]
    [InlineData(FixtureScenario.Degraded, FixtureScenario.Error)]
    [InlineData(FixtureScenario.Error, FixtureScenario.Loading)]
    [InlineData(FixtureScenario.Loading, FixtureScenario.Connected)]
    public void CycleToNextScenario_AdvancesToExpectedNextScenario(
        FixtureScenario startScenario, FixtureScenario expectedNext)
    {
        // Arrange
        var service = FixtureDataService.Instance;
        service.SetScenario(startScenario);

        // Act
        var next = service.CycleToNextScenario();

        // Assert
        next.Should().Be(expectedNext);
        service.ActiveScenario.Should().Be(expectedNext);

        // Cleanup
        service.SetScenario(FixtureScenario.Connected);
    }

    [Theory]
    [InlineData(FixtureScenario.Connected)]
    [InlineData(FixtureScenario.Disconnected)]
    [InlineData(FixtureScenario.Degraded)]
    [InlineData(FixtureScenario.Error)]
    [InlineData(FixtureScenario.Loading)]
    public void GetStatusForActiveScenario_ReturnsNonNullStatusForAllScenarios(FixtureScenario scenario)
    {
        // Arrange
        var service = FixtureDataService.Instance;
        service.SetScenario(scenario);

        // Act
        var status = service.GetStatusForActiveScenario();

        // Assert
        status.Should().NotBeNull();
        status.TimestampUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        // Cleanup
        service.SetScenario(FixtureScenario.Connected);
    }

    [Fact]
    public void GetStatusForActiveScenario_ConnectedScenario_IsConnected()
    {
        var service = FixtureDataService.Instance;
        service.SetScenario(FixtureScenario.Connected);
        service.GetStatusForActiveScenario().IsConnected.Should().BeTrue();
        service.SetScenario(FixtureScenario.Connected);
    }

    [Fact]
    public void GetStatusForActiveScenario_DisconnectedScenario_IsNotConnected()
    {
        var service = FixtureDataService.Instance;
        service.SetScenario(FixtureScenario.Disconnected);
        service.GetStatusForActiveScenario().IsConnected.Should().BeFalse();
        service.SetScenario(FixtureScenario.Connected);
    }

    [Fact]
    public void GetMockDegradedStatus_IsConnectedWithHighDropRate()
    {
        // Act
        var status = FixtureDataService.Instance.GetMockDegradedStatus();

        // Assert
        status.IsConnected.Should().BeTrue("degraded is still connected, just partially");
        status.Metrics.Should().NotBeNull();
        status.Metrics!.DropRate.Should().BeGreaterThan(0.02f,
            "degraded scenario should have a meaningful drop rate");
        status.Pipeline!.CurrentQueueSize.Should().BeGreaterThan(0,
            "degraded scenario should show queue pressure");
    }

    [Fact]
    public void GetMockErrorStatus_IsNotConnectedWithVeryHighDropRate()
    {
        // Act
        var status = FixtureDataService.Instance.GetMockErrorStatus();

        // Assert
        status.IsConnected.Should().BeFalse();
        status.Metrics.Should().NotBeNull();
        status.Metrics!.DropRate.Should().BeGreaterThan(0.5f,
            "error scenario should have a catastrophic drop rate");
        status.Pipeline!.CurrentQueueSize.Should().BeGreaterThan((int)(status.Pipeline.QueueCapacity * 0.9),
            "error scenario queue should be nearly full");
    }

    [Fact]
    public void GetMockLoadingStatus_IsNotConnectedWithNullMetrics()
    {
        // Act
        var status = FixtureDataService.Instance.GetMockLoadingStatus();

        // Assert
        status.IsConnected.Should().BeFalse();
        status.Metrics.Should().BeNull("loading state has no metrics yet");
        status.Pipeline.Should().BeNull("loading state has no pipeline data yet");
        status.Uptime.Should().Be(TimeSpan.Zero, "loading state has zero uptime");
    }

    [Theory]
    [InlineData(FixtureScenario.Connected, "Connected")]
    [InlineData(FixtureScenario.Disconnected, "Disconnected")]
    [InlineData(FixtureScenario.Degraded, "Degraded")]
    [InlineData(FixtureScenario.Error, "Error")]
    [InlineData(FixtureScenario.Loading, "Loading")]
    public void GetScenarioLabel_ContainsExpectedKeyword(FixtureScenario scenario, string expectedKeyword)
    {
        // Act
        var label = FixtureDataService.GetScenarioLabel(scenario);

        // Assert
        label.Should().NotBeNullOrWhiteSpace();
        label.Should().Contain(expectedKeyword, because: $"label for {scenario} should contain '{expectedKeyword}'");
    }
}
