using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// The activation surface for the wash-sale engine: the dated policy that gates it, the scope that
/// decides which accounts a replacement can come from, and the rebuild of relief projections from
/// retained disposal history that lets a governed report pack carry real realized-gain rows.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerWashSaleActivationTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");

    private static LedgerAccount Account => LedgerAccounts.Securities("AAPL", "broker-1");

    // -------------------------------------------------------------------------
    // Policy gating
    // -------------------------------------------------------------------------

    [Fact]
    public void Policy_DefaultsToLedgerBookScope()
    {
        // A repurchase in a sibling custody account is still a wash sale, so the default scope has
        // to be the book rather than the single disposing account.
        WashSalePolicy.UnitedStates.Scope.Should().Be(WashSaleReplacementScope.LedgerBook);
        WashSalePolicy.UnitedStates.WindowDays.Should().Be(30);
    }

    [Fact]
    public void Policy_WithoutEffectiveDate_AppliesToEverySale()
    {
        WashSalePolicy.UnitedStates.AppliesOn(new DateOnly(2020, 1, 1)).Should().BeTrue();
        WashSalePolicy.Disabled.AppliesOn(new DateOnly(2026, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Policy_BeforeEffectiveDate_DoesNotApply()
    {
        var dated = WashSalePolicy.UnitedStates with { EffectiveDate = new DateOnly(2026, 1, 1) };

        dated.AppliesOn(new DateOnly(2025, 12, 31)).Should().BeFalse();
        dated.AppliesOn(new DateOnly(2026, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void Projection_BeforePolicyEffectiveDate_RecognizesFullLoss()
    {
        // Enabling deferral must not restate periods that were already closed and reported.
        var projection = LedgerTaxLotReliefProjector.Project(LossSaleInput(
            WashSalePolicy.UnitedStates with { EffectiveDate = new DateOnly(2026, 6, 1) }));

        projection.WashSale.Should().BeNull();
        projection.RealizedGainOrLoss.Should().Be(-2_000m);
        projection.RecognizedGainOrLoss.Should().Be(-2_000m);
    }

    [Fact]
    public void Projection_OnOrAfterPolicyEffectiveDate_DefersLoss()
    {
        var projection = LedgerTaxLotReliefProjector.Project(LossSaleInput(
            WashSalePolicy.UnitedStates with { EffectiveDate = new DateOnly(2026, 3, 1) }));

        projection.WashSale.Should().NotBeNull();
        projection.DisallowedWashSaleLoss.Should().Be(2_000m);
        projection.RecognizedGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void Policy_RejectsNegativeWindow()
    {
        var act = () => new WashSalePolicy(true, -1).EnsureValid();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BasisIncrease_CarriesTheAccountThatAbsorbedTheLoss()
    {
        // Under a book-wide scope the absorbing account is frequently not the one that sold, so the
        // deferral has to name it or the trace back to the replacement lot is ambiguous.
        var replacementAccount = LedgerAccounts.Securities("AAPL", "broker-2");
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 80m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [
                new WashSaleReplacementAcquisition(
                    "lot-rep",
                    new DateOnly(2026, 3, 5),
                    100m,
                    SecurityId,
                    replacementAccount),
            ]));

        var increase = projection.WashSale!.BasisIncreases.Single();
        increase.ReplacementAccount.Should().Be(replacementAccount);
        increase.HoldingPeriodCarryDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void CarryDate_ChainsThroughAnAlreadyExtendedLot()
    {
        // Selling a lot that itself absorbed an earlier wash sale must pass the older holding-period
        // start along, not reset it to that lot's own purchase date.
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 80m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot(
                    "lot-a",
                    new DateOnly(2026, 1, 1),
                    100m,
                    100m,
                    SecurityId,
                    holdingPeriodStartDate: new DateOnly(2023, 5, 4)),
            ],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 100m, SecurityId),
            ]));

        projection.WashSale!.BasisIncreases.Single()
            .HoldingPeriodCarryDate.Should().Be(new DateOnly(2023, 5, 4));
    }

    // -------------------------------------------------------------------------
    // Rebuilding projections from retained disposal history
    // -------------------------------------------------------------------------

    [Fact]
    public void HistoryProjector_RecoversProceedsFromBookedGainAndBasis()
    {
        // Sale price is not retained; it is recovered as basis + recognized + deferred, which keeps
        // rebuilt rows tied to the journal rather than to a separately-stored price.
        var projection = LedgerTaxLotReliefHistoryProjector.Project(History(
            recognizedGainOrLoss: -2_000m));

        projection.Should().NotBeNull();
        projection!.Proceeds.Should().Be(8_000m);
        projection.CostBasis.Should().Be(10_000m);
        projection.RealizedGainOrLoss.Should().Be(-2_000m);
        projection.Selections.Single().TaxCharacter.Should().Be(TaxCharacter.LongTerm);
    }

    [Fact]
    public void HistoryProjector_ReattachesTheDeferralThatWasActuallyBooked()
    {
        // A fully-deferred loss books no loss line, so recognized is zero; adding the retained
        // deferral back is what recovers the true economic proceeds and loss.
        var projection = LedgerTaxLotReliefHistoryProjector.Project(History(
            recognizedGainOrLoss: 0m,
            increases:
            [
                new WashSaleBasisIncrease("lot-rep", 2_000m, new DateOnly(2024, 1, 1)),
            ],
            matchedReplacementQuantity: 100m));

        projection.Should().NotBeNull();
        projection!.Proceeds.Should().Be(8_000m);
        projection.RealizedGainOrLoss.Should().Be(-2_000m);
        projection.DisallowedWashSaleLoss.Should().Be(2_000m);
        projection.WashSale!.AllowedLoss.Should().Be(0m);
        projection.WashSale.MatchedReplacementQuantity.Should().Be(100m);
        projection.RecognizedGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void HistoryProjector_ReturnsNullRatherThanThrowingOnUnreconstructableHistory()
    {
        // One bad historical row must not stop a governed pack from being produced.
        LedgerTaxLotReliefHistoryProjector
            .Project(History() with { Lots = Array.Empty<LedgerTaxLotDisposalHistoryLot>() })
            .Should().BeNull();

        // Recognized loss larger than the basis would imply negative proceeds.
        LedgerTaxLotReliefHistoryProjector.Project(History(recognizedGainOrLoss: -20_000m))
            .Should().BeNull();
    }

    [Fact]
    public void HistoryProjector_SplitsRebuiltRowsByCharacter()
    {
        var projection = LedgerTaxLotReliefHistoryProjector.Project(new LedgerTaxLotDisposalHistory(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Account,
            new DateOnly(2026, 3, 1),
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLotDisposalHistoryLot(
                    "lot-old", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1), 100m, 100m, 10_000m),
                new LedgerTaxLotDisposalHistoryLot(
                    "lot-new", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 100m, 100m, 10_000m),
            ],
            RecognizedGainOrLoss: 4_000m,
            WashSaleBasisIncreases: [],
            MatchedReplacementQuantity: 0m));

        projection.Should().NotBeNull();
        projection!.Selections.Should().HaveCount(2);
        projection.Selections.Single(selection => selection.Lot.LotId == "lot-old")
            .TaxCharacter.Should().Be(TaxCharacter.LongTerm);
        projection.Selections.Single(selection => selection.Lot.LotId == "lot-new")
            .TaxCharacter.Should().Be(TaxCharacter.ShortTerm);
        (projection.ShortTermRealizedGainOrLoss + projection.LongTermRealizedGainOrLoss)
            .Should().Be(projection.RealizedGainOrLoss);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static LedgerTaxLotReliefInput LossSaleInput(WashSalePolicy policy) =>
        new(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 80m, // 100 @ cost 100 sold at 80 -> 2,000 loss
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            washSalePolicy: policy,
            replacementAcquisitions:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 100m, SecurityId),
            ]);

    private static LedgerTaxLotDisposalHistory History(
        decimal recognizedGainOrLoss = -2_000m,
        IReadOnlyList<WashSaleBasisIncrease>? increases = null,
        decimal matchedReplacementQuantity = 0m) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Account,
            new DateOnly(2026, 3, 1),
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLotDisposalHistoryLot(
                    "lot-a",
                    new DateOnly(2024, 1, 1),
                    new DateOnly(2024, 1, 1),
                    100m,
                    100m,
                    10_000m),
            ],
            recognizedGainOrLoss,
            increases ?? [],
            matchedReplacementQuantity);
}
