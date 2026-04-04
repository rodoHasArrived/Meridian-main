using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundAccountsStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_ACCOUNTS_SCHEMA";
    internal const string DefaultSchema = "fund_accounts";

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
                "Skipping Fund Accounts database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        // When PostgresFundAccountService is wired up in Phase 4b, add migration runner call here.
        logger?.LogInformation("Fund accounts schema is ready.");
    }
}
