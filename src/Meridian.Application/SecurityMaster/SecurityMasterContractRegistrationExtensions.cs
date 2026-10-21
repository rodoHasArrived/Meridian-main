using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CanonicalAdjuster = Meridian.Contracts.SecurityMaster.ILivePositionCorporateActionAdjuster;
using CanonicalResolver = Meridian.Contracts.SecurityMaster.ISecurityResolver;

namespace Meridian.Application.SecurityMaster;

/// <summary>Bridges existing host registrations to the canonical Security Master contracts.</summary>
public static class SecurityMasterContractRegistrationExtensions
{
    /// <summary>
    /// Shares the registered query service through its narrow contracts and bridges either
    /// generation of resolver/adjuster registrations. Explicit host registrations are preserved.
    /// </summary>
#pragma warning disable CS0618 // Both generations are intentional at this compatibility boundary.
    public static IServiceCollection AddSecurityMasterContractAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISecurityIdentityQueryService>(sp =>
            sp.GetRequiredService<ISecurityMasterQueryService>());
        services.TryAddSingleton<ISecurityTradingParametersQueryService>(sp =>
            sp.GetRequiredService<ISecurityMasterQueryService>());
        services.TryAddSingleton<ISecurityCorporateActionsQueryService>(sp =>
            sp.GetRequiredService<ISecurityMasterQueryService>());

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISecurityResolver)))
        {
            services.TryAddSingleton<CanonicalResolver>(sp => sp.GetRequiredService<ISecurityResolver>());
        }
        else if (services.Any(descriptor => descriptor.ServiceType == typeof(CanonicalResolver)))
        {
            services.TryAddSingleton<ISecurityResolver>(sp =>
                sp.GetRequiredService<CanonicalResolver>() is ISecurityResolver legacy
                    ? legacy
                    : new SecurityResolverCompatibilityAdapter(sp.GetRequiredService<CanonicalResolver>()));
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CorporateActions.ILivePositionCorporateActionAdjuster)))
        {
            services.TryAddSingleton<CanonicalAdjuster>(sp =>
                sp.GetRequiredService<CorporateActions.ILivePositionCorporateActionAdjuster>());
        }
        else if (services.Any(descriptor => descriptor.ServiceType == typeof(CanonicalAdjuster)))
        {
            services.TryAddSingleton<CorporateActions.ILivePositionCorporateActionAdjuster>(sp =>
                sp.GetRequiredService<CanonicalAdjuster>() is CorporateActions.ILivePositionCorporateActionAdjuster legacy
                    ? legacy
                    : new LivePositionCorporateActionAdjusterAdapter(sp.GetRequiredService<CanonicalAdjuster>()));
        }

        return services;
    }
#pragma warning restore CS0618
}
