using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class SecurityMasterStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_SECURITY_MASTER_SCHEMA";
    internal const string DefaultConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=secret";
    internal const string DefaultSchema = "security_master";

    public static void EnsureEnvironmentDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, DefaultConnectionString);
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
        {
            Environment.SetEnvironmentVariable(SchemaVariable, DefaultSchema);
        }
    }

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        EnsureEnvironmentDefaults();

        var migrationRunner = serviceProvider.GetService<SecurityMasterMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Security Master migration runner is not registered for this host.");
            return;
        }

        migrationRunner.EnsureMigratedAsync().GetAwaiter().GetResult();
        logger?.LogInformation("Security Master schema is ready.");
    }
}
