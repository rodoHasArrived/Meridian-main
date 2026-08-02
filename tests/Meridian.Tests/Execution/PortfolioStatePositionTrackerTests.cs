using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

/// <summary>
/// Tests for <see cref="PortfolioStatePositionTracker"/>, the production
/// <see cref="IPositionTracker"/> that projects a live <see cref="IPortfolioState"/> into the
/// position/P&amp;L view consumed by risk rules. The rule-integration tests deliberately wire the
/// real <see cref="PositionLimitRule"/> and <see cref="DrawdownCircuitBreaker"/> against a real
/// <see cref="PaperTradingPortfolio"/> — with no test doubles — to prove those safety-critical
/// rules can now function in a real deployment.
/// </summary>
public sealed class PortfolioStatePositionTrackerTests
{
    // ─── Construction / argument validation ──────────────────────────────────

    [Fact]
    public void Constructor_WithNullPortfolio_ThrowsArgumentNullException()
    {
        var act = () => new PortfolioStatePositionTracker(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("portfolio");
    }

    [Fact]
    public void GetPosition_WithNullOrWhitespaceSymbol_Throws()
    {
        var tracker = new PortfolioStatePositionTracker(new PaperTradingPortfolio(100_000m));

        var actNull = () => tracker.GetPosition(null!);
        var actBlank = () => tracker.GetPosition("   ");

        actNull.Should().Throw<ArgumentException>();
        actBlank.Should().Throw<ArgumentException>();
    }

    // ─── Projection of held positions ────────────────────────────────────────

    [Fact]
    public void GetPosition_HeldSymbol_ProjectsQuantityAndCostBasis()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 40m, price: 150m));
        var tracker = new PortfolioStatePositionTracker(portfolio);

        var position = tracker.GetPosition("AAPL");

        position.Symbol.Should().Be("AAPL");
        position.Quantity.Should().Be(40m);
        position.AverageCostBasis.Should().Be(150m);
    }

    [Fact]
    public void GetPosition_HeldSymbol_ReconstructsMarkConsistentWithUnrealizedPnl()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 100m, price: 150m));
        // Mark the position up so there is a non-zero unrealised P&L to reconstruct.
        portfolio.UpdateMarketPrice("AAPL", 170m);
        var tracker = new PortfolioStatePositionTracker(portfolio);

        var position = tracker.GetPosition("AAPL");

        // The derived market price must reproduce the source portfolio's unrealised P&L exactly,
        // i.e. UnrealizedPnl = (MarketPrice - AverageCostBasis) * Quantity.
        position.MarketPrice.Should().Be(170m);
        position.UnrealizedPnl.Should().Be(portfolio.Positions["AAPL"].UnrealizedPnl);
        position.UnrealizedPnl.Should().Be(2_000m); // (170 - 150) * 100
    }

    [Fact]
    public void GetPosition_UnknownSymbol_ReturnsFlatPosition()
    {
        var tracker = new PortfolioStatePositionTracker(new PaperTradingPortfolio(100_000m));

        var position = tracker.GetPosition("NVDA");

        position.Symbol.Should().Be("NVDA");
        position.Quantity.Should().Be(0m);
        position.MarketValue.Should().Be(0m);
    }

    [Fact]
    public void GetAllPositions_ReturnsEveryHeldSymbol()
    {
        var portfolio = new PaperTradingPortfolio(500_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10m, price: 150m));
        portfolio.ApplyFill(BuildFill("MSFT", OrderSide.Buy, qty: 5m, price: 300m));
        var tracker = new PortfolioStatePositionTracker(portfolio);

        var positions = tracker.GetAllPositions();

        positions.Should().ContainKeys("AAPL", "MSFT");
        positions["AAPL"].Quantity.Should().Be(10m);
        positions["MSFT"].Quantity.Should().Be(5m);
    }

    // ─── Portfolio-level projections ─────────────────────────────────────────

    [Fact]
    public void PortfolioAggregates_DelegateToSourcePortfolio()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 100m, price: 150m));
        portfolio.UpdateMarketPrice("AAPL", 140m); // 10-per-share unrealised loss on 100 shares
        var tracker = new PortfolioStatePositionTracker(portfolio);

        tracker.GetCash().Should().Be(portfolio.Cash);
        tracker.GetPortfolioValue().Should().Be(portfolio.PortfolioValue);
        tracker.GetUnrealizedPnl().Should().Be(portfolio.UnrealisedPnl);
        tracker.GetRealizedPnl().Should().Be(portfolio.RealisedPnl);
        tracker.GetUnrealizedPnl().Should().Be(-1_000m);
    }

    // ─── Rule integration — no test doubles ──────────────────────────────────

    [Fact]
    public async Task PositionLimitRule_WithRealTracker_ApprovesWithinCapAndRejectsBeyond()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 80m, price: 150m));
        var tracker = new PortfolioStatePositionTracker(portfolio);
        var rule = new PositionLimitRule(tracker, maxPositionSize: 100m, NullLogger<PositionLimitRule>.Instance);

        // Current 80 long, buy 15 → projected 95 ≤ 100 → approve.
        var approved = await rule.EvaluateAsync(BuildOrder("AAPL", OrderSide.Buy, 15m));
        // Current 80 long, buy 40 → projected 120 > 100 → reject.
        var rejected = await rule.EvaluateAsync(BuildOrder("AAPL", OrderSide.Buy, 40m));

        // A satisfied rule reports no finding; a breach reports one.
        approved.Should().BeNull();
        rejected.Should().NotBeNull();
        rejected!.Message.Should().NotBeNullOrWhiteSpace();
        // Evidence must be the projected position the rule compared, not the current one.
        rejected.ObservedValue.Should().Be(120m);
        rejected.LimitValue.Should().Be(100m);
    }

    [Fact]
    public async Task DrawdownCircuitBreaker_WithRealTracker_TripsWhenPortfolioValueFalls()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 100m, price: 150m));
        // Mark down to 50 → portfolio value = 85_000 cash + 5_000 mark = 90_000 → 10% drawdown.
        portfolio.UpdateMarketPrice("AAPL", 50m);
        var tracker = new PortfolioStatePositionTracker(portfolio);

        var tightBreaker = new DrawdownCircuitBreaker(
            tracker, initialCapital: 100_000m, maxDrawdownPercent: 5m, NullLogger<DrawdownCircuitBreaker>.Instance);
        var looseBreaker = new DrawdownCircuitBreaker(
            tracker, initialCapital: 100_000m, maxDrawdownPercent: 20m, NullLogger<DrawdownCircuitBreaker>.Instance);

        var tripped = await tightBreaker.EvaluateAsync(BuildOrder("MSFT", OrderSide.Buy, 1m));
        var allowed = await looseBreaker.EvaluateAsync(BuildOrder("MSFT", OrderSide.Buy, 1m));

        tripped.Should().NotBeNull("a 10% drawdown breaches the 5% kill-switch");
        allowed.Should().BeNull("a 10% drawdown is within the 20% threshold");
        // Observed and limit are both percentages, so the recorded evidence is comparable.
        tripped!.ObservedValue.Should().Be(10m);
        tripped.LimitValue.Should().Be(5m);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static OrderRequest BuildOrder(string symbol, OrderSide side, decimal quantity) => new()
    {
        Symbol = symbol,
        Side = side,
        Type = OrderType.Market,
        Quantity = quantity,
    };

    private static ExecutionReport BuildFill(string symbol, OrderSide side, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        Symbol = symbol,
        Side = side,
        ReportType = ExecutionReportType.Fill,
        FilledQuantity = qty,
        FillPrice = price,
        Commission = 0m,
        Timestamp = DateTimeOffset.UtcNow,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
    };
}
