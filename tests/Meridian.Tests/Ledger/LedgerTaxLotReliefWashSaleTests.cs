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

    [Fact]
    public void AverageCost_MultiSliceRoundingResidual_TotalTiesToRoundedPooledBasis()
    {
        // 3 shares pooled at 100/3 each; selling 2 across two lots. Rounding each slice independently
        // would give 33.33 + 33.33 = 66.66, but the rounded pooled basis for 2 shares is 66.67 — the
        // rounding residual is carried onto the final slice so the total ties.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 2m,
            salePrice: 40m,
            LedgerTaxLotReliefMethod.AverageCost,
            [
                new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 1m, 100m / 3m, SecurityId),
                new LedgerTaxLot("lot-b", new DateOnly(2026, 1, 2), 1m, 100m / 3m, SecurityId),
                new LedgerTaxLot("lot-c", new DateOnly(2026, 1, 3), 1m, 100m / 3m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.Selections.Should().HaveCount(2);
        projection.CostBasis.Should().Be(66.67m); // rounded pooled basis for 2 shares, not 66.66
        projection.Selections.Select(selection => selection.CostBasis).Should().Contain([33.33m, 33.34m]);
    }

    [Fact]
    public void AverageCost_SingleShareFromUnevenPool_TiesUnitCostToRoundedBasis()
    {
        // A 3-share pool at 100/3 per share has a repeating average; a 1-share sale rounds the basis
        // to currency precision, and the reported unit cost must tie so QuantityRelieved * UnitCost
        // equals the reported CostBasis rather than exposing the raw repeating decimal.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 1m,
            salePrice: 40m,
            LedgerTaxLotReliefMethod.AverageCost,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 3m, 100m / 3m, SecurityId)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var selection = projection.Selections.Single();
        (selection.QuantityRelieved * selection.UnitCost).Should().Be(selection.CostBasis);
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

    [Theory]
    [InlineData(40, 200, 600)]
    [InlineData(60, 0, 400)]
    [InlineData(80, -200, 200)]
    public void WashSale_MixedDisposal_DefersLossLotRegardlessOfAggregateResult(
        decimal gainLotUnitCost,
        decimal expectedEconomicResult,
        decimal expectedRecognizedResult)
    {
        // The earlier gain lot cannot consume the replacement shares or supply their carry date.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 20m,
            salePrice: 100m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("lot-gain", new DateOnly(2024, 1, 1), 10m, gainLotUnitCost, SecurityId),
                new LedgerTaxLot("lot-loss", new DateOnly(2026, 1, 1), 10m, 140m, SecurityId,
                    holdingPeriodStartDate: new DateOnly(2025, 5, 4)),
            ],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 10m, SecurityId)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.RealizedGainOrLoss.Should().Be(expectedEconomicResult);
        projection.WashSale.Should().NotBeNull();
        projection.WashSale!.DisallowedLoss.Should().Be(400m);
        projection.WashSale.AllowedLoss.Should().Be(0m);
        projection.WashSale.MatchedReplacementQuantity.Should().Be(10m);
        projection.WashSale.BasisIncreases.Should().ContainSingle()
            .Which.HoldingPeriodCarryDate.Should().Be(new DateOnly(2025, 5, 4));
        var source = projection.WashSale.BasisIncreases.Single().SourceAllocations.Should().ContainSingle().Which;
        source.Source.Should().Be(projection.Selections.Single(selection => selection.Lot.LotId == "lot-loss"));
        source.Quantity.Should().Be(10m);
        source.Amount.Should().Be(400m);
        projection.RecognizedGainOrLoss.Should().Be(expectedRecognizedResult);
        RealizedGainLine(projection).Should().Be(expectedRecognizedResult);
        RealizedLossLine(projection).Should().Be(0m);
        projection.IsBalanced.Should().BeTrue();
    }

    [Theory]
    [InlineData(10, 200, 1000, 10)]
    [InlineData(20, 400, 800, 20)]
    [InlineData(30, 800, 400, 30)]
    [InlineData(40, 1200, 0, 40)]
    [InlineData(60, 1200, 0, 40)]
    public void WashSale_MultipleLossLots_ConsumeReplacementCapacityOnceInReliefOrder(
        decimal replacementQuantity,
        decimal expectedDeferred,
        decimal expectedAllowed,
        decimal expectedMatchedQuantity)
    {
        // The first 20 shares lose 20 each and the next 20 lose 40 each. Reusing the same
        // replacement capacity for both loss lots, or averaging their losses, changes the result.
        var input = MultipleLossLotsInput(
            [new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), replacementQuantity, SecurityId)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var washSale = projection.WashSale!;
        washSale.DisallowedLoss.Should().Be(expectedDeferred);
        washSale.AllowedLoss.Should().Be(expectedAllowed);
        washSale.MatchedReplacementQuantity.Should().Be(expectedMatchedQuantity);
        washSale.BasisIncreases.Sum(increase => increase.Amount).Should().Be(expectedDeferred);
        washSale.BasisIncreases.SelectMany(increase => increase.SourceAllocations)
            .Sum(allocation => allocation.Quantity).Should().Be(expectedMatchedQuantity);
        projection.RealizedGainOrLoss.Should().Be(-1_200m);
        projection.RecognizedGainOrLoss.Should().Be(-expectedAllowed);
        RealizedLossLine(projection).Should().Be(expectedAllowed);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_MultipleLossLots_RepeatedAndReorderedInputsProduceIdenticalAllocations()
    {
        // Equal acquisition dates deliberately exercise the lot-ID tie breaker. The first
        // replacement spans two losses; the last replacement must receive only its unused tail.
        WashSaleReplacementAcquisition[] replacements =
        [
            new("rep-b", new DateOnly(2026, 3, 5), 10m, SecurityId),
            new("rep-a", new DateOnly(2026, 3, 5), 25m, SecurityId),
            new("rep-c", new DateOnly(2026, 3, 6), 10m, SecurityId),
        ];
        var input = MultipleLossLotsInput(replacements);
        var reorderedInput = new LedgerTaxLotReliefInput(
            input.Account, input.SaleDate, input.QuantitySold, input.SalePrice, input.ReliefMethod,
            input.OpenLots.Reverse().ToArray(),
            washSalePolicy: input.WashSalePolicy,
            replacementAcquisitions: replacements.Reverse().ToArray());

        var first = LedgerTaxLotReliefProjector.Project(input);
        var repeated = LedgerTaxLotReliefProjector.Project(input);
        var reordered = LedgerTaxLotReliefProjector.Project(reorderedInput);

        repeated.WashSale.Should().BeEquivalentTo(first.WashSale, options => options.WithStrictOrdering());
        reordered.WashSale.Should().BeEquivalentTo(first.WashSale, options => options.WithStrictOrdering());
        repeated.Lines.Should().Equal(first.Lines);
        reordered.Lines.Should().Equal(first.Lines);
        first.WashSale!.MatchedReplacementQuantity.Should().Be(40m);
        first.WashSale.DisallowedLoss.Should().Be(1_200m);
        first.WashSale.BasisIncreases.Where(increase => increase.ReplacementLotId == "rep-a")
            .Sum(increase => increase.Amount).Should().Be(600m);
        first.WashSale.BasisIncreases.Where(increase => increase.ReplacementLotId == "rep-b")
            .Sum(increase => increase.Amount).Should().Be(400m);
        first.WashSale.BasisIncreases.Where(increase => increase.ReplacementLotId == "rep-c")
            .Sum(increase => increase.Amount).Should().Be(200m);
        foreach (var replacement in replacements)
        {
            first.WashSale.BasisIncreases.Where(increase => increase.ReplacementLotId == replacement.LotId)
                .SelectMany(increase => increase.SourceAllocations)
                .Sum(allocation => allocation.Quantity).Should().BeLessThanOrEqualTo(replacement.Quantity);
        }
        input.ReplacementAcquisitions.Select(replacement => replacement.Quantity).Should().Equal(10m, 25m, 10m);
        first.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_MultipleLossLots_RoundingConservesEachLossAndTotalBasis()
    {
        // Each three-share source loses two cents. Five replacements fully match the first loss
        // and match two-thirds of the second, which rounds to one cent. Zero-cent slices must
        // still consume replacement quantity so that they cannot be reused by the second loss.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 6m,
            salePrice: 1m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("loss-a", new DateOnly(2026, 1, 1), 3m, 3.02m / 3m, SecurityId),
                new LedgerTaxLot("loss-b", new DateOnly(2026, 1, 2), 3m, 3.02m / 3m, SecurityId),
            ],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions: Enumerable.Range(1, 5)
                .Select(index => new WashSaleReplacementAcquisition($"rep-{index}", new DateOnly(2026, 3, 5), 1m, SecurityId))
                .ToArray());

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var washSale = projection.WashSale!;
        washSale.MatchedReplacementQuantity.Should().Be(5m);
        washSale.DisallowedLoss.Should().Be(0.03m);
        washSale.AllowedLoss.Should().Be(0.01m);
        washSale.BasisIncreases.Should().OnlyContain(increase => increase.Amount >= 0m);
        washSale.BasisIncreases.Sum(increase => increase.Amount).Should().Be(0.03m);
        washSale.BasisIncreases.SelectMany(increase => increase.SourceAllocations)
            .Sum(allocation => allocation.Quantity).Should().Be(5m);
        foreach (var increase in washSale.BasisIncreases)
            increase.SourceAllocations.Sum(allocation => allocation.Amount).Should().Be(increase.Amount);
        washSale.BasisIncreases.Where(increase => increase.HoldingPeriodCarryDate == new DateOnly(2026, 1, 1))
            .Sum(increase => increase.Amount).Should().Be(0.02m);
        washSale.BasisIncreases.Where(increase => increase.HoldingPeriodCarryDate == new DateOnly(2026, 1, 2))
            .Sum(increase => increase.Amount).Should().Be(0.01m);
        (washSale.DisallowedLoss + washSale.AllowedLoss).Should().Be(0.04m);
        projection.Selections.Sum(selection => selection.QuantityRelieved).Should().Be(input.QuantitySold);
        projection.Selections.Sum(selection => selection.CostBasis).Should().Be(projection.CostBasis);
        RealizedLossLine(projection).Should().Be(0.01m);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_MultipleLossLots_RetainsSourceSelectionsAndGoverningPolicyVersion()
    {
        var policy = WashSalePolicy.UnitedStates with { PolicyId = "policy-v7" };
        var input = MultipleLossLotsInput(
            [new("lot-rep", new DateOnly(2026, 3, 5), 30m, SecurityId, Account)],
            policy);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var increase = projection.WashSale!.BasisIncreases.Should().ContainSingle().Which;
        increase.AppliedPolicy.Should().Be(policy);
        increase.AppliedPolicy!.PolicyId.Should().Be("policy-v7");
        increase.ReplacementAccount.Should().Be(Account);
        increase.SourceAllocations.Should().HaveCount(2);
        increase.SourceAllocations.Select(allocation => allocation.Source).Should().Equal(projection.Selections);
        increase.SourceAllocations.Select(allocation => allocation.Quantity).Should().Equal(20m, 10m);
        increase.SourceAllocations.Select(allocation => allocation.Amount).Should().Equal(400m, 400m);
        increase.SourceAllocations.Sum(allocation => allocation.Amount).Should().Be(increase.Amount);
    }

    [Theory]
    [InlineData(WashSaleReplacementScope.DisposingAccount, 10, 200)]
    [InlineData(WashSaleReplacementScope.LedgerBook, 40, 1200)]
    public void WashSale_MultipleLossLots_HonorsConfiguredAccountScope(
        WashSaleReplacementScope scope,
        decimal expectedQuantity,
        decimal expectedDeferred)
    {
        var siblingAccount = LedgerAccounts.Securities("AAPL", "broker-2");
        var input = MultipleLossLotsInput(
            [
                new("same-account", new DateOnly(2026, 3, 4), 10m, SecurityId, Account),
                new("other-account", new DateOnly(2026, 3, 5), 30m, SecurityId, siblingAccount),
            ],
            WashSalePolicy.UnitedStates with { Scope = scope });

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.MatchedReplacementQuantity.Should().Be(expectedQuantity);
        projection.WashSale.DisallowedLoss.Should().Be(expectedDeferred);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_RelievedLotsCannotReplaceThemselves()
    {
        var input = MultipleLossLotsInput(
            [
                new("loss-a", new DateOnly(2026, 2, 20), 100m, SecurityId, Account),
                new("loss-b", new DateOnly(2026, 2, 21), 100m, SecurityId),
                new("eligible", new DateOnly(2026, 3, 5), 10m, SecurityId, Account),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.MatchedReplacementQuantity.Should().Be(10m);
        projection.WashSale.DisallowedLoss.Should().Be(200m);
        projection.WashSale.BasisIncreases.Should().ContainSingle()
            .Which.ReplacementLotId.Should().Be("eligible");
    }

    [Fact]
    public void WashSale_RepeatedReplacementCandidateCountsItsQuantityOnlyOnce()
    {
        var replacement = new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 15m, SecurityId, Account);
        var input = MultipleLossLotsInput([replacement, replacement]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.MatchedReplacementQuantity.Should().Be(15m);
        projection.WashSale.DisallowedLoss.Should().Be(300m);
        projection.WashSale.BasisIncreases.Should().ContainSingle();
        projection.WashSale.BasisIncreases.SelectMany(increase => increase.SourceAllocations)
            .Sum(allocation => allocation.Quantity).Should().Be(15m);
    }

    [Fact]
    public void WashSale_ConflictingReplacementCandidatesRejectAmbiguousCapacity()
    {
        var replacement = new WashSaleReplacementAcquisition("lot-rep", new DateOnly(2026, 3, 5), 15m, SecurityId, Account);
        var input = MultipleLossLotsInput([replacement, replacement with { Quantity = 20m }]);

        var act = () => LedgerTaxLotReliefProjector.Project(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WashSale_MultipleLossLots_PreservesConfiguredWindowAndCandidateFilters()
    {
        var saleDate = new DateOnly(2026, 3, 1);
        var policy = new WashSalePolicy(true, WindowDays: 5, EffectiveDate: saleDate);
        var input = MultipleLossLotsInput(
            [
                new("before-window", saleDate.AddDays(-6), 100m, SecurityId),
                new("at-lower-bound", saleDate.AddDays(-5), 10m, SecurityId),
                new("at-upper-bound", saleDate.AddDays(5), 20m, SecurityId),
                new("after-window", saleDate.AddDays(6), 100m, SecurityId),
                new("other-security", saleDate, 100m, OtherSecurityId),
                new("zero-quantity", saleDate, 0m, SecurityId),
                new("negative-quantity", saleDate, -100m, SecurityId),
            ],
            policy);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.WashSale!.DisallowedLoss.Should().Be(800m);
        projection.WashSale.MatchedReplacementQuantity.Should().Be(30m);
        projection.WashSale.BasisIncreases.Select(increase => increase.ReplacementLotId)
            .Distinct().Should().Equal("at-lower-bound", "at-upper-bound");
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_SameReplacementLotIdInDifferentAccounts_HasDistinctDeterministicCapacity()
    {
        var firstAccount = LedgerAccounts.Securities("AAPL", "broker-2");
        var secondAccount = LedgerAccounts.Securities("AAPL", "broker-3");
        WashSaleReplacementAcquisition[] replacements =
        [
            new("shared-id", new DateOnly(2026, 3, 5), 20m, SecurityId, secondAccount),
            new("shared-id", new DateOnly(2026, 3, 5), 10m, SecurityId, firstAccount),
        ];

        var projection = LedgerTaxLotReliefProjector.Project(MultipleLossLotsInput(replacements));
        var reordered = LedgerTaxLotReliefProjector.Project(MultipleLossLotsInput(replacements.Reverse().ToArray()));

        var washSale = projection.WashSale!;
        washSale.MatchedReplacementQuantity.Should().Be(30m);
        washSale.DisallowedLoss.Should().Be(800m);
        washSale.BasisIncreases.Should().HaveCount(2);
        var first = washSale.BasisIncreases.Single(increase => increase.ReplacementAccount == firstAccount);
        var second = washSale.BasisIncreases.Single(increase => increase.ReplacementAccount == secondAccount);
        first.Amount.Should().Be(200m);
        first.SourceAllocations.Sum(allocation => allocation.Quantity).Should().Be(10m);
        second.Amount.Should().Be(600m);
        second.SourceAllocations.Sum(allocation => allocation.Quantity).Should().Be(20m);
        reordered.WashSale.Should().BeEquivalentTo(washSale, options => options.WithStrictOrdering());
    }

    [Fact]
    public void WashSale_MultipleKnownSecurities_MatchesEachLossOnlyToItsOwnSecurity()
    {
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 40m,
            salePrice: 100m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("loss-a", new DateOnly(2026, 1, 1), 20m, 120m, SecurityId),
                new LedgerTaxLot("loss-b", new DateOnly(2026, 1, 2), 20m, 140m, OtherSecurityId),
            ],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [
                new("rep-b", new DateOnly(2026, 3, 4), 30m, OtherSecurityId),
                new("rep-a", new DateOnly(2026, 3, 5), 30m, SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        var washSale = projection.WashSale!;
        washSale.MatchedReplacementQuantity.Should().Be(40m);
        washSale.DisallowedLoss.Should().Be(1_200m);
        var first = washSale.BasisIncreases.Single(increase => increase.ReplacementLotId == "rep-a");
        first.Amount.Should().Be(400m);
        first.SourceAllocations.Should().ContainSingle().Which.Source.Lot.SecurityId.Should().Be(SecurityId);
        var second = washSale.BasisIncreases.Single(increase => increase.ReplacementLotId == "rep-b");
        second.Amount.Should().Be(800m);
        second.SourceAllocations.Should().ContainSingle().Which.Source.Lot.SecurityId.Should().Be(OtherSecurityId);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void WashSale_ProceedsRoundingCannotCreateLossOnZeroBasisLot()
    {
        // Independent one-cent proceeds rounding would consume three cents before the fourth
        // parcel, even though total sale proceeds are only two cents, manufacturing a final loss.
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 4m,
            salePrice: 0.006m,
            LedgerTaxLotReliefMethod.Fifo,
            Enumerable.Range(1, 4)
                .Select(index => new LedgerTaxLot($"lot-{index}", new DateOnly(2026, 1, index), 1m, 0m, SecurityId))
                .ToArray(),
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [new("lot-rep", new DateOnly(2026, 3, 5), 4m, SecurityId)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.Proceeds.Should().Be(0.02m);
        projection.Selections.Should().OnlyContain(selection => selection.Proceeds >= 0m);
        projection.Selections.Sum(selection => selection.Proceeds).Should().Be(projection.Proceeds);
        projection.WashSale.Should().BeNull();
        projection.RecognizedGainOrLoss.Should().Be(0.02m);
        projection.Lines.Should().OnlyContain(line => line.debit >= 0m && line.credit >= 0m);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void AverageCost_RoundingCannotCreateNegativeBasisOrLossOnBreakEvenSale()
    {
        var input = new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 4m,
            salePrice: 0.006m,
            LedgerTaxLotReliefMethod.AverageCost,
            Enumerable.Range(1, 4)
                .Select(index => new LedgerTaxLot($"lot-{index}", new DateOnly(2026, 1, index), 1m, 0.006m, SecurityId))
                .ToArray(),
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [new("lot-rep", new DateOnly(2026, 3, 5), 4m, SecurityId)]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.CostBasis.Should().Be(0.02m);
        projection.Selections.Should().OnlyContain(selection => selection.CostBasis >= 0m && selection.Proceeds >= 0m);
        projection.Selections.Should().OnlyContain(selection => selection.RealizedGainOrLoss == 0m);
        projection.Selections.Sum(selection => selection.CostBasis).Should().Be(projection.CostBasis);
        projection.Selections.Sum(selection => selection.Proceeds).Should().Be(projection.Proceeds);
        projection.WashSale.Should().BeNull();
        projection.IsBalanced.Should().BeTrue();
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

    private static decimal RealizedGainLine(LedgerTaxLotReliefProjection projection) =>
        projection.Lines
            .Where(line => line.account.Name == LedgerAccounts.RealizedGain.Name)
            .Sum(line => line.credit);

    private static LedgerTaxLotReliefInput MultipleLossLotsInput(
        IReadOnlyList<WashSaleReplacementAcquisition> replacements,
        WashSalePolicy? policy = null) =>
        new(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 40m,
            salePrice: 100m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("loss-a", new DateOnly(2026, 1, 1), 20m, 120m, SecurityId),
                new LedgerTaxLot("loss-b", new DateOnly(2026, 1, 2), 20m, 140m, SecurityId),
            ],
            washSalePolicy: policy ?? WashSalePolicy.UnitedStates,
            replacementAcquisitions: replacements);
}
