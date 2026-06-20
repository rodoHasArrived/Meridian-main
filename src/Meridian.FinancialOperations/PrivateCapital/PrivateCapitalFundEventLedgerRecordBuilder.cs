using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

public static class PrivateCapitalFundEventLedgerRecordBuilder
{
    public static IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> Build(
        string fundProfileId,
        IReadOnlyList<PrivateCapitalFundEventDto> fundEvents,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> capitalAccountSubledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
        => fundEvents
            .Select(fundEvent => BuildRecord(
                fundProfileId,
                fundEvent,
                capitalAccountSubledgerEntries,
                ledgerImpacts,
                reportOutputs))
            .OrderByDescending(static item => item.EffectiveDate)
            .ThenBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static PrivateCapitalFundEventLedgerRecordDto BuildRecord(
        string fundProfileId,
        PrivateCapitalFundEventDto fundEvent,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> capitalAccountSubledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var subledgerEntries = capitalAccountSubledgerEntries
            .Where(item => string.Equals(item.FundEventId, fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.EffectiveDate)
            .ThenBy(item => item.UpdatedAtUtc)
            .ThenBy(item => item.SubledgerEntryId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eventSubledgerNetActivity = subledgerEntries.Sum(static item => item.NetCapitalActivity);
        var openingNetActivity = subledgerEntries.Length == 0
            ? fundEvent.NetCapitalActivity
            : subledgerEntries.Sum(static item => item.RunningNetActivity - item.NetCapitalActivity);
        var endingNetActivity = subledgerEntries.Length == 0
            ? fundEvent.NetCapitalActivity
            : subledgerEntries.Sum(static item => item.RunningNetActivity);
        var eventLedgerImpacts = ledgerImpacts
            .Where(item => string.Equals(item.FundEventId, fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.EffectiveDate)
            .ThenBy(item => item.LedgerImpactId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eventReportOutputs = reportOutputs
            .Where(item => string.Equals(item.FundEventId, fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.EffectiveDate)
            .ThenBy(item => item.ReportOutputType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primaryReportOutput = SelectPrimaryReportOutput(eventReportOutputs);
        var evidenceLinks = MergeEvidenceLinks(
            MergeEvidenceLinks(
                MergeEvidenceLinks(
                    fundEvent.EvidenceLinks,
                    subledgerEntries.SelectMany(static item => item.EvidenceLinks)),
                eventLedgerImpacts.SelectMany(static item => item.EvidenceLinks)),
            eventReportOutputs.SelectMany(static item => item.EvidenceLinks));
        var validationIssues = fundEvent.ValidationIssues
            .Concat(subledgerEntries.SelectMany(static item => item.ValidationIssues))
            .Concat(eventLedgerImpacts.SelectMany(static item => item.ValidationIssues))
            .Concat(eventReportOutputs.SelectMany(static item => item.ValidationIssues))
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activityRoute = PrivateCapitalActivityRoutes.Build(
            fundProfileId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRoutes.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = PrivateCapitalActivityRoutes.BuildApprovalRoute(
            fundProfileId,
            fundEvent.JournalEntryId,
            fundEvent.ApprovalId);
        var paymentIntentEvidence = PrivateCapitalPaymentIntentEvidenceBuilder.BuildForFundEvent(
            fundEvent,
            evidenceLinks,
            evidenceRoute);
        var isPostingReady = eventLedgerImpacts.Length > 0 && eventLedgerImpacts.All(static item => item.IsPostingReady);
        var isReportReady = eventReportOutputs.Length > 0 && eventReportOutputs.All(static item => item.IsReportReady);
        var isPublished = eventReportOutputs.Any(static item => item.IsPublished);
        var primaryReportRoute = primaryReportOutput?.ReportOutputRoute ?? primaryReportOutput?.ReportRoute;
        var readiness = PrivateCapitalFundEventLedgerReadinessBuilder.Build(
            fundEvent.JournalStatus,
            evidenceLinks.Count,
            isPostingReady,
            isReportReady,
            isPublished,
            eventReportOutputs.Length,
            validationIssues.Any(static item => item.Severity == AccountingConfigurationValidationSeverityDto.Critical),
            activityRoute,
            evidenceRoute,
            approvalRoute,
            primaryReportRoute);
        var evidenceCategories = PrivateCapitalEvidenceCategoryBuilder.BuildForFundEvent(
            fundEvent,
            subledgerEntries,
            eventLedgerImpacts,
            eventReportOutputs,
            approvalRoute,
            paymentIntentEvidence);

        return new PrivateCapitalFundEventLedgerRecordDto(
            $"fund-event-ledger-record:{fundEvent.FundEventId}".ToLowerInvariant(),
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.JournalStatus,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            openingNetActivity,
            endingNetActivity,
            fundEvent.Memo,
            fundEvent.PaymentIntentId,
            fundEvent.SettlementReference,
            activityRoute,
            evidenceRoute,
            fundEvent.ApprovalId,
            approvalRoute,
            fundEvent.IsPosted,
            isPostingReady,
            isReportReady,
            isPublished,
            readiness.Readiness,
            readiness.Label,
            readiness.Reason,
            readiness.NextAction,
            readiness.NextActionRoute,
            evidenceLinks.Count,
            subledgerEntries.Length,
            eventLedgerImpacts.Length,
            eventReportOutputs.Length,
            validationIssues.Length,
            primaryReportOutput?.ReportOutputId,
            primaryReportOutput?.ReportOutputType,
            primaryReportRoute,
            primaryReportOutput?.ReportWorkflowState,
            primaryReportOutput?.PublicationManifestId,
            primaryReportOutput?.RetainedManifestPath,
            primaryReportOutput?.ReportLineProvenanceCount ?? 0,
            evidenceLinks,
            fundEvent,
            subledgerEntries,
            eventLedgerImpacts,
            eventReportOutputs,
            validationIssues,
            EvidenceCategories: evidenceCategories,
            PaymentIntentEvidence: paymentIntentEvidence);
    }

    private static PrivateCapitalReportOutputDto? SelectPrimaryReportOutput(
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
        => reportOutputs
            .OrderByDescending(static item => item.IsPublished)
            .ThenByDescending(static item => item.IsReportReady)
            .ThenBy(static item => item.ReportOutputType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static IReadOnlyList<string> MergeEvidenceLinks(
        IEnumerable<string> existing,
        IEnumerable<string>? incoming)
        => existing.Concat(incoming ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
