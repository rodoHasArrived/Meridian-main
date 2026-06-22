using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers relationship-aware provider routing services.
/// </summary>
internal sealed class ProviderRoutingFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.TryAddSingleton<IProviderConnectionHealthSource, DefaultProviderConnectionHealthSource>();
        services.TryAddSingleton<IProviderCertificationRunner, DefaultProviderCertificationRunner>();
        services.TryAddSingleton<IProviderFamilyCatalogService, ProviderFamilyCatalogService>();

        services.TryAddSingleton<ProviderConnectionService>();
        services.TryAddSingleton<ProviderBindingService>();
        foreach (var handler in DefaultProviderSetupHandlers.Create())
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IProviderSetupHandler), handler));
        }
        services.TryAddSingleton<IProviderSetupRegistry, ProviderSetupRegistry>();
        services.TryAddSingleton<ProviderSetupService>();
        services.TryAddSingleton<KernelObservabilityService>();
        services.TryAddSingleton<ProviderRoutingService>();
        services.TryAddSingleton<IBestOfBreedProviderSelector, BestOfBreedProviderSelector>();
        services.TryAddSingleton<ICapabilityRouter>(sp => sp.GetRequiredService<ProviderRoutingService>());
        services.TryAddSingleton<ProviderRouteExplainabilityService>();
        services.TryAddSingleton<ProviderCertificationService>();
        services.TryAddSingleton<ProviderTrustScoringService>();
        services.TryAddSingleton<ProviderPresetService>();

        return services;
    }
}
