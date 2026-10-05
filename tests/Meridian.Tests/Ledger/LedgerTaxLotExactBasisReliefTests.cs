using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

[Trait("Category", "Unit")]
public sealed class LedgerTaxLotExactBasisReliefTests
{
    private static LedgerAccount Account => LedgerAccounts.Securities("AAPL", "broker-1");

    [Fact]
    public void Project_DiscreteFullLotRelievesKnownExactBasisWithoutRounding()
    {
        // A governed current basis of 0.0025 on two units differs from acquisition unit cost 0.001.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 6, 1),
            quantitySold: 2m,
            salePrice: 1m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 5, 1), 2m, 0.001m, costBasis: 0.0025m)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.CostBasis.Should().Be(0.0025m);
        projection.Selections.Should().ContainSingle()
            .Which.UnitCost.Should().Be(0.00125m);
    }

    [Fact]
    public void Project_DiscretePartialLotKeepsUnitCostRelief()
    {
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 6, 1),
            quantitySold: 1m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 5, 1), 2m, 100m, costBasis: 250m)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        // An exact whole-lot basis says nothing about how to split it, so a partial slice keeps
        // the recorded unit cost.
        projection.CostBasis.Should().Be(100m);
    }

    [Theory]
    [InlineData(LedgerTaxLotReliefMethod.Fifo)]
    [InlineData(LedgerTaxLotReliefMethod.AverageCost)]
    public void HistoryProjector_VersionedFractionalCentBasisReconstructsExactly(LedgerTaxLotReliefMethod method)
    {
        var history = new LedgerTaxLotDisposalHistory(
            Guid.NewGuid(), Guid.NewGuid(), Account, new DateOnly(2026, 6, 1), method,
            [new LedgerTaxLotDisposalHistoryLot("lot-a", new(2026, 5, 1), new(2026, 5, 1), 2m, 0.00125m, 0.0025m)],
            RecognizedGainOrLoss: 0m, WashSaleBasisIncreases: [], MatchedReplacementQuantity: 0m,
            LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion);

        var rebuilt = LedgerTaxLotReliefHistoryProjector.Project(history);

        rebuilt.Should().NotBeNull();
        rebuilt!.CostBasis.Should().Be(0.0025m);
        rebuilt.Proceeds.Should().Be(0.0025m);
        rebuilt.RealizedGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void HistoryProjector_LegacyRowsKeepUnitCostRecalculation()
    {
        // Unversioned history predates exact-basis certification and keeps re-deriving each lot's
        // basis from its retained unit cost.
        var history = new LedgerTaxLotDisposalHistory(
            Guid.NewGuid(), Guid.NewGuid(), Account, new DateOnly(2026, 6, 1), LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLotDisposalHistoryLot("lot-a", new(2026, 5, 1), new(2026, 5, 1), 2m, 100m, 250m)],
            RecognizedGainOrLoss: 50m, WashSaleBasisIncreases: [], MatchedReplacementQuantity: 0m);

        var rebuilt = LedgerTaxLotReliefHistoryProjector.Project(history);

        rebuilt.Should().NotBeNull();
        rebuilt!.CostBasis.Should().Be(200m);
    }
}
