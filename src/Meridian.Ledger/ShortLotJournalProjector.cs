using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Book accounting only: opening proceeds are a liability; a cover relieves that liability.</summary>
public static class ShortLotJournalProjector
{
    /// <summary>Functional-currency cost of covering <paramref name="coveredQuantity"/> at the explicit execution price, at ledger rounding.</summary>
    public static decimal CoverCost(decimal coveredQuantity, decimal coverPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coveredQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(coverPrice);
        return LedgerCurrencyRounding.RoundCurrency(coveredQuantity * coverPrice);
    }

    /// <summary>
    /// True when the journal carries exactly the projected lines: the same count, and each expected
    /// account/debit/credit triple present exactly once. Extra, missing or inverted lines fail.
    /// </summary>
    public static bool MatchesExactly(IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> expected,
        IReadOnlyList<LedgerEntry> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return actual.Count == expected.Count && expected.All(line =>
            actual.Count(item => item.Account == line.account && item.Debit == line.debit && item.Credit == line.credit) == 1);
    }

    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Open(
        LedgerAccount carryingAccount, decimal proceeds)
    {
        Validate(carryingAccount, proceeds);
        return [(Cash(carryingAccount), proceeds, 0m), (carryingAccount, 0m, proceeds)];
    }

    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Cover(
        LedgerAccount carryingAccount, decimal relievedProceeds, decimal coverCost)
    {
        Validate(carryingAccount, relievedProceeds);
        ArgumentOutOfRangeException.ThrowIfNegative(coverCost);
        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>
            { (carryingAccount, relievedProceeds, 0m) };
        if (coverCost > 0m)
            lines.Add((Cash(carryingAccount), 0m, coverCost));
        var result = relievedProceeds - coverCost;
        if (result > 0m)
            lines.Add((LedgerAccounts.RealizedGainOrDefault(carryingAccount.FinancialAccountId), 0m, result));
        else if (result < 0m)
            lines.Add((LedgerAccounts.RealizedLossOrDefault(carryingAccount.FinancialAccountId), -result, 0m));
        return lines;
    }

    /// <summary>The exact inverse of a posted short opening or cover journal: every line's sides swapped, nothing else.</summary>
    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Reverse(
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> posted)
    {
        ArgumentNullException.ThrowIfNull(posted);
        if (posted.Count == 0)
            throw new ArgumentException("A reversal needs the posted lines it inverts.");
        return posted.Select(line => (line.account, line.credit, line.debit)).ToArray();
    }

    /// <summary>The exact inverse of a posted journal, read back from its retained entries.</summary>
    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Reverse(IReadOnlyList<LedgerEntry> posted)
    {
        ArgumentNullException.ThrowIfNull(posted);
        return Reverse(posted.Select(line => (line.Account, line.Debit, line.Credit)).ToArray());
    }

    private static LedgerAccount Cash(LedgerAccount account) => LedgerAccounts.CashFor(account.FinancialAccountId);

    private static void Validate(LedgerAccount account, decimal basis)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!OpenLotCarryingAccounts.Matches(account, OpenLotDirection.Short))
            throw new ArgumentException("Short lots require an explicit liability carrying account.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(basis);
    }
}
