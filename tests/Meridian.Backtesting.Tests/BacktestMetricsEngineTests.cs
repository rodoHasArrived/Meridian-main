using FluentAssertions;
using Meridian.Backtesting.Metrics;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Unit tests for <see cref="BacktestMetricsEngine"/> covering the statistical helpers
/// that are invisible through the full RunAsync integration path.
/// </summary>
public sealed class BacktestMetricsEngineTests
{

    [Fact]
    public void Compute_WithFrictionCashFlows_NetPnlIsEquityDeltaAndGrossAddsFrictionsBack()
    {
        // TotalEquity already includes the effect of commissions, margin interest, and short
        // rebates: SimulatedPortfolio posts all of them through cash. Net P&L must therefore be
        // the plain equity delta — the previous formula subtracted the frictions a second time,
        // understating Net P&L on every friction-bearing run.
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100_000m, 100_400m, 100_800m, 101_000m], startDate);
        var ts = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var cashFlows = new List<CashFlowEntry>
        {
            new CommissionCashFlow(ts, -125m, "AAPL", Guid.NewGuid()),
            new CommissionCashFlow(ts.AddDays(1), -75m, "AAPL", Guid.NewGuid()),
            new MarginInterestCashFlow(ts.AddDays(2), -40m, MarginBalance: 20_000m, AnnualRate: 0.06),
            new ShortRebateCashFlow(ts.AddDays(2), 15m, "SPY", ShortShares: 100, AnnualRebateRate: 0.01)
        };

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(3),
            InitialCash: 100_000m,
            RiskFreeRate: 0.0);

        var metrics = BacktestMetricsEngine.Compute(snapshots, cashFlows, [], request);

        metrics.NetPnl.Should().Be(1_000m, "net P&L is FinalEquity - InitialCapital; frictions are already in equity");
        metrics.NetPnl.Should().Be(metrics.FinalEquity - metrics.InitialCapital);
        metrics.TotalCommissions.Should().Be(200m);
        metrics.TotalMarginInterest.Should().Be(40m);
        metrics.TotalShortRebates.Should().Be(15m);
        metrics.GrossPnl.Should().Be(1_225m, "gross P&L adds commissions and interest back and removes rebates");
    }

    [Fact]
    public void Compute_ScalarRiskFreeRateFallback_UsesConstantDailyExcessReturns()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100m, 102m, 101m, 103m], startDate);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(3),
            InitialCash: 100m,
            RiskFreeRate: 0.05);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        var returns = snapshots.Select(s => (double)s.DailyReturn).ToList();
        var dailyRf = 0.05 / 252.0;
        var excess = returns.Select(r => r - dailyRf).ToList();
        var mean = excess.Average();
        var std = SampleStdDev(excess);
        var downside = excess.Where(r => r < 0).ToList();
        var downsideDev = Math.Sqrt(downside.Select(r => r * r).Average());
        var expectedSharpe = mean / std * Math.Sqrt(252.0);
        var expectedSortino = mean / downsideDev * Math.Sqrt(252.0);

        metrics.SharpeRatio.Should().BeApproximately(expectedSharpe, 1e-10);
        metrics.SortinoRatio.Should().BeApproximately(expectedSortino, 1e-10);
    }

    [Fact]
    public void Compute_RiskFreeRateSeries_UsesDateMatchedRatesWithFallback()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100m, 102m, 101m, 103m], startDate);

        var series = new Dictionary<DateOnly, double>
        {
            [startDate] = 0.00,
            [startDate.AddDays(1)] = 0.252, // daily 0.1%
            [startDate.AddDays(3)] = 0.126  // daily 0.05%
        };

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(3),
            InitialCash: 100m,
            RiskFreeRate: 0.05,
            RiskFreeRateSeries: series);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        var expectedExcess = snapshots
            .Select(s =>
            {
                var annual = series.TryGetValue(s.Date, out var rate) ? rate : 0.05;
                return (double)s.DailyReturn - annual / 252.0;
            })
            .ToList();
        var mean = expectedExcess.Average();
        var std = SampleStdDev(expectedExcess);
        var downside = expectedExcess.Where(r => r < 0).ToList();
        var downsideDev = Math.Sqrt(downside.Select(r => r * r).Average());

        metrics.SharpeRatio.Should().BeApproximately(mean / std * Math.Sqrt(252.0), 1e-10);
        metrics.SortinoRatio.Should().BeApproximately(mean / downsideDev * Math.Sqrt(252.0), 1e-10);
    }

    // ------------------------------------------------------------------ //
    //  ComputeMaxDrawdown — explicit peak tracking (regression: recovery  //
    //  days must use the recorded peak, not an algebraic reconstruction) //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Regression test: the recovery-day calculation in ComputeMaxDrawdown must use the
    /// peak equity value recorded at the time the maximum trough was observed, NOT an
    /// algebraically reconstructed threshold.  This ensures correctness even if the
    /// metrics engine is later extended with per-period drawdown computations.
    /// <para>
    /// Equity series: 1 000 → 1 100 (new peak) → 950 (trough, dd=13.6%) → 1 100 (recovery) → 1 200.
    /// Max drawdown is from 1 100 to 950; recovery must be measured back to the same 1 100 peak.
    /// </para>
    /// </summary>
    [Fact]
    public void Compute_MaxDrawdownWithRecovery_ReturnsCorrectRecoveryDays()
    {
        var startDate = new DateOnly(2024, 1, 2);

        var snapshots = BuildSnapshots([1_000m, 1_100m, 950m, 1_100m, 1_200m], startDate);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(4),
            InitialCash: 1_000m);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        // Max drawdown: from equity 1 100 (day 2) to 950 (day 3)
        metrics.MaxDrawdown.Should().Be(150m, "drawdown is 1100 - 950 = 150");
        metrics.MaxDrawdownPercent.Should().BeApproximately(150m / 1_100m, 1e-10m,
            "drawdown % = 150 / 1100 ≈ 13.64%");

        // Recovery: trough on day 3 (index 2), equity returns to ≥ 1 100 on day 4 (index 3).
        // Recovery days = day4 - day3 = 1 calendar day.
        metrics.MaxDrawdownRecoveryDays.Should().Be(1,
            "the portfolio recovers from the trough (day 3) to the peak (1100) on day 4 — 1 calendar day");
    }

    /// <summary>
    /// When the portfolio never recovers from its worst drawdown (equity stays below the peak
    /// through the end of the simulation), RecoveryDays must be 0.
    /// </summary>
    [Fact]
    public void Compute_MaxDrawdownWithNoRecovery_RecoveryDaysIsZero()
    {
        var startDate = new DateOnly(2024, 1, 2);
        // Equity rises then falls and never gets back to the peak.
        var snapshots = BuildSnapshots([1_000m, 1_100m, 900m, 950m, 1_050m], startDate);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(4),
            InitialCash: 1_000m);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        metrics.MaxDrawdown.Should().Be(200m, "drawdown is 1100 - 900 = 200");
        metrics.MaxDrawdownRecoveryDays.Should().Be(0,
            "the portfolio never recovers to the 1100 peak by end of period; recovery days must be 0");
    }

    /// <summary>
    /// When there is no drawdown at all (monotonically increasing equity), both MaxDrawdown and
    /// RecoveryDays must be zero.
    /// </summary>
    [Fact]
    public void Compute_NoDrawdown_DrawdownAndRecoveryDaysAreZero()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([1_000m, 1_050m, 1_100m, 1_150m], startDate);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(3),
            InitialCash: 1_000m);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        metrics.MaxDrawdown.Should().Be(0m);
        metrics.MaxDrawdownPercent.Should().Be(0m);
        metrics.MaxDrawdownRecoveryDays.Should().Be(0);
    }

    /// <summary>
    /// When the largest drawdown occurs from the second peak (not the first), the recovery
    /// threshold must be the equity at the SECOND peak, not the global all-time high.
    /// This validates that <c>peakAtTrough</c> is updated correctly throughout the scan.
    /// </summary>
    [Fact]
    public void Compute_LargestDrawdownFromSecondPeak_RecoveryMeasuredFromSecondPeak()
    {
        var startDate = new DateOnly(2024, 1, 2);
        // Day 1:  900 (start)
        // Day 2: 1 000 (first peak)
        // Day 3:  950 (minor trough: dd 5%)
        // Day 4: 1 200 (new, higher peak)
        // Day 5:  800 (major trough: dd from 1200 = 33.3%)
        // Day 6: 1 200 (recovery back to day-4 peak)
        var snapshots = BuildSnapshots([900m, 1_000m, 950m, 1_200m, 800m, 1_200m], startDate);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(5),
            InitialCash: 900m);

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], [], request);

        // The max drawdown trough is 800 (from peak 1 200).
        metrics.MaxDrawdown.Should().Be(400m, "max drawdown is 1200 - 800 = 400");
        metrics.MaxDrawdownPercent.Should().BeApproximately(400m / 1_200m, 1e-10m);

        // Recovery is from the trough on day 5 (index 4) back to 1 200 on day 6 (index 5).
        metrics.MaxDrawdownRecoveryDays.Should().Be(1,
            "the second peak (1200) is the correct recovery threshold; recovery occurs the next day");
    }

    // ------------------------------------------------------------------ //
    //  Helper                                                             //
    // ------------------------------------------------------------------ //

    private static double SampleStdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0.0;
        }

        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    [Fact]
    public void Compute_Attribution_HonoursLifoLotSelection()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100m, 101m], startDate);
        var t0 = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02) with
        {
            Rules = new FinancialAccountRules(LotSelection: LotSelectionMethod.Lifo)
        };

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(1),
            InitialCash: 100_000m,
            Accounts: [account]);

        var fills = new List<FillEvent>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, t0),
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 120m, 0m, t0.AddHours(1)),
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", -10L, 130m, 0m, t0.AddHours(2)),
        };

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], fills, request);

        // LIFO relieves the 120 lot: 10 x (130 - 120). FIFO would report 300.
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(100m);
    }

    [Fact]
    public void Compute_Attribution_RealisesShortCovers()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100m, 101m], startDate);
        var t0 = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(1),
            InitialCash: 100_000m);

        var fills = new List<FillEvent>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", -10L, 100m, 0m, t0),
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 90m, 0m, t0.AddHours(1)),
        };

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], fills, request);

        // Short 10 @ 100 covered @ 90: realized +100 (previously ignored by the long-only matcher).
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(100m);
    }

    [Fact]
    public void Compute_Attribution_MatchesLotsPerAccount()
    {
        var startDate = new DateOnly(2024, 1, 2);
        var snapshots = BuildSnapshots([100m, 101m], startDate);
        var t0 = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var accountA = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountB = accountA with { AccountId = "acct-b" };

        var request = new BacktestRequest(
            From: startDate,
            To: startDate.AddDays(1),
            InitialCash: 100_000m,
            Accounts: [accountA, accountB]);

        var fills = new List<FillEvent>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, t0, accountA.AccountId),
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 120m, 0m, t0.AddHours(1), accountB.AccountId),
            new(Guid.NewGuid(), Guid.NewGuid(), "SPY", -10L, 130m, 0m, t0.AddHours(2), accountB.AccountId),
        };

        var metrics = BacktestMetricsEngine.Compute(snapshots, [], fills, request);

        // Account B's sell relieves account B's 120 lot; account A's 100 lot stays open.
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(100m);
    }

    private static IReadOnlyList<PortfolioSnapshot> BuildSnapshots(
        IEnumerable<decimal> equityValues,
        DateOnly startDate)
    {
        var snapshots = new List<PortfolioSnapshot>();
        var day = startDate;
        decimal prev = 0m;

        foreach (var equity in equityValues)
        {
            var dailyReturn = prev == 0m ? 0m : (equity - prev) / prev;
            var ts = new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            snapshots.Add(new PortfolioSnapshot(
                Timestamp: ts,
                Date: day,
                Cash: equity,
                MarginBalance: 0m,
                LongMarketValue: 0m,
                ShortMarketValue: 0m,
                TotalEquity: equity,
                DailyReturn: dailyReturn,
                Positions: new Dictionary<string, Position>(),
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
                DayCashFlows: []));
            prev = equity;
            day = day.AddDays(1);
        }

        return snapshots;
    }
}
