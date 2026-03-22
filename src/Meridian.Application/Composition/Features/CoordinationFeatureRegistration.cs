using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers shared-storage coordination and lease ownership services.
/// </summary>
internal sealed class CoordinationFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.AddSingleton(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return config.Coordination ?? new CoordinationConfig();
        });

        services.AddSingleton<ICoordinationStore>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var coordination = config.Coordination ?? new CoordinationConfig();
            return new SharedStorageCoordinationStore(coordination, config.DataRoot);
        });

        services.AddSingleton<ILeaseManager>(sp =>
        {
            var config = sp.GetRequiredService<CoordinationConfig>();
            var store = sp.GetRequiredService<ICoordinationStore>();
            return new LeaseManager(config, store);
        });
        services.AddSingleton<ISubscriptionOwnershipService, SubscriptionOwnershipService>();
        services.AddSingleton<IScheduledWorkOwnershipService, ScheduledWorkOwnershipService>();

        return services;
    }
}
