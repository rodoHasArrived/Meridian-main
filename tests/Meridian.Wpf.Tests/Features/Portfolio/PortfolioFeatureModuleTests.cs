using Meridian.Wpf.Features.Portfolio;
using Meridian.Wpf.Features.Portfolio.Shell;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Portfolio;

public sealed class PortfolioFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new PortfolioFeatureModule(),
            "PortfolioShell",
            "AccountPortfolio",
            "AggregatePortfolio",
            "RunPortfolio",
            "PortfolioExplorer",
            "FundPortfolio",
            "FundAccounts",
            "PortfolioImport",
            "DirectLending");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsPortfolioWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new PortfolioFeatureModule(), "portfolio", "PortfolioShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(PortfolioWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(PortfolioWorkspaceShellStateProvider));
    }

    [Fact]
    public void DeclareCapabilities_ReturnsNoFeatureCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertNoCapabilities(new PortfolioFeatureModule());
    }

    [Fact]
    public void Register_AddsPortfolioShellStateViewModelAndPageAsTransient()
    {
        var services = new ServiceCollection();

        new PortfolioFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<PortfolioWorkspaceShellStateProvider>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<PortfolioWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<PortfolioWorkspaceShellPage>(services, ServiceLifetime.Transient);
    }

    [Theory]
    [InlineData("PortfolioShell", "PortfolioShell", "portfolio")]
    [InlineData("PortfolioInspector", "RunPortfolio", "portfolio")]
    [InlineData("PortfolioExplorer", "PortfolioExplorer", "portfolio")]
    public void ShellRegistry_ResolvesPortfolioAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
