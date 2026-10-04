using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
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

        services.AddSingleton<IProviderCredentialStore>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new FileProviderCredentialStore(config.DataRoot);
        });

        services.AddSingleton<IScopedProviderCredentialStore>(sp =>
            sp.GetRequiredService<IProviderCredentialStore>() as IScopedProviderCredentialStore
            ?? throw new InvalidOperationException("Configured credential vault does not support scoped ownership."));

        services.AddSingleton<OAuthTokenRefreshService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new OAuthTokenRefreshService(config.DataRoot, vault:
                sp.GetRequiredService<IProviderCredentialStore>() as IOAuthTokenVault
                ?? throw new InvalidOperationException("Configured credential vault does not support OAuth token persistence."));
        });

        return services;
    }
}
