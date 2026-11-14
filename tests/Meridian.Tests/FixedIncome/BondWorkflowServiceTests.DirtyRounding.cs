using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed partial class BondWorkflowServiceTests
{
    [Theory]
    [InlineData("USD", 1)]
    [InlineData("USD", -1)]
    [InlineData("JPY", 1)]
    [InlineData("JPY", -1)]
    [InlineData("KWD", 1)]
    [InlineData("KWD", -1)]
    public async Task DirtyQuoteCashRemainsAuthoritativeAtSignedNativeRoundingBoundaries(string currency, int sign)
    {
        var fixture = DirtyRoundingFixture(currency, sign);
        var result = (await fixture.Service.CalculateAsync(fixture.Request))!;
        var roundedDirty = fixture.Face + (sign < 0 ? fixture.Minor : 0m);
        var roundedAccrued = sign * fixture.Minor;

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.ContractualAccruedInterest.Should().Be(sign * fixture.Minor / 2m);
        result.AccruedInterest.Should().Be(roundedAccrued);
        result.DirtyConsideration.Should().Be(roundedDirty);
        result.CleanConsideration.Should().Be(roundedDirty - roundedAccrued);
        (result.CleanConsideration + result.AccruedInterest).Should().Be(result.DirtyConsideration);
        result.Fees.Should().Be(fixture.Minor);
        result.TotalCash.Should().Be(roundedDirty + fixture.Minor);
        result.OriginalDirtyConsideration.Should().Be(roundedDirty);
        result.SettlementDifference.Should().Be(0m);
        result.Issues.Should().NotContain(issue => issue.Code == "ConfirmationDifference");
        result.Inputs.Should().Contain(input => input.Kind == "QuoteObservation");
        result.Inputs.Should().Contain(input => input.Kind == "PriceSelectionReceipt");
        result.ProviderYield.Should().Be(.012345m);
        result.ProviderYieldConvention.Should().Be("retained-provider-convention");
        var rawDirty = fixture.Face + (sign < 0 ? fixture.Minor / 2m : 0m);
        var reference = (await Service(fixture.Bond).CalculateAsync(new(Id, Context(), fixture.Face,
            (rawDirty - sign * fixture.Minor / 2m) * 100m / fixture.Face)))!;
        result.Yield.Should().Be(reference.Yield, "yield calibration continues to use unrounded dirty economics");
    }

    [Fact]
    public async Task CleanQuoteKeepsItsExistingComponentRoundingPolicy()
    {
        var fixture = DirtyRoundingFixture("USD", 1, cleanQuote: true);
        var result = (await fixture.Service.CalculateAsync(fixture.Request with { ObservedDirtyCash = 100.02m }))!;
        result.CleanConsideration.Should().Be(100m);
        result.AccruedInterest.Should().Be(.01m);
        result.DirtyConsideration.Should().Be(100.01m);
        result.OriginalDirtyConsideration.Should().Be(100m);
        result.TotalCash.Should().Be(100.02m);
        result.SettlementDifference.Should().Be(0m);
    }

    [Fact]
    public async Task ModeledDirtyCashAtAnotherSettlementIsRoundedInsteadOfTheOriginalQuote()
    {
        var fixture = DirtyRoundingFixture("USD", 1, curves: Curves());
        var request = fixture.Request with
        { Context = Context() with { SettlementDate = new(2026, 2, 1) }, ObservedDirtyCash = null };
        var result = (await fixture.Service.CalculateAsync(request))!;
        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().NotBeNull();
        var modeledDirty = decimal.Round(result.PresentValue!.Value, 2, MidpointRounding.AwayFromZero);
        result.OriginalDirtyConsideration.Should().Be(100m);
        modeledDirty.Should().NotBe(100m, "the retained spread carries the quote to the new settlement");
        result.DirtyConsideration.Should().Be(modeledDirty);
        result.CleanConsideration.Should().Be(modeledDirty - result.AccruedInterest);
        var confirmed = (await fixture.Service.CalculateAsync(request with { ObservedDirtyCash = modeledDirty + .01m }))!;
        confirmed.SettlementDifference.Should().Be(0m);
        confirmed.Issues.Should().NotContain(issue => issue.Code == "ConfirmationDifference");
        confirmed.Yield.Should().Be(result.Yield);
        confirmed.OriginalDirtyConsideration.Should().Be(100m);
    }

    [Fact]
    public async Task UnavailableModeledCarryDoesNotFallBackToOriginalDirtyCash()
    {
        var fixture = DirtyRoundingFixture("USD", 1);
        var result = (await fixture.Service.CalculateAsync(fixture.Request with
        { Context = Context() with { SettlementDate = new(2026, 2, 1) } }))!;
        result.IsBlocked.Should().BeFalse();
        result.OriginalDirtyConsideration.Should().Be(100m);
        result.DirtyConsideration.Should().BeNull();
        result.CleanConsideration.Should().BeNull();
        result.TotalCash.Should().BeNull();
    }

    [Theory]
    [InlineData(BondWorkflowModelVersions.Legacy)]
    [InlineData(BondWorkflowModelVersions.Previous)]
    public async Task HistoricalDirtyQuoteRoundingRetainsLiteralOldCashAndExactReplay(string model)
    {
        var fixture = DirtyRoundingFixture("USD", 1);
        var retained = (await fixture.Service.CalculateAsync(fixture.Request with { ModelVersion = model }))!;
        retained.DirtyConsideration.Should().Be(100.01m);
        retained.CleanConsideration.Should().Be(100m);
        retained.AccruedInterest.Should().Be(.01m);
        retained.OriginalDirtyConsideration.Should().Be(100m);
        retained.TotalCash.Should().Be(100.02m);
        retained.SettlementDifference.Should().Be(-.01m);
        var json = JsonSerializer.Serialize(retained);
        JsonSerializer.Serialize(await fixture.Service.ReplayAsync(JsonSerializer.Deserialize<BondWorkflowResult>(json)!))
            .Should().Be(json);
    }

    private static (BondWorkflowService Service, BondWorkflowRequest Request, SecurityDetailDto Bond, decimal Face, decimal Minor)
        DirtyRoundingFixture(string currency, int sign, bool cleanQuote = false, ISecurityCurveStore? curves = null)
    {
        var minor = currency switch { "JPY" => 1m, "KWD" => .001m, _ => .01m };
        var face = currency == "JPY" ? 1000m : 100m;
        var rawAccrued = sign * minor / 2m;
        // One 30/360 day earns exactly half the native minor unit at this annual percentage.
        var bond = WithExerciseTerms(Bond(rawAccrued * 36_000m / face), terms => terms["issueDate"] = "2025-12-31") with
        { Currency = currency, EffectiveFrom = AsOf.AddDays(-1) };
        var rawDirty = face + (sign < 0 ? minor / 2m : 0m);
        var quotedCash = cleanQuote ? rawDirty - rawAccrued : rawDirty;
        var quote = Quote(AsOf);
        quote = quote with
        {
            Input = quote.Input with
            {
                Currency = currency,
                Value = quotedCash * 100m / face,
                Quote = quote.Input.Quote! with
                {
                    PriceBasis = cleanQuote ? BondQuotePriceBasis.Clean : BondQuotePriceBasis.Dirty,
                    SettlementBasis = "regular-way",
                    ProviderYield = .012345m,
                    ProviderYieldConvention = "retained-provider-convention"
                }
            }
        };
        var quotes = Substitute.For<IBondWorkflowQuoteSource>();
        quotes.GetAsync(quote.ObservationId, Arg.Any<CancellationToken>()).Returns(quote);
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        var receipt = Guid.Parse("6e4f1f73-b1bf-48d4-9fd3-51fdfb684c20");
        pricing.GetGoldenCopySelectionAsync(Id, null, receipt, Arg.Any<CancellationToken>()).Returns(
            new SecurityPriceGoldenCopyDto(Id, quote.Input.Value!.Value, SecurityPriceKind.MarketGoldenCopy,
                "provider-one", AsOf, false, 0, [], SecurityPriceUnit.PercentOfPar, EvaluatedAsOf: AsOf,
                KnowledgeAsOf: AsOf, SelectionReceiptId: receipt)
            {
                Quotation = new(cleanQuote ? SecurityPriceBasis.Clean : SecurityPriceBasis.Dirty,
                SecurityQuotationConvention.Per100Face, SecurityQuoteStatus.Mid, new(2026, 1, 1), "regular-way")
            });
        return (Service(bond, curves, quotes, pricing), new(Id, Context(), face, Fees: minor / 2m)
        {
            QuoteObservationId = quote.ObservationId,
            PriceSelectionReceiptId = receipt,
            ObservedDirtyCash = face + (sign < 0 ? minor : 0m) + minor
        }, bond, face, minor);
    }
}
