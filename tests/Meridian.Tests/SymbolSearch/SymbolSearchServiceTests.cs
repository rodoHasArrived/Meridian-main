using FluentAssertions;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Xunit;

namespace Meridian.Tests.SymbolSearch;

/// <summary>
/// Unit tests for the SymbolSearchService class.
/// Tests symbol search, details lookup, and FIGI integration.
/// </summary>
public class SymbolSearchServiceTests : IDisposable
{
    private SymbolSearchService? _service;

    public void Dispose()
    {
        _service?.Dispose();
    }

    #region Search Tests

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyResults()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "");

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("test");
        mockProvider.Setup(p => p.DisplayName).Returns("Test Provider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockProvider.Setup(p => p.SearchAsync("AAPL", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple Inc.", "NASDAQ", "Stock", "US", "USD", "test", 100)
            });

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL", Limit: 10);

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Symbol.Should().Be("AAPL");
        result.Results[0].Name.Should().Be("Apple Inc.");
        result.Sources.Should().Contain("test");
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesResultsFromMultipleProviders()
    {
        // Arrange
        var mockProvider1 = new Mock<ISymbolSearchProvider>();
        mockProvider1.Setup(p => p.Name).Returns("provider1");
        mockProvider1.Setup(p => p.Priority).Returns(1);
        mockProvider1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider1.Setup(p => p.SearchAsync("AAPL", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple Inc.", "NASDAQ", "Stock", "US", "USD", "provider1", 100)
            });

        var mockProvider2 = new Mock<ISymbolSearchProvider>();
        mockProvider2.Setup(p => p.Name).Returns("provider2");
        mockProvider2.Setup(p => p.Priority).Returns(2);
        mockProvider2.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider2.Setup(p => p.SearchAsync("AAPL", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple", "NASDAQ", "Stock", "US", "USD", "provider2", 80)
            });

        _service = new SymbolSearchService(
            new[] { mockProvider1.Object, mockProvider2.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL", Limit: 10);

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1); // Deduplicated
        result.Results[0].MatchScore.Should().Be(100); // Higher score kept
    }

    [Fact]
    public async Task SearchAsync_WithSpecificProvider_QueriesOnlyThatProvider()
    {
        // Arrange
        var mockProvider1 = new Mock<ISymbolSearchProvider>();
        mockProvider1.Setup(p => p.Name).Returns("provider1");
        mockProvider1.Setup(p => p.Priority).Returns(1);
        mockProvider1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider1.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple", "NASDAQ", "Stock", "US", "USD", "provider1", 100)
            });

        var mockProvider2 = new Mock<ISymbolSearchProvider>();
        mockProvider2.Setup(p => p.Name).Returns("provider2");
        mockProvider2.Setup(p => p.Priority).Returns(2);

        _service = new SymbolSearchService(
            new[] { mockProvider1.Object, mockProvider2.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL", Provider: "provider1");

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Sources.Should().Contain("provider1");
        mockProvider2.Verify(
            p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithUnavailableProvider_SkipsProvider()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("unavailable");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL");

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        mockProvider.Verify(
            p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Details Tests

    [Fact]
    public async Task GetDetailsAsync_WithValidSymbol_ReturnsDetails()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("test");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider.Setup(p => p.GetDetailsAsync(new SymbolId("AAPL"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SymbolDetails(
                Symbol: "AAPL",
                Name: "Apple Inc.",
                Exchange: "NASDAQ",
                AssetType: "Stock",
                MarketCap: 3000000000000m,
                Source: "test"));

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        // Act
        var result = await _service.GetDetailsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result!.Symbol.Should().Be("AAPL");
        result.Name.Should().Be("Apple Inc.");
        result.MarketCap.Should().Be(3000000000000m);
    }

    [Fact]
    public async Task GetDetailsAsync_WithUnknownSymbol_ReturnsNull()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("test");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider.Setup(p => p.GetDetailsAsync(It.IsAny<SymbolId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SymbolDetails?)null);

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        // Act
        var result = await _service.GetDetailsAsync("UNKNOWN");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailsAsync_QueriesProvidersInPriorityOrder()
    {
        // Arrange
        var callOrder = new List<string>();

        var mockProvider1 = new Mock<ISymbolSearchProvider>();
        mockProvider1.Setup(p => p.Name).Returns("high-priority");
        mockProvider1.Setup(p => p.Priority).Returns(1);
        mockProvider1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider1.Setup(p => p.GetDetailsAsync(It.IsAny<SymbolId>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("high-priority"))
            .ReturnsAsync(new SymbolDetails("AAPL", "Apple Inc.", Source: "high-priority"));

        var mockProvider2 = new Mock<ISymbolSearchProvider>();
        mockProvider2.Setup(p => p.Name).Returns("low-priority");
        mockProvider2.Setup(p => p.Priority).Returns(10);
        mockProvider2.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider2.Setup(p => p.GetDetailsAsync(It.IsAny<SymbolId>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("low-priority"))
            .ReturnsAsync(new SymbolDetails("AAPL", "Apple", Source: "low-priority"));

        _service = new SymbolSearchService(
            new[] { mockProvider2.Object, mockProvider1.Object }, // Added in wrong order
            null,
            new MetadataEnrichmentService());

        // Act
        var result = await _service.GetDetailsAsync("AAPL");

        // Assert
        callOrder.Should().HaveCount(1);
        callOrder.First().Should().Be("high-priority"); // Higher priority called first
        result!.Source.Should().Be("high-priority");
    }

    #endregion

    #region Provider Status Tests

    [Fact]
    public async Task GetProvidersAsync_ReturnsAllProviderStatuses()
    {
        // Arrange
        var mockProvider1 = new Mock<ISymbolSearchProvider>();
        mockProvider1.Setup(p => p.Name).Returns("available");
        mockProvider1.Setup(p => p.DisplayName).Returns("Available Provider");
        mockProvider1.Setup(p => p.Priority).Returns(1);
        mockProvider1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var mockProvider2 = new Mock<ISymbolSearchProvider>();
        mockProvider2.Setup(p => p.Name).Returns("unavailable");
        mockProvider2.Setup(p => p.DisplayName).Returns("Unavailable Provider");
        mockProvider2.Setup(p => p.Priority).Returns(2);
        mockProvider2.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        _service = new SymbolSearchService(
            new[] { mockProvider1.Object, mockProvider2.Object },
            null,
            new MetadataEnrichmentService());

        // Act
        var providers = await _service.GetProvidersAsync();

        // Assert
        providers.Should().Contain(p => p.Name == "available" && p.Available);
        providers.Should().Contain(p => p.Name == "unavailable" && !p.Available);
    }

    #endregion

    #region Cache Tests

    [Fact]
    public async Task SearchAsync_CachesResults()
    {
        // Arrange
        var callCount = 0;
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("test");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple Inc.", Source: "test", MatchScore: 100)
            });

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL");

        // Act - Call twice
        await _service.SearchAsync(request);
        await _service.SearchAsync(request);

        // Assert - Provider should only be called once due to caching
        callCount.Should().Be(1);
    }

    [Fact]
    public void ClearCache_ClearsSearchCache()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        // Act & Assert - Should not throw
        _service.ClearCache();
    }

    #endregion

    #region Limit Tests

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        mockProvider.Setup(p => p.Name).Returns("test");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple Inc.", MatchScore: 100),
                new("APLE", "Apple Hospitality REIT", MatchScore: 90),
                new("APLD", "Applied Digital", MatchScore: 80),
                new("APA", "APA Corporation", MatchScore: 70),
                new("APD", "Air Products", MatchScore: 60)
            });

        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AP", Limit: 3);

        // Act
        var result = await _service.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(3);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SearchAsync_WithProviderException_ContinuesWithOtherProviders()
    {
        // Arrange
        var mockProvider1 = new Mock<ISymbolSearchProvider>();
        mockProvider1.Setup(p => p.Name).Returns("failing");
        mockProvider1.Setup(p => p.Priority).Returns(1);
        mockProvider1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider1.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var mockProvider2 = new Mock<ISymbolSearchProvider>();
        mockProvider2.Setup(p => p.Name).Returns("working");
        mockProvider2.Setup(p => p.Priority).Returns(2);
        mockProvider2.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockProvider2.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymbolSearchResult>
            {
                new("AAPL", "Apple Inc.", Source: "working", MatchScore: 100)
            });

        _service = new SymbolSearchService(
            new[] { mockProvider1.Object, mockProvider2.Object },
            null,
            new MetadataEnrichmentService());

        var request = new SymbolSearchRequest(Query: "AAPL");

        // Act
        var result = await _service.SearchAsync(request);

        // Assert - Should still get results from working provider
        result.Results.Should().HaveCount(1);
        result.Sources.Should().Contain("working");
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task SearchAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());
        _service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _service.SearchAsync(new SymbolSearchRequest("AAPL")));
    }

    [Fact]
    public async Task GetDetailsAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());
        _service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _service.GetDetailsAsync("AAPL"));
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var mockProvider = new Mock<ISymbolSearchProvider>();
        _service = new SymbolSearchService(
            new[] { mockProvider.Object },
            null,
            new MetadataEnrichmentService());

        // Act & Assert - Should not throw
        _service.Dispose();
        _service.Dispose();
        _service.Dispose();
    }

    #endregion
}

/// <summary>
/// Unit tests for SymbolSearchResult model.
/// </summary>
public class SymbolSearchResultTests
{
    [Fact]
    public void Constructor_WithMinimalParameters_CreatesValidInstance()
    {
        // Act
        var result = new SymbolSearchResult("AAPL", "Apple Inc.");

        // Assert
        result.Symbol.Should().Be("AAPL");
        result.Name.Should().Be("Apple Inc.");
        result.MatchScore.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        // Act
        var result = new SymbolSearchResult(
            Symbol: "AAPL",
            Name: "Apple Inc.",
            Exchange: "NASDAQ",
            AssetType: "Stock",
            Country: "US",
            Currency: "USD",
            Source: "test",
            MatchScore: 100,
            Figi: "BBG000B9XRY4",
            CompositeFigi: "BBG000B9Y5X2");

        // Assert
        result.Symbol.Should().Be("AAPL");
        result.Name.Should().Be("Apple Inc.");
        result.Exchange.Should().Be("NASDAQ");
        result.AssetType.Should().Be("Stock");
        result.Country.Should().Be("US");
        result.Currency.Should().Be("USD");
        result.Source.Should().Be("test");
        result.MatchScore.Should().Be(100);
        result.Figi.Should().Be("BBG000B9XRY4");
        result.CompositeFigi.Should().Be("BBG000B9Y5X2");
    }

    [Fact]
    public void WithExpression_CreatesModifiedCopy()
    {
        // Arrange
        var original = new SymbolSearchResult("AAPL", "Apple Inc.", MatchScore: 50);

        // Act
        var modified = original with { MatchScore = 100 };

        // Assert
        original.MatchScore.Should().Be(50);
        modified.MatchScore.Should().Be(100);
        modified.Symbol.Should().Be("AAPL"); // Other properties unchanged
    }
}

/// <summary>
/// Unit tests for SymbolDetails model.
/// </summary>
public class SymbolDetailsTests
{
    [Fact]
    public void Constructor_WithMinimalParameters_CreatesValidInstance()
    {
        // Act
        var details = new SymbolDetails("AAPL", "Apple Inc.");

        // Assert
        details.Symbol.Should().Be("AAPL");
        details.Name.Should().Be("Apple Inc.");
        details.MarketCap.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMarketData_SetsMarketProperties()
    {
        // Act
        var details = new SymbolDetails(
            Symbol: "AAPL",
            Name: "Apple Inc.",
            MarketCap: 3000000000000m,
            AverageVolume: 80000000,
            LastPrice: 180.50m,
            Week52High: 200.00m,
            Week52Low: 150.00m);

        // Assert
        details.MarketCap.Should().Be(3000000000000m);
        details.AverageVolume.Should().Be(80000000);
        details.LastPrice.Should().Be(180.50m);
        details.Week52High.Should().Be(200.00m);
        details.Week52Low.Should().Be(150.00m);
    }
}

/// <summary>
/// Unit tests for SymbolSearchRequest model.
/// </summary>
public class SymbolSearchRequestTests
{
    [Fact]
    public void Constructor_WithQueryOnly_UsesDefaultValues()
    {
        // Act
        var request = new SymbolSearchRequest(Query: "AAPL");

        // Assert
        request.Query.Should().Be("AAPL");
        request.Limit.Should().Be(10);
        request.IncludeFigi.Should().BeTrue();
        request.AssetType.Should().BeNull();
        request.Exchange.Should().BeNull();
        request.Provider.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithCustomLimit_SetsLimit()
    {
        // Act
        var request = new SymbolSearchRequest(Query: "AAPL", Limit: 20);

        // Assert
        request.Limit.Should().Be(20);
    }

    [Fact]
    public void Constructor_WithFilters_SetsFilterProperties()
    {
        // Act
        var request = new SymbolSearchRequest(
            Query: "tech",
            AssetType: "ETF",
            Exchange: "NYSE",
            Provider: "finnhub");

        // Assert
        request.AssetType.Should().Be("ETF");
        request.Exchange.Should().Be("NYSE");
        request.Provider.Should().Be("finnhub");
    }
}
