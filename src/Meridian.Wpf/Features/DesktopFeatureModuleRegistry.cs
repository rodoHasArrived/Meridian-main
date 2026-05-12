using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
