using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    private static readonly IDesktopFeatureModule[] Modules =
    [
        new Trading.TradingFeatureModule(),
        new Data.DataFeatureModule()
    ];

    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var module in Modules)
        {
            module.Register(services);
        }

        return services;
    }
}
