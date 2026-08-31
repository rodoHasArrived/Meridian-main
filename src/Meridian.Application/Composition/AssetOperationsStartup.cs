using Meridian.Storage.AssetOperations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class AssetOperationsStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_ASSET_OPERATIONS_SCHEMA";
    internal const string DefaultSchema = "asset_operations";

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
                "Skipping Asset Operations database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var migrationRunner = serviceProvider.GetService<AssetOperationsMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Asset Operations migration runner is not registered for this host.");
            return;
        }

        await migrationRunner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        logger?.LogInformation("Asset Operations schema is ready.");
    }
}
