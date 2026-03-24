using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for WatchlistService singleton service.
/// Validates watchlist model defaults, event args, enum values, and base URL management.
/// </summary>
public sealed class WatchlistServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = WatchlistService.Instance;
        var instance2 = WatchlistService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "WatchlistService should be a singleton");
    }

    [Fact]
    public void Watchlist_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var watchlist = new Watchlist();

        // Assert
        watchlist.Id.Should().BeEmpty();
        watchlist.Name.Should().BeEmpty();
        watchlist.Symbols.Should().NotBeNull();
        watchlist.Symbols.Should().BeEmpty();
        watchlist.Color.Should().BeNull();
        watchlist.SortOrder.Should().Be(0);
        watchlist.IsPinned.Should().BeFalse();
        watchlist.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Watchlist_Symbols_ShouldBeEmptyList()
    {
        // Arrange & Act
        var watchlist = new Watchlist();

        // Assert
        watchlist.Symbols.Should().BeOfType<List<string>>();
        watchlist.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void WatchlistsChangedEventArgs_Construction_ShouldSetProperties()
    {
        // Arrange
        var watchlist = new Watchlist { Id = "wl_test", Name = "Test" };

        // Act
        var args = new WatchlistsChangedEventArgs(WatchlistChangeType.Created, watchlist);

        // Assert
        args.ChangeType.Should().Be(WatchlistChangeType.Created);
        args.Watchlist.Should().BeSameAs(watchlist);
    }

    [Fact]
    public void WatchlistsChangedEventArgs_WithNullWatchlist_ShouldAllowNull()
    {
        // Act
        var args = new WatchlistsChangedEventArgs(WatchlistChangeType.Synced, null);

        // Assert
        args.ChangeType.Should().Be(WatchlistChangeType.Synced);
        args.Watchlist.Should().BeNull();
    }

    [Theory]
    [InlineData(WatchlistChangeType.Created)]
    [InlineData(WatchlistChangeType.Updated)]
    [InlineData(WatchlistChangeType.Deleted)]
    [InlineData(WatchlistChangeType.Reordered)]
    [InlineData(WatchlistChangeType.Synced)]
    public void WatchlistChangeType_ShouldContainExpectedValues(WatchlistChangeType changeType)
    {
        // Assert
        Enum.IsDefined(typeof(WatchlistChangeType), changeType).Should().BeTrue();
    }

    [Fact]
    public void WatchlistChangeType_ShouldHaveFiveValues()
    {
        // Act
        var values = Enum.GetValues<WatchlistChangeType>();

        // Assert
        values.Should().HaveCount(5);
    }

    [Fact]
    public void BaseUrl_Default_ShouldContainLocalhost()
    {
        // Arrange
        var service = WatchlistService.Instance;

        // Act
        var baseUrl = service.BaseUrl;

        // Assert
        baseUrl.Should().Contain("localhost");
    }

    [Fact]
    public void BaseUrl_Setter_ShouldUpdateValue()
    {
        // Arrange
        var service = WatchlistService.Instance;
        var originalUrl = service.BaseUrl;
        var newUrl = "http://localhost:9090";

        // Act
        service.BaseUrl = newUrl;

        // Assert
        service.BaseUrl.Should().Be(newUrl);

        // Cleanup: restore original
        service.BaseUrl = originalUrl;
    }

    [Fact]
    public void Watchlist_InitProperties_ShouldBeAssignable()
    {
        // Arrange & Act
        var now = DateTimeOffset.UtcNow;
        var watchlist = new Watchlist
        {
            Id = "wl_abc123",
            Name = "Tech Stocks",
            Symbols = new List<string> { "AAPL", "MSFT" },
            Color = "#FF0000",
            SortOrder = 5,
            IsPinned = true,
            IsActive = false,
            CreatedAt = now,
            ModifiedAt = now
        };

        // Assert
        watchlist.Id.Should().Be("wl_abc123");
        watchlist.Name.Should().Be("Tech Stocks");
        watchlist.Symbols.Should().HaveCount(2);
        watchlist.Color.Should().Be("#FF0000");
        watchlist.SortOrder.Should().Be(5);
        watchlist.IsPinned.Should().BeTrue();
        watchlist.IsActive.Should().BeFalse();
        watchlist.CreatedAt.Should().Be(now);
        watchlist.ModifiedAt.Should().Be(now);
    }
}
