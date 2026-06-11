using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed class PrivateCapitalCloseCockpitService : IPrivateCapitalCloseCockpitService
{
    private const decimal ShadowNavTieOutTolerance = 0.01m;

    private static readonly IReadOnlyList<string> LiveCapabilities =
    [
        "Fund/book/period close lane projection from operations continuity workflows",
        "Private-capital journal, capital-account tie-out, report, delivery, and evidence posture",
        "Close-package manifest and period-lock readiness from retained workflow evidence",
        "Approval history from workflow decisions and checklist-control approvals",
        "NAV support packages with positions, cash, pricing, shadow NAV evidence, and administrator-versus-Meridian tie-out evidence",
        "Management-company operating records for expenses, fees, intercompany, budget, cash-plan, reimbursement, and bank/card evidence"
    ];

    private static readonly IReadOnlyList<string> PlannedCapabilities =
    [
        "Native external investor portal delivery",
        "Live payment release",
        "Tax filing workflow execution"
    ];

    private readonly IManualJournalEntryWorkbenchService? _manualJournalEntryWorkbenchService;
    private readonly IOperationsContinuityWorkflowService? _operationsContinuityWorkflowService;

    public PrivateCapitalCloseCockpitService(
        IManualJournalEntryWorkbenchService? manualJournalEntryWorkbenchService,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService)
    {
        _manualJournalEntryWorkbenchService = manualJournalEntryWorkbenchService;
        _operationsContinuityWorkflowService = operationsContinuityWorkflowService;
    }

    public async Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        Guid? fundAccountId = null,
        string? periodId = null,
        string? entityId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var activity = _manualJournalEntryWorkbenchService is null
            ? null
            : await _manualJournalEntryWorkbenchService
                .GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct)
                .ConfigureAwait(false);
        var workflows = await LoadWorkflowsAsync(fundAccountId, periodId, ct).ConfigureAwait(false);
        var records = FilterFundEventRecords(activity, periodId, entityId);
        var subledgers = FilterSubledgers(activity, records);
        var reportOutputs = FilterReportOutputs(activity, records);
        var workflowRows = workflows.Select(ToWorkflowRow).ToArray();
        var blockers = workflows
            .SelectMany(static workflow => workflow.CloseReadiness?.Blockers ?? [])
            .DistinctBy(static blocker => blocker.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nextActions = workflows
            .SelectMany(static workflow => workflow.CloseReadiness?.NextActions ?? workflow.NextActions)
            .DistinctBy(static action => action.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lanes = BuildLanes(activity, workflows, records, subledgers, reportOutputs);
        var approvalHistory = BuildApprovalHistory(workflows);
        var navSupportPackages = BuildNavSupportPackages(workflows, records, reportOutputs);
        var overallStatus = ResolveOverallStatus(lanes);
        var readinessScore = ResolveReadinessScore(workflows, lanes);

        return new PrivateCapitalCloseCockpitDto(
            FundProfileId: Normalize(activity?.FundProfileId) ?? Normalize(fundProfileId),
            LedgerBookId: activity?.LedgerBookId ?? ledgerBookId,
            FundAccountId: fundAccountId,
            PeriodId: Normalize(periodId),
            EntityId: Normalize(entityId),
            ProjectedAtUtc: activity?.ProjectedAtUtc ?? DateTimeOffset.UtcNow,
            CockpitRoute: BuildCockpitRoute(fundProfileId, ledgerBookId, fundAccountId, periodId, entityId),
            OverallStatus: overallStatus,
            IsReadyToClose: lanes.Count > 0 && lanes.All(static lane => lane.IsReady),
            ReadinessScore: readinessScore,
            WorkflowCount: workflowRows.Length,
            FundEventCount: records.Count,
            CapitalAccountCount: subledgers.Count,
            ReportOutputCount: reportOutputs.Count,
            DeliveredReportOutputCount: reportOutputs.Count(static output =>
                output.IsPublished && !string.IsNullOrWhiteSpace(output.RetainedManifestPath)),
            ReadyLaneCount: lanes.Count(static lane => lane.IsReady),
            BlockedLaneCount: lanes.Count(static lane => lane.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing),
            Lanes: lanes,
            Workflows: workflowRows,
            Blockers: blockers,
            NextActions: nextActions,
            LiveCapabilities: LiveCapabilities,
            PlannedCapabilities: PlannedCapabilities,
            ApprovalHistory: approvalHistory,
            NavSupportPackages: navSupportPackages);
    }

    private async Task<IReadOnlyList<OperationsContinuityWorkflowDto>> LoadWorkflowsAsync(
        Guid? fundAccountId,
        string? periodId,
        CancellationToken ct)
    {
        if (_operationsContinuityWorkflowService is null)
        {
            return [];
        }

        var summaries = await _operationsContinuityWorkflowService
            .ListAsync(fundAccountId, Normalize(periodId), status: null, ct)
            .ConfigureAwait(false);
        var workflows = new List<OperationsContinuityWorkflowDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            ct.ThrowIfCancellationRequested();
            var workflow = await _operationsContinuityWorkflowService
                .GetAsync(summary.WorkflowId, ct)
                .ConfigureAwait(false);
            if (workflow is not null)
            {
                workflows.Add(workflow);
            }
        }

        return workflows;
    }

    private static IReadOnlyList<PrivateCapitalCloseCockpitLaneDto> BuildLanes(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        return
        [
            BuildDataReceiptLane(activity, workflows, records),
            BuildReconciliationLane(workflows, records),
            BuildJournalLane(workflows, records),
            BuildCapitalAccountLane(activity, records, subledgers),
            BuildPartnerCapitalTieOutLane(activity, records, subledgers, reportOutputs),
            BuildExpenseFeeAllocationLane(activity, records, subledgers),
            BuildManagementCompanyOperationsLane(activity, records, subledgers),
            BuildNavSupportLane(workflows, records, reportOutputs),
            BuildValuationEvidenceLane(workflows, records),
            BuildReportingLane(workflows, reportOutputs),
            BuildDeliveryLane(reportOutputs),
            BuildClosePackageLane(workflows),
            BuildPeriodLockLane(workflows)
        ];
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildDataReceiptLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var hasPassedGate = HasPassedGate(workflows, OperationsGateKeyDto.BrokerIngest);
        var isReady = hasPassedGate || records.Count > 0;
        return Lane(
            "data-receipt",
            "Data receipt",
            isReady ? EvidenceStatusDto.Ready : MissingWhenNoSources(activity, workflows),
            isReady,
            isReady
                ? $"{records.Count} fund event(s) and {workflows.Count} close workflow(s) are available for the close scope."
                : "No retained private-capital activity or broker/source intake workflow is available for this close scope.",
            UiApiRoutes.OperationsContinuity,
            WorkflowsEvidence(workflows),
            "Retain source intake evidence for the close scope");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildReconciliationLane(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var hasReadyComponent = HasReadyComponent(workflows, "reconciliation");
        var hasValidationBlockers = records.Any(static record => record.ValidationIssueCount > 0);
        var isReady = hasReadyComponent && !hasValidationBlockers;
        return Lane(
            "reconciliation",
            "Reconciliation",
            ResolveComponentStatus(workflows, "reconciliation", isReady),
            isReady,
            isReady
                ? "Reconciliation readiness is clear for the retained close workflow."
                : "Reconciliation blockers or private-capital validation issues remain in the close scope.",
            FirstRoute(workflows, "reconciliation") ?? UiApiRoutes.ReconciliationOpenCases,
            WorkflowsEvidence(workflows).Concat(RecordEvidence(records, "Reconciliation evidence")).ToArray(),
            "Clear reconciliation blockers before close");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildJournalLane(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var postedRecords = records.Count(static record => record.IsPosted && record.LedgerImpactCount > 0);
        var isReady = postedRecords > 0 || HasReadyComponent(workflows, "ledger");
        return Lane(
            "journal-posting",
            "Journals",
            ResolveComponentStatus(workflows, "ledger", isReady),
            isReady,
            isReady
                ? $"{postedRecords} private-capital fund event(s) have posted ledger impact, or the close ledger component is ready."
                : "Posted journals or the close ledger readiness component are missing.",
            records.FirstOrDefault(static record => record.IsPosted)?.ActivityRoute ?? FirstRoute(workflows, "ledger"),
            RecordEvidence(records, "Journal evidence"),
            "Post or validate close journals");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildCapitalAccountLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
    {
        var isReady = subledgers.Count > 0 &&
                      records.Count > 0 &&
                      records.All(static record => record.CapitalAccountSubledgerEntryCount > 0);
        return Lane(
            "capital-accounts",
            "Capital accounts",
            isReady ? EvidenceStatusDto.Ready : MissingWhenNoSources(activity, []),
            isReady,
            isReady
                ? $"{subledgers.Count} capital-account subledger projection(s) tie to retained fund events."
                : "Capital-account roll-forward support is missing or incomplete for the close scope.",
            activity is null
                ? null
                : BuildCapitalAccountWorkbenchRoute(activity.FundProfileId, activity.LedgerBookId),
            RecordEvidence(records, "Capital-account evidence"),
            "Tie fund events to capital-account roll-forward support");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildPartnerCapitalTieOutLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var hasSources = records.Count > 0 || subledgers.Count > 0;
        var hasTieOutBlocker = subledgers.Any(HasPartnerCapitalTieOutBlocker) ||
                               records.Any(static record => record.ValidationIssueCount > 0);
        var isReady = records.Count > 0 &&
                      subledgers.Count > 0 &&
                      subledgers.All(IsPartnerCapitalTieOutReady) &&
                      records.All(static record =>
                          record.IsPosted &&
                          record.CapitalAccountSubledgerEntryCount > 0 &&
                          record.LedgerImpactCount > 0);
        var status = isReady
            ? EvidenceStatusDto.Ready
            : hasTieOutBlocker
                ? EvidenceStatusDto.Blocked
                : MissingWhenNoSources(activity, []);

        return Lane(
            "partner-capital-tie-outs",
            "Partner capital account tie-outs",
            status,
            isReady,
            isReady
                ? $"{subledgers.Count} partner capital account tie-out(s) reconcile fund events, subledger roll-forward, ledger impact, and retained statement evidence."
                : hasSources
                    ? "Partner capital account tie-out support is missing, mismatched, or lacks retained statement evidence."
                    : "No partner capital account tie-out support is available for this close scope.",
            subledgers.Select(static subledger => subledger.ActivityRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            SubledgerEvidence(subledgers, "Partner capital tie-out evidence")
                .Concat(ReportEvidence(reportOutputs))
                .ToArray(),
            "Retain partner capital tie-out evidence across subledger, ledger, and statement output");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildExpenseFeeAllocationLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
    {
        var reviewRecords = records
            .Where(IsExpenseFeeAllocationRecord)
            .ToArray();
        var hasAllocationEvidence = HasAllocationEvidence(subledgers);
        var hasReviewEvidence = reviewRecords.Any() &&
                                reviewRecords.All(static record =>
                                    record.IsPosted &&
                                    record.ValidationIssueCount == 0 &&
                                    record.EvidenceLinkCount > 0 &&
                                    record.CapitalAccountSubledgerEntryCount > 0);
        var isReady = hasReviewEvidence && hasAllocationEvidence;
        var status = isReady
            ? EvidenceStatusDto.Ready
            : reviewRecords.Any(static record => record.ValidationIssueCount > 0)
                ? EvidenceStatusDto.Blocked
                : MissingWhenNoSources(activity, []);

        return Lane(
            "expense-fee-allocation",
            "Expense, fee, and allocation review",
            status,
            isReady,
            isReady
                ? $"{reviewRecords.Length} expense/fee fund event(s) have posted journals, capital-account allocation support, and retained evidence."
                : "Expense, fee, or allocation-review support is missing or incomplete for the close scope.",
            reviewRecords
                .Select(static record => record.NextActionRoute ?? record.ActivityRoute ?? record.EvidenceRoute)
                .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route))
                ?? subledgers.Select(static subledger => subledger.ActivityRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            RecordEvidence(reviewRecords, "Expense, fee, and allocation evidence")
                .Concat(SubledgerEvidence(subledgers, "Allocation-rule evidence"))
                .ToArray(),
            "Retain posted expense, fee, and allocation review evidence");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildManagementCompanyOperationsLane(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
    {
        var managementRecords = records
            .Where(IsManagementCompanyOperatingRecord)
            .ToArray();
        var values = BuildManagementCompanyEvidenceValues(records, subledgers).ToArray();
        var signals = new[]
        {
            EvidenceSignal("expense allocation", ContainsManagementCompanyToken(values, "allocation", "expense allocation")),
            EvidenceSignal("management fee", ContainsManagementCompanyToken(values, "management fee", "managementfee", "management fees")),
            EvidenceSignal("intercompany balance", ContainsManagementCompanyToken(values, "intercompany", "due to", "due from", "due-to", "due-from")),
            EvidenceSignal("bank/card evidence", ContainsManagementCompanyToken(values, "bank", "card", "bank card", "bankcard")),
            EvidenceSignal("budget or cash-plan snapshot", ContainsManagementCompanyToken(values, "budget", "cash plan", "cash-plan", "cashplan")),
            EvidenceSignal("reimbursement evidence", ContainsManagementCompanyToken(values, "reimbursement", "reimbursements", "reimburse"))
        };
        var completeSignals = signals.Count(static signal => signal.IsPresent);
        var hasSource = managementRecords.Length > 0 || completeSignals > 0;
        var hasValidationBlocker = managementRecords.Any(static record => record.ValidationIssueCount > 0);
        var isReady = hasSource && !hasValidationBlocker && completeSignals == signals.Length;
        var status = isReady
            ? EvidenceStatusDto.Ready
            : hasValidationBlocker
                ? EvidenceStatusDto.Blocked
                : MissingWhenNoSources(activity, []);
        var missing = signals
            .Where(static signal => !signal.IsPresent)
            .Select(static signal => signal.Label)
            .ToArray();
        var evidence = RecordEvidence(managementRecords, "Management-company operating evidence")
            .Concat(SubledgerEvidence(subledgers, "Management-company support evidence"))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Lane(
            "management-company-operations",
            "Management company operating records",
            status,
            isReady,
            isReady
                ? $"{managementRecords.Length} management-company operating record(s) retain expense allocation, intercompany, management-fee, bank/card, budget or cash-plan, and reimbursement evidence."
                : hasSource
                    ? $"Management-company operating support is incomplete; missing {string.Join(", ", missing)}."
                    : "No retained management-company operating records are linked to this close scope.",
            managementRecords
                .Select(static record => record.NextActionRoute ?? record.ActivityRoute ?? record.EvidenceRoute)
                .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route))
                ?? subledgers.Select(static subledger => subledger.ActivityRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            evidence,
            "Retain management-company operating evidence for expense allocations, intercompany balances, fees, bank/card support, budget or cash-plan snapshots, and reimbursements");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildNavSupportLane(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var required = new[] { "positions", "cash", "pricing" };
        var shadowNavOutputs = reportOutputs
            .Where(IsShadowNavReportOutput)
            .ToArray();
        var administratorNavOutputs = reportOutputs
            .Where(IsAdministratorNavReportOutput)
            .ToArray();
        var tieOut = BuildShadowNavTieOut(shadowNavOutputs, administratorNavOutputs, records);
        var componentsReady = required.All(component => HasReadyComponent(workflows, component)) &&
                              records.All(static record => record.ValidationIssueCount == 0);
        var isReady = componentsReady && tieOut.IsReady;
        var componentStatus = ResolveComponentStatus(workflows, required, componentsReady);
        var status = componentsReady ? tieOut.Status : componentStatus;
        return Lane(
            "nav-support",
            "NAV support",
            status,
            isReady,
            isReady
                ? "Position, cash, pricing, shadow NAV, and administrator NAV tie-out support are clear for close readiness."
                : componentsReady
                    ? tieOut.Summary
                    : "NAV support waits on position, cash, pricing, or private-capital validation evidence.",
            FirstRoute(workflows, required) ?? tieOut.Route,
            WorkflowsEvidence(workflows).Concat(tieOut.EvidenceLinks)
                .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            isReady
                ? "Retain NAV support for positions, cash, pricing, shadow NAV, and administrator NAV tie-out evidence."
                : tieOut.RequiredActions.Count > 0
                    ? tieOut.RequiredActions[0]
                    : "Retain NAV support for positions, cash, pricing, shadow NAV, and administrator NAV tie-out evidence.");
    }

    private static IReadOnlyList<PrivateCapitalNavSupportPackageDto> BuildNavSupportPackages(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var required = new[] { "positions", "cash", "pricing" };
        var shadowNavOutputs = reportOutputs
            .Where(IsShadowNavReportOutput)
            .ToArray();
        var administratorNavOutputs = reportOutputs
            .Where(IsAdministratorNavReportOutput)
            .ToArray();
        var tieOut = BuildShadowNavTieOut(shadowNavOutputs, administratorNavOutputs, records);
        var components = required
            .Select(key => BuildNavSupportComponent(workflows, key, splitLabel: true))
            .Append(BuildShadowNavSupportComponent(workflows, shadowNavOutputs))
            .Append(BuildShadowNavTieOutComponent(tieOut))
            .ToArray();
        var shadowNavReady = components.Any(static component =>
            component.ComponentId == "shadow-nav" && component.IsReady);
        var tieOutReady = tieOut.IsReady;
        var recordsClear = records.All(static record => record.ValidationIssueCount == 0);
        var isReady = components.All(static component => component.IsReady) && shadowNavReady && tieOutReady && recordsClear;
        var status = ResolveNavSupportPackageStatus(workflows, records, reportOutputs, components, isReady);
        var evidence = WorkflowsEvidence(workflows)
            .Concat(RecordEvidence(records, "NAV support evidence"))
            .Concat(ReportEvidence(shadowNavOutputs))
            .Concat(ReportEvidence(administratorNavOutputs))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var route = FirstRoute(workflows, required) ??
                    tieOut.Route ??
                    FirstReportRoute(shadowNavOutputs) ??
                    FirstReportRoute(administratorNavOutputs) ??
                    records.Select(static record => record.PrimaryReportRoute ?? record.NextActionRoute ?? record.EvidenceRoute ?? record.ActivityRoute)
                        .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route));
        var periodId = workflows.Select(static workflow => workflow.PeriodId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ??
                       records.Select(static record => record.EffectiveDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).FirstOrDefault() ??
                       "all-periods";
        var fundAccountId = workflows.Select(static workflow => workflow.FundAccountId.ToString("D")).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ??
                            "all-accounts";

        return
        [
            new PrivateCapitalNavSupportPackageDto(
                $"nav-support:{fundAccountId}:{periodId}",
                "NAV support package",
                status,
                isReady,
                isReady
                    ? "NAV support package retains position, cash, pricing, shadow NAV, and administrator NAV tie-out evidence for close review."
                    : shadowNavReady
                        ? "NAV support package has shadow NAV evidence but still waits on administrator NAV tie-out, position, cash, pricing, or validation readiness."
                        : "NAV support package is missing retained shadow NAV, administrator NAV tie-out, or required close-readiness components.",
                Normalize(route),
                tieOut.MeridianShadowNav ?? EstimateShadowNav(records),
                ResolveNavCurrency(records),
                evidence.Length,
                evidence,
                components,
                isReady ? [] : tieOut.RequiredActions.Count > 0
                    ? tieOut.RequiredActions
                    : ["Retain NAV support package for positions, cash, pricing, shadow NAV, and administrator NAV tie-out evidence."],
                tieOut)
        ];
    }

    private static PrivateCapitalNavSupportComponentDto BuildNavSupportComponent(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        string key,
        bool splitLabel)
    {
        var component = FindCloseReadinessComponent(workflows, key);
        var isReady = component?.IsReady == true;
        return new PrivateCapitalNavSupportComponentDto(
            key,
            splitLabel ? SplitComponentLabel(key) : component?.Label ?? key,
            ResolveComponentStatus(workflows, key, isReady),
            isReady,
            component?.BlockingReason ?? (isReady
                ? $"{component?.Label ?? SplitComponentLabel(key)} support is ready for NAV support."
                : $"{component?.Label ?? SplitComponentLabel(key)} support is missing from close readiness."),
            Normalize(component?.RouteHint),
            component?.Score ?? (isReady ? 100 : 0));
    }

    private static PrivateCapitalNavSupportComponentDto BuildShadowNavSupportComponent(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalReportOutputDto> shadowNavOutputs)
    {
        var readyOutput = shadowNavOutputs.FirstOrDefault(static output =>
            output.IsReportReady &&
            output.EvidenceLinkCount > 0 &&
            output.ReportLineProvenanceCount > 0);
        var status = readyOutput is not null
            ? EvidenceStatusDto.Ready
            : workflows.Count == 0 && shadowNavOutputs.Count == 0
                ? EvidenceStatusDto.Missing
                : EvidenceStatusDto.ReviewRequired;

        return new PrivateCapitalNavSupportComponentDto(
            "shadow-nav",
            "Shadow NAV",
            status,
            readyOutput is not null,
            readyOutput is not null
                ? "Shadow NAV report output retains evidence and line provenance."
                : "Shadow NAV report output evidence or line provenance is missing.",
            FirstReportRoute(shadowNavOutputs),
            readyOutput is not null ? 100 : 0);
    }

    private static PrivateCapitalNavSupportComponentDto BuildShadowNavTieOutComponent(PrivateCapitalShadowNavTieOutDto tieOut)
        => new(
            "administrator-nav-tie-out",
            "Administrator NAV tie-out",
            tieOut.Status,
            tieOut.IsReady,
            tieOut.Summary,
            tieOut.Route,
            tieOut.IsReady ? 100 : 0);

    private static PrivateCapitalShadowNavTieOutDto BuildShadowNavTieOut(
        IReadOnlyList<PrivateCapitalReportOutputDto> shadowNavOutputs,
        IReadOnlyList<PrivateCapitalReportOutputDto> administratorNavOutputs,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var meridianShadowNav = ResolveReportOutputAmount(shadowNavOutputs) ?? EstimateShadowNav(records);
        var administratorNav = ResolveReportOutputAmount(administratorNavOutputs);
        var variance = meridianShadowNav.HasValue && administratorNav.HasValue
            ? meridianShadowNav.Value - administratorNav.Value
            : (decimal?)null;
        var evidence = ReportEvidence(shadowNavOutputs)
            .Concat(ReportEvidence(administratorNavOutputs))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var route = FirstReportRoute(administratorNavOutputs) ?? FirstReportRoute(shadowNavOutputs);
        var currency = ResolveNavCurrency(records) ??
                       shadowNavOutputs.Concat(administratorNavOutputs)
                           .Select(static output => Normalize(output.Currency))
                           .FirstOrDefault(static value => value is not null);
        var status = ResolveShadowNavTieOutStatus(shadowNavOutputs, administratorNavOutputs, variance);
        var requiredActions = BuildShadowNavTieOutRequiredActions(status, shadowNavOutputs, administratorNavOutputs, variance);

        return new PrivateCapitalShadowNavTieOutDto(
            TieOutId: BuildShadowNavTieOutId(records, shadowNavOutputs, administratorNavOutputs),
            Status: status,
            IsReady: status == EvidenceStatusDto.Ready,
            MeridianShadowNav: meridianShadowNav,
            AdministratorNav: administratorNav,
            Variance: variance,
            Tolerance: ShadowNavTieOutTolerance,
            Currency: currency,
            Summary: BuildShadowNavTieOutSummary(status, meridianShadowNav, administratorNav, variance),
            Route: Normalize(route),
            EvidenceLinkCount: evidence.Length,
            EvidenceLinks: evidence,
            RequiredActions: requiredActions);
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildValuationEvidenceLane(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var required = new[] { "security-master", "pricing" };
        var hasEventEvidence = records.Any(static record =>
            record.EvidenceCategories.Any(static category => category.IsReady));
        var isReady = required.All(component => HasReadyComponent(workflows, component)) || hasEventEvidence;
        return Lane(
            "valuation-evidence",
            "Valuation evidence",
            ResolveComponentStatus(workflows, required, isReady),
            isReady,
            isReady
                ? "Security Master, pricing, or retained fund-event evidence supports valuation review."
                : "Valuation support is missing for the close scope.",
            FirstRoute(workflows, required) ?? records.FirstOrDefault()?.EvidenceRoute,
            RecordEvidence(records, "Valuation evidence"),
            "Attach valuation and Security Master support");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildReportingLane(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var readyOutputs = reportOutputs.Count(static output => output.IsReportReady);
        var isReady = readyOutputs > 0 || HasReadyComponent(workflows, "reports");
        return Lane(
            "reporting",
            "Reporting",
            ResolveComponentStatus(workflows, "reports", isReady),
            isReady,
            isReady
                ? $"{readyOutputs} governed report output(s) are ready, or the close report component is ready."
                : "Governed report output is missing or still blocked.",
            reportOutputs.FirstOrDefault(static output => output.IsReportReady)?.ReportOutputRoute ??
                reportOutputs.FirstOrDefault()?.ReportRoute ??
                FirstRoute(workflows, "reports"),
            ReportEvidence(reportOutputs),
            "Generate and approve close report outputs");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildDeliveryLane(IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var deliveredOutputs = reportOutputs
            .Where(static output => output.IsPublished && !string.IsNullOrWhiteSpace(output.RetainedManifestPath))
            .ToArray();
        var isReady = deliveredOutputs.Length > 0;
        return Lane(
            "delivery",
            "Delivery",
            isReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Missing,
            isReady,
            isReady
                ? $"{deliveredOutputs.Length} published report output(s) retain delivery or manifest evidence."
                : "No retained delivery package or publication manifest is linked to this close scope.",
            deliveredOutputs.Select(static output => output.ReportOutputRoute ?? output.ReportRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            ReportEvidence(deliveredOutputs),
            "Retain report-package delivery evidence");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildClosePackageLane(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
    {
        var packages = workflows
            .Select(static workflow => workflow.ClosePackage)
            .Where(static package => package is not null)
            .Select(static package => package!)
            .ToArray();
        var isReady = packages.Any(static package =>
            !string.IsNullOrWhiteSpace(package.RetainedManifestId) &&
            !string.IsNullOrWhiteSpace(package.EvidenceHash));
        return Lane(
            "close-package",
            "Close package",
            isReady ? EvidenceStatusDto.Ready : MissingWhenNoSources(null, workflows),
            isReady,
            isReady
                ? $"{packages.Length} close package(s) retain manifests and evidence hashes."
                : "A signed close package with retained manifest and evidence hash is not available.",
            packages.Select(static package => package.RetainedManifestRoute).FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route)),
            packages.SelectMany(static package => package.EvidenceLinks).ToArray(),
            "Publish the close package manifest");
    }

    private static PrivateCapitalCloseCockpitLaneDto BuildPeriodLockLane(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
    {
        var closedWorkflows = workflows
            .Where(static workflow => workflow.Status == OperationsWorkflowStatusDto.Closed)
            .ToArray();
        var isReady = closedWorkflows.Length > 0 &&
                      closedWorkflows.All(static workflow => workflow.ClosePackage is not null);
        return Lane(
            "period-lock",
            "Period lock",
            isReady ? EvidenceStatusDto.Ready : MissingWhenNoSources(null, workflows),
            isReady,
            isReady
                ? $"{closedWorkflows.Length} workflow(s) are closed with retained close-package evidence."
                : "Period lock or reopen evidence is not retained for this close scope.",
            closedWorkflows.Select(static workflow => BuildWorkflowRoute(workflow.WorkflowId)).FirstOrDefault(),
            WorkflowsEvidence(closedWorkflows),
            "Close the workflow and retain period-lock evidence");
    }

    private static PrivateCapitalCloseCockpitLaneDto Lane(
        string laneId,
        string label,
        EvidenceStatusDto status,
        bool isReady,
        string summary,
        string? route,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        string requiredAction)
        => new(
            laneId,
            label,
            status,
            isReady,
            summary,
            Normalize(route),
            evidenceLinks.Count,
            evidenceLinks,
            isReady ? [] : [requiredAction]);

    private static IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FilterFundEventRecords(
        PrivateCapitalActivityProjectionDto? activity,
        string? periodId,
        string? entityId)
    {
        if (activity is null)
        {
            return [];
        }

        return activity.FundEventRecords
            .Where(record => MatchesPeriod(record.EffectiveDate, periodId))
            .Where(record => MatchesEntity(record, entityId))
            .ToArray();
    }

    private static IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> FilterSubledgers(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (activity is null || records.Count == 0)
        {
            return [];
        }

        var eventIds = records.Select(static record => record.FundEventId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return activity.CapitalAccountSubledgers
            .Where(subledger => subledger.FundEventRecords.Any(record => eventIds.Contains(record.FundEventId)))
            .ToArray();
    }

    private static IReadOnlyList<PrivateCapitalReportOutputDto> FilterReportOutputs(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (activity is null || records.Count == 0)
        {
            return [];
        }

        var eventIds = records.Select(static record => record.FundEventId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return activity.ReportOutputs
            .Where(output => eventIds.Contains(output.FundEventId))
            .ToArray();
    }

    private static bool MatchesPeriod(DateOnly effectiveDate, string? periodId)
    {
        var normalized = Normalize(periodId);
        return normalized is null ||
               string.Equals(effectiveDate.ToString("yyyy-MM", CultureInfo.InvariantCulture), normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntity(PrivateCapitalFundEventLedgerRecordDto record, string? entityId)
    {
        var normalized = Normalize(entityId);
        return normalized is null ||
               record.LedgerImpacts
                   .SelectMany(static impact => impact.Lines)
                   .Any(line => string.Equals(line.EntityId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPassedGate(IReadOnlyList<OperationsContinuityWorkflowDto> workflows, OperationsGateKeyDto gate)
        => workflows.Any(workflow =>
            workflow.Gates.Any(item => item.GateKey == gate && item.Status == OperationsGateStatusDto.Passed));

    private static bool HasReadyComponent(IReadOnlyList<OperationsContinuityWorkflowDto> workflows, string componentKey)
        => workflows.Any(workflow =>
            workflow.CloseReadiness?.Components.Any(component =>
                string.Equals(component.Key, componentKey, StringComparison.OrdinalIgnoreCase) &&
                component.IsReady) == true);

    private static OperationsCloseReadinessComponentDto? FindCloseReadinessComponent(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        string componentKey)
        => workflows
            .SelectMany(static workflow => workflow.CloseReadiness?.Components ?? [])
            .FirstOrDefault(component => string.Equals(component.Key, componentKey, StringComparison.OrdinalIgnoreCase));

    private static EvidenceStatusDto ResolveComponentStatus(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        string componentKey,
        bool isReady)
        => ResolveComponentStatus(workflows, [componentKey], isReady);

    private static EvidenceStatusDto ResolveComponentStatus(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<string> componentKeys,
        bool isReady)
    {
        if (isReady)
        {
            return EvidenceStatusDto.Ready;
        }

        if (workflows.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        var blockers = workflows
            .SelectMany(static workflow => workflow.CloseReadiness?.Blockers ?? [])
            .ToArray();
        return blockers.Any(blocker => componentKeys.Any(key =>
                string.Equals(blocker.Category, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(blocker.RouteHint?.Trim('/').Split('/').LastOrDefault(), key, StringComparison.OrdinalIgnoreCase)))
            ? EvidenceStatusDto.Blocked
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto MissingWhenNoSources(
        PrivateCapitalActivityProjectionDto? activity,
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        => activity is null && workflows.Count == 0
            ? EvidenceStatusDto.Missing
            : EvidenceStatusDto.ReviewRequired;

    private static EvidenceStatusDto ResolveNavSupportPackageStatus(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        IReadOnlyList<PrivateCapitalNavSupportComponentDto> components,
        bool isReady)
    {
        if (isReady)
        {
            return EvidenceStatusDto.Ready;
        }

        if (workflows.Count == 0 && records.Count == 0 && reportOutputs.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        return records.Any(static record => record.ValidationIssueCount > 0) ||
               components.Any(static component => component.Status == EvidenceStatusDto.Blocked)
            ? EvidenceStatusDto.Blocked
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveOverallStatus(IReadOnlyList<PrivateCapitalCloseCockpitLaneDto> lanes)
    {
        if (lanes.Any(static lane => lane.Status == EvidenceStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (lanes.Any(static lane => lane.Status == EvidenceStatusDto.Missing))
        {
            return EvidenceStatusDto.Missing;
        }

        if (lanes.All(static lane => lane.Status == EvidenceStatusDto.Ready))
        {
            return EvidenceStatusDto.Ready;
        }

        return EvidenceStatusDto.ReviewRequired;
    }

    private static int ResolveReadinessScore(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalCloseCockpitLaneDto> lanes)
    {
        var scoredWorkflows = workflows
            .Where(static workflow => workflow.CloseReadiness is not null)
            .Select(static workflow => workflow.CloseReadiness!.Score)
            .ToArray();
        if (scoredWorkflows.Length > 0)
        {
            return (int)Math.Round(scoredWorkflows.Average(), MidpointRounding.AwayFromZero);
        }

        return lanes.Count == 0
            ? 0
            : (int)Math.Round(lanes.Count(static lane => lane.IsReady) * 100m / lanes.Count, MidpointRounding.AwayFromZero);
    }

    private static string? FirstRoute(IReadOnlyList<OperationsContinuityWorkflowDto> workflows, string componentKey)
        => FirstRoute(workflows, [componentKey]);

    private static string? FirstRoute(IReadOnlyList<OperationsContinuityWorkflowDto> workflows, IReadOnlyList<string> componentKeys)
        => workflows
            .SelectMany(static workflow => workflow.CloseReadiness?.Components ?? [])
            .Where(component => componentKeys.Any(key => string.Equals(component.Key, key, StringComparison.OrdinalIgnoreCase)))
            .Select(static component => component.RouteHint)
            .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route));

    private static string? FirstReportRoute(IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
        => reportOutputs
            .Select(static output => output.ReportOutputRoute ?? output.EvidenceRoute ?? output.ReportRoute)
            .FirstOrDefault(static route => !string.IsNullOrWhiteSpace(route));

    private static IReadOnlyList<OperationsEvidenceLinkDto> WorkflowsEvidence(IEnumerable<OperationsContinuityWorkflowDto> workflows)
        => workflows
            .SelectMany(static workflow => workflow.EvidenceLinks
                .Concat(workflow.ClosePackage?.EvidenceLinks ?? []))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<OperationsEvidenceLinkDto> RecordEvidence(
        IEnumerable<PrivateCapitalFundEventLedgerRecordDto> records,
        string label)
        => records
            .SelectMany(static record => record.EvidenceLinks)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(route => LinkFromRoute(route, label, "private-capital"))
            .ToArray();

    private static IReadOnlyList<OperationsEvidenceLinkDto> SubledgerEvidence(
        IEnumerable<PrivateCapitalCapitalAccountSubledgerDto> subledgers,
        string label)
        => subledgers
            .SelectMany(static subledger => subledger.EvidenceLinks
                .Concat(subledger.EvidenceCategories.SelectMany(static category => category.EvidenceLinks)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(route => LinkFromRoute(route, label, "private-capital"))
            .ToArray();

    private static IReadOnlyList<OperationsEvidenceLinkDto> ReportEvidence(
        IEnumerable<PrivateCapitalReportOutputDto> reportOutputs)
        => reportOutputs
            .SelectMany(static output => output.EvidenceLinks)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(route => LinkFromRoute(route, "Report output evidence", "reporting"))
            .ToArray();

    private static decimal? EstimateShadowNav(IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (records.Count == 0)
        {
            return null;
        }

        return records
            .GroupBy(static record => record.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static record => record.EffectiveDate)
                .ThenByDescending(static record => record.FundEventRecordId, StringComparer.OrdinalIgnoreCase)
                .First()
                .CapitalAccountEndingNetActivity)
            .Sum();
    }

    private static decimal? ResolveReportOutputAmount(IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
    {
        var readyOutputs = reportOutputs
            .Where(static output => output.IsReportReady && output.ReportLineProvenanceCount > 0)
            .ToArray();
        if (readyOutputs.Length > 0)
        {
            return readyOutputs.Sum(static output => output.NetCapitalActivity);
        }

        return reportOutputs.Count == 0
            ? null
            : reportOutputs.Sum(static output => output.NetCapitalActivity);
    }

    private static EvidenceStatusDto ResolveShadowNavTieOutStatus(
        IReadOnlyList<PrivateCapitalReportOutputDto> shadowNavOutputs,
        IReadOnlyList<PrivateCapitalReportOutputDto> administratorNavOutputs,
        decimal? variance)
    {
        if (shadowNavOutputs.Count == 0 && administratorNavOutputs.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (shadowNavOutputs.Count == 0 || administratorNavOutputs.Count == 0 || !variance.HasValue)
        {
            return EvidenceStatusDto.ReviewRequired;
        }

        return Math.Abs(variance.Value) <= ShadowNavTieOutTolerance
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.Blocked;
    }

    private static IReadOnlyList<string> BuildShadowNavTieOutRequiredActions(
        EvidenceStatusDto status,
        IReadOnlyList<PrivateCapitalReportOutputDto> shadowNavOutputs,
        IReadOnlyList<PrivateCapitalReportOutputDto> administratorNavOutputs,
        decimal? variance)
        => status switch
        {
            EvidenceStatusDto.Ready => [],
            EvidenceStatusDto.Blocked => [$"Resolve administrator-versus-Meridian shadow NAV variance of {variance.GetValueOrDefault():N2} before close sign-off."],
            EvidenceStatusDto.Missing => ["Retain Meridian shadow NAV and administrator NAV support evidence before close sign-off."],
            _ when shadowNavOutputs.Count == 0 => ["Retain Meridian shadow NAV support evidence before administrator tie-out."],
            _ when administratorNavOutputs.Count == 0 => ["Retain administrator NAV support evidence before close sign-off."],
            _ => ["Complete administrator-versus-Meridian shadow NAV tie-out evidence before close sign-off."]
        };

    private static string BuildShadowNavTieOutSummary(
        EvidenceStatusDto status,
        decimal? meridianShadowNav,
        decimal? administratorNav,
        decimal? variance)
        => status switch
        {
            EvidenceStatusDto.Ready => $"Administrator NAV ties to Meridian shadow NAV within {ShadowNavTieOutTolerance:N2} tolerance.",
            EvidenceStatusDto.Blocked => $"Administrator NAV variance is {variance.GetValueOrDefault():N2}, above the {ShadowNavTieOutTolerance:N2} tolerance.",
            EvidenceStatusDto.Missing => "No Meridian shadow NAV or administrator NAV tie-out evidence is retained for this close scope.",
            _ when !meridianShadowNav.HasValue => "Meridian shadow NAV evidence is missing from the retained close package.",
            _ when !administratorNav.HasValue => "Administrator NAV evidence is missing from the retained close package.",
            _ => "Administrator-versus-Meridian shadow NAV tie-out needs review before close sign-off."
        };

    private static string BuildShadowNavTieOutId(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalReportOutputDto> shadowNavOutputs,
        IReadOnlyList<PrivateCapitalReportOutputDto> administratorNavOutputs)
    {
        var scope = records
            .Select(static record => $"{record.CapitalAccountId}:{record.EffectiveDate:yyyyMM}")
            .Concat(shadowNavOutputs.Select(static output => output.ReportOutputId))
            .Concat(administratorNavOutputs.Select(static output => output.ReportOutputId))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return $"shadow-nav-tie-out:{Normalize(scope) ?? "close-scope"}";
    }

    private static string? ResolveNavCurrency(IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        var currencies = records
            .Select(static record => Normalize(record.Currency))
            .Where(static currency => currency is not null)
            .Select(static currency => currency!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return currencies.Length switch
        {
            0 => null,
            1 => currencies[0],
            _ => "MULTI"
        };
    }

    private static OperationsEvidenceLinkDto LinkFromRoute(string route, string label, string source)
    {
        var trimmed = Normalize(route) ?? "unresolved";
        var evidenceId = trimmed
            .Trim('/')
            .Replace('/', ':')
            .Replace('?', ':')
            .Replace('&', ':')
            .Replace('=', ':');
        return new OperationsEvidenceLinkDto(
            evidenceId,
            label,
            trimmed,
            source,
            null);
    }

    private static bool HasAllocationEvidence(IEnumerable<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
        => subledgers.Any(static subledger =>
            subledger.EvidenceCategories.Any(static category =>
                category.IsReady &&
                (ContainsFinancialOperationToken(category.CategoryId, "allocation") ||
                 ContainsFinancialOperationToken(category.Label, "allocation"))) ||
            subledger.EvidenceLinks.Any(static link => ContainsFinancialOperationToken(link, "allocation")));

    private static bool IsPartnerCapitalTieOutReady(PrivateCapitalCapitalAccountSubledgerDto subledger)
        => !HasPartnerCapitalTieOutBlocker(subledger) &&
           subledger.CapitalAccount is not null &&
           !string.IsNullOrWhiteSpace(subledger.InvestorId) &&
           subledger.FundEventRecords.Count > 0 &&
           subledger.SubledgerEntries.Count > 0 &&
           subledger.LedgerImpacts.Count > 0 &&
           subledger.ReportOutputs.Any(IsPartnerCapitalStatementReady) &&
           subledger.EvidenceLinkCount > 0 &&
           subledger.FundEventCount == subledger.SubledgerEntries.Select(static entry => entry.FundEventId).Distinct(StringComparer.OrdinalIgnoreCase).Count() &&
           subledger.PostedFundEventCount == subledger.FundEventRecords.Count(static record => record.IsPosted);

    private static bool HasPartnerCapitalTieOutBlocker(PrivateCapitalCapitalAccountSubledgerDto subledger)
        => subledger.ValidationIssueCount > 0 ||
           subledger.ValidationIssues.Count > 0 ||
           !AmountsTie(subledger.EndingNetActivity, subledger.OpeningNetActivity + subledger.NetCapitalActivity) ||
           !AmountsTie(subledger.NetCapitalActivity, subledger.SubledgerEntries.Sum(static entry => entry.NetCapitalActivity)) ||
           (subledger.CapitalAccount is not null && !AmountsTie(subledger.EndingNetActivity, subledger.CapitalAccount.NetActivity)) ||
           subledger.LedgerImpacts.Any(static impact =>
               !impact.IsBalanced ||
               !AmountsTie(impact.Imbalance, 0m) ||
               impact.ValidationIssues.Count > 0);

    private static bool IsPartnerCapitalStatementReady(PrivateCapitalReportOutputDto reportOutput)
        => reportOutput.IsReportReady &&
           reportOutput.EvidenceLinkCount > 0 &&
           reportOutput.ReportLineProvenanceCount > 0 &&
           !string.IsNullOrWhiteSpace(reportOutput.RetainedManifestPath);

    private static bool AmountsTie(decimal left, decimal right)
        => left == right;

    private static bool IsExpenseFeeAllocationRecord(PrivateCapitalFundEventLedgerRecordDto record)
        => record.FundEvent.EntryType is ManualJournalEntryTypeDto.AccruedExpense
               or ManualJournalEntryTypeDto.PrepaidExpense
               or ManualJournalEntryTypeDto.Expense
               or ManualJournalEntryTypeDto.Amortization
               or ManualJournalEntryTypeDto.Deferral
               or ManualJournalEntryTypeDto.ManagementFee ||
           ContainsFinancialOperationToken(record.FundEventType, "fee") ||
           ContainsFinancialOperationToken(record.FundEventType, "expense") ||
           ContainsFinancialOperationToken(record.FundEventType, "allocation");

    private static bool IsManagementCompanyOperatingRecord(PrivateCapitalFundEventLedgerRecordDto record)
        => IsExpenseFeeAllocationRecord(record) ||
           ContainsManagementCompanyToken(BuildManagementCompanyRecordValues(record),
               "intercompany",
               "bank",
               "card",
               "budget",
               "cash plan",
               "cash-plan",
               "reimbursement",
               "reimburse");

    private static IEnumerable<string?> BuildManagementCompanyEvidenceValues(
        IEnumerable<PrivateCapitalFundEventLedgerRecordDto> records,
        IEnumerable<PrivateCapitalCapitalAccountSubledgerDto> subledgers)
    {
        foreach (var record in records)
        {
            foreach (var value in BuildManagementCompanyRecordValues(record))
            {
                yield return value;
            }
        }

        foreach (var subledger in subledgers)
        {
            yield return subledger.SubledgerId;
            yield return subledger.LastFundEventType;
            yield return subledger.ReadinessLabel;
            yield return subledger.ReadinessReason;
            yield return subledger.NextAction;
            yield return subledger.NextActionRoute;
            foreach (var link in subledger.EvidenceLinks)
            {
                yield return link;
            }

            foreach (var category in subledger.EvidenceCategories)
            {
                foreach (var value in BuildEvidenceCategoryValues(category))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string?> BuildManagementCompanyRecordValues(PrivateCapitalFundEventLedgerRecordDto record)
    {
        yield return record.FundEventRecordId;
        yield return record.FundEventId;
        yield return record.FundEventType;
        yield return record.FundEvent.EntryType.ToString();
        yield return record.Memo;
        yield return record.ActivityRoute;
        yield return record.EvidenceRoute;
        yield return record.ApprovalRoute;
        yield return record.ReadinessLabel;
        yield return record.ReadinessReason;
        yield return record.NextAction;
        yield return record.NextActionRoute;

        foreach (var link in record.EvidenceLinks)
        {
            yield return link;
        }

        foreach (var category in record.EvidenceCategories)
        {
            foreach (var value in BuildEvidenceCategoryValues(category))
            {
                yield return value;
            }
        }

        foreach (var impact in record.LedgerImpacts)
        {
            yield return impact.LedgerImpactId;
            yield return impact.FundEventType;
            foreach (var link in impact.EvidenceLinks)
            {
                yield return link;
            }

            foreach (var line in impact.Lines)
            {
                yield return line.AccountPath;
                yield return line.EvidenceLink;
            }
        }
    }

    private static IEnumerable<string?> BuildEvidenceCategoryValues(PrivateCapitalEvidenceCategoryDto category)
    {
        if (!category.IsReady &&
            category.EvidenceLinkCount == 0 &&
            category.EvidenceLinks.Count == 0)
        {
            yield break;
        }

        yield return category.CategoryId;
        yield return category.Label;
        yield return category.Summary;
        foreach (var required in category.RequiredEvidence)
        {
            yield return required;
        }

        foreach (var link in category.EvidenceLinks)
        {
            yield return link;
        }
    }

    private static ManagementCompanyEvidenceSignal EvidenceSignal(string label, bool isPresent)
        => new(label, isPresent);

    private static bool ContainsManagementCompanyToken(IEnumerable<string?> values, params string[] tokens)
    {
        var normalizedTokens = tokens
            .Select(NormalizeSearchToken)
            .Where(static token => token.Length > 0)
            .ToArray();
        return values.Any(value =>
        {
            var normalizedValue = NormalizeSearchToken(value);
            return normalizedValue.Length > 0 &&
                   normalizedTokens.Any(normalizedValue.Contains);
        });
    }

    private static bool ContainsFinancialOperationToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSearchToken(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value
                .Where(static character => char.IsLetterOrDigit(character))
                .Select(static character => char.ToLowerInvariant(character))
                .ToArray());

    private static bool IsShadowNavReportOutput(PrivateCapitalReportOutputDto output)
        => (ContainsFinancialOperationToken(output.ReportOutputType, "shadow") &&
            ContainsFinancialOperationToken(output.ReportOutputType, "nav")) ||
           (ContainsFinancialOperationToken(output.DisplayName, "shadow") &&
            ContainsFinancialOperationToken(output.DisplayName, "nav")) ||
           (ContainsFinancialOperationToken(output.ReportPackId, "shadow") &&
            ContainsFinancialOperationToken(output.ReportPackId, "nav"));

    private static bool IsAdministratorNavReportOutput(PrivateCapitalReportOutputDto output)
        => (ContainsFinancialOperationToken(output.ReportOutputType, "administrator") ||
            ContainsFinancialOperationToken(output.ReportOutputType, "admin") ||
            ContainsFinancialOperationToken(output.DisplayName, "administrator") ||
            ContainsFinancialOperationToken(output.DisplayName, "admin") ||
            ContainsFinancialOperationToken(output.ReportPackId, "administrator") ||
            ContainsFinancialOperationToken(output.ReportPackId, "admin")) &&
           (ContainsFinancialOperationToken(output.ReportOutputType, "nav") ||
            ContainsFinancialOperationToken(output.DisplayName, "nav") ||
            ContainsFinancialOperationToken(output.ReportPackId, "nav"));

    private static string SplitComponentLabel(string key)
        => string.Join(
            ' ',
            key.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => string.IsNullOrEmpty(part)
                    ? part
                    : $"{char.ToUpperInvariant(part[0])}{part[1..]}"));

    private static PrivateCapitalCloseCockpitWorkflowDto ToWorkflowRow(OperationsContinuityWorkflowDto workflow)
    {
        var openChecklistCount = workflow.CloseChecklist.Count(static task =>
            !string.Equals(task.Status, "Acknowledged", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(task.Status, "Complete", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(task.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        return new PrivateCapitalCloseCockpitWorkflowDto(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            workflow.Status,
            workflow.CloseReadiness?.Score ?? 0,
            workflow.CloseReadiness?.IsReadyToClose ?? false,
            BuildWorkflowRoute(workflow.WorkflowId),
            workflow.ClosePackage?.ClosePackageId,
            workflow.ClosePackage?.RetainedManifestRoute,
            workflow.CloseReadiness?.Blockers.Count ?? workflow.Blockers.Count,
            openChecklistCount,
            workflow.UpdatedAtUtc);
    }

    private static IReadOnlyList<PrivateCapitalCloseCockpitApprovalDto> BuildApprovalHistory(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        => workflows
            .SelectMany(BuildWorkflowApprovalHistory)
            .OrderByDescending(static approval =>
                approval.DecidedAtUtc ??
                approval.SubmittedAtUtc ??
                DateTimeOffset.MinValue)
            .ThenBy(static approval => approval.ApprovalId, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(static approval => approval.ApprovalId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<PrivateCapitalCloseCockpitApprovalDto> BuildWorkflowApprovalHistory(
        OperationsContinuityWorkflowDto workflow)
    {
        var workflowRoute = BuildWorkflowRoute(workflow.WorkflowId);
        foreach (var approval in workflow.Approvals.Select((approval, index) => (Approval: approval, Index: index)))
        {
            var evidence = approval.Approval.EvidenceLinks ?? [];
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                Normalize(approval.Approval.ApprovalId) ?? $"approval:{workflow.WorkflowId:D}:{approval.Index + 1}",
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                approval.Approval.Status,
                Normalize(approval.Approval.Operator),
                Normalize(approval.Approval.Reviewer),
                Normalize(approval.Approval.Rationale),
                approval.Approval.SubmittedAtUtc,
                approval.Approval.DecidedAtUtc,
                workflowRoute,
                evidence.Count,
                evidence);
        }

        var package = workflow.ClosePackage;
        if (package is null)
        {
            yield break;
        }

        var packageEvidence = package.EvidenceLinks ?? [];
        foreach (var approval in package.ChecklistControlApprovals)
        {
            var approvalId = $"checklist-control:{workflow.WorkflowId:D}:{Normalize(approval.TaskId) ?? "task"}:{approval.ApprovedAtUtc:yyyyMMddHHmmss}";
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                approvalId,
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                OperationsApprovalStateDto.Approved,
                null,
                Normalize(approval.ApprovedBy),
                $"Checklist control approval retained for {approval.TaskId}.",
                approval.ApprovedAtUtc,
                approval.ApprovedAtUtc,
                workflowRoute,
                packageEvidence.Count,
                packageEvidence);
        }
    }

    private static string BuildCockpitRoute(
        string? fundProfileId,
        Guid? ledgerBookId,
        Guid? fundAccountId,
        string? periodId,
        string? entityId)
    {
        var query = new List<string>();
        AddQuery(query, "fundProfileId", fundProfileId);
        if (ledgerBookId.HasValue)
        {
            AddQuery(query, "ledgerBookId", ledgerBookId.Value.ToString("D"));
        }

        if (fundAccountId.HasValue)
        {
            AddQuery(query, "fundAccountId", fundAccountId.Value.ToString("D"));
        }

        AddQuery(query, "periodId", periodId);
        AddQuery(query, "entityId", entityId);
        return query.Count == 0
            ? UiApiRoutes.OperationsPrivateCapitalCloseCockpit
            : UiApiRoutes.WithQuery(UiApiRoutes.OperationsPrivateCapitalCloseCockpit, string.Join("&", query));
    }

    private static string BuildCapitalAccountWorkbenchRoute(
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var query = new List<string>
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}"
        };

        if (ledgerBookId.HasValue)
        {
            query.Add($"ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString("D"))}");
        }

        return UiApiRoutes.WithQuery(UiApiRoutes.LedgerPrivateCapitalCapitalAccountWorkbench, string.Join("&", query));
    }

    private static void AddQuery(List<string> query, string key, string? value)
    {
        var normalized = Normalize(value);
        if (normalized is not null)
        {
            query.Add($"{key}={Uri.EscapeDataString(normalized)}");
        }
    }

    private static string BuildWorkflowRoute(Guid workflowId)
        => UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString("D"));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ManagementCompanyEvidenceSignal(string Label, bool IsPresent);
}
