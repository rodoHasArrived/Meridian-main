using Meridian.Wpf.Features.Portfolio.Shell;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Portfolio;

/// <summary>
/// Registers portfolio desktop services and contributes the feature's navigation pages and workspace metadata.
/// </summary>
public sealed class PortfolioFeatureModule : IDesktopFeatureModule
{
    private static readonly WorkspaceCapabilityDescriptor Capability = ShellNavigationCatalog.BuildPortfolioCapability();

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<PortfolioWorkspaceShellStateProvider>();
        services.AddTransient<PortfolioWorkspaceShellViewModel>();
        services.AddTransient<PortfolioWorkspaceShellPage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;
}
