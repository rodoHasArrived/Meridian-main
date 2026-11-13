using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    /// <summary>Retains the existing journal-command lock through the reviewed replacement receipt.</summary>
    public async Task WithIntercompanyDraftLinkAuthorityAsync(string fund, string tenant, string? company,
        Guid bookId, Guid journalId, Guid proposalId, string sourceDigest,
        bool requireExisting, Func<RejectedIntercompanyDraftEvidenceDto?, Task> retainLink, CancellationToken ct = default)
    {
        await using var session = await _mutationRecovery.OpenSessionAsync(ct).ConfigureAwait(false);
        var pending = await session.ListPendingAsync(ct).ConfigureAwait(false);
        if (pending.Any(intent => PendingMatchesScope(intent, RecoveryScope(fund, tenant, company), journalId)))
            throw new InvalidOperationException("Resolve the retained journal command before replacing its rejected counterpart.");
        var draft = await _draftStore.GetAsync(fund, journalId, ct, tenant, company).ConfigureAwait(false);
        if (draft is null)
        {
            if (requireExisting)
                throw new InvalidOperationException("The original delivered counterpart draft is unavailable.");
            await retainLink(null).ConfigureAwait(false);
            return;
        }
        if (draft.Status != ManualJournalEntryStatusDto.Rejected || draft.LedgerBookId != bookId
            || draft.IntercompanyProposalId != proposalId || draft.IntercompanyProposalSourceDigest != sourceDigest)
            throw new InvalidOperationException("Only the exact rejected, source-bound delivered draft can be resolved by a separate manual counterpart.");
        await retainLink(new(bookId, journalId, draft.Version, proposalId, sourceDigest)).ConfigureAwait(false);
    }
}
