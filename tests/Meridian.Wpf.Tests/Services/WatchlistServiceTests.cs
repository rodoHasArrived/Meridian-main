using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for WatchlistService singleton service.
/// Validates watchlist model defaults, event args, enum values, and local-file persistence.
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
        var args = new WatchlistsChangedEventArgs(WatchlistChangeType.Reordered, null);

        // Assert
        args.ChangeType.Should().Be(WatchlistChangeType.Reordered);
        args.Watchlist.Should().BeNull();
    }

    [Theory]
    [InlineData(WatchlistChangeType.Created)]
    [InlineData(WatchlistChangeType.Updated)]
    [InlineData(WatchlistChangeType.Deleted)]
    [InlineData(WatchlistChangeType.Reordered)]
    public void WatchlistChangeType_ShouldContainExpectedValues(WatchlistChangeType changeType)
    {
        // Assert
        Enum.IsDefined(typeof(WatchlistChangeType), changeType).Should().BeTrue();
    }

    [Fact]
    public void WatchlistChangeType_ShouldHaveFourValues()
    {
        // Act
        var values = Enum.GetValues<WatchlistChangeType>();

        // Assert
        values.Should().HaveCount(4, "there is no backend to sync with, so no Synced change");
    }

    [Fact]
    public async Task CreatedWatchlist_PersistsToTheLocalFile_AndReloadsInANewInstance()
    {
        // Arrange
        var watchlistsPath = CreateTempWatchlistPath();

        try
        {
            var service = new WatchlistService(watchlistsPath);

            // Act
            await service.CreateWatchlistAsync("Core", ["SPY", "QQQ"], color: null);
            var reloaded = await new WatchlistService(watchlistsPath).GetAllWatchlistsAsync();

            // Assert
            File.ReadAllText(watchlistsPath).Should().Contain("Core");
            reloaded.Should().ContainSingle(w => w.Name == "Core")
                .Which.Symbols.Should().Equal("SPY", "QQQ");
        }
        finally
        {
            DeleteTempWatchlistPath(watchlistsPath);
        }
    }

    [Fact]
    public void ServiceSource_DoesNotCallAServerWatchlistRoute()
    {
        var source = File.ReadAllText(GetRepositoryFilePath("src/Meridian.Wpf/Services/WatchlistService.cs"));

        source.Should().NotContain("/api/watchlists", "the host maps no watchlist route");
        source.Should().NotContain("IRemoteWorkstationClient");
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

    private static string CreateTempWatchlistPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Meridian.Wpf.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "watchlists.json");
    }

    private static void DeleteTempWatchlistPath(string watchlistsPath)
    {
        var directory = Path.GetDirectoryName(watchlistsPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
