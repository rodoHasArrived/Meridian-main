using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundAccountsStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_ACCOUNTS_SCHEMA";
    internal const string DefaultSchema = "fund_accounts";

    public static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    public static void EnsureEnvironmentDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
        {
            Environment.SetEnvironmentVariable(SchemaVariable, DefaultSchema);
        }
    }

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Fund Accounts database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<FundAccountStoreOptions>();
        var runner = new FundAccountMigrationRunner(options);
        runner.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
        logger?.LogInformation(
            "Fund accounts schema '{Schema}' is ready.",
            options.Schema);

        // On first startup, import any existing JSON snapshot so local data carries over.
        var store = serviceProvider.GetService<IFundAccountStore>();
        var storageRoot = serviceProvider.GetService<StorageOptions>();
        if (store is null || storageRoot is null)
            return;

        var snapshotPath = Path.Combine(storageRoot.RootPath, "governance", "fund-accounts.json");
        if (!File.Exists(snapshotPath))
            return;

        var isEmpty = Task.Run(() => store.IsEmptyAsync(CancellationToken.None)).GetAwaiter().GetResult();
        if (!isEmpty)
            return;

        logger?.LogInformation("Fund accounts store is empty — importing existing JSON snapshot from {Path}.", snapshotPath);
        ImportSnapshotAsync(snapshotPath, store, logger).GetAwaiter().GetResult();
        var importedPath = snapshotPath + ".imported";
        File.Move(snapshotPath, importedPath, overwrite: true);
        logger?.LogInformation("Snapshot import complete. Original file renamed to {ImportedPath}.", importedPath);
    }

    private static async Task ImportSnapshotAsync(string snapshotPath, IFundAccountStore store, ILogger? logger)
    {
        // Load the snapshot via the InMemory service (it knows how to deserialize its own format)
        // and replay each account and its sub-documents into the Postgres store.
        var inMemory = new InMemoryFundAccountService(snapshotPath);
        var allAccounts = await inMemory.QueryAccountsAsync(new Meridian.Contracts.FundStructure.AccountStructureQuery(), CancellationToken.None).ConfigureAwait(false);

        logger?.LogInformation("Importing {Count} accounts from snapshot.", allAccounts.Count);
        foreach (var account in allAccounts)
        {
            await store.UpsertAccountAsync(account, CancellationToken.None).ConfigureAwait(false);

            var snapshots = await inMemory.GetBalanceHistoryAsync(account.AccountId, null, null, CancellationToken.None).ConfigureAwait(false);
            foreach (var snap in snapshots)
                await store.InsertBalanceSnapshotAsync(snap, CancellationToken.None).ConfigureAwait(false);

            var reconRuns = await inMemory.GetReconciliationRunsAsync(account.AccountId, CancellationToken.None).ConfigureAwait(false);
            foreach (var run in reconRuns)
            {
                var results = await inMemory.GetReconciliationResultsAsync(run.ReconciliationRunId, CancellationToken.None).ConfigureAwait(false);
                await store.InsertReconciliationRunAsync(run, results, CancellationToken.None).ConfigureAwait(false);
            }

            var syncHistory = await inMemory.GetSyncHistoryAsync(account.AccountId, null, CancellationToken.None).ConfigureAwait(false);
            foreach (var entry in syncHistory)
                await store.InsertSyncHistoryAsync(entry, CancellationToken.None).ConfigureAwait(false);

            var marginSnapshots = await inMemory.GetMarginSnapshotsAsync(account.AccountId, CancellationToken.None).ConfigureAwait(false);
            foreach (var margin in marginSnapshots)
                await store.UpsertMarginSnapshotAsync(margin, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
