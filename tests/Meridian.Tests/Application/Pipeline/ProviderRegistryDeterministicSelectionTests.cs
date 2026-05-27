using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class ProviderRegistryDeterministicSelectionTests
{
    [Fact]
    public async Task GetBestBackfillProviderAsync_WhenEqualPriority_SelectsOrdinallyByProviderId()
    {
        var registry = new ProviderRegistry();
        var zulu = CreateBackfillProvider("zulu", priority: 10, isAvailable: true);
        var alpha = CreateBackfillProvider("alpha", priority: 10, isAvailable: true);

        registry.Register(zulu.Object);
        registry.Register(alpha.Object);

        var best = await registry.GetBestBackfillProviderAsync();

        best.Should().NotBeNull();
        best!.ProviderId.Should().Be("alpha");
    }

    [Fact]
    public async Task GetBestBackfillProviderAsync_SkipsDegradedProvider_ThenUsesDeterministicTieBreak()
    {
        var registry = new ProviderRegistry();
        var degraded = CreateBackfillProvider("alpha", priority: 5, isAvailable: false);
        var zulu = CreateBackfillProvider("zulu", priority: 10, isAvailable: true);
        var beta = CreateBackfillProvider("beta", priority: 10, isAvailable: true);

        registry.Register(zulu.Object);
        registry.Register(beta.Object);
        registry.Register(degraded.Object);

        var best = await registry.GetBestBackfillProviderAsync();

        best.Should().NotBeNull();
        best!.ProviderId.Should().Be("beta");
    }

    private static Mock<IHistoricalDataProvider> CreateBackfillProvider(string id, int priority, bool isAvailable)
    {
        var mock = new Mock<IHistoricalDataProvider>();
        mock.SetupGet(x => x.ProviderId).Returns(id);
        mock.SetupGet(x => x.ProviderDisplayName).Returns($"{id} provider");
        mock.SetupGet(x => x.ProviderDescription).Returns("test");
        mock.SetupGet(x => x.ProviderPriority).Returns(priority);
        mock.SetupGet(x => x.ProviderCapabilities).Returns(ProviderCapabilities.BackfillBarsOnly);
        mock.SetupGet(x => x.Name).Returns(id);
        mock.SetupGet(x => x.Description).Returns("test");
        mock.SetupGet(x => x.Capabilities).Returns(new HistoricalDataCapabilities());
        mock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(isAvailable);
        return mock;
    }
}
