using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    /// <summary>
    /// Stamps the retained Drafted authority onto the journal the atomic store will append: the
    /// draft and result fingerprints, the lot-mutation fingerprint and each lot instruction's own
    /// journal fingerprint tag, then restates every line in the projection's functional currency
    /// at a rate of one, refusing a generated line in any other currency.
    /// </summary>
    private static LedgerJournalEntryWrite BindAssetPostingIntentToJournal(
        LedgerJournalEntryWrite write,
        AssetPostingAuthoritySnapshot authority)
    {
        var projection = authority.Drafted.Projection;
        var tags = write.Entry.Metadata.Tags is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(write.Entry.Metadata.Tags, StringComparer.OrdinalIgnoreCase);
        tags["assetAccountingDraftFingerprint"] = RequireText(
            projection.DraftedCandidateFingerprint,
            nameof(projection.DraftedCandidateFingerprint));
        tags["assetAccountingDraftResultFingerprint"] = RequireText(
            projection.DraftedCandidateResultFingerprint,
            nameof(projection.DraftedCandidateResultFingerprint));
        if (!string.IsNullOrWhiteSpace(projection.DraftedLotMutationFingerprint))
            tags["assetAccountingLotMutationFingerprint"] = projection.DraftedLotMutationFingerprint;
        if (projection.DraftedLotMutation?.CorporateAction is { } successorInstruction)
            tags[OpenLotSuccessors.JournalFingerprintTag] = OpenLotSuccessors.Fingerprint(successorInstruction);
        if (projection.DraftedLotMutation?.CapitalReturn is { } capitalReturnInstruction)
            tags[OpenLotCapitalReturn.JournalFingerprintTag] = OpenLotCapitalReturn.Fingerprint(capitalReturnInstruction);
        if (projection.DraftedLotMutation?.FactorPaydown is { } factorPaydownInstruction)
            tags[OpenLotFactorPaydown.JournalFingerprintTag] = OpenLotFactorPaydown.Fingerprint(factorPaydownInstruction);

        var generatedLines = projection.DraftedCandidateResult?.GeneratedPostingLines
            ?? throw new InvalidOperationException("The retained Drafted candidate is missing generated posting lines.");
        RequireAssetAssertion(generatedLines.Count == write.Entry.Lines.Count,
            "The retained Drafted candidate line count does not match the journal write.");
        var lines = write.Entry.Lines.Select((line, index) =>
        {
            var generated = generatedLines[index];
            RequireAssetAssertion(string.Equals(generated.Currency, projection.Currency, StringComparison.OrdinalIgnoreCase),
                "Foreign-currency generated asset lines require explicit FX authority and cannot be posted as functional currency.");
            return new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                line.Dimensions,
                new LedgerEntryCurrency(
                    projection.Currency,
                    projection.Currency,
                    line.Debit,
                    line.Credit,
                    1m));
        }).ToArray();
        var entry = new JournalEntry(
            write.Entry.JournalEntryId,
            write.Entry.Timestamp,
            write.Entry.Description,
            lines,
            write.Entry.Metadata with { Tags = tags });
        return write with { Entry = entry };
    }
}
