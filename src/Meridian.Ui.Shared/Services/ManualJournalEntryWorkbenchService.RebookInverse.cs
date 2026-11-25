using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private const string RebookInverseLinePrefix = "rebook-inverse-";

    /// <summary>
    /// A rebook replaces a posted journal that stays posted, so the rebook draft must carry that
    /// journal's exact inverse. Without it the original economics would be counted twice. The
    /// replacement lines remain editable; the inverse lines do not.
    /// </summary>
    /// <remarks>
    /// Prior-period, opening and consolidation rebooks carry their own source authority and are
    /// validated by their dedicated guards. A claimed source that is not a retained workbench draft
    /// has no posted workbench journal to double-count and is left to the remaining controls.
    /// </remarks>
    private async Task<string?> ValidateRebookInverseAsync(
        ManualJournalEntryDraftDto draft,
        string fundProfileId,
        IReadOnlyList<ManualJournalEntryLineDto> normalizedLines,
        CancellationToken ct)
    {
        if (draft.RebookedFromJournalEntryId is not { } sourceId || sourceId == draft.JournalEntryId ||
            draft.ReversalOfJournalEntryId is not null || draft.PriorPeriodAdjustmentSource is not null ||
            IsOpeningCorrectionDraft(draft) || IsConsolidationDraft(draft))
            return null;

        var source = await _draftStore.GetAsync(fundProfileId, sourceId, ct, draft.TenantId, draft.CompanyId)
            .ConfigureAwait(false);
        if (source is null)
            return null;

        var unmatched = normalizedLines
            .Where(line => line.LineId.StartsWith(RebookInverseLinePrefix, StringComparison.Ordinal))
            .ToList();
        if (unmatched.Count != source.Lines.Count)
            return "A rebook must retain the complete inverse of the journal it replaces; the original stays posted and would otherwise be counted twice.";

        foreach (var original in source.Lines)
        {
            var index = unmatched.FindIndex(candidate => IsExactInverse(original, candidate));
            if (index < 0)
                return "A rebook's inverse lines no longer reproduce the journal it replaces; edit only the replacement lines.";
            unmatched.RemoveAt(index);
        }

        return null;
    }

    private static bool IsExactInverse(ManualJournalEntryLineDto original, ManualJournalEntryLineDto candidate)
        => candidate.Side == ReverseSide(original.Side) &&
           candidate with
           {
               LineId = original.LineId,
               Side = original.Side,
               Description = original.Description,
               EvidenceLink = original.EvidenceLink,
               Dimensions = null
           } == original with { Dimensions = null } &&
           SamePostingDimensions(original.Dimensions, candidate.Dimensions);
}
