using Meridian.Storage;

namespace Meridian.Application.Composition;

/// <summary>
/// Snapshot of which money-path store domains have database persistence configured.
/// </summary>
/// <param name="Mode">"none", "partial", or "configured".</param>
/// <param name="ConfiguredDomains">Domains backed by a database connection.</param>
/// <param name="MissingDomains">Domains that will run in-memory (or unregistered) and lose data on restart.</param>
public sealed record PersistenceStatusSnapshot(
    string Mode,
    IReadOnlyList<string> ConfiguredDomains,
    IReadOnlyList<string> MissingDomains)
{
    public const string None = "none";
    public const string Partial = "partial";
    public const string Configured = "configured";
}

/// <summary>
/// Evaluates persistence configuration across every money-path store domain, resolving the same
/// environment variables (including the unified <c>MERIDIAN_DATABASE_URL</c> propagation and the
/// Direct Lending / Reporting inheritance chains) as the composition switchboards. Used by
/// status endpoints, readiness checks, and startup logging so operators see a loud
/// "PERSISTENCE: NONE" signal instead of silently losing journal entries on restart.
/// </summary>
public static class PersistenceConfigurationStatus
{
    public static PersistenceStatusSnapshot Evaluate()
    {
        MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        var configured = new List<string>();
        var missing = new List<string>();

        void Classify(string domain, bool isConfigured)
            => (isConfigured ? configured : missing).Add(domain);

        Classify("ledger", LedgerStartup.IsConfigured());
        Classify("security-master", SecurityMasterStartup.IsConfigured());
        Classify("fund-accounts", FundAccountsStartup.IsConfigured());
        Classify("fund-structure", FundStructureStartup.IsConfigured());
        Classify("direct-lending", DirectLendingStartup.IsConfigured());
        Classify("asset-operations", AssetOperationsStartup.IsConfigured());
        Classify("banking", BankingStartup.IsConfigured());
        Classify("money-market", MoneyMarketStartup.IsConfigured());
        Classify("reporting", HasValue("MERIDIAN_REPORTING_CONNECTION_STRING") || LedgerStartup.IsConfigured());
        Classify("scoped-access", HasValue("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING"));

        var mode = missing.Count == 0
            ? PersistenceStatusSnapshot.Configured
            : configured.Count == 0
                ? PersistenceStatusSnapshot.None
                : PersistenceStatusSnapshot.Partial;

        return new PersistenceStatusSnapshot(mode, configured, missing);
    }

    private static bool HasValue(string variable)
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
}
