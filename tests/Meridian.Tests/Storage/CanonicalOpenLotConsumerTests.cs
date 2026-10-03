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

    internal static JournalEntry DisposalJournal(LedgerTaxLotRecord lot)
    {
        var id = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        return new JournalEntry(id, time, "Canonical disposal", [
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.Cash, 350m, 0m, "Canonical disposal", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, lot.Account, 0m, 300m, "Canonical disposal", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.RealizedGain, 0m, 50m, "Canonical disposal", dimensions)]);
    }
}
