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
