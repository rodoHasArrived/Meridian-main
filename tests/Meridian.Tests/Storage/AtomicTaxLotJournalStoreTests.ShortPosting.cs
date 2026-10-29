using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ShortOpeningAndPartialCover_RetainLiabilityCashResultAndRestartReplay()
    {
        foreach (var coverPrice in new[] { 80, 100, 120 })
            await AssertShortOpeningAndCoverAsync(coverPrice);
    }

    private static async Task AssertShortOpeningAndCoverAsync(int coverPrice)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (original, authority) = await PrepareReferencePostingAsync(database, false, BookPositionSides.Short);
        var liability = original.AcquisitionLot!.Account with { AccountType = LedgerAccountType.Liability };
        var opening = (original with
        {
            Direction = OpenLotDirection.Short,
            AcquisitionLot = original.AcquisitionLot with
            {
                Account = liability,
                Acquisition = original.AcquisitionLot.Acquisition! with { Direction = OpenLotDirection.Short }
            },
            Journal = WithShortLines(original.Journal, ShortLotJournalProjector.Open(liability, 10_000m), original.ExpectedPeriodVersion)
        }).WithComputedFingerprint();
        var posted = await database.JournalStore.AppendAssetPostingAsync(opening);
        var lot = posted.MutatedLots.Single();
        lot.Account.AccountType.Should().Be(LedgerAccountType.Liability);
        lot.Acquisition!.Direction.Should().Be(OpenLotDirection.Short);
        lot.OpenQuantity.Should().Be(100m);
        await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), original.LedgerBookId, liability,
            LedgerTaxLotReliefMethod.Fifo, "short-fifo", lot.AcquiredDate, lot.CreatedAt, lot.CreatedAt));
        var evidence = BuildEvidence("short-cover", 'd');
        var sourceId = Guid.NewGuid();
        var journal = BuildJournalWrite(original.LedgerBookId, original.Journal.PeriodId, sourceId, "short-cover");
        var cover = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), original.LedgerBookId,
            WithShortLines(journal, ShortLotJournalProjector.Cover(liability, 4_000m, 40m * coverPrice), original.ExpectedPeriodVersion),
            sourceId, "short-cover", original.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, 100m, 40m, 0, evidence.EvidenceId, 100m, 4_000m)],
            reliefMethod: "Fifo", policyRevision: "short-fifo", disposalSalePrice: coverPrice,
            direction: OpenLotDirection.Short, referenceVersions: authority.Versions);
        foreach (var invalid in new[]
        {
            cover with { DisposalSalePrice = null },
            cover with { DisposalSalePrice = coverPrice + 1m },
            cover with { Direction = OpenLotDirection.Long },
            cover with { Journal = cover.Journal with { PostingCommand = cover.Journal.PostingCommand! with
                { ApprovalState = AccountingPostingApprovalStateDto.Pending } } }
        })
        {
            var refused = () => database.JournalStore.AppendAssetPostingAsync(invalid.WithComputedFingerprint());
            await refused.Should().ThrowAsync<LedgerValidationException>();
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(cover.MutationBatchId)).Should().BeNull();
            (await database.JournalStore.GetByPeriodAsync(cover.Journal.PeriodId)).Should().ContainSingle();
        }
        var covered = await database.JournalStore.AppendAssetPostingAsync(cover);
        covered.MutatedLots.Single().OpenQuantity.Should().Be(60m);
        covered.MutatedLots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(6_000m);
        covered.Journal.Entry.IsBalanced.Should().BeTrue();
        var replay = await database.RestartJournalStore().AppendAssetPostingAsync(cover);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Single().MutationRecordId.Should().Be(covered.Mutations.Single().MutationRecordId);
        var forged = (cover with { DisposalSalePrice = coverPrice + 1m }).WithComputedFingerprint();
        var retry = () => database.RestartJournalStore().AppendAssetPostingAsync(forged);
        await retry.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
        var remaining = covered.MutatedLots.Single();
        var finalSource = Guid.NewGuid();
        var finalJournal = BuildJournalWrite(original.LedgerBookId, original.Journal.PeriodId, finalSource, "short-final-cover");
        var final = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), original.LedgerBookId,
            WithShortLines(finalJournal, ShortLotJournalProjector.Cover(liability, 6_000m, 60m * coverPrice), original.ExpectedPeriodVersion),
            finalSource, "short-final-cover", original.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, remaining.Version, 60m, 60m, 0, evidence.EvidenceId, 100m, 6_000m)],
            reliefMethod: "Fifo", policyRevision: "short-fifo", disposalSalePrice: coverPrice,
            direction: OpenLotDirection.Short, referenceVersions: authority.Versions);
        var closed = await database.RestartJournalStore().AppendAssetPostingAsync(final);
        closed.MutatedLots.Single().OpenQuantity.Should().Be(0m);
        closed.MutatedLots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(0m);
        var period = (await database.JournalStore.GetPeriodAsync(original.Journal.PeriodId))!;
        await database.JournalStore.SavePeriodAsync(period with { Status = "HardClosed", ClosedAt = DateTimeOffset.UtcNow }, period.Version);
        (await database.RestartJournalStore().AppendAssetPostingAsync(final)).IsExactReplay.Should().BeTrue();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(80, 800)]
    [InlineData(100, 0)]
    [InlineData(120, -800)]
    public void ShortCoverJournal_PreservesBookGainOrLoss(int coverPrice, int result)
    {
        var account = new LedgerAccount("Short investments", LedgerAccountType.Liability);
        var lines = ShortLotJournalProjector.Cover(account, 4_000m, 40m * coverPrice);
        lines.Sum(line => line.debit).Should().Be(lines.Sum(line => line.credit));
        lines.Where(line => line.account == account).Sum(line => line.debit - line.credit).Should().Be(4_000m);
        (lines.Where(line => line.account == LedgerAccounts.RealizedGain).Sum(line => line.credit)
            - lines.Where(line => line.account == LedgerAccounts.RealizedLoss).Sum(line => line.debit)).Should().Be(result);
    }

    private static LedgerJournalEntryWrite WithShortLines(LedgerJournalEntryWrite write,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines, long periodVersion)
    {
        var entry = write.Entry;
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), write.LedgerBookId!.Value, write.PeriodId,
            entry.Metadata.EffectiveDate!.Value, entry.Timestamp, entry.Metadata.IdempotencyKey!,
            SourceEventId: write.SourceEventId, ExpectedVersion: periodVersion,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-short-review",
            OperatorRationale: "Reviewed short cash, liability and realized book accounting", LedgerBookId: write.LedgerBookId)
        { Actor = "independent-controller" };
        return write with
        {
            PostingCommand = posting,
            Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description,
            lines.Select(line => new LedgerEntry(Guid.NewGuid(), entry.JournalEntryId, entry.Timestamp, line.account,
                line.debit, line.credit, entry.Description, entry.Lines[0].Dimensions,
                new LedgerEntryCurrency("USD", "USD", line.debit, line.credit, 1m))).ToArray(), entry.Metadata)
        };
    }
}
