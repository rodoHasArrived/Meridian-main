using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private async Task<FinancialRecordExplorerDto> BuildReportLineProvenanceExplorerAsync(
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        ReportAccessQueryContext? reportAccess,
        CancellationToken ct)
    {
        var workflowService = _reportPackWorkflowService;
        if (workflowService is null)
        {
            return CreateBlockedExplorer(
                ReportLineProvenanceExplorerId,
                "Report-Line Provenance Explorer",
                "Drill from reported lines into retained source records, reconciliations, ledger provenance, approvals, delivery history, and restatement evidence.",
                "Report pack workflow service is not registered.");
        }

        var systemViews = BuildSystemViews(
            ReportLineProvenanceExplorerId,
            "Report lines",
            "Governed report lines with retained source provenance.");
        var savedViews = await LoadSavedViewsAsync(tenantId, ReportLineProvenanceExplorerId, systemViews, ct).ConfigureAwait(false);
        var explorer = BuildReportLineProvenanceExplorer(
            workflowService.ListAccessibleRecords(200, reportAccess),
            _reportPackDeliveryService?.ListAttempts(500),
            savedViews: savedViews,
            accessContext: reportAccess);
        return ApplyExplorerQuery(explorer, query);
    }

    public static FinancialRecordExplorerDto BuildReportLineProvenanceExplorer(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto>? savedViews = null,
        ReportAccessQueryContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(workflowRecords);

        var authorizedRecords = accessContext is null
            ? workflowRecords
            : ReportPackRunReadService.FilterWorkflowRecords(workflowRecords, accessContext);
        var records = authorizedRecords
            .Where(static record => IsAuthoritativelyReported(record) && record.LineProvenance is { Count: > 0 })
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenBy(static record => record.ReportId)
            .ToArray();
        // Unbound (null) context = the legacy tenant-level endpoint: sanitized attempts, matching the unfiltered records above.
        var attempts = accessContext is null
            ? (deliveryAttempts ?? []).Select(ReportingDeliveryReadModelSecurity.SanitizeAttempt).ToArray()
            : ReportingDeliveryReadModelSecurity.FilterVisibleAttempts(deliveryAttempts ?? [], accessContext, records);
        var rows = records
            .SelectMany(record => record.LineProvenance!
                .Select((line, index) => BuildReportLineProvenanceRow(record, line, attempts, index)))
            .ToArray();
        var activeSavedViews = savedViews is { Count: > 0 }
            ? savedViews
            : BuildSystemViews(
                ReportLineProvenanceExplorerId,
                "Report lines",
                "Governed report lines with retained source provenance.");
        var sourceState = rows.Length == 0
            ? "No published or restated report-line provenance with retained publication evidence is available."
            : $"{rows.Length} reported line(s) across {records.Length} evidenced report pack(s) retain instrument, position or transaction, reconciliation, ledger provenance, approval, report, evidence, and audit drill-through links.";

        return new FinancialRecordExplorerDto(
            ReportLineProvenanceExplorerId,
            "Report-Line Provenance Explorer",
            "Drill from published or restated report lines into retained instruments, positions or transactions, reconciliations, ledger provenance, reports, evidence, and audit links.",
            sourceState,
            IsBlocked: false,
            BlockedReason: string.Empty,
            ScopeItems: BuildReportLineProvenanceScope(records, attempts),
            SavedViews: activeSavedViews,
            SummaryItems: BuildReportLineProvenanceSummary(records, attempts, rows.Length),
            Filters: BuildReportLineProvenanceFilters(records),
            Columns:
            [
                new("lineKey", "Report Line", Width: 200),
                new("report", "Report Pack", Width: 190),
                new("value", "Value", "money", 110, IsRightAligned: true),
                new("source", "Source", Width: 170),
                new("instrument", "Instrument", Width: 160),
                new("activity", "Position / Transaction", Width: 190),
                new("reconciliation", "Reconciliation", Width: 140),
                new("journal", "Ledger Reference", Width: 140),
                new("approval", "Approval", Width: 120),
                new("delivery", "Delivery", Width: 140),
                new("restatement", "Restatement", Width: 140)
            ],
            Rows: rows,
            SelectedRecord: rows.FirstOrDefault()?.Detail,
            ProofActions: BuildReportLineProvenanceProofActions(rows.Length),
            RecordGraph: BuildReportLineProvenanceGraph(rows));
    }

    private static IReadOnlyList<FinancialRecordExplorerScopeItemDto> BuildReportLineProvenanceScope(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        if (records.Count == 0)
        {
            return
            [
                new("Source", "No report-line provenance", FinancialRecordExplorerTone.Warning),
                new("Explorer", ReportLineProvenanceExplorerId, FinancialRecordExplorerTone.Info)
            ];
        }

        var latest = records
            .OrderByDescending(static record => record.UpdatedAt)
            .First();
        var reportIds = records.Select(static record => record.ReportId).ToHashSet();
        var scopedDeliveryCount = deliveryAttempts.Count(attempt => reportIds.Contains(attempt.ReportId));
        return
        [
            new("Report packs", records.Count.ToString(CultureInfo.InvariantCulture), FinancialRecordExplorerTone.Info),
            new("Latest period", latest.Period),
            new("Fund", latest.FundProfileId),
            new("Delivery attempts", scopedDeliveryCount.ToString(CultureInfo.InvariantCulture), scopedDeliveryCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning)
        ];
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildReportLineProvenanceSummary(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        int rowCount)
    {
        var lines = records.SelectMany(static record => record.LineProvenance ?? []).ToArray();
        var reportIds = records.Select(static record => record.ReportId).ToHashSet();
        var scopedDeliveries = deliveryAttempts.Where(attempt => reportIds.Contains(attempt.ReportId)).ToArray();
        var restatedLineCount = records
            .SelectMany(static record => (record.Restatement?.ChangedLines ?? [])
                .Select(line => $"{record.ReportId:D}:{line.LineKey}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var approvalCount = DistinctNonEmptyCount(lines.Select(static line => line.ApprovalId))
            + records.Sum(static record => record.AuditTrail.Count(static audit => audit.ToState == ReportPackWorkflowStateDto.Approved));

        return
        [
            new("Reported lines", rowCount.ToString(CultureInfo.InvariantCulture), "Published or restated report lines with retained publication evidence and provenance.", rowCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new("Instruments", lines.Count(static line => HasText(line.SecurityMasterId) || HasText(line.SecurityDefinitionId)).ToString(CultureInfo.InvariantCulture), "Lines linked to retained Security Master or instrument definition evidence."),
            new("Positions / transactions", lines.Count(static line => HasText(line.ProviderEventId) || HasText(line.SourceSessionId) || ContainsToken(line.SourceKind, "position") || ContainsToken(line.SourceKind, "transaction")).ToString(CultureInfo.InvariantCulture), "Lines linked to provider positions, transactions, source sessions, or retained source rows."),
            new("Source records", DistinctNonEmptyCount(lines.Select(static line => line.SourceId)).ToString(CultureInfo.InvariantCulture), "Distinct retained source records linked to report lines."),
            new("Reconciliations", lines.Count(static line => HasText(line.ReconciliationRunId) || HasText(line.ReconciliationCaseId)).ToString(CultureInfo.InvariantCulture), "Lines linked to reconciliation runs or break cases."),
            new("Ledger references", lines.Count(static line => HasText(line.LedgerEntryId)).ToString(CultureInfo.InvariantCulture), "Line-level ledger provenance references; these are not promoted to posted journals without durable resolution."),
            new("Approvals", approvalCount.ToString(CultureInfo.InvariantCulture), "Line-level and workflow approval evidence retained by the report-pack workflow."),
            new("Deliveries", scopedDeliveries.Length.ToString(CultureInfo.InvariantCulture), "Retained report-pack delivery attempts and packages.", scopedDeliveries.Length > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new("Audit links", lines.Count(static line => HasText(line.EvidenceId)).ToString(CultureInfo.InvariantCulture), "Lines with retained evidence ids that route to audit packets."),
            new("Restatements", restatedLineCount.ToString(CultureInfo.InvariantCulture), "Changed report lines with restatement evidence.", restatedLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
        ];
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildReportLineProvenanceFilters(
        IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        var filters = new List<FinancialRecordExplorerFilterDto>();
        filters.AddRange(records
            .Select(static record => record.State)
            .Distinct()
            .OrderBy(static state => state.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(state => new FinancialRecordExplorerFilterDto(
                $"state-{Slugify(state.ToString())}",
                "Workflow state",
                state.ToString(),
                Tone: ToneFromReportPackState(state))));
        filters.AddRange(records
            .Select(static record => record.Period)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static period => period, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(static period => new FinancialRecordExplorerFilterDto($"period-{Slugify(period)}", "Period", period)));
        filters.AddRange(records
            .Select(static record => record.FundProfileId)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static fund => fund, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(static fund => new FinancialRecordExplorerFilterDto($"fund-{Slugify(fund)}", "Fund", fund)));

        return filters.ToArray();
    }

    private static FinancialRecordExplorerRowDto BuildReportLineProvenanceRow(
        ReportPackWorkflowRecordDto record,
        ReportPackLineProvenanceDto line,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        int index)
    {
        var reportHref = UiApiRoutes.WithParam(
            UiApiRoutes.FundReportPackById,
            "reportId",
            record.ReportId.ToString("D", CultureInfo.InvariantCulture));
        var sourceHref = BuildReportLineSourceHref(line);
        var instrumentHref = BuildReportLineInstrumentHref(line);
        var activityHref = BuildReportLineActivityHref(line, sourceHref);
        var auditHref = BuildReportLineAuditHref(line);
        var reconciliationHref = BuildReportLineReconciliationHref(line);
        var approvalHref = BuildReportLineApprovalHref(line);
        var deliveries = deliveryAttempts
            .Where(attempt => attempt.ReportId == record.ReportId)
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenByDescending(static attempt => attempt.AttemptNumber)
            .ToArray();
        var latestDelivery = deliveries.FirstOrDefault();
        var deliveryGraphHref = latestDelivery is null
            ? string.Empty
            : BuildReportLineDeliveryGraphHref(record.ReportId, latestDelivery.AttemptId);
        // Legacy workflow records do not carry an immutable governed-run identifier. Do not
        // reinterpret the legacy GUID as a canonical run/package id; the latest retained delivery
        // evidence remains navigable while canonical distribution history stays unbound.
        var deliveryHistoryHref = deliveryGraphHref;
        var changedLines = record.Restatement?.ChangedLines
            .Where(changedLine => IsSameLineKey(changedLine.LineKey, line.LineKey))
            .ToArray() ?? [];
        var restatementHref = BuildReportLineRestatementHref(record, changedLines);
        var recordId = $"report-line:{record.ReportId:N}:{Slugify(line.LineKey)}:{index}";
        var reportLabel = $"{record.Period} - {record.TemplateId.Name} v{record.TemplateId.Version}";
        var deliveryLabel = latestDelivery is null
            ? "No canonical distribution binding"
            : $"{latestDelivery.State} #{latestDelivery.AttemptNumber}";
        var deliveryReferenceSummary = latestDelivery is null
            ? "Create a new certified governed run before distribution."
            : ReportingDeliveryReadModelSecurity.IsSafeRetainedEvidenceHref(latestDelivery.DeliveryReference)
                ? latestDelivery.DeliveryReference.Trim()
                : "Legacy delivery reference retained without navigable credentials.";
        var restatementLabel = changedLines.Length == 0
            ? "No change"
            : $"{changedLines.Length} changed line{Plural(changedLines.Length)}";
        var status = changedLines.Length > 0
            ? "Restated"
            : latestDelivery?.State.ToString() ?? record.State.ToString();
        var tone = changedLines.Length > 0
            ? FinancialRecordExplorerTone.Warning
            : latestDelivery?.State == ReportPackDeliveryStateDto.Delivered
                ? FinancialRecordExplorerTone.Success
                : ToneFromReportPackState(record.State);

        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Report line",
            line.LineKey,
            reportLabel,
            "Published or restated report line with retained instrument, position or transaction, reconciliation, ledger provenance, report, evidence, and audit drill-through links.",
            tone,
            Fields:
            [
                new("Report value", EmptyFallback(line.ReportValue, "Not captured")),
                new("Source record", $"{line.SourceKind}:{line.SourceId}", sourceHref),
                new("Instrument", BuildReportLineInstrumentLabel(line), instrumentHref, HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Position / transaction", BuildReportLineActivityLabel(line), activityHref, HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Evidence id", line.EvidenceId),
                new("Reconciliation", BuildReportLineReconciliationLabel(line), reconciliationHref, HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning),
                new("Ledger provenance", EmptyFallback(line.LedgerEntryId, "No ledger reference"), "A report-line ledger reference is provenance only; it is not a resolved posted journal.", HasText(line.LedgerEntryId) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Default),
                new("Approval", EmptyFallback(line.ApprovalId, "No line approval"), approvalHref, HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Evidence and audit links", line.EvidenceId, auditHref, HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Delivery history", deliveryLabel, deliveryReferenceSummary, latestDelivery is null ? FinancialRecordExplorerTone.Warning : ToneFromDeliveryState(latestDelivery.State)),
                new("Restatement", restatementLabel, record.Restatement?.ReasonCode ?? "No restatement on this line", changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default),
                new("Updated", record.UpdatedAt.ToString("u", CultureInfo.InvariantCulture))
            ],
            ProofActions: BuildReportLineProofActions(
                sourceHref,
                instrumentHref,
                activityHref,
                reconciliationHref,
                approvalHref,
                deliveryHistoryHref,
                auditHref,
                deliveryGraphHref,
                restatementHref,
                changedLines.Length),
            UsedIn: BuildReportLineUsedIn(record, reportHref, deliveryHistoryHref, deliveryGraphHref, restatementHref, latestDelivery, changedLines),
            Impacts: BuildReportLineImpacts(line, sourceHref, instrumentHref, activityHref, reconciliationHref, approvalHref, deliveryHistoryHref, auditHref, deliveryGraphHref, restatementHref, changedLines.Length),
            FullRecordHref: sourceHref);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "report-line",
            line.LineKey,
            $"{line.SourceKind}:{line.SourceId}",
            status,
            tone,
            Cells:
            [
                new("lineKey", line.LineKey, LinkHref: sourceHref),
                new("report", reportLabel, LinkHref: reportHref),
                new("value", EmptyFallback(line.ReportValue, "-")),
                new("source", $"{line.SourceKind}:{line.SourceId}", LinkHref: sourceHref),
                new("instrument", BuildReportLineInstrumentLabel(line), LinkHref: instrumentHref, Tone: HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("activity", BuildReportLineActivityLabel(line), LinkHref: activityHref, Tone: HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("reconciliation", BuildReportLineReconciliationLabel(line), LinkHref: reconciliationHref, Tone: HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning),
                new("journal", EmptyFallback(line.LedgerEntryId, "-"), Tone: HasText(line.LedgerEntryId) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Default),
                new("approval", EmptyFallback(line.ApprovalId, "-"), LinkHref: approvalHref, Tone: HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("delivery", deliveryLabel, LinkHref: deliveryHistoryHref, Tone: latestDelivery is null ? FinancialRecordExplorerTone.Info : ToneFromDeliveryState(latestDelivery.State)),
                new("restatement", restatementLabel, LinkHref: restatementHref, Tone: changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
            ],
            detail);
    }

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildReportLineProvenanceProofActions(int rowCount)
        =>
        [
            new(
                "open-reporting-runs",
                "Open governed reporting runs",
                "Open canonical governed reporting run records.",
                UiApiRoutes.ReportingRuns,
                rowCount > 0,
                rowCount > 0 ? string.Empty : "No report-line provenance is available.",
                rowCount > 0 ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-provenance-evidence-graph",
                "Open provenance evidence graph",
                "Open the retained evidence graph for report-line provenance.",
                BuildEvidenceSubjectRoute(
                    UiApiRoutes.WorkstationEvidenceSubjectGraph,
                    "report-line-provenance",
                    "all"),
                rowCount > 0,
                rowCount > 0 ? string.Empty : "No report-line provenance is available.",
                rowCount > 0 ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning)
        ];

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildReportLineProofActions(
        string sourceHref,
        string instrumentHref,
        string activityHref,
        string reconciliationHref,
        string approvalHref,
        string deliveryHistoryHref,
        string auditHref,
        string deliveryGraphHref,
        string restatementHref,
        int restatementLineCount)
        =>
        [
            new(
                "open-source-record",
                "Open source record",
                "Open the retained source record behind this report line.",
                sourceHref,
                HasText(sourceHref),
                HasText(sourceHref) ? string.Empty : "No source record route was retained.",
                HasText(sourceHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-instrument",
                "Open instrument",
                "Open the retained Security Master or instrument definition evidence for this report line.",
                instrumentHref,
                HasText(instrumentHref),
                HasText(instrumentHref) ? string.Empty : "No instrument identifier was retained on this line.",
                HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new(
                "open-position-transaction",
                "Open position/transaction evidence",
                "Open the retained provider position, transaction, source session, or source record feeding this report line.",
                activityHref,
                HasText(activityHref),
                HasText(activityHref) ? string.Empty : "No position, transaction, source session, or source record route was retained.",
                HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-reconciliation",
                "Open reconciliation",
                "Open the reconciliation run or break case linked to this report line.",
                reconciliationHref,
                HasText(reconciliationHref),
                HasText(reconciliationHref) ? string.Empty : "No reconciliation run or break case was retained.",
                HasText(reconciliationHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-approval-evidence",
                "Open approval evidence",
                "Open retained approval evidence for this report line.",
                approvalHref,
                HasText(approvalHref),
                HasText(approvalHref) ? string.Empty : "No approval reference was retained on this line.",
                HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new(
                "open-evidence-audit-links",
                "Open evidence and audit links",
                "Open the retained evidence packet that anchors this report line's audit trail.",
                auditHref,
                HasText(auditHref),
                HasText(auditHref) ? string.Empty : "No report-line evidence id was retained.",
                HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-delivery-history",
                "Open delivery history",
                "Open retained delivery evidence for the latest legacy report-pack attempt.",
                deliveryHistoryHref,
                HasText(deliveryHistoryHref),
                HasText(deliveryHistoryHref) ? string.Empty : "No retained legacy delivery evidence exists; create a certified governed run before distribution.",
                HasText(deliveryHistoryHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-delivery-evidence-graph",
                "Open delivery evidence graph",
                "Open the evidence graph for the latest retained report-pack delivery attempt.",
                deliveryGraphHref,
                HasText(deliveryGraphHref),
                HasText(deliveryGraphHref) ? string.Empty : "No retained delivery attempt has an evidence graph yet.",
                HasText(deliveryGraphHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-restatement-evidence",
                "Open restatement evidence",
                "Open retained evidence for restatement changes on this report line.",
                restatementHref,
                restatementLineCount > 0,
                restatementLineCount > 0 ? string.Empty : "This report line has not been restated.",
                restatementLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
        ];

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildReportLineUsedIn(
        ReportPackWorkflowRecordDto record,
        string reportHref,
        string deliveryHistoryHref,
        string deliveryGraphHref,
        string restatementHref,
        ReportPackDeliveryAttemptDto? latestDelivery,
        IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>
        {
            new(
                $"report-pack:{record.ReportId:D}",
                record.State == ReportPackWorkflowStateDto.Restated
                    ? "Restated report pack (read-only)"
                    : "Published report pack (read-only)",
                $"{record.TemplateId.Name} v{record.TemplateId.Version} for {record.Period} is retained with publication manifest {record.Publication!.ManifestId} and evidence hash {record.Publication.EvidenceHash}.",
                reportHref,
                ToneFromReportPackState(record.State))
        };

        if (latestDelivery is not null)
        {
            relationships.Add(new(
                $"report-delivery:{latestDelivery.AttemptId:D}",
                "Delivery history",
                $"{latestDelivery.Recipient} delivery attempt #{latestDelivery.AttemptNumber} is {latestDelivery.State}.",
                deliveryHistoryHref,
                ToneFromDeliveryState(latestDelivery.State)));
            relationships.Add(new(
                $"delivery-graph:{latestDelivery.AttemptId:D}",
                "Delivery evidence graph",
                "Latest retained report-pack delivery evidence packet.",
                deliveryGraphHref,
                FinancialRecordExplorerTone.Info));
        }

        if (changedLines.Count > 0)
        {
            relationships.Add(new(
                $"report-restatement:{record.ReportId:D}",
                "Restatement record",
                $"{record.Restatement?.ReasonCode ?? "Restatement"} changed this report line.",
                restatementHref,
                FinancialRecordExplorerTone.Warning));
        }

        return relationships;
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildReportLineImpacts(
        ReportPackLineProvenanceDto line,
        string sourceHref,
        string instrumentHref,
        string activityHref,
        string reconciliationHref,
        string approvalHref,
        string deliveryHistoryHref,
        string auditHref,
        string deliveryGraphHref,
        string restatementHref,
        int restatementLineCount)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>
        {
            new("source-record", "Source record", $"{line.SourceKind}:{line.SourceId} supports the report line value.", sourceHref, FinancialRecordExplorerTone.Info)
        };

        relationships.Add(new(
            "instrument",
            "Instrument",
            HasText(instrumentHref)
                ? $"Instrument identity {BuildReportLineInstrumentLabel(line)} anchors this report line."
                : "No Security Master or instrument definition identifier was retained.",
            instrumentHref,
            HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "position-transaction",
            "Position / transaction",
            HasText(activityHref)
                ? $"{BuildReportLineActivityLabel(line)} feeds reconciliation and reported-line provenance."
                : "No retained provider position, transaction, source session, or source route was retained.",
            activityHref,
            HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "reconciliation",
            "Reconciliation",
            HasText(reconciliationHref)
                ? $"Reconciliation outcome is {EmptyFallback(line.ReconciliationOutcome, "retained")}."
                : "No reconciliation run or break case was retained.",
            reconciliationHref,
            HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning));
        if (HasText(line.LedgerEntryId))
        {
            relationships.Add(new(
                "ledger-reference",
                "Ledger provenance reference",
                $"Report provenance retains ledger reference {line.LedgerEntryId}; no durable journal association was resolved by this read model.",
                string.Empty,
                FinancialRecordExplorerTone.Info));
        }
        relationships.Add(new(
            "approval",
            "Approval",
            HasText(approvalHref)
                ? $"Approval {line.ApprovalId} is retained as line evidence."
                : "No line approval reference was retained.",
            approvalHref,
            HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "delivery-history",
            "Delivery history",
            "Report-pack deliveries carry this line's publication and evidence packet history.",
            deliveryHistoryHref,
            HasText(deliveryGraphHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "evidence-audit-links",
            "Evidence and audit links",
            $"Evidence id {line.EvidenceId} anchors the retained audit packet for this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "evidence",
            "Evidence",
            HasText(auditHref)
                ? $"Evidence id {line.EvidenceId} anchors the report-line proof packet."
                : "No retained evidence packet is linked to this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "audit-event",
            "Audit event",
            HasText(auditHref)
                ? $"Audit trail retains evidence id {line.EvidenceId} for this report line."
                : "No retained audit event is linked to this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "restatement-evidence",
            "Restatement evidence",
            restatementLineCount > 0
                ? "Restatement evidence exists for this report line."
                : "No restatement evidence is retained for this report line.",
            restatementHref,
            restatementLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default));

        return relationships;
    }

    private static string BuildReportLineSourceHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.FinancialRecordHref))
        {
            return line.FinancialRecordHref!.Trim();
        }

        if (HasText(line.FinancialRecordExplorerId))
        {
            var route = UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationFinancialRecordExplorer,
                "explorerId",
                line.FinancialRecordExplorerId!);
            var query = $"lineKey={Uri.EscapeDataString(line.LineKey)}&sourceId={Uri.EscapeDataString(line.SourceId)}&evidenceId={Uri.EscapeDataString(line.EvidenceId)}";
            return UiApiRoutes.WithQuery(route, query);
        }

        if (HasText(line.RunId))
        {
            return UiApiRoutes.WithParam(UiApiRoutes.RunsLedger, "runId", line.RunId!);
        }

        return BuildEvidenceSubjectRoute(
            UiApiRoutes.WorkstationEvidenceSubjectPacket,
            "report-line",
            $"{line.SourceKind}:{line.SourceId}:{line.LineKey}");
    }

    private static string BuildReportLineInstrumentHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.SecurityMasterId))
        {
            return UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationSecurityMasterById,
                "securityId",
                line.SecurityMasterId!);
        }

        return HasText(line.SecurityDefinitionId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "security-definition",
                line.SecurityDefinitionId!)
            : string.Empty;
    }

    private static string BuildReportLineActivityHref(ReportPackLineProvenanceDto line, string sourceHref)
    {
        if (HasText(line.ProviderEventId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "provider-event",
                line.ProviderEventId!);
        }

        if (HasText(line.SourceSessionId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "source-session",
                line.SourceSessionId!);
        }

        return sourceHref;
    }

    private static string BuildReportLineAuditHref(ReportPackLineProvenanceDto line)
        => HasText(line.EvidenceId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "report-line",
                line.EvidenceId)
            : string.Empty;

    private static string BuildReportLineInstrumentLabel(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.SecurityMasterId))
        {
            return line.SecurityMasterId!.Trim();
        }

        return HasText(line.SecurityDefinitionId)
            ? line.SecurityDefinitionId!.Trim()
            : "No instrument retained";
    }

    private static string BuildReportLineActivityLabel(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.ProviderEventId))
        {
            return line.ProviderEventId!.Trim();
        }

        if (HasText(line.SourceSessionId))
        {
            return line.SourceSessionId!.Trim();
        }

        return $"{line.SourceKind}:{line.SourceId}";
    }

    private static string BuildReportLineReconciliationHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.ReconciliationRunId))
        {
            return UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationRunById,
                "reconciliationRunId",
                line.ReconciliationRunId!);
        }

        return HasText(line.ReconciliationCaseId)
            ? UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationBreakQueueById,
                "breakId",
                line.ReconciliationCaseId!)
            : string.Empty;
    }

    private static string BuildReportLineApprovalHref(ReportPackLineProvenanceDto line)
        => HasText(line.ApprovalId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "approval",
                line.ApprovalId!)
            : string.Empty;

    private static string BuildReportLineDeliveryGraphHref(Guid reportId, Guid attemptId)
        => BuildEvidenceSubjectRoute(
            UiApiRoutes.WorkstationEvidenceSubjectGraph,
            "report-pack-delivery",
            $"{reportId:D}:{attemptId:D}");

    private static string BuildReportLineRestatementHref(
        ReportPackWorkflowRecordDto record,
        IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        var route = changedLines
            .SelectMany(static line => line.EvidenceLinks ?? [])
            .Select(static link => link.Route)
            .FirstOrDefault(ReportingDeliveryReadModelSecurity.IsSafeRetainedEvidenceHref);
        if (HasText(route))
        {
            return route!.Trim();
        }

        return changedLines.Count > 0
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "report-pack-restatement",
                $"{record.ReportId:D}:{changedLines[0].LineKey}")
            : string.Empty;
    }

    private static string BuildEvidenceSubjectRoute(string route, string subjectKind, string subjectId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(route, "subjectKind", subjectKind),
            "subjectId",
            subjectId);

    private static string BuildReportLineReconciliationLabel(ReportPackLineProvenanceDto line)
        => HasText(line.ReconciliationOutcome)
            ? line.ReconciliationOutcome!.Trim()
            : HasText(line.ReconciliationRunId)
                ? line.ReconciliationRunId!.Trim()
                : EmptyFallback(line.ReconciliationCaseId, "No reconciliation");

    private static bool IsAuthoritativelyReported(ReportPackWorkflowRecordDto record)
    {
        if (record.State is not (ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated) ||
            record.Publication is not { } publication ||
            record.LineProvenance is not { Count: > 0 })
        {
            return false;
        }

        var publicationIsComplete =
            HasText(publication.ManifestId) &&
            HasText(publication.RetainedManifestPath) &&
            HasText(publication.EvidenceHash) &&
            HasText(publication.SignedOffBy) &&
            HasText(publication.SignedOffRole) &&
            HasText(publication.SignOffContext) &&
            publication.SignedOffAt != default &&
            Meridian.Contracts.Operations.OperationsOriginGuard.IsHumanOperator(publication.ActionOrigin) &&
            HasCompleteReportEvidence(publication.EvidenceLinks) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Approved &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Published &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.LineProvenance.All(line => EnumerateReportLineEvidencePointers(line)
                .All(pointer => publication.EvidenceLinks.Any(link =>
                    string.Equals(link.EvidenceId, pointer, StringComparison.OrdinalIgnoreCase))));
        if (!publicationIsComplete)
        {
            return false;
        }

        if (record.State == ReportPackWorkflowStateDto.Published)
        {
            return true;
        }

        return record.Restatement is { } restatement &&
               HasText(restatement.ReasonCode) &&
               HasText(restatement.Approver) &&
               restatement.PriorVersionReportId != Guid.Empty &&
               restatement.ChangedLines.Count > 0 &&
               restatement.ChangedLines.All(static line =>
                   HasText(line.LineKey) &&
                   HasText(line.PreviousValue) &&
                   HasText(line.CurrentValue) &&
                   HasCompleteReportEvidence(line.EvidenceLinks)) &&
               HasCompleteReportEvidence(restatement.EvidenceLinks) &&
               record.AuditTrail.Any(static audit =>
                   audit.ToState == ReportPackWorkflowStateDto.Restated &&
                   HasText(audit.Actor) &&
                   audit.At != default);
    }

    private static bool HasCompleteReportEvidence(IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
        => evidenceLinks is { Count: > 0 } &&
           evidenceLinks.All(static evidence =>
               HasText(evidence.EvidenceId) &&
               HasText(evidence.Label) &&
               HasText(evidence.Source));

    private static IEnumerable<string> EnumerateReportLineEvidencePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.EvidenceId;
        yield return line.SourceId;

        foreach (var pointer in new[]
                 {
                     line.RunId,
                     line.SourceSessionId,
                     line.LedgerEntryId,
                     line.ReconciliationCaseId,
                     line.ReconciliationRunId,
                     line.ProviderEventId,
                     line.SecurityMasterId,
                     line.SecurityDefinitionId,
                     line.ApprovalId
                 })
        {
            if (HasText(pointer))
            {
                yield return pointer!;
            }
        }
    }

}
