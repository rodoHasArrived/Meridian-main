using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundAccountsStartup
{
    // Retain the established migration sequence across every host.
    internal static DatabaseStartupStep StartupStep { get; } =
        new("fund-accounts", ["asset-operations"], EnsureDatabaseReadyAsync);

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

    public static async Task EnsureDatabaseReadyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Fund Accounts database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        await EnsureRegisteredDatabaseReadyAsync(serviceProvider, cancellationToken, logger).ConfigureAwait(false);
    }

    internal static async Task EnsureRegisteredDatabaseReadyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken,
        ILogger? logger)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = serviceProvider.GetRequiredService<FundAccountStoreOptions>();
        var readiness = serviceProvider.GetRequiredService<DatabaseMigrationReadinessReceipt>();
        var runner = new FundAccountMigrationRunner(options);
        await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        readiness.MarkFundAccountsReady();
        logger?.LogInformation(
            "Fund accounts schema '{Schema}' is ready.",
            options.Schema);
    }
}
