using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Selects a statement closing balance from retained source authority, never receipt order or row IDs.</summary>
public static class BankStatementClosingBalanceAuthority
{
    /// <summary>Selects statement evidence by its retained header date, with a fallback for legacy rows without headers.</summary>
    public static IEnumerable<BankStatementLineDto> ForStatementDate(IEnumerable<BankStatementLineDto> lines, DateOnly asOfDate) =>
        lines.Where(line => (line.StatementAsOfDate ?? line.TransactionDate) == asOfDate);

    /// <summary>
    /// The latest statement date and highest declared revision supersede earlier statements; transaction-feed
    /// deltas never supply or displace a balance. Within a statement, source line sequence selects the final
    /// running balance, including backdated entries.
    /// Conflicting evidence without sufficient authority remains unavailable.
    /// </summary>
    public static decimal? SelectClosingBalance(IEnumerable<BankStatementLineDto> lines, string? currency = null, DateOnly? throughValueDate = null)
    {
        // Balance authority comes from complete statements only. Transaction-feed deliveries carry no
        // running balance, so a later feed date must not displace the latest statement's balance.
        var selected = SelectLatestStatement(lines.Where(static line => line.IsTransactionFeed != true));
        return selected is null ? null : SelectWithinStatement(selected
            .Where(line => currency is null || string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .Where(line => throughValueDate is null || line.ValueDate <= throughValueDate));
    }

    /// <summary>
    /// Projects one complete, highest-revision batch per account/header date; competing statement batches
    /// remain unavailable. Transaction-feed deltas are additive and apply only to dates without a statement.
    /// </summary>
    public static IReadOnlyList<BankStatementLineDto>? SelectAuthoritativeLines(
        IEnumerable<BankStatementLineDto> lines, string? currency = null, DateOnly? fromValueDate = null, DateOnly? throughValueDate = null)
    {
        var selected = new List<BankStatementLineDto>();
        foreach (var group in lines.GroupBy(static line => (line.AccountId, line.StatementDate)))
        {
            var statement = group.Where(static line => line.IsTransactionFeed != true).ToArray();
            if (statement.Length == 0)
            {
                // Feed deliveries are deltas, and each transaction is retained once by its line identity.
                selected.AddRange(group);
                continue;
            }
            var revision = statement.Max(static line => line.StatementRevision ?? 0);
            var candidates = statement.Where(line => (line.StatementRevision ?? 0) == revision).ToArray();
            if (!candidates.Any(line => (currency is null || string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase))
                && (fromValueDate is null || line.ValueDate >= fromValueDate)
                && (throughValueDate is null || line.ValueDate <= throughValueDate)))
                continue;
            if (candidates.Select(static line => line.BatchId).Distinct().Count() != 1)
                return null;
            selected.AddRange(candidates);
        }
        return selected;
    }

    /// <summary>The latest retained header date, including when competing batches prevent selecting a balance.</summary>
    public static DateOnly? LatestStatementDate(IEnumerable<BankStatementLineDto> lines) =>
        lines.Select(static line => (DateOnly?)line.StatementDate).Max();

    /// <summary>The final source row from the authoritative latest statement, or unavailable when its order is ambiguous.</summary>
    public static BankStatementLineDto? SelectLatestLine(IEnumerable<BankStatementLineDto> lines, string? currency = null)
    {
        var statement = SelectLatestStatement(lines);
        if (statement is null)
            return null;
        var candidates = statement.Where(line => currency is null || string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            return null;
        if (candidates.All(static line => line.LineSequence.HasValue))
        {
            var sequence = candidates.Max(static line => line.LineSequence);
            candidates = candidates.Where(line => line.LineSequence == sequence).ToArray();
        }
        else
        {
            var valueDate = candidates.Max(static line => line.ValueDate);
            candidates = candidates.Where(line => line.ValueDate == valueDate).ToArray();
        }
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static IReadOnlyList<BankStatementLineDto>? SelectLatestStatement(IEnumerable<BankStatementLineDto> lines)
    {
        var candidates = lines.ToArray();
        var statementDate = LatestStatementDate(candidates);
        return statementDate is null ? [] : SelectAuthoritativeLines(candidates.Where(line => line.StatementDate == statementDate));
    }

    private static decimal? SelectWithinStatement(IEnumerable<BankStatementLineDto> lines)
    {
        var candidates = lines.Where(static line => line.ClosingBalance.HasValue).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        // Running balances follow the statement's order even when the last row has an earlier value date.
        if (candidates.All(static line => line.LineSequence.HasValue))
        {
            var sequence = candidates.Max(static line => line.LineSequence!.Value);
            candidates = candidates.Where(line => line.LineSequence == sequence).ToArray();
        }
        else
        {
            // Historical rows without an ordinal can only establish an unambiguous dated balance.
            var valueDate = candidates.Max(static line => line.ValueDate);
            candidates = candidates.Where(line => line.ValueDate == valueDate).ToArray();
        }

        var balances = candidates.Select(static line => line.ClosingBalance!.Value).Distinct().ToArray();
        return balances.Length == 1 ? balances[0] : null;
    }
}
