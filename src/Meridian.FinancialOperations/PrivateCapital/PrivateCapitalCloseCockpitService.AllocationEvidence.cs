using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    private static bool HasAllocationEvidence(PrivateCapitalFundEventLedgerRecordDto record)
    {
        // Record/category evidence is a diagnostic union that can include rejected
        // child outputs. A label alone cannot establish allocation proof; its link
        // must be retained on an underlying source belonging to this event's scope.
        var scopedLinks = AllocationEvidenceLinks(record)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return scopedLinks.Any(static link => ContainsFinancialOperationToken(link, "allocation")) ||
               record.EvidenceCategories.Any(category => category.IsReady &&
                   (ContainsFinancialOperationToken(category.CategoryId, "allocation") ||
                    ContainsFinancialOperationToken(category.Label, "allocation")) &&
                   category.EvidenceLinks.Any(scopedLinks.Contains));
    }

    private static IEnumerable<string> AllocationEvidenceLinks(PrivateCapitalFundEventLedgerRecordDto record)
        => record.FundEvent.EvidenceLinks
            .Concat(record.CapitalAccountSubledgerEntries
                .Where(entry => IsScopedAllocationEntry(entry, record))
                .SelectMany(static entry => entry.EvidenceLinks))
            .Concat(record.LedgerImpacts
                .Where(impact => string.Equals(impact.FundEventId, record.FundEventId, StringComparison.OrdinalIgnoreCase) &&
                                 impact.JournalEntryId == record.JournalEntryId &&
                                 impact.EffectiveDate == record.EffectiveDate &&
                                 string.Equals(impact.Currency, record.Currency, StringComparison.OrdinalIgnoreCase) &&
                                 HasAllocationSubject(impact.CapitalAccountId, impact.InvestorId, record))
                .SelectMany(static impact => impact.EvidenceLinks.Concat(impact.Lines
                    .Where(static line => !string.IsNullOrWhiteSpace(line.EvidenceLink))
                    .Select(static line => line.EvidenceLink!))))
            .Concat(record.ReportOutputs
                .Where(output => IsCloseReportOutput(output, record) && IsApprovedReadyReportOutput(output))
                .SelectMany(static output => output.EvidenceLinks));

    private static bool IsScopedAllocationEntry(
        PrivateCapitalCapitalAccountSubledgerEntryDto entry, PrivateCapitalFundEventLedgerRecordDto record)
        => string.Equals(entry.FundEventId, record.FundEventId, StringComparison.OrdinalIgnoreCase) &&
           entry.JournalEntryId == record.JournalEntryId && entry.EffectiveDate == record.EffectiveDate &&
           string.Equals(entry.Currency, record.Currency, StringComparison.OrdinalIgnoreCase);

    private static bool HasAllocationSubject(string capitalAccountId, string? investorId,
        PrivateCapitalFundEventLedgerRecordDto record)
        => (string.Equals(capitalAccountId, record.CapitalAccountId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(investorId, record.InvestorId, StringComparison.OrdinalIgnoreCase)) ||
           record.CapitalAccountSubledgerEntries.Any(entry => IsScopedAllocationEntry(entry, record) &&
               string.Equals(capitalAccountId, entry.CapitalAccountId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(investorId, entry.InvestorId, StringComparison.OrdinalIgnoreCase));
}
