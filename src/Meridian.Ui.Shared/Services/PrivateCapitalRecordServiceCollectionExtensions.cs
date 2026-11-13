using Meridian.PortfolioRecords.PrivateCapital;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

public static class PrivateCapitalRecordServiceCollectionExtensions
{
    /// <summary>Uses the existing account database or durable, partitioned local record authority.</summary>
    public static IServiceCollection AddPrivateCapitalRecords(this IServiceCollection services)
    {
        services.TryAddSingleton<IPrivateCapitalRecordStore>(provider =>
            provider.GetService<FundAccountStoreOptions>() is { ConnectionString.Length: > 0 } options
                ? new PostgresPrivateCapitalRecordStore(options)
                : new FilePrivateCapitalRecordStore(provider.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IPrivateCapitalRecordAuthority, PrivateCapitalRecordAuthority>();
        services.TryAddSingleton<PrivateCapitalRecordService>();
        return services;
    }
}
