using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Rules for correcting posted short openings and covers without editing them: a reversal is the exact
/// inverse of the original journal and restores the lot as it was; a rebook follows a retained reversal
/// and is itself an ordinary opening or cover derived from corrected inputs; short-cover reporting needs
/// an explicit, reviewed tax-character attestation and never infers it from opening-lot age.
/// </summary>
public static class ShortCorrectionGuard
{
    public static void RequireReversal(ShortCorrectionReceipt receipt, JournalEntry original, JournalEntry reversal)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reversal);
        Require(receipt);
        if (receipt.Kind != ShortCorrectionKind.Reversal)
            throw new ArgumentException("The receipt is not a reversal.");
        if (receipt.CorrectsJournalEntryId != original.JournalEntryId || reversal.JournalEntryId == original.JournalEntryId)
            throw new ArgumentException("A reversal names the exact posted journal it inverts and is a new journal.");
        // The reversal is posted later (its own timestamp) but reverses in the original's economic
        // period: the effective date is retained, so a reversal cannot move the entry between periods.
        if (reversal.Metadata.EffectiveDate != original.Metadata.EffectiveDate || reversal.Timestamp < original.Timestamp)
            throw new ArgumentException("A short reversal must be the exact inverse of the posted journal; partial, re-priced or re-dated reversals are refused.");
        if (!IsExactInverse(original.Lines, reversal.Lines))
            throw new ArgumentException("A short reversal must be the exact inverse of the posted journal; partial, re-priced or re-dated reversals are refused.");
    }

    /// <summary>
    /// One-to-one inverse: each posted line has exactly one reversal line on the same account with the same
    /// dimensions, the functional and transaction debit/credit swapped, and identical currency codes and FX,
    /// so scoped and transaction-currency balances are reversed, not just the functional totals.
    /// </summary>
    internal static bool IsExactInverse(IReadOnlyList<LedgerEntry> posted, IReadOnlyList<LedgerEntry> reversal)
    {
        if (posted.Count == 0 || posted.Count != reversal.Count)
            return false;
        return posted.All(line => reversal.Count(candidate => candidate.Account == line.Account
            && candidate.Debit == line.Credit && candidate.Credit == line.Debit
            && SameDimensions(candidate.Dimensions, line.Dimensions)
            && InverseCurrency(candidate.Currency, line.Currency)) == 1);
    }

    private static bool SameDimensions(LedgerLineDimensionSet? left, LedgerLineDimensionSet? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (left.ExternalGlDimensions.Count != right.ExternalGlDimensions.Count
            || left.ExternalGlDimensions.Any(pair => !right.ExternalGlDimensions.TryGetValue(pair.Key, out var value) || value != pair.Value))
            return false;
        var shared = new Dictionary<string, string>();
        return (left with { ExternalGlDimensions = shared }) == (right with { ExternalGlDimensions = shared });
    }

    private static bool InverseCurrency(LedgerEntryCurrency? candidate, LedgerEntryCurrency? original)
    {
        if (candidate is null || original is null)
            return candidate is null && original is null;
        return candidate.TransactionCurrency == original.TransactionCurrency && candidate.FunctionalCurrency == original.FunctionalCurrency
            && candidate.FxRateToFunctional == original.FxRateToFunctional
            && candidate.TransactionDebit == original.TransactionCredit && candidate.TransactionCredit == original.TransactionDebit;
    }

    public static void RequireRebook(ShortCorrectionReceipt receipt, ShortCorrectionReceipt reversal,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> projected, JournalEntry rebook)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(reversal);
        ArgumentNullException.ThrowIfNull(projected);
        ArgumentNullException.ThrowIfNull(rebook);
        Require(receipt);
        Require(reversal);
        if (receipt.Kind != ShortCorrectionKind.Rebook || reversal.Kind != ShortCorrectionKind.Reversal)
            throw new ArgumentException("A rebook follows a reversal; the receipts are of the wrong kinds.");
        if (receipt.FollowsReversalId != reversal.CorrectionId || receipt.CorrectsMutationBatchId != reversal.CorrectsMutationBatchId
            || receipt.CorrectsJournalEntryId != reversal.CorrectsJournalEntryId)
            throw new ArgumentException("A rebook must follow the reversal of the same corrected mutation and journal.");
        if (receipt.CorrectionId == reversal.CorrectionId || rebook.JournalEntryId == receipt.CorrectsJournalEntryId)
            throw new ArgumentException("A rebook is a fresh correction and a new journal; it cannot reuse the reversal's identity or the corrected journal.");
        if (!OpenLotAmortization.SameLot(receipt.RestoresLot, reversal.RestoresLot))
            throw new ArgumentException("A rebook addresses the lot its reversal restored, with the complete restored snapshot unchanged.");
        if (!ShortLotJournalProjector.MatchesExactly(projected, rebook.Lines))
            throw new ArgumentException("A rebook must carry exactly the lines projected from its corrected inputs.");
    }

    /// <summary>Reporting a covered short needs the explicit attestation; absence keeps the existing refusal.</summary>
    public static ShortSaleCharacterAttestation RequireCharacter(ShortSaleCharacterAttestation? attestation)
    {
        if (attestation is null)
            throw new ArgumentException("Short-cover reporting requires retained cover economics and tax-character evidence; opening-lot age cannot establish short-sale tax character.");
        var issues = attestation.Validate();
        if (issues.Count > 0)
            throw new ArgumentException("Short-sale tax character attestation is incomplete: " + string.Join(" ", issues));
        return attestation;
    }

    private static void Require(ShortCorrectionReceipt receipt)
    {
        var issues = receipt.Validate();
        if (issues.Count > 0)
            throw new ArgumentException("Short correction receipt is invalid: " + string.Join(" ", issues));
    }
}
