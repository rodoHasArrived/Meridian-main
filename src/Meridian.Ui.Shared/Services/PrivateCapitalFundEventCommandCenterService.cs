using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed class PrivateCapitalFundEventCommandCenterService : IPrivateCapitalFundEventCommandCenterService
{
    private static readonly IReadOnlyList<string> LiveCapabilities =
    [
        "Event-level evidence, workflow, ledger, capital-account, report, and audit navigation",
        "Payment-intent and cash-evidence posture",
        "Delivery, tax-support, and support-package readiness from retained report output"
    ];

    private static readonly IReadOnlyList<string> PlannedCapabilities =
    [
        "Native live treasury payment execution",
        "Full tax filing workflow",
        "Broad LP portal self-service",
        "Forecasting and scenario modeling"
    ];

    private readonly IManualJournalEntryWorkbenchService _manualJournalEntryWorkbenchService;

    public PrivateCapitalFundEventCommandCenterService(IManualJournalEntryWorkbenchService manualJournalEntryWorkbenchService)
    {
        _manualJournalEntryWorkbenchService = manualJournalEntryWorkbenchService;
    }

    public async Task<PrivateCapitalFundEventCommandCenterDto?> GetCommandCenterAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string fundEventId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedFundEventId = Normalize(fundEventId);
        if (normalizedFundEventId is null)
        {
            return null;
        }

        var activity = await _manualJournalEntryWorkbenchService
            .GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct)
            .ConfigureAwait(false);

        var record = activity.FundEventRecords
            .FirstOrDefault(item => Matches(item.FundEventId, normalizedFundEventId));
        if (record is null)
        {
            return null;
        }

        var paymentIntent = activity.PaymentIntents
            .FirstOrDefault(item => Matches(item.FundEventId, record.FundEventId) ||
                                    Matches(item.PaymentIntentId, record.PaymentIntentId));
        var lanes = BuildLanes(activity.FundProfileId, activity.LedgerBookId, record, paymentIntent);
        var supportPackages = BuildSupportPackages(record, paymentIntent);

        return new PrivateCapitalFundEventCommandCenterDto(
            record.FundEventId,
            record.FundEventType,
            activity.FundProfileId,
            activity.LedgerBookId,
            activity.ProjectedAtUtc,
            PrivateCapitalActivityRouteBuilder.BuildFundEventCommandCenterRoute(activity.FundProfileId, activity.LedgerBookId, record.FundEventId),
            record.Readiness,
            record.ReadinessLabel,
            record.ReadinessReason,
            record.NextAction,
            record.NextActionRoute,
            lanes.Count(static item => item.IsReady),
            lanes.Count(static item => !item.IsReady),
            record,
            lanes,
            supportPackages,
            LiveCapabilities,
            PlannedCapabilities);
    }

    private static IReadOnlyList<PrivateCapitalFundEventCommandCenterLaneDto> BuildLanes(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerRecordDto record,
        PaymentIntentWorkflowDto? paymentIntent)
    {
        return
        [
            BuildEvidenceLane(record),
            BuildWorkflowLane(record),
            BuildLedgerImpactLane(record),
            BuildCapitalAccountLane(fundProfileId, ledgerBookId, record),
            BuildTreasuryLane(record, paymentIntent),
            BuildReconciliationLane(record, paymentIntent),
            BuildReportUsageLane(record),
            BuildDeliveryLane(record),
            BuildTaxSupportLane(record),
            BuildAuditHistoryLane(record, paymentIntent)
        ];
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildEvidenceLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.EvidenceLinkCount > 0 &&
                      record.EvidenceCategories.Any(static item => item.CategoryId == "source-support" && item.IsReady);
        return Lane(
            "evidence",
            "Evidence",
            isReady,
            isReady
                ? $"{record.EvidenceLinkCount} retained evidence link(s) support this fund event."
                : "Retained source evidence is missing or incomplete for this fund event.",
            record.EvidenceRoute,
            record.EvidenceLinks,
            "Attach retained source evidence");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildWorkflowLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.IsPosted || record.ApprovalState is ManualJournalEntryStatusDto.Approved;
        return Lane(
            "workflow",
            "Workflow",
            isReady,
            isReady
                ? $"Fund-event workflow is {record.ApprovalState}."
                : $"Fund-event workflow is {record.ApprovalState}; approval is still required before reliance.",
            record.ApprovalRoute ?? record.ActivityRoute,
            record.EvidenceLinks,
            "Complete fund-event approval workflow");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildLedgerImpactLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.LedgerImpactCount > 0 && record.IsPostingReady;
        return Lane(
            "ledger-impact",
            "Ledger impact",
            isReady,
            isReady
                ? $"{record.LedgerImpactCount} ledger impact row(s) are posting ready."
                : "Ledger impact is missing or still in posting review.",
            record.ActivityRoute,
            record.LedgerImpacts.SelectMany(static item => item.EvidenceLinks).ToArray(),
            "Resolve ledger impact and posting readiness");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildCapitalAccountLane(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.CapitalAccountSubledgerEntryCount > 0;
        return Lane(
            "capital-account-impact",
            "Capital-account impact",
            isReady,
            isReady
                ? $"{record.CapitalAccountSubledgerEntryCount} capital-account subledger row(s) are linked."
                : "No capital-account subledger movement is linked to this fund event.",
            PrivateCapitalActivityRouteBuilder.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                record.CapitalAccountId,
                record.InvestorId,
                record.Currency),
            record.CapitalAccountSubledgerEntries.SelectMany(static item => item.EvidenceLinks).ToArray(),
            "Link fund event to capital-account subledger movement");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildTreasuryLane(
        PrivateCapitalFundEventLedgerRecordDto record,
        PaymentIntentWorkflowDto? paymentIntent)
    {
        var paymentEvidence = record.PaymentIntentEvidence;
        var isReady = paymentEvidence?.IsReady == true || paymentIntent?.Status == PaymentIntentWorkflowStatusDto.ExecutionDeferred;
        return Lane(
            "treasury-expectation",
            "Treasury expectation",
            isReady,
            paymentEvidence is null
                ? "Payment intent evidence is not retained for this fund event."
                : paymentEvidence.Summary,
            paymentIntent?.WorkbenchRoute ?? paymentEvidence?.EvidenceRoute,
            paymentEvidence?.CashEvidenceLinks ?? [],
            "Retain payment intent and cash evidence");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildReconciliationLane(
        PrivateCapitalFundEventLedgerRecordDto record,
        PaymentIntentWorkflowDto? paymentIntent)
    {
        var links = paymentIntent?.ReconciliationLinks ?? [];
        var isReady = record.ValidationIssueCount == 0 &&
                      (links.Count == 0 || links.All(static item => item.Status is "Matched" or "Ready" or "Reconciled"));
        return Lane(
            "reconciliation-status",
            "Reconciliation status",
            isReady,
            isReady
                ? links.Count == 0
                    ? "No reconciliation blockers are retained for this fund event."
                    : $"{links.Count} reconciliation link(s) are retained without blockers."
                : "Validation issues or reconciliation links require review.",
            links.Select(static item => item.EvidenceRoute).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)) ?? record.ActivityRoute,
            links.Select(static item => item.EvidenceRoute).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).ToArray(),
            "Resolve validation and reconciliation review items");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildReportUsageLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.ReportOutputCount > 0 && record.ReportOutputs.All(static item => item.IsReportReady);
        return Lane(
            "report-usage",
            "Report usage",
            isReady,
            isReady
                ? $"{record.ReportOutputCount} governed report output(s) use this fund event."
                : "Governed report output is missing or not ready for this fund event.",
            record.PrimaryReportRoute,
            record.ReportOutputs.SelectMany(static item => item.EvidenceLinks).ToArray(),
            "Generate or repair governed report output");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildDeliveryLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var publishedOutputs = record.ReportOutputs.Where(static item => item.IsPublished).ToArray();
        var isReady = publishedOutputs.Length > 0 &&
                      publishedOutputs.Any(static item => !string.IsNullOrWhiteSpace(item.RetainedManifestPath));
        return Lane(
            "delivery-record",
            "Delivery record",
            isReady,
            isReady
                ? $"{publishedOutputs.Length} published report output(s) retain delivery or manifest evidence."
                : "No retained delivery or publication manifest is linked to this fund event.",
            publishedOutputs.Select(static item => item.ReportOutputRoute ?? item.ReportRoute).FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item)),
            publishedOutputs.SelectMany(static item => item.EvidenceLinks).ToArray(),
            "Retain delivery package or publication manifest");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildTaxSupportLane(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var isReady = record.ReportOutputs.Any(static item => item.IsPublished && item.EvidenceLinkCount > 0) &&
                      record.EvidenceLinkCount > 0;
        return Lane(
            "tax-support",
            "Tax support",
            isReady,
            isReady
                ? "Retained event and report-output evidence can support tax package review."
                : "Tax support remains incomplete until retained event and report-output evidence are linked.",
            record.EvidenceRoute,
            record.EvidenceLinks.Concat(record.ReportOutputs.SelectMany(static item => item.EvidenceLinks)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            "Attach tax support evidence or governed report output");
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto BuildAuditHistoryLane(
        PrivateCapitalFundEventLedgerRecordDto record,
        PaymentIntentWorkflowDto? paymentIntent)
    {
        var auditEvidence = paymentIntent?.AuditHistory.SelectMany(static item => item.EvidenceLinks).ToArray() ?? [];
        var evidence = auditEvidence.Length == 0 ? record.EvidenceLinks : auditEvidence;
        var isReady = evidence.Count > 0 || record.ApprovalRoute is not null;
        return Lane(
            "audit-history",
            "Audit history",
            isReady,
            isReady
                ? "Audit, approval, or retained evidence links are available for this fund event."
                : "No audit history or retained approval evidence is linked to this fund event.",
            record.ApprovalRoute ?? record.EvidenceRoute,
            evidence,
            "Retain approval or audit evidence");
    }

    private static IReadOnlyList<PrivateCapitalFundEventCommandCenterSupportPackageDto> BuildSupportPackages(
        PrivateCapitalFundEventLedgerRecordDto record,
        PaymentIntentWorkflowDto? paymentIntent)
    {
        var packages = new List<PrivateCapitalFundEventCommandCenterSupportPackageDto>
        {
            new(
                $"evidence:{record.FundEventId}",
                "Operational evidence packet",
                record.EvidenceLinkCount > 0 ? "Ready" : "Missing",
                record.EvidenceRoute,
                record.EvidenceLinkCount,
                record.EvidenceLinks,
                record.EvidenceLinkCount > 0 ? [] : ["Attach retained source evidence"])
        };

        if (paymentIntent is not null)
        {
            packages.Add(new PrivateCapitalFundEventCommandCenterSupportPackageDto(
                $"payment-intent:{paymentIntent.PaymentIntentId}",
                "Payment intent evidence",
                paymentIntent.StatusLabel,
                paymentIntent.EvidenceRoute,
                paymentIntent.BankEvidence.Count,
                paymentIntent.BankEvidence.Select(static item => item.EvidenceRoute).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).ToArray(),
                paymentIntent.Status == PaymentIntentWorkflowStatusDto.EvidenceMissing ? ["Retain payment-intent evidence"] : []));
        }

        foreach (var output in record.ReportOutputs)
        {
            packages.Add(new PrivateCapitalFundEventCommandCenterSupportPackageDto(
                $"report-output:{output.ReportOutputId}",
                output.DisplayName,
                output.IsReportReady ? "Ready" : "ReviewRequired",
                output.ReportOutputRoute ?? output.ReportRoute,
                output.EvidenceLinkCount,
                output.EvidenceLinks,
                output.IsReportReady ? [] : ["Repair governed report-output evidence"]));
        }

        return packages;
    }

    private static PrivateCapitalFundEventCommandCenterLaneDto Lane(
        string laneId,
        string label,
        bool isReady,
        string summary,
        string? route,
        IReadOnlyList<string> evidenceLinks,
        string requiredAction)
        => new(
            laneId,
            label,
            isReady ? "Ready" : "ReviewRequired",
            isReady,
            summary,
            Normalize(route),
            evidenceLinks.Count,
            evidenceLinks,
            isReady ? [] : [requiredAction]);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Matches(string? value, string? expected)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.IsNullOrWhiteSpace(expected) &&
           string.Equals(value.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
