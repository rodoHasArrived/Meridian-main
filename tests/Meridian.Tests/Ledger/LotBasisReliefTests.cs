using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LotBasisReliefTests
{
    private sealed record Lot(decimal Quantity, decimal Basis);

    [Fact]
    public void PartialThenClosingRelief_ConservesExactBasisThroughRepeatingDivision()
    {
        var original = new Lot(7m, 1m);
        var partial = Relieve(original, 2.5m);
        var remaining = new Lot(4.5m, original.Basis - partial.CostBasis);
        var closing = Relieve(remaining, remaining.Quantity);

        partial.Quantity.Should().Be(2.5m);
        partial.ClosesLot.Should().BeFalse();
        closing.CostBasis.Should().Be(remaining.Basis);
        closing.ClosesLot.Should().BeTrue();
        (partial.CostBasis + closing.CostBasis).Should().Be(1m);
    }

    [Theory]
    [InlineData(0.01, 0.04)]
    [InlineData(101.000000000001, 137.000000000003)]
    public void PooledCurrencies_ConserveEachRetainedTotalIndependently(decimal transaction, decimal functional)
    {
        var lots = new[] { new Lot(1m, 1m), new Lot(1m, 2m), new Lot(1m, 3m) };
        var consumption = LotConsumption.Consume(lots, 3m, static lot => lot.Quantity);
        var tx = LotBasisRelief.Allocate(consumption.Slices, static lot => lot.Quantity,
            static lot => lot.Basis, new(3m, transaction));
        var fx = LotBasisRelief.Allocate(consumption.Slices, static lot => lot.Quantity,
            static lot => lot.Basis, new(3m, functional));

        tx.Sum(static slice => slice.CostBasis).Should().Be(transaction);
        fx.Sum(static slice => slice.CostBasis).Should().Be(functional);
        tx.Select(static slice => slice.Lot).Should().Equal(lots);
        fx.Select(static slice => slice.Quantity).Should().Equal(1m, 1m, 1m);
    }

    [Fact]
    public void PartialPool_UsesEveryOpenLotInThePoolIncludingUnselectedLots()
    {
        var lots = new[] { new Lot(2m, 10m), new Lot(3m, 30m), new Lot(5m, 110m) };
        var consumption = LotConsumption.Consume(lots, 2.5m, static lot => lot.Quantity);
        var result = LotBasisRelief.Allocate(consumption.Slices, static lot => lot.Quantity,
            static lot => lot.Basis, new(10m, 150m));

        result.Select(static slice => slice.CostBasis).Should().Equal(30m, 7.5m);
        result.Sum(static slice => slice.Quantity).Should().Be(2.5m);
        result.Sum(static slice => slice.CostBasis).Should().Be(37.5m);
    }

    [Fact]
    public void ValidPool_PreservesRetainedDecimalParcelValues()
    {
        var lots = new[] { new Lot(1m, 10m), new Lot(1m, 0m), new Lot(1m, 0m) };
        var consumption = LotConsumption.Consume(lots, 3m, static lot => lot.Quantity);
        var result = LotBasisRelief.Allocate(consumption.Slices, static lot => lot.Quantity,
            static lot => lot.Basis, new(3m, 10m));

        // These are the frozen pre-extraction parcel values. Computing the last parcel as
        // target minus the preceding parcels changes its exact value despite the same total.
        result.Select(static slice => slice.CostBasis).Should().Equal(
            3.3333333333333333333333333333m,
            3.3333333333333333333333333333m,
            3.333333333333333333333333333m);
        result.Sum(static slice => slice.CostBasis).Should().Be(10m);
    }

    [Fact]
    public void ClosingRelief_PreservesExactBasisInsteadOfExtendingRoundedUnitQuote()
    {
        var lot = new Lot(3m, 1m);
        (lot.Basis / lot.Quantity * lot.Quantity).Should().NotBe(lot.Basis);
        Relieve(lot, 3m).CostBasis.Should().Be(1m);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void InconsistentConsumptionEvidence_IsRejected(decimal quantity, bool closes)
    {
        var lot = new Lot(3m, 1m);
        var allocate = () => LotBasisRelief.Allocate([new LotSlice<Lot>(lot, quantity, closes)],
            static item => item.Quantity, static item => item.Basis);

        allocate.Should().Throw<ArgumentException>();
    }

    private static LotBasisSlice<Lot> Relieve(Lot lot, decimal quantity)
    {
        var consumption = LotConsumption.Consume([lot], quantity, static item => item.Quantity);
        return LotBasisRelief.Allocate(consumption.Slices,
            static item => item.Quantity, static item => item.Basis).Should().ContainSingle().Subject;
    }
}
