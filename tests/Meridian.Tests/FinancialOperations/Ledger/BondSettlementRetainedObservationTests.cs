using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondSettlementRetainedObservationTests
{
    private static readonly Guid SecurityId = Guid.Parse("a736a5a4-5ae1-4c6b-9705-394b97496383");
    private static readonly Guid QuoteReceiptId = Guid.Parse("bdec9036-f9f5-435d-bd13-7d8305af5ba0");
    private static readonly DateTimeOffset AsOf = new(2030, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly SettlementDate = new(2030, 4, 15);

    [Theory]
    [InlineData(SecurityPriceBasis.Clean, 100, "account-a")]
    [InlineData(SecurityPriceBasis.Dirty, 101.25, "account-a")]
    [InlineData(SecurityPriceBasis.Clean, 100, null)]
    public async Task QuoteDerivedSettlement_ReplaysTheSameSelectorAndAccountScope(
        SecurityPriceBasis basis, double price, string? accountId)
    {
        var (service, pricing, quote) = Service(basis, (decimal)price, accountId);
        var prepared = await service.PrepareAsync(Request(accountId));

        prepared.Blockers.Should().BeEmpty();
        var receipt = prepared.Settlement!;
        receipt.SelectionReceiptId.Should().Be(QuoteReceiptId);
        receipt.PriceAccountId.Should().Be(accountId);
        receipt.CleanConsideration.Should().Be(100_000m);
        receipt.PurchasedAccruedInterest.Should().Be(1_250m);
        receipt.DirtyConsideration.Should().Be(101_250m, "the dirty quote includes purchased accrued interest exactly once");
        var priceInput = receipt.Inputs.Single(input => input.Kind == "PriceSelectionReceipt");
        priceInput.VersionId.Should().Be(QuoteReceiptId);
        priceInput.ContentSha256.Should().Be(Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(quote)));

        // Exercise the representation used after retaining and reloading the settlement receipt.
        var restored = JsonSerializer.Deserialize<BondSettlementConsiderationDto>(JsonSerializer.Serialize(receipt))!;
        (await service.VerifyAsync(restored)).Should().BeEmpty();
        var acquisition = new AssetAcquisitionLotDto(Guid.NewGuid(), "synthetic-bond-lot", SettlementDate,
            1_000m, 100m, "Assets/Bonds", 100_000m, 1m, 100m, BondAmortizationMethod.NoAmortization,
            SettlementConsideration: restored);
        var verifyForPosting = () => BondSettlementPostingGuard.RequireVerifiedAsync(acquisition, service, default);
        await verifyForPosting.Should().NotThrowAsync();
        await pricing.Received(3).GetGoldenCopySelectionAsync(SecurityId, accountId, QuoteReceiptId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuoteDerivedSettlement_CannotReplayUnderAnotherPricingAccount()
    {
        var (service, pricing, _) = Service(SecurityPriceBasis.Clean, 100m, "account-a");
        var receipt = (await service.PrepareAsync(Request("account-a"))).Settlement!;

        var blockers = await service.VerifyAsync(receipt with { PriceAccountId = "account-b" });

        blockers.Should().ContainSingle().Which.Code.Should().Be("PriceReceiptUnavailable");
        await pricing.Received(1).GetGoldenCopySelectionAsync(SecurityId, "account-b", QuoteReceiptId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuoteDerivedSettlement_RequiresSelectorAgreementWithTheRetainedInputManifest()
    {
        var (service, pricing, _) = Service(SecurityPriceBasis.Clean, 100m, "account-a");
        var receipt = (await service.PrepareAsync(Request("account-a"))).Settlement!;
        pricing.ClearReceivedCalls();

        var stripped = await service.VerifyAsync(receipt with { SelectionReceiptId = null, PriceAccountId = null });
        var replaced = await service.VerifyAsync(receipt with { SelectionReceiptId = Guid.NewGuid() });

        stripped.Should().ContainSingle().Which.Code.Should().Be("SettlementNotReconciled");
        replaced.Should().ContainSingle().Which.Code.Should().Be("SettlementNotReconciled");
        await pricing.DidNotReceiveWithAnyArgs().GetGoldenCopySelectionAsync(default, default, default, default);
    }

    private static BondSettlementConsiderationRequest Request(string? accountId)
        => new(new BondAnalyticsRequest(SecurityId, new(AsOf, SettlementDate))
        {
            SelectionReceiptId = QuoteReceiptId,
            PriceAccountId = accountId
        }, 100_000m, 1m, 0m, 101_250m, "cash-source", "Assets/Cash", "Assets/PurchasedInterest");

    private static (BondSettlementConsiderationService Service, ISecurityMasterPricingService Pricing,
        SecurityPriceGoldenCopyDto Quote) Service(SecurityPriceBasis basis, decimal price, string? accountId)
    {
        var security = new SecurityDetailDto(SecurityId, "Bond", SecurityStatusDto.Active,
            "Synthetic settlement bond", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                issueDate = "2030-01-15",
                maturity = "2032-01-15",
                couponType = "Fixed",
                couponRate = 5m,
                dayCount = "30/360",
                par = 100m,
                paymentFrequency = "SemiAnnual",
                scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
            }), [], [], 7, AsOf, null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var pricing = Substitute.For<ISecurityMasterPricingService>();
        var quote = new SecurityPriceGoldenCopyDto(SecurityId, price, SecurityPriceKind.Raw, "synthetic-dealer",
            AsOf, false, 0, [], SecurityPriceUnit.PercentOfPar, AsOf, AsOf.AddDays(-1), AsOf.AddDays(1), QuoteReceiptId,
            new(SecurityId, accountId, [new(1, "synthetic-dealer", "Synthetic dealer", 1)], AsOf.AddDays(-1), "operator"))
        {
            Quotation = new(basis, SecurityQuotationConvention.Per100Face, SecurityQuoteStatus.Bid,
                SettlementDate, "Synthetic contractual settlement")
        };
        pricing.GetGoldenCopySelectionAsync(SecurityId, accountId, QuoteReceiptId, Arg.Any<CancellationToken>()).Returns(quote);
        return (new(new BondAnalyticsService(query, pricing: pricing)), pricing, quote);
    }
}
