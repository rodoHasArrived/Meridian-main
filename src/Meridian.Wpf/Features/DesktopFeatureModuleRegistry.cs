using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Shell.Services;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    private static readonly IDesktopFeatureModule[] Modules =
    [
        new Trading.TradingFeatureModule(),
        new Data.DataFeatureModule(),
        new Settings.SettingsFeatureModule()
    ];

    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(ShellPageRegistryBuilder.BuildDefault());

        foreach (var module in Modules)
        {
            module.Register(services);
        }

        return services;
    }

    public static IReadOnlyList<IDesktopFeatureModule> GetModules() => Modules;
}
