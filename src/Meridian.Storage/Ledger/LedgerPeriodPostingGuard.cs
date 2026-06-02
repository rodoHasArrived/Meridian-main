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

        var metadataSymbol = metadata.Symbol?.Trim();
        var instrumentSymbols = instrumentLines
            .Select(static line => line.Account.Symbol?.Trim())
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol!)
            .Concat(string.IsNullOrWhiteSpace(metadataSymbol) ? [] : [metadataSymbol!])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (instrumentSymbols.Length > 1)
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts multiple instrument symbols with a single Security Master security id; split the posting or provide line-level Security Master lineage before append.");
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

        if (instrumentSymbols.Length == 0 &&
            !LineageReferencesInstrument(lineage, null, securityId))
        {
            throw new LedgerValidationException(
                $"Journal entry '{journalEntry.JournalEntryId}' posts an instrument-bearing ledger entry without a Security Master ledger mapping tied to the resolved security id.");
        }

        foreach (var symbol in instrumentSymbols)
        {
            if (!LineageReferencesInstrument(lineage, symbol, securityId))
            {
                throw new LedgerValidationException(
                    $"Journal entry '{journalEntry.JournalEntryId}' declares instrument symbol '{symbol.Trim()}' without matching Security Master lineage.");
            }

            if (!LedgerMappingReferencesInstrument(lineage, symbol, securityId))
            {
                throw new LedgerValidationException(
                    $"Journal entry '{journalEntry.JournalEntryId}' declares instrument symbol '{symbol.Trim()}' without a Security Master ledger mapping tied to the resolved symbol or security id.");
            }
        }

        foreach (var line in instrumentLines)
        {
            var symbol = line.Account.Symbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new LedgerValidationException(
                    $"Journal entry '{journalEntry.JournalEntryId}' posts instrument line '{line.Account.Name}' without an instrument symbol for Security Master lineage.");
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
            normalized.Equals("Futures MTM Settlement", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("LoanPrincipal", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("LoanAndAccrualsClearing", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("AccruedInterestReceivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("InterestIncome", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("CommitmentFeeReceivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("CommitmentFeeIncome", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("PenaltyReceivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("PenaltyIncome", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FeeReceivable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FeeIncome", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("WriteOffExpense", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("LoanDiscount", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("DiscountAmortizationExpense", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("LoanPremium", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("PremiumAmortizationIncome", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("RestructuringLoss", StringComparison.OrdinalIgnoreCase);
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

    private static bool LineageReferencesInstrument(string lineage, string? symbol, Guid securityId)
    {
        var normalizedSymbol = symbol?.Trim();
        foreach (var entry in ExtractLineageEntries(lineage))
        {
            if (!string.IsNullOrWhiteSpace(normalizedSymbol))
            {
                if (string.Equals(entry.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase) &&
                    ReferencesSecurityId(entry.SecurityIdToken, securityId))
                {
                    return true;
                }

                continue;
            }

            if (ReferencesSecurityId(entry.SecurityIdToken, securityId))
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool LedgerMappingReferencesInstrument(string lineage, string? symbol, Guid securityId)
    {
        var normalizedSymbol = symbol?.Trim();
        foreach (var token in ExtractLedgerMappingTokens(lineage))
        {
            if ((!string.IsNullOrWhiteSpace(normalizedSymbol) &&
                    ContainsDelimitedToken(token, normalizedSymbol)) ||
                token.Contains(securityId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                token.Contains(securityId.ToString("N"), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDelimitedToken(string value, string token)
    {
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : value[index - 1];
            var afterIndex = index + token.Length;
            var after = afterIndex >= value.Length ? '\0' : value[afterIndex];
            if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
            {
                return true;
            }

            index = value.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IEnumerable<string> ExtractLedgerMappingTokens(string lineage)
    {
        foreach (var marker in new[] { "ledger-map:", "ledger-mapping:" })
        {
            var searchIndex = 0;
            while (searchIndex < lineage.Length)
            {
                var start = lineage.IndexOf(marker, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    break;
                }

                var end = FindLedgerMappingTokenEnd(lineage, start + marker.Length);
                yield return lineage[start..end].Trim();
                searchIndex = end;
            }
        }
    }

    private static IEnumerable<(string Symbol, string SecurityIdToken)> ExtractLineageEntries(string lineage)
    {
        foreach (var rawEntry in lineage.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var firstSeparator = rawEntry.IndexOf(':');
            if (firstSeparator <= 0)
            {
                continue;
            }

            var secondSeparator = rawEntry.IndexOf(':', firstSeparator + 1);
            if (secondSeparator <= firstSeparator + 1)
            {
                continue;
            }

            yield return (
                Symbol: rawEntry[..firstSeparator].Trim(),
                SecurityIdToken: rawEntry[(firstSeparator + 1)..secondSeparator].Trim());
        }
    }

    private static int FindLedgerMappingTokenEnd(string lineage, int valueStart)
    {
        var end = lineage.Length;
        foreach (var delimiter in new[] { ";", "|", ",", ":sm-approval:", ":approval:", ":status:", ":security-status:", ":securitystatus:", ":security-master:" })
        {
            var index = lineage.IndexOf(delimiter, valueStart, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                end = Math.Min(end, index);
            }
        }

        return end;
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
