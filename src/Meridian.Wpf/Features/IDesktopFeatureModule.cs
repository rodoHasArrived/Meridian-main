using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Features;

public interface IDesktopFeatureModule
{
    void Register(IServiceCollection services);

    IReadOnlyList<ShellPageDescriptor> DescribePages() => Array.Empty<ShellPageDescriptor>();

    WorkspaceCapabilityDescriptor? DescribeWorkspace() => null;

    IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() => Array.Empty<FeatureCapabilityDescriptor>();
}
