using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Unit tests for the average-cost relief method and wash-sale loss deferral added to
/// <see cref="LedgerTaxLotReliefProjector"/>. Wash-sale mechanics follow US IRC §1091: a realized
/// loss coinciding with a substantially-identical purchase within the policy window is disallowed
/// in proportion to the replacement quantity and capitalized into the replacement lot's basis.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerTaxLotReliefWashSaleTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
    private static readonly Guid OtherSecurityId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000020");

    private static LedgerAccount Account => LedgerAccounts.Securities("AAPL", "broker-1");

    // -------------------------------------------------------------------------
    // Average cost
    // -------------------------------------------------------------------------

    [Fact]
    public void AverageCost_PoolsLotsIntoSingleAverageBasis()
    {
        // Two lots: 100 @ 100 and 100 @ 140 -> pooled average 120.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.AverageCost,
            [
                new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId),
                new LedgerTaxLot("lot-b", new DateOnly(2026, 2, 1), 100m, 140m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.IsBalanced.Should().BeTrue();
        projection.CostBasis.Should().Be(12_000m); // 100 shares @ pooled 120
        projection.Proceeds.Should().Be(15_000m);
        projection.RealizedGainOrLoss.Should().Be(3_000m);
    }

    [Fact]
    public void AverageCost_DepletesOldestLotFirstForDeterministicClosing()
    {
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.AverageCost,
            [
                new LedgerTaxLot("lot-new", new DateOnly(2026, 2, 1), 100m, 140m, SecurityId),
                new LedgerTaxLot("lot-old", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        // Cost basis reflects the pooled average regardless of which lot closes...
        projection.CostBasis.Should().Be(12_000m);
        // ...but the oldest lot is the one relieved (holding-period determinism).
        projection.Selections.Should().ContainSingle();
        projection.Selections[0].Lot.LotId.Should().Be("lot-old");
    }

    [Fact]
    public void AverageCost_ResolvesThroughAccountPolicy()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook(LedgerTaxLotReliefMethod.AverageCost);
        var resolved = policyBook.Resolve(Account, new DateOnly(2026, 3, 1));
        resolved.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.AverageCost);
    }

    [Fact]
    public void AverageCost_ReportsPooledUnitCostAcrossLots()
    {
        // Sell 150 across two lots (100@100 and 100@140, pooled average 120). Every relieved slice
        // must report the pooled unit cost — not the individual lot cost — so realized-gain report
        // rows stay internally consistent: QuantityRelieved * UnitCost == CostBasis.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 150m,
            salePrice: 130m,
            LedgerTaxLotReliefMethod.AverageCost,
            [
                new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId),
                new LedgerTaxLot("lot-b", new DateOnly(2026, 2, 1), 100m, 140m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.Selections.Should().HaveCount(2);
        projection.Selections.Should().OnlyContain(selection => selection.UnitCost == 120m);
        projection.Selections.Should().OnlyContain(selection =>
            selection.QuantityRelieved * selection.UnitCost == selection.CostBasis);
        projection.CostBasis.Should().Be(18_000m); // 150 shares @ pooled 120
    }

    // -------------------------------------------------------------------------
    // Wash sale — no deferral paths (behaviour preservation)
    // -------------------------------------------------------------------------

    [Fact]
    public void WashSale_DisabledByDefault_RecognizesFullLoss()
    {
        var input = LossSaleInput(); // no wash-sale policy supplied

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale.Should().BeNull();
        projection.RealizedGainOrLoss.Should().Be(-2_000m);
        RealizedLossLine(projection).Should().Be(2_000m); // full loss recognized
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_OnGain_DoesNotApply()
    {
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m, // gain
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 100m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale.Should().BeNull();
        projection.RealizedGainOrLoss.Should().Be(5_000m);
    }

    [Fact]
    public void WashSale_ReplacementOutsideWindow_DoesNotApply()
    {
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                // 31 days after the sale — outside the ±30-day window.
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 1).AddDays(31), 100m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale.Should().BeNull();
        RealizedLossLine(projection).Should().Be(2_000m);
    }

    [Fact]
    public void WashSale_DifferentSecurity_DoesNotApply()
    {
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 100m, OtherSecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Wash sale — deferral
    // -------------------------------------------------------------------------

    [Fact]
    public void WashSale_FullReplacement_DefersEntireLoss()
    {
        // Sell 100 at a 2,000 loss; buy 100 replacement 4 days later -> whole loss disallowed.
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 100m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale.Should().NotBeNull();
        projection.WashSale!.DisallowedLoss.Should().Be(2_000m);
        projection.WashSale.AllowedLoss.Should().Be(0m);
        projection.RealizedGainOrLoss.Should().Be(-2_000m); // economic loss still reported
        RealizedLossLine(projection).Should().Be(0m); // but none recognized on the ledger
        projection.IsBalanced.Should().BeTrue();

        projection.WashSale.BasisIncreases.Should().ContainSingle();
        projection.WashSale.BasisIncreases[0].ReplacementLotId.Should().Be("lot-rep");
        projection.WashSale.BasisIncreases[0].Amount.Should().Be(2_000m);
        projection.WashSale.BasisIncreases[0].HoldingPeriodCarryDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void WashSale_PartialReplacement_DefersProportionalLoss()
    {
        // Sell 100 at a 2,000 loss; buy only 40 replacement -> 40% (800) disallowed, 1,200 allowed.
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 40m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.DisallowedLoss.Should().Be(800m);
        projection.WashSale.AllowedLoss.Should().Be(1_200m);
        projection.WashSale.MatchedReplacementQuantity.Should().Be(40m);
        RealizedLossLine(projection).Should().Be(1_200m);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_ReplacementExceedingSale_CapsDisallowanceAtLossQuantity()
    {
        // Buy 250 replacement against a 100-share sale -> disallowance capped at the full loss.
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 250m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.MatchedReplacementQuantity.Should().Be(100m);
        projection.WashSale.DisallowedLoss.Should().Be(2_000m);
    }

    [Fact]
    public void WashSale_MultipleReplacementLots_DistributesBasisAndSumsExactly()
    {
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep-1", new DateOnly(2026, 3, 5), 30m, SecurityId),
                new WashSaleReplacementAcquisition("lot-rep-2", new DateOnly(2026, 3, 6), 30m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        // 60 of 100 replaced -> 60% of 2,000 = 1,200 disallowed, split across two lots.
        projection.WashSale!.DisallowedLoss.Should().Be(1_200m);
        projection.WashSale.BasisIncreases.Should().HaveCount(2);
        projection.WashSale.BasisIncreases.Sum(increase => increase.Amount).Should().Be(1_200m);
    }

    [Fact]
    public void WashSale_ReplacementLotsBeyondSoldQuantity_LoadLossOnlyOnMatchedShares()
    {
        // Sell 100 shares; two 100-share replacement lots. A wash sale matches share-for-share, so
        // only the earliest 100 replacement shares carry the fully-disallowed loss — the later lot
        // must not have its basis touched.
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-early", new DateOnly(2026, 3, 4), 100m, SecurityId),
                new WashSaleReplacementAcquisition("lot-late", new DateOnly(2026, 3, 6), 100m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var washSale = projection.WashSale!;
        washSale.DisallowedLoss.Should().Be(2_000m); // replacement >= sold -> whole loss disallowed
        washSale.BasisIncreases.Should().ContainSingle();
        washSale.BasisIncreases[0].ReplacementLotId.Should().Be("lot-early");
        washSale.BasisIncreases[0].Amount.Should().Be(2_000m);
    }

    [Fact]
    public void WashSale_UnevenReplacementSplit_KeepsBasisIncreasesNonNegativeAndExact()
    {
        // Three replacement lots with quantities that force rounding when distributing the
        // disallowed loss: every basis increase must stay non-negative and the parts must sum
        // exactly to the disallowed amount.
        var input = LossSaleInput(
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacements:
            [
                new WashSaleReplacementAcquisition("lot-rep-1", new DateOnly(2026, 3, 5), 33m, SecurityId),
                new WashSaleReplacementAcquisition("lot-rep-2", new DateOnly(2026, 3, 6), 33m, SecurityId),
                new WashSaleReplacementAcquisition("lot-rep-3", new DateOnly(2026, 3, 7), 34m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var washSale = projection.WashSale!;
        washSale.BasisIncreases.Should().OnlyContain(increase => increase.Amount >= 0m);
        washSale.BasisIncreases.Sum(increase => increase.Amount).Should().Be(washSale.DisallowedLoss);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static LedgerTaxLotReliefInput LossSaleInput(
        WashSalePolicy? washSalePolicy = null,
        IReadOnlyList<WashSaleReplacementAcquisition>? replacements = null) =>
        new(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 80m, // 100 @ cost 100 sold at 80 -> 2,000 loss
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            washSalePolicy: washSalePolicy,
            replacementAcquisitions: replacements);

    private static decimal RealizedLossLine(LedgerTaxLotReliefProjection projection) =>
        projection.Lines
            .Where(line => line.account.Name == LedgerAccounts.RealizedLoss.Name)
            .Sum(line => line.debit);
}
