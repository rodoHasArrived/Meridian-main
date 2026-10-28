using Meridian.Contracts.SecurityMaster;
using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Tests.Storage;

namespace Meridian.Tests.Ledger;

public sealed class OpenLotDirectionTests
{
    [Fact]
    public void UnknownDirection_IsRejectedInsteadOfBeingTreatedAsLong()
    {
        var lot = OpenLotConvergenceTests.Lot(1).ToOpenLot();
        lot = lot with { Acquisition = lot.Acquisition with { Direction = (OpenLotDirection)2 } };

        var validate = () => OpenLotValidation.Validate(lot);

        validate.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(OpenLotDirection.Long)]
    [InlineData(OpenLotDirection.Short)]
    public void Direction_DoesNotPermitNegativeOpenQuantity(OpenLotDirection direction)
    {
        var lot = OpenLotConvergenceTests.Lot(1).ToOpenLot();
        lot = lot with { OpenQuantity = -1m, Acquisition = lot.Acquisition with { Direction = direction } };

        var validate = () => OpenLotValidation.Validate(lot);

        validate.Should().Throw<ArgumentException>().WithMessage("*quantity*");
    }

    [Fact]
    public void HistoricalLongAcquisition_KeepsItsWireShapeAndDirectionAfterReload()
    {
        var retained = OpenLotConvergenceTests.Lot(1);
        var json = JsonSerializer.Serialize(retained);
        json.Should().NotContain("Direction");
        var restored = JsonSerializer.Deserialize<LedgerTaxLotRecord>(json)!;

        restored.ToOpenLot().Acquisition.Direction.Should().Be(OpenLotDirection.Long);
        JsonSerializer.Serialize(restored).Should().Be(json);
        JsonSerializer.Serialize(new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Acquire))
            .Should().NotContain("Direction");
    }

    [Fact]
    public void RetainedShortDirection_DoesNotChangePositiveQuantityOrEitherCurrencyBasis()
    {
        var retained = OpenLotConvergenceTests.Lot(1, 2.5m, 100m, 1.2m);
        retained = retained with
        {
            Account = LedgerAccounts.ShortSecuritiesPayable("OLD"),
            Acquisition = retained.Acquisition! with { Direction = OpenLotDirection.Short }
        };
        var json = JsonSerializer.Serialize(retained);
        json.Should().Contain("\"Direction\":\"Short\"");
        var restored = JsonSerializer.Deserialize<LedgerTaxLotRecord>(json)!.ToOpenLot();

        restored.OriginalQuantity.Should().Be(2.5m);
        restored.OpenQuantity.Should().Be(2.5m);
        restored.OpenTransactionCostBasis.Should().Be(250m);
        restored.OpenFunctionalCostBasis.Should().Be(300m);
        restored.Acquisition.Direction.Should().Be(OpenLotDirection.Short);
    }

    [Theory]
    [InlineData(OpenLotReliefMethod.Fifo)]
    [InlineData(OpenLotReliefMethod.Lifo)]
    [InlineData(OpenLotReliefMethod.Hifo)]
    [InlineData(OpenLotReliefMethod.AverageCost)]
    public void Relief_CannotNetLongAndShortLotsEvenWhenAllOtherScopeFactsAgree(OpenLotReliefMethod method)
    {
        var first = OpenLotConvergenceTests.Lot(1).ToOpenLot();
        var second = OpenLotConvergenceTests.Lot(2).ToOpenLot();
        second = second with { Acquisition = second.Acquisition with { Direction = OpenLotDirection.Short } };

        var select = () => new OpenLotReliefService().Select([first, second], 1m, method);
        select.Should().Throw<ArgumentException>().WithMessage("*direction*");
    }

    [Theory]
    [InlineData(OpenLotReliefMethod.Fifo)]
    [InlineData(OpenLotReliefMethod.Lifo)]
    [InlineData(OpenLotReliefMethod.Hifo)]
    [InlineData(OpenLotReliefMethod.AverageCost)]
    [InlineData(OpenLotReliefMethod.SpecificId)]
    public void ShortReliefCalculation_PreservesPositiveQuantityAndBothRetainedBases(OpenLotReliefMethod method)
    {
        var lots = new[] { OpenLotConvergenceTests.Lot(1, 2.5m, 100m, 1.2m), OpenLotConvergenceTests.Lot(2, 2.5m, 100m, 1.2m) }
            .Select(retained => retained.ToOpenLot())
            .Select(lot => lot with { Acquisition = lot.Acquisition with { Direction = OpenLotDirection.Short } })
            .ToArray();

        var relief = new OpenLotReliefService().Select(lots, 4m, method,
            method == OpenLotReliefMethod.SpecificId ? lots.Select(lot => lot.TaxLotRecordId).ToArray() : null);

        relief.Quantity.Should().Be(4m);
        relief.TransactionCostBasis.Should().Be(400m);
        relief.FunctionalCostBasis.Should().Be(480m);
        relief.Selections.Should().OnlyContain(slice => slice.Quantity > 0m);
    }

    [Fact]
    public void LongConsumers_CannotBindAShortCanonicalLotWithOtherwiseIdenticalEconomics()
    {
        var lot = OpenLotConvergenceTests.Lot(1).ToOpenLot();
        lot = lot with { Acquisition = lot.Acquisition with { Direction = OpenLotDirection.Short } };
        var openedAt = new DateTimeOffset(lot.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var execution = new TaxLot(lot.TaxLotRecordId, "OLD", 10, 100m, openedAt) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m };
        var simulation = new OpenLot(lot.TaxLotRecordId, "NEW", 10, 100m, openedAt, Guid.NewGuid()) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m };

        var bindExecution = () => execution.BindCanonical(lot);
        var bindSimulation = () => simulation.BindCanonical(lot);

        bindExecution.Should().Throw<InvalidOperationException>().WithMessage("*direction-aware posting*");
        bindSimulation.Should().Throw<InvalidOperationException>().WithMessage("*direction-aware posting*");
    }

    [Fact]
    public void ProductionDisposal_DoesNotTreatShortBasisAsAnAssetCredit()
    {
        var retained = OpenLotConvergenceTests.Lot(1);
        retained = retained with { Acquisition = retained.Acquisition! with { Direction = OpenLotDirection.Short } };
        var selection = new LedgerTaxLotDisposalSelection(retained.TaxLotRecordId, retained.LotId,
            retained.Version, retained.OpenQuantity, 1m, 0, retained.Acquisition.Evidence[0].EvidenceId,
            retained.UnitCost, 110m);

        var validate = () => CanonicalOpenLotDisposalGuard.Validate([retained], [selection], LedgerTaxLotReliefMethod.Fifo, "USD");

        validate.Should().Throw<LedgerValidationException>().WithMessage("*Short covers require direction-aware posting*");
    }

    [Fact]
    public void LongAmortization_DoesNotRepriceAShortLiability()
    {
        var inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);
        inputs = inputs with
        {
            ExpectedLot = inputs.ExpectedLot with
            { Acquisition = inputs.ExpectedLot.Acquisition with { Direction = OpenLotDirection.Short } }
        };

        var project = () => OpenLotAmortization.Project(inputs);

        project.Should().Throw<ArgumentException>().WithMessage("*supports Long lots only*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessorPosting_CannotIntroduceShortDirection(bool changeSuccessor)
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var target = OpenLotSuccessorTestData.Successor(source, 1m);
        var inputs = OpenLotSuccessorTestData.Build(source, [target]);
        inputs = changeSuccessor
            ? inputs with { Successors = [target with { Lot = target.Lot with { Acquisition = target.Lot.Acquisition with { Direction = OpenLotDirection.Short } } }] }
            : inputs with { ExpectedLot = source with { Acquisition = source.Acquisition with { Direction = OpenLotDirection.Short } } };

        var validate = () => OpenLotSuccessors.Validate(inputs);

        validate.Should().Throw<ArgumentException>().WithMessage("*supports Long lots only*");
    }

    [Fact]
    public void LongCapitalReturn_DoesNotReduceAShortLiability()
    {
        var inputs = CapitalReturnTestData.Build(OpenLotSuccessorTestData.Predecessor());
        inputs = inputs with
        {
            ExpectedLot = inputs.ExpectedLot with
            { Acquisition = inputs.ExpectedLot.Acquisition with { Direction = OpenLotDirection.Short } }
        };

        var project = () => OpenLotCapitalReturn.Project(inputs);

        project.Should().Throw<ArgumentException>().WithMessage("*supports Long lots only*");
    }
}
