using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Core.Logging;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Connects the application's prepared configuration and credential store to the
/// descriptor-driven provider registration path shared with other hosts.
/// </summary>
internal sealed class ProviderFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // Register credential resolver
        services.AddSingleton<IProviderCredentialResolver>(sp =>
        {
            var configService = sp.GetRequiredService<ConfigurationService>();
            var fallback = new ConfigurationServiceCredentialAdapter(configService);
            var credentialStore = sp.GetService<IProviderCredentialStore>();
            return credentialStore is null
                ? fallback
                : new StoredProviderCredentialResolver(credentialStore, fallback);
        });

        var registrationConfig = new ConfigStore(options.ConfigPath).Load();
        services.AddProviderServices(
            registrationConfig,
            sp => LoadProviderBootstrapConfig(sp, sp.GetRequiredService<ConfigStore>()),
            LoggingSetup.ForContext<ProviderFactory>());
        return services;
    }

    private static AppConfig LoadProviderBootstrapConfig(IServiceProvider sp, ConfigStore configStore)
    {
        var configService = sp.GetRequiredService<ConfigurationService>();
        // Provider selection is itself backed by ProviderRegistry. Applying self-healing
        // while that registry is being constructed recursively resolves the registry and
        // leaves hosted composition stuck in its service factory. Bootstrap from the
        // overlaid, credential-resolved configuration; callers can still apply self-healing
        // after the provider graph is available.
        return configService.LoadAndPrepareConfig(configStore.ConfigPath, applySelfHealing: false);
    }

}
