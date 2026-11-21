using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Proves a transaction-currency short cover journal against its reviewed terms and the relieved lots: the
/// terms must come from the lots' retained acquisition currency and FX, the relieved proceeds must equal
/// the lots' relieved functional basis, and the journal must carry exactly the projected currency-aware
/// lines. Storage admits the journal; these are Ledger rules.
/// </summary>
public static class ShortCoverPostingGuard
{
    public static void RequireLots(ShortCoverTerms terms, IReadOnlyList<OpenLotDto> lots, decimal relievedQuantity, decimal relievedFunctionalBasis)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(lots);
        if (lots.Count == 0)
            throw new ArgumentException("A short cover must relieve at least one open short lot.");
        if (lots.Any(lot => lot.Acquisition.Direction != OpenLotDirection.Short))
            throw new ArgumentException("Short covers relieve Short lots only; long and short lots never share a pool.");
        var first = lots[0].Acquisition;
        if (lots.Any(lot => lot.Acquisition.AcquisitionCurrency != first.AcquisitionCurrency || lot.Acquisition.FunctionalCurrency != first.FunctionalCurrency
                || lot.Acquisition.AcquisitionFxRateToFunctional != first.AcquisitionFxRateToFunctional))
            throw new ArgumentException("Relieved short lots must share one acquisition currency, functional currency and acquisition FX.");
        if (!string.Equals(terms.TransactionCurrency, first.AcquisitionCurrency, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(terms.FunctionalCurrency, first.FunctionalCurrency, StringComparison.OrdinalIgnoreCase)
            || terms.AcquisitionFxRateToFunctional != first.AcquisitionFxRateToFunctional)
            throw new ArgumentException("Cover terms must use the lots' retained acquisition currency, functional currency and acquisition FX.");
        if (terms.CoveredQuantity != relievedQuantity)
            throw new ArgumentException("Covered quantity must equal the relieved lot quantity.");
        if (terms.RelievedProceedsFunctional != relievedFunctionalBasis)
            throw new ArgumentException("Relieved opening proceeds must equal the relieved lots' functional basis.");
        if (lots.Sum(lot => lot.OpenQuantity) < relievedQuantity)
            throw new ArgumentException("Relieved quantity exceeds the open short quantity.");
    }

    public static void RequireJournal(ShortCoverTerms terms, LedgerAccount carryingAccount, JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var expected = ShortLotJournalProjector.CoverInCurrency(carryingAccount, terms);
        if (!ShortLotJournalProjector.MatchesExactly(expected, entry.Lines))
            throw new ArgumentException("Short cover journal must carry exactly the liability relief, transaction-currency cash, fee, book result and FX result lines; extra, missing or inverted lines are refused.");
    }
}
