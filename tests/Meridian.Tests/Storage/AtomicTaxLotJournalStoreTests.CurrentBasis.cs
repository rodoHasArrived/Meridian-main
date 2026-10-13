using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_FifoAfterRedistributionConservesCurrentBasisAndReplays()
        => VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod.Fifo);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_LifoAfterRedistributionConservesCurrentBasisAndReplays()
        => VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod.Lifo);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_HifoAfterRedistributionOrdersCurrentBasisAndReplays()
        => VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod.Hifo);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_SpecificIdAfterRedistributionConservesCurrentBasisAndReplays()
        => VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod.SpecificId);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_AverageCostAfterRedistributionConservesCurrentBasisAndReplays()
        => VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod.AverageCost);

    private static async Task VerifyCurrentBasisRoundTripAsync(LedgerTaxLotReliefMethod method)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scenario = await CreateRedistributedBasisScenarioAsync(database);
        var originals = scenario.AcquiredLots.ToDictionary(static lot => lot.TaxLotRecordId);
        await SaveCurrentBasisMethodAsync(database.JournalStore, scenario, method);

        if (method == LedgerTaxLotReliefMethod.Hifo)
        {
            // The old 100-cost acquisition now carries 105 per unit. It outranks the later
            // 102-cost acquisition and ties the old 110-cost acquisition on current basis.
            var added = await database.JournalStore.AppendAssetPostingAsync(BuildCurrentBasisAcquisition(
                scenario.LedgerBookId, scenario.Period, "lot-current-new-102", 102m, 100m,
                Guid.Parse("33333333-3333-3333-3333-333333333333"), new DateOnly(2026, 5, 12)));
            originals.Add(added.MutatedLots[0].TaxLotRecordId, added.MutatedLots[0]);
        }

        var before = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        var adjustedTransaction = before.Sum(static lot => lot.ToOpenLot().OpenTransactionCostBasis);
        var adjustedFunctional = before.Sum(static lot => lot.ToOpenLot().OpenFunctionalCostBasis);
        var partial = BuildCurrentBasisDisposal(scenario, before, method, 25m, $"current-basis:{method}:partial");
        partial.Relief.FunctionalCostBasis.Should().Be(2_625m);
        partial.Relief.TransactionCostBasis.Should().Be(2_100m);
        partial.Command.DisposalSelections.Should().OnlyContain(selection =>
            selection.ExpectedCostBasis != selection.Quantity * selection.ExpectedUnitCost);
        if (method == LedgerTaxLotReliefMethod.Hifo)
            partial.Command.DisposalSelections![0].TaxLotRecordId.Should().Be(scenario.AcquiredLots[0].TaxLotRecordId);

        var posted = await database.JournalStore.AppendAssetPostingAsync(partial.Command);
        posted.ReliefMethod.Should().Be(method.ToString());
        posted.PolicyRevision.Should().Be(CurrentBasisPolicyRevision(method));
        posted.Mutations.Where(static mutation => mutation.MutationKind == AtomicTaxLotMutationKind.Disposal)
            .Sum(static mutation => mutation.CostBasis).Should().Be(partial.Relief.FunctionalCostBasis);
        posted.RetainedEvidence.Should().BeEquivalentTo(partial.Command.RetainedEvidence);
        AssertCurrentBasisAcquisitionFacts(posted.MutatedLots, originals);

        var afterPartial = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        AssertCurrentBasisConservation(adjustedTransaction, adjustedFunctional, partial.Relief, afterPartial);
        AssertCurrentBasisAcquisitionFacts(afterPartial, originals);
        await AssertCurrentBasisReportAsync(database.JournalStore, scenario, partial.Command, partial.Relief);

        // A new store instance must reproduce the retained posting before it can finish relief.
        var restarted = new PostgresLedgerJournalStore(database.Options);
        var auditBeforeReplay = (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var recovered = await restarted.GetAtomicTaxLotPostingAsync(partial.Command.MutationBatchId);
        recovered.Should().NotBeNull();
        recovered!.CanonicalFingerprint.Should().Be(partial.Command.CanonicalFingerprint);
        recovered.Mutations.Should().BeEquivalentTo(posted.Mutations);
        var replay = await restarted.AppendAssetPostingAsync(partial.Command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(posted.Mutations);
        replay.MutatedLots.Should().BeEquivalentTo(posted.MutatedLots);
        replay.RetainedEvidence.Should().BeEquivalentTo(posted.RetainedEvidence);
        (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBeforeReplay);

        var closing = BuildCurrentBasisDisposal(scenario,
            await ReadCurrentBasisLotsAsync(restarted, scenario.LedgerBookId), method,
            afterPartial.Sum(static lot => lot.OpenQuantity), $"current-basis:{method}:closing");
        var closed = await restarted.AppendAssetPostingAsync(closing.Command);
        closed.MutatedLots.Should().OnlyContain(static lot => lot.OpenQuantity == 0m);
        AssertCurrentBasisAcquisitionFacts(closed.MutatedLots, originals);
        (await ReadCurrentBasisLotsAsync(restarted, scenario.LedgerBookId)).Should().BeEmpty();
        (partial.Relief.TransactionCostBasis + closing.Relief.TransactionCostBasis).Should().Be(adjustedTransaction);
        (partial.Relief.FunctionalCostBasis + closing.Relief.FunctionalCostBasis).Should().Be(adjustedFunctional);
        await AssertCurrentBasisReportAsync(restarted, scenario, closing.Command, closing.Relief);
        await AssertCurrentBasisReportAsync(restarted, scenario, partial.Command, partial.Relief);

        // Exact replay still succeeds after the lots close and the active policy changes again.
        await SaveCurrentBasisMethodAsync(restarted, scenario,
            method == LedgerTaxLotReliefMethod.Fifo ? LedgerTaxLotReliefMethod.Lifo : LedgerTaxLotReliefMethod.Fifo);
        var auditAfterClosing = (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var closingReplay = await restarted.AppendAssetPostingAsync(closing.Command);
        closingReplay.IsExactReplay.Should().BeTrue();
        closingReplay.Mutations.Should().BeEquivalentTo(closed.Mutations);
        var olderReplay = await restarted.AppendAssetPostingAsync(partial.Command);
        olderReplay.MutatedLots.Should().BeEquivalentTo(posted.MutatedLots);
        var redistributionReplay = await restarted.AppendAssetPostingAsync(scenario.Redistribution);
        redistributionReplay.IsExactReplay.Should().BeTrue();
        (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditAfterClosing);

        var changedEvidence = (partial.Command with
        {
            RetainedEvidence = partial.Command.RetainedEvidence
                .Select(static evidence => evidence with { ReviewedBy = "different-fund-controller" }).ToArray()
        }).WithComputedFingerprint();
        var collision = () => restarted.AppendAssetPostingAsync(changedEvidence);
        await collision.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*identity collision*complete canonical fingerprint*");
        (await restarted.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditAfterClosing);
        (await ReadCurrentBasisLotsAsync(restarted, scenario.LedgerBookId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_CurrentBasisRejectsStaleSelectionsAndUnapprovedPolicyRevisions()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scenario = await CreateRedistributedBasisScenarioAsync(database);
        await SaveCurrentBasisMethodAsync(database.JournalStore, scenario, LedgerTaxLotReliefMethod.Fifo);
        var before = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var countsBefore = await ReadCurrentBasisPostingCountsAsync(database);
        var changes = new (string Name, Func<LedgerTaxLotDisposalSelection, LedgerTaxLotDisposalSelection> Apply)[]
        {
            ("version", selection => selection with { ExpectedVersion = selection.ExpectedVersion - 1 }),
            ("open-quantity", selection => selection with { ExpectedOpenQuantity = selection.ExpectedOpenQuantity + 1m }),
            ("basis", selection => selection with { ExpectedCostBasis = selection.Quantity * selection.ExpectedUnitCost }),
            ("acquisition-unit-cost", selection => selection with { ExpectedUnitCost = 105m })
        };

        foreach (var change in changes)
        {
            var valid = BuildCurrentBasisDisposal(scenario, before, LedgerTaxLotReliefMethod.Fifo, 25m,
                $"current-basis:stale:{change.Name}").Command;
            var stale = (valid with { DisposalSelections = [change.Apply(valid.DisposalSelections![0])] })
                .WithComputedFingerprint();
            var attempt = () => database.JournalStore.AppendAssetPostingAsync(stale);
            await attempt.Should().ThrowAsync<LedgerValidationException>();
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(stale.MutationBatchId)).Should().BeNull();
        }

        var current = BuildCurrentBasisDisposal(scenario, before, LedgerTaxLotReliefMethod.Fifo, 25m,
            "current-basis:obsolete-policy").Command;
        var oldRevision = (current with { PolicyRevision = "tax-policy-avg" }).WithComputedFingerprint();
        var oldMethod = (current with { ReliefMethod = "AverageCost", PolicyRevision = "tax-policy-avg" })
            .WithComputedFingerprint();
        foreach (var obsolete in new[] { oldRevision, oldMethod })
        {
            var attempt = () => database.JournalStore.AppendAssetPostingAsync(obsolete);
            await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*does not match effective policy*");
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(obsolete.MutationBatchId)).Should().BeNull();
        }

        (await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId)).Should().BeEquivalentTo(before);
        (await ReadCurrentBasisPostingCountsAsync(database)).Should().Be(countsBefore);
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
        var retry = await database.JournalStore.AppendAssetPostingAsync(current);
        retry.IsExactReplay.Should().BeFalse("a rejected revision must not claim the posting's idempotency identity");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_CurrentBasisJournalMismatchRollsBackMutationsAndEvidenceBeforeRetry()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scenario = await CreateRedistributedBasisScenarioAsync(database);
        await SaveCurrentBasisMethodAsync(database.JournalStore, scenario, LedgerTaxLotReliefMethod.Fifo);
        var before = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        // This touches both survivor lots before the exact journal economics guard refuses it.
        var disposal = BuildCurrentBasisDisposal(scenario, before, LedgerTaxLotReliefMethod.Fifo, 75m,
            "current-basis:rollback");
        disposal.Command.DisposalSelections.Should().HaveCount(2);
        var wrongJournal = BuildJournalWrite(scenario.LedgerBookId, scenario.Period.PeriodId,
            disposal.Command.SourceEventId, disposal.Command.IdempotencyKey,
            debitAccount: "Cash", creditAccount: AverageCostAccount.Name,
            amount: disposal.Relief.FunctionalCostBasis + 1m);
        var retainedEntry = disposal.Command.Journal.Entry;
        var invalid = (disposal.Command with
        {
            Journal = wrongJournal with
            {
                Entry = new JournalEntry(retainedEntry.JournalEntryId, retainedEntry.Timestamp,
                    retainedEntry.Description, wrongJournal.Entry.Lines.Select(line => new LedgerEntry(
                        line.EntryId, retainedEntry.JournalEntryId, line.Timestamp, line.Account,
                        line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray(),
                    retainedEntry.Metadata)
            }
        }).WithComputedFingerprint();
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var countsBefore = await ReadCurrentBasisPostingCountsAsync(database);

        var attempt = () => database.JournalStore.AppendAssetPostingAsync(invalid);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*one exact asset-account credit*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(invalid.MutationBatchId)).Should().BeNull();
        (await ReadCurrentBasisPostingCountsAsync(database)).Should().Be(countsBefore);
        (await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId)).Should().BeEquivalentTo(before);
        (await database.JournalStore.GetTaxLotDisposalHistoryAsync(scenario.LedgerBookId,
            [invalid.Journal.Entry.JournalEntryId])).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);

        var restarted = new PostgresLedgerJournalStore(database.Options);
        var retried = await restarted.AppendAssetPostingAsync(disposal.Command);
        retried.IsExactReplay.Should().BeFalse();
        retried.Mutations.Should().HaveCount(2);
        retried.RetainedEvidence.Should().BeEquivalentTo(disposal.Command.RetainedEvidence);
        AssertCurrentBasisConservation(before.Sum(static lot => lot.ToOpenLot().OpenTransactionCostBasis),
            before.Sum(static lot => lot.ToOpenLot().OpenFunctionalCostBasis), disposal.Relief,
            await ReadCurrentBasisLotsAsync(restarted, scenario.LedgerBookId));
        await AssertCurrentBasisReportAsync(restarted, scenario, disposal.Command, disposal.Relief);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_CurrentBasisFractionalCentConservesExactlyAndReportsPostedBasis()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        // Eight units carry 0.01 USD / 0.008 EUR. Redistribution leaves exact fractional-cent
        // relief of 0.00125 USD per unit, which must survive storage and Reporting unchanged.
        var scenario = await CreateRedistributedBasisScenarioAsync(database, firstUnitCost: 0.001m,
            firstQuantity: 4m, secondUnitCost: 0.0015m, secondQuantity: 4m, redistributedQuantity: 2m);
        await SaveCurrentBasisMethodAsync(database.JournalStore, scenario, LedgerTaxLotReliefMethod.Fifo);
        var before = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        var originalTransaction = before.Sum(static lot => lot.ToOpenLot().OpenTransactionCostBasis);
        var originalFunctional = before.Sum(static lot => lot.ToOpenLot().OpenFunctionalCostBasis);
        var partial = BuildCurrentBasisDisposal(scenario, before, LedgerTaxLotReliefMethod.Fifo, 1m,
            "current-basis:decimal:partial");
        partial.Relief.FunctionalCostBasis.Should().NotBe(decimal.Round(partial.Relief.FunctionalCostBasis, 2));
        var posted = await database.JournalStore.AppendAssetPostingAsync(partial.Command);
        posted.Mutations[0].CostBasis.Should().Be(partial.Relief.FunctionalCostBasis);
        var after = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        AssertCurrentBasisConservation(originalTransaction, originalFunctional, partial.Relief, after);
        await AssertCurrentBasisReportAsync(database.JournalStore, scenario, partial.Command, partial.Relief);

        var restarted = new PostgresLedgerJournalStore(database.Options);
        var closing = BuildCurrentBasisDisposal(scenario, after, LedgerTaxLotReliefMethod.Fifo,
            after.Sum(static lot => lot.OpenQuantity), "current-basis:decimal:closing");
        await restarted.AppendAssetPostingAsync(closing.Command);
        (partial.Relief.TransactionCostBasis + closing.Relief.TransactionCostBasis).Should().Be(originalTransaction);
        (partial.Relief.FunctionalCostBasis + closing.Relief.FunctionalCostBasis).Should().Be(originalFunctional);
        (await ReadCurrentBasisLotsAsync(restarted, scenario.LedgerBookId)).Should().BeEmpty();
        await AssertCurrentBasisReportAsync(restarted, scenario, closing.Command, closing.Relief);
        (await restarted.AppendAssetPostingAsync(partial.Command)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AppendAssetPostingAsync_CurrentBasisBeyondDurablePrecisionRefusesWithoutSideEffects()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scenario = await CreateRedistributedBasisScenarioAsync(database);
        await SaveCurrentBasisMethodAsync(database.JournalStore, scenario, LedgerTaxLotReliefMethod.Fifo);
        var before = await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId);
        var valid = BuildCurrentBasisDisposal(scenario, before, LedgerTaxLotReliefMethod.Fifo, 1m,
            "current-basis:precision").Command;
        var invalid = (valid with
        {
            DisposalSelections = [valid.DisposalSelections![0] with
            {
                ExpectedCostBasis = valid.DisposalSelections[0].ExpectedCostBasis + 0.0000000000001m
            }]
        }).WithComputedFingerprint();
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var countsBefore = await ReadCurrentBasisPostingCountsAsync(database);
        var attempt = () => database.JournalStore.AppendAssetPostingAsync(invalid);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*precision*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(invalid.MutationBatchId)).Should().BeNull();
        (await ReadCurrentBasisPostingCountsAsync(database)).Should().Be(countsBefore);
        (await ReadCurrentBasisLotsAsync(database.JournalStore, scenario.LedgerBookId)).Should().BeEquivalentTo(before);
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
        (await database.JournalStore.AppendAssetPostingAsync(valid)).IsExactReplay.Should().BeFalse();
    }

    private static async Task<CurrentBasisScenario> CreateRedistributedBasisScenarioAsync(
        LedgerPostgresTestDatabase database,
        decimal firstUnitCost = 100m,
        decimal firstQuantity = 100m,
        decimal secondUnitCost = 110m,
        decimal secondQuantity = 100m,
        decimal redistributedQuantity = 50m)
    {
        var ledgerBookId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(ledgerBookId,
            "fund-current-basis", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Current Basis Book", "USD", openedAt, openedAt));
        var period = await database.JournalStore.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(),
            ledgerBookId, 2026, 5, "2026-05", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31),
            "Open", openedAt, ClosedAt: null, Version: 0), expectedVersion: 0);
        var policy = await database.JournalStore.SaveTaxLotPolicyAsync(new LedgerAccountTaxLotPolicyRecord(
            Guid.NewGuid(), ledgerBookId, AverageCostAccount, LedgerTaxLotReliefMethod.AverageCost,
            "tax-policy-avg", new DateOnly(2026, 5, 1), openedAt, openedAt));
        var acquired = new List<LedgerTaxLotRecord>(2);
        acquired.Add((await database.JournalStore.AppendAssetPostingAsync(BuildCurrentBasisAcquisition(
            ledgerBookId, period, "lot-current-1", firstUnitCost, firstQuantity,
            Guid.Parse("11111111-1111-1111-1111-111111111111"), new DateOnly(2026, 5, 10)))).MutatedLots[0]);
        acquired.Add((await database.JournalStore.AppendAssetPostingAsync(BuildCurrentBasisAcquisition(
            ledgerBookId, period, "lot-current-2", secondUnitCost, secondQuantity,
            Guid.Parse("22222222-2222-2222-2222-222222222222"), new DateOnly(2026, 5, 11)))).MutatedLots[0]);
        var initial = new CurrentBasisScenario(ledgerBookId, period, policy, acquired, null!);
        var pooled = BuildCurrentBasisDisposal(initial, acquired, LedgerTaxLotReliefMethod.AverageCost,
            redistributedQuantity, "current-basis:redistribution", "tax-policy-avg");
        var redistributed = await database.JournalStore.AppendAssetPostingAsync(pooled.Command);
        redistributed.Mutations.Should().Contain(mutation => mutation.MutationKind == AtomicTaxLotMutationKind.BasisRedistribution);
        var current = await ReadCurrentBasisLotsAsync(database.JournalStore, ledgerBookId);
        AssertCurrentBasisConservation(acquired.Sum(static lot => lot.ToOpenLot().OpenTransactionCostBasis),
            acquired.Sum(static lot => lot.ToOpenLot().OpenFunctionalCostBasis), pooled.Relief, current);
        AssertCurrentBasisAcquisitionFacts(current, acquired.ToDictionary(static lot => lot.TaxLotRecordId));
        return initial with { Redistribution = pooled.Command };
    }

    private static AtomicTaxLotJournalCommand BuildCurrentBasisAcquisition(Guid ledgerBookId,
        LedgerAccountingPeriod period, string lotId, decimal unitCost, decimal quantity,
        Guid taxLotRecordId, DateOnly acquiredDate)
    {
        var command = BuildCanonicalAcquisition(ledgerBookId, period.PeriodId, period.Version, lotId, unitCost);
        var lot = command.AcquisitionLot!;
        var acquisition = lot.Acquisition! with
        {
            AcquisitionCurrency = "EUR",
            AcquisitionFxRateToFunctional = 1.25m,
            TransactionCostBasis = quantity * unitCost / 1.25m,
            FunctionalCostBasis = quantity * unitCost,
            HoldingPeriodStartDate = acquiredDate,
            Evidence = lot.Acquisition!.Evidence.Select(evidence => evidence with
            {
                SubjectId = taxLotRecordId.ToString("D"),
                EffectiveDate = acquiredDate
            }).ToArray()
        };
        var journal = BuildJournalWrite(ledgerBookId, period.PeriodId, command.SourceEventId,
            command.IdempotencyKey, amount: quantity * unitCost);
        return (command with
        {
            Journal = journal,
            AcquisitionLot = lot with
            {
                TaxLotRecordId = taxLotRecordId,
                AcquiredDate = acquiredDate,
                OriginalQuantity = quantity,
                OpenQuantity = quantity,
                Acquisition = acquisition,
                SourceJournalEntryId = journal.Entry.JournalEntryId
            }
        }).WithComputedFingerprint();
    }

    private static async Task SaveCurrentBasisMethodAsync(PostgresLedgerJournalStore store,
        CurrentBasisScenario scenario, LedgerTaxLotReliefMethod method)
        => await store.SaveTaxLotPolicyAsync(scenario.Policy with
        {
            ReliefMethod = method,
            PolicyId = CurrentBasisPolicyRevision(method),
            Rationale = $"Approved by fund-controller: current-basis {method} relief",
            UpdatedAt = DateTimeOffset.Parse("2026-05-12T13:00:00Z")
        });

    private static string CurrentBasisPolicyRevision(LedgerTaxLotReliefMethod method)
        => $"tax-policy-current-{method}";

    private static (AtomicTaxLotJournalCommand Command, OpenLotReliefResultDto Relief) BuildCurrentBasisDisposal(
        CurrentBasisScenario scenario, IReadOnlyList<LedgerTaxLotRecord> lots,
        LedgerTaxLotReliefMethod method, decimal quantity, string idempotencyKey, string? policyRevision = null)
    {
        var canonical = lots.Select(static lot => lot.ToOpenLot()).ToArray();
        var relief = new OpenLotReliefService().Select(canonical, quantity,
            Enum.Parse<OpenLotReliefMethod>(method.ToString()), method == LedgerTaxLotReliefMethod.SpecificId
                ? canonical.OrderByDescending(static lot => lot.AcquiredDate)
                    .ThenBy(static lot => lot.TaxLotRecordId).Select(static lot => lot.TaxLotRecordId).ToArray()
                : null);
        var sourceEventId = Guid.NewGuid();
        var evidence = BuildEvidence($"evidence-{idempotencyKey}", 'd');
        var byId = lots.ToDictionary(static lot => lot.TaxLotRecordId);
        var selections = relief.Selections.Select((slice, ordinal) => new LedgerTaxLotDisposalSelection(
            slice.TaxLotRecordId, slice.LotId, slice.ExpectedVersion,
            byId[slice.TaxLotRecordId].OpenQuantity, slice.Quantity, ordinal, evidence.EvidenceId,
            byId[slice.TaxLotRecordId].UnitCost, slice.FunctionalCostBasis)).ToArray();
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), scenario.LedgerBookId,
            BuildJournalWrite(scenario.LedgerBookId, scenario.Period.PeriodId, sourceEventId, idempotencyKey,
                debitAccount: "Cash", creditAccount: AverageCostAccount.Name, amount: relief.FunctionalCostBasis),
            sourceEventId, idempotencyKey, scenario.Period.Version, AtomicTaxLotMutationKind.Disposal,
            [evidence], disposalSelections: selections, reliefMethod: method.ToString(),
            policyRevision: policyRevision ?? CurrentBasisPolicyRevision(method));
        return (command, relief);
    }

    private static async Task<IReadOnlyList<LedgerTaxLotRecord>> ReadCurrentBasisLotsAsync(
        PostgresLedgerJournalStore store, Guid ledgerBookId)
        => (await store.ListOpenTaxLotsAsync(ledgerBookId, AverageCostAccount))
            .OrderBy(static lot => lot.AcquiredDate).ThenBy(static lot => lot.TaxLotRecordId).ToArray();

    private static void AssertCurrentBasisConservation(decimal transactionBefore, decimal functionalBefore,
        OpenLotReliefResultDto relief, IReadOnlyList<LedgerTaxLotRecord> remaining)
    {
        (relief.TransactionCostBasis + remaining.Sum(static lot => lot.ToOpenLot().OpenTransactionCostBasis))
            .Should().Be(transactionBefore);
        (relief.FunctionalCostBasis + remaining.Sum(static lot => lot.ToOpenLot().OpenFunctionalCostBasis))
            .Should().Be(functionalBefore);
    }

    private static void AssertCurrentBasisAcquisitionFacts(IEnumerable<LedgerTaxLotRecord> lots,
        IReadOnlyDictionary<Guid, LedgerTaxLotRecord> originals)
    {
        foreach (var lot in lots)
        {
            var original = originals[lot.TaxLotRecordId];
            lot.Acquisition.Should().BeEquivalentTo(original.Acquisition);
            lot.UnitCost.Should().Be(original.UnitCost);
            lot.OriginalQuantity.Should().Be(original.OriginalQuantity);
            lot.AcquiredDate.Should().Be(original.AcquiredDate);
            lot.SourceJournalEntryId.Should().Be(original.SourceJournalEntryId);
            lot.OriginatingMutationBatchId.Should().Be(original.OriginatingMutationBatchId);
        }
    }

    private static async Task AssertCurrentBasisReportAsync(PostgresLedgerJournalStore store,
        CurrentBasisScenario scenario, AtomicTaxLotJournalCommand command, OpenLotReliefResultDto relief)
    {
        var journal = (await store.GetByPeriodAsync(scenario.Period.PeriodId))
            .Single(item => item.Entry.JournalEntryId == command.Journal.Entry.JournalEntryId).Entry;
        var histories = await store.GetTaxLotDisposalHistoryAsync(scenario.LedgerBookId, [journal.JournalEntryId]);
        histories.Should().ContainSingle();
        var history = histories.Single();
        var report = CanonicalDisposalHistoryProjector.Project(history, journal, scenario.LedgerBookId, "USD");
        report.CostBasis.Should().Be(relief.FunctionalCostBasis);
        report.Selections.Sum(static selection => selection.CostBasis).Should().Be(relief.FunctionalCostBasis);
        report.Selections.Sum(static selection => selection.QuantityRelieved).Should().Be(relief.Quantity);
        report.Proceeds.Should().Be(relief.FunctionalCostBasis);
        report.RecognizedGainOrLoss.Should().Be(0m);
        report.IsBalanced.Should().BeTrue();
        report.CanonicalOpenLots.Should().BeEquivalentTo(history.CanonicalLots);
        foreach (var selection in report.Selections)
            selection.UnitCost.Should().Be(selection.CostBasis / selection.QuantityRelieved);
    }

    private static async Task<(long Journals, long Batches, long Mutations)> ReadCurrentBasisPostingCountsAsync(
        LedgerPostgresTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = $"""
            select (select count(*) from "{database.Options.SchemaName}".journal_entries),
                   (select count(*) from "{database.Options.SchemaName}".atomic_tax_lot_posting_batches),
                   (select count(*) from "{database.Options.SchemaName}".tax_lot_mutations);
            """;
        await using var reader = await query.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private sealed record CurrentBasisScenario(Guid LedgerBookId, LedgerAccountingPeriod Period,
        LedgerAccountTaxLotPolicyRecord Policy, IReadOnlyList<LedgerTaxLotRecord> AcquiredLots,
        AtomicTaxLotJournalCommand Redistribution);
}
