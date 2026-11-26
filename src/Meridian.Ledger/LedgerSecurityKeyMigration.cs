using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ledger;

/// <summary>One legacy ticker-keyed per-security account and its Security Master mapping.</summary>
/// <param name="LegacyAccount">The ticker-keyed account as it exists in retained journals.</param>
/// <param name="Balance">Net debit-minus-credit balance of <paramref name="LegacyAccount"/>.</param>
/// <param name="Resolution">Canonical resolution of the account's ticker at the report instant.</param>
/// <param name="TargetAccount">
/// The SecurityId-keyed account the balance belongs in, when the ticker resolved to exactly one security.
/// </param>
public sealed record LedgerSecurityKeyMappingRow(
    LedgerAccount LegacyAccount,
    decimal Balance,
    CanonicalSecurityResolution Resolution,
    LedgerAccount? TargetAccount)
{
    /// <summary><see langword="true"/> when the row has a target and a balance that must move.</summary>
    public bool RequiresReclass => TargetAccount is not null && Balance != 0m;
}

/// <summary>Old ticker keys mapped to SecurityId keys for one ledger, as of one instant.</summary>
public sealed record LedgerSecurityKeyMappingReport(
    DateTimeOffset AsOfUtc,
    IReadOnlyList<LedgerSecurityKeyMappingRow> Rows)
{
    /// <summary>Rows whose ticker resolved and whose balance should be reclassed.</summary>
    public IReadOnlyList<LedgerSecurityKeyMappingRow> ReclassCandidates =>
        Rows.Where(static row => row.RequiresReclass).ToArray();

    /// <summary>Rows that cannot be mapped automatically (ambiguous or unregistered tickers).</summary>
    public IReadOnlyList<LedgerSecurityKeyMappingRow> Unmapped =>
        Rows.Where(static row => row.TargetAccount is null).ToArray();
}

/// <summary>
/// A proposed reclass journal moving legacy ticker-keyed balances onto SecurityId-keyed accounts.
/// It is a proposal only: submit it through the governed adjustment posting path, which requires
/// approval before anything is posted. Retained journals are never rewritten.
/// </summary>
public sealed record LedgerSecurityKeyReclassProposal(
    DateTimeOffset AsOfUtc,
    string Description,
    IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> Lines,
    IReadOnlyList<LedgerSecurityKeyMappingRow> Sources);

/// <summary>
/// Migration support for the 2026-11-25 decision to key per-security ledger accounts by SecurityId
/// (Security Master architecture review F2). Existing journals keep their ticker keys; this type
/// reports how those keys map to SecurityIds and proposes a governed reclass when balances must move.
/// </summary>
public static class LedgerSecurityKeyMigration
{
    /// <summary>Returns the distinct legacy ticker-keyed per-security accounts in <paramref name="accounts"/>.</summary>
    public static IReadOnlyList<LedgerAccount> FindLegacyAccounts(IEnumerable<LedgerAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return accounts
            .Where(static account => account.SecurityId is null && account.Symbol is not null
                && LedgerAccounts.IsSecurityKeyedAccount(account))
            .Distinct()
            .OrderBy(static account => account.Name, StringComparer.Ordinal)
            .ThenBy(static account => account.Symbol, StringComparer.Ordinal)
            .ThenBy(static account => account.FinancialAccountId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Builds the old-ticker-key to SecurityId mapping report for <paramref name="ledger"/>, resolving
    /// each ticker once through the canonical (provider-less) resolver.
    /// </summary>
    public static Task<LedgerSecurityKeyMappingReport> BuildReportAsync(
        Ledger ledger,
        ISecurityResolver resolver,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var balances = ledger.Accounts.ToDictionary(static account => account, ledger.GetBalance);
        return BuildReportAsync(balances, resolver, asOfUtc, ct);
    }

    /// <inheritdoc cref="BuildReportAsync(Ledger, ISecurityResolver, DateTimeOffset, CancellationToken)"/>
    public static async Task<LedgerSecurityKeyMappingReport> BuildReportAsync(
        IReadOnlyDictionary<LedgerAccount, decimal> balances,
        ISecurityResolver resolver,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(resolver);

        var resolutions = new Dictionary<string, CanonicalSecurityResolution>(StringComparer.Ordinal);
        var rows = new List<LedgerSecurityKeyMappingRow>();
        foreach (var legacy in FindLegacyAccounts(balances.Keys))
        {
            var symbol = legacy.Symbol!;
            if (!resolutions.TryGetValue(symbol, out var resolution))
            {
                resolution = await resolver.ResolveCanonicalAsync(
                    new ResolveCanonicalSecurityRequest(SecurityIdentifierKind.Ticker, symbol, asOfUtc),
                    ct).ConfigureAwait(false);
                resolutions[symbol] = resolution;
            }

            var target = resolution.IsResolved
                ? legacy with { SecurityId = resolution.SecurityId }
                : null;
            rows.Add(new LedgerSecurityKeyMappingRow(legacy, balances.GetValueOrDefault(legacy), resolution, target));
        }

        return new LedgerSecurityKeyMappingReport(asOfUtc, rows);
    }

    /// <summary>
    /// Proposes a balanced reclass that zeroes each resolved legacy account and books its balance on
    /// the SecurityId-keyed account. Returns <see langword="null"/> when no balance must move.
    /// Ambiguous and unregistered tickers are excluded and stay listed in <see cref="LedgerSecurityKeyMappingReport.Unmapped"/>.
    /// </summary>
    public static LedgerSecurityKeyReclassProposal? ProposeReclass(LedgerSecurityKeyMappingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sources = report.ReclassCandidates;
        if (sources.Count == 0)
            return null;

        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>(sources.Count * 2);
        foreach (var row in sources)
        {
            var amount = Math.Abs(row.Balance);
            if (row.Balance > 0m)
            {
                lines.Add((row.TargetAccount!, amount, 0m));
                lines.Add((row.LegacyAccount, 0m, amount));
            }
            else
            {
                lines.Add((row.LegacyAccount, amount, 0m));
                lines.Add((row.TargetAccount!, 0m, amount));
            }
        }

        return new LedgerSecurityKeyReclassProposal(
            report.AsOfUtc,
            $"Reclass {sources.Count} ticker-keyed per-security balance(s) to SecurityId-keyed accounts (Security Master review F2)",
            lines,
            sources);
    }

    /// <summary>
    /// Returns <see langword="true"/> when a deterministically rebuilt line account matches a
    /// retained one. Retained journals keep their keys: a journal retained before SecurityId keying
    /// carries the ticker-keyed form of the same account, which is accepted as equivalent so replays
    /// verify instead of failing. Any other difference is a mismatch.
    /// </summary>
    public static bool IsRetainedEquivalent(LedgerAccount expected, LedgerAccount retained)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(retained);
        if (expected == retained)
            return true;

        return expected.SecurityId is not null
            && retained.SecurityId is null
            && expected.Symbol is not null
            && LedgerAccounts.IsSecurityKeyedAccount(retained)
            && expected with { SecurityId = null } == retained;
    }
}
