using Meridian.Wpf.Features.Trading;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Trading;

public sealed class TradingFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new TradingFeatureModule(),
            "TradingShell",
            "LiveData",
            "OrderBook",
            "PositionBlotter",
            "RunRisk",
            "TradingHours");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsTradingWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new TradingFeatureModule(), "trading", "TradingShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(TradingWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(TradingWorkspaceShellStateProvider));
        workspace.ShellDefinition.DefaultPanes.Select(static pane => pane.PageTag)
            .Should().Equal("LiveData", "OrderBook", "PositionBlotter", "RunRisk", "TradingHours");
    }

    [Fact]
    public void DeclareCapabilities_ReturnsStableTradingCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertCapabilityKeys(
            new TradingFeatureModule(),
            "desktop.trading.workspace",
            "desktop.trading.hours");
    }

    [Fact]
    public void Register_AddsTradingShellServicesViewModelAndPageWithIntendedLifetimes()
    {
        var services = new ServiceCollection();

        new TradingFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<TradingWorkspaceShellPresentationService>(services, ServiceLifetime.Scoped);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TradingWorkspaceShellStateProvider>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TradingWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TradingWorkspaceShellPage>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IExecutionSafetyControlClient>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TradingSafetyCommandService>(services, ServiceLifetime.Singleton);
    }

    [Theory]
    [InlineData("TradingShell", "TradingShell", "trading")]
    [InlineData("TradingWorkspace", "TradingShell", "trading")]
    [InlineData("Blotter", "PositionBlotter", "trading")]
    public void ShellRegistry_ResolvesTradingAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
