using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Selects a statement closing balance from retained source authority, never receipt order or row IDs.</summary>
public static class BankStatementClosingBalanceAuthority
{
    /// <summary>Selects statement evidence by its retained header date, with a fallback for legacy rows without headers.</summary>
    public static IEnumerable<BankStatementLineDto> ForStatementDate(IEnumerable<BankStatementLineDto> lines, DateOnly asOfDate) =>
        lines.Where(line => (line.StatementAsOfDate ?? line.TransactionDate) == asOfDate);

    /// <summary>
    /// The latest statement date and highest declared revision supersede earlier statements.
    /// Within a statement, source line sequence selects the final running balance, including backdated entries.
    /// Conflicting evidence without sufficient authority remains unavailable.
    /// </summary>
    public static decimal? SelectClosingBalance(IEnumerable<BankStatementLineDto> lines)
    {
        var candidates = lines.ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var statementDate = candidates.Max(static line => line.StatementAsOfDate ?? line.ValueDate);
        candidates = candidates.Where(line => (line.StatementAsOfDate ?? line.ValueDate) == statementDate).ToArray();
        var revision = candidates.Max(static line => line.StatementRevision ?? 0);
        var balances = candidates.Where(line => (line.StatementRevision ?? 0) == revision)
            .GroupBy(static line => line.BatchId)
            .Select(static batch => SelectWithinStatement(batch))
            .Distinct()
            .ToArray();

        return balances.Length == 1 ? balances[0] : null;
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
