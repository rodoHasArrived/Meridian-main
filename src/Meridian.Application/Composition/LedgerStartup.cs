using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class LedgerStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_LEDGER_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_LEDGER_SCHEMA";
    internal const string DefaultSchema = "ledger";

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
                "Skipping ledger database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var migrationRunner = serviceProvider.GetService<LedgerMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Ledger migration runner is not registered for this host.");
            return;
        }

        Task.Run(() => migrationRunner.EnsureMigratedAsync()).GetAwaiter().GetResult();
        logger?.LogInformation("Ledger schema is ready.");
    }
}
