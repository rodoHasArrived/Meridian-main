using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Verifies IB setup guidance when the API is unavailable and runtime behavior when the
/// smoke stub is enabled, including rejection of historical requests without a connection.
/// </summary>
public sealed class IBRuntimeGuidanceTests
{
    [Fact]
    public void ContractFactory_BuildMode_CreatesContractOrProvidesSetupGuidance()
    {
#if IBAPI_SMOKE
        var contract = ContractFactory.Create(new SymbolConfig("AAPL"));

        contract.Symbol.Should().Be("AAPL");
        contract.SecType.Should().Be("STK");
        contract.Exchange.Should().Be("SMART");
        contract.Currency.Should().Be("USD");
#else
        var act = () => ContractFactory.Create(new SymbolConfig("AAPL"));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*provider-onboarding-interactive-brokers.md*")
            .WithMessage("*build-ibapi-smoke.ps1*")
            .WithMessage("*build-ibapi-vendor.ps1*")
            .WithMessage("*EnableIbApiVendor=true*")
            .WithMessage("*EnableIbApiSmoke=true*")
            .WithMessage("*DefineConstants=IBAPI*");
#endif
    }

    [Theory]
    [InlineData(InstrumentType.Equity, "STK")]
    [InlineData(InstrumentType.IndexOption, "OPT")]
    [InlineData(InstrumentType.Bond, "BOND")]
    [InlineData(InstrumentType.Swap, "SWAP")]
    [InlineData(InstrumentType.DirectLoan, "LOAN")]
    [InlineData(InstrumentType.Repo, "REPO")]
    [InlineData(InstrumentType.Deposit, "DEPOSIT")]
    public void ContractFactory_ResolveSecType_UsesInstrumentTypeDescriptorCatalog(
        InstrumentType instrumentType,
        string expectedSecurityType)
    {
        var cfg = new SymbolConfig(
            Symbol: "TEST",
            SecurityType: "STK",
            InstrumentType: instrumentType);

        var securityType = ContractFactory.ResolveSecType(cfg);

        securityType.Should().Be(expectedSecurityType);
    }

    [Fact]
    public void ContractFactory_ResolveSecType_PreservesExplicitProviderSecurityType()
    {
        var cfg = new SymbolConfig(
            Symbol: "912828YY0",
            SecurityType: "GOVT",
            InstrumentType: InstrumentType.Bond);

        var securityType = ContractFactory.ResolveSecType(cfg);

        securityType.Should().Be("GOVT");
    }

    [Fact]
    public async Task EnhancedConnectionManager_BuildMode_ConnectsOrProvidesSetupGuidance()
    {
        using var manager = CreateConnectionManager();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

#if IBAPI_SMOKE
        manager.IsConnected.Should().BeFalse();
        await manager.ConnectAsync(timeout.Token);
        manager.IsConnected.Should().BeTrue();

        await manager.DisconnectAsync(timeout.Token);
        manager.IsConnected.Should().BeFalse();
#else
        var act = () => manager.ConnectAsync(timeout.Token);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*provider-onboarding-interactive-brokers.md*")
            .WithMessage("*build-ibapi-smoke.ps1*")
            .WithMessage("*build-ibapi-vendor.ps1*")
            .WithMessage("*EnableIbApiVendor=true*")
            .WithMessage("*EnableIbApiSmoke=true*")
            .WithMessage("*DefineConstants=IBAPI*");
#endif
    }

    [Fact]
    public void IbSimulationClient_Metadata_PointsToSetupDoc()
    {
        var client = new IBSimulationClient(new TestMarketEventPublisher(), enableAutoTicks: false);

        client.ProviderDescription.Should().Contain("provider-onboarding-interactive-brokers.md");
        client.ProviderNotes.Should().Contain(note => note.Contains("provider-onboarding-interactive-brokers.md"));
        client.ProviderDescription.Should().Contain("IBSimulationClient");
        client.ProviderWarnings.Should().Contain(warning => warning.Contains("simulated data"));
    }

    [Fact]
    public void IbHistoricalProvider_Metadata_DescribesCurrentBuildMode()
    {
        using var manager = CreateConnectionManager();
        using var provider = new IBHistoricalDataProvider(manager);

#if IBAPI_SMOKE
        provider.Name.Should().Be("ibkr");
        provider.DisplayName.Should().Be("Interactive Brokers");
        provider.Description.Should().Contain("TWS API");
        provider.Description.Should().Contain("Requires active streaming subscription");
        provider.Capabilities.Intraday.Should().BeTrue();
#else
        provider.Description.Should().Contain("provider-onboarding-interactive-brokers.md");
        provider.ProviderNotes.Should().Contain(note => note.Contains("build-ibapi-smoke.ps1"));
        provider.ProviderWarnings.Should().Contain(warning => warning.Contains("provider-onboarding-interactive-brokers.md"));
#endif
    }

    [Fact]
    public async Task IbHistoricalProvider_BuildMode_EnforcesHistoricalAvailability()
    {
        using var manager = CreateConnectionManager();
        using var provider = new IBHistoricalDataProvider(manager);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        (await provider.IsAvailableAsync(timeout.Token)).Should().BeFalse();

#if IBAPI_SMOKE
        var act = () => provider.GetDailyBarsAsync("AAPL", null, null, timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not connected to IB Gateway/TWS*ConnectAsync*");
#else
        provider.Description.Should().Contain("DefineConstants=IBAPI");
        provider.Description.Should().Contain("EnableIbApiSmoke=true");
        provider.Description.Should().Contain("EnableIbApiVendor=true");
        provider.Description.Should().Contain("build-ibapi-vendor.ps1");
        provider.ProviderNotes.Should().Contain(note => note.Contains("official IBApi surface"));
        provider.ProviderWarnings.Should().Contain(warning => warning.Contains("empty results"));
#endif
    }

    private static EnhancedIBConnectionManager CreateConnectionManager()
    {
        var router = new IBCallbackRouter(
            new MarketDepthCollector(new TestMarketEventPublisher(), requireExplicitSubscription: false),
            new TradeDataCollector(new TestMarketEventPublisher(), null));
        return new EnhancedIBConnectionManager(router, enableHeartbeat: false);
    }
}
