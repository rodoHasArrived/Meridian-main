using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public interface IDesktopFeatureModule
{
    void Register(IServiceCollection services);
}
