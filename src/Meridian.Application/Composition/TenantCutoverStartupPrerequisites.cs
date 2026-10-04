using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

/// <summary>Prepares the retained stores inspected by the tenant cutover guard before serving work.</summary>
public interface ITenantCutoverStartupPrerequisites
{
    Task PrepareAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Allows independently composed hosts to run the relevant schema migrations and legacy imports
/// before tenant inspection without starting unrelated background services.
/// </summary>
public sealed class TenantCutoverStartupPrerequisites(
    IServiceProvider services,
    ILogger<TenantCutoverStartupPrerequisites> logger) : ITenantCutoverStartupPrerequisites
{
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.GetService<LedgerJournalStoreOptions>() is { } ledgerOptions)
        {
            await new LedgerMigrationRunner(ledgerOptions).EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
            services.GetRequiredService<DatabaseMigrationReadinessReceipt>().MarkLedgerReady();
        }

        if (services.GetService<FundAccountStoreOptions>() is not null)
            await FundAccountsStartup.EnsureRegisteredDatabaseReadyAsync(services, cancellationToken, logger).ConfigureAwait(false);

        if (services.GetService<FundStructureStoreOptions>() is not null)
            await FundStructureStartup.EnsureRegisteredDatabaseReadyAsync(services, cancellationToken, logger).ConfigureAwait(false);
    }
}
