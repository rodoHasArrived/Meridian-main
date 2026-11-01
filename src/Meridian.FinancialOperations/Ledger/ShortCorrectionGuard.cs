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
        var expected = ShortLotJournalProjector.Reverse(original.Lines);
        if (!ShortLotJournalProjector.MatchesExactly(expected, reversal.Lines))
            throw new ArgumentException("A short reversal must be the exact inverse of the posted journal; partial, re-priced or re-dated reversals are refused.");
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
        if (receipt.FollowsReversalId != reversal.CorrectionId || receipt.CorrectsMutationBatchId != reversal.CorrectsMutationBatchId)
            throw new ArgumentException("A rebook must follow the reversal of the same corrected mutation.");
        if (receipt.RestoresLot.TaxLotRecordId != reversal.RestoresLot.TaxLotRecordId)
            throw new ArgumentException("A rebook addresses the lot its reversal restored.");
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
