using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

/// <summary>
/// The factor paydown through the Asset Accounting Event Spine: projected with the governed per-lot
/// instruction, drafted only with that exact instruction and the lots of record unchanged, approved
/// independently, then posted once through the atomic journal/lot boundary and replayed exactly.
/// </summary>
public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Fact]
    public async Task FactorPaydown_RequiresRetainedInstructionAndIndependentApproval_ThenPostsAtomicallyAndReplays()
    {
        var harness = BuildFactorPaydownSpineHarness();
        var (service, postService, fixture, instruction) = (harness.Service, harness.PostService, harness.Fixture, harness.Instruction);

        await service.ProjectAsync(fixture.Request);
        var omitted = () => service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture));
        await omitted.Should().ThrowAsync<InvalidOperationException>();
        harness.Retained.Should().HaveCount(2, "an omitted paydown instruction must fail before a draft is retained");
        var drafted = await service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with
        {
            LotMutation = FactorPaydownLotMutation(instruction)
        });
        var candidate = drafted.Spine.DraftedCandidate!;
        drafted.Spine.FactorPaydown.Should().BeEquivalentTo(instruction);
        drafted.Spine.DraftedLotMutation!.FactorPaydown.Should().BeEquivalentTo(instruction);
        var missingApproval = () => postService.PostCandidateAsync(new(candidate, "approver", "factor-paydown-approval"));
        await missingApproval.Should().ThrowAsync<InvalidOperationException>();
        await harness.Atomic.DidNotReceiveWithAnyArgs().AppendAssetPostingAsync(default!, default);

        var request = harness.ApprovedPostRequest(candidate, drafted.Spine.DraftedCandidateFingerprint!);
        var posted = await postService.PostCandidateAsync(request);
        var replay = await postService.PostCandidateAsync(request);

        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.PostedJournal.Should().BeEquivalentTo(posted.PostedJournal);
        var committed = harness.CommittedCommand!;
        committed.MutationKind.Should().Be(AtomicTaxLotMutationKind.FactorPaydown);
        committed.FactorPaydown.Should().BeEquivalentTo(instruction);
        committed.Journal.PostingCommand!.LotFactorPaydown.Should().BeEquivalentTo(instruction);
        committed.Journal.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        committed.Journal.PostingCommand.Actor.Should().Be("approver");
        committed.Journal.Entry.Metadata.Tags![OpenLotFactorPaydown.JournalFingerprintTag]
            .Should().Be(OpenLotFactorPaydown.Fingerprint(instruction));
        var lines = committed.Journal.Entry.Lines;
        lines.Should().HaveCount(3);
        lines.Single(line => line.Account.Name == "Assets:Cash").Debit.Should().Be(50_000m);
        lines.Single(line => line.Account.Name == "Assets:Investment").Credit.Should().Be(50_750m);
        lines.Single(line => line.Account.Name == "Expense:Realized Loss").Debit.Should().Be(750m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
        harness.Retained.Should().HaveCount(5);
        harness.Retained[5].Projection.Stages.Select(item => item.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        harness.Retained[5].Projection.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        harness.Retained[5].Projection.FactorPaydown.Should().BeEquivalentTo(instruction);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("account-type")]
    public async Task FactorPaydown_DraftRefusesWhenTheLotOfRecordNoLongerMatchesTheReviewedInstruction(string drift)
    {
        var harness = BuildFactorPaydownSpineHarness();
        var retained = harness.SourceRecord;
        harness.Ledger.GetTaxLotsByIdsAsync(retained.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([drift switch
            {
                "version" => retained with { Version = retained.Version + 1 },
                "factor" => retained with { CurrentFactor = 0.93m },
                _ => retained with { Account = new LedgerAccount(retained.Account.Name, LedgerAccountType.Liability, FinancialAccountId: "custody-main") }
            }]);

        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var draft = () => harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(harness.Instruction)
        });

        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*paid-down lots*");
        harness.Retained.Should().HaveCount(2, "a drifted lot of record must fail before a draft is retained");
    }

    [Fact]
    public async Task FactorPaydown_DraftRefusesAReplacedInstruction()
    {
        var harness = BuildFactorPaydownSpineHarness();
        var lot = harness.Instruction.Lots.Single();
        var shifted = harness.Instruction with
        {
            Lots = [lot with { Principal = lot.Principal + 0.01m, FunctionalBasisAfter = lot.FunctionalBasisAfter - 0.01m }]
        };

        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var draft = () => harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(shifted)
        });

        // The request envelope re-projects the instruction before any retained state is read, so a
        // shifted allocation is refused as an invalid instruction rather than as a replaced one.
        await draft.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must equal the server allocation*");
        harness.Retained.Should().HaveCount(2);
    }

    private static AssetLotMutationInstructionDto FactorPaydownLotMutation(OpenLotFactorPaydownInstructionDto instruction)
        => new(AssetLotMutationIntentDto.FactorPaydown, AssetAccountId: "Assets:Investment", FactorPaydown: instruction);

    private sealed class FactorPaydownSpineHarness
    {
        public required AssetAccountingEventSpineService Service { get; init; }
        public required AccountingPostingCandidatePostService PostService { get; init; }
        public required Fixture Fixture { get; init; }
        public required OpenLotFactorPaydownInstructionDto Instruction { get; init; }
        public required LedgerTaxLotRecord SourceRecord { get; init; }
        public required ILedgerJournalStore Ledger { get; init; }
        public required IAtomicTaxLotJournalStore Atomic { get; init; }
        public required Dictionary<long, AssetAccountingEventProjectionRecord> Retained { get; init; }
        public AtomicTaxLotJournalCommand? CommittedCommand { get; set; }
        public List<JournalEntry> RebuiltJournals { get; } = [];
        public Func<JournalEntry, JournalEntry>? CandidateMutation { get; set; }

        public PostPostingRuleJournalCandidateRequestDto ApprovedPostRequest(PostingRuleJournalCandidateRequestDto candidate, string draftedFingerprint)
        {
            var reviewed = candidate.AccountingTimestamp.AddMinutes(10);
            var approval = Fixture.Request.RetainedEvidence.Single(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event) with
            {
                EvidenceId = "factor-paydown-approval",
                EvidenceUri = "evidence://factor-paydown/approval",
                ReviewedBy = "approver",
                ReviewedAtUtc = reviewed,
                RetainedAtUtc = reviewed,
                SourceReference = "factor-paydown-approval",
                SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
                SubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(candidate.SourceEventId!.Value,
                    candidate.EconomicEvent!.EventVersion, candidate.FundProfileId, SourceRecord.LedgerBookId, candidate.PeriodId,
                    candidate.AccountingBasis, "factor-paydown-approval", draftedFingerprint, candidate.TenantId, candidate.CompanyId)
            };
            return new PostPostingRuleJournalCandidateRequestDto(candidate, "approver", "factor-paydown-approval")
            { ApprovalEvidence = [approval] };
        }
    }

    /// <summary>
    /// The CapitalReturn spine twin for a paydown: the worked premium lot (1,000,000 of par booked at
    /// 0.95 at 101.5) held in the fixture position pays 0.95 -> 0.90, so the projected effect is Dr
    /// cash 50,000 / Cr carrying 50,750 / Dr realized loss 750.
    /// </summary>
    private static FactorPaydownSpineHarness BuildFactorPaydownSpineHarness(bool regenerateCashFlowWrites = false)
    {
        var baseline = BuildFixture(AssetAccountingEventKindDto.CorporateAction);
        var lot = FactorPaydownTestData.PremiumLot(baseline.Security.SecurityId, baseline.Position.PositionId, baseline.Book.LedgerBookId);
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m,
            securityVersion: baseline.Security.Version, positionVersion: baseline.Position.Version,
            periodId: baseline.Period.PeriodId, periodVersion: baseline.Period.Version, fund: baseline.Book.FundProfileId,
            tenantId: null, companyId: null, dimensions: baseline.Request.Scope.Dimensions);
        var request = FactorPaydownTestData.ProjectRequest(instruction, baseline.Request.Scope.Dimensions!);
        request.EconomicEvent.EffectiveDate.Should().Be(baseline.Period.EndDate, "the fixture period must contain the paydown date");
        var fixture = baseline with
        {
            Request = request,
            Position = baseline.Position with
            {
                OriginEvent = request.EconomicEvent,
                ProjectionLineage = request.ProjectionLineage,
                RetainedEvidence = request.RetainedEvidence
            }
        };
        var sourceRecord = FactorPaydownTestData.LotRecord(lot,
            new LedgerAccount("Assets:Investment", LedgerAccountType.Asset, FinancialAccountId: "custody-main"), request.ProjectedAtUtc);
        sourceRecord.ToOpenLot().Should().BeEquivalentTo(lot);
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
        positions.GetBookPositionAsync(lot.BookPositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(lot.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(lot.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(BuildFactorPaydownDryRun(fixture));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetTaxLotsByIdsAsync(lot.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([sourceRecord]);
        ledger.GetLedgerBookAsync(lot.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            fixture.Book.LedgerBookId, fixture.Book.FundProfileId, fixture.Book.FundStructureNodeId,
            fixture.Book.FundStructureNodeKind, fixture.Book.DisplayName, fixture.Book.BaseCurrency,
            fixture.Book.CreatedAt, fixture.Book.UpdatedAt, AccountingBasis: fixture.Book.AccountingBasis,
            AccountingPolicyId: fixture.Book.AccountingPolicyId, AccountingPolicyVersion: fixture.Book.AccountingPolicyVersion));
        var authority = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        AccountingPostingCandidateWriteResult? stableWrite = null;
        Guid? stableCommandId = null;
        var atomic = Substitute.For<IAtomicTaxLotJournalStore>();
        var harness = new FactorPaydownSpineHarness
        {
            Service = new AssetAccountingEventSpineService(eventStore, positions, securities, books, policies, configuration, authority, ledger),
            PostService = new AccountingPostingCandidatePostService(Substitute.For<IAccountingPostingCandidateWriteBuilder>(),
                ledger, atomic, eventStore, authority),
            Fixture = fixture,
            Instruction = instruction,
            SourceRecord = sourceRecord,
            Ledger = ledger,
            Atomic = atomic,
            Retained = retained
        };
        authority.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (!regenerateCashFlowWrites)
                    return stableWrite ??= BuildFactorPaydownCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0), fixture);
                var result = BuildFactorPaydownCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0), fixture);
                stableCommandId ??= result.Write!.PostingCommand!.CommandId;
                var command = result.Write!.PostingCommand! with { CommandId = stableCommandId.Value };
                result = result with
                {
                    Candidate = result.Candidate with { PostingCommand = command },
                    Write = result.Write with { CommandId = stableCommandId.Value, PostingCommand = command }
                };
                var entry = LedgerCashFlowAttribution.WithClassifiedActivity(result.Write!.Entry,
                    LedgerCashFlowCategory.Investing, "Factor principal repayment");
                if (harness.CandidateMutation is { } mutate)
                    entry = mutate(entry);
                harness.RebuiltJournals.Add(entry);
                return result with { Write = result.Write with { Entry = entry } };
            });
        AtomicTaxLotJournalResult? committed = null;
        atomic.AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.ArgAt<AtomicTaxLotJournalCommand>(0);
            if (committed is not null)
            {
                if (command.CanonicalFingerprint != harness.CommittedCommand!.CanonicalFingerprint)
                    throw new LedgerValidationException("Atomic tax-lot posting identity collision: changed canonical fingerprint.");
                command.CanonicalFingerprint.Should().Be(harness.CommittedCommand!.CanonicalFingerprint);
                return committed with { IsExactReplay = true };
            }
            harness.CommittedCommand = command;
            var write = AccountingPostingCommandValidator.NormalizeAndValidate(command.Journal,
                requirePostingCommand: true, requireExpectedVersion: true);
            var journal = new LedgerJournalEntryRecord(write.Entry, write.AggregateId, write.PeriodId, write.CommandId,
                write.CorrelationId, 1, fixture.Request.ProjectedAtUtc.AddHours(1), write.AccountingBasis,
                write.AccountingPolicyId, write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId);
            return committed = new(command.MutationBatchId, command.MutationKind, command.CanonicalFingerprint,
                false, journal, [], [], command.RetainedEvidence, FactorPaydown: command.FactorPaydown);
        });
        ledger.GetByAggregateAsync(lot.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(_ => committed is null ? [] : new[] { committed.Journal });
        return harness;
    }

    /// <summary>Every projected line as a generated Rules Studio line, sides taken from the effect.</summary>
    private static RuleDryRunResultDto BuildFactorPaydownDryRun(Fixture fixture)
    {
        var projected = fixture.Request.ProjectedEffect;
        return new RuleDryRunResultDto(
            fixture.Book.FundProfileId,
            fixture.Book.LedgerBookId,
            AssetAccountingEventTypeNames.For(fixture.Request.EventKind),
            fixture.Request.EconomicEvent.EffectiveDate,
            fixture.Request.EventAmount,
            fixture.Request.Currency,
            IsPostingBalanced: true,
            SelectedRuleId: "rule-valuation",
            RuleMatches:
            [
                new AccountingRuleDryRunMatchDto(
                    "rule-valuation", "Valuation revaluation", "v2", 100,
                    IsMatched: true, Explanations: ["matched"], ValidationIssues: [])
            ],
            GeneratedLines: [],
            ValidationIssues: [],
            GeneratedPostingLines: projected.Lines.Select((line, index) => new GeneratedPostingLineDto(
                $"L{index + 1}", line.AccountId,
                line.Debit > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                "amount", line.Debit > 0m ? line.Debit : line.Credit, line.Currency, line.Dimensions)).ToArray());
    }

    private static AccountingPostingCandidateWriteResult BuildFactorPaydownCandidateWrite(
        PostingRuleJournalCandidateRequestDto request, Fixture fixture)
    {
        var result = BuildPreApprovedCandidateWrite(request);
        var write = result.Write!;
        var command = write.PostingCommand! with
        {
            ApprovalState = AccountingPostingApprovalStateDto.Pending,
            ApprovalId = null,
            Actor = request.Actor,
            ExpectedVersion = request.ExpectedPeriodVersion,
            BookContext = request.BookContext,
            BookPositionId = request.BookPositionId,
            EconomicEvent = request.EconomicEvent,
            ProjectionLineage = request.ProjectionLineage,
            RulePackReference = request.RulePackReference,
            LotFactorPaydown = request.AssetLotMutation!.FactorPaydown,
            Evidence = request.RetainedEvidence.Select(item => new AccountingPostingEvidenceReferenceDto(
                item.EvidenceId, item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem,
                item.RetainedAtUtc, item.RetainedBy, item.SubjectId, item.ContentHashSha256,
                SourceReference: item.SourceReference, Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc,
                EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus,
                SubjectType: item.SubjectType)).ToArray()
        };
        var generated = BuildFactorPaydownDryRun(fixture).GeneratedPostingLines;
        var lines = generated.Select(line =>
        {
            var dimensions = line.Dimensions!;
            var accountType = line.AccountPath switch
            {
                "Assets:Cash" or "Assets:Investment" => LedgerAccountType.Asset,
                "Income:Realized Gain" => LedgerAccountType.Revenue,
                _ => LedgerAccountType.Expense
            };
            return new LedgerEntry(Guid.NewGuid(), write.Entry.JournalEntryId, write.Entry.Timestamp,
                new LedgerAccount(line.AccountPath, accountType, FinancialAccountId: "custody-main"),
                line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m,
                write.Entry.Description,
                new LedgerLineDimensionSet(dimensions.FundId, dimensions.EntityId, dimensions.SleeveId,
                    dimensions.StrategyId, dimensions.InvestorId, dimensions.CapitalAccountId, dimensions.InstrumentId,
                    dimensions.TaxLotId, dimensions.CostCenterId, dimensions.CounterpartyId, dimensions.ExternalGlDimensions,
                    dimensions.OrganizationId, dimensions.PortfolioId, dimensions.BookId, dimensions.AccountId,
                    dimensions.CustomerId, dimensions.VendorId, dimensions.ProjectId)
                { PositionId = dimensions.PositionId });
        }).ToArray();
        var metadata = write.Entry.Metadata with
        {
            SecurityId = request.EconomicEvent!.SecurityId,
            IdempotencyKey = command.IdempotencyKey,
            FinancialAccountId = "custody-main"
        };
        var entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description, lines, metadata);
        return new AccountingPostingCandidateWriteResult(result.Candidate with
        {
            PostingCommand = command,
            GeneratedPostingLines = generated,
            TotalDebits = lines.Sum(line => line.Debit),
            TotalCredits = lines.Sum(line => line.Credit),
            BookContext = request.BookContext,
            BookPositionId = request.BookPositionId,
            EconomicEvent = request.EconomicEvent,
            ProjectionLineage = request.ProjectionLineage,
            RulePackReference = request.RulePackReference
        }, write with { PostingCommand = command, CommandId = command.CommandId, Entry = entry });
    }
}
