using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_PostedPublicationRecoversAfterPositionAdvances_OnlyWithExactCommittedBatch()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(OpenLotSuccessorTestData.MapRequest(command.CorporateAction!));
        mapped.IsMapped.Should().BeTrue();
        var request = mapped.Projection!.Event;
        var (versions, approval) = SuccessorPublicationVersions(command, request, mapped.Projection.LotMutation!);
        command = WithSuccessorPublicationEvidence(command, request, approval);
        var options = new AssetOperationsOptions
        {
            ConnectionString = fixture.Options.ConnectionString,
            Schema = "successor_pub_" + Guid.NewGuid().ToString("N")
        };
        await new AssetOperationsMigrationRunner(options).EnsureMigratedAsync();
        try
        {
            var publishers = new IAssetAccountingEventProjectionStore[]
            {
                new InMemoryAssetOperationsProjectionStore(fixture.Restart()),
                new PostgresAssetOperationsProjectionStore(options, fixture.Restart())
            };
            var source = await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId);
            var position = source.BookPositions.Single();
            foreach (var publisher in publishers)
            {
                await SeedPublicationPositionAsync((IInstrumentPositionProjectionStore)publisher, source.InstrumentRoles.Single(), position);
                foreach (var version in versions)
                    await publisher.AppendAsync(version, version.SpineVersion - 1, position.Version);
            }

            // The commit succeeds, but publication is interrupted before its first Posted append.
            var committed = await fixture.Store.AppendAssetPostingAsync(command);
            var impact = PublicationImpact(committed);
            var posted = versions[^1] with
            {
                SpineVersion = 5,
                Stages = [.. versions[^1].Stages, new(AssetAccountingLifecycleStageDto.Posted,
                    committed.Journal.CreatedAt, "independent-controller", [approval], committed.Journal.Entry.JournalEntryId.ToString("D"))],
                PostedJournalImpact = impact,
                TaxLotMutationBatchId = committed.MutationBatchId
            };
            AssetAccountingEventSpineValidator.Validate(posted).Should().BeEmpty();
            foreach (var publisher in publishers)
            {
                await ((IInstrumentPositionProjectionStore)publisher).UpsertAsync(source.InstrumentRoles.Single(),
                    position with { Version = position.Version + 1 }, null, position.Version,
                    new("controller", "evidence://publication/new-version", "Later legitimate position revision", DateTimeOffset.UtcNow));
                var missing = () => publisher.AppendAsync(posted with { TaxLotMutationBatchId = Guid.NewGuid() }, 4, position.Version);
                await missing.Should().ThrowAsync<InvalidOperationException>();
                (await publisher.GetLatestAsync(posted.EventId, posted.EventVersion))!.Projection.SpineVersion.Should().Be(4);
            }

            // New store instances must repair the lifecycle using retained posting authority.
            var restartedPostgres = new PostgresAssetOperationsProjectionStore(options, fixture.Restart());
            publishers[1] = restartedPostgres;
            (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
            foreach (var publisher in publishers)
            {
                var repaired = await publisher.AppendAsync(posted, 4, position.Version);
                repaired.WasReplay.Should().BeFalse();
                var replay = await publisher.AppendAsync(posted, 4, position.Version);
                replay.WasReplay.Should().BeTrue();
                replay.Projection.TaxLotMutationBatchId.Should().Be(committed.MutationBatchId);
                replay.Projection.PostedJournalImpact.Should().BeEquivalentTo(impact);
            }
            (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        }
        finally
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand($"drop schema {options.Schema} cascade", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_UnpostedPublicationStillRejectsStalePosition()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(OpenLotSuccessorTestData.MapRequest(command.CorporateAction!));
        var (versions, _) = SuccessorPublicationVersions(command, mapped.Projection!.Event, mapped.Projection.LotMutation!);
        var publisher = new InMemoryAssetOperationsProjectionStore(fixture.Store);
        var source = await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId);
        var position = source.BookPositions.Single();
        await SeedPublicationPositionAsync(publisher, source.InstrumentRoles.Single(), position);
        await publisher.AppendAsync(versions[0], 0, position.Version);
        await publisher.UpsertAsync(source.InstrumentRoles.Single(), position with { Version = position.Version + 1 },
            null, position.Version, new("controller", "evidence://publication/advance", "Advance source authority", DateTimeOffset.UtcNow));

        var stale = () => publisher.AppendAsync(versions[1], 1, position.Version);
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale or mismatched book-position*");
        (await publisher.GetLatestAsync(versions[0].EventId, versions[0].EventVersion))!.Projection.SpineVersion.Should().Be(1);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_DisposalCannotPrecedeOpeningDespiteInheritedAcquisitionDate()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var successorCommand = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var created = await fixture.Store.AppendAssetPostingAsync(successorCommand);
        var successor = created.Mutations.Single(mutation => mutation.LotBefore is null).LotAfter;
        var openingDate = successorCommand.CorporateAction!.Projection.EconomicEvent!.EffectiveDate;
        successor.AcquiredDate.Should().BeBefore(openingDate);
        var early = SuccessorDisposalCommand(successorCommand, successor, openingDate.AddDays(-1));

        var fail = () => fixture.Restart().AppendAssetPostingAsync(early);
        await fail.Should().ThrowAsync<LedgerValidationException>().WithMessage("*authoritative open tax lots*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(early.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [successor.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(successor);
        (await fixture.Store.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, successor.SecurityId,
            successor.BookPositionId, openingDate.AddDays(-1))).Should().BeEmpty();

        var onOpening = SuccessorDisposalCommand(successorCommand, successor, openingDate);
        var disposed = await fixture.Restart().AppendAssetPostingAsync(onOpening);
        disposed.MutatedLots.Should().ContainSingle().Which.OpenQuantity.Should().Be(0m);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
        (await fixture.Store.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, successor.SecurityId,
            successor.BookPositionId, openingDate)).Should().BeEmpty();
    }

    private static AtomicTaxLotJournalCommand SuccessorDisposalCommand(AtomicTaxLotJournalCommand opening,
        LedgerTaxLotRecord successor, DateOnly effectiveDate)
    {
        var id = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var key = "successor-disposal:" + Guid.NewGuid().ToString("N");
        var at = new DateTimeOffset(effectiveDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var canonical = successor.ToOpenLot();
        var scope = new LedgerLineDimensionSet(InstrumentId: successor.SecurityId) { PositionId = successor.BookPositionId };
        var entry = new JournalEntry(id, at, "Dispose retained successor", [
            new LedgerEntry(Guid.NewGuid(), id, at, successor.Account, 0m, canonical.OpenFunctionalCostBasis,
                "Dispose retained successor", scope, new("EUR", "USD", 0m, canonical.OpenTransactionCostBasis, 1.1m)),
            new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.Cash, canonical.OpenFunctionalCostBasis, 0m,
                "Dispose retained successor", scope, new("EUR", "USD", canonical.OpenTransactionCostBasis, 0m, 1.1m))],
            new(SecurityId: successor.SecurityId, EffectiveDate: effectiveDate, IdempotencyKey: key,
                Tags: SecurityMasterLineageTags(successor.SecurityId)));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), opening.LedgerBookId, opening.Journal.PeriodId,
            effectiveDate, at, key, AccountingPostingIntentDto.Adjustment, sourceId, ExpectedVersion: opening.ExpectedPeriodVersion,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Reviewed successor disposal", LedgerBookId: opening.LedgerBookId)
        { Actor = "independent-controller" };
        var write = opening.Journal with { Entry = entry, SourceEventId = sourceId, CommandId = null, PostingCommand = posting };
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), opening.LedgerBookId, write, sourceId, key,
            opening.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, opening.RetainedEvidence,
            disposalSelections: [new(successor.TaxLotRecordId, successor.LotId, successor.Version, successor.OpenQuantity,
                successor.OpenQuantity, 0, successor.EvidenceRef!, successor.UnitCost, canonical.OpenFunctionalCostBasis)],
            reliefMethod: "Fifo", policyRevision: "amort-fifo-v1");
    }

    private static async Task SeedPublicationPositionAsync(IInstrumentPositionProjectionStore store,
        InstrumentRoleDto role, BookPositionDto position)
    {
        for (long version = 1; version <= position.Version; version++)
            await store.UpsertAsync(role, position with { Version = version }, null, version - 1,
                new("controller", "evidence://publication/source", "Retain source authority revision", DateTimeOffset.UtcNow));
    }

    private static (AssetAccountingEventSpineDto[] Versions, RetainedEvidenceIdentityDto Approval) SuccessorPublicationVersions(
        AtomicTaxLotJournalCommand command, ProjectAssetAccountingEventRequestDto request, AssetLotMutationInstructionDto mutation)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        var evidence = request.RetainedEvidence.Single(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event);
        var expected = new AssetAccountingEventSpineDto(command.SourceEventId, request.EventKind,
            request.EconomicEvent.EventVersion, 1, request.EconomicEvent.EffectiveDate, request.EventAmount, request.Currency,
            request.Scope, request.EconomicEvent, request.ProjectionLineage, request.RetainedEvidence,
            [new(AssetAccountingLifecycleStageDto.Expected, at, "projector", [evidence], "expected-successor")],
            CorporateAction: command.CorporateAction);
        var projected = expected with
        {
            SpineVersion = 2,
            ProjectedEffect = request.ProjectedEffect,
            Stages = [.. expected.Stages, new(AssetAccountingLifecycleStageDto.Projected, at, "projector", [evidence], "projected-successor")]
        };
        var candidate = new PostingRuleJournalCandidateRequestDto(request.Scope.FundProfileId, request.EconomicEvent.EventType,
            request.EventAmount, request.Currency, request.EconomicEvent.EffectiveDate, "preparer", command.LedgerBookId,
            command.Journal.PeriodId, command.Journal.Entry.Timestamp, command.Journal.Entry.Description,
            request.Scope.AccountingBasis, command.LedgerBookId, SourceEventId: command.SourceEventId,
            TenantId: request.Scope.TenantId, CompanyId: request.Scope.CompanyId)
        { AssetLotMutation = mutation, RetainedEvidence = request.RetainedEvidence, ExpectedPeriodVersion = command.ExpectedPeriodVersion };
        var dryRun = new RuleDryRunResultDto(request.Scope.FundProfileId, command.LedgerBookId, request.EconomicEvent.EventType,
            request.EconomicEvent.EffectiveDate, request.EventAmount, request.Currency, true, "reviewed-successor-rule", [], [], []);
        var candidateResult = new PostingRuleJournalCandidateResultDto(dryRun, "reviewed-successor-rule", "1", [],
            command.Journal.PostingCommand, command.Journal.Entry.JournalEntryId, request.EventAmount, request.EventAmount,
            0m, true, false, true, false, [], []);
        var drafted = projected with
        {
            SpineVersion = 3,
            Stages = [.. projected.Stages, new(AssetAccountingLifecycleStageDto.Drafted, at, "preparer", [evidence], "drafted-successor")],
            DraftedCandidate = candidate,
            DraftedCandidateResult = candidateResult,
            DraftedCandidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate),
            DraftedCandidateResultFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateResult),
            DraftedLotMutation = mutation,
            DraftedLotMutationFingerprint = AssetLotMutationInstructionValidator.Fingerprint(mutation)
        };
        var approval = evidence with
        {
            EvidenceId = "successor-publication-approval",
            EvidenceUri = "evidence://successors/publication-approval",
            SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
            SubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(command.SourceEventId, request.EconomicEvent.EventVersion,
                request.Scope.FundProfileId, command.LedgerBookId, command.Journal.PeriodId, request.Scope.AccountingBasis,
                "successor-publication-approval", drafted.DraftedCandidateFingerprint!, request.Scope.TenantId, request.Scope.CompanyId),
            ReviewedBy = "independent-controller",
            ReviewedAtUtc = at,
            RetainedAtUtc = at
        };
        var approved = drafted with
        {
            SpineVersion = 4,
            Stages = [.. drafted.Stages, new(AssetAccountingLifecycleStageDto.Approved, at, "independent-controller", [approval], "successor-publication-approval")]
        };
        foreach (var version in new[] { expected, projected, drafted, approved })
            AssetAccountingEventSpineValidator.Validate(version).Should().BeEmpty();
        return ([expected, projected, drafted, approved], approval);
    }

    private static AtomicTaxLotJournalCommand WithSuccessorPublicationEvidence(AtomicTaxLotJournalCommand command,
        ProjectAssetAccountingEventRequestDto request, RetainedEvidenceIdentityDto approval)
    {
        var original = command.Journal.Entry;
        var lines = original.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description, line.Dimensions! with
            { FundId = request.Scope.FundProfileId, BookId = command.LedgerBookId.ToString("D") }, line.Currency)).ToArray();
        return (command with
        {
            RetainedEvidence = [.. request.RetainedEvidence, approval],
            Journal = command.Journal with
            {
                Entry = new(original.JournalEntryId, original.Timestamp, original.Description, lines, original.Metadata),
                PostingCommand = command.Journal.PostingCommand! with { ApprovalId = approval.EvidenceId }
            }
        }).WithComputedFingerprint();
    }

    private static PostedJournalImpactDto PublicationImpact(AtomicTaxLotJournalResult committed)
        => new(committed.Journal.Entry.JournalEntryId, committed.Journal.AggregateId, committed.Journal.PeriodId,
            committed.Journal.AccountingBasis, committed.Journal.CreatedAt, JournalPostingStatusDto.Posted, "USD",
            committed.Journal.Entry.Lines.Sum(line => line.Debit), committed.Journal.Entry.Lines.Sum(line => line.Credit),
            committed.Journal.Entry.Lines.Select(line => new PostedJournalImpactLineDto(line.EntryId, line.Account.ToString(),
                line.Debit, line.Credit, "USD", Dimensions: new LedgerDimensionSetDto(FundId: line.Dimensions!.FundId,
                    InstrumentId: line.Dimensions.InstrumentId, BookId: line.Dimensions.BookId)
                { PositionId = line.Dimensions.PositionId })).ToArray());
}
