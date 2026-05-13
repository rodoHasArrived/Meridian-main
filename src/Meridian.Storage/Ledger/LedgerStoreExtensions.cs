using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
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
        services.AddSingleton<ILedgerJournalStore, PostgresLedgerJournalStore>();
        services.AddSingleton<ILedgerBookService>(sp =>
            new PostgresLedgerBookService(
                sp.GetRequiredService<ILedgerJournalStore>(),
                sp.GetService<IOperatorInboxService>()));

        return services;
    }
}
