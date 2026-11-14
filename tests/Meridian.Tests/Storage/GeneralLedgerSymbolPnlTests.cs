using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class GeneralLedgerSymbolPnlTests
{
    [Fact]
    public void GoldenCase_RealizedDisposalAndApprovedUnrealizedReconcileToIndependentArithmetic()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var row = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot, marks).Should().ContainSingle().Which;
        // Sold 2.5 units: proceeds 350 less retained basis 300 = 50.
        row.RealizedAvailability.Should().Be("AvailableForRetainedDisposals");
        decimal.Parse(row.Proceeds!).Should().Be(350m);
        decimal.Parse(row.CostBasis!).Should().Be(300m);
        decimal.Parse(row.BookRealizedGainLoss!).Should().Be(50m);
        // Remaining 7.5 units: approved 125 per unit less historical basis 900 = 37.5.
        row.UnrealizedAvailability.Should().Be("Available");
        decimal.Parse(row.UnrealizedGainLoss!).Should().Be(37.5m);
    }

    [Fact]
    public void MissingOrUnapprovedMarksAndIncompleteBasisNeverBecomeZeroUnrealized()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var missing = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot);
        var unapproved = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot,
            marks with { ClosePrices = marks.ClosePrices with { Approval = null } });
        var badBasis = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot with
        {
            OpenPositions = snapshot.OpenPositions! with { Blockers = ["Missing historical basis"] }
        }, marks);
        foreach (var row in new[] { missing.Single(), unapproved.Single(), badBasis.Single() })
        {
            row.UnrealizedAvailability.Should().Be("Unavailable");
            row.UnrealizedGainLoss.Should().BeNull();
            row.UnrealizedReason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void MissingCanonicalDisposalEvidenceIsUnavailableRatherThanZeroRealized()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var missing = snapshot with
        {
            TaxLotDisposalHistory = [snapshot.TaxLotDisposalHistory[0] with { CanonicalLots = null }]
        };
        var row = GeneralLedgerSymbolPnlBuilder.Build(book, missing, marks).Single();
        row.RealizedAvailability.Should().Be("MissingEvidence");
        row.BookRealizedGainLoss.Should().BeNull();
        row.CostBasis.Should().BeNull();
        row.UnrealizedGainLoss.Should().NotBeNull();
    }

    [Fact]
    public void EmptyCanonicalPopulationDoesNotTurnNonzeroJournalSecuritiesIntoZeroUnrealized()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var missing = snapshot with { OpenPositions = snapshot.OpenPositions! with { Lots = [] } };
        var row = GeneralLedgerSymbolPnlBuilder.Build(book, missing, marks).Single();
        row.UnrealizedAvailability.Should().Be("Unavailable");
        row.UnrealizedGainLoss.Should().BeNull();
        row.UnrealizedReason.Should().Contain("No captured canonical open lot");
    }

    [Fact]
    public void RetainedCanonicalClosureCanEstablishZeroUnrealized()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var closedLot = snapshot.OpenPositions!.Lots[0] with { OpenQuantity = 0m };
        var closed = snapshot with { OpenPositions = snapshot.OpenPositions with { Lots = [], ClosedLots = [closedLot] } };
        var row = GeneralLedgerSymbolPnlBuilder.Build(book, closed, marks).Single();
        row.UnrealizedAvailability.Should().Be("Available");
        row.UnrealizedGainLoss.Should().Be("0");
        row.UnrealizedReason.Should().Contain("establish closure");
    }

    [Fact]
    public void ChangedMarkEvidenceChangesBoundExportSnapshot()
    {
        var (book, snapshot, marks) = CreateGoldenCase();
        var first = GeneralLedgerExportBuilder.BuildDataset(book, snapshot, marks);
        var changed = GeneralLedgerExportBuilder.BuildDataset(book, snapshot, marks with
        {
            Securities = [marks.Securities[0] with { Version = marks.Securities[0].Version + 1 }]
        });
        first.SnapshotSha256.Should().NotBe(changed.SnapshotSha256);
    }

    internal static (LedgerBookRecord Book, LedgerReportingSnapshot Snapshot, GeneralLedgerUnrealizedInputs Marks) CreateGoldenCase()
    {
        var (baseBook, baseSnapshot) = GeneralLedgerExportTestData.Create();
        var lot = CanonicalOpenLotConsumerTests.DurableLot(1);
        lot = lot with
        {
            Acquisition = lot.Acquisition! with
            {
                AcquisitionCurrency = "USD",
                AcquisitionFxRateToFunctional = 1m,
                TransactionCostBasis = lot.Acquisition.FunctionalCostBasis
            }
        };
        var book = baseBook with { LedgerBookId = lot.LedgerBookId };
        var period = baseSnapshot.Period! with
        {
            LedgerBookId = book.LedgerBookId,
            PeriodNo = 7,
            StartDate = new(2026, 7, 1),
            EndDate = new(2026, 7, 31)
        };
        var journal = CanonicalOpenLotConsumerTests.DisposalJournal(lot);
        var disposal = CanonicalOpenLotConsumerTests.History(lot, journal, lot.ToOpenLot());
        var snapshot = new LedgerReportingSnapshot([new(journal, Guid.NewGuid(), period.PeriodId, null, null, 2, journal.Timestamp)],
            [disposal], period)
        {
            OpenPositions = new(period.EndDate, [lot with { OpenQuantity = 7.5m }], [])
        };
        return (book, snapshot, CreateApprovedMarks(book, lot, period.EndDate, 125m));
    }

    internal static GeneralLedgerUnrealizedInputs CreateApprovedMarks(LedgerBookRecord book, LedgerTaxLotRecord lot,
        DateOnly valuationDate, decimal closePrice)
    {
        var evaluated = new DateTimeOffset(valuationDate.ToDateTime(new TimeOnly(20, 0)), TimeSpan.Zero);
        var account = book.FundStructureNodeId.ToString("D");
        var hierarchy = new SecurityPricingHierarchyDto(lot.SecurityId, account,
            [new(1, "synthetic-source", "Synthetic source", 1)], evaluated.AddDays(-1), "reviewer");
        var selection = new SecurityPriceGoldenCopyDto(lot.SecurityId, closePrice, SecurityPriceKind.MarketGoldenCopy,
            "synthetic-source", evaluated, false, 0, [], SecurityPriceUnit.CurrencyPerUnit,
            evaluated, hierarchy.AsOf, evaluated, Guid.NewGuid(), hierarchy);
        var close = new SecurityClosePriceSnapshotDto(Guid.NewGuid(), account, book.LedgerBookId.ToString("D"),
            valuationDate, 1, null, null, evaluated, evaluated, 1m, "preparer", evaluated,
            [new(lot.SecurityId, selection, [new("synthetic-source", closePrice, evaluated, SecurityPriceUnit.CurrencyPerUnit)],
                null, closePrice, SecurityPriceUnit.CurrencyPerUnit)], []);
        var approved = new SecurityClosePriceSetDto(close, new(close.SnapshotId, SecurityClosePriceEvidence.Digest(close),
            "independent-checker", "Reviewed synthetic mark", evaluated.AddHours(1)));
        var detail = new SecurityDetailDto(lot.SecurityId, "Equity", SecurityStatusDto.Active, "Synthetic equity", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], 1, evaluated.AddYears(-1), null);
        return new(approved, [detail]);
    }
}
