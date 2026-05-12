using Meridian.Wpf.Features.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Register(services, new DataFeatureModule());
        return services;
    }

    private static void Register(IServiceCollection services, IDesktopFeatureModule module)
        => module.Register(services);
}
