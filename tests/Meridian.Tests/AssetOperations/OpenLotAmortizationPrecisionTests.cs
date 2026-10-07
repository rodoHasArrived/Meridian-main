using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotAmortizationPrecisionTests
{
    [Fact]
    public void FractionalPartialBasis_RefusesAJournalDeltaThatStorageWouldSilentlyRound()
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        inputs = inputs with
        {
            ExpectedLot = inputs.ExpectedLot with
            {
                OpenQuantity = 100m / 3m,
                OpenTransactionCostBasis = 110m / 3m,
                OpenFunctionalCostBasis = 121m / 3m
            }
        };

        var project = () => OpenLotAmortization.Project(inputs);

        project.Should().Throw<ArgumentException>().WithMessage("*exactly representable*12-decimal journal boundary*governed residual treatment*");
    }

    [Fact]
    public void SeparatelyRoundedTargets_PreserveAcquisitionFxWithinOneStorageQuantum()
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null)
            with
        { AsOfDate = new DateOnly(2025, 5, 1) };

        var projection = OpenLotAmortization.Project(inputs);

        projection.TransactionMovement.Should().Be(-1.666666666667m);
        projection.FunctionalMovement.Should().Be(-1.833333333334m);
        var difference = Math.Abs(projection.FunctionalMovement
            - projection.TransactionMovement * inputs.ExpectedLot.Acquisition.AcquisitionFxRateToFunctional);
        difference.Should().BeGreaterThan(0m).And.BeLessThanOrEqualTo(0.000000000001m);
    }
}
