using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers core configuration services that all hosts need.
/// </summary>
internal sealed class ConfigurationFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // ConfigStore - unified configuration access
        if (!string.IsNullOrEmpty(options.ConfigPath))
        {
            services.AddSingleton(new ConfigStore(options.ConfigPath));
        }
        else
        {
            services.AddSingleton<ConfigStore>();
        }

        // ConfigurationService - consolidated configuration operations
        services.AddSingleton<ConfigurationService>();

        // Configuration utilities
        services.AddSingleton<ConfigTemplateGenerator>();
        services.AddSingleton<ConfigEnvironmentOverride>();
        services.AddSingleton<DryRunService>();

        return services;
    }
}
