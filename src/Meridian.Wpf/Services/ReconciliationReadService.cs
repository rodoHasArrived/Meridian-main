using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds the Accounting reconciliation workbench summary for a fund profile.
/// Reconciliation posture is read exclusively from the server workstation API — the same
/// <see cref="IWorkstationReconciliationApiClient"/> the workbench break queue already uses —
/// so the summary tiles and the break queue on the same screen share one source of truth
/// (co-equal-lanes contract: neither client forks product state). When the API is
/// unavailable the result preserves that failure separately from a confirmed missing
/// reconciliation record; it never falls back to the desktop-local fund-account JSON stores.
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

    public async Task<FundReconciliationReadResult> GetAsync(
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
        var missingRunCount = 0;
        var unavailableRunCount = 0;

        foreach (var run in relevantRuns)
        {
            var detailRead = await ReadLatestRunDetailAsync(run.RunId, ct).ConfigureAwait(false);
            if (detailRead.State == ReconciliationDetailReadState.Missing)
            {
                missingRunCount++;
                continue;
            }

            if (detailRead.State == ReconciliationDetailReadState.Unavailable)
            {
                unavailableRunCount++;
                continue;
            }

            var detail = detailRead.Detail!;

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

        return new FundReconciliationReadResult(
            Summary: new ReconciliationSummary(
                RunCount: ordered.Length,
                OpenBreakCount: openBreaks,
                BreakAmountTotal: breakAmountTotal,
                RecentRuns: ordered,
                SecurityCoverageIssueCount: securityCoverageIssues),
            KnownRunCount: relevantRuns.Length,
            MissingRunCount: missingRunCount,
            UnavailableRunCount: unavailableRunCount);
    }

    private async Task<ReconciliationDetailRead> ReadLatestRunDetailAsync(string runId, CancellationToken ct)
    {
        try
        {
            var detail = await _reconciliationApiClient.GetLatestRunDetailAsync(runId, ct).ConfigureAwait(false);
            return detail is null
                ? new ReconciliationDetailRead(ReconciliationDetailReadState.Missing, null)
                : new ReconciliationDetailRead(ReconciliationDetailReadState.Available, detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ReconciliationDetailRead(ReconciliationDetailReadState.Unavailable, null);
        }
    }

    private enum ReconciliationDetailReadState : byte
    {
        Available = 0,
        Missing = 1,
        Unavailable = 2
    }

    private readonly record struct ReconciliationDetailRead(
        ReconciliationDetailReadState State,
        ReconciliationRunDetail? Detail);

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
