using Meridian.Wpf.Features.Data.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Data;

public sealed class DataFeatureModule : IDesktopFeatureModule
{
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IDataWorkspaceShellSnapshotService, DataWorkspaceShellSnapshotService>();
        services.AddTransient<IDataWorkspaceShellPresentationService, DataWorkspaceShellPresentationService>();
        services.AddTransient<DataWorkspaceShellViewModel>();
        services.AddTransient<DataWorkspaceShellPage>();
    }
}
