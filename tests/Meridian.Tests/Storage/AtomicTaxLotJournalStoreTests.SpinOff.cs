using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SpinOff_ParentAndMultipleChildrenPostAtomicallyAndReplayAfterPeriodLock()
        => AssertSpinOffRoundTripAsync(fractionalBasisAfterRelief: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SpinOff_FractionalBasisAfterPartialReliefConservesBothBasesAndReplaysAfterPeriodLock()
        => AssertSpinOffRoundTripAsync(fractionalBasisAfterRelief: true);

    private static async Task AssertSpinOffRoundTripAsync(bool fractionalBasisAfterRelief)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SpinOffCommandAsync(fixture, fractionalBasisAfterRelief);
        var journalsBefore = (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Count;

        var posted = await fixture.Store.AppendAssetPostingAsync(command);

        posted.IsExactReplay.Should().BeFalse();
        posted.Mutations.Should().HaveCount(4);
        var source = command.CorporateAction!.ExpectedLot;
        posted.CorporateAction!.Projection.EventAmount.Should().Be(source.OpenFunctionalCostBasis);
        posted.Mutations.Single(item => item.LotBefore is not null).LotAfter.OpenQuantity.Should().Be(0m);
        var targets = posted.Mutations.Where(item => item.LotBefore is null).Select(item => item.LotAfter.ToOpenLot()).ToArray();
        targets.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        targets.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        var parent = targets.Single(lot => lot.SecurityId == source.SecurityId);
        parent.BookPositionId.Should().Be(source.BookPositionId);
        parent.OpenQuantity.Should().Be(source.OpenQuantity);
        foreach (var lot in targets)
        {
            var reviewed = command.CorporateAction.Successors.Single(target => target.Lot.TaxLotRecordId == lot.TaxLotRecordId);
            lot.Should().BeEquivalentTo(OpenLotSuccessors.WithLineage(command.CorporateAction, reviewed.Lot));
            lot.Acquisition.HoldingPeriodStartDate.Should().Be(source.Acquisition.HoldingPeriodStartDate);
            lot.Acquisition.CorporateActionLineage!.ReportingTags.Should().BeEmpty();
        }
        posted.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(source.OpenFunctionalCostBasis);
        posted.Journal.Entry.Lines.Sum(line => line.Credit).Should().Be(source.OpenFunctionalCostBasis);
        posted.Journal.Entry.Lines.Sum(line => line.Currency!.TransactionDebit).Should().Be(source.OpenTransactionCostBasis);
        posted.Journal.Entry.Lines.Sum(line => line.Currency!.TransactionCredit).Should().Be(source.OpenTransactionCostBasis);
        CanonicalOpenLotSuccessorEvidence.Validate(posted, posted.Journal, fixture.BookId, "USD");
        var reloaded = (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        reloaded.Mutations.Should().BeEquivalentTo(posted.Mutations);
        reloaded.CorporateAction.Should().BeEquivalentTo(command.CorporateAction);
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId,
            posted.MutatedLots.Select(lot => lot.TaxLotRecordId).ToArray())).Should().BeEquivalentTo(posted.MutatedLots);
        var child = reloaded.MutatedLots.Single(lot => lot.OriginalQuantity == 17.5m);
        child.UnitCost.Should().Be(decimal.Round(child.Acquisition!.FunctionalCostBasis / 17.5m, 12, MidpointRounding.ToEven));
        (child.OriginalQuantity * child.UnitCost).Should().NotBe(child.Acquisition!.FunctionalCostBasis);
        child.ToOpenLot().OpenFunctionalCostBasis.Should().Be(command.CorporateAction.Successors
            .Single(target => target.Lot.TaxLotRecordId == child.TaxLotRecordId).Lot.OpenFunctionalCostBasis);
        await AssertDurableSuccessorImpactAsync(fixture, reloaded);
        await fixture.LockPeriodAsync();
        var countsBeforeReplay = await ReadSplitAncestryCountsAsync(fixture);
        var auditBeforeReplay = (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(posted.Mutations);
        replay.CorporateAction.Should().BeEquivalentTo(command.CorporateAction);
        replay.CanonicalFingerprint.Should().Be(command.CanonicalFingerprint);
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(journalsBefore + 1);
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(countsBeforeReplay);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBeforeReplay);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SpinOff_LastSuccessorFailureRollsBackTheParentChildrenJournalAndAudit()
        => AssertSpinOffRollbackAsync(fractionalBasisAfterRelief: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task SpinOff_FractionalBasisAfterPartialReliefRollsBackEveryWriteThenRetriesAndReplays()
        => AssertSpinOffRollbackAsync(fractionalBasisAfterRelief: true);

    private static async Task AssertSpinOffRollbackAsync(bool fractionalBasisAfterRelief)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SpinOffCommandAsync(fixture, fractionalBasisAfterRelief);
        var before = await ReadSplitAncestryCountsAsync(fixture);
        var auditBefore = (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents;
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Options.SchemaName}".reject_test_spin_off() returns trigger
            language plpgsql as $$ begin raise exception 'injected spin-off successor failure'; end $$;
            create trigger reject_test_spin_off before insert on "{fixture.Options.SchemaName}".tax_lot_mutations
            for each row when (new.tax_lot_record_id = '{command.CorporateAction!.Successors.OrderBy(target => target.Lot.TaxLotRecordId).Last().Lot.TaxLotRecordId:D}'::uuid)
            execute function "{fixture.Options.SchemaName}".reject_test_spin_off();
            """;
        await injection.ExecuteNonQueryAsync();

        var fail = () => fixture.Store.AppendAssetPostingAsync(command);

        await fail.Should().ThrowAsync<PostgresException>().WithMessage("*injected spin-off successor failure*");
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(before);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
        var source = command.CorporateAction.ExpectedLot;
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [source.TaxLotRecordId]))
            .Single().ToOpenLot().Should().BeEquivalentTo(source);
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId,
            command.CorporateAction.Successors.Select(target => target.Lot.TaxLotRecordId).ToArray())).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        injection.CommandText = $"drop trigger reject_test_spin_off on \"{fixture.Options.SchemaName}\".tax_lot_mutations";
        await injection.ExecuteNonQueryAsync();
        var posted = await fixture.Restart().AppendAssetPostingAsync(command);
        posted.IsExactReplay.Should().BeFalse();
        var successors = posted.Mutations.Where(mutation => mutation.LotBefore is null)
            .Select(mutation => mutation.LotAfter.ToOpenLot()).ToArray();
        successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        var countsBeforeReplay = await ReadSplitAncestryCountsAsync(fixture);
        var auditBeforeReplay = (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(posted.Mutations);
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(countsBeforeReplay);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBeforeReplay);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SpinOff_ChangedChildReferenceRefusesTheWholeAllocation()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SpinOffCommandAsync(fixture);
        var target = command.CorporateAction!.Successors[1];
        var child = (await fixture.Securities.GetProjectionAsync(target.Lot.SecurityId))!;
        await fixture.Securities.UpsertProjectionAsync(child with { Version = child.Version + 1, DisplayName = "Revised child" });
        var before = await ReadSplitAncestryCountsAsync(fixture);

        var post = () => fixture.Restart().AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>();
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(before);
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
    }

    private static async Task<LedgerTaxLotRecord> RelieveSpinOffPredecessorAsync(AmortFixture fixture,
        LedgerTaxLotRecord retained)
    {
        const decimal quantity = 0.1233m;
        var source = retained.ToOpenLot();
        var relief = new OpenLotReliefService().Select([source], quantity, OpenLotReliefMethod.Fifo);
        var journalId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var key = "spin-off-partial-relief:" + Guid.NewGuid().ToString("N");
        var at = new DateTimeOffset(AmortAsOf.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: source.SecurityId) { PositionId = source.BookPositionId };
        const string description = "Reviewed partial relief before non-taxable spin-off";
        var entry = new JournalEntry(journalId, at, description,
            [
                new LedgerEntry(Guid.NewGuid(), journalId, at, new("Cash", LedgerAccountType.Asset),
                    relief.FunctionalCostBasis, 0m, description, dimensions,
                    new LedgerEntryCurrency("EUR", "USD", relief.TransactionCostBasis, 0m, 1.1m)),
                new LedgerEntry(Guid.NewGuid(), journalId, at, retained.Account, 0m,
                    relief.FunctionalCostBasis, description, dimensions,
                    new LedgerEntryCurrency("EUR", "USD", 0m, relief.TransactionCostBasis, 1.1m))
            ], new(SecurityId: source.SecurityId, EffectiveDate: AmortAsOf, IdempotencyKey: key,
                Tags: SecurityMasterLineageTags(source.SecurityId)));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            AmortAsOf, at, key, AccountingPostingIntentDto.Originating, sourceEventId, ExpectedVersion: fixture.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Independent review of partial relief at exact canonical basis.", LedgerBookId: fixture.BookId)
        { Actor = "independent-controller" };
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1", SourceEventId: sourceEventId,
            LedgerBookId: fixture.BookId, PostingCommand: posting);
        var evidence = source.Acquisition.Evidence.Single();
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, sourceEventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(retained.TaxLotRecordId, retained.LotId, retained.Version, retained.OpenQuantity,
                quantity, 0, evidence.EvidenceId, retained.UnitCost, relief.FunctionalCostBasis)],
            reliefMethod: "Fifo", policyRevision: "amort-fifo-v1",
            referenceVersions: new(fixture.Security.Version, (await fixture.Positions.GetBookPositionAsync(fixture.PositionId))!.Version));

        var posted = await fixture.Store.AppendAssetPostingAsync(command);
        posted.IsExactReplay.Should().BeFalse();
        var reloaded = (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [source.TaxLotRecordId])).Single();
        reloaded.Should().BeEquivalentTo(posted.MutatedLots.Single());
        var remaining = reloaded.ToOpenLot();
        remaining.OpenQuantity.Should().Be(59.8767m);
        remaining.OpenTransactionCostBasis.Should().Be(598.767m);
        remaining.OpenFunctionalCostBasis.Should().Be(658.6437m);
        remaining.OpenFunctionalCostBasis.Should().NotBe(decimal.Round(remaining.OpenFunctionalCostBasis, 2));
        (remaining.OpenTransactionCostBasis + relief.TransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        (remaining.OpenFunctionalCostBasis + relief.FunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        return reloaded;
    }

    private static async Task<AtomicTaxLotJournalCommand> SpinOffCommandAsync(AmortFixture fixture,
        bool fractionalBasisAfterRelief = false)
    {
        if (fractionalBasisAfterRelief)
        {
            // Close the fixture's face-valued seed before exercising unit-only FIFO relief.
            var closing = await fixture.CurrentBasisDisposalAsync(fixture.Lot.OpenQuantity, AmortAsOf,
                "spin-off-close-fixture-face-lot");
            await fixture.Store.AppendAssetPostingAsync(closing.Command);
        }
        var retained = await AcquireSplitAncestryUnitsAsync(fixture, AmortAsOf.AddDays(-10));
        if (fractionalBasisAfterRelief)
            retained = await RelieveSpinOffPredecessorAsync(fixture, retained);
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
                null, position.Version, new("independent-controller", "evidence://spin-off-basis", "Review spin-off basis", DateTimeOffset.UtcNow));
        }
        var sourceSecurity = (await fixture.Securities.GetProjectionAsync(predecessor.SecurityId))!;
        var targets = OpenLotSpinOffTestData.Targets(predecessor, children: 2).ToArray();
        targets[0] = targets[0] with
        {
            ExpectedBookPositionVersion = position.Version,
            ExpectedSecurityVersion = sourceSecurity.Version,
            ExpectedSecurityHash = OpenLotAmortization.SecurityHash(sourceSecurity)
        };
        for (var index = 1; index < targets.Length; index++)
        {
            var target = targets[index];
            var security = AmortSecurity(target.Lot.SecurityId) with { PrimaryIdentifierValue = "SPINOFF-" + Guid.NewGuid().ToString("N") };
            await fixture.Securities.UpsertProjectionAsync(security);
            security = (await fixture.Securities.GetProjectionAsync(security.SecurityId))!;
            var eventRef = position.OriginEvent! with { SecurityId = security.SecurityId, BookPositionId = target.Lot.BookPositionId };
            var role = snapshot.InstrumentRoles.Single(item => item.RoleId == position.RoleId) with
            { RoleId = Guid.NewGuid(), SecurityId = security.SecurityId, OriginEvent = eventRef };
            var childPosition = position with
            {
                PositionId = target.Lot.BookPositionId,
                SecurityId = security.SecurityId,
                RoleId = role.RoleId,
                Version = 1,
                OriginEvent = eventRef
            };
            await fixture.Positions.UpsertAsync(role, childPosition, null, 0,
                new("independent-controller", "evidence://spin-off-child", "Review child identity", DateTimeOffset.UtcNow));
            targets[index] = target with
            {
                ExpectedBookPositionVersion = childPosition.Version,
                ExpectedSecurityVersion = security.Version,
                ExpectedSecurityHash = OpenLotAmortization.SecurityHash(security)
            };
        }
        var instruction = OpenLotSuccessorTestData.Build(predecessor, targets,
            expectedPositionVersion: position.Version, expectedSecurityVersion: sourceSecurity.Version,
            periodId: fixture.Period.PeriodId, expectedSecurityHash: OpenLotAmortization.SecurityHash(sourceSecurity),
            allocationPercents: OpenLotSpinOffTestData.Allocations(2), fundProfileId: "amort-fund", expectedPeriodVersion: fixture.Period.Version,
            actionType: CorporateActionAccountingTypeDto.SpinOff,
            policyInputs: new(CarryHoldingPeriod: true, SpinOffTaxTreatment: CorporateActionSpinOffTaxTreatmentDto.NonTaxableBasisAllocation));
        var mapRequest = OpenLotSuccessorTestData.MapRequest(instruction);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(mapRequest);
        mapped.Blockers.Should().BeEmpty();
        var preview = await new CanonicalLotCorporateActionService(fixture.Store, fixture.Securities, fixture.Positions)
            .PreviewAsync(mapped.Projection!, instruction, "Assets:Investment");
        preview.CorporateAction.Should().BeEquivalentTo(instruction);
        var projection = instruction.Projection;
        var eventId = projection.EconomicEvent!.EventId;
        var date = projection.EconomicEvent.EffectiveDate;
        var key = "spin-off-posting:" + Guid.NewGuid().ToString("N");
        var journalId = Guid.NewGuid();
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        LedgerLineDimensionSet Dimensions(OpenLotDto lot) => new(FundId: "amort-fund", InstrumentId: lot.SecurityId,
            BookId: fixture.BookId.ToString("D"), TaxLotId: lot.LotId)
        { PositionId = lot.BookPositionId };
        const string description = "Independently reviewed non-taxable spin-off";
        var lines = targets.Select(target => new LedgerEntry(Guid.NewGuid(), journalId, at, retained.Account,
            target.Lot.OpenFunctionalCostBasis, 0m, description, Dimensions(target.Lot),
            new LedgerEntryCurrency("EUR", "USD", target.Lot.OpenTransactionCostBasis, 0m, 1.1m))).ToList();
        lines.Add(new(Guid.NewGuid(), journalId, at, retained.Account, 0m, predecessor.OpenFunctionalCostBasis,
            description, Dimensions(predecessor), new LedgerEntryCurrency("EUR", "USD", 0m, predecessor.OpenTransactionCostBasis, 1.1m)));
        var entry = new JournalEntry(journalId, at, description, lines,
            new(SecurityId: predecessor.SecurityId, EffectiveDate: date, IdempotencyKey: key,
                Tags: new Dictionary<string, string>(SecurityMasterLineageTags(predecessor.SecurityId))
                { [OpenLotSuccessors.JournalFingerprintTag] = OpenLotSuccessors.Fingerprint(instruction) }));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventId, ExpectedVersion: fixture.Period.Version,
            SourceEventType: AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
            OperatorRationale: "Independent review of non-taxable spin-off allocation and holding period.", LedgerBookId: fixture.BookId)
        {
            Actor = "independent-controller",
            LotCorporateAction = instruction,
            BookContext = position.BookContext with { PeriodId = fixture.Period.PeriodId },
            BookPositionId = position.PositionId,
            EconomicEvent = projection.EconomicEvent,
            ProjectionLineage = projection.ProjectionLineage,
            RulePackReference = mapRequest.MappedEffect.AccountingRulePack,
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
                    Evidence = [.. command.Journal.PostingCommand!.Evidence, new AccountingPostingEvidenceReferenceDto(
                approval.EvidenceId, approval.EvidenceUri, AccountingPostingEvidenceKindDto.Approval, approval.SourceSystem,
                approval.RetainedAtUtc, approval.RetainedBy, approval.SubjectId, approval.ContentHashSha256,
                SourceReference: approval.SourceReference, Reviewer: approval.ReviewedBy, ReviewedAtUtc: approval.ReviewedAtUtc,
                EffectiveDate: approval.EffectiveDate, EvidenceVersion: approval.EvidenceVersion, ReviewStatus: approval.ReviewStatus,
                SubjectType: approval.SubjectType)]
                }
            }
        }).WithComputedFingerprint();
    }
}
