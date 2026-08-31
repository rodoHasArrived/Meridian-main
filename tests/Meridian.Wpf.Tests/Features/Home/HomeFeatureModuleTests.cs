using Meridian.Wpf.Features.Home;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Home;

public sealed class HomeFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        var module = new HomeFeatureModule();

        DesktopFeatureModuleTestAssertions.AssertPageTags(module, "HomeWorkspace", "OperatorReadinessConsole");
    }

    [Fact]
    public void DeclareCapabilities_ReturnsNoFeatureCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertNoCapabilities(new HomeFeatureModule());
    }

    [Fact]
    public void Register_AddsHomeViewModelAndPageAsTransient()
    {
        var services = new ServiceCollection();

        new HomeFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<HomeWorkspaceViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<HomeWorkspacePage>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<OperatorReadinessConsoleViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<OperatorReadinessConsolePage>(services, ServiceLifetime.Transient);
    }

    [Fact]
    public void Register_ResolvesReadinessConsoleViewModelWithoutOtherModules()
    {
        var services = new ServiceCollection();
        new HomeFeatureModule().Register(services);
        using var provider = services.BuildServiceProvider();

        using var viewModel = provider.GetRequiredService<OperatorReadinessConsoleViewModel>();

        viewModel.Should().NotBeNull(
            "the console must degrade per-panel instead of failing to resolve when readiness sources are absent");
    }

    [Theory]
    [InlineData("HomeWorkspace", "HomeWorkspace", "strategy")]
    [InlineData("Home", "HomeWorkspace", "strategy")]
    [InlineData("WorkstationHome", "HomeWorkspace", "strategy")]
    [InlineData("OperatorReadinessConsole", "OperatorReadinessConsole", "strategy")]
    public void ShellRegistry_ResolvesHomeAliases(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
