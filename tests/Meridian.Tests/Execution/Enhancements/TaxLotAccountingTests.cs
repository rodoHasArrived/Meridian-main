using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.TaxLotAccounting;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Advanced Tax Lot Accounting types (Phase 2).
/// Validates <see cref="TaxLot"/>, <see cref="TaxLotSelectors"/>,
/// and the various relief methods (FIFO, LIFO, HIFO, SpecificId).
/// </summary>
public sealed class TaxLotAccountingTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 10, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2024, 1, 11, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2024, 1, 12, 9, 30, 0, TimeSpan.Zero);

    private static TaxLot MakeLot(long qty, decimal cost, DateTimeOffset? openedAt = null) =>
        new(Guid.NewGuid(), "AAPL", qty, cost, openedAt ?? T0);

    // -------------------------------------------------------------------------
    // TaxLot record
    // -------------------------------------------------------------------------

    [Fact]
    public void TaxLot_TotalCost_IsAbsoluteQuantityTimesCostBasis()
    {
        var lot = MakeLot(100, 150m);
        lot.TotalCost.Should().Be(100 * 150m);
    }

    [Fact]
    public void TaxLot_IsShort_TrueWhenNegativeQuantity()
    {
        MakeLot(-50, 200m).IsShort.Should().BeTrue();
        MakeLot(50, 200m).IsShort.Should().BeFalse();
    }

    [Fact]
    public void TaxLot_WithReducedQuantity_ReturnsLotWithLessShares()
    {
        var lot = MakeLot(100, 150m);
        var reduced = lot.WithReducedQuantity(40);
        reduced.Quantity.Should().Be(60);
        reduced.LotId.Should().Be(lot.LotId);
    }

    [Fact]
    public void TaxLot_WithReducedQuantity_ThrowsWhenExceedsLot()
    {
        var lot = MakeLot(100, 150m);
        var act = () => lot.WithReducedQuantity(101);
        act.Should().Throw<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // FIFO selector
    // -------------------------------------------------------------------------

    [Fact]
    public void Fifo_RelievesOldestLotsFirst()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),   // oldest
            MakeLot(100, 120m, T1),   // middle
            MakeLot(100, 140m, T2),   // newest
        };

        var result = TaxLotSelectors.Fifo().Relieve(lots, 100, 150m);

        result.RelievedLots.Should().ContainSingle();
        result.RelievedLots[0].Lot.OpenedAt.Should().Be(T0);
        result.RelievedLots[0].Lot.CostBasis.Should().Be(100m);
        result.RemainingLots.Should().HaveCount(2);
    }

    [Fact]
    public void Fifo_PartialRelief_LeavesReducedLot()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),
            MakeLot(100, 120m, T1),
        };

        var result = TaxLotSelectors.Fifo().Relieve(lots, 60, 150m);

        result.RelievedLots.Should().ContainSingle();
        result.RelievedLots[0].RelievedQuantity.Should().Be(60);
        result.RemainingLots.Should().HaveCount(2); // partial + second
        result.RemainingLots.First().Quantity.Should().Be(40); // 100 - 60
    }

    [Fact]
    public void Fifo_ThrowsWhenQuantityExceedsOpen()
    {
        var lots = new[] { MakeLot(50, 100m) };
        var act = () => TaxLotSelectors.Fifo().Relieve(lots, 100, 150m);
        act.Should().Throw<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // LIFO selector
    // -------------------------------------------------------------------------

    [Fact]
    public void Lifo_RelievesNewestLotsFirst()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),
            MakeLot(100, 140m, T2),   // newest — relieved first by LIFO
        };

        var result = TaxLotSelectors.Lifo().Relieve(lots, 100, 150m);

        result.RelievedLots.Should().ContainSingle();
        result.RelievedLots[0].Lot.OpenedAt.Should().Be(T2);
    }

    // -------------------------------------------------------------------------
    // HIFO selector
    // -------------------------------------------------------------------------

    [Fact]
    public void Hifo_RelievesHighestCostLotsFirst()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),
            MakeLot(100, 180m, T1),  // highest cost — relieved first
            MakeLot(100, 140m, T2),
        };

        var result = TaxLotSelectors.Hifo().Relieve(lots, 100, 150m);

        result.RelievedLots[0].Lot.CostBasis.Should().Be(180m);
        // Gain = (150 - 180) * 100 = -3000 (a loss)
        result.RelievedLots[0].RealizedPnl.Should().Be(-3_000m);
    }

    // -------------------------------------------------------------------------
    // SpecificId selector
    // -------------------------------------------------------------------------

    [Fact]
    public void SpecificId_RelievesDesignatedLot()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),
            MakeLot(100, 180m, T1),
        };
        var targetId = lots[1].LotId;

        var result = TaxLotSelectors.SpecificId([targetId]).Relieve(lots, 100, 200m);

        result.RelievedLots.Should().ContainSingle();
        result.RelievedLots[0].Lot.LotId.Should().Be(targetId);
        result.RelievedLots[0].RealizedPnl.Should().Be((200m - 180m) * 100);
    }

    [Fact]
    public void SpecificId_ThrowsWhenLotIdNotFound()
    {
        var lots = new[] { MakeLot(100, 100m) };
        var act = () => TaxLotSelectors.SpecificId([Guid.NewGuid()]).Relieve(lots, 100, 150m);
        act.Should().Throw<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // TaxLotReliefResult
    // -------------------------------------------------------------------------

    [Fact]
    public void TaxLotReliefResult_TotalRealizedPnl_SumsAcrossLots()
    {
        var lots = new[]
        {
            MakeLot(100, 100m, T0),
            MakeLot(100, 120m, T1),
        };

        var result = TaxLotSelectors.Fifo().Relieve(lots, 200, 150m);

        // Lot 1: (150 - 100) * 100 = 5000
        // Lot 2: (150 - 120) * 100 = 3000
        result.TotalRealizedPnl.Should().Be(8_000m);
        result.TotalRelievedQuantity.Should().Be(200);
    }
}
