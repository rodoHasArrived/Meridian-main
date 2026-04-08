using Meridian.Application.ProviderRouting;
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
        services.TryAddSingleton<ProviderRoutingService>();
        services.TryAddSingleton<ICapabilityRouter>(sp => sp.GetRequiredService<ProviderRoutingService>());
        services.TryAddSingleton<ProviderRouteExplainabilityService>();
        services.TryAddSingleton<ProviderCertificationService>();
        services.TryAddSingleton<ProviderTrustScoringService>();
        services.TryAddSingleton<ProviderPresetService>();

        return services;
    }
}
