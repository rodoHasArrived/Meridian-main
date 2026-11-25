using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.SecurityMaster;

/// <summary>Bridges existing host registrations to the canonical Security Master contracts.</summary>
public static class SecurityMasterContractRegistrationExtensions
{
    /// <summary>
    /// Shares the registered query service through its narrow contracts. Explicit host
    /// registrations are preserved.
    /// </summary>
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
}
