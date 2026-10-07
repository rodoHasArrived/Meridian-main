using Meridian.Application.Tenancy;
using Meridian.Contracts.Tenancy;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition;

/// <summary>Shares account authority and local migration posture across runtime and command graphs.</summary>
internal static class PortfolioRecordsServiceRegistration
{
    internal static IServiceCollection AddPortfolioRecordServices(
        this IServiceCollection services,
        CompositionConfiguration configuration)
    {
        services.TryAddSingleton(sp => new LocalTenantMigrationGate(
            sp.GetService<TenantScopeEnforcementOptions>() ?? TenantScopeEnforcementOptions.FailClosed));

        if (configuration.IsConfigured(FundAccountsStartup.ConnectionStringVariable))
        {
            if (configuration.UsesEnvironment)
                FundAccountsStartup.EnsureEnvironmentDefaults();
            services.TryAddSingleton(new FundAccountStoreOptions
            {
                ConnectionString = configuration.GetConnectionString(FundAccountsStartup.ConnectionStringVariable)!,
                Schema = configuration.GetSchema(FundAccountsStartup.SchemaVariable, FundAccountsStartup.DefaultSchema)
            });
            services.TryAddSingleton<IFundAccountStore, PostgresFundAccountStore>();
            services.TryAddSingleton<PostgresFundAccountService>();
            services.TryAddSingleton<IFundAccountService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
            services.TryAddSingleton<IAccountManagementService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
            services.TryAddSingleton<IAccountQueryService>(sp => sp.GetRequiredService<PostgresFundAccountService>());
        }
        else
        {
            services.TryAddSingleton<IFundAccountService>(sp =>
            {
                var persistencePath = Path.Combine(sp.GetRequiredService<StorageOptions>().RootPath,
                    "governance", "fund-accounts.json");
                return new TenantGuardedLocalFundAccountService(new InMemoryFundAccountService(persistencePath),
                    sp.GetRequiredService<LocalTenantMigrationGate>());
            });
            services.TryAddSingleton<IAccountManagementService>(sp =>
                (IAccountManagementService)sp.GetRequiredService<IFundAccountService>());
            services.TryAddSingleton<IAccountQueryService>(sp =>
                (IAccountQueryService)sp.GetRequiredService<IFundAccountService>());
        }

        return services;
    }
}
