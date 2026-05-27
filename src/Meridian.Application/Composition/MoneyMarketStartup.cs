using Meridian.Storage.MoneyMarket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class MoneyMarketStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_MONEY_MARKET_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_MONEY_MARKET_SCHEMA";
    internal const string DefaultSchema = "money_market";

    public static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    public static void EnsureEnvironmentDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
            Environment.SetEnvironmentVariable(SchemaVariable, DefaultSchema);
    }

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Money Market database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<MoneyMarketStoreOptions>();
        var runner = new MoneyMarketMigrationRunner(options);
        runner.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
        logger?.LogInformation("Money market schema '{Schema}' is ready.", options.Schema);
    }
}
