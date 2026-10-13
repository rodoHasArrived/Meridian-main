using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class CanonicalOpenLotConsumerTests
{
    [Fact]
    public void UnitSuccessor_RoundedQuotePreservesExactBasisOnProjectionAndRelief()
    {
        var lot = FractionalUnitSuccessor();
        (lot.OriginalQuantity * lot.UnitCost).Should().NotBe(lot.Acquisition!.FunctionalCostBasis);

        var canonical = lot.ToOpenLot();

        canonical.OriginalQuantity.Should().Be(17.5m);
        canonical.OpenTransactionCostBasis.Should().Be(1_000m);
        canonical.OpenFunctionalCostBasis.Should().Be(1_200m);
        canonical.Acquisition.Should().BeSameAs(lot.Acquisition);
        var remaining = (lot with { OpenQuantity = 7m, Version = 2 }).ToOpenLot();
        remaining.OpenTransactionCostBasis.Should().Be(400m);
        remaining.OpenFunctionalCostBasis.Should().Be(480m);
        var selection = Selection(lot, 7m) with { ExpectedCostBasis = 480m };
        var relief = CanonicalOpenLotDisposalGuard.Validate([lot], [selection], LedgerTaxLotReliefMethod.Fifo, "USD");
        relief.TransactionCostBasis.Should().Be(400m);
        relief.FunctionalCostBasis.Should().Be(480m);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("origin")]
    [InlineData("birth")]
    [InlineData("empty-birth")]
    [InlineData("adjustment")]
    public void UnitSuccessor_RoundedQuoteCannotHideChangedPriceOrMissingGovernedLineage(string fault)
    {
        var lot = FractionalUnitSuccessor();
        lot = fault switch
        {
            "quote" => lot with { UnitCost = lot.UnitCost + 0.000000000001m },
            "origin" => lot with { Acquisition = lot.Acquisition! with { CorporateActionLineage = null } },
            "birth" => lot with { OriginatingMutationBatchId = null },
            "empty-birth" => lot with { OriginatingMutationBatchId = Guid.Empty },
            _ => lot with { BasisAdjustment = null }
        };

        var project = () => lot.ToOpenLot();

        project.Should().Throw<LedgerValidationException>().WithMessage("*basis differs*retained acquisition*");
    }

    [Fact]
    public void OrdinaryAcquisition_RoundedQuoteCannotReplaceExactAcquisitionEvidence()
    {
        var lot = FractionalUnitSuccessor();
        lot = lot with
        {
            Acquisition = lot.Acquisition! with { CorporateActionLineage = null },
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null,
            BasisAdjustment = null
        };

        var project = () => lot.ToOpenLot();

        project.Should().Throw<LedgerValidationException>().WithMessage("*basis differs*retained acquisition*");
    }

    private static LedgerTaxLotRecord FractionalUnitSuccessor()
    {
        var lot = DurableLot(1);
        var batch = Guid.NewGuid();
        return lot with
        {
            OriginalQuantity = 17.5m,
            OpenQuantity = 17.5m,
            UnitCost = 68.571428571429m,
            OriginatingMutationBatchId = batch,
            LastMutationBatchId = batch,
            Acquisition = lot.Acquisition! with
            {
                CorporateActionLineage = new(Guid.NewGuid(), CorporateActionAccountingTypeDto.SpinOff,
                    lot.AcquiredDate.AddDays(1), Guid.NewGuid(), 1, 33.33m, CorporateActionSuccessorRoleDto.Child, [])
                { SourceCorporateActionId = Guid.NewGuid() }
            },
            BasisAdjustment = new(batch, OpenLotBasisAdjustmentReasons.CorporateActionSuccessor, 17.5m, 1_000m, 1_200m)
        };
    }
}
