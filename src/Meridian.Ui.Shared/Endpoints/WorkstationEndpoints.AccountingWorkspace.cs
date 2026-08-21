using System.Globalization;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    // Returns null when the strategy run read service is not registered so the route can
    // respond 503 instead of serving fabricated reconciliation/cash-flow data.
    private static async Task<WorkstationAccountingPayload?> BuildAccountingPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var breakQueueRepository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        var kernelObservability = context.RequestServices.GetService<KernelObservabilityService>()?.GetSnapshot();
        var requestedLedgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
        if (readService is null || breakQueueRepository is null)
        {
            return null;
        }

        if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
        {
            return null;
        }

        // The workspace admits every desk that works the period; what it may not do is hand each of
        // them the families a narrower route owns. Resolved once and applied to every embedded family
        // below, and an unreadable family is not fetched at all rather than fetched and discarded.
        var readScope = ResolveAccountingWorkspaceReadScope(context);

        var manualJournalWorkbench = readScope.ManualJournal
            ? await BuildManualJournalWorkbenchPayloadAsync(context).ConfigureAwait(false)
            : null;

        // Fetched whatever the caller may see, because the headline counters below are derived from
        // it. The records themselves are withheld from a caller the break-queue routes would refuse;
        // the counts are what the workspace exists to show every desk it admits.
        var breakQueueItems = await GetBreakQueueItemsAsync(
                breakQueueRepository,
                queueScope,
                status: null,
                fundAccountId: null,
                ledgerBookId: requestedLedgerBookId,
                ct: context.RequestAborted)
            .ConfigureAwait(false);
        var scopedOpenBreaks = breakQueueItems.Count(static item =>
            item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview);
        var visibleBreakQueueItems = readScope.BreakQueue
            ? breakQueueItems
            : (IReadOnlyList<ReconciliationBreakQueueItem>)[];

        var allRuns = await GetAuthorizedAccountingRunsAsync(
                context,
                readService,
                queueScope,
                context.RequestAborted)
            .ConfigureAwait(false);
        if (allRuns is null)
        {
            return null;
        }

        var runs = allRuns.Take(6).ToArray();
        if (runs.Length == 0)
        {
            var reporting = BuildAccountingReportingPayload(context, readScope);
            // PR-03: return typed DTO
            return new WorkstationAccountingPayload(
                Metrics:
                [
                    new WorkstationMetricCard("open-breaks", "Open Breaks", scopedOpenBreaks.ToString(CultureInfo.InvariantCulture), "0%", scopedOpenBreaks == 0 ? "success" : "warning"),
                    new WorkstationMetricCard("timing-drift", "Timing Drift", "0", "0%", "default"),
                    new WorkstationMetricCard("security-gaps", "Security Gaps", "0", "0%", "success"),
                    new WorkstationMetricCard("audit-ready", "Audit Ready", "0", "0%", "default"),
                    .. readScope.KernelObservability
                        ? new[] { new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability)) }
                        : Array.Empty<WorkstationMetricCard>()
                ],
                ReconciliationQueue: Array.Empty<WorkstationAccountingRunRecord>(),
                BreakQueue: visibleBreakQueueItems,
                Workspace: new WorkstationAccountingWorkspaceSummary(0, 0, 0, scopedOpenBreaks, 0),
                CashFlow: BuildAccountingWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                Reporting: reporting,
                ControlCenter: BuildAccountingControlCenterPayload(breakQueueItems, reporting, readScope.BreakQueue),
                KernelObservability: BuildKernelObservabilityPayload(readScope.KernelObservability ? kernelObservability : null),
                ManualJournalWorkbench: manualJournalWorkbench);
        }

        var reconciliationService = context.RequestServices.GetService<IReconciliationRunService>();
        var detailTasks = runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted));
        var reconciliationTasks = reconciliationService is null
            ? runs.Select(_ => Task.FromResult<ReconciliationRunDetail?>(null))
            : runs.Select(run => reconciliationService.GetLatestForRunAsync(run.RunId, context.RequestAborted));

        var details = await Task.WhenAll(detailTasks).ConfigureAwait(false);
        var reconciliations = await Task.WhenAll(reconciliationTasks).ConfigureAwait(false);

        var timingDriftRuns = reconciliations.Count(static detail => detail?.Summary.HasTimingDrift == true);
        var runsWithBreaks = reconciliations.Count(static detail => (detail?.Summary.BreakCount ?? 0) > 0);
        var runsWithSecurityIssues = details.Count(static detail =>
            (detail?.Portfolio?.SecurityMissingCount ?? 0) > 0 ||
            (detail?.Ledger?.SecurityMissingCount ?? 0) > 0);
        var auditReadyRuns = runs.Count(static run => !string.IsNullOrWhiteSpace(run.AuditReference)) - runsWithBreaks;
        var reportingPayload = BuildAccountingReportingPayload(context, readScope);

        // PR-03: return typed DTO
        return new WorkstationAccountingPayload(
            Metrics:
            [
                new WorkstationMetricCard("open-breaks", "Open Breaks", scopedOpenBreaks.ToString(CultureInfo.InvariantCulture), "0%", scopedOpenBreaks == 0 ? "success" : "warning"),
                new WorkstationMetricCard("timing-drift", "Timing Drift", timingDriftRuns.ToString(CultureInfo.InvariantCulture), "0%", timingDriftRuns == 0 ? "default" : "warning"),
                new WorkstationMetricCard("security-gaps", "Security Gaps", runsWithSecurityIssues.ToString(CultureInfo.InvariantCulture), "0%", runsWithSecurityIssues == 0 ? "success" : "warning"),
                new WorkstationMetricCard("audit-ready", "Audit Ready", Math.Max(0, auditReadyRuns).ToString(CultureInfo.InvariantCulture), "0%", auditReadyRuns > 0 ? "success" : "default"),
                .. readScope.KernelObservability
                    ? new[] { new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability)) }
                    : Array.Empty<WorkstationMetricCard>()
            ],
            // Withheld exactly as the empty-run branch above renders them, so a caller without run
            // authority sees the shape the workspace already has when there is nothing to show rather
            // than a new one. The counters beside them are counts and stay; the records and the
            // balances are what the run routes serve to ViewStrategies and ManageStrategies alone.
            ReconciliationQueue: readScope.StrategyRuns
                ? runs
                    .Zip(details, static (run, detail) => (run, detail))
                    .Zip(reconciliations, (pair, reconciliation) => BuildAccountingRunCard(pair.run, pair.detail, reconciliation, kernelObservability))
                    .ToArray()
                : Array.Empty<WorkstationAccountingRunRecord>(),
            BreakQueue: visibleBreakQueueItems,
            Workspace: new WorkstationAccountingWorkspaceSummary(
                TotalRuns: allRuns.Length,
                ReconciledRuns: reconciliations.Count(static detail => detail is not null),
                LedgerReadyRuns: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                OpenBreaks: scopedOpenBreaks,
                SecurityIssues: runsWithSecurityIssues),
            CashFlow: BuildAccountingWorkspaceCashFlowSummary(
                readScope.StrategyRuns ? details : Array.Empty<StrategyRunDetail?>()),
            Reporting: reportingPayload,
            ControlCenter: BuildAccountingControlCenterPayload(breakQueueItems, reportingPayload, readScope.BreakQueue),
            KernelObservability: BuildKernelObservabilityPayload(readScope.KernelObservability ? kernelObservability : null),
            ManualJournalWorkbench: manualJournalWorkbench);
    }

    private static async Task<StrategyRunSummary[]?> GetAuthorizedAccountingRunsAsync(
        HttpContext context,
        StrategyRunReadService readService,
        ReconciliationBreakQueueScope scope,
        CancellationToken ct)
    {
        var tenancyRegistry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (tenancyRegistry is null)
        {
            return null;
        }

        try
        {
            var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
            var ownershipByFund = new Dictionary<string, FundProfileOwnership?>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var fundProfileId in runs
                         .Select(static run => run.FundProfileId)
                         .Where(static fundProfileId => !string.IsNullOrWhiteSpace(fundProfileId))
                         .Select(static fundProfileId => fundProfileId!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ownershipByFund[fundProfileId] = await tenancyRegistry
                    .ResolveAsync(fundProfileId, ct)
                    .ConfigureAwait(false);
            }

            return runs
                .Where(run =>
                {
                    if (string.IsNullOrWhiteSpace(run.FundProfileId))
                    {
                        return false;
                    }

                    var fundProfileId = run.FundProfileId.Trim();
                    return ownershipByFund.TryGetValue(fundProfileId, out var ownership) &&
                           ownership is not null &&
                           ownership.IsHeldBy(scope.TenantId) &&
                           !string.IsNullOrWhiteSpace(ownership.CompanyId) &&
                           string.Equals(
                               ownership.CompanyId.Trim(),
                               scope.CompanyId.Trim(),
                               StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// The reporting projection embedded in the accounting workspace, or a withheld one whose summary
    /// says why it is empty.
    /// <para>
    /// A withheld payload rather than the unavailable one: "reporting is unavailable, review the
    /// deployment capability" would send an operator to look for a fault that is not there. The
    /// distinction matters because the workspace's own gate does not admit reporting authority at
    /// all, so this is the ordinary case for most callers, not an error.
    /// </para>
    /// </summary>
    private static WorkstationReportingPayload BuildAccountingReportingPayload(
        HttpContext context,
        AccountingWorkspaceReadScope scope)
        => scope.Reporting
            ? BuildReportingPayload(context)
            : new WorkstationReportingPayload(
                ProfileCount: 0,
                RecommendedProfiles: [],
                Profiles: [],
                ReportPackDistributions: [],
                Summary: "Reporting is not included in this workspace for your permissions. Open the reporting workspace with reporting authority to review profiles, templates, and report-pack runs.",
                Templates: [],
                RecentRuns: [],
                DeploymentCapability: null);

    /// <summary>
    /// Resolves which embedded families the caller may see in the accounting workspace payload. Each
    /// set is the one its own route requires, read from that route rather than inferred, so widening
    /// a route later cannot silently leave the composite behind.
    /// </summary>
    private static AccountingWorkspaceReadScope ResolveAccountingWorkspaceReadScope(HttpContext context)
        => new(
            BreakQueue: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewDirectLending,
                UserPermission.ManageDirectLending,
                UserPermission.ViewSecurityMaster,
                UserPermission.ModifySecurityMaster,
                UserPermission.AdminMaintenance),
            ManualJournal: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.AdminMaintenance,
                UserPermission.ManageDirectLending),
            Reporting: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewReporting,
                UserPermission.AdminMaintenance),
            StrategyRuns: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewStrategies,
                UserPermission.ManageStrategies),
            KernelObservability: EndpointAuthorization.HasAnyPermission(
                context,
                UserPermission.ViewHistoricalData,
                UserPermission.ViewDiagnostics,
                UserPermission.ManageStorage));

    private static async Task<ManualJournalEntryWorkbenchDto?> BuildManualJournalWorkbenchPayloadAsync(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return null;
        }

        var query = context.Request.Query;
        var fundProfileId = query["fundProfileId"].FirstOrDefault();
        var ledgerBookId = ParseOptionalGuid(query["ledgerBookId"].FirstOrDefault());
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);

        return await service
            .GetWorkbenchAsync(fundProfileId, ledgerBookId, context.RequestAborted, tenantContext.TenantId, tenantContext.CompanyId)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Close-readiness triage over the break queue.
    /// <para>
    /// <paramref name="discloseCasework"/> separates the two kinds of field this payload carries.
    /// Counts, distributions, aging curves and readiness stay truthful for every caller the
    /// workspace admits -- they are the same shape as the headline metrics, and a control centre
    /// computed from a withheld queue would report "no blockers" to an operator who has several.
    /// The two identifier-bearing fields are the ones the break-queue routes actually own: the fund
    /// accounts under casework and the assignees carrying it. Those are withheld from a caller
    /// those routes would refuse.
    /// </para>
    /// </summary>
    private static WorkstationAccountingControlCenterPayload BuildAccountingControlCenterPayload(
        IReadOnlyList<ReconciliationBreakQueueItem> breakQueue,
        WorkstationReportingPayload reporting,
        bool discloseCasework = true)
    {
        var criticalOpen = breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Critical && item.Status != ReconciliationBreakQueueStatus.Resolved && item.Status != ReconciliationBreakQueueStatus.Dismissed);
        var inReview = breakQueue.Count(item => item.Status == ReconciliationBreakQueueStatus.InReview);
        var unowned = breakQueue.Count(item => string.IsNullOrWhiteSpace(item.AssignedTo));
        var overdue = breakQueue.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-2));
        var breachCount = breakQueue.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-3));

        var alerts = new List<WorkstationAccountingAlertPayload>();
        if (criticalOpen > 0)
        {
            alerts.Add(new WorkstationAccountingAlertPayload("danger", $"{criticalOpen} critical reconciliation breaks remain unresolved."));
        }

        if (overdue > 0)
        {
            alerts.Add(new WorkstationAccountingAlertPayload("danger", $"{overdue} reconciliation breaks are overdue for resolution."));
        }

        if (reporting.ReportPackDistributions.Any(distribution => distribution.PendingItems > 0))
        {
            alerts.Add(new WorkstationAccountingAlertPayload("warning", "Report-pack distribution recipients have pending approval, publication, or delivery work."));
        }

        return new WorkstationAccountingControlCenterPayload(
            CloseReadiness: criticalOpen == 0 && overdue == 0 ? "ReadyWithAttention" : "Blocked",
            PortfolioFilterOptions: ["all-portfolios", "macro", "equity", "fixed-income"],
            AccountFilterOptions: discloseCasework
                ? breakQueue.Select(item => item.FundAccountId).Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct().Cast<string>().ToArray()
                : [],
            BlockerSeverityDistribution:
            [
                new WorkstationAccountingSeverityCountPayload("Critical", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Critical)),
                new WorkstationAccountingSeverityCountPayload("High", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.High)),
                new WorkstationAccountingSeverityCountPayload("Medium", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Medium)),
                new WorkstationAccountingSeverityCountPayload("Low", breakQueue.Count(item => item.Severity == ReconciliationBreakSeverity.Low))
            ],
            AgingCurves:
            [
                new WorkstationAccountingAgingBucketPayload("0-1d", breakQueue.Count(item => item.LastUpdatedAt >= DateTimeOffset.UtcNow.AddDays(-1))),
                new WorkstationAccountingAgingBucketPayload("2-3d", breakQueue.Count(item => item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-1) && item.LastUpdatedAt >= DateTimeOffset.UtcNow.AddDays(-3))),
                new WorkstationAccountingAgingBucketPayload("4d+", breakQueue.Count(item => item.LastUpdatedAt < DateTimeOffset.UtcNow.AddDays(-3)))
            ],
            OwnerWorkload: discloseCasework
                ? breakQueue.GroupBy(item => string.IsNullOrWhiteSpace(item.AssignedTo) ? "Unassigned" : item.AssignedTo!)
                    .Select(group => new WorkstationAccountingOwnerWorkloadPayload(
                        Owner: group.Key,
                        OpenCount: group.Count(item => item.Status != ReconciliationBreakQueueStatus.Resolved && item.Status != ReconciliationBreakQueueStatus.Dismissed)))
                    .OrderByDescending(item => item.OpenCount)
                    .ToArray()
                : [],
            SlaBreachCount: breachCount,
            TrendSnapshots:
            [
                new WorkstationAccountingTrendSnapshotPayload("Open critical breaks", criticalOpen, criticalOpen > 0 ? "worsening" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload("Breaks in review", inReview, inReview > 0 ? "improving" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload("Unassigned breaks", unowned, unowned > 0 ? "worsening" : "stable"),
                new WorkstationAccountingTrendSnapshotPayload(
                    "Report distributions pending",
                    reporting.ReportPackDistributions.Count(distribution => distribution.PendingItems > 0),
                    "stable")
            ],
            DrillLinks:
            [
                new WorkstationAccountingDrillLinkPayload("Open close readiness", "/trading/readiness"),
                new WorkstationAccountingDrillLinkPayload("Open reconciliation queue", "/accounting/reconciliation"),
                new WorkstationAccountingDrillLinkPayload("Open report approvals", "/reporting/report-packs"),
                new WorkstationAccountingDrillLinkPayload("Open evidence completeness", "/reporting/evidence")
            ],
            Alerts: alerts);
    }
}
