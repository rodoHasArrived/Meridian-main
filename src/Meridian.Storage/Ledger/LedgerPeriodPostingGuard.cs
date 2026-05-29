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
        ValidateSecurityMasterLineage(entry);

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

    private static void ValidateSecurityMasterLineage(LedgerJournalEntryWrite entry)
    {
        var journalEntry = entry.Entry;
        var instrumentLines = journalEntry.Lines
            .Where(IsInstrumentBearingLine)
            .ToArray();
        var metadata = journalEntry.Metadata;
        var instrumentBearing = instrumentLines.Length > 0 ||
            !string.IsNullOrWhiteSpace(metadata.Symbol) ||
            metadata.SecurityId.GetValueOrDefault() != Guid.Empty;

        if (!instrumentBearing)
        {
            return;
        }

        var securityId = metadata.SecurityId.GetValueOrDefault();
        if (securityId == Guid.Empty)
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without a resolved Security Master security id.");
        }

        if (!TryGetMetadataTag(metadata, "securityMasterProvenance", out var provenance) ||
            !ReferencesSecurityId(provenance, securityId))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without Security Master provenance for security '{securityId}'.");
        }

        if (!TryGetMetadataTag(metadata, "securityMasterLineage", out var lineage) ||
            !ReferencesSecurityId(lineage, securityId))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without approved Security Master line lineage for security '{securityId}'.");
        }

        if (!HasApprovalEvidence(provenance, lineage))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without approved Security Master evidence.");
        }

        if (!HasActiveSecurityMasterStatus(lineage))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without active Security Master status evidence.");
        }

        if (!HasLedgerMappingEvidence(lineage))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without a Security Master ledger mapping reference.");
        }

        foreach (var line in instrumentLines)
        {
            var symbol = line.Account.Symbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new LedgerValidationException(
                    $"Journal entry '{journalEntry.JournalEntryId}' posts instrument line '{line.Account.Name}' without an instrument symbol for Security Master lineage.");
            }

            if (!lineage.Contains(symbol.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new LedgerValidationException(
                    $"Journal entry '{journalEntry.JournalEntryId}' posts instrument line '{symbol.Trim()}' without matching Security Master line lineage.");
            }
        }
    }

    private static bool IsInstrumentBearingLine(LedgerEntry line) =>
        !string.IsNullOrWhiteSpace(line.Account.Symbol) ||
        IsInstrumentAccountName(line.Account.Name);

    private static bool IsInstrumentAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return false;
        }

        var normalized = accountName.Trim();
        return normalized.Equals("Securities", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Dividend Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Accrued Interest Receivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Corporate Action Distribution", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Short Securities Payable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Asset", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Option Premium Liability", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Futures MTM Settlement", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetMetadataTag(JournalEntryMetadata metadata, string key, out string value)
    {
        value = string.Empty;
        if (metadata.Tags is null ||
            !metadata.Tags.TryGetValue(key, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool ReferencesSecurityId(string value, Guid securityId) =>
        value.Contains(securityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
        value.Contains(securityId.ToString("N"), StringComparison.OrdinalIgnoreCase);

    private static bool HasApprovalEvidence(string provenance, string lineage) =>
        provenance.Contains("approved:true", StringComparison.OrdinalIgnoreCase) ||
        lineage.Contains("sm-approval:", StringComparison.OrdinalIgnoreCase) ||
        lineage.Contains("approval:", StringComparison.OrdinalIgnoreCase);

    private static bool HasActiveSecurityMasterStatus(string lineage) =>
        lineage.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static line => line.Split(':', StringSplitOptions.TrimEntries)
                .Any(static token => string.Equals(token, "security-status", StringComparison.OrdinalIgnoreCase)) &&
                line.Contains("security-status:active", StringComparison.OrdinalIgnoreCase));

    private static bool HasLedgerMappingEvidence(string lineage) =>
        lineage.Contains("ledger-map:", StringComparison.OrdinalIgnoreCase) ||
        lineage.Contains("ledger-mapping:", StringComparison.OrdinalIgnoreCase);

    private static void RequireText(string? value, Guid journalEntryId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntryId}' adjustment approval requires {fieldName}.");
        }
    }
}
