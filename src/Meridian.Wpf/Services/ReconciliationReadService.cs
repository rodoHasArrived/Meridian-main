using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds the Accounting reconciliation workbench summary for a fund profile.
/// Reconciliation posture is read exclusively from the server workstation API — the same
/// <see cref="IWorkstationReconciliationApiClient"/> the workbench break queue already uses —
/// so the summary tiles and the break queue on the same screen share one source of truth
/// (co-equal-lanes contract: neither client forks product state). When the API is
/// unavailable the summary degrades to an empty posture, mirroring the break queue's
/// degrade path; it never falls back to the desktop-local fund-account JSON stores.
/// </summary>
public sealed class ReconciliationReadService
{
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly IWorkstationReconciliationApiClient _reconciliationApiClient;

    public ReconciliationReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        IWorkstationReconciliationApiClient reconciliationApiClient)
    {
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _reconciliationApiClient = reconciliationApiClient ?? throw new ArgumentNullException(nameof(reconciliationApiClient));
    }

    public async Task<ReconciliationSummary> GetAsync(
        string fundProfileId,
        CancellationToken ct = default)
    {
        var runs = await _runWorkspaceService.GetRecordedRunsAsync(ct).ConfigureAwait(false);
        var relevantRuns = runs
            .Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var items = new List<FundReconciliationItem>();
        var openBreaks = 0;
        decimal breakAmountTotal = 0m;
        var securityCoverageIssues = 0;

        foreach (var run in relevantRuns)
        {
            var detail = await ReadLatestRunDetailAsync(run.RunId, ct).ConfigureAwait(false);
            if (detail is null)
            {
                // The server has no recorded reconciliation for this run, or the API read
                // failed. Surface no posture for the run instead of recomputing one from
                // desktop-local state.
                continue;
            }

            var asOf = detail.Summary.PortfolioAsOf
                ?? detail.Summary.LedgerAsOf
                ?? detail.Summary.CreatedAt;
            var strategyBreakAmount = detail.Breaks.Sum(result => Math.Abs(result.Variance));
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

        var ordered = items
            .OrderByDescending(item => item.RequestedAt)
            .ToArray();

        return new ReconciliationSummary(
            RunCount: ordered.Length,
            OpenBreakCount: openBreaks,
            BreakAmountTotal: breakAmountTotal,
            RecentRuns: ordered,
            SecurityCoverageIssueCount: securityCoverageIssues);
    }

    private async Task<ReconciliationRunDetail?> ReadLatestRunDetailAsync(string runId, CancellationToken ct)
    {
        try
        {
            return await _reconciliationApiClient.GetLatestRunDetailAsync(runId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Same degrade contract as the workbench break queue: an API outage renders an
            // empty posture, never a desktop-local recomputation.
            return null;
        }
    }

    private static string MapStrategyStatus(ReconciliationRunSummary summary)
    {
        if (summary.HasSecurityCoverageIssues)
        {
            return "SecurityCoverageOpen";
        }

        return summary.BreakCount > 0 ? "BreaksOpen" : "Matched";
    }

    private static Guid ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
