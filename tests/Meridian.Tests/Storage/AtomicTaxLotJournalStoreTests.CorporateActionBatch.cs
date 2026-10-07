using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_MultipleEqualBasisPredecessors_CommitOneJournalAndReplayEveryLot()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var second = await SaveAdditionalCorporateLotAsync(fixture);
        var instruction = AddCorporatePredecessor(fixture.Instruction, second.ToOpenLot());
        var command = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext).Command();

        var result = await fixture.Store.AppendAssetPostingAsync(command);
        result.Mutations.Should().HaveCount(6);
        result.Mutations.Select(row => row.SelectionOrdinal).Should().Equal(0, 1, 2, 3, 4, 5);
        result.Mutations.Count(row => row.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose).Should().Be(2);
        result.Journal.Entry.Lines.Should().HaveCount(6);
        result.Journal.Entry.Lines.Select(line => line.Dimensions!.TaxLotId).Should().OnlyHaveUniqueItems();
        result.Journal.Entry.Lines.Where(line => line.Credit > 0m).Select(line => line.Credit).Should().Equal(6_930m, 6_930m);
        foreach (var group in OpenLotCorporateAction.Groups(instruction))
        {
            var close = result.Mutations.Single(row => row.TaxLotRecordId == group.ExpectedLot.TaxLotRecordId);
            close.LotAfter.OpenQuantity.Should().Be(0m);
            close.ResultVersion.Should().Be(group.ExpectedLot.Version + 1);
            var successors = result.MutatedLots.Where(lot => lot.Acquisition?.CorporateActionLineage?.PredecessorTaxLotRecordId
                == group.ExpectedLot.TaxLotRecordId).Select(lot => lot.ToOpenLot()).ToArray();
            successors.Should().HaveCount(2);
            var remainingFraction = group.ExpectedLot.OpenQuantity / group.ExpectedLot.OriginalQuantity;
            successors.Sum(lot => lot.Acquisition.TransactionCostBasis).Should().Be(group.ExpectedLot.Acquisition.TransactionCostBasis * remainingFraction);
            successors.Sum(lot => lot.Acquisition.FunctionalCostBasis).Should().Be(group.ExpectedLot.Acquisition.FunctionalCostBasis * remainingFraction);
            successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(group.ExpectedLot.OpenTransactionCostBasis);
            successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(group.ExpectedLot.OpenFunctionalCostBasis);
        }
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Select(row => row.MutationRecordId).Should().Equal(result.Mutations.Select(row => row.MutationRecordId));
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(replay.MutatedLots),
            JsonSerializer.SerializeToElement(result.MutatedLots)).Should().BeTrue();
        (await fixture.Store.GetByPeriodAsync(fixture.Base.Period.PeriodId)).Should().HaveCount(fixture.InitialJournalCount + 1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_MultipleEqualBasisPredecessors_RejectMissingForeignAndDuplicateJournalLotIdentities()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var second = await SaveAdditionalCorporateLotAsync(fixture);
        var instruction = AddCorporatePredecessor(fixture.Instruction, second.ToOpenLot());
        var command = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext).Command();
        var entry = command.Journal.Entry;
        foreach (var index in new[] { 0, 1, 3, 4 })
        {
            foreach (var lotId in new[] { null, Guid.NewGuid().ToString("D"), entry.Lines[(index + 3) % 6].Dimensions!.TaxLotId })
            {
                var lines = entry.Lines.Select((line, ordinal) => ordinal == index
                    ? new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account, line.Debit, line.Credit,
                        line.Description, line.Dimensions! with { TaxLotId = lotId }, line.Currency)
                    : line).ToArray();
                var write = command.Journal with
                { Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) };
                var normalize = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
                normalize.Should().Throw<LedgerValidationException>().WithMessage("*exact reviewed predecessor or successor lot*");
            }
        }
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [second.TaxLotRecordId])).Single().Should().BeEquivalentTo(second);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_StaleSecondPredecessor_LeavesEveryLotJournalAndAuditUnchanged()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var second = await SaveAdditionalCorporateLotAsync(fixture);
        var instruction = AddCorporatePredecessor(fixture.Instruction, second.ToOpenLot() with { Version = second.Version + 1 });
        var command = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext).Command();
        var audit = await fixture.Store.VerifyLedgerEventAuditAsync();
        var attempt = () => fixture.Store.AppendAssetPostingAsync(command);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*predecessor*changed*");
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [second.TaxLotRecordId])).Single().Should().BeEquivalentTo(second);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, instruction.AdditionalPredecessors![0].Successors
            .Select(successor => successor.TaxLotRecordId).ToArray())).Should().BeEmpty();
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(audit.ChainedEvents);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_OmittedOpenPredecessor_RefusesPartialPositionTransformation()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var second = await SaveAdditionalCorporateLotAsync(fixture);
        var command = fixture.Command();
        var attempt = () => fixture.Store.AppendAssetPostingAsync(command);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*complete open inventory*");
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [second.TaxLotRecordId])).Single().Should().BeEquivalentTo(second);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_OmittedPredecessorFullyDisposedLater_RefusesBackdatedTransformation()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var second = await SaveAdditionalCorporateLotAsync(fixture);
        var date = fixture.Instruction.EffectiveDate.AddDays(1);
        var at = DateTimeOffset.UtcNow;
        await fixture.Store.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), fixture.BookId, second.Account,
            LedgerTaxLotReliefMethod.SpecificId, "corporate-disposal-specific", date, at, at));
        var disposalJournal = CorporateInventoryJournal(fixture, second, date, assetDebit: false);
        var disposal = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, disposalJournal,
            disposalJournal.SourceEventId!.Value, disposalJournal.Entry.Metadata!.IdempotencyKey!, fixture.Base.Period.Version,
            AtomicTaxLotMutationKind.Disposal, second.Acquisition!.Evidence,
            disposalSelections: [new(second.TaxLotRecordId, second.LotId, second.Version, second.OpenQuantity,
                second.OpenQuantity, 0, second.EvidenceRef!, second.UnitCost, 6_930m)],
            reliefMethod: "SpecificId", policyRevision: "corporate-disposal-specific");
        (await fixture.Store.AppendAssetPostingAsync(disposal)).MutatedLots.Single().OpenQuantity.Should().Be(0m);
        (await fixture.Store.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, second.SecurityId, second.BookPositionId,
            fixture.Instruction.EffectiveDate)).Should().HaveCount(2);

        var command = fixture.Command();
        var audit = await fixture.Store.VerifyLedgerEventAuditAsync();
        var attempt = () => fixture.Store.AppendAssetPostingAsync(command);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot precede*source-position*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Base.Period.PeriodId)).Should().HaveCount(fixture.InitialJournalCount + 1);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Instruction.ExpectedLot.TaxLotRecordId]))
            .Single().ToOpenLot().Should().BeEquivalentTo(fixture.Instruction.ExpectedLot);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(audit.ChainedEvents);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_LateBackdatedAcquisitions_CannotReopenTransformedInventory()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var before = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Instruction.ExpectedLot.TaxLotRecordId])).Single();
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var late = AdditionalCorporateLot(before);
        var save = () => fixture.Restart().SaveTaxLotAsync(late);
        await save.Should().ThrowAsync<LedgerValidationException>().WithMessage("*retained corporate action*");
        var foreignSecurity = AdditionalCorporateLot(before) with { SecurityId = Guid.NewGuid() };
        var saveForeignSecurity = () => fixture.Restart().SaveTaxLotAsync(foreignSecurity);
        await saveForeignSecurity.Should().ThrowAsync<LedgerValidationException>().WithMessage("*retained corporate action*");
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [foreignSecurity.TaxLotRecordId])).Should().BeEmpty();
        var journal = CorporateInventoryJournal(fixture, late, fixture.Instruction.EffectiveDate, assetDebit: true);
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal,
            journal.SourceEventId!.Value, journal.Entry.Metadata!.IdempotencyKey!, fixture.Base.Period.Version,
            AtomicTaxLotMutationKind.Acquisition, late.Acquisition!.Evidence,
            acquisitionLot: late with { Version = 0, SourceJournalEntryId = journal.Entry.JournalEntryId });
        var post = () => fixture.Restart().AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*retained corporate action*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [late.TaxLotRecordId])).Should().BeEmpty();
        (await fixture.Store.GetByPeriodAsync(fixture.Base.Period.PeriodId)).Should().HaveCount(fixture.InitialJournalCount + 1);
    }

    private static async Task<LedgerTaxLotRecord> SaveAdditionalCorporateLotAsync(CorporateFixture fixture)
    {
        var source = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Instruction.ExpectedLot.TaxLotRecordId])).Single();
        return await fixture.Store.SaveTaxLotAsync(AdditionalCorporateLot(source));
    }

    private static LedgerTaxLotRecord AdditionalCorporateLot(LedgerTaxLotRecord source)
    {
        var id = Guid.NewGuid();
        var evidence = BuildEvidence("additional-source-" + id.ToString("N"), 'f') with
        { EffectiveDate = source.AcquiredDate, SubjectType = "OpenLotAcquisition", SubjectId = id.ToString("D") };
        return source with
        {
            TaxLotRecordId = id,
            LotId = "additional-source-" + id.ToString("N"),
            Version = 1,
            OriginalQuantity = 60m,
            OpenQuantity = 60m,
            OriginalFace = 6_000m,
            UnitCost = 115.5m,
            SourceJournalEntryId = null,
            EvidenceRef = evidence.EvidenceId,
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null,
            BasisAdjustment = null,
            Acquisition = source.Acquisition! with
            { TransactionCostBasis = 6_300m, FunctionalCostBasis = 6_930m, Evidence = [evidence], CorporateActionLineage = null }
        };
    }

    private static OpenLotCorporateActionInstructionDto AddCorporatePredecessor(
        OpenLotCorporateActionInstructionDto instruction, OpenLotDto source)
    {
        var successors = instruction.Successors.Select(successor =>
        {
            var id = Guid.NewGuid();
            return successor with
            {
                TaxLotRecordId = id,
                LotId = "additional-" + successor.LotId,
                AcquisitionEvidence = successor.AcquisitionEvidence with
                { EvidenceId = "additional-successor-" + id.ToString("N"), SubjectId = id.ToString("D") }
            };
        }).ToArray();
        var mutations = instruction.Mutations.Select((mutation, index) => mutation with
        { SourceLotId = source.TaxLotRecordId, ExpectedSourceLotVersion = source.Version, TargetLotId = successors[index].TaxLotRecordId });
        return instruction with
        {
            AdditionalPredecessors = [new(source, successors, instruction.SourceAssetAccountId)],
            Mutations = instruction.Mutations.Concat(mutations).ToArray()
        };
    }

    private static LedgerJournalEntryWrite CorporateInventoryJournal(CorporateFixture fixture, LedgerTaxLotRecord lot,
        DateOnly date, bool assetDebit)
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var key = "corporate-inventory:" + id.ToString("N");
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        LedgerEntry Leg(LedgerAccount account, bool debit) => new(Guid.NewGuid(), id, at, account,
            debit ? 6_930m : 0m, debit ? 0m : 6_930m, "Reviewed inventory movement", dimensions,
            new("EUR", "USD", debit ? 6_300m : 0m, debit ? 0m : 6_300m, 1.1m));
        var entry = new JournalEntry(id, at, "Reviewed inventory movement",
            [Leg(lot.Account, assetDebit), Leg(new("Cash", LedgerAccountType.Asset), !assetDebit)],
            new(SecurityId: lot.SecurityId, EffectiveDate: date, IdempotencyKey: key, Tags: SecurityMasterLineageTags(lot.SecurityId)));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Base.Period.PeriodId, date,
            at, key, AccountingPostingIntentDto.Adjustment, eventId, ExpectedVersion: fixture.Base.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Review complete inventory movement.", LedgerBookId: fixture.BookId)
        { Actor = "independent-controller" };
        return new(entry, fixture.BookId, fixture.Base.Period.PeriodId, SourceEventId: eventId, LedgerBookId: fixture.BookId,
            AccountingPolicyId: fixture.BookContext.AccountingPolicyId, AccountingPolicyVersion: fixture.BookContext.AccountingPolicyVersion,
            PostingCommand: posting);
    }
}
