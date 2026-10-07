using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private readonly IConsolidationDraftGuard? _consolidationGuard;

    private static bool IsConsolidationDraft(ManualJournalEntryDraftDto draft)
        => draft.RequiresConsolidationEvidence || draft.ConsolidationEvidenceJson is not null ||
           draft.ConsolidationEvidenceDigest is not null ||
           draft.TreasuryContext?.IdempotencyKey?.Trim().StartsWith("consolidation:", StringComparison.OrdinalIgnoreCase) == true;

    private async Task ValidateCurrentConsolidationDraftAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (!IsConsolidationDraft(draft))
            return;
        if (string.IsNullOrWhiteSpace(draft.ConsolidationEvidenceJson) ||
            string.IsNullOrWhiteSpace(draft.ConsolidationEvidenceDigest))
            throw new InvalidOperationException("Consolidation source evidence is unavailable; rerun consolidation and obtain renewed review.");
        var guard = _consolidationGuard ?? throw new InvalidOperationException(
            "Authoritative consolidation source validation is unavailable; elimination approval and posting are blocked.");
        await ConsolidationChartValidation.ValidateAsync(_configurationService, draft, ct).ConfigureAwait(false);
        await guard.ValidateCurrentAsync(draft, ct).ConfigureAwait(false);
    }

    private async Task ValidateConsolidationMutationAsync(string operation, string fund, Guid journalEntryId,
        string? tenant, string? company, IReadOnlyList<ManualJournalMutationIntent> pending,
        ManualJournalMutationIntent? retained, CancellationToken ct)
    {
        var scope = RecoveryScope(fund, tenant, company);
        var draft = await _draftStore.GetAsync(NormalizeFundProfileId(fund), journalEntryId, ct, tenant, company).ConfigureAwait(false);
        if (draft is not null && IsConsolidationDraft(draft) &&
            operation is "lifecycle-Reverse" or "lifecycle-Rebook")
            throw new InvalidOperationException(
                "Consolidation corrections require a linked adjustment from current source balances; rerun consolidation and review the resulting draft.");

        // A completed posting is an immutable fact. Verify its exact receipt and finish any
        // draft/audit handoff even if balances have since changed; never re-post it.
        var committedReplay = retained is not null &&
            await VerifyCommittedPostingAsync(retained, requirePresent: false, ct).ConfigureAwait(false);
        if (!committedReplay && draft is not null &&
            operation is "submit" or "lifecycle-Submit" or "lifecycle-Approve" or "lifecycle-Post")
            await ValidateCurrentConsolidationDraftAsync(draft, ct).ConfigureAwait(false);

        foreach (var intent in pending.Where(item => PendingMatchesScope(item, scope, journalEntryId)))
        {
            var source = intent.Before.OfType<ManualJournalEntryDraftDto>()
                .Concat(intent.After).FirstOrDefault(IsConsolidationDraft);
            if (source is null || await VerifyCommittedPostingAsync(intent, requirePresent: false, ct).ConfigureAwait(false))
                continue;
            if (intent.Posting is not null || intent.After.Any(item => item.Status is
                    ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
                await ValidateCurrentConsolidationDraftAsync(source, ct).ConfigureAwait(false);
        }
    }
}
