using System.Text;
using Serilog;

namespace Meridian.Storage;

/// <summary>
/// Unified database configuration for every money-path store. Setting the single
/// <c>MERIDIAN_DATABASE_URL</c> environment variable enables PostgreSQL persistence for all
/// domains at once; each per-domain <c>MERIDIAN_*_CONNECTION_STRING</c> variable remains
/// supported and always wins over the unified value, so split-database deployments keep working.
/// </summary>
/// <remarks>
/// Propagation works by populating the per-domain variables that are unset, which keeps every
/// existing read site (composition roots, readiness probes, migration runners, tooling) working
/// without changes as long as <see cref="ApplyUnifiedDatabaseUrl"/> runs before those sites read
/// the environment. Direct Lending and Reporting are not propagated directly: they already
/// inherit from Security Master and Ledger respectively, and populating their dedicated
/// variables would change how their schema/migration inheritance resolves.
/// </remarks>
public static class MeridianDatabaseEnvironment
{
    public const string UnifiedVariable = "MERIDIAN_DATABASE_URL";

    /// <summary>
    /// Per-domain connection-string variables that inherit <see cref="UnifiedVariable"/> when unset.
    /// </summary>
    public static readonly IReadOnlyList<string> PropagatedConnectionStringVariables =
    [
        "MERIDIAN_LEDGER_CONNECTION_STRING",
        "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
        "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
        "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
        "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING",
        "MERIDIAN_BANKING_CONNECTION_STRING",
        "MERIDIAN_MONEY_MARKET_CONNECTION_STRING",
        "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING"
    ];

    private static readonly Lock SyncRoot = new();

    /// <summary>
    /// Propagates <c>MERIDIAN_DATABASE_URL</c> into every unset per-domain connection-string
    /// variable. Idempotent and safe to call from every composition root; explicitly set
    /// per-domain variables are never overwritten. Returns the variables populated by this call.
    /// </summary>
    public static IReadOnlyList<string> ApplyUnifiedDatabaseUrl()
    {
        var raw = Environment.GetEnvironmentVariable(UnifiedVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        lock (SyncRoot)
        {
            var connectionString = NormalizeToConnectionString(raw.Trim());
            var inherited = new List<string>();
            foreach (var variable in PropagatedConnectionStringVariables)
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                {
                    Environment.SetEnvironmentVariable(variable, connectionString);
                    inherited.Add(variable);
                }
            }

            if (inherited.Count > 0)
            {
                Log.Information(
                    "{UnifiedVariable} is set; {Count} store domains inherit it: {Variables}",
                    UnifiedVariable, inherited.Count, inherited);
            }

            return inherited;
        }
    }

    /// <summary>
    /// Converts a <c>postgres://</c> / <c>postgresql://</c> URL into Npgsql keyword form
    /// (<c>Host=...;Port=...;Database=...</c>). Values already in keyword form pass through
    /// unchanged. Query parameters are forwarded as keywords; <c>sslmode</c> maps to
    /// <c>SSL Mode</c>.
    /// </summary>
    public static string NormalizeToConnectionString(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var builder = new StringBuilder();
        builder.Append("Host=").Append(uri.Host);
        builder.Append(";Port=").Append(uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port);

        var database = uri.AbsolutePath.Trim('/');
        if (database.Length > 0)
            builder.Append(";Database=").Append(Uri.UnescapeDataString(database));

        if (uri.UserInfo.Length > 0)
        {
            var separatorIndex = uri.UserInfo.IndexOf(':');
            var username = separatorIndex >= 0 ? uri.UserInfo[..separatorIndex] : uri.UserInfo;
            builder.Append(";Username=").Append(Uri.UnescapeDataString(username));
            if (separatorIndex >= 0)
                builder.Append(";Password=").Append(Uri.UnescapeDataString(uri.UserInfo[(separatorIndex + 1)..]));
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = Uri.UnescapeDataString(pair[..eq]);
            var parameterValue = Uri.UnescapeDataString(pair[(eq + 1)..]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                key = "SSL Mode";
            builder.Append(';').Append(key).Append('=').Append(parameterValue);
        }

        return builder.ToString();
    }
}
