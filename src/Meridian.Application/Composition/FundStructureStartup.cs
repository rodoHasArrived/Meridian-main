using Meridian.Storage.FundStructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundStructureStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_STRUCTURE_SCHEMA";
    internal const string DefaultSchema = "fund_structure";

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
                "Skipping Fund Structure database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<FundStructureStoreOptions>();
        var runner = new FundStructureMigrationRunner(options);
        runner.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
        logger?.LogInformation(
            "Fund structure schema '{Schema}' is ready.",
            options.Schema);
    }
}
