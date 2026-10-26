using FluentAssertions;
using Meridian.Application.SecurityMaster.Otc;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The observed-price mark: a percent-of-par golden-copy price is the value per 100 of notional
/// to the buyer, so the mark is sign × notional × price / 100; anything else is a named blocker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PricingStoreOtcContractValuationSourceTests
{
    private static readonly DateOnly Trade = new(2026, 3, 2);
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly Guid SwapId = Guid.NewGuid();

    [Fact]
    public async Task PercentOfParPrice_IsScaledByNotional_AndSignedByDirection()
    {
        var receipt = Guid.NewGuid();
        var pricing = Pricing(Price(1.25m, SecurityPriceUnit.PercentOfPar, receipt: receipt));
        var source = new PricingStoreOtcContractValuationSource(pricing);

        var bought = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m), AsOf, "USD"));
        bought.Status.Should().Be(OtcValuationStatus.Valued);
        bought.MarkToMarket.Should().Be(125_000m);
        bought.Currency.Should().Be("USD");
        bought.Method.Should().Be("otc-mtm-observed-price-v1");
        bought.Provenance.Should().Be(DataProvenance.Real);
        bought.Blockers.Should().BeEmpty();
        var input = bought.Inputs.Should().ContainSingle().Subject;
        input.Kind.Should().Be("GoldenCopyPrice");
        input.Source.Should().Be("vendor-a");
        input.Reference.Should().Be(receipt.ToString("D"), "the selection receipt is the replayable provenance");

        var sold = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Sell, 4_000_000m), AsOf, "USD"));
        sold.MarkToMarket.Should().Be(-50_000m);

        await pricing.Received(2).GetGoldenCopyPriceAsOfAsync(
            SwapId, null,
            Arg.Is<DateTimeOffset>(asOf => asOf.Offset == TimeSpan.Zero && DateOnly.FromDateTime(asOf.UtcDateTime) == AsOf && asOf.Hour == 23 && asOf.Minute == 59),
            Arg.Any<CancellationToken>(), null);
    }

    [Fact]
    public async Task KnowledgeTime_IsPassedThrough()
    {
        var knownAt = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);
        var pricing = Pricing(Price(1.25m, SecurityPriceUnit.PercentOfPar));
        var source = new PricingStoreOtcContractValuationSource(pricing);

        await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 1m), AsOf, "USD", knownAt));

        await pricing.Received(1).GetGoldenCopyPriceAsOfAsync(SwapId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), knownAt);
    }

    [Fact]
    public async Task ACurrencyPerUnitQuote_IsAUnitMismatch_AndKeepsTheObservationAsInput()
    {
        var source = new PricingStoreOtcContractValuationSource(Pricing(Price(101.5m, SecurityPriceUnit.CurrencyPerUnit)));

        var valuation = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m), AsOf, "USD"));

        valuation.Status.Should().Be(OtcValuationStatus.Unvalued);
        valuation.MarkToMarket.Should().BeNull();
        valuation.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.UnitMismatch && b.Reason.Contains("CurrencyPerUnit"));
        valuation.Inputs.Should().ContainSingle();
    }

    [Fact]
    public async Task NoPrice_IsAMissingInput()
    {
        var source = new PricingStoreOtcContractValuationSource(Pricing(null));

        var valuation = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m), AsOf, "USD"));

        valuation.Status.Should().Be(OtcValuationStatus.Unvalued);
        valuation.MarkToMarket.Should().BeNull();
        valuation.Inputs.Should().BeEmpty();
        valuation.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.MissingInput && b.Reason.Contains(SwapId.ToString("D")));
    }

    [Fact]
    public async Task AStaleFallback_IsAStaleInput()
    {
        var source = new PricingStoreOtcContractValuationSource(Pricing(Price(1.25m, SecurityPriceUnit.PercentOfPar, stale: true)));

        var valuation = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m), AsOf, "USD"));

        valuation.Status.Should().Be(OtcValuationStatus.Unvalued);
        valuation.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.StaleInput);
    }

    [Fact]
    public async Task ANotionalInAnotherCurrency_IsACurrencyMismatch_WithoutConsultingTheHierarchy()
    {
        var pricing = Pricing(Price(1.25m, SecurityPriceUnit.PercentOfPar));
        var source = new PricingStoreOtcContractValuationSource(pricing);

        var valuation = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m) with { NotionalCurrency = "EUR" }, AsOf, "USD"));

        valuation.Status.Should().Be(OtcValuationStatus.Unvalued);
        valuation.Currency.Should().Be("USD");
        valuation.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.CurrencyMismatch);
        await pricing.DidNotReceiveWithAnyArgs().GetGoldenCopyPriceAsOfAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task ASimulatedSource_IsReported_NotHidden()
    {
        var source = new PricingStoreOtcContractValuationSource(Pricing(Price(1.25m, SecurityPriceUnit.PercentOfPar, sourceId: "demo")));

        var valuation = await source.ValueAsync(new OtcContractValuationRequest(Contract(OtcContractDirection.Buy, 10_000_000m), AsOf, "USD"));

        valuation.Status.Should().Be(OtcValuationStatus.Valued);
        valuation.MarkToMarket.Should().Be(125_000m);
        valuation.Provenance.Should().Be(DataProvenance.Simulated);
        valuation.Inputs.Should().ContainSingle().Which.Provenance.Should().Be(DataProvenance.Simulated);
    }

    private static ISecurityMasterPricingService Pricing(SecurityPriceGoldenCopyDto? price)
    {
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        pricing.GetGoldenCopyPriceAsOfAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(price);
        return pricing;
    }

    private static SecurityPriceGoldenCopyDto Price(decimal price, SecurityPriceUnit unit, bool stale = false, string sourceId = "vendor-a", Guid? receipt = null)
        => new(SwapId, price, SecurityPriceKind.MarketGoldenCopy, sourceId, new DateTimeOffset(AsOf.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero), stale, stale ? 9 : 0, [], unit, null, null, null, receipt ?? Guid.NewGuid());

    private static OtcContractDto Contract(OtcContractDirection direction, decimal notional)
        => new(Guid.NewGuid(), SwapId, Guid.NewGuid(), null, direction, notional, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, null, null, 1, "casey", DateTimeOffset.UtcNow);
}
