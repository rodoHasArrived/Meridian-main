using System.Security.Cryptography;
using System.Text;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Builds governance reconciliation posture across all accounts linked to a fund profile.
/// </summary>
public sealed class ReconciliationReadService
{
    private readonly IFundAccountService _fundAccountService;
    private readonly FundAccountReadService _fundAccountReadService;
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly IReconciliationRunService? _strategyReconciliationService;

    public ReconciliationReadService(
        IFundAccountService fundAccountService,
        FundAccountReadService fundAccountReadService,
        StrategyRunWorkspaceService runWorkspaceService,
        IReconciliationRunService? strategyReconciliationService = null)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _fundAccountReadService = fundAccountReadService ?? throw new ArgumentNullException(nameof(fundAccountReadService));
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _strategyReconciliationService = strategyReconciliationService;
    }

    public async Task<ReconciliationSummary> GetAsync(
        string fundProfileId,
        CancellationToken ct = default)
    {
        var accounts = await _fundAccountReadService.GetAccountsAsync(fundProfileId, ct).ConfigureAwait(false);
        var items = new List<FundReconciliationItem>();
        var openBreaks = 0;
        decimal breakAmountTotal = 0m;
        var securityCoverageIssues = 0;

        foreach (var account in accounts)
        {
            var runs = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);

            foreach (var run in runs)
            {
                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: run.ReconciliationRunId,
                    AccountId: run.AccountId,
                    AccountDisplayName: account.DisplayName,
                    AsOfDate: run.AsOfDate,
                    Status: run.Status,
                    TotalChecks: run.TotalChecks,
                    TotalMatched: run.TotalMatched,
                    TotalBreaks: run.TotalBreaks,
                    BreakAmountTotal: run.BreakAmountTotal,
                    RequestedAt: run.RequestedAt,
                    CompletedAt: run.CompletedAt,
                    ScopeLabel: "Account",
                    CoverageLabel: "Account-level reconciliation"));

                if (!string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                {
                    openBreaks += run.TotalBreaks;
                    breakAmountTotal += run.BreakAmountTotal;
                }
            }
        }

        if (_strategyReconciliationService is not null)
        {
            var runs = await _runWorkspaceService.GetRecordedRunsAsync(ct).ConfigureAwait(false);
            var relevantRuns = runs
                .Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var run in relevantRuns)
            {
                var detail = await _strategyReconciliationService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false)
                    ?? await _strategyReconciliationService
                        .RunAsync(new ReconciliationRunRequest(run.RunId), ct)
                        .ConfigureAwait(false);

                if (detail is null)
                {
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
