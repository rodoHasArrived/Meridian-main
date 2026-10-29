using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Contracts.Domain;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Moq;

namespace Meridian.Tests.SymbolSearch;

public sealed class SymbolSearchCancellationTests
{
    [Fact]
    public async Task Search_CancellationDuringProviderSearchPropagates()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider();
        provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string _, int _, CancellationToken ct) =>
            {
                cts.Cancel();
                return Task.FromCanceled<IReadOnlyList<SymbolSearchResult>>(ct);
            });
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync(new("AAPL"), cts.Token));
    }

    [Fact]
    public async Task Search_ProviderIgnoresCancellation_DoesNotPublishCancelledResponseToCache()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider();
        var calls = 0;
        provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++calls == 1)
                    cts.Cancel();
                return Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
            });
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync(new("AAPL"), cts.Token));
        await service.SearchAsync(new("AAPL"));

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Search_CancelledRequestDoesNotReturnCachedResponse()
    {
        var provider = Provider();
        provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var service = Service(provider);
        await service.SearchAsync(new("AAPL"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync(new("AAPL"), cts.Token));
    }

    [Fact]
    public async Task Details_CancellationDoesNotFallThroughToAnotherProvider()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider();
        provider.Setup(p => p.GetDetailsAsync(It.IsAny<SymbolId>(), It.IsAny<CancellationToken>()))
            .Returns((SymbolId _, CancellationToken ct) =>
            {
                cts.Cancel();
                return Task.FromCanceled<SymbolDetails?>(ct);
            });
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDetailsAsync("AAPL", ct: cts.Token));
    }

    [Fact]
    public async Task ProviderAvailability_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider();
        provider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
        {
            cts.Cancel();
            return Task.FromCanceled<bool>(ct);
        });
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetProvidersAsync(cts.Token));
    }

    private static Mock<ISymbolSearchProvider> Provider()
    {
        var provider = new Mock<ISymbolSearchProvider>();
        provider.SetupGet(p => p.Name).Returns("test");
        provider.SetupGet(p => p.Priority).Returns(1);
        provider.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return provider;
    }

    private static SymbolSearchService Service(Mock<ISymbolSearchProvider> provider)
        => new([provider.Object], null, new MetadataEnrichmentService());
}
