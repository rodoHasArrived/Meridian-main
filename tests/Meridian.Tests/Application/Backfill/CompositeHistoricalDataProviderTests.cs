using FluentAssertions;
using Meridian.Application.Exceptions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

public sealed class CompositeHistoricalDataProviderTests : IDisposable
{
    private readonly List<Mock<IHistoricalDataProvider>> _mockProviders = new();

    public void Dispose()
    {
        // Nothing to dispose; mocks don't hold resources.
    }

    [Fact]
    public void Constructor_WithNoProviders_ThrowsArgumentException()
    {
        var act = () => new CompositeHistoricalDataProvider(
            Array.Empty<IHistoricalDataProvider>());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("providers");
    }

    [Fact]
    public void Name_ReturnsComposite()
    {
        using var composite = CreateComposite(CreateMockProvider("p1", priority: 1));

        composite.Name.Should().Be("composite");
    }

    [Fact]
    public void Capabilities_AggregatesFromAllProviders()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.Capabilities).Returns(new HistoricalDataCapabilities
        {
            AdjustedPrices = true,
            Intraday = false,
            SupportedMarkets = new[] { "US" }
        });

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.Capabilities).Returns(new HistoricalDataCapabilities
        {
            AdjustedPrices = false,
            Intraday = true,
            SupportedMarkets = new[] { "UK" }
        });

        using var composite = CreateComposite(p1, p2);

        composite.Capabilities.AdjustedPrices.Should().BeTrue("p1 supports it");
        composite.Capabilities.Intraday.Should().BeTrue("p2 supports it");
        composite.Capabilities.SupportedMarkets.Should().Contain("US").And.Contain("UK");
    }

    [Fact]
    public async Task GetDailyBarsAsync_ReturnsDataFromFirstSuccessfulProvider()
    {
        var bars = CreateBars("SPY", 5);

        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        var p2 = CreateMockProvider("p2", priority: 2);

        using var composite = CreateComposite(p1, p2);

        var result = await composite.GetDailyBarsAsync("SPY", null, null);

        result.Should().HaveCount(5);
        p2.Verify(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never,
            "second provider should not be called when first succeeds");
    }

    [Fact]
    public async Task GetDailyBarsAsync_FailsOverToNextProvider()
    {
        var bars = CreateBars("SPY", 3);

        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        using var composite = CreateComposite(p1, p2);

        var result = await composite.GetDailyBarsAsync("SPY", null, null);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ThrowsAggregateException_WhenAllProvidersFail()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("p1 failed"));

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("p2 failed"));

        using var composite = CreateComposite(p1, p2);

        var act = () => composite.GetDailyBarsAsync("SPY", null, null);

        await act.Should().ThrowAsync<AggregateException>()
            .WithMessage("*All providers failed*");
    }

    [Fact]
    public async Task GetDailyBarsAsync_SkipsProviderReturningEmptyResults()
    {
        var bars = CreateBars("SPY", 2);

        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<HistoricalBar>());

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        using var composite = CreateComposite(p1, p2);

        var result = await composite.GetDailyBarsAsync("SPY", null, null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithEmptySymbol_ThrowsArgumentException()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        using var composite = CreateComposite(p1);

        var act = () => composite.GetDailyBarsAsync("", null, null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("symbol");
    }

    [Fact]
    public async Task GetDailyBarsAsync_PropagatsCancellation()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Returns((string _, DateOnly? _, DateOnly? _, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());
            });

        using var composite = CreateComposite(p1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => composite.GetDailyBarsAsync("SPY", null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_ReturnsEmptyList_WhenNoDataFound()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("XYZ", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<HistoricalBar>());

        using var composite = CreateComposite(p1);

        var result = await composite.GetDailyBarsAsync("XYZ", null, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_RespectsProviderPriorityOrder()
    {
        var callOrder = new List<string>();

        var p1 = CreateMockProvider("slow", priority: 10);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("slow"))
            .ReturnsAsync(Array.Empty<HistoricalBar>());

        var p2 = CreateMockProvider("fast", priority: 1);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("fast"))
            .ReturnsAsync(Array.Empty<HistoricalBar>());

        using var composite = CreateComposite(p1, p2);

        await composite.GetDailyBarsAsync("SPY", null, null);

        callOrder.Should().Equal("fast", "slow");
        // Providers should be tried in priority order (lower number first)
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueIfAnyProviderAvailable()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var composite = CreateComposite(p1, p2);

        var result = await composite.IsAvailableAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseIfAllUnavailable()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        using var composite = CreateComposite(p1);

        var result = await composite.IsAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public void ProviderHealth_InitializesForAllProviders()
    {
        var p1 = CreateMockProvider("alpha", priority: 1);
        var p2 = CreateMockProvider("beta", priority: 2);

        using var composite = CreateComposite(p1, p2);

        composite.ProviderHealth.Should().ContainKey("alpha");
        composite.ProviderHealth.Should().ContainKey("beta");
    }

    [Fact]
    public async Task GetDailyBarsAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        var composite = CreateComposite(p1);
        composite.Dispose();

        var act = () => composite.GetDailyBarsAsync("SPY", null, null);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_RecordsRateLimitHit_OnRateLimitException()
    {
        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitException("Rate limited", provider: "p1", retryAfter: TimeSpan.FromMinutes(1)));

        var bars = CreateBars("SPY", 2);
        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        using var composite = CreateComposite(p1, p2);

        var result = await composite.GetDailyBarsAsync("SPY", null, null);

        result.Should().HaveCount(2, "should fail over to p2 after rate limit hit on p1");
    }

    [Fact]
    public async Task GetDailyBarsAsync_ThreeProviderCascade_P1DataException_P2RateLimit_P3Succeeds()
    {
        // Arrange – three providers with distinct failure modes, only the third returns data
        var bars = CreateBars("SPY", 7);

        var p1 = CreateMockProvider("p1", priority: 1);
        p1.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DataProviderException("p1 connection failed", provider: "p1"));

        var p2 = CreateMockProvider("p2", priority: 2);
        p2.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitException("p2 rate limited", provider: "p2", retryAfter: TimeSpan.FromMinutes(5)));

        var p3 = CreateMockProvider("p3", priority: 3);
        p3.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        using var composite = CreateComposite(p1, p2, p3);

        // Act
        var result = await composite.GetDailyBarsAsync("SPY", null, null);

        // Assert
        result.Should().HaveCount(7, "composite must return p3 data after p1 and p2 fail");

        p1.Verify(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once,
            "p1 should be tried exactly once");
        p2.Verify(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once,
            "p2 should be tried exactly once after p1 fails");
        p3.Verify(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once,
            "p3 should be tried exactly once after p2 fails");
    }

    #region Helpers

    private Mock<IHistoricalDataProvider> CreateMockProvider(string name, int priority)
    {
        var mock = new Mock<IHistoricalDataProvider>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.DisplayName).Returns(name);
        mock.Setup(p => p.Description).Returns($"Mock provider {name}");
        mock.Setup(p => p.Priority).Returns(priority);
        mock.Setup(p => p.RateLimitDelay).Returns(TimeSpan.Zero);
        mock.Setup(p => p.MaxRequestsPerWindow).Returns(int.MaxValue);
        mock.Setup(p => p.RateLimitWindow).Returns(TimeSpan.FromHours(1));
        mock.Setup(p => p.Capabilities).Returns(HistoricalDataCapabilities.None);
        mock.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockProviders.Add(mock);
        return mock;
    }

    private static CompositeHistoricalDataProvider CreateComposite(params Mock<IHistoricalDataProvider>[] mocks)
    {
        return new CompositeHistoricalDataProvider(
            mocks.Select(m => m.Object),
            enableRateLimitRotation: false);
    }

    private static IReadOnlyList<HistoricalBar> CreateBars(string symbol, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new HistoricalBar(
                symbol,
                DateOnly.FromDateTime(DateTime.Today.AddDays(-count + i)),
                100m + i, 105m + i, 95m + i, 102m + i,
                1000L * (i + 1),
                "test",
                i))
            .ToArray();
    }

    #endregion
}
