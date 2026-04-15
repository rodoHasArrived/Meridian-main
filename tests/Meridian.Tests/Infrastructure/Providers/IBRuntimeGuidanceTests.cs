using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class IBRuntimeGuidanceTests
{
    [Fact]
    public void ContractFactory_NonIbApiGuidance_PointsToSetupDocAndSmokeBuildPath()
    {
        var act = () => ContractFactory.Create(new SymbolConfig("AAPL"));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*interactive-brokers-setup.md*")
            .WithMessage("*build-ibapi-smoke.ps1*")
            .WithMessage("*EnableIbApiVendor=true*")
            .WithMessage("*EnableIbApiSmoke=true*")
            .WithMessage("*DefineConstants=IBAPI*");
    }

    [Fact]
    public async Task EnhancedConnectionManager_NonIbApiGuidance_PointsToSetupDocAndSmokeBuildPath()
    {
        var router = new IBCallbackRouter(
            new MarketDepthCollector(new TestMarketEventPublisher(), requireExplicitSubscription: false),
            new TradeDataCollector(new TestMarketEventPublisher(), null));
        var manager = new EnhancedIBConnectionManager(router);

        var act = () => manager.ConnectAsync();

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*interactive-brokers-setup.md*")
            .WithMessage("*build-ibapi-smoke.ps1*")
            .WithMessage("*EnableIbApiVendor=true*")
            .WithMessage("*EnableIbApiSmoke=true*")
            .WithMessage("*DefineConstants=IBAPI*");
    }

    [Fact]
    public void IbSimulationClient_Metadata_PointsToSetupDoc()
    {
        var client = new IBSimulationClient(new TestMarketEventPublisher(), enableAutoTicks: false);

        client.ProviderDescription.Should().Contain("interactive-brokers-setup.md");
        client.ProviderNotes.Should().Contain(note => note.Contains("interactive-brokers-setup.md"));
        client.ProviderDescription.Should().Contain("IBSimulationClient");
        client.ProviderWarnings.Should().Contain(warning => warning.Contains("simulated data"));
    }

    [Fact]
    public void IbHistoricalProvider_Metadata_PointsToSetupDocAndSmokeBuildPath()
    {
        var provider = new IBHistoricalDataProvider();

        provider.Description.Should().Contain("interactive-brokers-setup.md");
        provider.ProviderNotes.Should().Contain(note => note.Contains("build-ibapi-smoke.ps1"));
        provider.ProviderWarnings.Should().Contain(warning => warning.Contains("interactive-brokers-setup.md"));
    }

    [Fact]
    public void IbHistoricalProvider_Metadata_ExplainsCompileOnlyAndVendorBuildModes()
    {
        var provider = new IBHistoricalDataProvider();

        provider.Description.Should().Contain("DefineConstants=IBAPI");
        provider.Description.Should().Contain("EnableIbApiSmoke=true");
        provider.Description.Should().Contain("EnableIbApiVendor=true");
        provider.ProviderNotes.Should().Contain(note => note.Contains("official IBApi surface"));
        provider.ProviderWarnings.Should().Contain(warning => warning.Contains("empty results"));
    }
}
