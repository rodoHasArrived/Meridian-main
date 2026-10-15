using Meridian.Strategies.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

public static class PromotionTrustEvidenceServiceCollectionExtensions
{
    /// <summary>Registers the same source-backed promotion trust provider for browser and desktop hosts.</summary>
    public static IServiceCollection AddPromotionTrustEvidenceServices(this IServiceCollection services)
    {
        services.TryAddSingleton(Dk1TrustGateReadinessOptions.Default);
        services.TryAddSingleton<Dk1TrustGateReadinessService>();
        services.TryAddSingleton<IPromotionTrustEvidenceProvider, Dk1PromotionTrustEvidenceProvider>();
        return services;
    }
}
