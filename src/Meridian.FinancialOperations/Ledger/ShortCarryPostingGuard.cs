using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Proves a short carrying-charge journal against its reviewed instruction and the open short lot: Short
/// direction, lot identity, quantity within the open quantity, the lot's liability carrying account, and
/// exactly the projected expense and cash lines. Storage admits the journal; these are Ledger rules.
/// </summary>
public static class ShortCarryPostingGuard
{
    public static void RequireLot(ShortCarryInstruction instruction, OpenLotDto lot)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.TaxLotRecordId != instruction.TaxLotRecordId || !string.Equals(lot.LotId, instruction.LotId, StringComparison.Ordinal))
            throw new ArgumentException("The short carrying charge names a different lot than the one supplied.");
        if (lot.Acquisition.Direction != OpenLotDirection.Short || instruction.Direction != OpenLotDirection.Short)
            throw new ArgumentException("Borrow fees and payments in lieu apply to Short lots only.");
        if (lot.OpenQuantity <= 0m)
            throw new ArgumentException("The short lot is fully covered; no carrying charge can accrue.");
        if (instruction.Quantity > lot.OpenQuantity)
            throw new ArgumentException("The charged quantity exceeds the lot's open quantity.");
        if (!string.Equals(lot.Acquisition.FunctionalCurrency, instruction.FunctionalCurrency, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The instruction's functional currency does not match the lot's book.");
    }

    public static void RequireJournal(ShortCarryInstruction instruction, LedgerAccount carryingAccount, JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var expected = ShortCarryProjector.Lines(instruction, carryingAccount);
        if (!ShortLotJournalProjector.MatchesExactly(expected, entry.Lines))
            throw new ArgumentException("Short carrying-charge journal must carry exactly the expense debit and cash credit; extra, missing or inverted lines are refused.");
        if (entry.Lines.Any(line => line.Currency is { } currency
                && (currency.TransactionCurrency != instruction.Currency || currency.FunctionalCurrency != instruction.FunctionalCurrency || currency.FxRateToFunctional != 1m)))
            throw new ArgumentException("Short carrying-charge lines must be functional-currency lines at FX 1.");
    }
}
