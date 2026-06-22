using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

public static class PrivateCapitalCapitalAccountSubledgerBuilder
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
            .Where(item => MatchesFundEventRecordKey(item, key))
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
            .Where(item => MatchesKey(item.CapitalAccountId, item.InvestorId, item.Currency, key) ||
                (IsEventLevelReportOutput(item) &&
                 entries.Any(entry => string.Equals(entry.FundEventId, item.FundEventId, StringComparison.OrdinalIgnoreCase))))
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
        var activityRoute = PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
            fundProfileId,
            ledgerBookId,
            key.CapitalAccountId,
            key.InvestorId,
            key.Currency);
        var paymentIntentEvidence = PrivateCapitalPaymentIntentEvidenceBuilder.BuildForSubledger(
            records,
            evidenceLinks,
            key.Currency,
            lastRecord?.EffectiveDate ?? account?.LastEffectiveDate,
            account?.NetActivity ?? records.Sum(static item => item.NetCapitalActivity),
            activityRoute);
        var evidenceCategories = PrivateCapitalEvidenceCategoryBuilder.BuildForCapitalAccountSubledger(
            records,
            entries,
            impacts,
            outputs,
            paymentIntentEvidence);
        var readiness = BuildReadiness(
            records,
            evidenceCategories,
            validationIssues,
            activityRoute);

        return new PrivateCapitalCapitalAccountSubledgerDto(
            $"capital-account-subledger:{key.CapitalAccountId}:{key.InvestorId ?? "unassigned"}:{key.Currency}".ToLowerInvariant(),
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            key.CapitalAccountId,
            key.InvestorId,
            key.Currency,
            activityRoute,
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
            validationIssues,
            readiness.Readiness,
            readiness.Label,
            readiness.Reason,
            readiness.NextAction,
            readiness.NextActionRoute,
            EvidenceCategories: evidenceCategories,
            PaymentIntentEvidence: paymentIntentEvidence);
    }

    private static PrivateCapitalFundEventLedgerReadinessProjection BuildReadiness(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalEvidenceCategoryDto> evidenceCategories,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        string activityRoute)
    {
        if (validationIssues.Any(static item => item.Severity == AccountingConfigurationValidationSeverityDto.Critical) ||
            records.Any(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked))
        {
            var blockedRecord = records.FirstOrDefault(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked);
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.Blocked,
                "Blocked",
                "A critical validation issue or blocked fund event prevents capital-account subledger reliance.",
                "Repair capital-account subledger",
                blockedRecord?.NextActionRoute ?? activityRoute);
        }

        if (records.Count == 0 ||
            evidenceCategories.Any(static item =>
                !item.IsReady &&
                string.Equals(item.CategoryId, "source-support", StringComparison.OrdinalIgnoreCase)))
        {
            var evidenceRecord = records.FirstOrDefault(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing);
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
                records.Count == 0 ? "No fund events" : "Evidence missing",
                records.Count == 0
                    ? "No fund-event ledger records are linked to this capital-account subledger."
                    : "Retained source evidence is missing for one or more fund events in this capital-account subledger.",
                records.Count == 0 ? "Add fund event" : "Attach retained evidence",
                evidenceRecord?.NextActionRoute ?? activityRoute);
        }

        if (records.Any(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending))
        {
            var approvalRecord = records.First(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending);
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending,
                "Approval pending",
                "One or more fund events require approval before the capital-account subledger can be treated as posting ready.",
                "Submit or review approval",
                approvalRecord.NextActionRoute ?? activityRoute);
        }

        if (records.Any(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.PostingReview))
        {
            var postingRecord = records.First(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.PostingReview);
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.PostingReview,
                "Posting review",
                "One or more fund events have approval and evidence but are not posting ready in the ledger.",
                "Review ledger impact",
                postingRecord.NextActionRoute ?? activityRoute);
        }

        if (records.Any(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.ReportReview))
        {
            var reportRecord = records.First(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.ReportReview);
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.ReportReview,
                "Report review",
                "Ledger impact is ready, but one or more retained report outputs are not ready for stakeholder use.",
                "Prepare report output",
                reportRecord.NextActionRoute ?? activityRoute);
        }

        if (evidenceCategories.Any(static item =>
                !item.IsReady &&
                string.Equals(item.CategoryId, "report-output", StringComparison.OrdinalIgnoreCase)))
        {
            var reportRecord = records.FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item.PrimaryReportRoute));
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.ReportReview,
                "Report review",
                "Ledger impact is ready, but capital-account report-output evidence is not complete.",
                "Prepare report output",
                reportRecord?.PrimaryReportRoute ?? reportRecord?.NextActionRoute ?? activityRoute);
        }

        if (records.All(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Published))
        {
            var publishedRecord = records.First();
            return new(
                PrivateCapitalFundEventLedgerReadinessDto.Published,
                "Published",
                "All fund events in this capital-account subledger have retained evidence, posting-ready ledger impact, capital-account movement, and published report output.",
                "Open published report",
                publishedRecord.NextActionRoute ?? activityRoute);
        }

        var readyRecord = records.FirstOrDefault(static item => item.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Ready) ?? records.First();
        return new(
            PrivateCapitalFundEventLedgerReadinessDto.Ready,
            "Ready",
            "The capital-account subledger has retained evidence, posting-ready ledger impact, capital-account movement, and report output ready for publication.",
            "Review report output",
            readyRecord.NextActionRoute ?? activityRoute);
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

    private static bool IsEventLevelReportOutput(PrivateCapitalReportOutputDto output)
        => string.Equals(output.CapitalAccountId, "capital-account:unassigned", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFundEventRecordKey(
        PrivateCapitalFundEventLedgerRecordDto record,
        CapitalAccountSubledgerKey key)
        => MatchesKey(record.CapitalAccountId, record.InvestorId, record.Currency, key) ||
           record.CapitalAccountSubledgerEntries.Any(entry => MatchesKey(entry.CapitalAccountId, entry.InvestorId, entry.Currency, key));

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
