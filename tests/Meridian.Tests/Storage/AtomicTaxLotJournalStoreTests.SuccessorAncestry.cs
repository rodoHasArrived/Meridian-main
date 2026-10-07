using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_SplitChainRejectsRepeatedSourceActionAfterRestartAndReplaysOriginal()
        => await AssertSplitAncestryRefusalAsync(legacyProjection: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_LegacySplitChainUsesRetainedSourceManifestToRejectRepeatedAction()
        => await AssertSplitAncestryRefusalAsync(legacyProjection: true);

    private static async Task AssertSplitAncestryRefusalAsync(bool legacyProjection)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var source = await AcquireSplitAncestryUnitsAsync(fixture);
        var sourceActionA = Guid.NewGuid();
        var sourceActionB = Guid.NewGuid();
        var date = OpenLotSuccessorTestData.EffectiveDate;
        var actionA = await SplitAncestryCommandAsync(fixture, source, sourceActionA, date, 2m, legacyProjection);
        var postedA = await fixture.Store.AppendAssetPostingAsync(actionA);
        var successorA = postedA.Mutations.Single(mutation => mutation.LotBefore is null).LotAfter;
        successorA.ToOpenLot().OpenQuantity.Should().Be(120m);
        var actionB = await SplitAncestryCommandAsync(fixture, successorA, sourceActionB, date.AddDays(1), 0.5m, legacyProjection);
        var postedB = await fixture.Store.AppendAssetPostingAsync(actionB);
        var successorB = postedB.Mutations.Single(mutation => mutation.LotBefore is null).LotAfter;
        successorB.ToOpenLot().OpenQuantity.Should().Be(60m);

        // Re-review the first source action against the later lot: its new case, projected event,
        // date and lot versions are valid, but immutable ancestry must still identify source A.
        var repeatedA = await SplitAncestryCommandAsync(fixture, successorB, sourceActionA, date.AddDays(2), 2m, legacyProjection,
            sourceEventVersion: 2);
        var validate = () => OpenLotSuccessors.Validate(repeatedA.CorporateAction!);
        validate.Should().NotThrow();
        repeatedA.CorporateAction!.Projection.CaseId.Should().NotBe(actionA.CorporateAction!.Projection.CaseId);
        repeatedA.SourceEventId.Should().NotBe(actionA.SourceEventId);
        repeatedA.CorporateAction.Projection.EconomicEvent!.EventVersion.Should().Be(2);
        repeatedA.CorporateAction.Projection.EconomicEvent!.EffectiveDate.Should().Be(date.AddDays(2));
        repeatedA.CorporateAction.Projection.EvidenceManifest.Single(item => item.Role == CorporateActionProjectionEvidenceRoleDto.SourceEvent)
            .SubjectId.Should().Be(sourceActionA.ToString("D"));
        if (legacyProjection)
        {
            actionA.CorporateAction.Projection.SourceCorporateActionId.Should().BeNull();
            actionB.CorporateAction!.Projection.SourceCorporateActionId.Should().BeNull();
            repeatedA.CorporateAction.Projection.SourceCorporateActionId.Should().BeNull();
        }

        var retainedIds = new[] { source.TaxLotRecordId, successorA.TaxLotRecordId, successorB.TaxLotRecordId };
        var before = await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, retainedIds);
        var countsBefore = await ReadSplitAncestryCountsAsync(fixture);
        var restarted = fixture.Restart();
        var repeat = () => restarted.AppendAssetPostingAsync(repeatedA);

        await repeat.Should().ThrowAsync<LedgerValidationException>();

        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(countsBefore,
            "an ancestry refusal must not append a journal, mutation batch, lot mutation or successor lot");
        (await restarted.GetTaxLotsByIdsAsync(fixture.BookId, retainedIds)).Should().BeEquivalentTo(before);
        (await restarted.GetAtomicTaxLotPostingAsync(repeatedA.MutationBatchId)).Should().BeNull();
        (await restarted.GetTaxLotsByIdsAsync(fixture.BookId,
            repeatedA.CorporateAction.Successors.Select(target => target.Lot.TaxLotRecordId).ToArray())).Should().BeEmpty();

        // A retry of an already committed command still replays even after its successor was
        // consumed. A distinct source action remains admissible on the current descendant.
        var replayA = await restarted.AppendAssetPostingAsync(actionA);
        replayA.IsExactReplay.Should().BeTrue();
        replayA.Mutations.Should().BeEquivalentTo(postedA.Mutations);
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(countsBefore);
        var actionC = await SplitAncestryCommandAsync(fixture, successorB, Guid.NewGuid(), date.AddDays(2), 2m, legacyProjection);
        var postedC = await restarted.AppendAssetPostingAsync(actionC);
        postedC.IsExactReplay.Should().BeFalse();
        var successorC = postedC.Mutations.Single(mutation => mutation.LotBefore is null).LotAfter.ToOpenLot();
        successorC.OpenQuantity.Should().Be(120m);
        successorC.OpenTransactionCostBasis.Should().Be(600m);
        successorC.OpenFunctionalCostBasis.Should().Be(660m);
        (await restarted.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(4);
    }

    private static async Task<LedgerTaxLotRecord> AcquireSplitAncestryUnitsAsync(AmortFixture fixture)
    {
        var date = AmortAsOf;
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var lotId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var key = "split-ancestry-acquisition:" + Guid.NewGuid().ToString("N");
        var evidence = BuildEvidence("split-ancestry-acquisition-" + lotId.ToString("N"), 'e') with
        { SubjectType = "OpenLotAcquisition", SubjectId = lotId.ToString("D"), EffectiveDate = date };
        var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Units, "EUR", "USD", 1.1m,
            600m, 660m, date, null, [evidence]);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: fixture.Security.SecurityId)
        { PositionId = fixture.PositionId };
        var entry = new JournalEntry(journalId, at, "Reviewed unit acquisition for split ancestry",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, at, AmortAccount, 660m, 0m,
                    "Reviewed unit acquisition", dimensions, new LedgerEntryCurrency("EUR", "USD", 600m, 0m, 1.1m)),
                new LedgerEntry(Guid.NewGuid(), journalId, at, new("Cash", LedgerAccountType.Asset), 0m, 660m,
                    "Reviewed unit acquisition", dimensions, new LedgerEntryCurrency("EUR", "USD", 0m, 600m, 1.1m))
            ], new(SecurityId: fixture.Security.SecurityId, EffectiveDate: date, IdempotencyKey: key,
                Tags: SecurityMasterLineageTags(fixture.Security.SecurityId)));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Originating, sourceId, ExpectedVersion: fixture.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Independent review of a fresh canonical unit acquisition.", LedgerBookId: fixture.BookId)
        { Actor = "independent-controller" };
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1", SourceEventId: sourceId,
            LedgerBookId: fixture.BookId, PostingCommand: posting);
        var lot = new LedgerTaxLotRecord(lotId, fixture.BookId, AmortAccount, "split-ancestry-source-" + lotId.ToString("N"),
            date, 60m, 60m, 11m, "USD", at, at, journalId, evidence.EvidenceId, 1, batchId, batchId,
            fixture.Security.SecurityId, fixture.PositionId, Acquisition: acquisition);
        var command = AtomicTaxLotJournalCommand.Create(batchId, fixture.BookId, journal, sourceId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.Acquisition, [evidence], acquisitionLot: lot);
        return (await fixture.Store.AppendAssetPostingAsync(command)).MutatedLots.Single();
    }

    private static async Task<AtomicTaxLotJournalCommand> SplitAncestryCommandAsync(AmortFixture fixture,
        LedgerTaxLotRecord retained, Guid sourceActionId, DateOnly date, decimal ratio, bool legacyProjection,
        long sourceEventVersion = 1)
    {
        var predecessor = retained.ToOpenLot();
        var snapshot = await fixture.Positions.GetSecurityAsync(predecessor.SecurityId);
        var position = snapshot.BookPositions.Single(item => item.PositionId == predecessor.BookPositionId);
        var book = (await fixture.Store.GetLedgerBookAsync(fixture.BookId))!;
        if (book.AccountingBasis != AccountingBasisKindDto.Gaap)
        {
            await fixture.Store.SaveLedgerBookAsync(book with { AccountingBasis = AccountingBasisKindDto.Gaap });
            book = (await fixture.Store.GetLedgerBookAsync(fixture.BookId))!;
            position = await fixture.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(item => item.RoleId == position.RoleId),
                position with { BookContext = position.BookContext with { AccountingBasis = AccountingBasisKindDto.Gaap }, Version = position.Version + 1 },
                null, position.Version, new("independent-controller", "evidence://split-ancestry-basis",
                    "Review canonical split accounting basis", DateTimeOffset.UtcNow));
        }
        var security = (await fixture.Securities.GetProjectionAsync(predecessor.SecurityId))!;
        var targetId = Guid.NewGuid();
        var evidence = BuildEvidence("split-ancestry-successor-" + targetId.ToString("N"), 'd') with
        { SubjectType = "OpenLotAcquisition", SubjectId = targetId.ToString("D"), EffectiveDate = predecessor.AcquiredDate };
        var remaining = predecessor.OpenQuantity / predecessor.OriginalQuantity;
        var target = new OpenLotDto(targetId, predecessor.SecurityId, predecessor.BookPositionId, fixture.BookId,
            "split-ancestry-successor-" + targetId.ToString("N"), predecessor.AcquiredDate,
            predecessor.OpenQuantity * ratio, predecessor.OpenQuantity * ratio,
            predecessor.OpenTransactionCostBasis, predecessor.OpenFunctionalCostBasis, 1,
            predecessor.Acquisition with
            {
                TransactionCostBasis = predecessor.Acquisition.TransactionCostBasis * remaining,
                FunctionalCostBasis = predecessor.Acquisition.FunctionalCostBasis * remaining,
                CorporateActionLineage = null,
                Evidence = [.. predecessor.Acquisition.Evidence, evidence]
            });
        var instruction = OpenLotSuccessorTestData.Build(predecessor,
            [new(target, position.Version, security.Version, OpenLotAmortization.SecurityHash(security))],
            expectedPositionVersion: position.Version, expectedSecurityVersion: security.Version,
            periodId: fixture.Period.PeriodId, expectedSecurityHash: OpenLotAmortization.SecurityHash(security),
            fundProfileId: "amort-fund", expectedPeriodVersion: fixture.Period.Version,
            actionType: ratio > 1m ? CorporateActionAccountingTypeDto.StockSplit : CorporateActionAccountingTypeDto.ReverseStockSplit,
            policyInputs: new(CarryHoldingPeriod: true), splitRatio: ratio, actionId: sourceActionId, effectiveDate: date,
            sourceEventVersion: sourceEventVersion);
        if (legacyProjection)
            instruction = instruction with { Projection = instruction.Projection with { SourceCorporateActionId = null } };
        var mapRequest = OpenLotSuccessorTestData.MapRequest(instruction);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(mapRequest);
        mapped.Blockers.Should().BeEmpty();
        var projection = instruction.Projection;
        var eventId = projection.EconomicEvent!.EventId;
        var key = "split-ancestry-posting:" + Guid.NewGuid().ToString("N");
        var journalId = Guid.NewGuid();
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        LedgerLineDimensionSet Dimensions(OpenLotDto lot) => new(FundId: "amort-fund", InstrumentId: lot.SecurityId,
            BookId: fixture.BookId.ToString("D"), TaxLotId: lot.LotId) { PositionId = lot.BookPositionId };
        var entry = new JournalEntry(journalId, at, "Independently reviewed canonical split",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, at, retained.Account, target.OpenFunctionalCostBasis, 0m,
                    "Carry basis to whole-unit split successor", Dimensions(target),
                    new LedgerEntryCurrency("EUR", "USD", target.OpenTransactionCostBasis, 0m, 1.1m)),
                new LedgerEntry(Guid.NewGuid(), journalId, at, retained.Account, 0m, predecessor.OpenFunctionalCostBasis,
                    "Relieve split predecessor", Dimensions(predecessor),
                    new LedgerEntryCurrency("EUR", "USD", 0m, predecessor.OpenTransactionCostBasis, 1.1m))
            ], new(SecurityId: predecessor.SecurityId, EffectiveDate: date, IdempotencyKey: key,
                Tags: new Dictionary<string, string>(SecurityMasterLineageTags(predecessor.SecurityId))
                { [OpenLotSuccessors.JournalFingerprintTag] = OpenLotSuccessors.Fingerprint(instruction) }));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventId, ExpectedVersion: fixture.Period.Version,
            SourceEventType: AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Independent review of canonical split lineage and carrying basis.", LedgerBookId: fixture.BookId)
        {
            Actor = "independent-controller", LotCorporateAction = instruction,
            BookContext = position.BookContext with { PeriodId = fixture.Period.PeriodId },
            BookPositionId = position.PositionId, EconomicEvent = projection.EconomicEvent,
            ProjectionLineage = projection.ProjectionLineage, RulePackReference = mapRequest.MappedEffect.AccountingRulePack,
            Evidence = mapRequest.RetainedEvidence.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: book.AccountingPolicyId,
            AccountingPolicyVersion: book.AccountingPolicyVersion, SourceEventId: eventId, LedgerBookId: fixture.BookId,
            RuleId: posting.RulePackReference.SelectedRuleId, RuleVersion: posting.RulePackReference.SelectedRuleVersion, PostingCommand: posting);
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, eventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.CorporateAction, mapRequest.RetainedEvidence, corporateAction: instruction);
        var (_, approval) = SuccessorPublicationVersions(command, mapped.Projection!.Event, mapped.Projection.LotMutation!);
        command = WithSuccessorPublicationEvidence(command, mapped.Projection.Event, approval);
        return (command with
        {
            Journal = command.Journal with
            {
                PostingCommand = command.Journal.PostingCommand! with
                {
                    Evidence = [.. command.Journal.PostingCommand!.Evidence,
                        new AccountingPostingEvidenceReferenceDto(approval.EvidenceId, approval.EvidenceUri,
                            AccountingPostingEvidenceKindDto.Approval, approval.SourceSystem, approval.RetainedAtUtc,
                            approval.RetainedBy, approval.SubjectId, approval.ContentHashSha256,
                            SourceReference: approval.SourceReference, Reviewer: approval.ReviewedBy,
                            ReviewedAtUtc: approval.ReviewedAtUtc, EffectiveDate: approval.EffectiveDate,
                            EvidenceVersion: approval.EvidenceVersion, ReviewStatus: approval.ReviewStatus, SubjectType: approval.SubjectType)]
                }
            }
        }).WithComputedFingerprint();
    }

    private static async Task<(long Journals, long Batches, long Mutations, long Lots)> ReadSplitAncestryCountsAsync(AmortFixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = $"""
            select (select count(*) from "{fixture.Options.SchemaName}".journal_entries),
                   (select count(*) from "{fixture.Options.SchemaName}".atomic_tax_lot_posting_batches),
                   (select count(*) from "{fixture.Options.SchemaName}".tax_lot_mutations),
                   (select count(*) from "{fixture.Options.SchemaName}".tax_lots);
            """;
        await using var reader = await query.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }
}
