using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Fact]
    public async Task CapitalReturn_RequiresRetainedInstructionAndIndependentApproval_ThenPostsAtomicallyAndReplays()
    {
        var baseline = BuildFixture(AssetAccountingEventKindDto.CorporateAction);
        var original = OpenLotSuccessorTestData.Predecessor();
        var source = original with
        {
            SecurityId = baseline.Security.SecurityId,
            BookPositionId = baseline.Position.PositionId,
            LedgerBookId = baseline.Book.LedgerBookId,
            Acquisition = original.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null }
        };
        var instruction = CapitalReturnTestData.Build(source, positionVersion: baseline.Position.Version,
            securityVersion: baseline.Security.Version, periodId: baseline.Period.PeriodId,
            periodVersion: baseline.Period.Version, fund: baseline.Book.FundProfileId);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(CapitalReturnTestData.MapRequest(instruction));
        mapped.IsMapped.Should().BeTrue(string.Join("; ", mapped.Blockers.Select(item => item.Message)));
        var fixture = baseline with
        {
            Request = mapped.Projection!.Event,
            Security = baseline.Security with { Currency = "EUR" },
            Position = baseline.Position with
            {
                OriginEvent = mapped.Projection.Event.EconomicEvent,
                ProjectionLineage = mapped.Projection.Event.ProjectionLineage,
                RetainedEvidence = mapped.Projection.Event.RetainedEvidence
            },
            Period = baseline.Period with
            {
                PeriodNo = 8,
                Label = "August 2026",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 31)
            }
        };
        var sourceRecord = new LedgerTaxLotRecord(source.TaxLotRecordId, source.LedgerBookId,
            new LedgerAccount("Assets:Investment", LedgerAccountType.Asset, FinancialAccountId: "custody-main"),
            source.LotId, source.AcquiredDate, source.OriginalQuantity, source.OpenQuantity,
            source.Acquisition.TransactionCostBasis / source.OriginalQuantity, source.Acquisition.AcquisitionCurrency,
            fixture.Request.ProjectedAtUtc, fixture.Request.ProjectedAtUtc, Version: source.Version,
            SecurityId: source.SecurityId, BookPositionId: source.BookPositionId, Acquisition: source.Acquisition,
            BasisAdjustment: new OpenLotBasisAdjustmentDto(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.Amortization,
                source.OpenQuantity, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis));
        sourceRecord.ToOpenLot().Should().BeEquivalentTo(source);
        var retained = new Dictionary<long, AssetAccountingEventProjectionRecord>();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var spine = call.ArgAt<AssetAccountingEventSpineDto>(0);
                retained.Add(spine.SpineVersion, new(spine, ComputeFingerprint(spine)));
                return new AssetAccountingEventAppendResult(spine, ComputeFingerprint(spine), false);
            });
        eventStore.GetAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => retained.GetValueOrDefault(call.ArgAt<long>(2)));
        eventStore.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => retained.Count == 0 ? null : retained[retained.Keys.Max()]);
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(source.BookPositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(source.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(source.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(BuildMatchingDryRun(fixture));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetTaxLotsByIdsAsync(source.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([sourceRecord]);
        ledger.GetLedgerBookAsync(source.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            fixture.Book.LedgerBookId, fixture.Book.FundProfileId, fixture.Book.FundStructureNodeId,
            fixture.Book.FundStructureNodeKind, fixture.Book.DisplayName, fixture.Book.BaseCurrency,
            fixture.Book.CreatedAt, fixture.Book.UpdatedAt, AccountingBasis: fixture.Book.AccountingBasis,
            AccountingPolicyId: fixture.Book.AccountingPolicyId, AccountingPolicyVersion: fixture.Book.AccountingPolicyVersion));
        var authority = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        AccountingPostingCandidateWriteResult? stableWrite = null;
        authority.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>())
            .Returns(call => stableWrite ??= BuildSuccessorCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0), fixture));
        var atomic = Substitute.For<IAtomicTaxLotJournalStore>();
        AtomicTaxLotJournalCommand? committedCommand = null;
        AtomicTaxLotJournalResult? committed = null;
        atomic.AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.ArgAt<AtomicTaxLotJournalCommand>(0);
            if (committed is not null)
            {
                command.CanonicalFingerprint.Should().Be(committedCommand!.CanonicalFingerprint);
                return committed with { IsExactReplay = true };
            }
            committedCommand = command;
            var write = AccountingPostingCommandValidator.NormalizeAndValidate(command.Journal,
                requirePostingCommand: true, requireExpectedVersion: true);
            var journal = new LedgerJournalEntryRecord(write.Entry, write.AggregateId, write.PeriodId, write.CommandId,
                write.CorrelationId, 1, fixture.Request.ProjectedAtUtc.AddHours(1), write.AccountingBasis,
                write.AccountingPolicyId, write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId);
            return committed = new(command.MutationBatchId, command.MutationKind, command.CanonicalFingerprint,
                false, journal, [], [], command.RetainedEvidence, CapitalReturn: command.CapitalReturn);
        });
        ledger.GetByAggregateAsync(source.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(_ => committed is null ? [] : new[] { committed.Journal });
        var service = new AssetAccountingEventSpineService(eventStore, positions, securities, books, policies,
            configuration, authority, ledger);
        await service.ProjectAsync(fixture.Request);
        var omitted = () => service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture));
        await omitted.Should().ThrowAsync<InvalidOperationException>();
        retained.Should().HaveCount(2, "omitted basis authority must fail before a draft is retained");
        var drafted = await service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with
        {
            LotMutation = mapped.Projection.LotMutation! with { AssetAccountId = "Assets:Investment" }
        });
        var candidate = drafted.Spine.DraftedCandidate!;
        var postService = new AccountingPostingCandidatePostService(Substitute.For<IAccountingPostingCandidateWriteBuilder>(),
            ledger, atomic, eventStore, authority);
        var missingApproval = () => postService.PostCandidateAsync(new(candidate, "approver", "capital-return-approval"));
        await missingApproval.Should().ThrowAsync<InvalidOperationException>();
        await atomic.DidNotReceiveWithAnyArgs().AppendAssetPostingAsync(default!, default);
        var reviewed = candidate.AccountingTimestamp.AddMinutes(10);
        var approval = fixture.Request.RetainedEvidence.Single(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event) with
        {
            EvidenceId = "capital-return-approval",
            EvidenceUri = "evidence://capital-return/approval",
            ReviewedBy = "approver",
            ReviewedAtUtc = reviewed,
            RetainedAtUtc = reviewed,
            SourceReference = "capital-return-approval",
            SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
            SubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(candidate.SourceEventId!.Value,
                candidate.EconomicEvent!.EventVersion, candidate.FundProfileId, source.LedgerBookId, candidate.PeriodId,
                candidate.AccountingBasis, "capital-return-approval", drafted.Spine.DraftedCandidateFingerprint!, candidate.TenantId, candidate.CompanyId)
        };
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate, "approver", "capital-return-approval")
        { ApprovalEvidence = [approval] };
        var posted = await postService.PostCandidateAsync(request);
        var replay = await postService.PostCandidateAsync(request);
        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.PostedJournal.Should().BeEquivalentTo(posted.PostedJournal);
        committedCommand!.MutationKind.Should().Be(AtomicTaxLotMutationKind.CapitalReturn);
        committedCommand.CapitalReturn.Should().BeEquivalentTo(instruction);
        committedCommand.Journal.PostingCommand!.LotCapitalReturn.Should().BeEquivalentTo(instruction);
        committedCommand.Journal.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        committedCommand.Journal.PostingCommand.Actor.Should().Be("approver");
        committedCommand.Journal.Entry.Metadata.Tags![OpenLotCapitalReturn.JournalFingerprintTag]
            .Should().Be(OpenLotCapitalReturn.Fingerprint(instruction));
        retained.Should().HaveCount(5);
        retained[5].Projection.Stages.Select(item => item.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        retained[5].Projection.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
    }
}
