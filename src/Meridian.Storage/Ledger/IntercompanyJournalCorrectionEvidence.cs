using Meridian.Ledger;
using static Meridian.Storage.Ledger.LedgerRetainedValueComparison;

namespace Meridian.Storage.Ledger;

/// <summary>Checks the retained economics of a bounded intercompany reversal.</summary>
public static class IntercompanyJournalCorrectionEvidence
{
    private static readonly IReadOnlyDictionary<string, string> NoExternalDimensions =
        new Dictionary<string, string>();

    public static bool IsExactInverse(JournalEntry inverse, JournalEntry original)
    {
        if (inverse.Lines.Count != original.Lines.Count || !SameJournalScope(inverse.Metadata, original.Metadata))
            return false;
        var unmatched = original.Lines.ToList();
        foreach (var line in inverse.Lines)
        {
            var index = unmatched.FindIndex(source => SameLineScope(line, source)
                && AmountsMatch(source.Debit, line.Credit) && AmountsMatch(source.Credit, line.Debit)
                && InverseCurrency(line, source));
            if (index < 0)
                return false;
            unmatched.RemoveAt(index);
        }
        return unmatched.Count == 0;
    }

    private static bool SameLineScope(LedgerEntry inverse, LedgerEntry original)
    {
        var left = inverse.Dimensions ?? new LedgerLineDimensionSet();
        var right = original.Dimensions ?? new LedgerLineDimensionSet();
        return inverse.Account == original.Account
            && (left with { ExternalGlDimensions = NoExternalDimensions })
                == (right with { ExternalGlDimensions = NoExternalDimensions })
            && ExternalDimensionsMatch(left.ExternalGlDimensions, right.ExternalGlDimensions);
    }

    private static bool InverseCurrency(LedgerEntry inverse, LedgerEntry original)
    {
        if (original.Currency is not { } source)
            return inverse.Currency is null;
        var reversed = new LedgerEntryCurrency(source.TransactionCurrency, source.FunctionalCurrency,
            source.TransactionCredit, source.TransactionDebit, source.FxRateToFunctional);
        return CurrencyMatches(reversed, inverse.Currency, inverse.Debit, inverse.Credit);
    }

    private static bool SameJournalScope(JournalEntryMetadata inverse, JournalEntryMetadata original)
    {
        var left = inverse.Normalize();
        var right = original.Normalize();
        return (left.Symbol, left.SecurityId, left.ProjectId, left.LedgerBook, left.LedgerView,
                left.ScenarioId, left.StrategyId, left.FinancialAccountId, left.CounterpartyAccountId,
                left.Institution, left.CapitalAccountId, left.InvestorId, left.PaymentIntentId, left.SettlementReference)
            == (right.Symbol, right.SecurityId, right.ProjectId, right.LedgerBook, right.LedgerView,
                right.ScenarioId, right.StrategyId, right.FinancialAccountId, right.CounterpartyAccountId,
                right.Institution, right.CapitalAccountId, right.InvestorId, right.PaymentIntentId, right.SettlementReference);
    }
}
