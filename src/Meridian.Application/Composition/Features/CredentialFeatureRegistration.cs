using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Application.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers credential management services.
/// </summary>
internal sealed class CredentialFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.AddSingleton<CredentialTestingService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new CredentialTestingService(config.DataRoot);
        });

        services.AddSingleton<OAuthTokenRefreshService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new OAuthTokenRefreshService(config.DataRoot);
        });

        return services;
    }
}
