using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Identity;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        // Desktop activation must inspect retained stores before resolving workspaces or showing
        // a shell. Replace only the hosted guard with the same cached pre-shell startup gate.
        foreach (var descriptor in services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(TenantCutoverGuardService)).ToArray())
            services.Remove(descriptor);
        services.TryAddSingleton<DatabaseMigrationReadinessReceipt>();
        services.TryAddSingleton<ITenantCutoverStartupPrerequisites, TenantCutoverStartupPrerequisites>();
        services.TryAddSingleton<TenantCutoverGuardService>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DesktopTenantStartup)))
        {
            services.AddSingleton<DesktopTenantStartup>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DesktopTenantStartup>());
        }
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
