using Meridian.Core.Config;
using Meridian.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public interface IDesktopFeatureModule
{
    void Register(IServiceCollection services);

    IReadOnlyList<ShellPageDescriptor> DescribePages() => Array.Empty<ShellPageDescriptor>();

    WorkspaceCapabilityDescriptor? DescribeWorkspace() => null;

    IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() => Array.Empty<FeatureCapabilityDescriptor>();
}
