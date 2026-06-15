using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

internal static class PrivateCapitalFundEventLedgerRecordBuilder
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
        var activityRoute = PrivateCapitalActivityRouteBuilder.Build(
            fundProfileId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRouteBuilder.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = PrivateCapitalActivityRouteBuilder.BuildApprovalRoute(
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

        var operationalRecord = BuildOperationalRecord(
            fundEvent,
            subledgerEntries,
            eventLedgerImpacts,
            eventReportOutputs,
            evidenceLinks,
            validationIssues,
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
            PaymentIntentEvidence: paymentIntentEvidence,
            OperationalRecord: operationalRecord);
    }

    private static PrivateCapitalOperationalRecordLinkageDto BuildOperationalRecord(
        PrivateCapitalFundEventDto fundEvent,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        PrivateCapitalPaymentIntentEvidenceDto? paymentIntentEvidence)
    {
        var eventKind = ResolveEventKind(fundEvent.EntryType, fundEvent.FundEventType);
        var reconciliationCount = validationIssues.Count(static item =>
            item.Code.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
            item.TargetId.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));
        var deliveryEvidenceCount = reportOutputs.Count(static item => item.IsPublished &&
            (!string.IsNullOrWhiteSpace(item.RetainedManifestPath) || !string.IsNullOrWhiteSpace(item.PublicationManifestId)));
        var auditCount = (fundEvent.ApprovalId is null ? 0 : 1) +
                         (paymentIntentEvidence?.CashEvidenceLinkCount ?? 0);
        var requiredActions = new List<string>();
        if (evidenceLinks.Count == 0)
        {
            requiredActions.Add("Retain source evidence for the private-capital event.");
        }

        if (subledgerEntries.Count == 0)
        {
            requiredActions.Add("Normalize the event into capital-account subledger records.");
        }

        if (ledgerImpacts.Count == 0 || ledgerImpacts.Any(static item => !item.IsPostingReady))
        {
            requiredActions.Add("Resolve ledger impact before governed reporting relies on the event.");
        }

        if (reportOutputs.Count == 0)
        {
            requiredActions.Add("Link the event to governed report or stakeholder package output.");
        }

        return new PrivateCapitalOperationalRecordLinkageDto(
            eventKind,
            subledgerEntries.Count > 0 ? "Normalized" : "Missing",
            validationIssues.Count == 0 ? "Clear" : reconciliationCount > 0 ? "ReconciliationReview" : "ValidationReview",
            ledgerImpacts.Count > 0 && ledgerImpacts.All(static item => item.IsPostingReady) ? "PostingReady" : "ReviewRequired",
            fundEvent.JournalStatus.ToString(),
            reportOutputs.Count > 0 && reportOutputs.All(static item => item.IsReportReady) ? "ReportReady" : "ReportReview",
            deliveryEvidenceCount > 0 ? "DeliveryEvidenceRetained" : "DeliveryEvidenceMissing",
            auditCount > 0 ? "AuditLinked" : "AuditMissing",
            evidenceLinks.Count,
            subledgerEntries.Count,
            reconciliationCount,
            ledgerImpacts.Count,
            fundEvent.ApprovalId is null ? 0 : 1,
            reportOutputs.Count,
            deliveryEvidenceCount,
            auditCount,
            evidenceLinks,
            requiredActions);
    }

    private static PrivateCapitalFundEventKindDto ResolveEventKind(ManualJournalEntryTypeDto entryType, string fundEventType)
        => entryType switch
        {
            ManualJournalEntryTypeDto.FormationClosing => PrivateCapitalFundEventKindDto.FormationClosing,
            ManualJournalEntryTypeDto.SubscriptionPacket or ManualJournalEntryTypeDto.Subscription => PrivateCapitalFundEventKindDto.SubscriptionPacket,
            ManualJournalEntryTypeDto.CapitalCall => PrivateCapitalFundEventKindDto.CapitalCall,
            ManualJournalEntryTypeDto.ContributionReceipt => PrivateCapitalFundEventKindDto.ContributionReceipt,
            ManualJournalEntryTypeDto.Investment => PrivateCapitalFundEventKindDto.Investment,
            ManualJournalEntryTypeDto.Distribution or ManualJournalEntryTypeDto.Redemption => PrivateCapitalFundEventKindDto.Distribution,
            ManualJournalEntryTypeDto.Valuation => PrivateCapitalFundEventKindDto.Valuation,
            ManualJournalEntryTypeDto.FeeExpense or ManualJournalEntryTypeDto.ManagementFee or ManualJournalEntryTypeDto.Expense => PrivateCapitalFundEventKindDto.FeeExpense,
            ManualJournalEntryTypeDto.TaxRequest => PrivateCapitalFundEventKindDto.TaxRequest,
            ManualJournalEntryTypeDto.AuditRequest => PrivateCapitalFundEventKindDto.AuditRequest,
            ManualJournalEntryTypeDto.WindDownSupport => PrivateCapitalFundEventKindDto.WindDownSupport,
            _ => ResolveEventKind(fundEventType)
        };

    private static PrivateCapitalFundEventKindDto ResolveEventKind(string fundEventType)
        => fundEventType.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "FORMATIONCLOSING" or "CLOSING" or "FORMATION" => PrivateCapitalFundEventKindDto.FormationClosing,
            "SUBSCRIPTIONPACKET" or "SUBSCRIPTION" => PrivateCapitalFundEventKindDto.SubscriptionPacket,
            "CAPITALCALL" => PrivateCapitalFundEventKindDto.CapitalCall,
            "CONTRIBUTIONRECEIPT" or "CONTRIBUTION" => PrivateCapitalFundEventKindDto.ContributionReceipt,
            "INVESTMENT" => PrivateCapitalFundEventKindDto.Investment,
            "DISTRIBUTION" or "REDEMPTION" => PrivateCapitalFundEventKindDto.Distribution,
            "VALUATION" => PrivateCapitalFundEventKindDto.Valuation,
            "FEEEXPENSE" or "FEE" or "EXPENSE" or "MANAGEMENTFEE" => PrivateCapitalFundEventKindDto.FeeExpense,
            "TAXREQUEST" => PrivateCapitalFundEventKindDto.TaxRequest,
            "AUDITREQUEST" => PrivateCapitalFundEventKindDto.AuditRequest,
            "WINDDOWNSUPPORT" or "WINDDOWN" => PrivateCapitalFundEventKindDto.WindDownSupport,
            _ => PrivateCapitalFundEventKindDto.Other
        };

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
