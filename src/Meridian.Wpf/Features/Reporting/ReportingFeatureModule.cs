using Meridian.Core.Config;
using Meridian.Wpf.Features.Reporting.Shell;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Reporting;

public sealed class ReportingFeatureModule : IDesktopFeatureModule
{
    private static readonly WorkspaceCapabilityDescriptor Capability = ShellNavigationCatalog.BuildReportingCapability();

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ReportingWorkspaceShellStateProvider>();
        services.AddTransient<ReportingWorkspaceShellViewModel>();
        services.AddTransient<ReportingWorkspaceShellPage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() =>
        FeatureCapabilityCatalog.Reporting;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;
}
