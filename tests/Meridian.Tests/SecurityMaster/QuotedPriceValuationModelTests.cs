using FluentAssertions;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The quoted-price model passes the golden-copy selection through: price, unit and receipt become
/// the result, the staleness verdict is a diagnostic, market value follows the quote unit, and no
/// selection is a blocker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuotedPriceValuationModelTests
{
    private static readonly DateTimeOffset PriceAsOf = ValuationTestSecurities.AsOf.AddDays(-1);
    private static readonly DateTimeOffset KnownAt = ValuationTestSecurities.AsOf.AddHours(6);

    [Fact]
    public async Task PassesTheSelectionThrough_AndValuesFaceValueLotsPercentOfPar()
    {
        var bond = ValuationTestSecurities.Bond();
        var receiptId = Guid.NewGuid();
        var pricing = Pricing(bond, new SecurityPriceGoldenCopyDto(
            bond.SecurityId, 101.4m, SecurityPriceKind.MarketGoldenCopy, "BLOOMBERG", PriceAsOf, false, 1, [],
            SecurityPriceUnit.PercentOfPar, ValuationTestSecurities.AsOf, PriceAsOf, KnownAt, receiptId));
        var model = new QuotedPriceValuationModel(pricing);

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, ValuationTestSecurities.AsOf, KnownAt, "ACC-1", Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.Model.Should().Be(SecurityValuationModelKind.QuotedPrice);
        result.ModelKey.Should().Be("quoted-price");
        result.Price.Should().Be(101.4m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.PresentValue.Should().BeNull();
        result.MarketValue.Should().Be(507_000.00m, "500,000 face x 101.4 / 100");
        result.ObservedAsOf.Should().Be(PriceAsOf);
        result.KnowledgeAsOf.Should().Be(KnownAt);
        result.Observability.Should().Be(SecurityValuationObservability.QuotedPrice);
        result.Currency.Should().Be("USD");
        var input = result.Inputs.Should().ContainSingle().Subject;
        input.Kind.Should().Be("PriceSelectionReceipt");
        input.Reference.Should().Be("BLOOMBERG");
        input.Id.Should().Be(receiptId);
        input.AsOf.Should().Be(PriceAsOf);
        result.Diagnostics.Should().BeEmpty();
        await pricing.Received(1).GetGoldenCopyPriceAsOfAsync(bond.SecurityId, "ACC-1", ValuationTestSecurities.AsOf, Arg.Any<CancellationToken>(), KnownAt);
    }

    [Fact]
    public async Task APercentOfParQuote_ScalesTheFaceByTheFactorInEffectOnTheValuationDate()
    {
        // The dated schedule says 0.5 on 2025-12-01 (0.4 only from 2026-06-01); the undated scalar
        // says 0.8. The factor in effect on 2026-01-01 is the schedule's 0.5, not the scalar.
        var bond = ValuationTestSecurities.FactorScheduledBond();
        var pricing = Pricing(bond, new SecurityPriceGoldenCopyDto(
            bond.SecurityId, 101.4m, SecurityPriceKind.MarketGoldenCopy, "BLOOMBERG", PriceAsOf, false, 1, [],
            SecurityPriceUnit.PercentOfPar, ValuationTestSecurities.AsOf, PriceAsOf, KnownAt, Guid.NewGuid()));

        var result = await new QuotedPriceValuationModel(pricing).ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, ValuationTestSecurities.AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.Price.Should().Be(101.4m);
        result.MarketValue.Should().Be(253_500.00m, "500,000 par x 0.5 x 101.4 / 100");
    }

    [Fact]
    public async Task AScalarFactorDatedAfterTheValuationDate_DoesNotReduceTheFaceUntilItsDate()
    {
        // The scalar 0.8 is effective from 2026-02-01 and there is no schedule: on 2026-01-01 the face
        // is unfactored, as the cash-flow projection reads it; from 2026-02-01 the factor applies.
        var factorDate = new DateOnly(2026, 2, 1);
        var bond = ValuationTestSecurities.DatedFactorBond(0.8m, factorDate);
        var pricing = Pricing(bond, new SecurityPriceGoldenCopyDto(
            bond.SecurityId, 100m, SecurityPriceKind.MarketGoldenCopy, "BLOOMBERG", PriceAsOf, false, 1, [],
            SecurityPriceUnit.PercentOfPar, ValuationTestSecurities.AsOf, PriceAsOf, KnownAt, Guid.NewGuid()));
        var model = new QuotedPriceValuationModel(pricing);

        var before = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, ValuationTestSecurities.AsOf, Quantity: 500_000m), bond, CancellationToken.None);
        var onTheDate = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, new DateTimeOffset(factorDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), Quantity: 500_000m), bond, CancellationToken.None);

        before.MarketValue.Should().Be(500_000.00m, "the 0.8 factor is not yet in effect: 500,000 par x 1 x 100 / 100");
        onTheDate.MarketValue.Should().Be(400_000.00m, "from its date the factor applies: 500,000 par x 0.8 x 100 / 100");
    }

    [Fact]
    public async Task AQuoteWithoutAUnit_HasNoMarketValue_AndSaysSo()
    {
        var bond = ValuationTestSecurities.Bond();
        var pricing = Pricing(bond, new SecurityPriceGoldenCopyDto(
            bond.SecurityId, 101.4m, SecurityPriceKind.MarketGoldenCopy, "BLOOMBERG", PriceAsOf, false, 1, [], SecurityPriceUnit.Unspecified));

        var result = await new QuotedPriceValuationModel(pricing).ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, ValuationTestSecurities.AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.Price.Should().Be(101.4m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.Unspecified);
        result.MarketValue.Should().BeNull("zero is never substituted for a value that cannot be computed");
        result.Diagnostics.Should().ContainSingle().Which.Should().StartWith("MarketValueUnavailable");
    }

    [Fact]
    public async Task ValuesUnitLotsPerUnit_AndReportsAStaleFallbackAsADiagnostic()
    {
        var equity = ValuationTestSecurities.Equity();
        var pricing = Pricing(equity, new SecurityPriceGoldenCopyDto(
            equity.SecurityId, 12.5m, SecurityPriceKind.MarketGoldenCopy, "POLYGON", PriceAsOf, true, 9, [],
            SecurityPriceUnit.CurrencyPerUnit, ValuationTestSecurities.AsOf, PriceAsOf, KnownAt, Guid.NewGuid()));
        var model = new QuotedPriceValuationModel(pricing);

        var result = await model.ValueAsync(new SecurityValuationRequest(equity.SecurityId, ValuationTestSecurities.AsOf, Quantity: 1_000m), equity, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.Price.Should().Be(12.5m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.CurrencyPerUnit);
        result.MarketValue.Should().Be(12_500m, "1,000 shares x 12.5");
        result.Diagnostics.Should().ContainSingle().Which.Should().StartWith("StaleFallback").And.Contain("POLYGON").And.Contain("9 day");
    }

    [Fact]
    public async Task WithoutAQuantity_ThereIsNoMarketValue()
    {
        var equity = ValuationTestSecurities.Equity();
        var pricing = Pricing(equity, new SecurityPriceGoldenCopyDto(
            equity.SecurityId, 12.5m, SecurityPriceKind.MarketGoldenCopy, "POLYGON", PriceAsOf, false, 0, [], SecurityPriceUnit.CurrencyPerUnit));

        var result = await new QuotedPriceValuationModel(pricing).ValueAsync(new SecurityValuationRequest(equity.SecurityId, ValuationTestSecurities.AsOf), equity, CancellationToken.None);

        result.Price.Should().Be(12.5m);
        result.Quantity.Should().BeNull();
        result.MarketValue.Should().BeNull();
    }

    [Fact]
    public async Task NoSelection_Blocks_AndSubstitutesNothing()
    {
        var bond = ValuationTestSecurities.Bond();
        var pricing = Pricing(bond, null);

        var result = await new QuotedPriceValuationModel(pricing).ValueAsync(new SecurityValuationRequest(bond.SecurityId, ValuationTestSecurities.AsOf, Quantity: 1m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeTrue();
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("NoPriceSelection");
        result.Price.Should().BeNull();
        result.MarketValue.Should().BeNull();
        result.PriceUnit.Should().Be(SecurityPriceUnit.Unspecified);
    }

    private static ISecurityMasterPricingService Pricing(SecurityDetailDto security, SecurityPriceGoldenCopyDto? selection)
    {
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        pricing.GetGoldenCopyPriceAsOfAsync(security.SecurityId, Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(selection);
        return pricing;
    }
}
