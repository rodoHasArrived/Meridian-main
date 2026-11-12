using Meridian.Application.Composition;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Storage.Workstation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

public static class OperatorTriageServiceCollectionExtensions
{
    public static IServiceCollection AddOperatorTriageStorage(this IServiceCollection services,
        CompositionConfiguration configuration, bool isProduction)
    {
        var connectionString = configuration["MERIDIAN_OPERATOR_TRIAGE_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("MERIDIAN_LEDGER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton<IOperatorTriageStore>(new PostgresOperatorTriageStore(new OperatorTriageStoreOptions
            {
                ConnectionString = MeridianDatabaseEnvironment.NormalizeToConnectionString(connectionString),
                Schema = configuration.GetSchema("MERIDIAN_OPERATOR_TRIAGE_SCHEMA", "operator_triage")
            }));
        }
        else if (!isProduction)
        {
            services.TryAddSingleton<IOperatorTriageStore>(sp =>
                new FileOperatorTriageStore(sp.GetRequiredService<StorageOptions>().RootPath));
        }
        // Missing production persistence is reported as unavailable; it never borrows a demo store.
        return services;
    }
}
