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

        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(ISecurityMasterQueryService)))
        {
            TryAddAlias<ISecurityIdentityQueryService, ISecurityMasterQueryService>(services, sp =>
                sp.GetRequiredService<ISecurityMasterQueryService>());
            TryAddAlias<ISecurityTradingParametersQueryService, ISecurityMasterQueryService>(services, sp =>
                sp.GetRequiredService<ISecurityMasterQueryService>());
            TryAddAlias<ISecurityCorporateActionsQueryService, ISecurityMasterQueryService>(services, sp =>
                sp.GetRequiredService<ISecurityMasterQueryService>());
        }

        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(ISecurityResolver)))
        {
            TryAddAlias<CanonicalResolver, ISecurityResolver>(services, sp => sp.GetRequiredService<ISecurityResolver>());
        }
        else if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(CanonicalResolver)))
        {
            TryAddAlias<ISecurityResolver, CanonicalResolver>(services, sp =>
            {
                var canonical = sp.GetRequiredService<CanonicalResolver>();
                return canonical as ISecurityResolver ?? new SecurityResolverCompatibilityAdapter(canonical);
            });
        }

        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(CorporateActions.ILivePositionCorporateActionAdjuster)))
        {
            TryAddAlias<CanonicalAdjuster, CorporateActions.ILivePositionCorporateActionAdjuster>(services, sp =>
                sp.GetRequiredService<CorporateActions.ILivePositionCorporateActionAdjuster>());
        }
        else if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(CanonicalAdjuster)))
        {
            TryAddAlias<CorporateActions.ILivePositionCorporateActionAdjuster, CanonicalAdjuster>(services, sp =>
            {
                var canonical = sp.GetRequiredService<CanonicalAdjuster>();
                return canonical as CorporateActions.ILivePositionCorporateActionAdjuster
                    ?? new LivePositionCorporateActionAdjusterAdapter(canonical);
            });
        }

        return services;
    }

    // Match the last unkeyed registration, which GetRequiredService resolves. A singleton
    // alias must never capture a host's scoped or transient implementation.
    private static void TryAddAlias<TAlias, TSource>(
        IServiceCollection services, Func<IServiceProvider, TAlias> factory)
        where TAlias : class
        where TSource : class
    {
        var source = services.LastOrDefault(descriptor =>
            !descriptor.IsKeyedService && descriptor.ServiceType == typeof(TSource));
        if (source is not null)
            services.TryAdd(ServiceDescriptor.Describe(typeof(TAlias), sp => factory(sp), source.Lifetime));
    }
#pragma warning restore CS0618
}
