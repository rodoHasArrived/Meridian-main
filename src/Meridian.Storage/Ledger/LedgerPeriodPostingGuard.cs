using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public static class LedgerPeriodPostingGuard
{
    public static void Validate(LedgerJournalEntryWrite entry, LedgerAccountingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Entry);
        ArgumentNullException.ThrowIfNull(period);

        var postingDate = DateOnly.FromDateTime(entry.Entry.Timestamp.UtcDateTime);
        if (postingDate < period.StartDate || postingDate > period.EndDate)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' posting date '{postingDate:yyyy-MM-dd}' is outside accounting period '{period.Label}' ({period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}).");
        }

        if (string.Equals(period.Status, "Open", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(period.Status, "SoftClosed", StringComparison.Ordinal))
        {
            if (entry.PostingKind == LedgerPostingKindDto.Adjustment)
            {
                return;
            }

            throw new LedgerValidationException(
                $"Accounting period '{period.Label}' is soft-closed; only Adjustment postings are accepted.");
        }

        if (string.Equals(period.Status, "HardClosed", StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                $"Accounting period '{period.Label}' is hard-closed; no postings are permitted.");
        }

        throw new LedgerValidationException(
            $"Accounting period '{period.Label}' has unsupported status '{period.Status}'.");
    }
}
