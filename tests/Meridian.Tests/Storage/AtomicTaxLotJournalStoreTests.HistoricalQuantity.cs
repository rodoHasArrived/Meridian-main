using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task HistoricalAssetScope_PartialDisposalPreservesEarlierPaydownEntitlement()
        => VerifyHistoricalDisposalAsync(40m);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task HistoricalAssetScope_FullyDisposedLotStillHasEarlierPaydownEntitlement()
        => VerifyHistoricalDisposalAsync(100m);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task HistoricalAssetScope_RejectsLegacyReliefWithoutRetainedDates()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "historical-legacy", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Historical legacy", "USD", at, at));
        var lot = BuildAcquisitionCommand(bookId).AcquisitionLot! with
        {
            OpenQuantity = 60m,
            SourceJournalEntryId = null,
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null
        };
        await database.JournalStore.SaveTaxLotAsync(lot);
        var act = () => database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(bookId,
            TestSecurityId, TestBookPositionId, new(2026, 5, 15));
        await act.Should().ThrowAsync<LedgerValidationException>().WithMessage("*complete retained mutation evidence*");
    }

    private static async Task VerifyHistoricalDisposalAsync(decimal quantitySold)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "historical-lots", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Historical lots", "USD", openedAt, openedAt));
        var period = await database.JournalStore.SavePeriodAsync(new(periodId, bookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", openedAt, null, 0), 0);
        await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), bookId,
            new("Investment lots", LedgerAccountType.Asset), LedgerTaxLotReliefMethod.Fifo,
            "tax-policy-v1", new(2026, 5, 1), openedAt, openedAt));
        var acquisition = BuildAcquisitionCommand(bookId, periodId, period.Version);
        var lot = acquisition.AcquisitionLot!;
        var acquisitionEvidence = BuildEvidence("historical-acquisition", 'e') with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = lot.TaxLotRecordId.ToString("D"),
            EffectiveDate = lot.AcquiredDate
        };
        acquisition = (acquisition with
        {
            AcquisitionLot = lot with
            {
                OriginalFace = 10_000m,
                BookedFactor = 0.8m,
                ParBasis = 100m,
                Acquisition = new(LotQuantityBasis.Face, "USD", "USD", 1m, 10_000m, 10_000m,
                lot.AcquiredDate, new(100m, 0.8m, BondAmortizationMethod.StraightLine, null), [acquisitionEvidence])
            }
        }).WithComputedFingerprint();
        var acquired = await database.JournalStore.AppendAssetPostingAsync(acquisition);
        lot = acquired.MutatedLots.Single();
        var eventId = Guid.NewGuid();
        var evidence = BuildEvidence("historical-disposal", 'f') with { EffectiveDate = new(2026, 5, 20) };
        var write = BuildJournalWrite(bookId, periodId, eventId, "historical-disposal", "Cash",
            "Investment lots", quantitySold * 100m);
        write = write with
        {
            Entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp,
            write.Entry.Description, write.Entry.Lines,
            write.Entry.Metadata with { EffectiveDate = new DateOnly(2026, 5, 20) })
        };
        var disposal = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), bookId, write, eventId,
            "historical-disposal", period.Version, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, 100m,
                quantitySold, 0, evidence.EvidenceId, 100m, quantitySold * 100m)],
            reliefMethod: "Fifo", policyRevision: "tax-policy-v1");
        await database.JournalStore.AppendAssetPostingAsync(disposal);
        (await database.JournalStore.AppendAssetPostingAsync(disposal)).IsExactReplay.Should().BeTrue();

        // Reconstruct services so the result must come from durable history, not cached acquisition.
        var reopened = new PostgresLedgerJournalStore(database.Options);
        var historical = await reopened.ListOpenTaxLotsByAssetScopeAsync(bookId, TestSecurityId,
            TestBookPositionId, new(2026, 5, 15));
        historical.Should().ContainSingle().Which.OpenQuantity.Should().Be(100m);
        var held = historical.Single();
        (held.ToFaceValueLot()!.CurrentFace(1m) * held.OpenQuantity / held.OriginalQuantity)
            .Should().Be(12_500m);
        var after = await reopened.ListOpenTaxLotsByAssetScopeAsync(bookId, TestSecurityId,
            TestBookPositionId, new(2026, 5, 20));
        if (quantitySold == 100m)
            after.Should().BeEmpty();
        else
            after.Should().ContainSingle().Which.OpenQuantity.Should().Be(60m);
        (await reopened.GetTaxLotsByIdsAsync(bookId, [lot.TaxLotRecordId])).Single()
            .OpenQuantity.Should().Be(100m - quantitySold, "historical reads never rewrite current holdings");
    }
}
