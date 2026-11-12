using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<string?> ValidatePriorPeriodAdjustmentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        var authority = draft.PriorPeriodAdjustmentSource;
        if (authority is null)
            return draft.TreasuryContext?.IdempotencyKey?.StartsWith("prior-period|", StringComparison.Ordinal) == true
                ? "Prior-period correction requires server-retained source authority." : null;
        if (authority.SourceJournalEntryId != draft.ReversalOfJournalEntryId
            || authority.TargetPeriodId.ToString("D") != draft.PeriodId
            || authority.AccountingDate != draft.AccountingDate
            || authority.AccountingDate != draft.TreasuryContext?.EffectiveDate
            || string.IsNullOrWhiteSpace(authority.Reason) || string.IsNullOrWhiteSpace(authority.IntentId)
            || !SamePostingLines(authority.ExpectedLines.Select(line => line with { Description = null, EvidenceLink = null }).ToArray(),
                draft.Lines.Select(line => line with { Description = null, EvidenceLink = null }).ToArray()))
            return "Prior-period correction source, date, scope or inverse lines changed after preparation.";
        if (_journalStore is null || draft.LedgerBookId is not { } bookId)
            return "Authoritative ledger state is required for a prior-period correction.";
        var source = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: bookId, JournalEntryId: authority.SourceJournalEntryId), ct).ConfigureAwait(false);
        if (source.Count != 1 || source[0].PeriodId != authority.SourcePeriodId
            || (source[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(source[0].Entry.Timestamp.UtcDateTime)) != authority.OriginalEffectiveDate)
            return "The exact retained source journal is unavailable in the selected ledger book.";
        var sourcePeriod = await _journalStore.GetPeriodAsync(authority.SourcePeriodId, ct).ConfigureAwait(false);
        var target = await _journalStore.GetPeriodAsync(authority.TargetPeriodId, ct).ConfigureAwait(false);
        if (sourcePeriod is null || sourcePeriod.Status != "HardClosed" || sourcePeriod.LedgerBookId != bookId
            || target is null || target.LedgerBookId != bookId || target.Status != "Open"
            || draft.AccountingDate < target.StartDate || draft.AccountingDate > target.EndDate
            || target.StartDate <= sourcePeriod.EndDate)
            return "The source is no longer hard-closed or the selected later period is no longer open.";
        return null;
    }
}
