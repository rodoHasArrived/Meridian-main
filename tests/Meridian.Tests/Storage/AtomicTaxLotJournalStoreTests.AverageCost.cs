using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    private static readonly LedgerAccount AverageCostAccount = new("Investment lots", LedgerAccountType.Asset);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_AverageCostReliefRestatesThePoolAndReportingCertifiesIt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            ledgerBookId, "fund-average-cost", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Average Cost Book", "USD", openedAt, openedAt));
        var period = await database.JournalStore.SavePeriodAsync(
            new LedgerAccountingPeriod(periodId, ledgerBookId, 2026, 5, "2026-05",
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "Open", openedAt, ClosedAt: null, Version: 0),
            expectedVersion: 0);
        await database.JournalStore.SaveTaxLotPolicyAsync(new LedgerAccountTaxLotPolicyRecord(
            Guid.NewGuid(), ledgerBookId, AverageCostAccount, LedgerTaxLotReliefMethod.AverageCost,
            "tax-policy-avg", new DateOnly(2026, 5, 1), openedAt, openedAt));

        // Pool: 100 @ 100 and 100 @ 110, so 200 units carry 21,000 of basis (105 per unit).
        var first = (await database.JournalStore.AppendAssetPostingAsync(
            BuildCanonicalAcquisition(ledgerBookId, periodId, period.Version, "lot-avg-1", 100m))).MutatedLots[0];
        var second = (await database.JournalStore.AppendAssetPostingAsync(
            BuildCanonicalAcquisition(ledgerBookId, periodId, period.Version, "lot-avg-2", 110m))).MutatedLots[0];
        var pool = new[] { first, second }.OrderBy(static lot => lot.TaxLotRecordId).ToArray();

        // Relieving 50 units books the pooled 5,250 from the FIFO-first lot, not its own 100 or 110.
        var partial = BuildAverageCostDisposal(ledgerBookId, periodId, period.Version, "atomic-avg:partial",
            [(pool[0], 1, 100m, 50m, 5_250m)]);
        var disposed = await database.JournalStore.AppendAssetPostingAsync(partial);

        disposed.Mutations.Should().HaveCount(2);
        disposed.Mutations[0].MutationKind.Should().Be(AtomicTaxLotMutationKind.Disposal);
        disposed.Mutations[0].CostBasis.Should().Be(5_250m);
        disposed.Mutations[1].MutationKind.Should().Be(AtomicTaxLotMutationKind.BasisRedistribution);
        disposed.Mutations[1].TaxLotRecordId.Should().Be(pool[1].TaxLotRecordId);
        disposed.Mutations[1].QuantityDelta.Should().Be(0m);
        disposed.Mutations[1].CostBasis.Should().Be(10_500m);

        // Every survivor now carries the pooled 105 per unit, and the lots of record tie to the
        // asset account: 21,000 acquired less 5,250 relieved.
        var restated = await ReadCanonicalOpenLotsAsync(database, ledgerBookId);
        restated.Select(static lot => lot.OpenQuantity).Should().Equal(50m, 100m);
        restated.Select(static lot => lot.OpenFunctionalCostBasis).Should().Equal(5_250m, 10_500m);
        restated.Sum(static lot => lot.OpenFunctionalCostBasis).Should().Be(21_000m - 5_250m);

        var replay = await database.JournalStore.AppendAssetPostingAsync(partial);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Select(static mutation => mutation.MutationRecordId)
            .Should().Equal(disposed.Mutations.Select(static mutation => mutation.MutationRecordId));

        // Closing the pool relieves exactly the restated basis and leaves nothing behind.
        var closing = BuildAverageCostDisposal(ledgerBookId, periodId, period.Version, "atomic-avg:closing",
            [(pool[0], 2, 50m, 50m, 5_250m), (pool[1], 2, 100m, 100m, 10_500m)]);
        var closed = await database.JournalStore.AppendAssetPostingAsync(closing);
        closed.Mutations.Should().OnlyContain(static mutation => mutation.MutationKind == AtomicTaxLotMutationKind.Disposal);
        (await ReadCanonicalOpenLotsAsync(database, ledgerBookId)).Should().BeEmpty();

        // Reporting re-runs pooled relief over the retained pre-relief pool for both batches.
        var journals = (await database.JournalStore.GetByPeriodAsync(periodId))
            .ToDictionary(static item => item.Entry.JournalEntryId, static item => item.Entry);
        var history = await database.JournalStore.GetTaxLotDisposalHistoryAsync(ledgerBookId,
            [partial.Journal.Entry.JournalEntryId, closing.Journal.Entry.JournalEntryId]);
        history.Should().HaveCount(2);
        var partialHistory = history.Single(record => record.MutationBatchId == partial.MutationBatchId);
        partialHistory.PoolLots.Should().HaveCount(2);
        CanonicalDisposalHistoryProjector.Project(partialHistory, journals[partial.Journal.Entry.JournalEntryId],
            ledgerBookId, "USD").CostBasis.Should().Be(5_250m);
        var closingHistory = history.Single(record => record.MutationBatchId == closing.MutationBatchId);
        CanonicalDisposalHistoryProjector.Project(closingHistory, journals[closing.Journal.Entry.JournalEntryId],
            ledgerBookId, "USD").CostBasis.Should().Be(15_750m);

        var withoutPool = () => CanonicalDisposalHistoryProjector.Project(partialHistory with { PoolLots = null },
            journals[partial.Journal.Entry.JournalEntryId], ledgerBookId, "USD");
        withoutPool.Should().Throw<LedgerValidationException>().WithMessage("*pre-relief pool*");
        var tampered = () => CanonicalDisposalHistoryProjector.Project(
            partialHistory with { PoolLots = [partialHistory.PoolLots![0]] },
            journals[partial.Journal.Entry.JournalEntryId], ledgerBookId, "USD");
        tampered.Should().Throw<LedgerValidationException>().WithMessage("*pooled relief*");
    }

    private static async Task<IReadOnlyList<OpenLotDto>> ReadCanonicalOpenLotsAsync(
        LedgerPostgresTestDatabase database,
        Guid ledgerBookId)
        => (await database.JournalStore.ListOpenTaxLotsAsync(ledgerBookId, AverageCostAccount))
            .OrderBy(static lot => lot.TaxLotRecordId)
            .Select(static lot => lot.ToOpenLot())
            .ToArray();

    private static AtomicTaxLotJournalCommand BuildCanonicalAcquisition(
        Guid ledgerBookId,
        Guid periodId,
        long expectedPeriodVersion,
        string lotId,
        decimal unitCost)
    {
        var mutationBatchId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var idempotencyKey = $"atomic-acquisition:{lotId}";
        var evidence = BuildEvidence("evidence-acquisition", 'a');
        var journal = BuildJournalWrite(ledgerBookId, periodId, sourceEventId, idempotencyKey,
            debitAccount: AverageCostAccount.Name, creditAccount: "Cash", amount: 100m * unitCost);
        var recordedAt = DateTimeOffset.Parse("2026-05-12T14:00:00Z");
        var taxLotRecordId = Guid.NewGuid();
        var acquisitionEvidence = BuildEvidence($"canonical-{lotId}", 'e') with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = taxLotRecordId.ToString("D"),
            EffectiveDate = new DateOnly(2026, 5, 12)
        };
        var lot = new LedgerTaxLotRecord(
            taxLotRecordId, ledgerBookId, AverageCostAccount, lotId, new DateOnly(2026, 5, 12),
            OriginalQuantity: 100m, OpenQuantity: 100m, UnitCost: unitCost, Currency: "USD",
            CreatedAt: recordedAt, UpdatedAt: recordedAt, SourceJournalEntryId: journal.Entry.JournalEntryId,
            EvidenceRef: evidence.EvidenceId, Version: 1, OriginatingMutationBatchId: mutationBatchId,
            LastMutationBatchId: mutationBatchId, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId,
            Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 100m * unitCost, 100m * unitCost,
                new DateOnly(2026, 5, 12), null, [acquisitionEvidence]));
        return AtomicTaxLotJournalCommand.Create(mutationBatchId, ledgerBookId, journal, sourceEventId,
            idempotencyKey, expectedPeriodVersion, AtomicTaxLotMutationKind.Acquisition, [evidence],
            acquisitionLot: lot);
    }

    private static AtomicTaxLotJournalCommand BuildAverageCostDisposal(
        Guid ledgerBookId,
        Guid periodId,
        long expectedPeriodVersion,
        string idempotencyKey,
        IReadOnlyList<(LedgerTaxLotRecord Lot, long Version, decimal OpenQuantity, decimal Quantity, decimal CostBasis)> slices)
    {
        var sourceEventId = Guid.NewGuid();
        var evidence = BuildEvidence("evidence-average-cost", 'c');
        var selections = slices.Select((slice, index) => new LedgerTaxLotDisposalSelection(
            slice.Lot.TaxLotRecordId,
            slice.Lot.LotId,
            ExpectedVersion: slice.Version,
            ExpectedOpenQuantity: slice.OpenQuantity,
            Quantity: slice.Quantity,
            SelectionOrdinal: index,
            evidence.EvidenceId,
            ExpectedUnitCost: slice.Lot.UnitCost,
            ExpectedCostBasis: slice.CostBasis)).ToArray();
        return AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            BuildJournalWrite(ledgerBookId, periodId, sourceEventId, idempotencyKey,
                debitAccount: "Cash", creditAccount: AverageCostAccount.Name,
                amount: slices.Sum(static slice => slice.CostBasis)),
            sourceEventId,
            idempotencyKey,
            expectedPeriodVersion,
            AtomicTaxLotMutationKind.Disposal,
            [evidence],
            disposalSelections: selections,
            reliefMethod: "AverageCost",
            policyRevision: "tax-policy-avg");
    }
}
