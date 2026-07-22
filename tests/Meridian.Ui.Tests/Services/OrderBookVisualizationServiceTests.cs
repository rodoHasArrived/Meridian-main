using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class OrderBookVisualizationServiceTests
{
    [Fact]
    public async Task SubscribeAsync_InitializesOrderBookState()
    {
        // Arrange
        var liveData = new RecordingLiveDataSubscriptionClient();
        using var service = new OrderBookVisualizationService(liveData);
        var symbol = "AAPL";
        var depthLevels = 10;

        // Act
        await service.SubscribeAsync(symbol, depthLevels);

        // Assert
        var orderBook = service.GetOrderBook(symbol);
        orderBook.Should().NotBeNull();
        orderBook!.Symbol.Should().Be(symbol);
        orderBook.DepthLevels.Should().Be(depthLevels);
        liveData.SubscribeRequests.Should().ContainSingle(request =>
            request.Symbol == symbol &&
            request.SubscribeDepth &&
            request.DepthLevels == depthLevels);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesOrderBookState()
    {
        // Arrange
        var liveData = new RecordingLiveDataSubscriptionClient();
        using var service = new OrderBookVisualizationService(liveData);
        var symbol = "AAPL";
        await service.SubscribeAsync(symbol);

        // Act
        await service.UnsubscribeAsync(symbol);

        // Assert
        var orderBook = service.GetOrderBook(symbol);
        orderBook.Should().BeNull();
        liveData.UnsubscribeSymbols.Should().ContainSingle(symbol);
    }

    [Fact]
    public void GetOrderBook_ReturnsNullForUnsubscribedSymbol()
    {
        // Arrange
        using var service = new OrderBookVisualizationService(new RecordingLiveDataSubscriptionClient());

        // Act
        var orderBook = service.GetOrderBook("AAPL");

        // Assert
        orderBook.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSymbols_MaintainsSeparateStates()
    {
        // Arrange
        var liveData = new RecordingLiveDataSubscriptionClient();
        using var service = new OrderBookVisualizationService(liveData);
        var symbol1 = "AAPL";
        var symbol2 = "MSFT";

        // Act
        await service.SubscribeAsync(symbol1, 10);
        await service.SubscribeAsync(symbol2, 20);

        // Assert
        var orderBook1 = service.GetOrderBook(symbol1);
        var orderBook2 = service.GetOrderBook(symbol2);

        orderBook1.Should().NotBeNull();
        orderBook2.Should().NotBeNull();
        orderBook1!.Symbol.Should().Be(symbol1);
        orderBook2!.Symbol.Should().Be(symbol2);
        orderBook1.DepthLevels.Should().Be(10);
        orderBook2.DepthLevels.Should().Be(20);
        liveData.SubscribeRequests.Should().HaveCount(2);
    }

    private sealed class RecordingLiveDataSubscriptionClient : IOrderBookLiveDataSubscriptionClient
    {
        public List<SubscribeRequest> SubscribeRequests { get; } = [];

        public List<string> UnsubscribeSymbols { get; } = [];

        public Task SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
        {
            SubscribeRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string symbol, CancellationToken ct = default)
        {
            UnsubscribeSymbols.Add(symbol);
            return Task.CompletedTask;
        }
    }
}
