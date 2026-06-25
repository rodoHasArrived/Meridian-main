using Meridian.Wpf.Features.Strategy;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Strategy;

public sealed class StrategyFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new StrategyFeatureModule(),
            "StrategyShell",
            "Backtest",
            "StrategyRuns",
            "RunDetail",
            "Charts",
            "RunMat",
            "BatchBacktest",
            "AdvancedAnalytics",
            "QuantScript",
            "LeanIntegration",
            "EventReplay",
            "Watchlist");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsStrategyWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new StrategyFeatureModule(), "strategy", "StrategyShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(StrategyWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(StrategyWorkspaceShellStateProvider));
    }

    [Fact]
    public void DeclareCapabilities_ReturnsNoFeatureCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertNoCapabilities(new StrategyFeatureModule());
    }

    [Fact]
    public void Register_AddsStrategyRuntimeServicesWithoutPageDescriptors()
    {
        var services = new ServiceCollection();

        new StrategyFeatureModule().Register(services);

        services.Single(service => service.ServiceType == typeof(ISecurityMasterService))
            .Lifetime
            .Should()
            .Be(ServiceLifetime.Singleton);
        services.Should().NotContain(service => typeof(System.Windows.Controls.Page).IsAssignableFrom(service.ServiceType));
    }

    [Theory]
    [InlineData("StrategyShell", "StrategyShell", "strategy")]
    [InlineData("ResearchShell", "StrategyShell", "strategy")]
    [InlineData("ResearchWorkspace", "StrategyShell", "strategy")]
    [InlineData("BacktestStudio", "Backtest", "strategy")]
    [InlineData("RunBrowser", "StrategyRuns", "strategy")]
    [InlineData("ResearchAutomation", "RunMat", "strategy")]
    public void ShellRegistry_ResolvesStrategyAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
