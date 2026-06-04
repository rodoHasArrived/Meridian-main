using Meridian.Storage.DirectLending;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class DirectLendingStartup
{
    // Legacy direct-lending variables remain supported for isolated test databases and
    // controlled migration windows. Production defaults inherit Security Master storage.
    internal const string ConnectionStringVariable = "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_DIRECT_LENDING_SCHEMA";
    internal const string DefaultSchema = SecurityMasterStartup.DefaultSchema;

    public static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(GetEffectiveConnectionString());

    public static bool HasDedicatedConfiguration()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    internal static string GetEffectiveConnectionString()
        => FirstConfiguredValue(
            Environment.GetEnvironmentVariable(ConnectionStringVariable),
            Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable));

    internal static string GetEffectiveSchema()
        => FirstConfiguredValue(
            Environment.GetEnvironmentVariable(SchemaVariable),
            Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable),
            DefaultSchema);

    public static void EnsureEnvironmentDefaults()
    {
        SecurityMasterStartup.EnsureEnvironmentDefaults();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
        {
            Environment.SetEnvironmentVariable(SchemaVariable, GetEffectiveSchema());
        }
    }

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Direct Lending database readiness because neither {ConnectionStringVariable} nor {SecurityMasterConnectionStringVariable} is configured.",
                ConnectionStringVariable,
                SecurityMasterStartup.ConnectionStringVariable);
            return;
        }

        var migrationRunner = serviceProvider.GetService<DirectLendingMigrationRunner>();
        if (migrationRunner is null)
        {
            logger?.LogDebug("Direct lending migration runner is not registered for this host.");
            return;
        }

        Task.Run(() => migrationRunner.EnsureMigratedAsync()).GetAwaiter().GetResult();
        logger?.LogInformation("Direct Lending persistence is ready under the Security Master storage lane.");
    }

    private static string FirstConfiguredValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
