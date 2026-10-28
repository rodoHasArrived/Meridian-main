using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>The contract model's write rules, which every store applies before touching a table.</summary>
[Trait("Category", "Unit")]
public sealed class OtcContractRulesTests
{
    private static readonly DateOnly Trade = new(2026, 3, 2);

    [Fact]
    public void MasterAgreement_IsNormalized_AndNamesBothParties()
    {
        var normalized = OtcContractRules.Normalize(new UpsertOtcMasterAgreementRequest(
            null, "  Big Bank ", "5493001kjtiigc8y1r12", " Meridian Master Fund ", null, OtcMasterAgreementType.Isda2002, Trade, " NY ", " ", null));

        normalized.Counterparty.Should().Be("Big Bank");
        normalized.CounterpartyLei.Should().Be("5493001KJTIIGC8Y1R12");
        normalized.OurEntity.Should().Be("Meridian Master Fund");
        normalized.GoverningLaw.Should().Be("NY");
        normalized.Notes.Should().BeNull();
        normalized.UpdatedBy.Should().Be("unknown");

        FluentActions.Invoking(() => OtcContractRules.Normalize(new UpsertOtcMasterAgreementRequest(null, " ", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade)))
            .Should().Throw<ArgumentException>().WithMessage("*counterparty*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(new UpsertOtcMasterAgreementRequest(null, "Bank", "SHORT", "Fund", null, OtcMasterAgreementType.Isda2002, Trade)))
            .Should().Throw<ArgumentException>().WithMessage("*20 characters*");
    }

    [Fact]
    public void CollateralAgreement_ChecksAmounts_Window_AndHaircuts()
    {
        var master = Guid.NewGuid();
        var normalized = OtcContractRules.Normalize(new UpsertOtcCollateralAgreementRequest(
            null, master, "usd", 0m, 250_000m, 100_000m, null, 10_000m, "Daily",
            [new OtcEligibleCollateralDto(" USD cash ", null, 0m), new OtcEligibleCollateralDto("US Treasuries", " Bond ", 2m)],
            Trade, null, null, "casey"));

        normalized.Currency.Should().Be("USD");
        normalized.EligibleCollateral.Should().HaveCount(2);
        normalized.EligibleCollateral[0].Description.Should().Be("USD cash");
        normalized.EligibleCollateral[1].AssetClass.Should().Be("Bond");

        UpsertOtcCollateralAgreementRequest Valid() => new(null, master, "USD", 0m, 0m, 0m, null, null, "Daily", [], Trade);

        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { ThresholdTheirs = -1m })).Should().Throw<ArgumentException>().WithMessage("*threshold*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { MinimumTransferAmount = -1m })).Should().Throw<ArgumentException>().WithMessage("*minimum transfer*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { RoundingAmount = 0m })).Should().Throw<ArgumentException>().WithMessage("*rounding*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { TerminationDate = Trade.AddDays(-1) })).Should().Throw<ArgumentException>().WithMessage("*terminate before*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { Currency = "US" })).Should().Throw<ArgumentException>().WithMessage("*three-letter*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { EligibleCollateral = [new OtcEligibleCollateralDto("Equities", null, 101m)] })).Should().Throw<ArgumentException>().WithMessage("*between 0 and 100*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { MasterAgreementId = Guid.Empty })).Should().Throw<ArgumentException>().WithMessage("*master agreement*");
    }

    [Fact]
    public void Contract_RequiresPositiveNotional_OrderedDates_AndAHome()
    {
        UpsertOtcContractRequest Valid() => new(null, Guid.NewGuid(), Guid.NewGuid(), " T-1 ", OtcContractDirection.Buy, 10_000_000m, "eur", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Pending);

        var normalized = OtcContractRules.Normalize(Valid());
        normalized.ExternalTradeId.Should().Be("T-1");
        normalized.NotionalCurrency.Should().Be("EUR");

        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { Notional = 0m })).Should().Throw<ArgumentException>().WithMessage("*notional*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { EffectiveDate = Trade.AddDays(-1) })).Should().Throw<ArgumentException>().WithMessage("*before it is traded*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { TerminationDate = Trade })).Should().Throw<ArgumentException>().WithMessage("*terminate before*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { SecurityId = Guid.Empty })).Should().Throw<ArgumentException>().WithMessage("*instrument*");
        FluentActions.Invoking(() => OtcContractRules.Normalize(Valid() with { NettingSetId = Guid.Empty })).Should().Throw<ArgumentException>().WithMessage("*netting set*");
    }

    [Fact]
    public void OnlyBilateralAssetClasses_CarryContracts()
    {
        foreach (var assetClass in new[] { "Swap", "FxForward", "NonDeliverableForward", "CreditDefaultSwap", "Swaption", "CapFloor", "Repo", "Cfd" })
        {
            FluentActions.Invoking(() => OtcContractRules.RequireBilateralAssetClass(assetClass)).Should().NotThrow(assetClass);
        }

        foreach (var assetClass in new[] { "Equity", "Bond", "Option", "Future", "StructuredNote", "NotAClass" })
        {
            FluentActions.Invoking(() => OtcContractRules.RequireBilateralAssetClass(assetClass)).Should().Throw<ArgumentException>(assetClass);
        }
    }

    [Theory]
    [InlineData(OtcContractStatus.Pending, false)]
    [InlineData(OtcContractStatus.Confirmed, false)]
    [InlineData(OtcContractStatus.Terminated, true)]
    [InlineData(OtcContractStatus.Novated, true)]
    [InlineData(OtcContractStatus.Matured, true)]
    public void ClosedStatuses_AreTerminalHistory(OtcContractStatus status, bool closed)
        => OtcContractRules.IsClosed(status).Should().Be(closed);
}
