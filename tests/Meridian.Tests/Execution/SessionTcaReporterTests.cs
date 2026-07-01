using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class SessionTcaReporterTests
{
    private static ExecutionReport MakeFill(
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal price,
        decimal? commission = null,
        string? orderId = null,
        DateTimeOffset? timestamp = null,
        ExecutionReportType reportType = ExecutionReportType.Fill) => new()
    {
        OrderId = orderId ?? $"order-{Guid.NewGuid():N}",
        ReportType = reportType,
        Symbol = symbol,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Commission = commission,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow
    };

    private static OrderState MakeOrder(
        string orderId,
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal? limitPrice = null,
        DateTimeOffset? createdAt = null) => new()
    {
        OrderId = orderId,
        Symbol = symbol,
        Side = side,
        Type = limitPrice.HasValue ? OrderType.Limit : OrderType.Market,
        Quantity = quantity,
        FilledQuantity = quantity,
        LimitPrice = limitPrice,
        Status = OrderStatus.Filled,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public void Generate_EmptyFills_ReturnsZeroedReport()
    {
        var report = SessionTcaReporter.Generate("session-1", "strategy-1", Array.Empty<ExecutionReport>());

        report.SessionId.Should().Be("session-1");
        report.StrategyId.Should().Be("strategy-1");
        report.CostSummary.TotalFills.Should().Be(0);
        report.CostSummary.TotalNotional.Should().Be(0m);
        report.CostSummary.CommissionRateBps.Should().Be(0.0);
        report.SymbolSummaries.Should().BeEmpty();
        report.Outliers.Should().BeEmpty();
        report.ExecutionQuality.Should().Be(SessionTcaExecutionQuality.Empty);
    }

    [Fact]
    public void Generate_IgnoresNonFillReportsAndFillsWithoutPrice()
    {
        var fills = new[]
        {
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 1m),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, reportType: ExecutionReportType.Rejected),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, reportType: ExecutionReportType.Cancelled),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m) with { FillPrice = null },
            MakeFill("SPY", OrderSide.Buy, 0m, 400m)
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.CostSummary.TotalFills.Should().Be(1);
        report.CostSummary.TotalBuyNotional.Should().Be(40_000m);
    }

    [Fact]
    public void Generate_ComputesSideSplitAndCommissionRate()
    {
        var fills = new[]
        {
            // Buy: 100 × 400 = 40,000 notional, $2 commission
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 2m),
            // Sell: 50 × 402 = 20,100 notional, $1 commission
            MakeFill("SPY", OrderSide.Sell, 50m, 402m, commission: 1m)
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.CostSummary.TotalBuyNotional.Should().Be(40_000m);
        report.CostSummary.TotalSellNotional.Should().Be(20_100m);
        report.CostSummary.TotalNotional.Should().Be(60_100m);
        report.CostSummary.TotalCommissions.Should().Be(3m);
        report.CostSummary.BuyFills.Should().Be(1);
        report.CostSummary.SellFills.Should().Be(1);
        // 3 / 60,100 × 10,000 ≈ 0.499 bps
        report.CostSummary.CommissionRateBps.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void Generate_ComputesPerSymbolVwapAndSortsByCommission()
    {
        var fills = new[]
        {
            MakeFill("AAPL", OrderSide.Buy, 100m, 200m, commission: 0.5m),
            MakeFill("AAPL", OrderSide.Buy, 300m, 210m, commission: 0.5m),
            MakeFill("MSFT", OrderSide.Sell, 10m, 500m, commission: 5m)
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.SymbolSummaries.Should().HaveCount(2);
        // MSFT first: higher total commission.
        report.SymbolSummaries[0].Symbol.Should().Be("MSFT");
        report.SymbolSummaries[0].AvgSellPrice.Should().Be(500m);

        var aapl = report.SymbolSummaries[1];
        aapl.Symbol.Should().Be("AAPL");
        // VWAP = (100×200 + 300×210) / 400 = 207.5
        aapl.AvgBuyPrice.Should().Be(207.5m);
        aapl.TotalBuyNotional.Should().Be(83_000m);
        aapl.TotalFills.Should().Be(2);
    }

    [Fact]
    public void Generate_FlagsCommissionOutliersAboveMedianMultiple()
    {
        var fills = new[]
        {
            // Baseline fills at ~0.25 bps (1 / 40,000 × 10,000)
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 1m),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 1m),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 1m),
            // Outlier at 25 bps (100 / 40,000 × 10,000) — > 3× median and > 1 bps floor.
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 100m, orderId: "outlier-order")
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.Outliers.Should().ContainSingle();
        report.Outliers[0].OrderId.Should().Be("outlier-order");
        report.Outliers[0].CommissionRateBps.Should().BeApproximately(25.0, 0.01);
    }

    [Fact]
    public void Generate_DoesNotFlagOutliersBelowMinimumRateFloor()
    {
        var fills = new[]
        {
            // All commissions well below the 1 bps outlier floor.
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 0.001m),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 0.001m),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 0.03m)
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.Outliers.Should().BeEmpty();
    }

    [Fact]
    public void Generate_TreatsMissingCommissionAsZero()
    {
        var fills = new[] { MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: null) };

        var report = SessionTcaReporter.Generate("s", "strat", fills);

        report.CostSummary.TotalCommissions.Should().Be(0m);
        report.CostSummary.CommissionRateBps.Should().Be(0.0);
        report.Outliers.Should().BeEmpty();
    }

    [Fact]
    public void Generate_ComputesTimeToFillFromOrderHistory()
    {
        var created = DateTimeOffset.UtcNow;
        var orders = new[]
        {
            MakeOrder("o1", "SPY", OrderSide.Buy, 100m, createdAt: created),
            MakeOrder("o2", "SPY", OrderSide.Buy, 100m, createdAt: created)
        };
        var fills = new[]
        {
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, orderId: "o1", timestamp: created.AddSeconds(2)),
            MakeFill("SPY", OrderSide.Buy, 100m, 400m, orderId: "o2", timestamp: created.AddSeconds(6))
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills, orders);

        report.ExecutionQuality.TimedFillCount.Should().Be(2);
        report.ExecutionQuality.MedianTimeToFillSeconds.Should().BeApproximately(4.0, 0.001);
    }

    [Fact]
    public void Generate_ComputesLimitPriceImprovement()
    {
        var created = DateTimeOffset.UtcNow;
        var orders = new[]
        {
            // Buy limit at 100, filled at 99.90 → 10 bps improvement.
            MakeOrder("buy-order", "SPY", OrderSide.Buy, 100m, limitPrice: 100m, createdAt: created),
            // Sell limit at 200, filled at 200.20 → 10 bps improvement.
            MakeOrder("sell-order", "SPY", OrderSide.Sell, 100m, limitPrice: 200m, createdAt: created)
        };
        var fills = new[]
        {
            MakeFill("SPY", OrderSide.Buy, 100m, 99.90m, orderId: "buy-order", timestamp: created.AddSeconds(1)),
            MakeFill("SPY", OrderSide.Sell, 100m, 200.20m, orderId: "sell-order", timestamp: created.AddSeconds(1))
        };

        var report = SessionTcaReporter.Generate("s", "strat", fills, orders);

        report.ExecutionQuality.OrdersWithLimitPrice.Should().Be(2);
        // Both legs improve by 10 bps; the notional-weighted average stays ≈ 10 bps.
        report.ExecutionQuality.AvgLimitPriceImprovementBps.Should().BeApproximately(10.0, 0.05);
    }

    [Fact]
    public void Generate_WithoutOrderHistory_ReturnsEmptyExecutionQuality()
    {
        var fills = new[] { MakeFill("SPY", OrderSide.Buy, 100m, 400m, commission: 1m) };

        var report = SessionTcaReporter.Generate("s", "strat", fills, orders: null);

        report.ExecutionQuality.Should().Be(SessionTcaExecutionQuality.Empty);
    }
}
