using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private static bool IsOpeningBalanceDraft(ManualJournalEntryDraftDto draft)
        => draft.TreasuryContext?.IdempotencyKey?.StartsWith("portfolio-opening:", StringComparison.Ordinal) == true;

    internal static void EnsureOpeningBalanceSaveAuthority(ManualJournalEntryDraftDto incoming,
        ManualJournalEntryDraftDto? retained, bool trustedAutomatedIntake)
    {
        if (retained is null || !IsOpeningBalanceDraft(retained))
        {
            if (IsOpeningBalanceDraft(incoming) && !trustedAutomatedIntake)
                throw new InvalidOperationException("Opening balances require the retained broker-import intake.");
            return;
        }
        // Source accounts and their chart paths are part of the reviewed economics. Replacing
        // a securities asset with an expense account is not a descriptive draft repair.
        if (incoming.LedgerBookId != retained.LedgerBookId || incoming.PeriodId != retained.PeriodId ||
            incoming.AccountingDate != retained.AccountingDate || incoming.Currency != retained.Currency ||
            incoming.EntryType != retained.EntryType || incoming.AccountingBasis != retained.AccountingBasis ||
            incoming.TreasuryContext != retained.TreasuryContext ||
            !SamePostingLines(retained.Lines, incoming.Lines) ||
            retained.EvidenceLinks.Except(incoming.EvidenceLinks, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Opening source economics and reservation identity are immutable; reject and use a governed correction.");
    }
}
