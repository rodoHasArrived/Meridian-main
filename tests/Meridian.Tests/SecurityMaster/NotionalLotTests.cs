using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The notional lot: the lot unit the asset-class registry assigns to swaps, credit default swaps,
/// swaptions and caps/floors, which until now had no lot type.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotionalLotTests
{
    private static readonly Guid Security = Guid.NewGuid();
    private static readonly DateOnly Effective = new(2026, 6, 15);

    [Fact]
    public void NotionalLot_SizeIsTheNotionalOutstanding()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 10_000_000m);

        LotQuantity.Of(lot).Should().Be(new LotQuantity(10_000_000m, SecurityLotUnitKind.Notional, 1m));
        lot.CurrentNotional.Should().Be(10_000_000m);
        lot.AmortizationFactor.Should().Be(1m);
        lot.AmortizedNotional.Should().Be(0m);
        lot.CostBasis.Should().Be(0m, "a par swap exchanges nothing at inception");
        lot.IsFullyAmortized.Should().BeFalse();
    }

    [Fact]
    public void NotionalLot_MatchesTheRegistryUnitForItsClasses()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 10_000_000m);

        foreach (var assetClass in new[] { "Swap", "CreditDefaultSwap", "Swaption", "CapFloor" })
        {
            LotQuantity.UnitFor(assetClass).UnitKind.Should().Be(((IUnitizedLot)lot).UnitKind, assetClass);
            SecurityLotModel.ForAssetClass(assetClass).CanonicalLotType.Should().Be<NotionalLot>(assetClass);
        }
    }

    [Fact]
    public void Amortize_ReducesTheNotionalAndKeepsTheOriginal()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 10_000_000m, upfrontPayment: 25_000m);

        var stepped = lot.Amortize(2_500_000m);

        stepped.CurrentNotional.Should().Be(7_500_000m);
        stepped.OriginalNotional.Should().Be(10_000_000m);
        stepped.AmortizationFactor.Should().Be(0.75m);
        stepped.AmortizedNotional.Should().Be(2_500_000m);
        stepped.UpfrontPayment.Should().Be(25_000m, "amortization changes the notional, not the cost");
        LotQuantity.Of(stepped).Amount.Should().Be(7_500_000m);
        lot.CurrentNotional.Should().Be(10_000_000m, "lots are immutable");
    }

    [Fact]
    public void Amortize_ToZero_IsFullyAmortized()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 1_000_000m).Amortize(1_000_000m);

        lot.IsFullyAmortized.Should().BeTrue();
        LotQuantity.Of(lot).Amount.Should().Be(0m);
    }

    [Fact]
    public void Amortize_RefusesNonPositiveOrExcessAmounts()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 1_000_000m);

        lot.Invoking(l => l.Amortize(0m)).Should().Throw<ArgumentOutOfRangeException>();
        lot.Invoking(l => l.Amortize(1_000_000.01m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithNotional_FollowsAScheduleStep()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 1_000_000m);

        lot.WithNotional(400_000m).CurrentNotional.Should().Be(400_000m);
        lot.Invoking(l => l.WithNotional(1_000_000.01m)).Should().Throw<ArgumentOutOfRangeException>("the notional cannot grow past inception");
        lot.Invoking(l => l.WithNotional(-1m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RefusesInvalidLots()
    {
        var create = (string id, Guid security, decimal original, decimal upfront) =>
            new NotionalLot(id, security, Effective, original, upfrontPayment: upfront);

        create.Invoking(c => c(" ", Security, 1m, 0m)).Should().Throw<ArgumentException>();
        create.Invoking(c => c("lot", Guid.Empty, 1m, 0m)).Should().Throw<ArgumentException>();
        create.Invoking(c => c("lot", Security, 0m, 0m)).Should().Throw<ArgumentOutOfRangeException>();
        create.Invoking(c => c("lot", Security, 1m, -1m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToLedgerTaxLot_CarriesTheNotionalAndReproducesTheUpfrontCost()
    {
        var lot = new NotionalLot("lot-cds-1", Security, Effective, 5_000_000m, upfrontPayment: 125_000m);

        var ledger = lot.ToLedgerTaxLot();

        ledger.UnitKind.Should().Be(SecurityLotUnitKind.Notional);
        ledger.Multiplier.Should().Be(1m);
        ledger.Quantity.Should().Be(5_000_000m);
        (ledger.Quantity * ledger.UnitCost).Should().Be(125_000m);
        ledger.SecurityId.Should().Be(Security);
        ledger.AcquiredDate.Should().Be(Effective);
    }

    [Fact]
    public void ToLedgerTaxLot_ParContract_HasZeroCost()
    {
        new NotionalLot("lot-irs-1", Security, Effective, 5_000_000m).ToLedgerTaxLot().UnitCost.Should().Be(0m);
    }

    [Fact]
    public void ToLedgerTaxLot_FullyAmortized_Refuses()
    {
        var lot = new NotionalLot("lot-irs-1", Security, Effective, 1m).Amortize(1m);

        lot.Invoking(l => l.ToLedgerTaxLot()).Should().Throw<InvalidOperationException>();
    }
}
