using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Fact]
    public async Task Acquisition_CannotInjectSuccessorBasisAdjustmentToChangeOpeningBasis()
    {
        var command = BuildAcquisitionCommand();
        command = command with
        {
            AcquisitionLot = command.AcquisitionLot! with
            {
                BasisAdjustment = new OpenLotBasisAdjustmentDto(command.MutationBatchId,
                    OpenLotBasisAdjustmentReasons.CorporateActionSuccessor, command.AcquisitionLot!.OpenQuantity, 1m, 1m)
            }
        };
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions
        { ConnectionString = "Host=invalid.invalid;Database=unused;Username=unused;Timeout=1" });
        var inject = () => store.AppendAssetPostingAsync(command.WithComputedFingerprint());
        await inject.Should().ThrowAsync<LedgerValidationException>().WithMessage("*basis adjustment*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_Exchange_ClosesPredecessorPreservesBasisAndReplaysAfterRestart()
        => await AssertSuccessorRoundTripAsync(advanceRefunding: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdvanceRefundingOpenLotScenario_TwoSuccessorsConserveBothBasesAndOnlyRefundedCarriesScheduleD()
        => await AssertSuccessorRoundTripAsync(advanceRefunding: true);

    private static async Task AssertSuccessorRoundTripAsync(bool advanceRefunding)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding);
        var expected = command.CorporateAction!;
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        result.IsExactReplay.Should().BeFalse();
        CanonicalOpenLotSuccessorEvidence.Validate(result, result.Journal, fixture.BookId, "USD");
        result.Mutations.Should().HaveCount(advanceRefunding ? 3 : 2);
        var parent = result.Mutations.Single(m => m.LotBefore is not null);
        parent.LotBefore!.ToOpenLot().Should().BeEquivalentTo(expected.ExpectedLot);
        parent.LotAfter.OpenQuantity.Should().Be(0m);
        parent.LotAfter.Version.Should().Be(expected.ExpectedLot.Version + 1);
        parent.LotAfter.Acquisition.Should().BeEquivalentTo(parent.LotBefore!.Acquisition);
        var successors = result.Mutations.Where(m => m.LotBefore is null).Select(m => m.LotAfter.ToOpenLot()).ToArray();
        successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(6_600m);
        successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(7_260m);
        successors.Sum(lot => lot.OpenQuantity).Should().Be(6_000m);
        foreach (var target in expected.Successors)
        {
            var actual = successors.Single(lot => lot.TaxLotRecordId == target.Lot.TaxLotRecordId);
            actual.Should().BeEquivalentTo(target.Lot);
            actual.AcquiredDate.Should().Be(expected.ExpectedLot.AcquiredDate);
            actual.Acquisition.HoldingPeriodStartDate.Should().Be(expected.ExpectedLot.Acquisition.HoldingPeriodStartDate);
            actual.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
            actual.Acquisition.AcquisitionCurrency.Should().Be("EUR");
            actual.Acquisition.FunctionalCurrency.Should().Be("USD");
            actual.Acquisition.FaceValueTerms.Should().BeEquivalentTo(expected.ExpectedLot.Acquisition.FaceValueTerms);
            actual.Acquisition.Evidence.Should().Contain(expected.ExpectedLot.Acquisition.Evidence);
        }
        if (advanceRefunding)
        {
            successors.Select(lot => lot.OpenTransactionCostBasis).Order().Should().Equal(2_640m, 3_960m);
            successors.Select(lot => lot.OpenFunctionalCostBasis).Order().Should().Equal(2_904m, 4_356m);
            var mutations = result.CorporateAction!.Projection.LotMutations!.Mutations;
            mutations.Single(m => m.ReportingTags.Contains("ScheduleD")).TargetLotId
                .Should().Be(expected.Successors[0].Lot.TaxLotRecordId);
            mutations.Single(m => m.TargetLotId == expected.Successors[1].Lot.TaxLotRecordId)
                .ReportingTags.Should().BeEmpty();
        }
        result.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(7_260m);
        result.Journal.Entry.Lines.Sum(line => line.Credit).Should().Be(7_260m);
        var reloaded = (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        reloaded.Mutations.Should().BeEquivalentTo(result.Mutations);
        await AssertDurableSuccessorImpactAsync(fixture, reloaded);
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(reloaded.CorporateAction),
            JsonSerializer.SerializeToElement(expected)).Should().BeTrue();
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId,
            result.MutatedLots.Select(lot => lot.TaxLotRecordId).ToArray()))
            .Should().BeEquivalentTo(result.MutatedLots);
        foreach (var target in expected.Successors)
        {
            (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, target.Lot.SecurityId,
                target.Lot.BookPositionId, expected.Projection.EconomicEvent!.EffectiveDate.AddDays(-1))).Should().BeEmpty();
            (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, target.Lot.SecurityId,
                target.Lot.BookPositionId, expected.Projection.EconomicEvent.EffectiveDate)).Should().ContainSingle();
        }

        await fixture.LockPeriodAsync();
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(result.Mutations);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdvanceRefundingOpenLotScenario_AmortizedCurrentBasisIsAllocatedSeparatelyFromAcquisitionBasis()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        CanonicalOpenLotSuccessorEvidence.Validate(result, result.Journal, fixture.BookId, "USD");
        var successors = result.Mutations.Where(mutation => mutation.LotBefore is null)
            .Select(mutation => mutation.LotAfter.ToOpenLot()).ToArray();
        successors.Sum(lot => lot.Acquisition.TransactionCostBasis).Should().Be(6_600m);
        successors.Sum(lot => lot.Acquisition.FunctionalCostBasis).Should().Be(7_260m);
        successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(6_300m);
        successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(6_930m);
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_StaleSourceAndTargetReferences_RefuseWithoutAnyWrite()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var targetId = command.CorporateAction!.Successors[0].Lot.SecurityId;
        var target = (await fixture.Securities.GetProjectionAsync(targetId))!;
        await fixture.Securities.UpsertProjectionAsync(target with { Version = target.Version + 1 });
        var staleTarget = () => fixture.Restart().AppendAssetPostingAsync(command);
        await staleTarget.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
        await fixture.Securities.UpsertProjectionAsync(target);

        await fixture.AdvancePositionAsync();
        var staleSource = () => fixture.Restart().AppendAssetPostingAsync(command);
        await staleSource.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_StaleLotAfterAnotherPosting_RefusesWithoutAnotherJournal()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var winner = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var stale = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        await fixture.Store.AppendAssetPostingAsync(winner);
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(stale);
        await attempt.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(stale.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_ConcurrentDuplicateCommands_CommitOneBatchAndRetryExactly()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        async Task<AtomicTaxLotJournalResult?> AttemptAsync()
        {
            try
            { return await fixture.Restart().AppendAssetPostingAsync(command); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
            { return null; }
        }
        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        results.Count(result => result is { IsExactReplay: false }).Should().Be(1);
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().HaveCount(3);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        var history = await fixture.Restart().GetOpenLotSuccessorHistoryAsync(fixture.BookId,
            [replay.Journal.Entry.JournalEntryId]);
        history.Should().ContainSingle().Which.Mutations.Should().BeEquivalentTo(replay.Mutations);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_JournalOnlyAppend_RefusesWithoutAnyWrite()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var bypass = () => fixture.Store.AppendAsync(command.Journal);
        await bypass.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_StalePeriodAndTargetPosition_RefuseWithoutAnyWrite()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var target = command.CorporateAction!.Successors[1].Lot;
        var snapshot = await fixture.Positions.GetSecurityAsync(target.SecurityId);
        var position = snapshot.BookPositions.Single();
        await fixture.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), position with { Version = position.Version + 1 },
            null, position.Version, new("independent-controller", "evidence://target-revision", "Review target revision", DateTimeOffset.UtcNow));
        var stalePosition = () => fixture.Store.AppendAssetPostingAsync(command);
        await stalePosition.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
        await fixture.LockPeriodAsync();
        var stalePeriod = () => fixture.Store.AppendAssetPostingAsync(command);
        await stalePeriod.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_FailureOnLastMutation_RollsBackJournalAndEveryLotThenRetrySucceeds()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Options.SchemaName}".reject_test_successor() returns trigger
            language plpgsql as $$ begin raise exception 'injected final successor mutation failure'; end $$;
            create trigger reject_test_successor before insert on "{fixture.Options.SchemaName}".tax_lot_mutations
            for each row when (new.tax_lot_record_id = '{command.CorporateAction!.Successors.OrderBy(target => target.Lot.TaxLotRecordId).Last().Lot.TaxLotRecordId:D}'::uuid)
            execute function "{fixture.Options.SchemaName}".reject_test_successor();
            """;
        await injection.ExecuteNonQueryAsync();
        var fail = () => fixture.Store.AppendAssetPostingAsync(command);
        await fail.Should().ThrowAsync<PostgresException>().WithMessage("*injected final successor mutation failure*");
        await AssertSuccessorUnchangedAsync(fixture, command);
        injection.CommandText = $"drop trigger reject_test_successor on \"{fixture.Options.SchemaName}\".tax_lot_mutations";
        await injection.ExecuteNonQueryAsync();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_ChangedReplayPayloadAndJournalAllocation_AreRejected()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var lines = command.Journal.Entry.Lines.ToArray();
        var first = lines[0];
        var second = lines[1];
        // Preserve balance while moving a whole EUR of basis to the wrong successor.
        lines[0] = new(first.EntryId, first.JournalEntryId, first.Timestamp, first.Account,
            first.Debit + 1.1m, first.Credit, first.Description, first.Dimensions,
            new LedgerEntryCurrency("EUR", "USD", first.Currency!.TransactionDebit + 1m, 0m, 1.1m));
        lines[1] = new(second.EntryId, second.JournalEntryId, second.Timestamp, second.Account,
            second.Debit - 1.1m, second.Credit, second.Description, second.Dimensions,
            new LedgerEntryCurrency("EUR", "USD", second.Currency!.TransactionDebit - 1m, 0m, 1.1m));
        var wrong = command with
        {
            Journal = command.Journal with
            {
                Entry = new JournalEntry(
            command.Journal.Entry.JournalEntryId, command.Journal.Entry.Timestamp, command.Journal.Entry.Description,
            lines, command.Journal.Entry.Metadata)
            }
        };
        var invalidAllocation = () => fixture.Store.AppendAssetPostingAsync(wrong.WithComputedFingerprint());
        await invalidAllocation.Should().ThrowAsync<LedgerValidationException>();
        await AssertSuccessorUnchangedAsync(fixture, command);
        await fixture.Store.AppendAssetPostingAsync(command);
        var changedEvidence = command with
        {
            RetainedEvidence = command.RetainedEvidence
            .Select((item, index) => index == 0 ? item with { ContentHashSha256 = new string('f', 64) } : item).ToArray()
        };
        var collision = () => fixture.Restart().AppendAssetPostingAsync(changedEvidence.WithComputedFingerprint());
        await collision.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    private static async Task AssertSuccessorUnchangedAsync(AmortFixture fixture, AtomicTaxLotJournalCommand command)
    {
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        var allIds = new[] { command.CorporateAction!.ExpectedLot.TaxLotRecordId }
            .Concat(command.CorporateAction.Successors.Select(target => target.Lot.TaxLotRecordId)).ToArray();
        var lots = await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, allIds);
        lots.Should().ContainSingle();
        lots.Single().ToOpenLot().Should().BeEquivalentTo(command.CorporateAction.ExpectedLot);
    }

    private static async Task AssertDurableSuccessorImpactAsync(AmortFixture fixture, AtomicTaxLotJournalResult batch)
    {
        var instruction = batch.CorporateAction!;
        var projected = instruction.Projection;
        var source = instruction.ExpectedLot;
        var journal = batch.Journal;
        var impact = new PostedJournalImpactDto(journal.Entry.JournalEntryId, fixture.BookId,
            fixture.Period.PeriodId, journal.AccountingBasis, journal.CreatedAt, JournalPostingStatusDto.Posted,
            "USD", journal.Entry.Lines.Sum(line => line.Debit), journal.Entry.Lines.Sum(line => line.Credit),
            journal.Entry.Lines.Select(line => new PostedJournalImpactLineDto(line.EntryId, line.Account.ToString(),
                line.Debit, line.Credit, line.Currency!.FunctionalCurrency, line.Description,
                LedgerDimensionMapper.ToDto(line.Dimensions))).ToArray());
        var scope = new AssetAccountingEventScopeDto(source.SecurityId, instruction.ExpectedSecurityVersion,
            source.BookPositionId, projected.LotMutations!.ExpectedPositionVersion, fixture.BookId, fixture.Period.PeriodId,
            projected.Treatment.AccountingBasis, "amort-fund", "tenant-alpha", "company-alpha");
        var spine = new AssetAccountingEventSpineDto(projected.EconomicEvent!.EventId,
            AssetAccountingEventKindDto.CorporateAction, 1, 5, projected.EconomicEvent.EffectiveDate,
            projected.EventAmount, "USD", scope, projected.EconomicEvent, projected.ProjectionLineage!,
            RetainedEvidence: batch.RetainedEvidence, PostedJournalImpact: impact, TaxLotMutationBatchId: batch.MutationBatchId,
            DraftedLotMutation: new(AssetLotMutationIntentDto.CorporateAction, CorporateAction: instruction), CorporateAction: instruction);
        await AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(spine, fixture.Restart(), CancellationToken.None);
        var missing = () => AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(
            spine with { TaxLotMutationBatchId = null }, fixture.Restart(), CancellationToken.None);
        await missing.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<AtomicTaxLotJournalCommand> SuccessorCommandAsync(AmortFixture fixture, bool advanceRefunding)
    {
        var predecessor = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single().ToOpenLot();
        var origin = await fixture.Positions.GetSecurityAsync(predecessor.SecurityId);
        var sourcePosition = origin.BookPositions.Single();
        var basis = advanceRefunding ? AccountingBasisKindDto.Statutory : AccountingBasisKindDto.Gaap;
        var book = (await fixture.Store.GetLedgerBookAsync(fixture.BookId))!;
        if (book.AccountingBasis != basis)
        {
            await fixture.Store.SaveLedgerBookAsync(book with { AccountingBasis = basis });
            sourcePosition = await fixture.Positions.UpsertAsync(origin.InstrumentRoles.Single(), sourcePosition with
            { BookContext = sourcePosition.BookContext with { AccountingBasis = basis }, Version = sourcePosition.Version + 1 },
                null, sourcePosition.Version, new("independent-controller", "evidence://successor-basis",
                    "Review action accounting basis", DateTimeOffset.UtcNow));
        }
        var targets = new List<OpenLotSuccessorTargetDto>();
        foreach (var fraction in advanceRefunding ? new[] { 0.6m, 0.4m } : new[] { 1m })
        {
            var security = AmortSecurity(Guid.NewGuid()) with { PrimaryIdentifierValue = "SUCCESSOR-" + Guid.NewGuid().ToString("N") };
            await fixture.Securities.UpsertProjectionAsync(security);
            security = (await fixture.Securities.GetProjectionAsync(security.SecurityId))!;
            var positionId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            var eventRef = sourcePosition.OriginEvent! with { SecurityId = security.SecurityId, BookPositionId = positionId };
            var role = origin.InstrumentRoles.Single() with { RoleId = roleId, SecurityId = security.SecurityId, OriginEvent = eventRef };
            var position = sourcePosition with
            {
                PositionId = positionId,
                SecurityId = security.SecurityId,
                RoleId = roleId,
                Version = 1,
                OriginEvent = eventRef
            };
            await fixture.Positions.UpsertAsync(role, position, null, 0,
                new("independent-controller", "evidence://successor-position", "Review successor identity", DateTimeOffset.UtcNow));
            var lotId = Guid.NewGuid();
            var evidence = BuildEvidence("successor-acquisition-" + lotId.ToString("N"), 'd') with
            { SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D"), EffectiveDate = predecessor.AcquiredDate };
            var openFraction = predecessor.OpenQuantity / predecessor.OriginalQuantity;
            var acquisition = predecessor.Acquisition with
            {
                TransactionCostBasis = predecessor.Acquisition.TransactionCostBasis * openFraction * fraction,
                FunctionalCostBasis = predecessor.Acquisition.FunctionalCostBasis * openFraction * fraction,
                Evidence = [.. predecessor.Acquisition.Evidence, evidence]
            };
            var lot = new OpenLotDto(lotId, security.SecurityId, positionId, predecessor.LedgerBookId,
                "successor-" + lotId.ToString("N"), predecessor.AcquiredDate, predecessor.OpenQuantity * fraction,
                predecessor.OpenQuantity * fraction, predecessor.OpenTransactionCostBasis * fraction,
                predecessor.OpenFunctionalCostBasis * fraction, 1, acquisition);
            targets.Add(new(lot, 1, security.Version, OpenLotAmortization.SecurityHash(security)));
        }
        var instruction = OpenLotSuccessorTestData.Build(predecessor, targets, advanceRefunding,
            sourcePosition.Version, fixture.Security.Version, fixture.Period.PeriodId,
            fundProfileId: "amort-fund", expectedPeriodVersion: fixture.Period.Version) with
        { ExpectedSecurityHash = OpenLotAmortization.SecurityHash(fixture.Security) };
        var eventId = instruction.Projection.EconomicEvent!.EventId;
        var key = "successor-posting:" + Guid.NewGuid().ToString("N");
        var journalId = Guid.NewGuid();
        var date = instruction.Projection.EconomicEvent.EffectiveDate;
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var lines = targets.Select(target => new LedgerEntry(Guid.NewGuid(), journalId, at, AmortAccount,
            target.Lot.OpenFunctionalCostBasis, 0m, "Approved cashless successor allocation",
            new LedgerLineDimensionSet(InstrumentId: target.Lot.SecurityId) { PositionId = target.Lot.BookPositionId },
            new LedgerEntryCurrency("EUR", "USD", target.Lot.OpenTransactionCostBasis, 0m, 1.1m))).ToList();
        lines.Add(new(Guid.NewGuid(), journalId, at, AmortAccount, 0m, predecessor.OpenFunctionalCostBasis, "Approved cashless successor allocation",
            new LedgerLineDimensionSet(InstrumentId: predecessor.SecurityId) { PositionId = predecessor.BookPositionId },
            new LedgerEntryCurrency("EUR", "USD", 0m, predecessor.OpenTransactionCostBasis, 1.1m)));
        var tags = new Dictionary<string, string>(SecurityMasterLineageTags(predecessor.SecurityId))
        { ["openLotSuccessorInstructionFingerprint"] = OpenLotSuccessors.Fingerprint(instruction) };
        var entry = new JournalEntry(journalId, at, "Approved cashless successor allocation", lines,
            new(SecurityId: predecessor.SecurityId, EffectiveDate: date, IdempotencyKey: key,
                Tags: tags));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventId, ExpectedVersion: fixture.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Independently reviewed corporate-action basis allocation.", LedgerBookId: fixture.BookId)
        { Actor = "independent-controller", LotCorporateAction = instruction };
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingBasis: basis, SourceEventId: eventId, LedgerBookId: fixture.BookId, PostingCommand: posting);
        var retained = predecessor.Acquisition.Evidence.Concat(targets.SelectMany(t => t.Lot.Acquisition.Evidence))
            .Concat(instruction.Projection.EvidenceManifest.Select(item => BuildEvidence(item.EvidenceId, 'a') with
            {
                EvidenceUri = item.EvidenceUri,
                ContentHashSha256 = item.ContentHashSha256,
                EvidenceVersion = item.EvidenceVersion,
                SubjectType = item.SubjectType,
                SubjectId = item.SubjectId,
                EffectiveDate = date
            })).DistinctBy(item => item.EvidenceId).ToArray();
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, eventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.CorporateAction, retained, corporateAction: instruction);
    }
}
