using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class OrderBookVisualizationServiceTests
{
    [Fact]
    public async Task SubscribeAsync_InitializesOrderBookState()
    {
        // Arrange
        using var service = new OrderBookVisualizationService();
        var symbol = "AAPL";
        var depthLevels = 10;

        // Act
        await service.SubscribeAsync(symbol, depthLevels);

        // Assert
        var orderBook = service.GetOrderBook(symbol);
        orderBook.Should().NotBeNull();
        orderBook!.Symbol.Should().Be(symbol);
        orderBook.DepthLevels.Should().Be(depthLevels);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesOrderBookState()
    {
        // Arrange
        using var service = new OrderBookVisualizationService();
        var symbol = "AAPL";
        await service.SubscribeAsync(symbol);

        // Act
        await service.UnsubscribeAsync(symbol);

        // Assert
        var orderBook = service.GetOrderBook(symbol);
        orderBook.Should().BeNull();
    }

    [Fact]
    public void GetOrderBook_ReturnsNullForUnsubscribedSymbol()
    {
        // Arrange
        using var service = new OrderBookVisualizationService();

        // Act
        var orderBook = service.GetOrderBook("AAPL");

        // Assert
        orderBook.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSymbols_MaintainsSeparateStates()
    {
        // Arrange
        using var service = new OrderBookVisualizationService();
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
    }
}
