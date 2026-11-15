using Meridian.Application.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

internal static class SecurityAssetProfilePromotionServiceRegistration
{
    internal static void AddSecurityAssetProfilePromotionServices(this IServiceCollection services)
    {
        services.TryAddSingleton<ISecurityAssetProfileUsageStore>(provider =>
            provider.GetService<ISecurityMasterStore>() as ISecurityAssetProfileUsageStore ?? new UnavailableProfileUsageStore());
        services.TryAddSingleton<ISecurityAssetProfilePromotionReadService, SecurityAssetProfilePromotionReadService>();
    }

    private sealed class UnavailableProfileUsageStore : ISecurityAssetProfileUsageStore
    {
        public Task<IReadOnlyList<SecurityAssetProfileUsageCount>> GetProfileUsageCountsAsync(CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<SecurityAssetProfileUsageCount>>(new NotSupportedException("Profile usage is unavailable in this store."));
    }
}
