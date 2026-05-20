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

        ValidateAdjustmentApprovalMetadata(entry);

        if (string.Equals(period.Status, "Open", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(period.Status, "SoftClosed", StringComparison.Ordinal))
        {
            if (entry.PostingKind == LedgerPostingKindDto.Adjustment)
            {
                if (entry.AdjustmentApproval is null)
                {
                    throw new LedgerValidationException(
                        $"Accounting period '{period.Label}' is soft-closed; Adjustment postings require approved governance metadata.");
                }

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

    private static void ValidateAdjustmentApprovalMetadata(LedgerJournalEntryWrite entry)
    {
        if (entry.PostingKind != LedgerPostingKindDto.Adjustment)
        {
            if (entry.AdjustmentApproval is not null)
            {
                throw new LedgerValidationException(
                    $"Journal entry '{entry.Entry.JournalEntryId}' has adjustment approval metadata but is not an Adjustment posting.");
            }

            return;
        }

        var approval = entry.AdjustmentApproval;
        if (approval is null)
        {
            return;
        }

        if (approval.Status != LedgerAdjustmentApprovalStatusDto.Approved)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' adjustment approval '{approval.ApprovalId}' must be Approved.");
        }

        RequireText(approval.ApprovalId, entry.Entry.JournalEntryId, "approval id");
        RequireText(approval.ApprovedBy, entry.Entry.JournalEntryId, "approved by");
        RequireText(approval.ReasonCode, entry.Entry.JournalEntryId, "reason code");

        if (approval.ApprovedAt == default)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' adjustment approval requires an approved-at timestamp.");
        }

        if (string.IsNullOrWhiteSpace(approval.GovernanceCaseId) &&
            string.IsNullOrWhiteSpace(approval.EvidenceLink))
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' adjustment approval requires a governance case id or evidence link.");
        }
    }

    private static void RequireText(string? value, Guid journalEntryId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntryId}' adjustment approval requires {fieldName}.");
        }
    }
}
