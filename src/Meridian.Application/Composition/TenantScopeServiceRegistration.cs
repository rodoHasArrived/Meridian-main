using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition;

/// <summary>Registers the read posture and retained tenant authority for every host graph.</summary>
public static class TenantScopeServiceRegistration
{
    public static IServiceCollection AddFundScopeTenantServices(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TenantScopeEnforcementOptions)))
        {
            // Attribute retained data before selecting fail-closed. An absent setting keeps the
            // deployment-boundary default; an invalid explicit setting still refuses composition.
            services.AddSingleton(TenantScopeEnforcementOptions.FromEnvironmentValue(
                Environment.GetEnvironmentVariable(TenantScopeEnforcementOptions.EnvironmentVariable),
                TenantScopeEnforcementOptions.DeploymentBoundary));
        }

        services.TryAddSingleton<IFundScopeTenantAccessor, RetainedAuthorityFundScopeTenantAccessor>();
        return services;
    }

    /// <summary>Replaces the worker fallback with the host adapter, preserving explicit registrations.</summary>
    public static IServiceCollection AddFundScopeTenantServices<TAccessor>(this IServiceCollection services)
        where TAccessor : class, IFundScopeTenantAccessor
    {
        services.AddFundScopeTenantServices();
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IFundScopeTenantAccessor)
                && descriptor.ImplementationType == typeof(RetainedAuthorityFundScopeTenantAccessor))
            {
                services.RemoveAt(index);
            }
        }

        services.TryAddSingleton<IFundScopeTenantAccessor, TAccessor>();
        return services;
    }
}

/// <summary>Resolves only explicitly established worker authority, without an HTTP dependency.</summary>
public sealed class RetainedAuthorityFundScopeTenantAccessor : IFundScopeTenantAccessor
{
    public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
}
