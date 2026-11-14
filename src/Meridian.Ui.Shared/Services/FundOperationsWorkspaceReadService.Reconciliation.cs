using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{

    private async Task<ReconciliationSummary> BuildReconciliationSummaryAsync(
        string fundProfileId,
        IReadOnlyList<AccountWorkspaceProjection> accountProjections,
        IReadOnlyList<StrategyRunEntry> runs,
        ReconciliationBreakQueueScope? accessScope,
        CancellationToken ct)
    {
        var items = new List<FundReconciliationItem>();
        var openBreaks = 0;
        decimal breakAmountTotal = 0m;
        var securityCoverageIssues = 0;

        foreach (var projection in accountProjections)
        {
            ct.ThrowIfCancellationRequested();
            var account = projection.Summary;
            var accountRuns = projection.ReconciliationRuns;
            var resultsByRun = projection.ReconciliationResults.ToLookup(result => result.ReconciliationRunId);

            foreach (var run in accountRuns)
            {
                var results = resultsByRun[run.ReconciliationRunId];
                var activeBreaks = results.Where(static result => result.IsOpenBreak).ToArray();
                var activeBreakAmount = activeBreaks.Sum(static result => Math.Abs(result.Variance ?? 0m));
                var historicalBreaks = results.Where(static result => !result.IsMatch
                    && !string.Equals(result.Status, "Unverified", StringComparison.OrdinalIgnoreCase)).ToArray();
                var lifecycleStatus = run.TotalBreaks > 0 && activeBreaks.Length == 0
                    ? historicalBreaks.Length > 0 && historicalBreaks.All(static result => result.ResolvedAt.HasValue)
                        ? "Resolved" : "Superseded"
                    : run.Status;
                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: run.ReconciliationRunId,
                    AccountId: run.AccountId,
                    AccountDisplayName: account.DisplayName,
                    AsOfDate: run.AsOfDate,
                    Status: lifecycleStatus,
                    TotalChecks: run.TotalChecks,
                    TotalMatched: run.TotalMatched,
                    TotalBreaks: activeBreaks.Length,
                    BreakAmountTotal: activeBreakAmount,
                    RequestedAt: run.RequestedAt,
                    CompletedAt: run.CompletedAt,
                    ScopeLabel: "Account",
                    CoverageLabel: "Account-level reconciliation"));

                openBreaks += activeBreaks.Length;
                breakAmountTotal += activeBreakAmount;
            }
        }

        if (_strategyReconciliationService is not null)
        {
            foreach (var run in runs)
            {
                ct.ThrowIfCancellationRequested();

                var detail = await _strategyReconciliationService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false);

                if (detail is null)
                {
                    continue;
                }

                var asOf = detail.Summary.PortfolioAsOf
                    ?? detail.Summary.LedgerAsOf
                    ?? detail.Summary.CreatedAt;
                var strategyBreakAmount = detail.Breaks.Sum(static result => Math.Abs(result.Variance));
                var status = MapStrategyStatus(detail.Summary);

                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: ParseGuid(detail.Summary.ReconciliationRunId),
                    AccountId: Guid.Empty,
                    AccountDisplayName: run.StrategyName,
                    AsOfDate: DateOnly.FromDateTime(asOf.UtcDateTime),
                    Status: status,
                    TotalChecks: detail.Summary.MatchCount + detail.Summary.BreakCount,
                    TotalMatched: detail.Summary.MatchCount,
                    TotalBreaks: detail.Summary.BreakCount,
                    BreakAmountTotal: strategyBreakAmount,
                    RequestedAt: detail.Summary.CreatedAt,
                    CompletedAt: detail.Summary.CreatedAt,
                    ScopeLabel: "Strategy Run",
                    StrategyName: run.StrategyName,
                    RunId: run.RunId,
                    SecurityIssueCount: detail.Summary.SecurityIssueCount,
                    HasSecurityCoverageIssues: detail.Summary.HasSecurityCoverageIssues,
                    CoverageLabel: detail.Summary.HasSecurityCoverageIssues
                        ? $"{detail.Summary.SecurityIssueCount} security issue(s)"
                        : "Security Master aligned"));

                if (detail.Summary.BreakCount > 0)
                {
                    openBreaks += detail.Summary.BreakCount;
                    breakAmountTotal += strategyBreakAmount;
                }

                securityCoverageIssues += detail.Summary.SecurityIssueCount;
            }
        }

        var ordered = items
            .OrderByDescending(static item => item.RequestedAt)
            .ToArray();
        var breakQueue = await BuildBreakQueueProjectionAsync(fundProfileId, accessScope, ct)
            .ConfigureAwait(false);

        return new ReconciliationSummary(
            RunCount: ordered.Length,
            OpenBreakCount: openBreaks,
            BreakAmountTotal: breakAmountTotal,
            RecentRuns: ordered,
            SecurityCoverageIssueCount: securityCoverageIssues,
            BreakQueue: breakQueue,
            LedgerImpactPreview: BuildLedgerImpactPreview(ordered),
            HasCriticalBreakOpen: breakQueue?.CriticalOpenCount > 0);
    }

    private async Task<ReconciliationBreakQueueProjectionDto?> BuildBreakQueueProjectionAsync(
        string fundProfileId,
        ReconciliationBreakQueueScope? accessScope,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return null;
        }

        var requiredScope = accessScope
            ?? throw new InvalidOperationException(
                "A tenant- and company-scoped request context is required to read reconciliation casework.");
        var items = (await _breakQueueRepository
                .GetAllAsync(requiredScope, status: null, ct)
                .ConfigureAwait(false))
            .Where(item => string.Equals(
                item.FundProfileId,
                fundProfileId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var projected = items
            .OrderByDescending(static item => item.LastUpdatedAt)
            .Select(item => new ReconciliationBreakQueueProjectionItemDto(
                BreakId: item.BreakId,
                WorkflowId: item.RunId,
                Severity: item.Severity,
                Status: item.Status,
                Owner: item.AssignedTo,
                RequiredSignoffRole: item.RequiredSignoffRole,
                SignoffStatus: item.SignoffStatus,
                RoutingTarget: item.RoutingTarget,
                RoutingDetail: item.RoutingDetail,
                EvidenceReference: item.UpstreamSyncCursor ?? item.ExternalAccountId,
                LastUpdatedAt: item.LastUpdatedAt,
                Priority: item.Priority,
                SlaState: item.SlaState,
                AgeBand: item.AgeBand,
                RootCauseCode: item.RootCauseCode,
                ResolutionCode: item.ResolutionCode,
                CommentCount: item.CommentCount,
                EvidenceCount: item.EvidenceCount,
                LastActivityAt: item.LastActivityAt,
                LastCommentExcerpt: BuildLastCommentExcerpt(item),
                RelatedCaseCount: items.Count(candidate =>
                    !string.Equals(candidate.BreakId, item.BreakId, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(candidate.ExternalAccountId, item.ExternalAccountId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.Counterparty, item.Counterparty, StringComparison.OrdinalIgnoreCase) ||
                     candidate.Category == item.Category)),
                SlaBadgeLabel: BuildSlaBadgeLabel(item),
                SlaBadgeTone: BuildSlaBadgeTone(item.SlaState)))
            .ToArray();

        return new ReconciliationBreakQueueProjectionDto(
            TotalCount: projected.Length,
            OpenCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
            InReviewCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
            ResolvedCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
            DismissedCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
            CriticalOpenCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open && item.Severity == ReconciliationBreakSeverity.Critical),
            Items: projected,
            BreachedCount: projected.Count(static item => item.SlaState == ReconciliationCaseSlaState.Breached),
            AwaitingEvidenceCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview && string.Equals(item.SignoffStatus, "awaiting-evidence", StringComparison.OrdinalIgnoreCase)),
            SignedOffEvidenceCount: projected.Where(static item => item.Status == ReconciliationBreakQueueStatus.SignedOff).Sum(static item => item.EvidenceCount));
    }


    private static string? BuildLastCommentExcerpt(ReconciliationBreakQueueItem item)
    {
        var body = item.Comments?
            .Where(static comment => comment.DeletedAt is null && !string.IsNullOrWhiteSpace(comment.Body))
            .OrderByDescending(static comment => comment.EditedAt ?? comment.CreatedAt)
            .Select(static comment => comment.Body.Trim())
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return body.Length <= 120 ? body : string.Concat(body.AsSpan(0, 117), "...");
    }

    private static string BuildSlaBadgeLabel(ReconciliationBreakQueueItem item)
        => item.SlaDueAt.HasValue
            ? $"SLA {item.SlaState}; due {item.SlaDueAt.Value:O}"
            : $"SLA {item.SlaState}";

    private static string BuildSlaBadgeTone(ReconciliationCaseSlaState state)
        => state switch
        {
            ReconciliationCaseSlaState.Breached => "danger",
            ReconciliationCaseSlaState.Warning => "warning",
            ReconciliationCaseSlaState.Paused => "neutral",
            ReconciliationCaseSlaState.Stopped => "success",
            _ => "info"
        };

    private static LedgerImpactPreviewDto BuildLedgerImpactPreview(IReadOnlyList<FundReconciliationItem> items)
    {
        var draftEntryCount = items.Sum(static item => item.TotalBreaks);
        var netDebit = items.Where(static item => item.BreakAmountTotal >= 0m).Sum(static item => item.BreakAmountTotal);
        var netCredit = items.Where(static item => item.BreakAmountTotal < 0m).Sum(static item => Math.Abs(item.BreakAmountTotal));
        var flags = new List<string>();
        if (draftEntryCount > 0)
        {
            flags.Add("draft-entries-present");
        }

        if (items.Any(static item => item.TotalBreaks > 0 && !string.Equals(item.Status, "Resolved", StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add("unresolved-breaks-blocking-close");
        }

        return new LedgerImpactPreviewDto(
            DraftEntryCount: draftEntryCount,
            NetDebitEffect: netDebit,
            NetCreditEffect: netCredit,
            NetBalanceDelta: netDebit - netCredit,
            HasValidationWarnings: flags.Count > 0,
            ValidationFlags: flags);
    }

    private ReconciliationBreakQueueScope? ResolveReconciliationQueueScope(
        ReportAccessQueryContext? accessContext)
    {
        if (_breakQueueRepository is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(accessContext?.TenantId) ||
            string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            throw new InvalidOperationException(
                "A tenant- and company-scoped request context is required to read reconciliation casework.");
        }

        return new ReconciliationBreakQueueScope(accessContext.TenantId, accessContext.CompanyId);
    }

    private static string MapStrategyStatus(ReconciliationRunSummary summary)
        => summary.HasSecurityCoverageIssues
            ? "SecurityCoverageOpen"
            : summary.BreakCount > 0
                ? "BreaksOpen"
                : "Matched";

    private static Guid ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
