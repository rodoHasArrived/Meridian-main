using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

[Collection("WatchlistService serial")]
public class PortfolioImportServiceTests : IDisposable
{
    public PortfolioImportServiceTests()
    {
        WatchlistService.Instance = new WatchlistService();
    }

    public void Dispose()
    {
        WatchlistService.Instance = new WatchlistService();
    }

    [Fact]
    public async Task ImportToWatchlistAsync_WithValidEntries_CreatesOrUpdatesWatchlist()
    {
        // Arrange
        var service = PortfolioImportService.Instance; // Use singleton instance
        var mockWatchlistService = new MockWatchlistService();
        WatchlistService.Instance = mockWatchlistService;

        var entries = new[]
        {
            new PortfolioEntry { Symbol = "AAPL", Quantity = 100 },
            new PortfolioEntry { Symbol = "MSFT", Quantity = 200 },
            new PortfolioEntry { Symbol = "GOOGL", Quantity = 50 }
        };

        // Act
        var result = await service.ImportToWatchlistAsync(entries, "My Portfolio");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ImportedCount.Should().Be(3);
        mockWatchlistService.LastCreatedWatchlistName.Should().Be("My Portfolio");
        mockWatchlistService.LastCreatedSymbols.Should().BeEquivalentTo(new[] { "AAPL", "MSFT", "GOOGL" });
    }

    [Fact]
    public async Task ImportToWatchlistAsync_WithDuplicateSymbols_ImportsDistinctSymbols()
    {
        // Arrange
        var service = PortfolioImportService.Instance; // Use singleton instance
        var mockWatchlistService = new MockWatchlistService();
        WatchlistService.Instance = mockWatchlistService;

        var entries = new[]
        {
            new PortfolioEntry { Symbol = "AAPL", Quantity = 100 },
            new PortfolioEntry { Symbol = "AAPL", Quantity = 50 },
            new PortfolioEntry { Symbol = "MSFT", Quantity = 200 }
        };

        // Act
        var result = await service.ImportToWatchlistAsync(entries, "My Portfolio");

        // Assert
        result.Success.Should().BeTrue();
        result.ImportedCount.Should().Be(2);
        mockWatchlistService.LastCreatedSymbols.Should().BeEquivalentTo(new[] { "AAPL", "MSFT" });
    }

    [Fact]
    public async Task ImportToWatchlistAsync_WithEmptyEntries_ReturnsSuccess()
    {
        // Arrange
        var service = PortfolioImportService.Instance; // Use singleton instance
        var mockWatchlistService = new MockWatchlistService();
        WatchlistService.Instance = mockWatchlistService;

        var entries = Array.Empty<PortfolioEntry>();

        // Act
        var result = await service.ImportToWatchlistAsync(entries, "Empty Portfolio");

        // Assert
        result.Success.Should().BeTrue();
        result.ImportedCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportToWatchlistAsync_WithFailingWatchlistService_ReturnsFailure()
    {
        // Arrange
        var service = PortfolioImportService.Instance; // Use singleton instance
        var failingWatchlistService = new FailingWatchlistService();
        WatchlistService.Instance = failingWatchlistService;

        var entries = new[]
        {
            new PortfolioEntry { Symbol = "AAPL", Quantity = 100 }
        };

        // Act
        var result = await service.ImportToWatchlistAsync(entries, "My Portfolio");

        // Assert
        result.Success.Should().BeFalse();
        result.ImportedCount.Should().Be(0);
        result.Error.Should().NotBeNullOrEmpty();
    }

    private class MockWatchlistService : WatchlistService
    {
        public string? LastCreatedWatchlistName { get; private set; }
        public IEnumerable<string>? LastCreatedSymbols { get; private set; }

        public override Task<bool> CreateOrUpdateWatchlistAsync(string name, IEnumerable<string> symbols, CancellationToken ct = default)
        {
            LastCreatedWatchlistName = name;
            LastCreatedSymbols = symbols.ToList();
            return Task.FromResult(true);
        }
    }

    private class FailingWatchlistService : WatchlistService
    {
        public override Task<bool> CreateOrUpdateWatchlistAsync(string name, IEnumerable<string> symbols, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }
    }
}
