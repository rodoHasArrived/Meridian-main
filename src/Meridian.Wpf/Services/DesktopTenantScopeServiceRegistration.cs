using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Wpf.Services;

/// <summary>Applies the shared tenant posture to the desktop's independently composed host.</summary>
public static class DesktopTenantScopeServiceRegistration
{
    public static IServiceCollection AddDesktopTenantScopeServices(this IServiceCollection services)
    {
        services.TryAddSingleton<DesktopWorkstationTenantContextAccessor>();
        services.TryAddSingleton<IWorkstationTenantContextAccessor>(sp =>
            sp.GetRequiredService<DesktopWorkstationTenantContextAccessor>());
        services.AddFundScopeTenantServices<DesktopFundScopeTenantAccessor>();
        services.TryAddSingleton<UserProfileRegistry>(sp => new UserProfileRegistry(
            roleProfileStore: null,
            accountStore: sp.GetRequiredService<IUserAccountStore>(),
            tenantScope: sp.GetRequiredService<TenantScopeEnforcementOptions>()));
        return services;
    }
}

/// <summary>
/// Resolves direct desktop reads from the live authenticated session. Background authority cannot
/// supply a missing desktop session or replace the signed-in operator's tenant.
/// </summary>
public sealed class DesktopFundScopeTenantAccessor(DesktopWorkstationTenantContextAccessor context)
    : IFundScopeTenantAccessor
{
    public string? ResolveCallerTenant()
        => context.TryGetCurrent(out var current) ? current.TenantId : null;
}
