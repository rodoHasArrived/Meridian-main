using Meridian.Storage.FundStructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundStructureStartup
{
    // Retain the established migration sequence across every host.
    internal static DatabaseStartupStep StartupStep { get; } =
        new("fund-structure", ["fund-accounts"], EnsureDatabaseReadyAsync);

    internal const string ConnectionStringVariable = "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_STRUCTURE_SCHEMA";
    internal const string DefaultSchema = "fund_structure";

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
                "Skipping Fund Structure database readiness because {ConnectionStringVariable} is not configured.",
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
        var options = serviceProvider.GetRequiredService<FundStructureStoreOptions>();
        var readiness = serviceProvider.GetRequiredService<DatabaseMigrationReadinessReceipt>();
        var runner = new FundStructureMigrationRunner(options);
        await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        readiness.MarkFundStructureReady();
        logger?.LogInformation(
            "Fund structure schema '{Schema}' is ready.",
            options.Schema);
    }
}
