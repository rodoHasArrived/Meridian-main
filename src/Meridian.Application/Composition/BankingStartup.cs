using Meridian.Storage.Banking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class BankingStartup
{
    // Retain the established migration sequence across every host.
    internal static DatabaseStartupStep StartupStep { get; } =
        new("banking", ["fund-structure"], EnsureDatabaseReadyAsync);

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
                "Skipping Banking database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<BankingStoreOptions>();
        var runner = new BankingMigrationRunner(options);
        await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        logger?.LogInformation(
            "Banking schema '{Schema}' is ready.",
            options.Schema);
    }
}
