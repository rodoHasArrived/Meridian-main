using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

[Trait("Category", "Unit")]
public sealed class LedgerAverageCostWashSaleRoundingTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");

    [Theory]
    [InlineData(0.004, 0.006, 0.02, 0.02)]
    [InlineData(0.004, 0.010, 0.02, 0.04)]
    [InlineData(0.006, 0.004, 0.02, 0.02)]
    [InlineData(0.009, 0.004, 0.04, 0.02)]
    [InlineData(0.006, 0.000, 0.02, 0.00)]
    [InlineData(0.004, 0.000, 0.02, 0.00)]
    [InlineData(0.000, 0.006, 0.00, 0.02)]
    public void PooledRounding_PreservesResultSignAndConservesAmounts(
        decimal unitCost, decimal salePrice, decimal expectedBasis, decimal expectedProceeds)
    {
        var input = CreateInput(
            Enumerable.Range(1, 4)
                .Select(index => new LedgerTaxLot($"lot-{index}", new DateOnly(2026, 1, index), 1m, unitCost, SecurityId))
                .ToArray(), 4m, salePrice);

        var projection = LedgerTaxLotReliefProjector.Project(input);
        var result = expectedProceeds - expectedBasis;

        projection.CostBasis.Should().Be(expectedBasis);
        projection.Proceeds.Should().Be(expectedProceeds);
        projection.RealizedGainOrLoss.Should().Be(result);
        projection.Selections.Sum(selection => selection.QuantityRelieved).Should().Be(4m);
        projection.Selections.Sum(selection => selection.CostBasis).Should().Be(expectedBasis);
        projection.Selections.Sum(selection => selection.Proceeds).Should().Be(expectedProceeds);
        projection.Selections.Sum(selection => selection.RealizedGainOrLoss).Should().Be(result);
        projection.Selections.Should().OnlyContain(selection => selection.CostBasis >= 0m && selection.Proceeds >= 0m);
        projection.Selections.Should().OnlyContain(selection =>
            selection.QuantityRelieved * selection.UnitCost == selection.CostBasis);
        projection.Selections.Should().OnlyContain(selection =>
            result == 0m ? selection.RealizedGainOrLoss == 0m
                : result > 0m ? selection.RealizedGainOrLoss >= 0m : selection.RealizedGainOrLoss <= 0m);
        if (result >= 0m)
        {
            projection.WashSale.Should().BeNull();
        }
        else
        {
            projection.WashSale!.DisallowedLoss.Should().Be(-result);
            projection.WashSale.AllowedLoss.Should().Be(0m);
            projection.WashSale.BasisIncreases.Sum(increase => increase.Amount).Should().Be(-result);
        }
        projection.Lines.Should().OnlyContain(line => line.debit >= 0m && line.credit >= 0m);
        projection.IsBalanced.Should().BeTrue();
        LedgerTaxLotReliefProjector.Project(input).Should().BeEquivalentTo(projection,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void PooledRounding_PreservesExistingBasisResidualAttribution()
    {
        var input = CreateInput(
            Enumerable.Range(1, 3)
                .Select(index => new LedgerTaxLot($"lot-{index}", new DateOnly(2026, 1, index), 1m, 100m / 3m, SecurityId))
                .ToArray(), 2m, 40m);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.Selections.Select(selection => selection.CostBasis).Should().Equal(33.33m, 33.34m);
        projection.Selections.Select(selection => selection.Proceeds).Should().Equal(40m, 40m);
        projection.Selections.Select(selection => selection.RealizedGainOrLoss).Should().Equal(6.67m, 6.66m);
        projection.CostBasis.Should().Be(66.67m);
        projection.Proceeds.Should().Be(80m);
        projection.WashSale.Should().BeNull();
        projection.IsBalanced.Should().BeTrue();
    }

    private static LedgerTaxLotReliefInput CreateInput(IReadOnlyList<LedgerTaxLot> lots, decimal quantity, decimal salePrice)
        => new(
            LedgerAccounts.Securities("AAPL", "broker-1"),
            new DateOnly(2026, 3, 1),
            quantity,
            salePrice,
            LedgerTaxLotReliefMethod.AverageCost,
            lots,
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [new WashSaleReplacementAcquisition("replacement", new DateOnly(2026, 3, 5), quantity, SecurityId)]);
}
