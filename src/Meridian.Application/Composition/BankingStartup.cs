using Meridian.Storage.Banking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class BankingStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_BANKING_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_BANKING_SCHEMA";
    internal const string DefaultSchema = "banking";

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
                "Skipping Banking database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<BankingStoreOptions>();
        var runner = new BankingMigrationRunner(options);
        runner.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
        logger?.LogInformation(
            "Banking schema '{Schema}' is ready.",
            options.Schema);
    }
}
