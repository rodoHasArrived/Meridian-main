using FluentAssertions;
using Meridian.Ui.Services.Services;
using Moq;

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
    public async Task SimulateNetworkDelayAsync_UsesConfiguredClockWithinSimulationDelayRange()
    {
        var timeProvider = new Mock<TimeProvider>();
        TimerCallback? fireTimer = null;
        object? timerState = null;
        TimeSpan? scheduledDelay = null;
        TimeSpan? scheduledPeriod = null;
        timeProvider.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Callback<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, dueTime, period) =>
            {
                fireTimer = callback;
                timerState = state;
                scheduledDelay = dueTime;
                scheduledPeriod = period;
            })
            .Returns(Mock.Of<ITimer>());
        var service = new FixtureDataService(timeProvider.Object);

        var delay = service.SimulateNetworkDelayAsync();

        scheduledDelay.Should().NotBeNull();
        scheduledDelay!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));
        scheduledDelay.Value.Should().BeLessThan(TimeSpan.FromMilliseconds(150));
        scheduledPeriod.Should().Be(Timeout.InfiniteTimeSpan);
        delay.IsCompleted.Should().BeFalse("the simulated delay must await its timer");
        fireTimer.Should().NotBeNull();

        fireTimer!(timerState);
        await delay;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimulateNetworkDelayAsync_HonorsCancellation(bool cancelBeforeStarting)
    {
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns(Mock.Of<ITimer>());
        var service = new FixtureDataService(timeProvider.Object);
        using var cancellation = new CancellationTokenSource();
        if (cancelBeforeStarting)
        {
            cancellation.Cancel();
        }

        var delay = service.SimulateNetworkDelayAsync(cancellation.Token);
        if (!cancelBeforeStarting)
        {
            delay.IsCompleted.Should().BeFalse();
            cancellation.Cancel();
        }

        // Bound a broken cancellation path without using wall-clock elapsed time as the
        // simulation-delay contract. The timer itself remains controlled by this test.
        var failure = await Record.ExceptionAsync(() => delay.WaitAsync(TimeSpan.FromSeconds(5)));
        failure.Should().BeAssignableTo<OperationCanceledException>()
            .Which.CancellationToken.Should().Be(cancellation.Token);
        if (cancelBeforeStarting)
        {
            timeProvider.Verify(clock => clock.CreateTimer(
                    It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()),
                Times.Never);
        }
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
}
