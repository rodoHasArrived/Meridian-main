using Meridian.Wpf.Features.Settings.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Settings;

public sealed class SettingsFeatureModule : IDesktopFeatureModule
{
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ISettingsWorkspaceShellSnapshotService, SettingsWorkspaceShellSnapshotService>();
        services.AddTransient<ISettingsWorkspaceShellPresentationService, SettingsWorkspaceShellPresentationService>();
        services.AddTransient<SettingsWorkspaceShellViewModel>();
        services.AddTransient<SettingsWorkspaceShellPage>();
    }
}
