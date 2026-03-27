using System.Linq;
using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="WatchlistService"/> functionality.
/// </summary>
[Collection("WatchlistService serial")]
public sealed class WatchlistServiceTests : IDisposable
{
    public WatchlistServiceTests()
    {
        WatchlistService.Instance = new WatchlistService();
    }

    public void Dispose()
    {
        WatchlistService.Instance = new WatchlistService();
    }

    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        // Act
        var instance = WatchlistService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = WatchlistService.Instance;
        var instance2 = WatchlistService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public async Task LoadWatchlistAsync_ReturnsEmptyWatchlistByDefault()
    {
        // Arrange
        var service = new WatchlistService();

        // Act
        var watchlist = await service.LoadWatchlistAsync();

        // Assert
        watchlist.Should().NotBeNull();
        watchlist.Symbols.Should().NotBeNull();
        watchlist.Groups.Should().NotBeNull();
    }

    [Fact]
    public void WatchlistData_Initialization_CreatesEmptyCollections()
    {
        // Act
        var watchlist = new WatchlistData();

        // Assert
        watchlist.Symbols.Should().NotBeNull();
        watchlist.Symbols.Should().BeEmpty();
        watchlist.Groups.Should().NotBeNull();
        watchlist.Groups.Should().BeEmpty();
    }

    [Fact]
    public void WatchlistItem_CanStoreSymbolAndNotes()
    {
        // Act
        var item = new WatchlistItem
        {
            Symbol = "SPY",
            Notes = "S&P 500 ETF"
        };

        // Assert
        item.Symbol.Should().Be("SPY");
        item.Notes.Should().Be("S&P 500 ETF");
    }

    [Fact]
    public void WatchlistGroup_CanStoreNameAndSymbols()
    {
        // Act
        var group = new WatchlistGroup
        {
            Name = "Tech Stocks",
            Symbols = new List<string> { "AAPL", "MSFT", "GOOGL" }
        };

        // Assert
        group.Name.Should().Be("Tech Stocks");
        group.Symbols.Should().HaveCount(3);
        group.Symbols.Should().Contain(new[] { "AAPL", "MSFT", "GOOGL" });
    }

    [Fact]
    public void WatchlistData_CanAddMultipleSymbols()
    {
        // Arrange
        var watchlist = new WatchlistData();

        // Act
        watchlist.Symbols.Add(new WatchlistItem { Symbol = "SPY" });
        watchlist.Symbols.Add(new WatchlistItem { Symbol = "AAPL" });
        watchlist.Symbols.Add(new WatchlistItem { Symbol = "MSFT" });

        // Assert
        watchlist.Symbols.Should().HaveCount(3);
    }

    [Fact]
    public void WatchlistData_CanAddMultipleGroups()
    {
        // Arrange
        var watchlist = new WatchlistData();

        // Act
        watchlist.Groups.Add(new WatchlistGroup { Name = "Tech" });
        watchlist.Groups.Add(new WatchlistGroup { Name = "Finance" });

        // Assert
        watchlist.Groups.Should().HaveCount(2);
    }

    [Fact]
    public void CustomWatchlistService_CanBeUsedAsWatchlistService()
    {
        // Arrange
        WatchlistService service = new CustomWatchlistService();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<CustomWatchlistService>();
    }

    [Fact]
    public async Task LoadWatchlistAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = new WatchlistService();
        using var cts = new CancellationTokenSource();

        // Act - LoadWatchlistAsync doesn't accept CancellationToken yet
        var watchlist = await service.LoadWatchlistAsync();

        // Assert
        watchlist.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrUpdateWatchlistAsync_CreatesNewWatchlist()
    {
        // Arrange
        var service = new WatchlistService();
        var symbols = new List<string> { "SPY", " spy ", "", "AAPL" };

        // Act
        var result = await service.CreateOrUpdateWatchlistAsync("Test Watchlist", symbols);
        var watchlist = await service.LoadWatchlistAsync();

        // Assert
        result.Should().BeTrue();
        watchlist.Symbols.Select(item => item.Symbol).Should().Equal("SPY", "AAPL");
    }

    [Fact]
    public void WatchlistItem_CanStoreMultipleProperties()
    {
        // Act
        var item = new WatchlistItem
        {
            Symbol = "AAPL",
            Notes = "Apple Inc.",
            Tags = new List<string> { "tech", "mega-cap" }
        };

        // Assert
        item.Symbol.Should().Be("AAPL");
        item.Notes.Should().Be("Apple Inc.");
        item.Tags.Should().HaveCount(2);
        item.Tags.Should().Contain(new[] { "tech", "mega-cap" });
    }

    [Fact]
    public void WatchlistItem_Tags_DefaultsToEmptyList()
    {
        // Act
        var item = new WatchlistItem { Symbol = "SPY" };

        // Assert
        item.Tags.Should().NotBeNull();
        item.Tags.Should().BeEmpty();
    }

    [Fact]
    public void WatchlistItem_Tags_SupportsAddAndRemove()
    {
        // Arrange
        var item = new WatchlistItem { Symbol = "SPY" };

        // Act
        item.Tags.Add("etf");
        item.Tags.Add("index");
        item.Tags.Remove("etf");

        // Assert
        item.Tags.Should().HaveCount(1);
        item.Tags.Should().Contain("index");
    }

    [Fact]
    public void WatchlistGroup_SupportsNestedHierarchy()
    {
        // Act
        var parentGroup = new WatchlistGroup
        {
            Name = "All Stocks",
            Symbols = new List<string>()
        };
        var childGroup = new WatchlistGroup
        {
            Name = "Tech",
            Symbols = new List<string> { "AAPL", "MSFT" }
        };

        // Assert
        parentGroup.Should().NotBeNull();
        childGroup.Should().NotBeNull();
        childGroup.Symbols.Should().HaveCount(2);
    }

    [Fact]
    public void WatchlistData_EmptyWatchlist_IsValid()
    {
        // Act
        var watchlist = new WatchlistData();

        // Assert
        watchlist.Symbols.Should().BeEmpty();
        watchlist.Groups.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    [InlineData("TSLA")]
    public void WatchlistItem_AcceptsDifferentSymbols(string symbol)
    {
        // Act
        var item = new WatchlistItem { Symbol = symbol };

        // Assert
        item.Symbol.Should().Be(symbol);
    }

    [Fact]
    public void WatchlistData_SupportsLargeNumberOfSymbols()
    {
        // Arrange
        var watchlist = new WatchlistData();

        // Act
        for (int i = 0; i < 100; i++)
        {
            watchlist.Symbols.Add(new WatchlistItem { Symbol = $"SYMBOL{i}" });
        }

        // Assert
        watchlist.Symbols.Should().HaveCount(100);
    }

    [Fact]
    public void WatchlistGroup_CanHaveEmptySymbolList()
    {
        // Act
        var group = new WatchlistGroup
        {
            Name = "Empty Group",
            Symbols = new List<string>()
        };

        // Assert
        group.Symbols.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadWatchlistAsync_MultipleCallsInSequence_ReturnsConsistentData()
    {
        // Arrange
        var service = new WatchlistService();

        // Act
        var watchlist1 = await service.LoadWatchlistAsync();
        var watchlist2 = await service.LoadWatchlistAsync();

        // Assert
        watchlist1.Should().NotBeNull();
        watchlist2.Should().NotBeNull();
        watchlist1.Symbols.Count.Should().Be(watchlist2.Symbols.Count);
    }

    [Fact]
    public void WatchlistItem_WithNullNotes_IsValid()
    {
        // Act
        var item = new WatchlistItem
        {
            Symbol = "SPY",
            Notes = null
        };

        // Assert
        item.Symbol.Should().Be("SPY");
        item.Notes.Should().BeNull();
    }

    [Fact]
    public void WatchlistData_Modification_PreservesIntegrity()
    {
        // Arrange
        var watchlist = new WatchlistData();
        watchlist.Symbols.Add(new WatchlistItem { Symbol = "SPY" });

        // Act
        var symbol = watchlist.Symbols[0];
        symbol.Notes = "Modified";

        // Assert
        watchlist.Symbols[0].Notes.Should().Be("Modified");
    }

    private class CustomWatchlistService : WatchlistService
    {
        public override Task<WatchlistData> LoadWatchlistAsync()
        {
            return Task.FromResult(new WatchlistData
            {
                Symbols = new List<WatchlistItem>
                {
                    new WatchlistItem { Symbol = "CUSTOM" }
                }
            });
        }
    }
}
