using Meridian.Core.Config;
using Meridian.Wpf.Features;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features;

internal static class DesktopFeatureModuleTestAssertions
{
    public static void AssertPageTags(IDesktopFeatureModule module, params string[] expectedPageTags)
    {
        var pages = module.DescribePages();

        pages.Select(static page => page.PageTag).Should().Equal(expectedPageTags);
        pages.Select(static page => page.PageTag).Should().OnlyHaveUniqueItems();
        pages.SelectMany(static page => page.Aliases).Where(static alias => !string.IsNullOrWhiteSpace(alias)).Should().OnlyHaveUniqueItems();
    }

    public static WorkspaceCapabilityDescriptor AssertWorkspace(
        IDesktopFeatureModule module,
        string expectedWorkspaceId,
        string expectedHomePageTag)
    {
        var workspace = module.DescribeWorkspace();

        workspace.Should().NotBeNull();
        workspace!.Workspace.Id.Should().Be(expectedWorkspaceId);
        workspace.Workspace.HomePageTag.Should().Be(expectedHomePageTag);
        workspace.ShellDefinition.WorkspaceId.Should().Be(expectedWorkspaceId);
        workspace.ShellDefinition.DefaultPanes.Should().NotBeEmpty();
        workspace.Pages.Select(static page => page.PageTag).Should().OnlyHaveUniqueItems();

        return workspace;
    }

    public static void AssertCapabilityKeys(IDesktopFeatureModule module, params string[] expectedCapabilityKeys)
    {
        var capabilities = module.DeclareCapabilities();

        capabilities.Select(static capability => capability.CapabilityKey).Should().Equal(expectedCapabilityKeys);
        capabilities.Select(static capability => capability.CapabilityKey).Should().OnlyHaveUniqueItems();
    }

    public static void AssertNoCapabilities(IDesktopFeatureModule module)
        => module.DeclareCapabilities().Should().BeEmpty();

    public static void AssertRegistered<TService>(IServiceCollection services, ServiceLifetime lifetime)
        where TService : class
    {
        services.Should().Contain(
            descriptor => descriptor.ServiceType == typeof(TService) && descriptor.Lifetime == lifetime,
            $"{typeof(TService).Name} should be registered with {lifetime} lifetime");
    }

    public static void AssertRegistered<TService, TImplementation>(IServiceCollection services, ServiceLifetime lifetime)
        where TService : class
        where TImplementation : class, TService
    {
        services.Should().Contain(
            descriptor => descriptor.ServiceType == typeof(TService) &&
                          descriptor.ImplementationType == typeof(TImplementation) &&
                          descriptor.Lifetime == lifetime,
            $"{typeof(TService).Name} should resolve to {typeof(TImplementation).Name} with {lifetime} lifetime");
    }

    public static void AssertRouteResolves(string requestedTag, string expectedCanonicalTag, string expectedWorkspaceId)
    {
        var registry = new ShellRouteRegistry(ShellPageRegistryBuilder.BuildDefault());

        var route = registry.GetRoute(requestedTag);

        route.Should().NotBeNull();
        route!.PageTag.Should().Be(expectedCanonicalTag);
        route.WorkspaceId.Should().Be(expectedWorkspaceId);
        registry.GetCanonicalPageTag(requestedTag).Should().Be(expectedCanonicalTag);
        registry.InferWorkspaceIdForPageTag(requestedTag).Should().Be(expectedWorkspaceId);
    }
}
