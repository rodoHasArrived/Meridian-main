using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The commitment and fund-unit lots: the two lot units the asset-class registry already assigned
/// (Commitment for private fund interests and guarantees, Units for funds) that had no lot type.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlternativeAssetLotTests
{
    private static readonly Guid Security = Guid.NewGuid();
    private static readonly DateOnly Committed = new(2026, 3, 31);

    [Fact]
    public void CommitmentLot_SizeIsTheCommitment_AndFundedUnfundedAreSeparateFacts()
    {
        var lot = new CommitmentLot("lot-pe-1", Security, Committed, 5_000_000m, fundedAmount: 1_250_000m);

        LotQuantity.Of(lot).Should().Be(new LotQuantity(5_000_000m, SecurityLotUnitKind.Commitment, 1m));
        lot.UnfundedCommitment.Should().Be(3_750_000m);
        lot.FundedFraction.Should().Be(0.25m);
        lot.IsFullyFunded.Should().BeFalse();
    }

    [Fact]
    public void CommitmentLot_MatchesTheRegistryUnitForItsClasses()
    {
        LotQuantity.UnitFor("PrivateFundInterest").Should().Be((SecurityLotUnitKind.Commitment, 1m));
        LotQuantity.UnitFor("CommitmentGuarantee").Should().Be((SecurityLotUnitKind.Commitment, 1m));
    }

    [Fact]
    public void CommitmentLot_Draw_MovesCapitalFromUnfundedToFunded()
    {
        var lot = new CommitmentLot("lot-pe-2", Security, Committed, 1_000_000m);

        var drawn = lot.Draw(400_000m).Draw(600_000m);

        drawn.FundedAmount.Should().Be(1_000_000m);
        drawn.UnfundedCommitment.Should().Be(0m);
        drawn.IsFullyFunded.Should().BeTrue();
        drawn.TotalCommitment.Should().Be(lot.TotalCommitment, "a drawdown never changes the commitment");
        lot.FundedAmount.Should().Be(0m, "lots are immutable; Draw returns the new state");
    }

    [Fact]
    public void CommitmentLot_RecallableDistribution_RestoresUnfundedCommitment()
    {
        var funded = new CommitmentLot("lot-pe-3", Security, Committed, 1_000_000m).Draw(1_000_000m);

        var recycled = funded.RecordRecallableDistribution(150_000m);

        recycled.UnfundedCommitment.Should().Be(150_000m);
        recycled.IsFullyFunded.Should().BeFalse();
        recycled.Draw(150_000m).FundedFraction.Should().Be(1.15m, "re-contributed recalls take funding past the commitment");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CommitmentLot_RejectsNonPositiveDrawdowns(decimal amount)
    {
        var lot = new CommitmentLot("lot-pe-4", Security, Committed, 100m);

        lot.Invoking(l => l.Draw(amount)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CommitmentLot_RejectsDrawdownsBeyondTheUnfundedBalance()
    {
        var lot = new CommitmentLot("lot-pe-5", Security, Committed, 100m, fundedAmount: 90m);

        lot.Invoking(l => l.Draw(10.01m)).Should().Throw<ArgumentOutOfRangeException>().WithMessage("*unfunded commitment of 10*");
        lot.Draw(10m).IsFullyFunded.Should().BeTrue();
    }

    [Fact]
    public void CommitmentLot_RejectsInvalidConstruction()
    {
        var act = () => new CommitmentLot("lot", Security, Committed, 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();

        act = () => new CommitmentLot("lot", Security, Committed, 100m, fundedAmount: 101m);
        act.Should().Throw<ArgumentOutOfRangeException>("funding cannot exceed the commitment without recallable distributions");

        act = () => new CommitmentLot("lot", Guid.Empty, Committed, 100m);
        act.Should().Throw<ArgumentException>();

        act = () => new CommitmentLot(" ", Security, Committed, 100m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CommitmentLot_ToLedgerTaxLot_CarriesContributedCapitalAsCurrency()
    {
        var lot = new CommitmentLot("lot-pe-6", Security, Committed, 2_000_000m, fundedAmount: 750_000m);

        var ledger = lot.ToLedgerTaxLot();

        ledger.LotId.Should().Be("lot-pe-6");
        ledger.AcquiredDate.Should().Be(Committed);
        ledger.SecurityId.Should().Be(Security);
        ledger.UnitKind.Should().Be(SecurityLotUnitKind.CurrencyAmount, "the ledger relieves paid-in capital, not the commitment");
        LotQuantity.Of(ledger).EconomicUnits.Should().Be(750_000m);
        (ledger.Quantity * ledger.UnitCost).Should().Be(750_000m);
    }

    [Fact]
    public void CommitmentLot_ToLedgerTaxLot_RefusesAnUnfundedCommitment()
    {
        var lot = new CommitmentLot("lot-pe-7", Security, Committed, 2_000_000m);

        lot.Invoking(l => l.ToLedgerTaxLot()).Should().Throw<InvalidOperationException>().WithMessage("*no contributed capital*");
    }

    [Fact]
    public void FundUnitLot_IsTheUnitDenominatedCase_WithNavCostBasis()
    {
        var lot = new FundUnitLot("lot-fund-1", Security, new DateOnly(2026, 1, 15), 1_234.5678m, 10.4321m);

        LotQuantity.Of(lot).Should().Be(new LotQuantity(1_234.5678m, SecurityLotUnitKind.Units, 1m));
        lot.CostBasis.Should().Be(1_234.5678m * 10.4321m);
        lot.ValueAt(11m).Should().Be(1_234.5678m * 11m);
        LotQuantity.UnitFor("InvestmentFund").Should().Be((SecurityLotUnitKind.Units, 1m));
        LotQuantity.UnitFor("MoneyMarketFund").Should().Be((SecurityLotUnitKind.Units, 1m));
    }

    [Fact]
    public void FundUnitLot_WithReducedUnits_ScalesCostBasisWithTheUnits()
    {
        var lot = new FundUnitLot("lot-fund-2", Security, new DateOnly(2026, 1, 15), 100m, 12.5m);

        var remaining = lot.WithReducedUnits(40m);

        remaining.Units.Should().Be(60m);
        remaining.NavPerUnit.Should().Be(12.5m);
        remaining.CostBasis.Should().Be(750m);
        lot.Invoking(l => l.WithReducedUnits(100m)).Should().Throw<ArgumentOutOfRangeException>("a full redemption closes the lot");
        lot.Invoking(l => l.WithReducedUnits(0m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FundUnitLot_RejectsInvalidConstruction()
    {
        var act = () => new FundUnitLot("lot", Security, new DateOnly(2026, 1, 15), 0m, 10m);
        act.Should().Throw<ArgumentOutOfRangeException>();

        act = () => new FundUnitLot("lot", Security, new DateOnly(2026, 1, 15), 10m, -0.01m);
        act.Should().Throw<ArgumentOutOfRangeException>();

        act = () => new FundUnitLot("lot", Guid.Empty, new DateOnly(2026, 1, 15), 10m, 10m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FundUnitLot_ToLedgerTaxLot_ReproducesCostBasis()
    {
        var lot = new FundUnitLot("lot-fund-3", Security, new DateOnly(2026, 1, 15), 250m, 40m);

        var ledger = lot.ToLedgerTaxLot();

        ledger.UnitKind.Should().Be(SecurityLotUnitKind.Units);
        ledger.Quantity.Should().Be(250m);
        ledger.UnitCost.Should().Be(40m);
        (ledger.Quantity * ledger.UnitCost).Should().Be(lot.CostBasis);
        LotQuantity.Of(ledger).EconomicUnits.Should().Be(250m);
    }
}
