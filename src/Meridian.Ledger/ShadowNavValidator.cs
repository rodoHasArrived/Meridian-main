namespace Meridian.Ledger;

/// <summary>
/// Compares actual and shadow ledgers for independent NAV validation.
/// </summary>
public static class ShadowNavValidator
{
    public static ShadowNavValidationReport Validate(
        ProjectLedgerBook projectLedgerBook,
        LedgerBookKey actualLedgerKey,
        LedgerBookKey shadowLedgerKey,
        DateTimeOffset asOf,
        ShadowNavValidationPolicy policy,
        ChartOfAccounts? chart = null,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(projectLedgerBook);
        ArgumentNullException.ThrowIfNull(actualLedgerKey);
        ArgumentNullException.ThrowIfNull(shadowLedgerKey);
        ArgumentNullException.ThrowIfNull(policy);

        if (!projectLedgerBook.TryGetLedger(actualLedgerKey, out var actualLedger))
            throw new InvalidOperationException($"Actual ledger '{actualLedgerKey.LedgerBook}' was not found.");
        if (!projectLedgerBook.TryGetLedger(shadowLedgerKey, out var shadowLedger))
            throw new InvalidOperationException($"Shadow ledger '{shadowLedgerKey.LedgerBook}' was not found.");

        ArgumentNullException.ThrowIfNull(actualLedger);
        ArgumentNullException.ThrowIfNull(shadowLedger);

        var actualKey = actualLedgerKey.Normalize();
        var shadowKey = shadowLedgerKey.Normalize();
        var actualStatements = LedgerFinancialStatementBuilder.BuildAsOf(actualLedger, asOf, chart, financialAccountId);
        var shadowStatements = LedgerFinancialStatementBuilder.BuildAsOf(shadowLedger, asOf, chart, financialAccountId);
        var actualBalances = actualLedger.TrialBalanceAsOf(asOf, financialAccountId);
        var shadowBalances = shadowLedger.TrialBalanceAsOf(asOf, financialAccountId);
        var findings = BuildFindings(actualBalances, shadowBalances, policy.AccountTolerance);
        var actualNav = actualStatements.TotalAssets - actualStatements.TotalLiabilities;
        var shadowNav = shadowStatements.TotalAssets - shadowStatements.TotalLiabilities;

        return new ShadowNavValidationReport(
            actualKey,
            shadowKey,
            asOf,
            policy.PolicyId,
            policy.ReviewerGroup,
            policy.NavTolerance,
            policy.AccountTolerance,
            actualNav,
            shadowNav,
            actualNav - shadowNav,
            findings);
    }

    private static IReadOnlyList<ShadowNavValidationFinding> BuildFindings(
        IReadOnlyDictionary<LedgerAccount, decimal> actualBalances,
        IReadOnlyDictionary<LedgerAccount, decimal> shadowBalances,
        decimal accountTolerance)
    {
        return actualBalances.Keys
            .Concat(shadowBalances.Keys)
            .Distinct()
            .Select(account =>
            {
                actualBalances.TryGetValue(account, out var actualBalance);
                shadowBalances.TryGetValue(account, out var shadowBalance);
                var variance = actualBalance - shadowBalance;
                var absoluteVariance = Math.Abs(variance);
                return new ShadowNavValidationFinding(
                    account,
                    actualBalance,
                    shadowBalance,
                    variance,
                    absoluteVariance,
                    absoluteVariance > accountTolerance);
            })
            .Where(static finding => finding.AbsoluteVariance > 0m)
            .OrderByDescending(static finding => finding.AbsoluteVariance)
            .ThenBy(static finding => finding.Account.Name, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Account.Symbol, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Account.FinancialAccountId, StringComparer.Ordinal)
            .ToList();
    }
}
