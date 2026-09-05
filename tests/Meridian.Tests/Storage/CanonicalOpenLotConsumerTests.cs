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
        lot = lot with { Acquisition = lot.Acquisition! with
        {
            QuantityBasis = LotQuantityBasis.Face,
            FaceValueTerms = new(100m, 0.9m, BondAmortizationMethod.ConstantYield, 0.05m)
        } };
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
    public void DurableRelief_AverageCostRequiresGovernedSurvivingBasisRedistribution()
    {
        var lot = DurableLot(1);
        var act = () => CanonicalOpenLotDisposalGuard.Validate([lot], [Selection(lot, 2m)], LedgerTaxLotReliefMethod.AverageCost, "USD");
        act.Should().Throw<LedgerValidationException>().WithMessage("*supported discrete relief policy*");
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
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.Cash, 350m, 0m, "Proceeds", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, lot.Account, 0m, 300m, "Functional acquisition basis", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, time, LedgerAccounts.RealizedGain, 0m, 50m, "Realized gain", dimensions)]);
    }
}
