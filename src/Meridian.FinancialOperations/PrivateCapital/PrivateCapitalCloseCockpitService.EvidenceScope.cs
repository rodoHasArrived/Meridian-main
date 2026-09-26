using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    // Records have already passed the requested period and ledger-entity filter. An
    // account's cumulative subledger remains useful for balance diagnostics, but its
    // older or other-entity evidence cannot establish support for these records.
    private static bool IsCloseReportOutput(
        PrivateCapitalReportOutputDto output,
        PrivateCapitalFundEventLedgerRecordDto record)
        => string.Equals(output.FundEventId, record.FundEventId, StringComparison.OrdinalIgnoreCase) &&
           output.EffectiveDate.Year == record.EffectiveDate.Year &&
           output.EffectiveDate.Month == record.EffectiveDate.Month &&
           !string.IsNullOrWhiteSpace(record.Currency) &&
           string.Equals(output.Currency, record.Currency, StringComparison.OrdinalIgnoreCase) &&
           (MatchesReportAccount(output, record.CapitalAccountId, record.InvestorId) ||
            record.CapitalAccountSubledgerEntries.Any(entry =>
                string.Equals(entry.FundEventId, record.FundEventId, StringComparison.OrdinalIgnoreCase) &&
                entry.EffectiveDate == record.EffectiveDate &&
                entry.JournalEntryId == record.JournalEntryId &&
                string.Equals(entry.Currency, record.Currency, StringComparison.OrdinalIgnoreCase) &&
                MatchesReportAccount(output, entry.CapitalAccountId, entry.InvestorId)));

    // Posted allocation reports may name one retained capital-account impact while the event
    // itself uses capital-account:unassigned. The entry proves that subject; a matching event
    // id alone cannot authorize another account, investor, or currency to supply NAV evidence.
    private static bool MatchesReportAccount(PrivateCapitalReportOutputDto output, string capitalAccountId, string? investorId)
        => !string.IsNullOrWhiteSpace(capitalAccountId) &&
           string.Equals(output.CapitalAccountId, capitalAccountId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(output.InvestorId ?? string.Empty, investorId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool IsSubledgerStatement(
        PrivateCapitalReportOutputDto output,
        PrivateCapitalCapitalAccountSubledgerDto subledger)
        => string.Equals(output.CapitalAccountId, subledger.CapitalAccountId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(output.InvestorId, subledger.InvestorId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(output.Currency, subledger.Currency, StringComparison.OrdinalIgnoreCase);

}
