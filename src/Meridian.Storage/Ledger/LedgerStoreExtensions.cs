using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Storage.Ledger;

public static class LedgerStoreExtensions
{
    public static IServiceCollection AddLedgerJournalStore(this IServiceCollection services, string connStr)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new ArgumentException("A ledger journal store connection string is required.", nameof(connStr));
        }

        services.AddSingleton(new LedgerJournalStoreOptions { ConnectionString = connStr });
        services.AddSingleton<PostgresLedgerJournalStore>();
        services.AddSingleton<ILedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
        services.AddSingleton<ITransactionalLedgerJournalStore>(sp => sp.GetRequiredService<PostgresLedgerJournalStore>());
        services.AddSingleton<IGovernedLedgerPostingTarget, DurableLedgerPostingTarget>();
        services.AddSingleton(sp => new DurableAutomatedJournalPoster(
            sp.GetRequiredService<IGovernedLedgerPostingTarget>()));
        services.AddSingleton<IAutomatedJournalPostingTarget>(sp =>
            sp.GetRequiredService<DurableAutomatedJournalPoster>());
        services.AddSingleton<PostgresAccountingConfigurationStore>();
        services.AddSingleton<IAccountingConfigurationStore>(sp => sp.GetRequiredService<PostgresAccountingConfigurationStore>());
        services.AddSingleton<IAccountingActionAuditStore>(sp => sp.GetRequiredService<PostgresAccountingConfigurationStore>());
        services.AddSingleton<LedgerMigrationRunner>();
        services.AddSingleton<ILedgerBookService>(sp =>
            new PostgresLedgerBookService(
                sp.GetRequiredService<ILedgerJournalStore>(),
                sp.GetService<IOperatorInboxService>()));

        return services;
    }
}
