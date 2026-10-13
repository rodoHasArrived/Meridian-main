using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_ReducesBothBasesPreservesAcquisitionAndReplaysAfterRestartAndLock()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await CapitalReturnCommandAsync(fixture);
        var before = fixture.Lot;
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var after = result.MutatedLots.Should().ContainSingle().Subject;
        after.ToOpenLot().Should().BeEquivalentTo(OpenLotCapitalReturn.Project(command.CapitalReturn!));
        after.OpenQuantity.Should().Be(before.OpenQuantity);
        after.Acquisition.Should().BeEquivalentTo(before.Acquisition);
        after.ToOpenLot().OpenTransactionCostBasis.Should().Be(6550m);
        after.ToOpenLot().OpenFunctionalCostBasis.Should().Be(7205m);
        result.Mutations.Single().QuantityDelta.Should().Be(0m);
        result.Mutations.Single().CostBasis.Should().Be(55m);
        result.Mutations.Single().LotBefore.Should().BeEquivalentTo(before);
        result.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(55m);
        result.Journal.Entry.Lines.Sum(line => line.Credit).Should().Be(55m);
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!.CapitalReturn
            .Should().BeEquivalentTo(command.CapitalReturn);
        var quantity = await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, before.SecurityId,
            before.BookPositionId, command.CapitalReturn!.Projection.EconomicEvent!.EffectiveDate);
        quantity.Should().ContainSingle().Which.OpenQuantity.Should().Be(before.OpenQuantity);
        await fixture.LockPeriodAsync();
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(result.Mutations);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_AfterAmortizationAndPartialRelief_UsesCurrentBasisForLaterDisposal()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var command = await CapitalReturnCommandAsync(fixture);
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var canonical = result.MutatedLots.Single().ToOpenLot();
        canonical.OpenTransactionCostBasis.Should().Be(6250m);
        canonical.OpenFunctionalCostBasis.Should().Be(6875m);
        var disposal = await fixture.CurrentBasisDisposalAsync(30m, new DateOnly(2026, 9, 1), "capital-return-later-sale");
        disposal.Relief.FunctionalCostBasis.Should().Be(3437.5m);
        disposal.Relief.TransactionCostBasis.Should().Be(3125m);
        var sold = await fixture.Restart().AppendAssetPostingAsync(disposal.Command);
        sold.MutatedLots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(3437.5m);
        sold.MutatedLots.Single().Acquisition.Should().BeEquivalentTo(fixture.Lot.Acquisition);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_StaleLotCannotAppendASecondJournal()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var winner = await CapitalReturnCommandAsync(fixture);
        var stale = await CapitalReturnCommandAsync(fixture);
        var result = await fixture.Store.AppendAssetPostingAsync(winner);
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(stale);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*changed*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(stale.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(result.MutatedLots.Single());
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_ChangedPositionOrReferenceRefusesBeforeWriting()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await CapitalReturnCommandAsync(fixture);
        var security = fixture.Security;
        await fixture.Securities.UpsertProjectionAsync(security with { Version = security.Version + 1 });
        var staleSecurity = () => fixture.Restart().AppendAssetPostingAsync(command);
        await staleSecurity.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*");
        await fixture.Securities.UpsertProjectionAsync(security);
        await fixture.AdvancePositionAsync();
        var stalePosition = () => fixture.Restart().AppendAssetPostingAsync(command);
        await stalePosition.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_SourceActionCannotBeReappliedWithNewCaseAndCurrentLotVersion()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var first = await CapitalReturnCommandAsync(fixture);
        await fixture.Store.AppendAssetPostingAsync(first);
        var duplicate = await CapitalReturnCommandAsync(fixture,
            OpenLotSuccessors.GetSourceCorporateActionId(first.CapitalReturn!.Projection));
        duplicate.SourceEventId.Should().NotBe(first.SourceEventId);
        duplicate.CapitalReturn!.ExpectedLot.Version.Should().Be(first.CapitalReturn.ExpectedLot.Version + 1);
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(duplicate);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*already reduced*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(duplicate.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_FailureAfterLotUpdateRollsBackJournalBatchAndBasis_ThenRetries()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await CapitalReturnCommandAsync(fixture);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        var schema = fixture.Options.SchemaName;
        await using (var fault = connection.CreateCommand())
        {
            fault.CommandText = $"""
                create function {schema}.reject_capital_return() returns trigger language plpgsql as $body$
                begin raise exception 'Injected capital-return receipt failure'; end $body$;
                create trigger reject_capital_return before insert on {schema}.tax_lot_mutations
                for each row when (new.mutation_kind = 'CapitalReturn') execute function {schema}.reject_capital_return();
                """;
            await fault.ExecuteNonQueryAsync();
        }
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(command);
        await attempt.Should().ThrowAsync<PostgresException>().WithMessage("*Injected capital-return*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(fixture.Lot);
        await using (var removeFault = connection.CreateCommand())
        {
            removeFault.CommandText = $"drop trigger reject_capital_return on {schema}.tax_lot_mutations;";
            await removeFault.ExecuteNonQueryAsync();
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CapitalReturn_JournalOnlyAppendAndChangedReplayRefuseWithoutFurtherMutation()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await CapitalReturnCommandAsync(fixture);
        var journalOnly = () => fixture.Store.AppendAsync(command.Journal);
        await journalOnly.Should().ThrowAsync<LedgerValidationException>().WithMessage("*atomic journal/lot*");
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var changed = command with { RetainedEvidence = [.. command.RetainedEvidence, BuildEvidence("different-review", 'd')] };
        var collision = () => fixture.Restart().AppendAssetPostingAsync(changed.WithComputedFingerprint());
        await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*collision*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(result.MutatedLots.Single());
    }

    private static async Task<AtomicTaxLotJournalCommand> CapitalReturnCommandAsync(AmortFixture fixture, Guid? actionId = null)
    {
        var lot = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single().ToOpenLot();
        var position = (await fixture.Positions.GetSecurityAsync(lot.SecurityId)).BookPositions.Single();
        var instruction = CapitalReturnTestData.Build(lot, 50m, 55m, position.Version, fixture.Security.Version,
            OpenLotAmortization.SecurityHash(fixture.Security), fixture.Period.PeriodId, fixture.Period.Version,
            "amort-fund", actionId: actionId, accountingBasis: position.BookContext.AccountingBasis);
        var request = CapitalReturnTestData.MapRequest(instruction);
        var retained = request.RetainedEvidence.ToArray();
        var eventRef = request.Projection.EconomicEvent! with
        { RetainedEvidence = retained, EvidenceLinks = retained.Select(item => item.EvidenceUri).ToArray() };
        var original = fixture.Command();
        var key = "capital-return:" + Guid.NewGuid().ToString("N");
        var journalId = Guid.NewGuid();
        var date = eventRef.EffectiveDate;
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        var tags = new Dictionary<string, string>(SecurityMasterLineageTags(lot.SecurityId))
        { [OpenLotCapitalReturn.JournalFingerprintTag] = OpenLotCapitalReturn.Fingerprint(instruction) };
        var entry = new JournalEntry(journalId, at, "Approved return of capital",
            [new LedgerEntry(Guid.NewGuid(), journalId, at, AmortAccount, 0m, 55m, "Carrying reduction", dimensions, new("USD", "USD", 0m, 55m, 1m)),
             new LedgerEntry(Guid.NewGuid(), journalId, at, LedgerAccounts.Cash, 55m, 0m, "Cash return", dimensions, new("USD", "USD", 55m, 0m, 1m))],
            new(SecurityId: lot.SecurityId, EffectiveDate: date, IdempotencyKey: key, Tags: tags));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventRef.EventId, ExpectedVersion: fixture.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-capital-return-review",
            OperatorRationale: "Reviewed carrying reduction and cash return", LedgerBookId: fixture.BookId)
        {
            Actor = "independent-controller",
            LotCapitalReturn = instruction,
            SourceEventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            BookContext = position.BookContext with { PeriodId = fixture.Period.PeriodId },
            BookPositionId = lot.BookPositionId,
            EconomicEvent = eventRef,
            ProjectionLineage = instruction.Projection.ProjectionLineage! with
            { TriggerEvent = eventRef, BookPositionId = lot.BookPositionId, RetainedEvidence = retained, EvidenceLinks = eventRef.EvidenceLinks },
            RulePackReference = new("canonical-capital-return", "1", "capital-return", "1"),
            Evidence = retained.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var journal = original.Journal with
        {
            Entry = entry,
            SourceEventId = eventRef.EventId,
            PostingCommand = posting,
            RuleId = "capital-return",
            RuleVersion = "1"
        };
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, eventRef.EventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.CapitalReturn, retained, capitalReturn: instruction);
    }
}
