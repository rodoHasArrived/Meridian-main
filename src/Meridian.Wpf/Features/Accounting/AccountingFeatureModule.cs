using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Accounting;

public sealed class AccountingFeatureModule : IDesktopFeatureModule
{
    private static readonly WorkspaceCapabilityDescriptor Capability = ShellNavigationCatalog.BuildAccountingCapability();

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<GovernanceWorkspaceShellStateProvider>();
        services.AddTransient<GovernanceWorkspaceShellViewModel>();
        services.AddTransient<GovernanceWorkspaceShellPage>();
        services.AddTransient<AccountingCloseViewModel>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;
}
