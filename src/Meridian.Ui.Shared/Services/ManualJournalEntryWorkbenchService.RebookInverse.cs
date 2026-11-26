using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

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
            var index = unmatched.FindIndex(candidate => IsExactInverse(original, candidate, source.Currency));
            if (index < 0)
                return "A rebook's inverse lines no longer reproduce the journal it replaces; edit only the replacement lines.";
            unmatched.RemoveAt(index);
        }

        return null;
    }

    /// <summary>
    /// Compares the posting economics of a candidate inverse line with the source line, side flipped.
    /// Labels (line id, description, evidence link) are free. A value the source left for
    /// normalization to derive (currency, entity, tax lot, dimensions) matches whatever was derived;
    /// a value the source stated explicitly must be carried unchanged.
    /// </summary>
    private static bool IsExactInverse(
        ManualJournalEntryLineDto original,
        ManualJournalEntryLineDto candidate,
        string? sourceCurrency)
    {
        var originalCurrency = string.IsNullOrWhiteSpace(original.Currency) ? sourceCurrency : original.Currency;
        return candidate.Side == ReverseSide(original.Side) &&
               candidate.Amount == original.Amount &&
               string.Equals(candidate.AccountPath?.Trim(), original.AccountPath?.Trim(), StringComparison.Ordinal) &&
               string.Equals(candidate.Currency?.Trim(), originalCurrency?.Trim(), StringComparison.OrdinalIgnoreCase) &&
               candidate.SecurityId == original.SecurityId &&
               candidate.TransactionCurrency == original.TransactionCurrency &&
               SameOptional(candidate.LedgerAccountSymbol, original.LedgerAccountSymbol) &&
               SameOptional(candidate.LedgerAccountFinancialAccountId, original.LedgerAccountFinancialAccountId) &&
               SameOptional(candidate.FundAllocationId, original.FundAllocationId) &&
               (NormalizeOptional(original.EntityId) is null || SameOptional(candidate.EntityId, original.EntityId)) &&
               (NormalizeOptional(original.TaxLotId) is null || SameOptional(candidate.TaxLotId, original.TaxLotId)) &&
               (original.Dimensions is null || SamePostingDimensions(original.Dimensions, candidate.Dimensions));
    }

    private static bool SameOptional(string? left, string? right)
        => string.Equals(NormalizeOptional(left), NormalizeOptional(right), StringComparison.Ordinal);
}
