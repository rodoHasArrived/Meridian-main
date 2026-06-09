using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

internal static class PrivateCapitalCapitalAccountSubledgerBuilder
{
    public static IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> Build(
        string fundProfileId,
        Guid? ledgerBookId,
        DateTimeOffset projectedAtUtc,
        IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> capitalAccounts,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> fundEventRecords,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> projectionValidationIssues)
        => BuildKeys(capitalAccounts, fundEventRecords, subledgerEntries)
            .Select(key => BuildOne(
                fundProfileId,
                ledgerBookId,
                projectedAtUtc,
                key,
                capitalAccounts,
                fundEventRecords,
                subledgerEntries,
                ledgerImpacts,
                reportOutputs,
                projectionValidationIssues))
            .OrderBy(static item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static PrivateCapitalCapitalAccountSubledgerDto BuildOne(
        string fundProfileId,
        Guid? ledgerBookId,
        DateTimeOffset projectedAtUtc,
        CapitalAccountSubledgerKey key,
        IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> capitalAccounts,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> fundEventRecords,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> projectionValidationIssues)
    {
        var account = capitalAccounts.FirstOrDefault(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key));
        var records = fundEventRecords
            .Where(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key))
            .OrderByDescending(static item => item.EffectiveDate)
            .ThenBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var entries = subledgerEntries
            .Where(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key))
            .OrderBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.SubledgerEntryId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var impacts = ledgerImpacts
            .Where(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key))
            .OrderBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.LedgerImpactId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var outputs = reportOutputs
            .Where(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key))
            .OrderByDescending(static item => item.IsPublished)
            .ThenByDescending(static item => item.IsReportReady)
            .ThenBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fundEventIds = records
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var journalEntryIds = records
            .Select(static item => item.JournalEntryId.ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validationIssues = records.SelectMany(static item => item.ValidationIssues)
            .Concat(entries.SelectMany(static item => item.ValidationIssues))
            .Concat(impacts.SelectMany(static item => item.ValidationIssues))
            .Concat(outputs.SelectMany(static item => item.ValidationIssues))
            .Concat(projectionValidationIssues.Where(issue => MatchesValidationIssue(issue, key.CapitalAccountId, fundEventIds, journalEntryIds)))
            .DistinctBy(static item => $"{item.Severity}|{item.Code}|{item.TargetId}|{item.Message}|{item.SuggestedAction}")
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceLinks = records.SelectMany(static item => item.EvidenceLinks)
            .Concat(entries.SelectMany(static item => item.EvidenceLinks))
            .Concat(impacts.SelectMany(static item => item.EvidenceLinks))
            .Concat(outputs.SelectMany(static item => item.EvidenceLinks))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var openingNetActivity = entries.Length == 0 ? 0m : entries[0].RunningNetActivity - entries[0].NetCapitalActivity;
        var endingNetActivity = entries.Length == 0
            ? account?.NetActivity ?? records.Sum(static item => item.NetCapitalActivity)
            : entries[^1].RunningNetActivity;
        var firstEffectiveDate = entries.Select(static item => (DateOnly?)item.EffectiveDate)
            .Concat(records.Select(static item => (DateOnly?)item.EffectiveDate))
            .Where(static item => item.HasValue)
            .Min();
        var lastRecord = records
            .OrderByDescending(static item => item.EffectiveDate)
            .ThenBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new PrivateCapitalCapitalAccountSubledgerDto(
            $"capital-account-subledger:{key.CapitalAccountId}:{key.InvestorId ?? "unassigned"}:{key.Currency}".ToLowerInvariant(),
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            key.CapitalAccountId,
            key.InvestorId,
            key.Currency,
            PrivateCapitalActivityRouteBuilder.BuildCapitalAccountSubledgerRoute(fundProfileId, ledgerBookId, key.CapitalAccountId, key.InvestorId),
            account?.Contributions ?? SumByType(records, ManualJournalEntryTypeDto.CapitalCall),
            account?.Distributions ?? SumByType(records, ManualJournalEntryTypeDto.Distribution),
            account?.Subscriptions ?? SumByType(records, ManualJournalEntryTypeDto.Subscription),
            account?.Redemptions ?? SumByType(records, ManualJournalEntryTypeDto.Redemption),
            account?.ManagementFees ?? SumByType(records, ManualJournalEntryTypeDto.ManagementFee),
            openingNetActivity,
            endingNetActivity,
            account?.NetActivity ?? records.Sum(static item => item.NetCapitalActivity),
            records.Length,
            records.Count(static item => item.ApprovalState == ManualJournalEntryStatusDto.Submitted),
            records.Count(static item => item.IsPosted),
            outputs.Count(static item => item.IsPublished),
            evidenceLinks.Length,
            validationIssues.Length,
            firstEffectiveDate,
            lastRecord?.EffectiveDate ?? account?.LastEffectiveDate,
            lastRecord?.FundEventType ?? account?.LastFundEventType,
            evidenceLinks,
            account,
            records,
            entries,
            impacts,
            outputs,
            validationIssues);
    }

    private static IReadOnlyList<CapitalAccountSubledgerKey> BuildKeys(
        IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> capitalAccounts,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> fundEventRecords,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries)
        => capitalAccounts
            .Select(static item => new CapitalAccountSubledgerKey(item.CapitalAccountId, item.InvestorId, item.Currency))
            .Concat(fundEventRecords.Select(static item => new CapitalAccountSubledgerKey(item.CapitalAccountId, item.InvestorId, item.Currency)))
            .Concat(subledgerEntries.Select(static item => new CapitalAccountSubledgerKey(item.CapitalAccountId, item.InvestorId, item.Currency)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.CapitalAccountId))
            .GroupBy(static item => $"{item.CapitalAccountId.Trim().ToUpperInvariant()}|{(item.InvestorId ?? string.Empty).Trim().ToUpperInvariant()}|{item.Currency.Trim().ToUpperInvariant()}")
            .Select(static group => group.First() with
            {
                CapitalAccountId = group.First().CapitalAccountId.Trim(),
                InvestorId = string.IsNullOrWhiteSpace(group.First().InvestorId) ? null : group.First().InvestorId!.Trim(),
                Currency = group.First().Currency.Trim()
            })
            .ToArray();

    private static bool MatchesKey(string capitalAccountId, string? investorId, string currency, CapitalAccountSubledgerKey key)
        => string.Equals(capitalAccountId, key.CapitalAccountId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(investorId ?? string.Empty, key.InvestorId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(currency, key.Currency, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesValidationIssue(
        AccountingConfigurationValidationIssueDto issue,
        string capitalAccountId,
        IReadOnlySet<string> fundEventIds,
        IReadOnlySet<string> journalEntryIds)
        => issue.TargetId is not null &&
           (string.Equals(issue.TargetId, capitalAccountId, StringComparison.OrdinalIgnoreCase) ||
            fundEventIds.Contains(issue.TargetId) ||
            journalEntryIds.Contains(issue.TargetId));

    private static decimal SumByType(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        ManualJournalEntryTypeDto entryType)
        => records
            .Where(item => item.FundEvent.EntryType == entryType)
            .Sum(static item => Math.Abs(item.NetCapitalActivity));

    private sealed record CapitalAccountSubledgerKey(
        string CapitalAccountId,
        string? InvestorId,
        string Currency);
}
