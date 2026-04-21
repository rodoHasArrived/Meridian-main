using FluentAssertions;
using Meridian.Backtesting.Portfolio;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Tests for named lot tracking, FIFO/LIFO/HIFO/SpecificId selection methods,
/// and ClosedLot generation in <see cref="SimulatedPortfolio"/>.
/// </summary>
public sealed class LotLevelTrackingTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SimulatedPortfolio MakePortfolio(
        decimal cash = 100_000m,
        LotSelectionMethod lotMethod = LotSelectionMethod.Fifo)
    {
        var rules = new FinancialAccountRules(LotSelection: lotMethod);
        var account = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "Primary Brokerage",
            FinancialAccountKind.Brokerage,
            "Simulated Broker",
            cash,
            rules);
        return new SimulatedPortfolio(
            [account],
            BacktestDefaults.DefaultBrokerageAccountId,
            new FixedCommissionModel(0m));
    }

    private static FillEvent Buy(string symbol, long qty, decimal price,
        DateTimeOffset? at = null, Guid? lotId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), symbol, qty, price, 0m,
            at ?? DateTimeOffset.UtcNow, null, lotId);

    private static FillEvent Sell(string symbol, long qty, decimal price,
        DateTimeOffset? at = null, Guid? targetLotId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), symbol, -qty, price, 0m,
            at ?? DateTimeOffset.UtcNow, null, targetLotId);

    // ── OpenLot creation ───────────────────────────────────────────────────────

    [Fact]
    public void Buy_CreatesOpenLot_WithCorrectFields()
    {
        var portfolio = MakePortfolio();
        var at = DateTimeOffset.UtcNow;

        portfolio.ProcessFill(Buy("AAPL", 10, 150m, at));

        var lots = portfolio.GetOpenLots("AAPL");
        lots.Should().HaveCount(1);
        var lot = lots[0];
        lot.Symbol.Should().Be("AAPL");
        lot.Quantity.Should().Be(10);
        lot.EntryPrice.Should().Be(150m);
        lot.OpenedAt.Should().Be(at);
    }

    [Fact]
    public void MultipleBuys_CreateMultipleOpenLots()
    {
        var portfolio = MakePortfolio();

        portfolio.ProcessFill(Buy("AAPL", 10, 100m));
        portfolio.ProcessFill(Buy("AAPL", 5, 120m));

        portfolio.GetOpenLots("AAPL").Should().HaveCount(2);
    }

    // ── FIFO selection ─────────────────────────────────────────────────────────

    [Fact]
    public void Fifo_FullClose_RemovesOldestLot_RealisesCorrectPnl()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.Fifo);
        portfolio.ProcessFill(Buy("AAPL", 10, 100m));
        portfolio.ProcessFill(Buy("AAPL", 5, 120m));

        // Sell all 10 from the first lot
        portfolio.ProcessFill(Sell("AAPL", 10, 110m));

        portfolio.GetOpenLots("AAPL").Should().HaveCount(1);
        portfolio.GetOpenLots("AAPL")[0].EntryPrice.Should().Be(120m); // second lot remains

        portfolio.GetClosedLots("AAPL").Should().HaveCount(1);
        portfolio.GetClosedLots("AAPL")[0].RealizedPnl.Should().Be(100m); // (110-100)*10
    }

    [Fact]
    public void Fifo_PartialClose_SplitsLot()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.Fifo);
        portfolio.ProcessFill(Buy("AAPL", 10, 100m));

        portfolio.ProcessFill(Sell("AAPL", 3, 110m));

        portfolio.GetOpenLots("AAPL").Should().HaveCount(1);
        portfolio.GetOpenLots("AAPL")[0].Quantity.Should().Be(7);
        portfolio.GetClosedLots("AAPL")[0].RealizedPnl.Should().Be(30m); // (110-100)*3
    }

    // ── LIFO selection ─────────────────────────────────────────────────────────

    [Fact]
    public void Lifo_SelectsNewestLotFirst()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.Lifo);
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var t1 = DateTimeOffset.UtcNow.AddDays(-1);

        portfolio.ProcessFill(Buy("MSFT", 10, 100m, t0));  // older lot
        portfolio.ProcessFill(Buy("MSFT", 5, 200m, t1));   // newer lot (higher price)

        portfolio.ProcessFill(Sell("MSFT", 5, 250m));

        // Newest lot (200m) should be closed first
        var closed = portfolio.GetClosedLots("MSFT");
        closed.Should().HaveCount(1);
        closed[0].EntryPrice.Should().Be(200m);
        closed[0].RealizedPnl.Should().Be(250m); // (250-200)*5

        // Older lot should remain open
        portfolio.GetOpenLots("MSFT").Should().HaveCount(1);
        portfolio.GetOpenLots("MSFT")[0].EntryPrice.Should().Be(100m);
    }

    // ── HIFO selection ─────────────────────────────────────────────────────────

    [Fact]
    public void Hifo_SelectsHighestCostLotFirst()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.Hifo);

        portfolio.ProcessFill(Buy("TSLA", 10, 100m));
        portfolio.ProcessFill(Buy("TSLA", 10, 300m)); // highest
        portfolio.ProcessFill(Buy("TSLA", 10, 200m));

        portfolio.ProcessFill(Sell("TSLA", 10, 350m));

        var closed = portfolio.GetClosedLots("TSLA");
        closed.Should().HaveCount(1);
        closed[0].EntryPrice.Should().Be(300m);
        closed[0].RealizedPnl.Should().Be(500m); // (350-300)*10
    }

    // ── SpecificId selection ──────────────────────────────────────────────────

    [Fact]
    public void SpecificId_ClosesNominatedLotFirst()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.SpecificId);

        portfolio.ProcessFill(Buy("NVDA", 10, 100m));
        portfolio.ProcessFill(Buy("NVDA", 10, 200m));
        portfolio.ProcessFill(Buy("NVDA", 10, 300m));

        // Nominate the middle lot (200m entry)
        var targetLotId = portfolio.GetOpenLots("NVDA")
            .Single(l => l.EntryPrice == 200m).LotId;

        portfolio.ProcessFill(Sell("NVDA", 10, 250m, targetLotId: targetLotId));

        var closed = portfolio.GetClosedLots("NVDA");
        closed.Should().HaveCount(1);
        closed[0].LotId.Should().Be(targetLotId);
        closed[0].EntryPrice.Should().Be(200m);
        closed[0].RealizedPnl.Should().Be(500m); // (250-200)*10
    }

    [Fact]
    public void SpecificId_FallsBackToFifo_WhenLotNotFound()
    {
        var portfolio = MakePortfolio(lotMethod: LotSelectionMethod.SpecificId);

        portfolio.ProcessFill(Buy("NVDA", 5, 100m));
        portfolio.ProcessFill(Buy("NVDA", 5, 200m));

        // Non-existent lot id → FIFO fallback (100m lot closed first)
        portfolio.ProcessFill(Sell("NVDA", 5, 150m, targetLotId: Guid.NewGuid()));

        portfolio.GetClosedLots("NVDA")[0].EntryPrice.Should().Be(100m);
    }

    // ── ClosedLot record ──────────────────────────────────────────────────────

    [Fact]
    public void ClosedLot_HoldingPeriod_IsLongTerm()
    {
        var portfolio = MakePortfolio();
        var openAt = DateTimeOffset.UtcNow.AddDays(-400);
        portfolio.ProcessFill(Buy("X", 10, 50m, openAt));
        portfolio.ProcessFill(Sell("X", 10, 60m));

        portfolio.GetClosedLots("X")[0].IsLongTerm.Should().BeTrue();
    }

    [Fact]
    public void ClosedLot_ShortTerm_WhenHeldLessThan365Days()
    {
        var portfolio = MakePortfolio();
        var openAt = DateTimeOffset.UtcNow.AddDays(-100);
        portfolio.ProcessFill(Buy("X", 10, 50m, openAt));
        portfolio.ProcessFill(Sell("X", 10, 60m));

        portfolio.GetClosedLots("X")[0].IsLongTerm.Should().BeFalse();
    }

    // ── OpenLot helpers ───────────────────────────────────────────────────────

    [Fact]
    public void OpenLot_UnrealizedPnl_CalculatesCorrectly()
    {
        var lot = new OpenLot(Guid.NewGuid(), "AAPL", 10, 100m, DateTimeOffset.UtcNow, Guid.NewGuid());
        lot.UnrealizedPnl(150m).Should().Be(500m);
        lot.UnrealizedPnl(80m).Should().Be(-200m);
    }

    [Fact]
    public void OpenLot_IsLongTerm_WhenOlderThan365Days()
    {
        var lot = new OpenLot(Guid.NewGuid(), "X", 1, 100m,
            DateTimeOffset.UtcNow.AddDays(-400), Guid.NewGuid());
        lot.IsLongTerm(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    // ── Position includes open lots ───────────────────────────────────────────

    [Fact]
    public void Position_IncludesOpenLots()
    {
        var portfolio = MakePortfolio();
        portfolio.ProcessFill(Buy("AAPL", 10, 100m));
        portfolio.ProcessFill(Buy("AAPL", 5, 120m));
        portfolio.UpdateLastPrice("AAPL", 110m);

        var positions = portfolio.GetCurrentPositions();
        positions["AAPL"].OpenLots.Should().HaveCount(2);
    }

    // ── FinancialAccountSnapshot includes lots ────────────────────────────────

    [Fact]
    public void AccountSnapshot_IncludesOpenAndClosedLots()
    {
        var portfolio = MakePortfolio();
        portfolio.ProcessFill(Buy("AAPL", 10, 100m));
        portfolio.ProcessFill(Sell("AAPL", 5, 120m));

        var snapshots = portfolio.GetAccountSnapshots();
        var snapshot = snapshots[BacktestDefaults.DefaultBrokerageAccountId];

        snapshot.OpenLots.Should().HaveCount(1);
        snapshot.ClosedLots.Should().HaveCount(1);
    }

    // ── Multiple symbols, isolated lots ──────────────────────────────────────

    [Fact]
    public void DifferentSymbols_HaveIsolatedLotLists()
    {
        var portfolio = MakePortfolio();
        portfolio.ProcessFill(Buy("AAPL", 10, 100m));
        portfolio.ProcessFill(Buy("MSFT", 20, 200m));

        portfolio.GetOpenLots("AAPL").Should().HaveCount(1);
        portfolio.GetOpenLots("MSFT").Should().HaveCount(1);
        portfolio.GetOpenLots().Should().HaveCount(2);
    }

    // ── LotSelectionMethod default ────────────────────────────────────────────

    [Fact]
    public void DefaultLotSelectionMethod_IsFifo()
    {
        new FinancialAccountRules().LotSelection.Should().Be(LotSelectionMethod.Fifo);
    }
}
