using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Live;
using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Unit contract tests for <see cref="LiveRunMetricsTracker"/>.
///
/// <para>
/// Regression guard: this tracker and <c>BacktestMetricsEngine</c> both populate the same
/// <see cref="BacktestMetrics"/> record, so they must agree on units. The tracker previously
/// wrote <c>MaxDrawdownPercent</c> in percent units (15.0) while the metrics engine wrote a
/// fraction (0.15). Because the workstation multiplies by 100 at render time, live and paper
/// runs displayed drawdown 100x too large.
/// </para>
/// </summary>
public sealed class LiveRunMetricsTrackerTests
{
    private static readonly DateTimeOffset StartedAt = new(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Equity 1,000 -> 1,100 (peak) -> 950 (trough) -> 1,050.
    /// Worst drawdown is 1,100 -> 950 = 150, i.e. 150/1100 ≈ 0.13636 as a fraction of peak.
    /// </summary>
    [Fact]
    public void Build_MaxDrawdownPercent_IsAFractionOfPeakEquityNotPercentUnits()
    {
        var metrics = BuildMetricsFor([1_100m, 950m, 1_050m], initialEquity: 1_000m, finalEquity: 1_050m);

        metrics.MaxDrawdown.Should().Be(150m, "the worst peak-to-trough decline is 1100 - 950");

        metrics.MaxDrawdownPercent.Should().BeApproximately(150m / 1_100m, 1e-10m,
            "MaxDrawdownPercent is a FRACTION of peak equity (~0.1364), matching BacktestMetricsEngine; " +
            "emitting percent units (13.64) here renders as a 1364% drawdown in the workstation");

        metrics.MaxDrawdownPercent.Should().BeLessThan(1m,
            "a drawdown that never exceeded 100% of peak equity must stay below 1.0 in fraction units");
    }

    /// <summary>
    /// Calmar is annualized return divided by max drawdown. Both operands must be in the same
    /// units; the tracker previously compensated for its percent-unit drawdown by dividing by
    /// 100 here, so changing the drawdown unit without changing this would break Calmar.
    /// </summary>
    [Fact]
    public void Build_CalmarRatio_DividesAnnualizedReturnByTheSameFractionItReports()
    {
        var metrics = BuildMetricsFor([1_100m, 950m, 1_050m], initialEquity: 1_000m, finalEquity: 1_050m);

        var expectedCalmar = (double)(metrics.AnnualizedReturn / metrics.MaxDrawdownPercent);

        metrics.CalmarRatio.Should().BeApproximately(expectedCalmar, 1e-9,
            "Calmar must be derived from the same drawdown fraction the tracker reports");
    }

    [Fact]
    public void Build_WhenEquityOnlyRises_ReportsZeroDrawdownAndZeroCalmar()
    {
        var metrics = BuildMetricsFor([1_050m, 1_100m, 1_200m], initialEquity: 1_000m, finalEquity: 1_200m);

        metrics.MaxDrawdown.Should().Be(0m);
        metrics.MaxDrawdownPercent.Should().Be(0m);
        metrics.CalmarRatio.Should().Be(0, "Calmar is undefined without a drawdown and is reported as zero");
    }

    /// <summary>
    /// Regression guard for the fixed-income booking convention carried by
    /// <c>TradeExecutedEvent.GrossValue</c> and the OMS fill stamp: a face-value fill quotes its
    /// price as a percentage of par, so 100,000 face at 101.25 is a $101,250 cash movement.
    /// The tracker previously multiplied quantity by the raw price and booked $10,125,000.
    /// </summary>
    [Fact]
    public void RecordFill_FaceValuePercentOfParFill_BooksTheParScaledCashFlow()
    {
        var tracker = new LiveRunMetricsTracker(initialEquity: 1_000_000m, StartedAt);

        tracker.RecordFill(
            CreateFill(symbol: "US912828XG55", quantity: 100_000, price: 101.25m),
            StartedAt,
            usesFaceValuePercentageOfPar: true);

        var cashFlow = BuildCashFlows(tracker).Should().ContainSingle().Subject;
        cashFlow.Amount.Should().Be(-101_250m,
            "a buy of 100,000 face at a clean price of 101.25 percent of par spends $101,250, not $10,125,000");
        cashFlow.Quantity.Should().Be(100_000);
        cashFlow.Price.Should().Be(101.25m, "the cash flow still reports the price as it was quoted");
    }

    [Fact]
    public void RecordFill_FaceValueSell_BooksParScaledProceeds()
    {
        var tracker = new LiveRunMetricsTracker(initialEquity: 1_000_000m, StartedAt);

        // Sells carry a negative signed quantity, so the proceeds come back positive.
        tracker.RecordFill(
            CreateFill(symbol: "US912828XG55", quantity: -100_000, price: 99.50m),
            StartedAt,
            usesFaceValuePercentageOfPar: true);

        BuildCashFlows(tracker).Should().ContainSingle()
            .Which.Amount.Should().Be(99_500m, "selling 100,000 face at 99.50 percent of par raises $99,500");
    }

    [Fact]
    public void RecordFill_EquityFill_StillBooksRawQuantityTimesPrice()
    {
        var tracker = new LiveRunMetricsTracker(initialEquity: 1_000_000m, StartedAt);

        tracker.RecordFill(CreateFill(symbol: "AAPL", quantity: 100, price: 50.25m), StartedAt);

        BuildCashFlows(tracker).Should().ContainSingle()
            .Which.Amount.Should().Be(-5_025m, "an outright equity fill books quantity times price unscaled");
    }

    private static FillEvent CreateFill(string symbol, long quantity, decimal price) => new(
        FillId: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        Symbol: symbol,
        FilledQuantity: quantity,
        FillPrice: price,
        Commission: 0m,
        FilledAt: StartedAt);

    private static IReadOnlyList<TradeCashFlow> BuildCashFlows(LiveRunMetricsTracker tracker) => tracker
        .Build(
            engineId: "BrokerPaper",
            universe: new HashSet<string>(),
            finalEquity: 1_000_000m,
            ledger: new MeridianLedger(),
            endedAt: StartedAt.AddDays(1))
        .CashFlows
        .OfType<TradeCashFlow>()
        .ToArray();

    private static BacktestMetrics BuildMetricsFor(
        IReadOnlyList<decimal> dailyEquity,
        decimal initialEquity,
        decimal finalEquity)
    {
        var tracker = new LiveRunMetricsTracker(initialEquity, StartedAt);
        var date = DateOnly.FromDateTime(StartedAt.UtcDateTime);

        for (var i = 0; i < dailyEquity.Count; i++)
        {
            tracker.RecordDayEnd(
                date.AddDays(i + 1),
                cash: dailyEquity[i],
                equity: dailyEquity[i],
                positions: new Dictionary<string, Position>(),
                accounts: new Dictionary<string, FinancialAccountSnapshot>());
        }

        var result = tracker.Build(
            engineId: "BrokerPaper",
            universe: new HashSet<string>(),
            finalEquity: finalEquity,
            ledger: new MeridianLedger(),
            endedAt: StartedAt.AddDays(dailyEquity.Count));

        return result.Metrics;
    }
}
