using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>One projected currency-aware short cover line; functional amounts plus the transaction leg.</summary>
public sealed record ShortCoverLine(LedgerAccount Account, decimal Debit, decimal Credit, string TransactionCurrency,
    decimal TransactionDebit, decimal TransactionCredit, decimal FxRateToFunctional);

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

    /// <summary>
    /// Cover in the transaction currency with execution fees. Cash is credited in the transaction
    /// currency at the cover FX; the liability relief stays at the retained acquisition FX; the book
    /// result is measured at acquisition FX and the FX movement on the cover cost is a separate realized
    /// FX result, so the two results add up to proceeds relieved less functional cash paid. Each amount is
    /// rounded to its currency's minor units once; the functional cash credit is the sum of the rounded
    /// cost and fee legs so the journal always balances with no residue line.
    /// </summary>
    public static IReadOnlyList<ShortCoverLine> CoverInCurrency(LedgerAccount carryingAccount, ShortCoverTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        Validate(carryingAccount, terms.RelievedProceedsFunctional);
        if (terms.Refusal() is { } refusal)
            throw new ArgumentException(refusal);
        var tx = terms.TransactionCurrency.Trim().ToUpperInvariant();
        var fn = terms.FunctionalCurrency.Trim().ToUpperInvariant();
        var financialAccountId = carryingAccount.FinancialAccountId;
        var coverCostTx = ShortCoverTerms.RoundMinor(terms.CoveredQuantity * terms.CoverPrice, tx);
        var feesTx = ShortCoverTerms.RoundMinor(terms.Fees, tx);
        var coverCostFn = ShortCoverTerms.RoundMinor(coverCostTx * terms.CoverFxRateToFunctional, fn);
        var costAtAcquisitionFn = ShortCoverTerms.RoundMinor(coverCostTx * terms.AcquisitionFxRateToFunctional, fn);
        var feesFn = ShortCoverTerms.RoundMinor(feesTx * terms.CoverFxRateToFunctional, fn);
        var bookResult = terms.RelievedProceedsFunctional - costAtAcquisitionFn;
        var fxResult = costAtAcquisitionFn - coverCostFn;
        var cash = terms.IsFunctionalCurrency ? LedgerAccounts.CashFor(financialAccountId) : LedgerAccounts.CashInCurrency(tx, financialAccountId);
        var lines = new List<ShortCoverLine>
        {
            new(carryingAccount, terms.RelievedProceedsFunctional, 0m, fn, terms.RelievedProceedsFunctional, 0m, 1m)
        };
        var cashTx = coverCostTx + feesTx;
        var cashFn = coverCostFn + feesFn;
        if (cashFn > 0m || cashTx > 0m)
            lines.Add(new(cash, 0m, cashFn, tx, 0m, cashTx, terms.CoverFxRateToFunctional));
        if (feesFn > 0m)
            lines.Add(new(LedgerAccounts.CommissionExpenseOrDefault(financialAccountId), feesFn, 0m, tx, feesTx, 0m, terms.CoverFxRateToFunctional));
        if (bookResult > 0m)
            lines.Add(new(LedgerAccounts.RealizedGainOrDefault(financialAccountId), 0m, bookResult, fn, 0m, bookResult, 1m));
        else if (bookResult < 0m)
            lines.Add(new(LedgerAccounts.RealizedLossOrDefault(financialAccountId), -bookResult, 0m, fn, -bookResult, 0m, 1m));
        if (fxResult > 0m)
            lines.Add(new(LedgerAccounts.RealizedFxGainOrDefault(financialAccountId), 0m, fxResult, fn, 0m, fxResult, 1m));
        else if (fxResult < 0m)
            lines.Add(new(LedgerAccounts.RealizedFxLossOrDefault(financialAccountId), -fxResult, 0m, fn, -fxResult, 0m, 1m));
        return lines;
    }

    /// <summary>
    /// True when the journal carries exactly the projected currency-aware lines: same count, and each
    /// expected account, functional debit/credit, transaction currency, transaction amounts and FX rate
    /// present exactly once.
    /// </summary>
    public static bool MatchesExactly(IReadOnlyList<ShortCoverLine> expected, IReadOnlyList<LedgerEntry> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return actual.Count == expected.Count && expected.All(line => actual.Count(item =>
            item.Account == line.Account && item.Debit == line.Debit && item.Credit == line.Credit
            && item.Currency is { } currency && currency.TransactionCurrency == line.TransactionCurrency
            && currency.TransactionDebit == line.TransactionDebit && currency.TransactionCredit == line.TransactionCredit
            && currency.FxRateToFunctional == line.FxRateToFunctional) == 1);
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
