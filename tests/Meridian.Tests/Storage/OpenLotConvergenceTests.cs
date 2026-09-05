using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class OpenLotConvergenceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(OpenLotReliefMethod.Fifo, 280, 314)]
    [InlineData(OpenLotReliefMethod.Lifo, 330, 429)]
    [InlineData(OpenLotReliefMethod.Hifo, 330, 429)]
    [InlineData(OpenLotReliefMethod.AverageCost, 308, 378.4)]
    public void DecimalRelief_PreservesTransactionAndFunctionalAcquisitionBasis(OpenLotReliefMethod method,
        decimal transaction, decimal functional)
    {
        var first = Lot(1, 2.5m, 100m, 1.1m).ToOpenLot();
        var second = Lot(2, 3.75m, 120m, 1.3m).ToOpenLot();
        var result = new OpenLotReliefService().Select([first, second], 2.75m, method);
        result.Quantity.Should().Be(2.75m);
        result.TransactionCostBasis.Should().Be(transaction);
        result.FunctionalCostBasis.Should().Be(functional);
        result.Selections.Sum(s => s.Quantity).Should().Be(result.Quantity);
    }

    [Fact]
    public void SpecificRelief_RejectsDuplicateAndUnknownIdsEvenAfterQuantityIsFilled()
    {
        var lot = Lot(1).ToOpenLot();
        var service = new OpenLotReliefService();
        var duplicate = () => service.Select([lot], 1m, OpenLotReliefMethod.SpecificId, [lot.TaxLotRecordId, lot.TaxLotRecordId]);
        var unknown = () => service.Select([lot], 1m, OpenLotReliefMethod.SpecificId, [lot.TaxLotRecordId, Guid.NewGuid()]);
        duplicate.Should().Throw<ArgumentException>();
        unknown.Should().Throw<ArgumentException>();
        service.Select([lot], 1m, OpenLotReliefMethod.SpecificId, [lot.TaxLotRecordId]).Selections.Should().ContainSingle();
    }

    [Fact]
    public void Relief_CannotCombineDifferentSecurityOrPositionScopes()
    {
        var first = Lot(1).ToOpenLot();
        var second = Lot(2).ToOpenLot() with { BookPositionId = Guid.NewGuid() };
        var action = () => new OpenLotReliefService().Select([first, second], 1m, OpenLotReliefMethod.Fifo);
        action.Should().Throw<ArgumentException>().WithMessage("*scope*");
    }

    [Fact]
    public void AverageCost_FullDepletionConservesRepeatingDecimalBasis()
    {
        var lots = new[] { Lot(1, 1m, 10m).ToOpenLot(), Lot(2, 2m, 11m).ToOpenLot() };
        var result = new OpenLotReliefService().Select(lots, 3m, OpenLotReliefMethod.AverageCost);
        result.TransactionCostBasis.Should().Be(32m);
        result.FunctionalCostBasis.Should().Be(lots.Sum(l => l.OpenFunctionalCostBasis));
    }

    [Fact]
    public void LegacyRows_DoNotAcquireInventedIdentityOrFx()
    {
        var legacy = Lot(1) with { Acquisition = null, SecurityId = Guid.Empty, BookPositionId = Guid.Empty };
        JsonSerializer.Serialize(legacy).Should().NotContain("Acquisition");
        var project = () => legacy.ToOpenLot();
        project.Should().Throw<LedgerValidationException>().WithMessage("*evidence*");
    }

    [Fact]
    public void AcquisitionEvidence_MustBindTheExactLot()
    {
        var record = Lot(1);
        record = record with { Acquisition = record.Acquisition! with { Evidence = Lot(2).Acquisition!.Evidence } };
        var project = () => record.ToOpenLot();
        project.Should().Throw<ArgumentException>().WithMessage("*exact durable lot*");
    }

    [Fact]
    public void RetainedFx_RoundTripsAndDisagreedBasisFailsClosed()
    {
        var record = Lot(1, 10m, 12m, 1.25m);
        var restored = JsonSerializer.Deserialize<LedgerTaxLotRecord>(JsonSerializer.Serialize(record))!;
        restored.ToOpenLot().OpenFunctionalCostBasis.Should().Be(150m);
        var changed = restored with { Acquisition = restored.Acquisition! with { AcquisitionFxRateToFunctional = 1.5m } };
        var project = () => changed.ToOpenLot();
        project.Should().Throw<ArgumentException>().WithMessage("*acquisition FX*");
    }

    [Fact]
    public void FaceProjection_ConvertsPerHundredStorageUnitsToDeclaredFace()
    {
        var record = Lot(1, 1000m, 102m);
        record = record with
        {
            OriginalFace = 100000m,
            ParBasis = 100m,
            BookedFactor = 0.9m,
            Acquisition = record.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
            }
        };
        var lot = record.ToOpenLot();
        lot.OriginalQuantity.Should().Be(100000m);
        lot.OpenTransactionCostBasis.Should().Be(102000m);
        new OpenLotReliefService().Select([lot], 25000m, OpenLotReliefMethod.Fifo).TransactionCostBasis.Should().Be(25500m);
    }

    [Fact]
    public void ExecutionAndBacktesting_BindTheSameRetainedLotAcrossTickerRename()
    {
        var record = Lot(1);
        var canonical = record.ToOpenLot();
        var openedAt = new DateTimeOffset(record.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var execution = new TaxLot(record.TaxLotRecordId, "OLD", 10, 100m, openedAt);
        var simulation = new OpenLot(record.TaxLotRecordId, "NEW", 10, 100m, openedAt, Guid.NewGuid());
        execution.BindCanonical(canonical).Should().BeSameAs(canonical);
        simulation.BindCanonical(canonical).Should().BeSameAs(canonical);
        var changed = () => (execution with { Quantity = 9 }).BindCanonical(canonical);
        changed.Should().Throw<InvalidOperationException>();
    }

    internal static LedgerTaxLotRecord Lot(int day, decimal quantity = 10m, decimal price = 100m, decimal fx = 1.1m)
    {
        var id = Guid.NewGuid();
        var date = new DateOnly(2026, 1, day);
        var now = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("acquisition:" + id, "evidence://acquisition/" + id,
            new string('a', 64), "custodian", "retained-source", "Accepted", "reviewer", now, date, 1,
            now, "custodian", "OpenLotAcquisition", id.ToString("D"));
        return new(id, BookId, new LedgerAccount("Investments", LedgerAccountType.Asset, "OLD"), "lot-" + day,
            date, quantity, quantity, price, "EUR", now, now, Version: 1, SecurityId: SecurityId, BookPositionId: PositionId,
            Acquisition: new(LotQuantityBasis.Units, "EUR", "USD", fx, quantity * price, quantity * price * fx,
                date, null, [evidence]));
    }
}
