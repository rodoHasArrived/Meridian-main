using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.Storage;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed class AmortizationReversalPreviewTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PreviewReversal_UsesRetainedOriginalEconomicsAndCurrentPositionVersion(bool premium, bool legacy)
    {
        var fixture = new Fixture(premium, legacy);

        var preview = await fixture.Service.PreviewReversalAsync(fixture.Before.LedgerBookId, fixture.BatchId);

        preview.Instruction.ExpectedLot.Should().BeEquivalentTo(fixture.After.ToOpenLot());
        preview.Instruction.ExpectedBookPositionVersion.Should().Be(fixture.Position.Version);
        preview.Instruction.Security.Should().BeSameAs(fixture.Inputs.Security);
        preview.Instruction.SecurityEvidence.Should().Be(fixture.Inputs.SecurityEvidence);
        preview.Instruction.AsOfDate.Should().Be(fixture.Inputs.AsOfDate);
        preview.Instruction.CalculationVersion.Should().Be(OpenLotAmortization.ModelVersion);
        preview.Instruction.Reversal.Should().BeEquivalentTo(new OpenLotAmortizationReversalDto(
            fixture.BatchId, fixture.Original.Journal.Entry.JournalEntryId, fixture.Before.ToOpenLot()));
        preview.Projection.TransactionCostBasis.Should().Be(fixture.Inputs.ExpectedLot.OpenTransactionCostBasis);
        preview.Projection.FunctionalCostBasis.Should().Be(fixture.Inputs.ExpectedLot.OpenFunctionalCostBasis);
        preview.Projection.TransactionMovement.Should().Be(-fixture.Forward.TransactionMovement);
        preview.Projection.FunctionalMovement.Should().Be(-fixture.Forward.FunctionalMovement);
        await fixture.Securities.Received(1).GetProjectionAsync(fixture.Before.SecurityId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewReversal_ChangedSecurityRequiresFreshEvidenceAndDoesNotRecalculateTheInverse()
    {
        var fixture = new Fixture(premium: true);
        var replacement = fixture.Inputs.Security with
        {
            Version = fixture.Inputs.Security.Version + 1,
            CommonTerms = JsonSerializer.SerializeToElement(new { maturityDate = "2040-01-01", couponRate = 30m })
        };
        fixture.Securities.GetProjectionAsync(fixture.Before.SecurityId, Arg.Any<CancellationToken>()).Returns(replacement);
        var stalePreview = () => fixture.Service.PreviewReversalAsync(fixture.Before.LedgerBookId, fixture.BatchId);
        await stalePreview.Should().ThrowAsync<ArgumentException>().WithMessage("*hash-bound versioned Security Master*");
        var reviewed = fixture.Inputs.SecurityEvidence with
        {
            EvidenceId = "reviewed-replacement-security",
            EvidenceVersion = replacement.Version,
            ContentHashSha256 = OpenLotAmortization.SecurityHash(replacement)
        };

        var preview = await fixture.Service.PreviewReversalAsync(fixture.Before.LedgerBookId, fixture.BatchId, reviewed);

        preview.Instruction.Security.Should().BeSameAs(replacement);
        preview.Instruction.SecurityEvidence.Should().Be(reviewed);
        preview.Instruction.Reversal!.RestoresLot.Should().BeEquivalentTo(fixture.Before.ToOpenLot());
        preview.Projection.TransactionMovement.Should().Be(-fixture.Forward.TransactionMovement);
        preview.Projection.FunctionalMovement.Should().Be(-fixture.Forward.FunctionalMovement);
    }

    [Theory]
    [InlineData("later-mutation")]
    [InlineData("version")]
    [InlineData("basis")]
    public async Task PreviewReversal_RejectsTheLotAfterAnyInterveningChange(string change)
    {
        var fixture = new Fixture(premium: true);
        var changed = change switch
        {
            "later-mutation" => fixture.After with { LastMutationBatchId = Guid.NewGuid() },
            "version" => fixture.After with { Version = fixture.After.Version + 1 },
            "basis" => fixture.After with
            {
                BasisAdjustment = fixture.After.BasisAdjustment! with
                { TransactionCostBasis = fixture.After.BasisAdjustment.TransactionCostBasis + 1m }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        fixture.ReturnCurrent(changed);

        var preview = () => fixture.Service.PreviewReversalAsync(fixture.Before.LedgerBookId, fixture.BatchId);

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*latest unchanged lot mutation*");
        fixture.Positions.ReceivedCalls().Should().BeEmpty("stale lot authority must fail before a new preview is assembled");
    }

    [Fact]
    public async Task PreviewReversal_RejectsAnOriginalFromAnotherBook()
    {
        var fixture = new Fixture(premium: true);

        var preview = () => fixture.Service.PreviewReversalAsync(Guid.NewGuid(), fixture.BatchId);

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requested book*");
    }

    [Fact]
    public async Task PreviewReversal_RequiresTheImmutableBeforeSnapshot()
    {
        var fixture = new Fixture(premium: true);
        var original = fixture.Original with { Mutations = [fixture.Original.Mutations.Single() with { LotBefore = null }] };
        fixture.Lots.GetAtomicTaxLotPostingAsync(fixture.BatchId, Arg.Any<CancellationToken>()).Returns(original);

        var preview = () => fixture.Service.PreviewReversalAsync(fixture.Before.LedgerBookId, fixture.BatchId);

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retained original amortization mutation*");
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset RecordedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        public Guid BatchId { get; } = Guid.NewGuid();
        public ILedgerJournalStore Lots { get; } = Substitute.For<ILedgerJournalStore>();
        public ISecurityMasterStore Securities { get; } = Substitute.For<ISecurityMasterStore>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public OpenLotAmortizationInstructionDto Inputs { get; }
        public OpenLotAmortizationProjectionDto Forward { get; }
        public LedgerTaxLotRecord Before { get; }
        public LedgerTaxLotRecord After { get; }
        public AtomicTaxLotJournalResult Original { get; }
        public BookPositionDto Position { get; }
        public CanonicalLotAmortizationService Service { get; }

        public Fixture(bool premium, bool legacy = false)
        {
            Inputs = AtomicTaxLotJournalStoreTests.AmortPureInstruction(premium ? 110m : 90m, 10m,
                BondAmortizationMethod.StraightLine, null);
            if (legacy)
                Inputs = Inputs with { CalculationVersion = null };
            Forward = OpenLotAmortization.Project(Inputs);
            var lot = Inputs.ExpectedLot;
            var account = LedgerAccounts.Securities("AMORT", "preview-account");
            Before = new(lot.TaxLotRecordId, lot.LedgerBookId, account, lot.LotId, lot.AcquiredDate,
                lot.OriginalQuantity / 100m, lot.OpenQuantity / 100m,
                lot.Acquisition.TransactionCostBasis / (lot.OriginalQuantity / 100m),
                lot.Acquisition.AcquisitionCurrency, RecordedAt, RecordedAt, Version: lot.Version,
                SecurityId: lot.SecurityId, BookPositionId: lot.BookPositionId,
                OriginalFace: lot.OriginalQuantity, BookedFactor: 1m, ParBasis: 100m, Acquisition: lot.Acquisition);
            After = Before with
            {
                Version = Before.Version + 1,
                LastMutationBatchId = BatchId,
                BasisAdjustment = new(BatchId, OpenLotBasisAdjustmentReasons.Amortization, Before.OpenQuantity,
                    Forward.TransactionCostBasis, Forward.FunctionalCostBasis, Inputs)
            };
            var journalId = Guid.NewGuid();
            const string description = "Retained amortization";
            var debit = Math.Max(Forward.FunctionalMovement, 0m);
            var credit = Math.Max(-Forward.FunctionalMovement, 0m);
            var journal = new JournalEntry(journalId, RecordedAt, description,
            [
                new(Guid.NewGuid(), journalId, RecordedAt, account, debit, credit, description),
                new(Guid.NewGuid(), journalId, RecordedAt, LedgerAccounts.CouponIncomeFor("preview-account"), credit, debit, description)
            ]);
            var evidence = lot.Acquisition.Evidence.Append(Inputs.SecurityEvidence).ToArray();
            var mutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), BatchId, AtomicTaxLotMutationKind.Amortization,
                lot.TaxLotRecordId, lot.LotId, 0, Before.OpenQuantity, 0m, After.OpenQuantity, Before.UnitCost,
                Math.Abs(Forward.FunctionalMovement), Before.Version, After.Version, Inputs.SecurityEvidence.EvidenceId,
                journalId, Guid.NewGuid(), evidence, RecordedAt, Before, After, SecurityId: lot.SecurityId,
                BookPositionId: lot.BookPositionId);
            Original = new(BatchId, AtomicTaxLotMutationKind.Amortization, new string('a', 64), false,
                new LedgerJournalEntryRecord(journal, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, RecordedAt),
                [After], [mutation], evidence);
            var book = new AccountingBookContextDto(lot.LedgerBookId, "preview-fund", Guid.NewGuid(),
                FundStructureNodeKindDto.Fund, "Preview book", "USD", AccountingBasisKindDto.Gaap, "gaap", "v1");
            Position = new(lot.BookPositionId, lot.SecurityId, Guid.NewGuid(), book, BookPositionSides.Long, "Active",
                lot.AcquiredDate, Version: Inputs.ExpectedBookPositionVersion + 2);
            Lots.GetAtomicTaxLotPostingAsync(BatchId, Arg.Any<CancellationToken>()).Returns(Original);
            ReturnCurrent(After);
            Positions.GetBookPositionAsync(lot.BookPositionId, Arg.Any<CancellationToken>()).Returns(Position);
            Securities.GetProjectionAsync(lot.SecurityId, Arg.Any<CancellationToken>()).Returns(Inputs.Security);
            Service = new(Lots, Securities, Positions);
        }

        public void ReturnCurrent(LedgerTaxLotRecord lot)
            => Lots.GetTaxLotsByIdsAsync(Before.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<LedgerTaxLotRecord>>([lot]));
    }
}
