using Meridian.Contracts.Api;
using Meridian.Wpf.Contracts;
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
        var remoteClient = new FakeRemoteWorkstationClient();
        var service = new WatchlistService(remoteClient, CreateTempWatchlistPath());
        var newUrl = "http://localhost:9090";

        // Act
        service.BaseUrl = newUrl;

        // Assert
        service.BaseUrl.Should().Be(newUrl);
        remoteClient.ConfiguredServiceUrl.Should().Be(newUrl);
    }

    [Fact]
    public async Task SyncWithBackendAsync_ShouldUseRemoteWorkstationClientAndPersistReturnedWatchlists()
    {
        // Arrange
        var watchlistsPath = CreateTempWatchlistPath();
        var remoteClient = new FakeRemoteWorkstationClient
        {
            Watchlists =
            [
                new Watchlist
                {
                    Id = "remote-core",
                    Name = "Remote Core",
                    Symbols = ["SPY", "QQQ"],
                    SortOrder = 0,
                    IsPinned = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var service = new WatchlistService(remoteClient, watchlistsPath);
        WatchlistChangeType? observedChange = null;
        service.WatchlistsChanged += (_, args) => observedChange = args.ChangeType;

        try
        {
            // Act
            var result = await service.SyncWithBackendAsync();

            // Assert
            result.Should().BeTrue();
            remoteClient.LastGetEndpoint.Should().Be("/api/watchlists");
            observedChange.Should().Be(WatchlistChangeType.Synced);

            var watchlists = await service.GetAllWatchlistsAsync();
            watchlists.Should().ContainSingle();
            watchlists[0].Name.Should().Be("Remote Core");
            File.ReadAllText(watchlistsPath).Should().Contain("Remote Core");
        }
        finally
        {
            DeleteTempWatchlistPath(watchlistsPath);
        }
    }

    [Fact]
    public async Task SyncWithBackendAsync_WhenRemoteClientHasNoPayload_ShouldFailClosed()
    {
        // Arrange
        var watchlistsPath = CreateTempWatchlistPath();
        var remoteClient = new FakeRemoteWorkstationClient();
        var service = new WatchlistService(remoteClient, watchlistsPath);
        var eventRaised = false;
        service.WatchlistsChanged += (_, _) => eventRaised = true;

        try
        {
            // Act
            var result = await service.SyncWithBackendAsync();

            // Assert
            result.Should().BeFalse();
            remoteClient.LastGetEndpoint.Should().Be("/api/watchlists");
            eventRaised.Should().BeFalse();
            File.Exists(watchlistsPath).Should().BeFalse();
        }
        finally
        {
            DeleteTempWatchlistPath(watchlistsPath);
        }
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

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://localhost:8080";
        public string? ConfiguredServiceUrl { get; private set; }
        public string? LastGetEndpoint { get; private set; }
        public List<Watchlist>? Watchlists { get; init; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
        {
            ConfiguredServiceUrl = serviceUrl;
            BaseUrl = serviceUrl;
        }

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ServiceHealthResult { IsReachable = true, IsConnected = true });

        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult<StatusResponse?>(null);

        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default) =>
            Task.FromResult(new ApiResponse<StatusResponse> { Success = true });

        public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
        {
            LastGetEndpoint = endpoint;
            return Task.FromResult(Watchlists as T);
        }

        public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
        {
            LastGetEndpoint = endpoint;
            return Task.FromResult(new ApiResponse<T> { Success = true, Data = Watchlists as T });
        }

        public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
            where T : class =>
            Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> PostWithResponseAsync<T>(
            string endpoint,
            object? body = null,
            CancellationToken ct = default) where T : class =>
            Task.FromResult(new ApiResponse<T> { Success = false });

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class =>
            Task.FromResult(new ApiResponse<T> { Success = false });

        public void Dispose()
        {
        }
    }
}
