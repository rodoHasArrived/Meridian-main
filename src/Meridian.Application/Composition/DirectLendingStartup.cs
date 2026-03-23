using Meridian.Storage.DirectLending;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class DirectLendingStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_DIRECT_LENDING_SCHEMA";
    internal const string DefaultConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=secret";
    internal const string DefaultSchema = "direct_lending";

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

        var migrationRunner = serviceProvider.GetService<DirectLendingMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Direct lending migration runner is not registered for this host.");
            return;
        }

        migrationRunner.EnsureMigratedAsync().GetAwaiter().GetResult();
        logger?.LogInformation("Direct lending schema is ready.");
    }
}
