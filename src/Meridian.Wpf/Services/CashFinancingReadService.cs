using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

/// <summary>
/// Fund-level cash, financing, and capital-control projection for governance and trading shells.
/// </summary>
public sealed class CashFinancingReadService
{
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

<<<<<<< ours
        var accounts = await accountsTask;
        var relevantRuns = (await runsTask)
=======
        var accounts = await accountsTask.ConfigureAwait(false);
        var runs = await runsTask.ConfigureAwait(false);
        var relevantRuns = runs
>>>>>>> theirs
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

        foreach (var run in relevantRuns)
        {
            var portfolio = await _runWorkspaceService.GetPortfolioAsync(run.RunId, ct).ConfigureAwait(false);
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
                : $"Financing costs total {financing:C2} across linked runs."
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
            Highlights: highlights);
    }
}
