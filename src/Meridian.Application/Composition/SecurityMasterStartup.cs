using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class SecurityMasterStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_SECURITY_MASTER_SCHEMA";
    internal const string DefaultSchema = "security_master";

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
                "Skipping Security Master database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var migrationRunner = serviceProvider.GetService<SecurityMasterMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Security Master migration runner is not registered for this host.");
            return;
        }

        await migrationRunner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        logger?.LogInformation("Security Master schema is ready.");
    }
}
