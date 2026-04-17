using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

/// <summary>
/// Fund-level cash, financing, and capital-control projection for governance and trading shells.
/// </summary>
public sealed class CashFinancingReadService
{
    private const int ProjectionBucketDays = 7;

    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly FundAccountReadService _fundAccountReadService;

    public CashFinancingReadService(
        StrategyRunWorkspaceService runWorkspaceService,
        FundAccountReadService fundAccountReadService)
    {
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _fundAccountReadService = fundAccountReadService ?? throw new ArgumentNullException(nameof(fundAccountReadService));
    }

    public async Task<CashFinancingSummary> GetAsync(
        string fundProfileId,
        string currency,
        CancellationToken ct = default)
    {
        var accountsTask = _fundAccountReadService.GetAccountsAsync(fundProfileId, ct);
        var runsTask = _runWorkspaceService.GetRecordedRunsAsync(ct);

        await Task.WhenAll(accountsTask, runsTask).ConfigureAwait(false);

        var accounts = await accountsTask.ConfigureAwait(false);
        var runs = await runsTask.ConfigureAwait(false);
        var relevantRuns = runs
            .Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        decimal totalCash = accounts.Sum(account => account.CashBalance);
        decimal pendingSettlement = accounts.Sum(account => Math.Max(account.SecuritiesMarketValue - account.NetAssetValue + account.CashBalance, 0m));
        decimal financing = 0m;
        decimal realized = 0m;
        decimal unrealized = 0m;
        decimal longMarketValue = 0m;
        decimal shortMarketValue = 0m;
        decimal grossExposure = 0m;
        decimal netExposure = 0m;
        decimal totalEquity = 0m;

        var portfolioTasks = relevantRuns
            .Select(run => _runWorkspaceService.GetPortfolioAsync(run.RunId, ct))
            .ToArray();
        var cashFlowTasks = relevantRuns
            .Select(run => _runWorkspaceService.GetCashFlowAsync(run.RunId, currency, ProjectionBucketDays, ct))
            .ToArray();

        var portfolios = await Task.WhenAll(portfolioTasks).ConfigureAwait(false);
        var cashFlows = await Task.WhenAll(cashFlowTasks).ConfigureAwait(false);

        foreach (var portfolio in portfolios)
        {
            if (portfolio is null)
            {
                continue;
            }

            financing += portfolio.Financing;
            realized += portfolio.RealizedPnl;
            unrealized += portfolio.UnrealizedPnl;
            longMarketValue += portfolio.LongMarketValue;
            shortMarketValue += portfolio.ShortMarketValue;
            grossExposure += portfolio.GrossExposure;
            netExposure += portfolio.NetExposure;
            totalEquity += portfolio.TotalEquity;
        }

        var cashFlowSummaries = cashFlows
            .Where(static summary => summary is not null)
            .Select(static summary => summary!)
            .ToArray();

        var projectedInflows = cashFlowSummaries.Sum(static summary => summary.TotalInflows);
        var projectedOutflows = cashFlowSummaries.Sum(static summary => summary.TotalOutflows);
        var projectedNetCashFlow = cashFlowSummaries.Sum(static summary => summary.NetCashFlow);
        var aggregatedEntries = cashFlowSummaries
            .SelectMany(static summary => summary.Entries)
            .OrderBy(static entry => entry.Timestamp)
            .ThenBy(static entry => entry.EventKind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var aggregatedBuckets = cashFlowSummaries
            .SelectMany(static summary => summary.Ladder.Buckets)
            .GroupBy(
                static bucket => (bucket.BucketStart, bucket.BucketEnd, bucket.Currency),
                static bucket => bucket)
            .OrderBy(static group => group.Key.BucketStart)
            .Select(static group => new CashLadderBucketDto(
                BucketStart: group.Key.BucketStart,
                BucketEnd: group.Key.BucketEnd,
                ProjectedInflows: group.Sum(static bucket => bucket.ProjectedInflows),
                ProjectedOutflows: group.Sum(static bucket => bucket.ProjectedOutflows),
                NetFlow: group.Sum(static bucket => bucket.NetFlow),
                Currency: group.Key.Currency,
                EventCount: group.Sum(static bucket => bucket.EventCount)))
            .ToArray();

        var highlights = new List<string>
        {
            accounts.Count == 0
                ? "No linked fund accounts have been configured yet."
                : $"{accounts.Count} fund account(s) are contributing banking and custody balances.",
            relevantRuns.Length == 0
                ? "No recorded fund-scoped runs are contributing portfolio posture yet."
                : $"{relevantRuns.Length} recorded run(s) are contributing capital posture.",
            financing == 0m
                ? "No financing costs have been recorded for the current fund scope."
                : $"Financing costs total {financing:C2} across linked runs.",
            cashFlowSummaries.Length == 0
                ? "No run-derived cash-flow projections are available yet."
                : $"{aggregatedEntries.Length} cash-flow event(s) are grouped into {aggregatedBuckets.Length} projection bucket(s)."
        };

        return new CashFinancingSummary(
            Currency: currency,
            TotalCash: totalCash,
            PendingSettlement: pendingSettlement,
            FinancingCost: financing,
            MarginBalance: 0m,
            RealizedPnl: realized,
            UnrealizedPnl: unrealized,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            TotalEquity: totalEquity,
            Highlights: highlights,
            CashFlowEntryCount: aggregatedEntries.Length,
            ProjectedInflows: projectedInflows,
            ProjectedOutflows: projectedOutflows,
            NetProjectedCashFlow: projectedNetCashFlow,
            ProjectionBucketDays: ProjectionBucketDays,
            CashFlowBuckets: aggregatedBuckets,
            CashFlowEntries: aggregatedEntries);
    }
}
