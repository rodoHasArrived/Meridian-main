using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting.Tests;

public sealed partial class CorporateActionAdjustmentServiceTests
{
    private static readonly DateTimeOffset BasisCutoff = new(2024, 3, 2, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown, null)]
    [InlineData(HistoricalPriceBasis.Unknown, false)]
    [InlineData(HistoricalPriceBasis.Unknown, true)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted, null)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted, null)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted, null)]
    public async Task PrepareAsync_AdjustmentRequiresExplicitRawHistory(
        HistoricalPriceBasis priceBasis, bool? legacyAdjusted)
    {
        SetBasisActions(splits: true, dividends: true);
        var raw = BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 1, 2));
        var unverifiedReference = BasisBar(priceBasis, new DateOnly(2024, 2, 29), legacyAdjusted: legacyAdjusted);

        var prepare = () => _service.PrepareAsync([raw, unverifiedReference], "SPY", BasisCutoff);

        await prepare.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PriceBasis.Raw*");
    }

    [Theory]
    [InlineData(true, false, HistoricalPriceBasis.SplitAdjusted, 50)]
    [InlineData(false, true, HistoricalPriceBasis.DividendAdjusted, 99)]
    [InlineData(true, true, HistoricalPriceBasis.FullyAdjusted, 49)]
    public async Task PrepareAsync_DeclaresTheAppliedRegimeOnEveryRow(
        bool splits, bool dividends, HistoricalPriceBasis expectedBasis, int firstClose)
    {
        SetBasisActions(splits, dividends);
        var referencePrice = splits ? 50m : 100m;
        var bars = new[]
        {
            BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 1, 2)),
            BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 2, 29), referencePrice),
            BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 3, 1), referencePrice - (dividends ? 1m : 0m))
        };

        var plan = await _service.PrepareAsync(bars, "SPY", BasisCutoff);
        var adjusted = bars.Select(plan.Apply).ToArray();

        adjusted.Should().OnlyContain(bar => bar.PriceBasis == expectedBasis && bar.IsAdjusted == true);
        adjusted[0].Close.Should().Be(firstClose);
        adjusted[^1].Close.Should().Be(bars[^1].Close,
            "a factor-one row keeps its observed price but is still part of the declared adjusted history");
        var applyAgain = () => plan.Apply(adjusted[0]);
        applyAgain.Should().Throw<InvalidOperationException>().WithMessage("*PriceBasis.Raw*");
    }

    [Fact]
    public async Task PrepareAsync_OffsettingSplitsRetainSplitBasisWhenPricesAreUnchanged()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([
            BasisAction(securityId, "StockSplit", new DateOnly(2024, 2, 1), split: 2m),
            BasisAction(securityId, "ReverseStockSplit", new DateOnly(2024, 3, 1), split: 0.5m)
        ]);
        var raw = BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 1, 2));

        var plan = await _service.PrepareAsync([raw], "SPY", BasisCutoff);
        var adjusted = plan.Apply(raw);

        adjusted.Close.Should().Be(raw.Close);
        adjusted.Volume.Should().Be(raw.Volume);
        adjusted.PriceBasis.Should().Be(HistoricalPriceBasis.SplitAdjusted);
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown, false)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted, null)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted, null)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted, null)]
    public async Task Apply_RejectsNonRawPricesEvenAfterTheLastAction(
        HistoricalPriceBasis priceBasis, bool? legacyAdjusted)
    {
        SetBasisActions(splits: true, dividends: false);
        var raw = BasisBar(HistoricalPriceBasis.Raw, new DateOnly(2024, 3, 1));
        var plan = await _service.PrepareAsync([raw], "SPY", BasisCutoff);
        var otherBasis = BasisBar(priceBasis, raw.SessionDate, legacyAdjusted: legacyAdjusted);

        var apply = () => plan.Apply(otherBasis);

        apply.Should().Throw<InvalidOperationException>().WithMessage("*PriceBasis.Raw*");
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted)]
    public async Task PrepareAsync_NoActionsPreservesTheOriginalBasis(HistoricalPriceBasis priceBasis)
    {
        _mockResolver.SetResolveResult(null);
        var original = BasisBar(priceBasis, new DateOnly(2024, 1, 2));

        var plan = await _service.PrepareAsync([original], "SPY", BasisCutoff);

        plan.Apply(original).Should().BeSameAs(original);
    }

    [Fact]
    public void FromAdjustedBars_LegacyUnknownIdentityPreservesItsHistoricalDigest()
    {
        var legacy = BasisBar(HistoricalPriceBasis.Unknown, new DateOnly(2024, 1, 2), legacyAdjusted: false);
        var cutoff = new DateTimeOffset(2024, 1, 31, 23, 59, 59, TimeSpan.Zero);

        var plan = CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", cutoff, [legacy], [legacy]);

        plan.ContentVersion.Should().Be("sha256:aa5743c7c568f8c2423be11825250ebff3c4b6c38efc74a74ee5dc7379a3b9f4");
        plan.Apply(legacy).Should().BeSameAs(legacy);
    }

    [Fact]
    public void FromAdjustedBars_BasisIsPartOfTheCapturedIdentityAndDigest()
    {
        var date = new DateOnly(2024, 1, 2);
        var legacy = BasisBar(HistoricalPriceBasis.Unknown, date, legacyAdjusted: false);
        var raw = BasisBar(HistoricalPriceBasis.Raw, date);
        var legacyPlan = CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", BasisCutoff, [legacy], [legacy]);
        var rawPlan = CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", BasisCutoff, [raw], [raw]);

        rawPlan.ContentVersion.Should().NotBe(legacyPlan.ContentVersion);
        var wrongBasis = () => legacyPlan.Apply(raw);
        wrongBasis.Should().Throw<InvalidOperationException>().WithMessage("*price basis*captured adjustment plan*");

        var split = BasisBar(HistoricalPriceBasis.SplitAdjusted, date, 50m);
        var dividend = BasisBar(HistoricalPriceBasis.DividendAdjusted, date, 50m);
        var splitPlan = CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", BasisCutoff, [raw], [split]);
        var dividendPlan = CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", BasisCutoff, [raw], [dividend]);
        splitPlan.ContentVersion.Should().NotBe(dividendPlan.ContentVersion,
            "equal values and legacy IsAdjusted=true do not make split and dividend prices interchangeable");
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown)]
    [InlineData(HistoricalPriceBasis.Raw)]
    public void FromAdjustedBars_ChangedPricesRequireAnAdjustedDeclaration(HistoricalPriceBasis outputBasis)
    {
        var date = new DateOnly(2024, 1, 2);
        var original = BasisBar(HistoricalPriceBasis.Raw, date);
        var changed = BasisBar(outputBasis, date, 50m);

        var create = () => CorporateActionAdjustmentPlan.FromAdjustedBars("SPY", BasisCutoff, [original], [changed]);

        create.Should().Throw<InvalidOperationException>().WithMessage("*explicit adjusted price basis*");
    }

    private void SetBasisActions(bool splits, bool dividends)
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        var actions = new List<CorporateActionDto>();
        if (splits)
            actions.Add(BasisAction(securityId, "StockSplit", new DateOnly(2024, 2, 1), split: 2m));
        if (dividends)
            actions.Add(BasisAction(securityId, "Dividend", new DateOnly(2024, 3, 1), dividend: 1m));
        _mockQueryService.SetCorporateActions(actions);
    }

    private static HistoricalBar BasisBar(
        HistoricalPriceBasis priceBasis, DateOnly date, decimal price = 100m, bool? legacyAdjusted = null)
        => new("SPY", date, price, price, price, price, 1_000, "fixture", 7,
            IsAdjusted: legacyAdjusted, PriceBasis: priceBasis);

    private static CorporateActionDto BasisAction(
        Guid securityId, string type, DateOnly date, decimal? split = null, decimal? dividend = null)
        => new(Guid.NewGuid(), securityId, type, date, null, dividend, "USD", split,
            null, null, null, null, null, null, LifecycleState: CorporateActionLifecycleStates.Confirmed);
}
