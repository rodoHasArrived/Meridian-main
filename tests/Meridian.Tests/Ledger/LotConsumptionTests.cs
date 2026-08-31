using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LotConsumptionTests
{
    private sealed record Lot(string Id, decimal Quantity);

    [Fact]
    public void Consume_TakesLotsInOrderAndSplitsTheFinalLot()
    {
        var lots = new[] { new Lot("a", 10m), new Lot("b", 10m), new Lot("c", 10m) };

        var result = LotConsumption.Consume(lots, 15m, static lot => lot.Quantity);

        result.FullyConsumed.Should().BeTrue();
        result.Shortfall.Should().Be(0m);
        result.Slices.Should().HaveCount(2);
        result.Slices[0].Should().Be(new LotSlice<Lot>(lots[0], 10m, ClosesLot: true));
        result.Slices[1].Should().Be(new LotSlice<Lot>(lots[1], 5m, ClosesLot: false));
    }

    [Fact]
    public void Consume_ExactLotBoundaryClosesTheLot()
    {
        var lots = new[] { new Lot("a", 10m) };

        var result = LotConsumption.Consume(lots, 10m, static lot => lot.Quantity);

        result.Slices.Should().ContainSingle()
            .Which.ClosesLot.Should().BeTrue();
    }

    [Fact]
    public void Consume_ReportsShortfallWhenLotsRunOut()
    {
        var lots = new[] { new Lot("a", 4m), new Lot("b", 3m) };

        var result = LotConsumption.Consume(lots, 10m, static lot => lot.Quantity);

        result.FullyConsumed.Should().BeFalse();
        result.Shortfall.Should().Be(3m);
        result.Slices.Should().HaveCount(2);
        result.Slices.Sum(static slice => slice.Quantity).Should().Be(7m);
    }

    [Fact]
    public void Consume_SkipsLotsWithNonPositiveQuantity()
    {
        var lots = new[] { new Lot("empty", 0m), new Lot("negative", -5m), new Lot("open", 8m) };

        var result = LotConsumption.Consume(lots, 6m, static lot => lot.Quantity);

        result.Slices.Should().ContainSingle();
        result.Slices[0].Lot.Id.Should().Be("open");
        result.Slices[0].Quantity.Should().Be(6m);
    }

    [Fact]
    public void Consume_ZeroQuantityProducesNoSlices()
    {
        var lots = new[] { new Lot("a", 10m) };

        var result = LotConsumption.Consume(lots, 0m, static lot => lot.Quantity);

        result.Slices.Should().BeEmpty();
        result.FullyConsumed.Should().BeTrue();
    }

    [Fact]
    public void Consume_StopsEnumeratingOnceFilled()
    {
        var visited = new List<string>();

        IEnumerable<Lot> TrackedLots()
        {
            foreach (var lot in new[] { new Lot("a", 10m), new Lot("b", 10m), new Lot("c", 10m) })
            {
                visited.Add(lot.Id);
                yield return lot;
            }
        }

        LotConsumption.Consume(TrackedLots(), 5m, static lot => lot.Quantity);

        visited.Should().Equal("a");
    }

    [Fact]
    public void Consume_SupportsFractionalQuantities()
    {
        var lots = new[] { new Lot("a", 0.75m) };

        var result = LotConsumption.Consume(lots, 0.5m, static lot => lot.Quantity);

        result.Slices[0].Quantity.Should().Be(0.5m);
        result.Slices[0].ClosesLot.Should().BeFalse();
    }
}
