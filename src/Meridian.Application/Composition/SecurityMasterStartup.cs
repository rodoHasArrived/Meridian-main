using Meridian.Storage.SecurityMaster;
using Meridian.Storage.DirectLending;
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

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
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

        Task.Run(() => migrationRunner.EnsureMigratedAsync()).GetAwaiter().GetResult();
        logger?.LogInformation("Security Master schema is ready.");

        EnsureInheritedDirectLendingDatabaseReady(serviceProvider, logger);
    }

    private static void EnsureInheritedDirectLendingDatabaseReady(IServiceProvider serviceProvider, ILogger? logger)
    {
        if (DirectLendingStartup.HasDedicatedConfiguration())
        {
            logger?.LogDebug(
                "Skipping inherited Direct Lending migrations because {ConnectionStringVariable} is explicitly configured.",
                DirectLendingStartup.ConnectionStringVariable);
            return;
        }

        DirectLendingStartup.EnsureEnvironmentDefaults();
        if (!DirectLendingStartup.IsConfigured())
        {
            return;
        }

        var migrationRunner = serviceProvider.GetService<DirectLendingMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Direct Lending migration runner is not registered for the Security Master storage lane.");
            return;
        }

        Task.Run(() => migrationRunner.EnsureMigratedAsync()).GetAwaiter().GetResult();
        logger?.LogInformation("Direct Lending persistence is ready under the Security Master schema.");
    }
}
