using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class CanonicalOpenLotConsumerTests
{
    [Theory]
    [InlineData(LedgerTaxLotReliefMethod.Fifo, 1)]
    [InlineData(LedgerTaxLotReliefMethod.Lifo, 2)]
    [InlineData(LedgerTaxLotReliefMethod.Hifo, 2)]
    [InlineData(LedgerTaxLotReliefMethod.SpecificId, 2)]
    public void DurableRelief_UsesCanonicalDecimalAcquisitionFxAndPolicy(LedgerTaxLotReliefMethod method, int selectedDay)
    {
        var first = DurableLot(1, 100m, 1.1m);
        var second = DurableLot(2, 90m, 1.4m);
        var selected = selectedDay == 1 ? first : second;
        var selection = Selection(selected, 2.75m);
        var result = CanonicalOpenLotDisposalGuard.Validate([first, second], [selection], method, "USD");
        result.Quantity.Should().Be(2.75m);
        result.FunctionalCostBasis.Should().Be(selection.ExpectedCostBasis);
        result.TransactionCostBasis.Should().Be(2.75m * (selectedDay == 1 ? 100m : 90m));
        // HIFO follows functional basis (126 > 110), not today's FX or transaction price (90 < 100).
        result.Selections.Should().ContainSingle().Which.TaxLotRecordId.Should().Be(selected.TaxLotRecordId);
    }

    [Fact]
    public void DurableRelief_MissingEvidenceBlocksUntilRetainedAcquisitionIsRestored()
    {
        var lot = DurableLot(1);
        var legacy = lot with { Acquisition = null };
        var act = () => CanonicalOpenLotDisposalGuard.Validate([legacy], [Selection(lot, 2.5m)], LedgerTaxLotReliefMethod.Fifo, "USD");
        act.Should().Throw<LedgerValidationException>().WithMessage("*backfill exception*reviewed acquisition evidence*");
        CanonicalOpenLotDisposalGuard.Validate([lot], [Selection(lot, 2.5m)], LedgerTaxLotReliefMethod.Fifo, "USD")
            .FunctionalCostBasis.Should().Be(300m);
    }

    [Fact]
    public void DurableRelief_RefusesUnselectedUnresolvedLotAndMixedPositionScope()
    {
        var first = DurableLot(1);
        var second = DurableLot(2) with { Acquisition = null };
        var act = () => CanonicalOpenLotDisposalGuard.Validate([first, second], [Selection(first, 1m)], LedgerTaxLotReliefMethod.Fifo, "USD");
        act.Should().Throw<LedgerValidationException>();
        second = DurableLot(2) with { BookPositionId = Guid.NewGuid() };
        act.Should().Throw<LedgerValidationException>().WithMessage("*scope*");
    }

    [Fact]
    public void DurableRelief_FaceQuantityConservesFunctionalAndTransactionBasis()
    {
        var lot = DurableLot(1) with { OriginalFace = 1000m, BookedFactor = 0.9m, ParBasis = 100m };
        lot = lot with
        {
            Acquisition = lot.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
            }
        };
        var result = CanonicalOpenLotDisposalGuard.Validate([lot], [Selection(lot, 2.5m)], LedgerTaxLotReliefMethod.Fifo, "USD");
        result.Quantity.Should().Be(250m);
        result.TransactionCostBasis.Should().Be(250m);
        result.FunctionalCostBasis.Should().Be(300m);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scope")]
    [InlineData("basis")]
    public void Reporting_UnresolvedCanonicalHistoryBlocksAndRestoredEvidenceRecovers(string fault)
    {
        var lot = DurableLot(1);
        var canonical = lot.ToOpenLot();
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, canonical);
        var broken = fault switch
        {
            "missing" => history with { CanonicalLots = null },
            "scope" => history with { CanonicalLots = [canonical with { SecurityId = Guid.NewGuid() }] },
            _ => history with { Lots = [history.Lots[0] with { CostBasis = 301m }] }
        };
        var act = () => CanonicalDisposalHistoryProjector.Project(broken, journal, lot.LedgerBookId, "USD");
        act.Should().Throw<LedgerValidationException>();
        var restored = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");
        restored.CostBasis.Should().Be(300m);
        restored.RecognizedGainOrLoss.Should().Be(50m);
        restored.CanonicalOpenLots.Should().ContainSingle().Which.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.2m);
        restored.Selections.Sum(static selection => selection.QuantityRelieved).Should().Be(2.5m);
    }

    [Fact]
    public void Reporting_FrozenLegacyDisposalPreservesParcelAmountsEvenWhenCurrentAllocatorSucceeds()
    {
        // Frozen legacy economics with exact-cent canonical bases: five shares sold at .014
        // produced .07 proceeds and .01 recognized gain. The final-residual allocator assigned
        // .03 to lot 5; a successful new projection would instead move .01 from lot 5 to lot 4.
        var lots = new[]
        {
            OpenLotConvergenceTests.Lot(1, 1m, 0.01m, 1m),
            OpenLotConvergenceTests.Lot(2, 1m, 0.01m, 1m),
            OpenLotConvergenceTests.Lot(3, 1m, 0.01m, 1m),
            OpenLotConvergenceTests.Lot(4, 1m, 0.01m, 1m),
            OpenLotConvergenceTests.Lot(5, 1m, 0.02m, 1m),
        };
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var time = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lots[0].SecurityId)
        {
            PositionId = lots[0].BookPositionId
        };
        const string description = "Frozen legacy disposal";
        var journal = new JournalEntry(id, time, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.Cash, 0.07m, 0m, description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, lots[0].Account, 0m, 0.06m, description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.RealizedGain, 0m, 0.01m, description, dimensions),
        ]);
        var history = new LedgerTaxLotDisposalHistoryRecord(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            id, lots[0].Account, LedgerTaxLotReliefMethod.Fifo,
            lots.Select(static lot => new LedgerTaxLotDisposalHistoryLot(
                lot.LotId, lot.AcquiredDate, lot.AcquiredDate, 1m, lot.UnitCost, lot.UnitCost)).ToArray(),
            [], 0m, lots.Select(static lot => lot.ToOpenLot()).ToArray());

        var rebuilt = CanonicalDisposalHistoryProjector.Project(history, journal, lots[0].LedgerBookId, "USD");

        rebuilt.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(0.01m, 0.01m, 0.01m, 0.01m, 0.03m);
        rebuilt.Selections.Select(static selection => selection.RealizedGainOrLoss)
            .Should().Equal(0m, 0m, 0m, 0m, 0.01m);
        rebuilt.Proceeds.Should().Be(0.07m);
        rebuilt.CostBasis.Should().Be(0.06m);
        rebuilt.RecognizedGainOrLoss.Should().Be(0.01m);
        rebuilt.IsBalanced.Should().BeTrue();
        rebuilt.CanonicalOpenLots.Should().HaveCount(5);

        // This guards against a try-current-then-fallback implementation: it would succeed but
        // silently change which retained lot reported the gain.
        var current = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            lots[0].Account, new DateOnly(2026, 3, 1), 5m, 0.014m, LedgerTaxLotReliefMethod.Fifo,
            lots.Select(static lot => new LedgerTaxLot(lot.LotId, lot.AcquiredDate, 1m, lot.UnitCost)).ToArray()));
        current.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(0.01m, 0.01m, 0.01m, 0.02m, 0.02m);

        var versioned = CanonicalDisposalHistoryProjector.Project(history with
        {
            ProceedsAllocationVersion = LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion,
            SalePrice = 0.014m
        }, journal, lots[0].LedgerBookId, "USD");
        versioned.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(current.Selections.Select(static selection => selection.Proceeds));
        versioned.Selections.Select(static selection => selection.RealizedGainOrLoss)
            .Should().Equal(current.Selections.Select(static selection => selection.RealizedGainOrLoss));
    }

    [Fact]
    public void Reporting_VersionedFaceDisposalConvertsRetainedPriceWithQuantity()
    {
        var lot = DurableLot(1) with { OriginalFace = 1000m, BookedFactor = 0.9m, ParBasis = 100m };
        lot = lot with
        {
            Acquisition = lot.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
            }
        };
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, lot.ToOpenLot()) with
        {
            ProceedsAllocationVersion = LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion,
            SalePrice = 140m
        };

        var rebuilt = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        rebuilt.Input.QuantitySold.Should().Be(250m);
        rebuilt.Input.SalePrice.Should().Be(1.4m);
        rebuilt.Proceeds.Should().Be(350m);
        rebuilt.CostBasis.Should().Be(300m);
        rebuilt.RecognizedGainOrLoss.Should().Be(50m);
    }

    [Fact]
    public void DurableRelief_AverageCostCertifiesThePooledBasis()
    {
        // Functional bases 1,100 and 1,400 pool to 125 per unit; the lot's own 110 is refused.
        var first = DurableLot(1, 100m, 1.1m);
        var second = DurableLot(2, 100m, 1.4m);
        var discrete = () => CanonicalOpenLotDisposalGuard.Validate([first, second], [Selection(first, 2m)],
            LedgerTaxLotReliefMethod.AverageCost, "USD");
        discrete.Should().Throw<LedgerValidationException>().WithMessage("*AverageCost*functional-basis relief plan*");
        var pooled = CanonicalOpenLotDisposalGuard.Validate([first, second],
            [Selection(first, 2m) with { ExpectedCostBasis = 250m }], LedgerTaxLotReliefMethod.AverageCost, "USD");
        pooled.FunctionalCostBasis.Should().Be(250m);
        pooled.Selections.Should().ContainSingle().Which.TaxLotRecordId.Should().Be(first.TaxLotRecordId);
    }

    [Theory]
    [InlineData(LedgerTaxLotReliefMethod.Fifo, false)]
    [InlineData(LedgerTaxLotReliefMethod.Fifo, true)]
    [InlineData(LedgerTaxLotReliefMethod.Lifo, false)]
    [InlineData(LedgerTaxLotReliefMethod.Lifo, true)]
    [InlineData(LedgerTaxLotReliefMethod.Hifo, false)]
    [InlineData(LedgerTaxLotReliefMethod.Hifo, true)]
    [InlineData(LedgerTaxLotReliefMethod.SpecificId, false)]
    [InlineData(LedgerTaxLotReliefMethod.SpecificId, true)]
    [InlineData(LedgerTaxLotReliefMethod.AverageCost, false)]
    [InlineData(LedgerTaxLotReliefMethod.AverageCost, true)]
    public void Reporting_AdjustedCurrentBasisPreservesAcquisitionFacts(
        LedgerTaxLotReliefMethod method, bool fullyDisposed)
    {
        var lot = DurableLot(1) with
        {
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                10m, 1_400m, 1_750m)
        };
        var canonical = lot.ToOpenLot();
        var quantity = fullyDisposed ? 10m : 2.5m;
        var relievedBasis = fullyDisposed ? canonical.OpenFunctionalCostBasis
            : canonical.OpenFunctionalCostBasis * quantity / canonical.OpenQuantity;
        var journal = DisposalJournal(lot, relievedBasis);
        var history = History(lot, journal, canonical) with
        {
            ReliefMethod = method,
            Lots = [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, quantity, lot.UnitCost, relievedBasis)],
            PoolLots = method == LedgerTaxLotReliefMethod.AverageCost ? [canonical] : null
        };

        var projection = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        projection.CostBasis.Should().Be(relievedBasis);
        var selection = projection.Selections.Should().ContainSingle().Which;
        selection.UnitCost.Should().Be(175m);
        selection.CostBasis.Should().Be(relievedBasis);
        selection.Proceeds.Should().Be(relievedBasis + 50m);
        projection.RecognizedGainOrLoss.Should().Be(50m);
        projection.IsBalanced.Should().BeTrue();
        projection.CanonicalOpenLots.Should().ContainSingle().Which.Acquisition.Should().BeSameAs(lot.Acquisition);
        projection.CanonicalOpenLots[0].Acquisition.FunctionalCostBasis.Should().Be(1_200m);
        history.Lots[0].UnitCost.Should().Be(120m, "the mutation preserves its acquisition unit cost");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reporting_FractionalCentCurrentBasisReproducesExactJournal(bool fullyDisposed)
    {
        var adjustedBasis = 10m / 3m;
        var lot = DurableLot(1) with
        {
            OpenQuantity = 3m,
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                3m, 3m, adjustedBasis)
        };
        var canonical = lot.ToOpenLot();
        var quantity = fullyDisposed ? 3m : 1m;
        var basis = fullyDisposed ? adjustedBasis : adjustedBasis * quantity / 3m;
        var journal = DisposalJournal(lot, basis);
        var history = History(lot, journal, canonical) with
        {
            Lots = [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, quantity, lot.UnitCost, basis)]
        };

        var projection = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        projection.CostBasis.Should().Be(basis);
        projection.Selections.Sum(static selection => selection.CostBasis).Should().Be(basis);
        projection.Selections.Sum(static selection => selection.Proceeds).Should().Be(basis + 50m);
        projection.Selections.Sum(static selection => selection.RealizedGainOrLoss).Should().Be(50m);
        projection.Selections[0].UnitCost.Should().Be(basis / quantity);
        projection.Lines.Should().Equal(journal.Lines.Select(static line => (line.Account, line.Debit, line.Credit)));
        projection.CanonicalOpenLots[0].Acquisition.FunctionalCostBasis.Should().Be(1_200m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Reporting_RepeatingAverageCostSlicesPreserveExactBasisAndPooledResultSign(int recognized)
    {
        var first = DurableLot(1) with
        {
            OpenQuantity = 1m,
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution, 1m, 1m, 1m)
        };
        var second = DurableLot(2) with
        {
            OpenQuantity = 2m,
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution, 2m, 1m, 1m)
        };
        var pool = new[] { first.ToOpenLot(), second.ToOpenLot() };
        var relief = new OpenLotReliefService().Select(pool, 3m, OpenLotReliefMethod.AverageCost);
        var journal = DisposalJournal(first, 2m, recognized);
        var history = History(first, journal, pool[0]) with
        {
            ReliefMethod = LedgerTaxLotReliefMethod.AverageCost,
            Lots = relief.Selections.Select((slice, index) => new LedgerTaxLotDisposalHistoryLot(
                pool[index].LotId, pool[index].AcquiredDate, pool[index].AcquiredDate,
                slice.Quantity, first.UnitCost, slice.FunctionalCostBasis)).ToArray(),
            CanonicalLots = pool,
            PoolLots = pool
        };

        var projection = CanonicalDisposalHistoryProjector.Project(history, journal, first.LedgerBookId, "USD");

        projection.Selections.Select(static selection => selection.CostBasis)
            .Should().Equal(relief.Selections.Select(static slice => slice.FunctionalCostBasis));
        projection.CostBasis.Should().Be(2m);
        projection.Selections.Sum(static selection => selection.Proceeds).Should().Be(2m + recognized);
        projection.Selections.Sum(static selection => selection.RealizedGainOrLoss).Should().Be(recognized);
        projection.Selections.Should().OnlyContain(selection => recognized > 0
            ? selection.RealizedGainOrLoss >= 0m : selection.RealizedGainOrLoss <= 0m);
        projection.RecognizedGainOrLoss.Should().Be(recognized);
        projection.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Reporting_AverageCostCanonicalSnapshotMustMatchRetainedPool()
    {
        var lot = DurableLot(1);
        var canonical = lot.ToOpenLot();
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, canonical) with
        {
            ReliefMethod = LedgerTaxLotReliefMethod.AverageCost,
            PoolLots = [canonical],
            CanonicalLots = [canonical with { OpenFunctionalCostBasis = 1_300m }]
        };

        var act = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        act.Should().Throw<LedgerValidationException>().WithMessage("*canonical lot basis*pool snapshot*");
        // Separate deserialization creates distinct acquisition/evidence objects with the same
        // retained primitive facts. That copy remains compatible with the authoritative pool.
        var repaired = history with
        {
            CanonicalLots = [canonical with
            {
                Acquisition = canonical.Acquisition with
                {
                    Evidence = canonical.Acquisition.Evidence.Select(static evidence => evidence with { }).ToArray()
                }
            }]
        };
        CanonicalDisposalHistoryProjector.Project(repaired, journal, lot.LedgerBookId, "USD")
            .CostBasis.Should().Be(300m);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("uri")]
    [InlineData("hash")]
    [InlineData("source-system")]
    [InlineData("source-reference")]
    [InlineData("review-status")]
    [InlineData("reviewer")]
    [InlineData("reviewed-at")]
    [InlineData("effective-date")]
    [InlineData("version")]
    [InlineData("retained-at")]
    [InlineData("retained-by")]
    [InlineData("subject-type")]
    [InlineData("subject-id")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("null")]
    public void Reporting_AverageCostRejectsChangedAcquisitionEvidenceWithUnchangedFacts(string fault)
    {
        var lot = DurableLot(1);
        var canonical = lot.ToOpenLot();
        var journal = DisposalJournal(lot);
        var evidence = canonical.Acquisition.Evidence[0];
        var changed = fault switch
        {
            "id" => evidence with { EvidenceId = "different-evidence" },
            "uri" => evidence with { EvidenceUri = "evidence://different/source" },
            "hash" => evidence with { ContentHashSha256 = new string('b', 64) },
            "source-system" => evidence with { SourceSystem = "different-custodian" },
            "source-reference" => evidence with { SourceReference = "different-reference" },
            "review-status" => evidence with { ReviewStatus = "Pending" },
            "reviewer" => evidence with { ReviewedBy = "different-reviewer" },
            "reviewed-at" => evidence with { ReviewedAtUtc = evidence.ReviewedAtUtc.AddMinutes(1) },
            "effective-date" => evidence with { EffectiveDate = evidence.EffectiveDate.AddDays(1) },
            "version" => evidence with { EvidenceVersion = evidence.EvidenceVersion + 1 },
            "retained-at" => evidence with { RetainedAtUtc = evidence.RetainedAtUtc.AddMinutes(1) },
            "retained-by" => evidence with { RetainedBy = "different-retainer" },
            "subject-type" => evidence with { SubjectType = "DifferentSubject" },
            "subject-id" => evidence with { SubjectId = Guid.NewGuid().ToString("D") },
            _ => evidence with { }
        };
        var altered = canonical with
        {
            Acquisition = canonical.Acquisition with
            {
                Evidence = fault switch
                {
                    "missing" => [],
                    "extra" => [evidence, evidence with { EvidenceId = "additional-evidence" }],
                    "null" => null!,
                    _ => [changed]
                }
            }
        };
        var history = History(lot, journal, altered) with
        {
            ReliefMethod = LedgerTaxLotReliefMethod.AverageCost,
            PoolLots = [canonical]
        };

        var act = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        act.Should().Throw<LedgerValidationException>().WithMessage("*acquisition*pool snapshot*");
    }

    [Fact]
    public void Reporting_AcquisitionPricedReliefAfterRedistributionIsRejectedEvenWhenJournalMatches()
    {
        var lot = DurableLot(1) with
        {
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                10m, 1_400m, 1_750m)
        };
        // Both retained history and journal claim the old acquisition-priced 300, while the
        // current canonical basis authorizes 437.5. Journal agreement cannot certify that drift.
        var journal = DisposalJournal(lot);
        var history = History(lot, journal, lot.ToOpenLot());

        var act = () => CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        act.Should().Throw<LedgerValidationException>().WithMessage("*basis*canonical lot evidence*");
    }

    [Fact]
    public void Reporting_AdjustedFaceBasisReportsCostPerDeclaredFaceQuantity()
    {
        var lot = DurableLot(1) with
        {
            OriginalFace = 1_000m,
            BookedFactor = 0.9m,
            ParBasis = 100m,
            BasisAdjustment = new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                10m, 1_400m, 1_750m)
        };
        lot = lot with
        {
            Acquisition = lot.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
            }
        };
        var journal = DisposalJournal(lot, 437.5m);
        var history = History(lot, journal, lot.ToOpenLot()) with
        {
            Lots = [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, 2.5m, lot.UnitCost, 437.5m)]
        };

        var projection = CanonicalDisposalHistoryProjector.Project(history, journal, lot.LedgerBookId, "USD");

        var selection = projection.Selections.Should().ContainSingle().Which;
        selection.QuantityRelieved.Should().Be(250m);
        selection.CostBasis.Should().Be(437.5m);
        selection.UnitCost.Should().Be(1.75m);
        projection.CanonicalOpenLots[0].Acquisition.Should().BeSameAs(lot.Acquisition);
    }

    internal static LedgerTaxLotRecord DurableLot(int day, decimal price = 100m, decimal fx = 1.2m)
    {
        var lot = OpenLotConvergenceTests.Lot(day, 10m, price, fx);
        return lot with { Currency = "USD", UnitCost = price * fx };
    }

    internal static LedgerTaxLotDisposalHistoryRecord History(LedgerTaxLotRecord lot, JournalEntry journal, OpenLotDto canonical)
        => new(Guid.NewGuid(), journal.JournalEntryId, lot.Account, LedgerTaxLotReliefMethod.Fifo,
            [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, 2.5m, lot.UnitCost, 300m)], [], 0m, [canonical]);

    private static LedgerTaxLotDisposalSelection Selection(LedgerTaxLotRecord lot, decimal quantity)
        => new(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity, quantity, 0, "selection-proof",
            lot.UnitCost, lot.UnitCost * quantity);

    internal static JournalEntry DisposalJournal(LedgerTaxLotRecord lot, decimal basis = 300m, decimal recognized = 50m)
    {
        var id = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        return new JournalEntry(id, time, "Canonical disposal", [
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.Cash, basis + recognized, 0m, "Canonical disposal", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, lot.Account, 0m, basis, "Canonical disposal", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, recognized < 0m ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedGain,
                recognized < 0m ? -recognized : 0m, recognized > 0m ? recognized : 0m, "Canonical disposal", dimensions)]);
    }
}
