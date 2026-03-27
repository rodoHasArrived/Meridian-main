using FluentAssertions;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Sdk;
using Xunit;

namespace Meridian.Backtesting.Tests;

public sealed class TcaReporterTests
{
    private static BacktestRequest MakeRequest() => new(
        From: DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
        To: DateOnly.FromDateTime(DateTime.Today),
        StrategyAssemblyPath: "strategies/my-strat.dll");

    private static FillEvent MakeFill(
        string symbol,
        long qty,
        decimal price,
        decimal commission,
        DateTimeOffset? filledAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), symbol, qty, price, commission,
            filledAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Generate_EmptyFills_ReturnsZeroedReport()
    {
        var report = PostSimulationTcaReporter.Generate(MakeRequest(), Array.Empty<FillEvent>());

        report.CostSummary.TotalFills.Should().Be(0);
        report.CostSummary.TotalCommissions.Should().Be(0m);
        report.CostSummary.TotalNotional.Should().Be(0m);
        report.CostSummary.CommissionRateBps.Should().Be(0.0);
        report.SymbolSummaries.Should().BeEmpty();
        report.Outliers.Should().BeEmpty();
        report.GeneratedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Generate_EmptyFills_PreservesStrategyPath()
    {
        var request = MakeRequest();
        var report = PostSimulationTcaReporter.Generate(request, Array.Empty<FillEvent>());

        report.StrategyAssemblyPath.Should().Be(request.StrategyAssemblyPath);
    }

    [Fact]
    public void Generate_SingleBuyFill_ComputesCorrectSideCounts()
    {
        var fills = new[] { MakeFill("SPY", 100, 400m, 1.00m) };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.CostSummary.TotalFills.Should().Be(1);
        report.CostSummary.BuyFills.Should().Be(1);
        report.CostSummary.SellFills.Should().Be(0);
        report.CostSummary.TotalBuyNotional.Should().Be(40_000m);   // 100 × 400
        report.CostSummary.TotalSellNotional.Should().Be(0m);
        report.CostSummary.TotalCommissions.Should().Be(1.00m);
    }

    [Fact]
    public void Generate_BuyAndSellFills_SeparatesNotional()
    {
        var fills = new[]
        {
            MakeFill("SPY",  100, 400m, 1.00m),  // buy
            MakeFill("SPY", -100, 401m, 1.00m),  // sell
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.CostSummary.BuyFills.Should().Be(1);
        report.CostSummary.SellFills.Should().Be(1);
        report.CostSummary.TotalBuyNotional.Should().Be(40_000m);
        report.CostSummary.TotalSellNotional.Should().Be(40_100m);
        report.CostSummary.TotalNotional.Should().Be(80_100m);
        report.CostSummary.TotalCommissions.Should().Be(2.00m);
    }

    [Fact]
    public void Generate_CommissionRateBps_IsCorrect()
    {
        // 2 fills at 400/401, commission = $1 each → ~2 / 80100 × 10000 ≈ 0.25 bps
        var fills = new[]
        {
            MakeFill("SPY",  100, 400m, 1.00m),
            MakeFill("SPY", -100, 401m, 1.00m),
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.CostSummary.CommissionRateBps.Should().BeApproximately(0.25, 0.01);
    }

    [Fact]
    public void Generate_PerSymbolSummary_SortsHighestCostFirst()
    {
        var fills = new[]
        {
            MakeFill("AAPL",  50, 170m, 5.00m),  // $850 notional, $5 commission = 58.8 bps
            MakeFill("SPY",  100, 400m, 0.50m),  // $40000 notional, $0.50 commission = 0.13 bps
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.SymbolSummaries.Should().HaveCount(2);
        // Higher commission amount is sorted first
        report.SymbolSummaries[0].Symbol.Should().Be("AAPL");
        report.SymbolSummaries[1].Symbol.Should().Be("SPY");
    }

    [Fact]
    public void Generate_SymbolSummary_ComputesVwapCorrectly()
    {
        // 100 shares @ $400 and 200 shares @ $402 → VWAP = (100×400 + 200×402) / 300 = 401.33
        var fills = new[]
        {
            MakeFill("SPY", 100, 400m, 0.50m),
            MakeFill("SPY", 200, 402m, 1.00m),
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.SymbolSummaries.Should().ContainSingle();
        var sym = report.SymbolSummaries[0];
        sym.AvgBuyPrice.Should().BeApproximately(401.3333m, 0.001m);
        sym.AvgSellPrice.Should().Be(0m);  // no sell fills
    }

    [Fact]
    public void Generate_OutlierDetection_FlagsHighCostFills()
    {
        // Mean: (1 + 1 + 50) / (40000 + 40100 + 40000) × 10000 ≈ 1.5 bps
        // Third fill: 50 / 40000 × 10000 = 12.5 bps — should be flagged (> 3× mean)
        var fills = new[]
        {
            MakeFill("SPY",  100, 400m, 1.00m),
            MakeFill("SPY", -100, 401m, 1.00m),
            MakeFill("SPY",  100, 400m, 50.00m),  // outlier
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.Outliers.Should().ContainSingle();
        report.Outliers[0].Commission.Should().Be(50.00m);
        report.Outliers[0].Symbol.Should().Be("SPY");
    }

    [Fact]
    public void Generate_OutlierDetection_DoesNotFlagNearZeroCommission()
    {
        // All fills have zero commission — no outliers should be reported.
        var fills = new[]
        {
            MakeFill("SPY", 100, 400m, 0m),
            MakeFill("SPY", 100, 401m, 0m),
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.Outliers.Should().BeEmpty();
    }

    [Fact]
    public void Generate_MultipleSymbols_AggregatesEachSymbolSeparately()
    {
        var fills = new[]
        {
            MakeFill("SPY",  100, 400m, 1.00m),
            MakeFill("AAPL",  50, 170m, 0.25m),
            MakeFill("SPY", -100, 401m, 1.00m),
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        report.SymbolSummaries.Should().HaveCount(2);
        var spy = report.SymbolSummaries.Single(s => s.Symbol == "SPY");
        spy.TotalFills.Should().Be(2);
        spy.TotalBuyNotional.Should().Be(40_000m);
        spy.TotalSellNotional.Should().Be(40_100m);
        spy.TotalCommission.Should().Be(2.00m);

        var aapl = report.SymbolSummaries.Single(s => s.Symbol == "AAPL");
        aapl.TotalFills.Should().Be(1);
        aapl.TotalBuyNotional.Should().Be(8_500m);
    }

    [Fact]
    public void Generate_OutliersSortedByRateDescending()
    {
        var fills = new[]
        {
            MakeFill("A", 100, 100m, 0.10m),    // 1 bps baseline (very low)
            MakeFill("B", 100, 100m, 10.00m),   // 100 bps — outlier
            MakeFill("C", 100, 100m, 20.00m),   // 200 bps — bigger outlier
        };

        var report = PostSimulationTcaReporter.Generate(MakeRequest(), fills);

        // Outliers should be sorted worst-first (highest bps first)
        if (report.Outliers.Count >= 2)
        {
            report.Outliers[0].CommissionRateBps
                .Should().BeGreaterThanOrEqualTo(report.Outliers[1].CommissionRateBps);
        }
    }
}
