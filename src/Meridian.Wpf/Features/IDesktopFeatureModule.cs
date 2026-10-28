using Microsoft.Extensions.DependencyInjection;
using Meridian.Core.Config;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Features;

/// <summary>
/// Defines dependency registration and optional page, workspace, and capability contributions for a desktop feature.
/// </summary>
public interface IDesktopFeatureModule
{
    void Register(IServiceCollection services);

    IReadOnlyList<ShellPageDescriptor> DescribePages() => Array.Empty<ShellPageDescriptor>();

    WorkspaceCapabilityDescriptor? DescribeWorkspace() => null;

    IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() => Array.Empty<FeatureCapabilityDescriptor>();
}
