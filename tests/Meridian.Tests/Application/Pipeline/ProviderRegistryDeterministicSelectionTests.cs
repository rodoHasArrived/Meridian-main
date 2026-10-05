using FluentAssertions;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class ProviderRegistryDeterministicSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Streaming_registration_apis_share_one_alias_normalized_factory(bool replaceThroughCapabilityApi)
    {
        using var registry = new ProviderRegistry();
        var original = Mock.Of<IMarketDataClient>();
        var replacement = Mock.Of<IMarketDataClient>();

        if (replaceThroughCapabilityApi)
        {
            registry.RegisterStreamingFactory("interactive-brokers", () => original);
            registry.GetCapability<IMarketDataClient>("ibkr").Should().BeSameAs(original);
            registry.RegisterCapabilityFactory(" IB ", typeof(IMarketDataClient), () => replacement);
        }
        else
        {
            registry.RegisterCapabilityFactory(" IB ", typeof(IMarketDataClient), () => original);
            registry.CreateStreamingClient("interactive-brokers").Should().BeSameAs(original);
            registry.RegisterStreamingFactory("ibkr", () => replacement);
        }

        registry.GetCapability<IMarketDataClient>("interactive-brokers").Should().BeSameAs(replacement);
        registry.CreateStreamingClient(" IBKR ").Should().BeSameAs(replacement);
        registry.SupportedStreamingSources.Should().Equal("ibkr");
        registry.Disable("interactive_brokers");
        registry.GetCapability<IMarketDataClient>("ib").Should().BeNull();
        var createDisabled = () => registry.CreateStreamingClient("ibkr");
        createDisabled.Should().Throw<InvalidOperationException>();
        registry.SupportedStreamingSources.Should().Equal("ibkr",
            "registered factory inventory remains available while runtime resolution is disabled");

        registry.Enable(" IB ");
        registry.GetCapability<IMarketDataClient>("ibkr").Should().BeSameAs(replacement);
        registry.CreateStreamingClient("interactive-brokers").Should().BeSameAs(replacement);
    }

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
