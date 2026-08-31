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

        services.AddSingleton<IReportingGovernanceApiClient, ReportingGovernanceApiClient>();
        services.AddSingleton<IEvidenceWorkbenchApiClient, EvidenceWorkbenchApiClient>();
        services.AddTransient<ReportingGovernanceWorkbenchViewModel>();
        services.AddTransient<EvidenceWorkbenchViewModel>();
        // Null-tolerant factory: the continuity client is owned by the accounting module, and the
        // record-release page degrades into explicit error text when it is absent.
        services.AddTransient(static sp => new OperationsRecordReleaseViewModel(
            sp.GetService<IOperationsControlCenterClient>()));
        services.AddTransient<ReportingWorkspaceShellStateProvider>();
        services.AddTransient<ReportingWorkspaceShellViewModel>();
        services.AddTransient<ReportingWorkspaceShellPage>();
        services.AddTransient<Meridian.Wpf.Views.EvidenceWorkbenchPage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() =>
        FeatureCapabilityCatalog.Reporting;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;
}
