using System.Globalization;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Operator Inbox composition for the workstation API surface: builds the prioritized operator
/// work-item inbox (trading-readiness work items, run-review packets, reconciliation-break work
/// items), continuity metrics, operator navigation routing, and the inbox summary. Split out of
/// the WorkstationEndpoints core partial as a behavior-preserving relocation; the inline inbox
/// route lambda and the shared GetTradingOperatorReadinessAsync / NormalizeOperatorInboxToken
/// helpers remain in core.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static async Task<OperatorInboxDto> BuildOperatorInboxAsync(Guid? fundAccountId, HttpContext context)
    {
        var asOf = DateTimeOffset.UtcNow;
        var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);
        var workItems = readiness.WorkItems
            .Select(AttachOperatorNavigation)
            .ToList();

        // The inbox aggregates four families whose own routes carry different permissions, so each
        // contribution is gated by the permission its source route requires. Without this, the single
        // route-level permission would decide the whole payload and a caller admitted for one family
        // would read another's content -- reconciliation break items, for instance, carry the strategy
        // name, break reason, status and assignee, not merely a count.
        if (HasRunReviewInboxPermission(context))
        {
            await AddRunReviewPacketWorkItemsAsync(context, fundAccountId, workItems, asOf).ConfigureAwait(false);
        }

        if (CanViewReconciliationBreakQueue(context))
        {
            await AddReconciliationBreakWorkItemsAsync(context, workItems, asOf).ConfigureAwait(false);
        }

        var operatorInbox = context.RequestServices.GetService<IOperatorInboxService>();
        if (operatorInbox is not null && HasContributedInboxPermission(context))
        {
            var contributedItems = await operatorInbox.GetItemsAsync(context.RequestAborted).ConfigureAwait(false);
            workItems.AddRange(contributedItems.Select(AttachOperatorNavigation));
        }

        PreferCanonicalReconciliationBreakWorkItems(workItems);

        var items = workItems
            .GroupBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static item => item.Tone)
                .ThenByDescending(static item => item.CreatedAt)
                .First())
            .Select(item => OperatorInboxPriorityScoringService.ApplyScore(item, asOf))
            .OrderByDescending(static item => item.PriorityScore)
            .ThenByDescending(static item => item.Tone)
            .ThenBy(static item => item.CreatedAt)
            .ThenBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RecordOperatorInboxContinuityMetrics(items);

        var criticalCount = items.Count(static item => item.Tone == OperatorWorkItemToneDto.Critical);
        var warningCount = items.Count(static item => item.Tone == OperatorWorkItemToneDto.Warning);
        var reviewCount = criticalCount + warningCount;

        return new OperatorInboxDto(
            AsOf: asOf,
            Items: items,
            CriticalCount: criticalCount,
            WarningCount: warningCount,
            ReviewCount: reviewCount,
            Summary: BuildOperatorInboxSummary(items, criticalCount, warningCount, readiness.PortfolioLedgerWorkflowStatus));
    }

    /// <summary>
    /// Run-review packets restate strategy-run detail, so they are contributed only to callers the
    /// run drill-in routes admit.
    /// </summary>
    private static bool HasRunReviewInboxPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewStrategies,
            UserPermission.ManageStrategies);

    /// <summary>
    /// Items contributed through <see cref="IOperatorInboxService"/> come from the ledger book and
    /// direct-lending accrual paths, so they are contributed only to callers who can read those.
    /// </summary>
    private static bool HasContributedInboxPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewDirectLending,
            UserPermission.ManageDirectLending,
            UserPermission.AdminMaintenance);

    private static void PreferCanonicalReconciliationBreakWorkItems(List<OperatorWorkItemDto> workItems)
    {
        var canonicalRunIds = workItems
            .Where(static item =>
                item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
                item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.RunId))
            .Select(static item => item.RunId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (canonicalRunIds.Count == 0)
        {
            return;
        }

        workItems.RemoveAll(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            !item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.RunId) &&
            canonicalRunIds.Contains(item.RunId));
    }

    private static void RecordOperatorInboxContinuityMetrics(IReadOnlyList<OperatorWorkItemDto> items)
    {
        foreach (var item in items)
        {
            if (item.Tone is not (OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Workspace)
                || string.IsNullOrWhiteSpace(item.TargetRoute)
                || string.IsNullOrWhiteSpace(item.TargetPageTag))
            {
                var failureKind = string.IsNullOrWhiteSpace(item.WorkItemId)
                    ? "missing-navigation"
                    : item.WorkItemId;
                PrometheusMetrics.RecordRunContinuityUnresolvedBlockerLinkage("operator-inbox", failureKind);
            }
        }
    }

    private static async Task AddRunReviewPacketWorkItemsAsync(
        HttpContext context,
        Guid? fundAccountId,
        List<OperatorWorkItemDto> workItems,
        DateTimeOffset asOf)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var reviewPacketService = context.RequestServices.GetService<StrategyRunReviewPacketService>();
        if (readService is null || reviewPacketService is null)
        {
            return;
        }

        try
        {
            var runs = await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 6), context.RequestAborted)
                .ConfigureAwait(false);

            var reviewRuns = runs
                .Where(ShouldSurfaceRunReviewWorkItems)
                .OrderByDescending(GetRunReviewTimestamp)
                .ToArray();
            var latestReviewRunId = reviewRuns.FirstOrDefault()?.RunId;

            foreach (var run in reviewRuns)
            {
                var packet = await reviewPacketService
                    .GetAsync(run.RunId, fundAccountId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (packet is null)
                {
                    continue;
                }

                var isLatestReviewRun = string.Equals(run.RunId, latestReviewRunId, StringComparison.OrdinalIgnoreCase);
                var hasStalePromotionAttention = HasStalePromotionAttention(packet);
                workItems.AddRange(packet.WorkItems
                    .Where(item =>
                        item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical &&
                        (isLatestReviewRun ||
                         item.Kind != OperatorWorkItemKindDto.PromotionReview ||
                         !hasStalePromotionAttention))
                    .Select(AttachOperatorNavigation));
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            workItems.Add(BuildRunReviewPacketUnavailableWorkItem(asOf));
        }
    }

    private static bool HasStalePromotionAttention(StrategyRunReviewPacketDto packet)
        => packet.Continuity?.ContinuityStatus.HasCashFlow == true &&
           packet.WorkItems.Any(static item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.WorkItemId.StartsWith("continuity-promotion-target-run-missing-", StringComparison.OrdinalIgnoreCase) &&
            item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical);

    private static bool ShouldSurfaceRunReviewWorkItems(StrategyRunSummary run)
        => run.Promotion?.RequiresReview == true ||
           run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled;

    private static DateTimeOffset GetRunReviewTimestamp(StrategyRunSummary run)
        => run.CompletedAt ?? run.LastUpdatedAt;

    private static OperatorWorkItemDto BuildRunReviewPacketUnavailableWorkItem(DateTimeOffset asOf)
        => new(
            WorkItemId: "run-review-packets-unavailable",
            Kind: OperatorWorkItemKindDto.PromotionReview,
            Label: "Run review packets unavailable",
            Detail: "Trading readiness is still available, but run review-packet work items could not be loaded. Review run-read service health before accepting promotion queue coverage.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: asOf,
            Workspace: "Trading",
            TargetRoute: UiApiRoutes.WorkstationOperatorInbox,
            TargetPageTag: "TradingShell");

    private static async Task AddReconciliationBreakWorkItemsAsync(
        HttpContext context,
        List<OperatorWorkItemDto> workItems,
        DateTimeOffset asOf)
    {
        try
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return;
            }

            var reconciliationBreaks = await GetBreakQueueItemsAsync(
                context.RequestServices,
                queueScope,
                status: null,
                fundAccountId: null,
                ledgerBookId: null,
                ct: context.RequestAborted).ConfigureAwait(false);
            workItems.AddRange(reconciliationBreaks
                .Where(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview)
                .Select(MapReconciliationBreakWorkItem));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            workItems.Add(BuildReconciliationBreakQueueUnavailableWorkItem(asOf));
        }
    }

    private static OperatorWorkItemDto BuildReconciliationBreakQueueUnavailableWorkItem(DateTimeOffset asOf)
        => new(
            WorkItemId: "reconciliation-break-queue-unavailable",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Reconciliation queue unavailable",
            Detail: "Trading readiness is still available, but reconciliation break work items could not be loaded. Review storage health before accepting accounting queue coverage.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: asOf,
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "AccountingShell");

    private static OperatorWorkItemDto AttachOperatorNavigation(OperatorWorkItemDto item)
    {
        var navigation = ResolveOperatorNavigation(item.Kind, item.FundAccountId);
        return item with
        {
            Workspace = item.Workspace ?? navigation.Workspace,
            TargetRoute = item.TargetRoute ?? navigation.TargetRoute,
            TargetPageTag = item.TargetPageTag ?? navigation.TargetPageTag
        };
    }

    private static OperatorWorkItemDto MapReconciliationBreakWorkItem(ReconciliationBreakQueueItem item)
    {
        var tone = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => OperatorWorkItemToneDto.Critical,
            ReconciliationBreakSeverity.High or ReconciliationBreakSeverity.Medium => OperatorWorkItemToneDto.Warning,
            _ => OperatorWorkItemToneDto.Info
        };
        var assignment = string.IsNullOrWhiteSpace(item.AssignedTo)
            ? "unassigned"
            : $"assigned to {item.AssignedTo}";
        var status = item.Status == ReconciliationBreakQueueStatus.InReview
            ? "in review"
            : "open";
        var routeDetail = BuildReconciliationRoutingDetail(item);

        return new OperatorWorkItemDto(
            WorkItemId: BuildOperatorInboxScopedId("reconciliation-break", item.BreakId),
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: item.Status == ReconciliationBreakQueueStatus.InReview
                ? "Reconciliation break in review"
                : "Reconciliation break requires review",
            Detail: $"{item.StrategyName}: {item.Reason} The break is {status} and {assignment}. {routeDetail}",
            Tone: tone,
            CreatedAt: item.DetectedAt,
            RunId: item.RunId,
            AuditReference: item.BreakId,
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "AccountingShell");
    }

    private static string BuildReconciliationRoutingDetail(ReconciliationBreakQueueItem item)
    {
        var exceptionRoute = string.IsNullOrWhiteSpace(item.ExceptionRoute)
            ? "operations-triage"
            : item.ExceptionRoute;
        var toleranceProfileId = string.IsNullOrWhiteSpace(item.ToleranceProfileId)
            ? "standard-recon-tolerance"
            : item.ToleranceProfileId;
        var requiredSignoffRole = string.IsNullOrWhiteSpace(item.RequiredSignoffRole)
            ? "Operations reviewer"
            : item.RequiredSignoffRole;
        var signoffStatus = string.IsNullOrWhiteSpace(item.SignoffStatus)
            ? "pending-signoff"
            : item.SignoffStatus;
        var toleranceBand = item.ToleranceBand.HasValue
            ? $" ({item.ToleranceBand.Value.ToString("0.##", CultureInfo.InvariantCulture)} tolerance)"
            : string.Empty;

        return $"Exception route: {exceptionRoute}; tolerance profile {toleranceProfileId}{toleranceBand}; sign-off {signoffStatus} by {requiredSignoffRole}.";
    }

    private static (string Workspace, string TargetRoute, string TargetPageTag) ResolveOperatorNavigation(
        OperatorWorkItemKindDto kind,
        Guid? fundAccountId)
        => kind switch
        {
            OperatorWorkItemKindDto.SecurityMasterCoverage => (
                "Data",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                "DataShell"),
            OperatorWorkItemKindDto.ReconciliationBreak => (
                "Accounting",
                UiApiRoutes.ReconciliationBreakQueue,
                "AccountingShell"),
            OperatorWorkItemKindDto.LedgerPeriodClose => (
                "Accounting",
                UiApiRoutes.ReconciliationBreakQueue,
                "FundReconciliation"),
            OperatorWorkItemKindDto.ReportPackApproval => (
                "Reporting",
                UiApiRoutes.FundReportPacks,
                "ReportingShell"),
            OperatorWorkItemKindDto.BrokerageSync => (
                "Trading",
                fundAccountId.HasValue
                    ? UiApiRoutes.WithParam(UiApiRoutes.FundAccountBrokerageSyncStatus, "accountId", fundAccountId.Value.ToString())
                    : UiApiRoutes.FundAccountBrokerageSyncAccounts,
                "AccountPortfolio"),
            _ => (
                "Trading",
                UiApiRoutes.WorkstationTradingReadiness,
                "TradingShell")
        };

    private static string BuildOperatorInboxSummary(
        IReadOnlyCollection<OperatorWorkItemDto> items,
        int criticalCount,
        int warningCount,
        PortfolioLedgerWorkflowStatusSnapshotDto? statusSnapshot)
    {
        if (items.Count == 0)
        {
            return "No operator work items are open.";
        }

        if (criticalCount > 0)
        {
            return $"{criticalCount} critical and {warningCount} warning work item(s) need review. {statusSnapshot?.Summary}".Trim();
        }

        if (warningCount > 0)
        {
            return $"{warningCount} warning work item(s) need review. {statusSnapshot?.Summary}".Trim();
        }

        return $"{items.Count} informational work item(s) are available. {statusSnapshot?.Summary}".Trim();
    }

    private static string BuildOperatorInboxScopedId(string prefix, string scope)
    {
        var normalizedPrefix = NormalizeOperatorInboxToken(prefix);
        var normalizedScope = NormalizeOperatorInboxToken(scope);
        return string.IsNullOrEmpty(normalizedScope)
            ? normalizedPrefix
            : $"{normalizedPrefix}-{normalizedScope}";
    }
}
