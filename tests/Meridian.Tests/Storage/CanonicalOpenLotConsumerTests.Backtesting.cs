using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class CanonicalOpenLotConsumerTests
{
    [Fact]
    public void BacktestingComposite_ExactBasisBindsDespiteRoundedQuoteAndRoundTrip()
    {
        var retained = BacktestingRetainedLot(3m, 1m);
        var simulated = BacktestingLot(retained) with
        {
            BasisComponents = BacktestingComponents(retained, 0.4m, 0.6m)
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
        var retained = BacktestingRetainedLot(3m, 900m);
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

        bind.Should().Throw<InvalidOperationException>().WithMessage("*Short-lot*direction model*");
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

    private static OpenLot BacktestingLot(OpenLotDto retained) => new(
        retained.TaxLotRecordId, "DISPLAY", (long)retained.OpenQuantity,
        retained.OpenTransactionCostBasis / retained.OpenQuantity,
        new DateTimeOffset(retained.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        Guid.NewGuid());

    private static OpenLotBasisComponent[] BacktestingComponents(OpenLotDto retained, decimal first, decimal second)
    {
        var acquiredAt = new DateTimeOffset(retained.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return
        [
            new(Guid.NewGuid(), Guid.NewGuid(), acquiredAt, 1m, first),
            new(Guid.NewGuid(), Guid.NewGuid(), acquiredAt, 2m, second)
        ];
    }
}
