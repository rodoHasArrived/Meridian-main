using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private async Task<AtomicTaxLotJournalCommand> BindRetainedReferenceVersionsAsync(
        AtomicTaxLotJournalCommand command, AssetAccountingCandidateAuthorityContext authority,
        bool hasExistingJournal, CancellationToken ct)
    {
        if (command.MutationKind is not (AtomicTaxLotMutationKind.Acquisition or AtomicTaxLotMutationKind.Disposal))
            return command;
        command = (command with { ReferenceVersions = new(authority.SecurityVersion, authority.ExpectedBookPositionVersion) })
            .WithComputedFingerprint();
        if (!hasExistingJournal)
            return command;

        AtomicTaxLotJournalResult? retained;
        try
        {
            retained = await _journalStore!.GetAtomicTaxLotPostingAsync(command.MutationBatchId, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Separate journal/atomic stores need not expose this optional read. Keep the
            // approved versions and let the atomic writer validate the complete command.
            // Unavailable receipt evidence never authorizes a historical fingerprint downgrade.
            return command;
        }
        // Only an exact immutable pre-cutover receipt can use the historical omitted field.
        // New receipts bind both approved versions; neither a changed payload nor a changed
        // version may downgrade that identity to escape the storage collision check.
        var historical = (command with { ReferenceVersions = null }).WithComputedFingerprint();
        return retained?.CanonicalFingerprint == historical.CanonicalFingerprint ? historical : command;
    }
}
