using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class CanonicalOpenLotConsumerTests
{
    [Fact]
    public void BacktestingComposite_ExactBasisBindsDespiteRoundedQuoteAndRoundTrip()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 1m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 0.25m, 0.75m, 0.75m, 2.25m)
        };
        (simulated.Quantity * simulated.EntryPrice).Should().NotBe(retained.OpenTransactionCostBasis);
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
        var restored = JsonSerializer.Deserialize<OpenLot>(JsonSerializer.Serialize(simulated))!;
        restored.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_MatchingQuoteCannotHideDifferentRetainedBasis()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 300m, 599m)
        };
        (simulated.Quantity * simulated.EntryPrice).Should().Be(retained.OpenTransactionCostBasis);
        simulated.CostBasis().Should().Be(899m);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*basis differs*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BacktestingComposite_OrdinaryLotCannotTrustUnexpectedComponents(bool matchingQuote)
    {
        var retained = BacktestingRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with
        {
            EntryPrice = matchingQuote ? 300m : 1m,
            BasisComponents = BacktestingComponents(retained, 300m, 600m)
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);
        (simulated.Quantity * simulated.EntryPrice == retained.OpenTransactionCostBasis).Should().Be(matchingQuote);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*component*");
    }

    [Theory]
    [InlineData("total-quantity")]
    [InlineData("zero-quantity")]
    [InlineData("negative-quantity")]
    [InlineData("empty-source")]
    [InlineData("different-source")]
    [InlineData("mixed-sources")]
    [InlineData("successor-as-source")]
    [InlineData("empty-fill")]
    [InlineData("mixed-fills")]
    [InlineData("different-date")]
    [InlineData("mixed-open-times")]
    [InlineData("negative-basis")]
    public void BacktestingComposite_EqualTotalCannotHideInvalidComponentFacts(string fault)
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var components = BacktestingComponents(retained, 300m, 600m);
        components = fault switch
        {
            "total-quantity" => [components[0] with { SuccessorQuantity = 0.5m }, components[1]],
            "zero-quantity" => [components[0] with { SuccessorQuantity = 0m }, components[1] with { SuccessorQuantity = 3m }],
            "negative-quantity" => [components[0] with { SuccessorQuantity = -1m }, components[1] with { SuccessorQuantity = 4m }],
            "empty-source" => [components[0] with { SourceLotId = Guid.Empty }, components[1]],
            "different-source" => [components[0] with { SourceLotId = Guid.NewGuid() }, components[1] with { SourceLotId = Guid.NewGuid() }],
            "mixed-sources" => [components[0], components[1] with { SourceLotId = Guid.NewGuid() }],
            "successor-as-source" => components.Select(component => component with { SourceLotId = retained.TaxLotRecordId }).ToArray(),
            "empty-fill" => components.Select(component => component with { SourceOpenFillId = Guid.Empty }).ToArray(),
            "mixed-fills" => [components[0], components[1] with { SourceOpenFillId = Guid.NewGuid() }],
            "different-date" => components.Select(component => component with { OpenedAt = component.OpenedAt.AddDays(-1) }).ToArray(),
            "mixed-open-times" => [components[0], components[1] with { OpenedAt = components[1].OpenedAt.AddHours(1) }],
            "negative-basis" => [components[0] with { AllocatedBasis = -300m }, components[1] with { AllocatedBasis = 1_200m }],
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var simulated = BacktestingLot(retained) with { BasisComponents = components };
        // Every forged case preserves the aggregate carrying basis that the old adapter trusted.
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*component*");
    }

    [Fact]
    public void BacktestingComposite_NullComponentFailsClosedBeforeBasisAggregation()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with { BasisComponents = [null!] };

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*component*");
    }

    [Theory]
    [InlineData("unsupported-action")]
    [InlineData("unreviewed-evidence")]
    public void BacktestingComposite_ExactComponentsCannotAuthorizeInvalidCanonicalEvidence(string fault)
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 300m, 600m)
        };
        retained = retained with
        {
            Acquisition = fault == "unsupported-action"
                ? retained.Acquisition with
                {
                    CorporateActionLineage = retained.Acquisition.CorporateActionLineage! with
                    {
                        ActionType = CorporateActionAccountingTypeDto.CashDividend
                    }
                }
                : retained.Acquisition with
                {
                    Evidence = [retained.Acquisition.Evidence[0] with { ReviewStatus = "Pending" }]
                }
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<ArgumentException>().WithMessage(fault == "unsupported-action"
            ? "*Successor acquisition origin*" : "*complete retained evidence*");
    }

    [Fact]
    public void BacktestingComposite_SingleComponentSuccessorBindsWithExactBasis()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 1m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = [BacktestingComponents(retained, 1m, 0m, 3m, 0m)[0]]
        };
        simulated.BasisComponents.Should().ContainSingle();
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);
        (simulated.Quantity * simulated.EntryPrice).Should().NotBe(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_FlattenedAncestorCannotReplaceImmediateCanonicalPredecessor()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var ancestor = Guid.NewGuid();
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 300m, 600m)
                .Select(component => component with { SourceLotId = ancestor }).ToArray()
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*component*");
    }

    [Fact]
    public void BacktestingComposite_PartialReliefUsesExactCurrentBasisRatherThanAcquisitionBasis()
    {
        var retained = BacktestingSuccessorRetainedLot(9m, 3m) with
        {
            OpenQuantity = 3m,
            OpenTransactionCostBasis = 1m,
            OpenFunctionalCostBasis = 1.2m,
            Version = 2
        };
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 0.25m, 0.75m, 0.75m, 2.25m)
        };
        retained.Acquisition.TransactionCostBasis.Should().Be(3m);
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_HomogeneousPartitionsCertifyAggregateParityOnly()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 400m, 500m)
        };
        // The retained DTO proves one source pool and its carrying total, not a component allocation manifest.
        simulated.BasisComponents.Sum(component => component.SuccessorQuantity).Should().Be(retained.OpenQuantity);
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_PartialReliefPreservesSimulationDecimalResidual()
    {
        var retained = BacktestingSuccessorRetainedLot(3m, 1m);
        var originalQuantity = retained.OpenQuantity;
        var originalBasis = retained.OpenTransactionCostBasis;
        var sourceQuantity = 0.5m;
        var sourceBasis = originalBasis * sourceQuantity / originalQuantity;
        var remainingQuantity = 1m;
        var remainingBasis = originalBasis * remainingQuantity / originalQuantity;
        var quantityRatio = remainingQuantity / originalQuantity;
        var basisRatio = remainingBasis / originalBasis;
        var firstQuantity = sourceQuantity * quantityRatio;
        var firstBasis = sourceBasis * basisRatio;
        retained = retained with
        {
            OpenQuantity = remainingQuantity,
            OpenTransactionCostBasis = remainingBasis,
            OpenFunctionalCostBasis = remainingBasis * retained.Acquisition.AcquisitionFxRateToFunctional,
            Version = 2
        };
        // ReduceOpenLot scales retained component facts and assigns the exact final residual.
        // A new proportional calculation would differ by one decimal unit and reject valid relief.
        firstBasis.Should().NotBe(remainingBasis * firstQuantity / remainingQuantity);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, firstBasis, remainingBasis - firstBasis,
                firstQuantity, remainingQuantity - firstQuantity)
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_LargeExactBasisDoesNotRequireAllocationMultiplication()
    {
        var basis = decimal.MaxValue / 2m;
        var retained = BacktestingSuccessorRetainedLot(4m, basis);
        var firstBasis = basis / 4m * 3m;
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, firstBasis, basis - firstBasis, 3m, 1m)
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Fact]
    public void BacktestingComposite_FinalComponentPreservesExactDecimalResidual()
    {
        var retained = BacktestingSuccessorRetainedLot(7m, 1m);
        var component = BacktestingComponents(retained, 0m, 0m)[0];
        var slice = 1m / 7m;
        var residual = 1m - slice - slice;
        residual.Should().NotBe(1m * 5m / 7m, "the final component absorbs decimal division residue");
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents =
            [
                component with { SuccessorQuantity = 1m, AllocatedBasis = slice },
                component with { SuccessorQuantity = 1m, AllocatedBasis = slice },
                component with { SuccessorQuantity = 5m, AllocatedBasis = residual }
            ]
        };
        simulated.CostBasis().Should().Be(1m);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Backtesting_ExplicitShortCannotBindCanonicalLong(bool composite)
    {
        var retained = BacktestingRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained) with
        {
            IsShort = true,
            BasisComponents = composite ? BacktestingComponents(retained, 300m, 600m) : []
        };
        simulated.CostBasis().Should().Be(retained.OpenTransactionCostBasis);

        var bind = () => simulated.BindCanonical(retained);

        bind.Should().Throw<InvalidOperationException>().WithMessage("*Short-lot*direction-aware posting*certified consumer parity*");
    }

    [Fact]
    public void Backtesting_OrdinaryExactBasisWithoutComponentsStillBinds()
    {
        var retained = BacktestingRetainedLot(3m, 900m);
        var simulated = BacktestingLot(retained);

        simulated.BindCanonical(retained).Should().BeSameAs(retained);
    }

    private static OpenLotDto BacktestingRetainedLot(decimal quantity, decimal basis)
    {
        var retained = DurableLot(1).ToOpenLot();
        return retained with
        {
            OriginalQuantity = quantity,
            OpenQuantity = quantity,
            OpenTransactionCostBasis = basis,
            OpenFunctionalCostBasis = basis * retained.Acquisition.AcquisitionFxRateToFunctional,
            Acquisition = retained.Acquisition with
            {
                TransactionCostBasis = basis,
                FunctionalCostBasis = basis * retained.Acquisition.AcquisitionFxRateToFunctional
            }
        };
    }

    private static OpenLotDto BacktestingSuccessorRetainedLot(decimal quantity, decimal basis)
    {
        var retained = BacktestingRetainedLot(quantity, basis);
        return retained with
        {
            Acquisition = retained.Acquisition with
            {
                CorporateActionLineage = new(Guid.NewGuid(), CorporateActionAccountingTypeDto.StockSplit,
                    retained.AcquiredDate, Guid.NewGuid(), 1, 100m, CorporateActionSuccessorRoleDto.Successor, [])
                { SourceCorporateActionId = Guid.NewGuid() }
            }
        };
    }

    private static OpenLot BacktestingLot(OpenLotDto retained) => new(
        retained.TaxLotRecordId, "DISPLAY", (long)retained.OpenQuantity,
        retained.OpenTransactionCostBasis / retained.OpenQuantity,
        new DateTimeOffset(retained.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        Guid.NewGuid());

    private static OpenLotBasisComponent[] BacktestingComponents(OpenLotDto retained, decimal first, decimal second,
        decimal firstQuantity = 1m, decimal secondQuantity = 2m)
    {
        var acquiredAt = new DateTimeOffset(retained.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var sourceLotId = retained.Acquisition.CorporateActionLineage?.PredecessorTaxLotRecordId ?? Guid.NewGuid();
        var sourceFillId = Guid.NewGuid();
        return
        [
            new(sourceLotId, sourceFillId, acquiredAt, firstQuantity, first),
            new(sourceLotId, sourceFillId, acquiredAt, secondQuantity, second)
        ];
    }
}
