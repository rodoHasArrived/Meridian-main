namespace Meridian.Ledger;

/// <summary>Book accounting only: opening proceeds are a liability; a cover relieves that liability.</summary>
public static class ShortLotJournalProjector
{
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
            lines.Add((string.IsNullOrWhiteSpace(carryingAccount.FinancialAccountId)
                ? LedgerAccounts.RealizedGain : LedgerAccounts.RealizedGainFor(carryingAccount.FinancialAccountId), 0m, result));
        else if (result < 0m)
            lines.Add((string.IsNullOrWhiteSpace(carryingAccount.FinancialAccountId)
                ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedLossFor(carryingAccount.FinancialAccountId), -result, 0m));
        return lines;
    }

    private static LedgerAccount Cash(LedgerAccount account) => string.IsNullOrWhiteSpace(account.FinancialAccountId)
        ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(account.FinancialAccountId);

    private static void Validate(LedgerAccount account, decimal basis)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.AccountType != LedgerAccountType.Liability)
            throw new ArgumentException("Short lots require an explicit liability carrying account.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(basis);
    }
}
