using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
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

public sealed class AssetAccountingEventSpineServiceTests
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CorporateAction_MapsProjectsDraftsAndPostsSuccessorsWithIndependentApprovalAndExactReplay()
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
        var sourceSecurity = baseline.Security with { Currency = "EUR" };
        var target = OpenLotSuccessorTestData.Successor(source, 1m);
        var targetSecurity = sourceSecurity with { SecurityId = target.Lot.SecurityId, Version = target.ExpectedSecurityVersion };
        target = target with { ExpectedSecurityHash = new string('b', 64) };
        var instruction = OpenLotSuccessorTestData.Build(source, [target],
            expectedPositionVersion: baseline.Position.Version, expectedSecurityVersion: sourceSecurity.Version,
            periodId: baseline.Period.PeriodId, expectedSecurityHash: new string('a', 64),
            expectedPeriodVersion: baseline.Period.Version);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(
            OpenLotSuccessorTestData.MapRequest(instruction, baseline.Request.Scope.Dimensions));
        mapped.IsMapped.Should().BeTrue("{0}", string.Join("; ", mapped.Blockers.Select(item => item.Message)));
        var fixture = baseline with
        {
            Request = mapped.Projection!.Event,
            Security = sourceSecurity,
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
            new LedgerAccount("Assets:Investment", LedgerAccountType.Asset, FinancialAccountId: "custody-main"), source.LotId, source.AcquiredDate,
            source.OriginalQuantity, source.OpenQuantity, source.Acquisition.TransactionCostBasis / source.OriginalQuantity,
            source.Acquisition.AcquisitionCurrency, fixture.Request.ProjectedAtUtc, fixture.Request.ProjectedAtUtc,
            Version: source.Version, SecurityId: source.SecurityId, BookPositionId: source.BookPositionId,
            Acquisition: source.Acquisition, BasisAdjustment: new OpenLotBasisAdjustmentDto(Guid.NewGuid(),
                OpenLotBasisAdjustmentReasons.Amortization, source.OpenQuantity, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis));
        sourceRecord.ToOpenLot().Should().BeEquivalentTo(source);

        var retained = new Dictionary<long, AssetAccountingEventProjectionRecord>();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var spine = call.ArgAt<AssetAccountingEventSpineDto>(0);
                retained.Add(spine.SpineVersion, new AssetAccountingEventProjectionRecord(spine, ComputeFingerprint(spine)));
                return new AssetAccountingEventAppendResult(spine, ComputeFingerprint(spine), false);
            });
        eventStore.GetAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => retained.GetValueOrDefault(call.ArgAt<long>(2)));
        eventStore.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => retained.Count == 0 ? null : retained[retained.Keys.Max()]);
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(source.BookPositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        positions.GetBookPositionAsync(target.Lot.BookPositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position with
        {
            PositionId = target.Lot.BookPositionId,
            SecurityId = target.Lot.SecurityId,
            Version = target.ExpectedBookPositionVersion
        });
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(source.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(sourceSecurity);
        securities.GetRecordedByIdAsOfAsync(target.Lot.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(targetSecurity);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>()).Returns(BuildMatchingDryRun(fixture));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetTaxLotsByIdsAsync(source.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<Guid>>(1).Contains(source.TaxLotRecordId) ? [sourceRecord] : []);
        ledger.GetLedgerBookAsync(source.LedgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            fixture.Book.LedgerBookId, fixture.Book.FundProfileId, fixture.Book.FundStructureNodeId, fixture.Book.FundStructureNodeKind,
            fixture.Book.DisplayName, fixture.Book.BaseCurrency, fixture.Book.CreatedAt, fixture.Book.UpdatedAt,
            AccountingBasis: fixture.Book.AccountingBasis, AccountingPolicyId: fixture.Book.AccountingPolicyId,
            AccountingPolicyVersion: fixture.Book.AccountingPolicyVersion));
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
            committed = new AtomicTaxLotJournalResult(command.MutationBatchId, command.MutationKind,
                command.CanonicalFingerprint, false, journal, [], [], command.RetainedEvidence, CorporateAction: command.CorporateAction);
            return committed;
        });
        ledger.GetByAggregateAsync(source.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(_ => committed is null ? [] : new[] { committed.Journal });
        var spineService = new AssetAccountingEventSpineService(eventStore, positions, securities, books, policies,
            configuration, authority, ledger);

        var projected = await spineService.ProjectAsync(fixture.Request);
        var omitSuccessors = () => spineService.BuildPostingCandidateAsync(BuildCandidateRequest(fixture));
        await omitSuccessors.Should().ThrowAsync<InvalidOperationException>();
        retained.Should().HaveCount(2, "omitting the retained successor plan must fail before Drafted is appended");
        var drafted = await spineService.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with
        {
            LotMutation = mapped.Projection.LotMutation
        });
        var candidate = drafted.Spine.DraftedCandidate!;
        var reviewed = candidate.AccountingTimestamp.AddMinutes(10);
        var approval = fixture.Request.RetainedEvidence.Single(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event) with
        {
            EvidenceId = "successor-posting-approval",
            EvidenceUri = "evidence://successors/approval",
            ReviewedBy = "approver",
            ReviewedAtUtc = reviewed,
            RetainedAtUtc = reviewed,
            SourceReference = "successor-approval",
            SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
            SubjectId = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(candidate.SourceEventId!.Value,
                candidate.EconomicEvent!.EventVersion, candidate.FundProfileId, source.LedgerBookId, candidate.PeriodId,
                candidate.AccountingBasis, "successor-approval", drafted.Spine.DraftedCandidateFingerprint!, candidate.TenantId, candidate.CompanyId)
        };
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate, "approver", "successor-approval")
        { ApprovalEvidence = [approval] };
        var postService = new AccountingPostingCandidatePostService(Substitute.For<IAccountingPostingCandidateWriteBuilder>(),
            ledger, atomic, eventStore, authority);

        var posted = await postService.PostCandidateAsync(request);
        var replay = await postService.PostCandidateAsync(request);

        projected.Spine.SpineVersion.Should().Be(2);
        drafted.Spine.DraftedLotMutation!.CorporateAction.Should().BeEquivalentTo(instruction);
        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.PostedJournal.Should().BeEquivalentTo(posted.PostedJournal);
        committedCommand!.MutationKind.Should().Be(AtomicTaxLotMutationKind.CorporateAction);
        committedCommand.CorporateAction.Should().BeEquivalentTo(instruction);
        committedCommand.Journal.PostingCommand!.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        committedCommand.Journal.PostingCommand.Actor.Should().Be("approver");
        committedCommand.Journal.Entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == "custody-main");
        committedCommand.Journal.Entry.Metadata.Tags![OpenLotSuccessors.JournalFingerprintTag]
            .Should().Be(OpenLotSuccessors.Fingerprint(instruction));
        retained.Should().HaveCount(5);
        retained[5].Projection.Stages.Select(item => item.Stage).Should().Equal(AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected, AssetAccountingLifecycleStageDto.Drafted,
            AssetAccountingLifecycleStageDto.Approved, AssetAccountingLifecycleStageDto.Posted);
        retained[5].Projection.TaxLotMutationBatchId.Should().Be(posted.TaxLotMutationBatchId);
        await atomic.Received(2).AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>());
    }

    private static AccountingPostingCandidateWriteResult BuildSuccessorCandidateWrite(
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
            LotCorporateAction = request.AssetLotMutation!.CorporateAction,
            Evidence = request.RetainedEvidence.Select(item => new AccountingPostingEvidenceReferenceDto(
                item.EvidenceId, item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem,
                item.RetainedAtUtc, item.RetainedBy, item.SubjectId, item.ContentHashSha256,
                SourceReference: item.SourceReference, Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc,
                EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus,
                SubjectType: item.SubjectType)).ToArray()
        };
        var generated = BuildMatchingDryRun(fixture).GeneratedPostingLines;
        var lines = write.Entry.Lines.Select((line, index) =>
        {
            var dimensions = generated[index].Dimensions!;
            return new LedgerEntry(line.EntryId, line.JournalEntryId,
                line.Timestamp, new LedgerAccount(generated[index].AccountPath, LedgerAccountType.Asset, FinancialAccountId: "custody-main"),
                line.Debit, line.Credit, line.Description,
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
            BookContext = request.BookContext,
            BookPositionId = request.BookPositionId,
            EconomicEvent = request.EconomicEvent,
            ProjectionLineage = request.ProjectionLineage,
            RulePackReference = request.RulePackReference
        }, write with { PostingCommand = command, CommandId = command.CommandId, Entry = entry });
    }

    [Fact]
    public async Task PostCandidateAsync_DuplicateTypedEvidenceIds_FailsBeforeLedgerRead()
    {
        var eventId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 6, 30);
        var reviewedAt = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        var source = new RetainedEvidenceIdentityDto(
            "duplicate-source", "evidence://asset/duplicate-source", new string('a', 64),
            "Custodian", "source-entity", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "controller", reviewedAt, effectiveDate, 1, reviewedAt.AddMinutes(1), "retention-service",
            AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var approvalSubject = AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
            eventId, "fund-alpha", bookId, null, null);
        var approval = source with
        {
            EvidenceId = "approval-1",
            EvidenceUri = "evidence://asset/approval-1",
            SourceSystem = "FinancialOperations",
            SourceReference = "approval-1",
            ReviewedBy = "approver",
            SubjectType = AssetAccountingEvidenceSubjects.PostingApproval,
            SubjectId = approvalSubject
        };
        var candidate = new PostingRuleJournalCandidateRequestDto(
            "fund-alpha", AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Valuation),
            100m, "USD", effectiveDate, "preparer", bookId, Guid.NewGuid(), reviewedAt,
            "valuation", LedgerBookId: bookId, SourceEventId: eventId)
        {
            RetainedEvidence = [source, source]
        };
        var ledger = Substitute.For<ILedgerJournalStore>();
        var service = new AccountingPostingCandidatePostService(
            Substitute.For<IAccountingPostingCandidateWriteBuilder>(), ledger);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            candidate, "approver", "approval-1")
        {
            ApprovalEvidence = [approval]
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*duplicate evidence ids*");
        await ledger.DidNotReceiveWithAnyArgs().GetLedgerBookAsync(default, default);
    }

    [Theory]
    [InlineData(AssetAccountingEventKindDto.Acquisition)]
    [InlineData(AssetAccountingEventKindDto.Capitalization)]
    [InlineData(AssetAccountingEventKindDto.Valuation)]
    [InlineData(AssetAccountingEventKindDto.Income)]
    [InlineData(AssetAccountingEventKindDto.CorporateAction)]
    [InlineData(AssetAccountingEventKindDto.Impairment)]
    [InlineData(AssetAccountingEventKindDto.DepreciationAmortization)]
    [InlineData(AssetAccountingEventKindDto.Disposal)]
    public async Task ProjectAsync_AllEventKinds_ResolveRecordedAuthorityAndAppendSeparateExpectedThenProjected(
        AssetAccountingEventKindDto eventKind)
    {
        var fixture = BuildFixture(eventKind);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.AppendAsync(
                Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<long>(), fixture.Position.Version, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var projection = call.ArgAt<AssetAccountingEventSpineDto>(0);
                var fingerprint = Convert.ToHexString(SHA256.HashData(
                        JsonSerializer.SerializeToUtf8Bytes(projection, CanonicalJsonOptions)))
                    .ToLowerInvariant();
                return new AssetAccountingEventAppendResult(projection, fingerprint, WasReplay: false);
            });
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>())
            .Returns(fixture.Position);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetRecordedByIdAsOfAsync(
                fixture.Security.SecurityId,
                fixture.Request.ProjectionLineage.GeneratedAtUtc,
                Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns([fixture.Period]);
        var service = new AssetAccountingEventSpineService(
            eventStore,
            positionStore,
            securityMaster,
            books,
            Substitute.For<IAccountingPolicyService>(),
            Substitute.For<IAccountingConfigurationService>(),
            Substitute.For<IAccountingPostingCandidateAuthorityBuilder>(),
            Substitute.For<ILedgerJournalStore>());

        var result = await service.ProjectAsync(fixture.Request);

        result.WasReplay.Should().BeFalse();
        result.Spine.Stages.Select(static stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected);
        await eventStore.Received(1).AppendAsync(
            Arg.Is<AssetAccountingEventSpineDto>(spine =>
                spine.SpineVersion == 1 &&
                spine.Stages.Count == 1 &&
                spine.Stages[0].Stage == AssetAccountingLifecycleStageDto.Expected &&
                spine.ProjectedEffect == null),
            0,
            fixture.Position.Version,
            Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            Arg.Is<AssetAccountingEventSpineDto>(spine =>
                spine.SpineVersion == 2 &&
                spine.Stages.Count == 2 &&
                spine.Stages[1].Stage == AssetAccountingLifecycleStageDto.Projected &&
                spine.ProjectedEffect != null),
            1,
            fixture.Position.Version,
            Arg.Any<CancellationToken>());
        await securityMaster.Received(1).GetRecordedByIdAsOfAsync(
            fixture.Security.SecurityId,
            fixture.Request.ProjectionLineage.GeneratedAtUtc,
            Arg.Any<CancellationToken>());
        await securityMaster.DidNotReceive().GetRecordedByIdAsOfAsync(
            fixture.Security.SecurityId,
            fixture.Request.ProjectedAtUtc,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProjectAsync_DuplicateEvidenceIds_FailsBeforeAnyAuthorityRead()
    {
        var fixture = BuildFixture();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        var service = new AssetAccountingEventSpineService(
            Substitute.For<IAssetAccountingEventProjectionStore>(),
            Substitute.For<IInstrumentPositionProjectionStore>(),
            securityMaster,
            Substitute.For<ILedgerBookService>(),
            Substitute.For<IAccountingPolicyService>(),
            Substitute.For<IAccountingConfigurationService>(),
            Substitute.For<IAccountingPostingCandidateAuthorityBuilder>(),
            Substitute.For<ILedgerJournalStore>());
        var request = fixture.Request with
        {
            RetainedEvidence = [fixture.Request.RetainedEvidence[0], fixture.Request.RetainedEvidence[0]]
        };

        var act = () => service.ProjectAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*duplicate evidence ids*");
        await securityMaster.DidNotReceiveWithAnyArgs()
            .GetRecordedByIdAsOfAsync(default, default, default);
    }

    [Fact]
    public async Task ProjectAsync_RewrittenEvidenceMetadata_FailsAgainstAuthoritativePositionSnapshot()
    {
        var fixture = BuildFixture();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>())
            .Returns(fixture.Position);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetRecordedByIdAsOfAsync(
                fixture.Security.SecurityId,
                fixture.Request.ProjectionLineage.GeneratedAtUtc,
                Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns([fixture.Period]);
        var service = new AssetAccountingEventSpineService(
            eventStore,
            positionStore,
            securityMaster,
            books,
            Substitute.For<IAccountingPolicyService>(),
            Substitute.For<IAccountingConfigurationService>(),
            Substitute.For<IAccountingPostingCandidateAuthorityBuilder>(),
            Substitute.For<ILedgerJournalStore>());
        var rewritten = fixture.Request.RetainedEvidence[0] with
        {
            EvidenceUri = "evidence://asset/caller-rewritten-location",
            ReviewedBy = "caller-rewritten-reviewer"
        };

        var act = () => service.ProjectAsync(fixture.Request with { RetainedEvidence = [rewritten] });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not retain every exact evidence identity*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task ProjectAsync_NonPositiveEventAmount_FailsBeforeAnyAuthorityRead(decimal eventAmount)
    {
        var fixture = BuildFixture();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        var service = BuildService(securityMaster: securityMaster);

        var act = () => service.ProjectAsync(fixture.Request with { EventAmount = eventAmount });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*positive asset accounting event amount*");
        await securityMaster.DidNotReceiveWithAnyArgs()
            .GetRecordedByIdAsOfAsync(default, default, default);
    }

    [Fact]
    public async Task BuildPostingCandidateAsync_NonPositiveEventAmount_FailsBeforeStoreRead()
    {
        var fixture = BuildFixture();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var service = BuildService(eventStore: eventStore);

        var act = () => service.BuildPostingCandidateAsync(
            BuildCandidateRequest(fixture) with { EventAmount = 0m });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*positive asset accounting event amount*");
        await eventStore.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
    }

    [Fact]
    public async Task BuildPostingCandidateAsync_MismatchedAmountAgainstRetainedSnapshot_FailsClosed()
    {
        var fixture = BuildFixture();
        var projectedSpine = BuildProjectedSpine(fixture);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.GetAsync(projectedSpine.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(projectedSpine, ComputeFingerprint(projectedSpine)));
        var service = BuildService(eventStore: eventStore);

        var act = () => service.BuildPostingCandidateAsync(
            BuildCandidateRequest(fixture) with { EventAmount = 250m });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*amount does not match the retained Projected snapshot*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Fact]
    public async Task BuildPostingCandidateAsync_ResolvesAllAuthoritiesAndDraftsPendingCandidate()
    {
        var fixture = BuildFixture();
        var projectedSpine = BuildProjectedSpine(fixture);
        var sourceFingerprint = ComputeFingerprint(projectedSpine);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.GetAsync(projectedSpine.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(projectedSpine, sourceFingerprint));
        eventStore.AppendAsync(
                Arg.Any<AssetAccountingEventSpineDto>(), 2, fixture.Position.Version, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var projection = call.ArgAt<AssetAccountingEventSpineDto>(0);
                return new AssetAccountingEventAppendResult(
                    projection, ComputeFingerprint(projection), WasReplay: false);
            });
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>())
            .Returns(fixture.Position);
        var request = BuildCandidateRequest(fixture);
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetRecordedByIdAsOfAsync(
                fixture.Security.SecurityId, request.AccountingTimestamp, Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns([fixture.Period]);
        var policyService = Substitute.For<IAccountingPolicyService>();
        policyService.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>())
            .Returns(BuildPolicy());
        var configurationService = Substitute.For<IAccountingConfigurationService>();
        configurationService.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(BuildMatchingDryRun(fixture));
        AssetAccountingCandidateAuthorityContext? capturedAuthority = null;
        var authorityBuilder = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        authorityBuilder.BuildAuthoritativeCandidateWriteAsync(
                Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedAuthority = call.ArgAt<AssetAccountingCandidateAuthorityContext>(1);
                return BuildPreApprovedCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0));
            });
        var service = new AssetAccountingEventSpineService(
            eventStore,
            positionStore,
            securityMaster,
            books,
            policyService,
            configurationService,
            authorityBuilder,
            Substitute.For<ILedgerJournalStore>());

        var result = await service.BuildPostingCandidateAsync(request);

        result.Candidate.PostingCommand!.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        result.Candidate.PostingCommand.ApprovalId.Should().BeNull();
        result.Spine.SpineVersion.Should().Be(3);
        result.Spine.Stages.Select(static stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected,
            AssetAccountingLifecycleStageDto.Drafted);
        result.Spine.PostedJournalImpact.Should().BeNull();
        result.Spine.TaxLotMutationBatchId.Should().BeNull();
        result.Spine.DraftedCandidate.Should().NotBeNull();
        result.Spine.DraftedCandidateFingerprint.Should().Be(
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(result.Spine.DraftedCandidate!));
        result.Spine.DraftedCandidateResultFingerprint.Should().Be(
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(result.Spine.DraftedCandidateResult!));
        capturedAuthority.Should().NotBeNull();
        capturedAuthority!.SourceProjectionFingerprint.Should().Be(sourceFingerprint);
        capturedAuthority.SourceSpineVersion.Should().Be(2);
        capturedAuthority.DraftSpineVersion.Should().Be(3);
        capturedAuthority.ExpectedBookPositionVersion.Should().Be(fixture.Position.Version);
        capturedAuthority.ExpectedPeriodVersion.Should().Be(fixture.Period.Version);
        capturedAuthority.RulePackId.Should().Be("pack-asset");
        capturedAuthority.RulePackVersion.Should().Be("v7");
        capturedAuthority.SecurityVersion.Should().Be(fixture.Security.Version,
            "the candidate builder stamps Security Master lineage at the version the spine resolved and asserted Active");
        await eventStore.Received(1).AppendAsync(
            Arg.Is<AssetAccountingEventSpineDto>(spine =>
                spine.SpineVersion == 3 &&
                spine.PostedJournalImpact == null &&
                spine.DraftedCandidate != null &&
                spine.DraftedCandidateResult != null),
            2,
            fixture.Position.Version,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AssetAccountingLifecycleStageDto.Expected)]
    [InlineData(AssetAccountingLifecycleStageDto.Projected)]
    [InlineData(AssetAccountingLifecycleStageDto.Drafted)]
    [InlineData(AssetAccountingLifecycleStageDto.Approved)]
    [InlineData(AssetAccountingLifecycleStageDto.Posted)]
    public async Task AppendLifecycleStageAsync_NonEvidenceOnlyStage_IsRejected(
        AssetAccountingLifecycleStageDto stage)
    {
        var fixture = BuildFixture();
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var service = BuildService(eventStore: eventStore);

        var act = () => service.AppendLifecycleStageAsync(
            BuildLifecycleRequest(fixture, stage));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Only Reconciled and Reported*");
        await eventStore.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default);
    }

    [Fact]
    public async Task AppendLifecycleStageAsync_ReconciledWithoutRetainedPostedAttestation_FailsClosed()
    {
        var fixture = BuildFixture();
        var projectedSpine = BuildProjectedSpine(fixture);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.GetAsync(projectedSpine.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(projectedSpine, ComputeFingerprint(projectedSpine)));
        var service = BuildService(eventStore: eventStore);

        var act = () => service.AppendLifecycleStageAsync(
            BuildLifecycleRequest(fixture, AssetAccountingLifecycleStageDto.Reconciled));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a retained Posted attestation*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData("Fifo", false, false)]
    [InlineData("Lifo", false, false)]
    [InlineData("Hifo", false, false)]
    [InlineData("SpecificId", false, false)]
    [InlineData("AverageCost", false, false)]
    [InlineData("Fifo", true, false)]
    [InlineData("Lifo", true, false)]
    [InlineData("Hifo", true, false)]
    [InlineData("SpecificId", true, false)]
    [InlineData("AverageCost", true, false)]
    [InlineData("Fifo", false, true)]
    [InlineData("Fifo", true, true)]
    public async Task BuildPostingCandidateAsync_AdjustedBasis_RetainsCurrentReliefAndAcquisitionFacts(
        string method, bool full, bool face)
    {
        const decimal adjustedBasis = 360.024691357802m;
        var quantity = full ? 8m : 4m;
        var expectedBasis = full ? adjustedBasis : 180.012345678901m;
        var fixture = BuildDisposalFixture(expectedBasis);
        var lot = BuildAdjustedDisposalLot(fixture, face, adjustedBasis);
        var instruction = BuildDisposalInstruction(fixture, lot, quantity, expectedBasis, method);
        var (service, _) = BuildDisposalService(fixture, [lot], [lot]);

        var result = await service.BuildPostingCandidateAsync(
            BuildCandidateRequest(fixture) with { LotMutation = instruction });

        var retained = result.Spine.DraftedLotMutation!.DisposalSelections.Should().ContainSingle().Subject;
        retained.ExpectedCostBasis.Should().Be(expectedBasis);
        retained.ExpectedUnitCost.Should().Be(25m);
        retained.ExpectedOpenQuantity.Should().Be(8m);
        retained.Quantity.Should().Be(quantity);
        lot.UnitCost.Should().Be(25m);
        lot.Acquisition!.FunctionalCostBasis.Should().Be(250m);
        lot.Acquisition.TransactionCostBasis.Should().Be(250m);
        lot.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1m);
        if (face)
        {
            lot.OriginalFace.Should().Be(1_000m);
            lot.BookedFactor.Should().Be(1m);
            lot.ParBasis.Should().Be(100m);
        }
    }

    [Fact]
    public async Task BuildPostingCandidateAsync_AverageCost_CertifiesCurrentWholePoolInExactAssetScope()
    {
        var fixture = BuildDisposalFixture(120m);
        var selected = BuildAdjustedDisposalLot(fixture, face: false, adjustedBasis: 360m);
        var survivor = BuildAdjustedDisposalLot(fixture, face: false, adjustedBasis: 120m) with
        {
            TaxLotRecordId = Guid.NewGuid(),
            LotId = "later-lot",
            AcquiredDate = new DateOnly(2026, 2, 1),
            UnitCost = 15m
        };
        survivor = survivor with
        {
            Acquisition = survivor.Acquisition! with
            {
                TransactionCostBasis = 150m,
                FunctionalCostBasis = 150m,
                HoldingPeriodStartDate = survivor.AcquiredDate,
                Evidence = [survivor.Acquisition.Evidence[0] with
                {
                    SubjectId = survivor.TaxLotRecordId.ToString("D"),
                    EffectiveDate = survivor.AcquiredDate
                }]
            }
        };
        var unrelated = selected with { TaxLotRecordId = Guid.NewGuid(), BookPositionId = Guid.NewGuid() };
        var future = selected with { TaxLotRecordId = Guid.NewGuid(), AcquiredDate = new DateOnly(2026, 7, 1) };
        var instruction = BuildDisposalInstruction(fixture, selected, 4m, 120m, "AverageCost");
        var (service, _) = BuildDisposalService(fixture, [selected], [selected, survivor, unrelated, future]);

        var result = await service.BuildPostingCandidateAsync(
            BuildCandidateRequest(fixture) with { LotMutation = instruction });

        var retained = result.Spine.DraftedLotMutation!.DisposalSelections.Should().ContainSingle().Subject;
        retained.ExpectedCostBasis.Should().Be(120m,
            "the pool has 480 current basis over 16 units, so four units relieve 120");
        retained.ExpectedUnitCost.Should().Be(25m);
        survivor.UnitCost.Should().Be(15m);
    }

    [Theory]
    [InlineData("basis")]
    [InlineData("unit cost")]
    [InlineData("version")]
    [InlineData("open quantity")]
    public async Task BuildPostingCandidateAsync_StaleDisposalAssertions_FailBeforeCandidateBuild(string stale)
    {
        var fixture = BuildDisposalFixture(180m);
        var lot = BuildAdjustedDisposalLot(fixture, face: false, adjustedBasis: 360m);
        var instruction = BuildDisposalInstruction(fixture, lot, 4m, 180m, "Fifo");
        var selection = instruction.DisposalSelections[0];
        selection = stale switch
        {
            "basis" => selection with { ExpectedCostBasis = 100m },
            "unit cost" => selection with { ExpectedUnitCost = 45m },
            "version" => selection with { ExpectedVersion = selection.ExpectedVersion - 1 },
            _ => selection with { ExpectedOpenQuantity = 10m }
        };
        var (service, builder) = BuildDisposalService(fixture, [lot], [lot]);

        var act = () => service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture) with
        {
            LotMutation = instruction with { DisposalSelections = [selection] }
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        await builder.DidNotReceiveWithAnyArgs().BuildAuthoritativeCandidateWriteAsync(default!, default!, default);
    }

    private static Fixture BuildDisposalFixture(decimal basis)
    {
        var fixture = BuildFixture(AssetAccountingEventKindDto.Disposal);
        var projected = fixture.Request.ProjectedEffect with
        {
            TotalDebits = basis,
            TotalCredits = basis,
            Lines =
            [
                fixture.Request.ProjectedEffect.Lines[0] with { AccountId = "Assets:Cash", Debit = basis },
                fixture.Request.ProjectedEffect.Lines[1] with { AccountId = "Assets:Investment", Credit = basis }
            ]
        };
        return fixture with { Request = fixture.Request with { EventAmount = basis, ProjectedEffect = projected } };
    }

    private static LedgerTaxLotRecord BuildAdjustedDisposalLot(Fixture fixture, bool face, decimal adjustedBasis)
    {
        var lotId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var acquiredDate = new DateOnly(2026, 1, 1);
        var acquisitionEvidence = fixture.Request.RetainedEvidence[0] with
        {
            SubjectType = "OpenLotAcquisition",
            SubjectId = lotId.ToString("D"),
            EffectiveDate = acquiredDate
        };
        var acquisition = new OpenLotAcquisitionDto(
            face ? LotQuantityBasis.Face : LotQuantityBasis.Units,
            "USD", "USD", 1m, 250m, 250m, acquiredDate,
            face ? new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.NoAmortization, null) : null,
            [acquisitionEvidence]);
        return new LedgerTaxLotRecord(lotId, fixture.Book.LedgerBookId,
            new LedgerAccount("Assets:Investment", LedgerAccountType.Asset), "adjusted-lot",
            acquiredDate, 10m, 8m, 25m, "USD", fixture.Request.ProjectedAtUtc,
            fixture.Request.ProjectedAtUtc, Version: 4, SecurityId: fixture.Security.SecurityId,
            BookPositionId: fixture.Position.PositionId,
            OriginalFace: face ? 1_000m : null, BookedFactor: face ? 1m : null, ParBasis: face ? 100m : null,
            Acquisition: acquisition,
            BasisAdjustment: new OpenLotBasisAdjustmentDto(Guid.NewGuid(),
                OpenLotBasisAdjustmentReasons.AverageCostRedistribution, 8m, adjustedBasis, adjustedBasis));
    }

    private static AssetLotMutationInstructionDto BuildDisposalInstruction(Fixture fixture,
        LedgerTaxLotRecord lot, decimal quantity, decimal basis, string method)
        => new(AssetLotMutationIntentDto.Dispose,
            DisposalSelections: [new AssetDisposalLotSelectionDto(lot.TaxLotRecordId, lot.LotId,
                lot.Version, lot.OpenQuantity, quantity, 0, fixture.Request.RetainedEvidence[0].EvidenceId,
                lot.UnitCost, basis)],
            ReliefMethod: method, PolicyRevision: "approved-policy-v2", AssetAccountId: lot.Account.ToString());

    private static (AssetAccountingEventSpineService Service, IAccountingPostingCandidateAuthorityBuilder Builder)
        BuildDisposalService(Fixture fixture, IReadOnlyList<LedgerTaxLotRecord> selectedLots,
            IReadOnlyList<LedgerTaxLotRecord> pool)
    {
        var spine = BuildProjectedSpine(fixture);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.GetAsync(spine.EventId, 1, 2, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(spine, ComputeFingerprint(spine)));
        eventStore.AppendAsync(Arg.Any<AssetAccountingEventSpineDto>(), 2, fixture.Position.Version,
                Arg.Any<CancellationToken>()).Returns(call =>
        {
            var next = call.ArgAt<AssetAccountingEventSpineDto>(0);
            return new AssetAccountingEventAppendResult(next, ComputeFingerprint(next), WasReplay: false);
        });
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        positionStore.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>())
            .Returns(fixture.Position);
        var security = Substitute.For<ISecurityMasterQueryService>();
        security.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId,
                BuildCandidateRequest(fixture).AccountingTimestamp, Arg.Any<CancellationToken>()).Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(BuildPolicy());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(BuildMatchingDryRun(fixture));
        var builder = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        builder.BuildAuthoritativeCandidateWriteAsync(Arg.Any<PostingRuleJournalCandidateRequestDto>(),
                Arg.Any<AssetAccountingCandidateAuthorityContext>(), Arg.Any<CancellationToken>())
            .Returns(call => BuildPreApprovedCandidateWrite(call.ArgAt<PostingRuleJournalCandidateRequestDto>(0)));
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetTaxLotsByIdsAsync(fixture.Book.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<CancellationToken>()).Returns(selectedLots);
        ledger.ListOpenTaxLotsAsync(fixture.Book.LedgerBookId, selectedLots[0].Account,
            Arg.Any<CancellationToken>()).Returns(pool);
        return (new AssetAccountingEventSpineService(eventStore, positionStore, security, books,
            policies, configuration, builder, ledger), builder);
    }

    private static AssetAccountingEventSpineService BuildService(
        IAssetAccountingEventProjectionStore? eventStore = null,
        ISecurityMasterQueryService? securityMaster = null)
        => new(
            eventStore ?? Substitute.For<IAssetAccountingEventProjectionStore>(),
            Substitute.For<IInstrumentPositionProjectionStore>(),
            securityMaster ?? Substitute.For<ISecurityMasterQueryService>(),
            Substitute.For<ILedgerBookService>(),
            Substitute.For<IAccountingPolicyService>(),
            Substitute.For<IAccountingConfigurationService>(),
            Substitute.For<IAccountingPostingCandidateAuthorityBuilder>(),
            Substitute.For<ILedgerJournalStore>());

    private static AssetAccountingEventSpineDto BuildProjectedSpine(Fixture fixture)
    {
        var request = fixture.Request;
        var expectedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Expected,
            request.ProjectedAtUtc,
            request.Actor,
            request.RetainedEvidence,
            $"economic-event://{request.EconomicEvent.EventId:D}/{request.EconomicEvent.EventVersion}");
        var projectedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Projected,
            request.ProjectedAtUtc,
            request.Actor,
            request.RetainedEvidence,
            $"projection-run://{request.ProjectionLineage.ProjectionRunId:D}");
        return new AssetAccountingEventSpineDto(
            request.EconomicEvent.EventId,
            request.EventKind,
            request.EconomicEvent.EventVersion,
            SpineVersion: 2,
            request.EconomicEvent.EffectiveDate,
            request.EventAmount,
            request.Currency,
            request.Scope,
            request.EconomicEvent,
            request.ProjectionLineage,
            request.RetainedEvidence,
            [expectedStage, projectedStage],
            request.ProjectedEffect);
    }

    private static string ComputeFingerprint(AssetAccountingEventSpineDto spine)
        => Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(spine, CanonicalJsonOptions)))
            .ToLowerInvariant();

    private static AssetAccountingPostingCandidateRequestDto BuildCandidateRequest(Fixture fixture)
        => new(
            fixture.Request.EventKind,
            fixture.Request.Scope,
            fixture.Request.EconomicEvent,
            fixture.Request.ProjectionLineage,
            fixture.Request.EventAmount,
            fixture.Request.Currency,
            "drafter",
            fixture.Request.ProjectedAtUtc.AddMinutes(10),
            "valuation draft",
            ExpectedSpineVersion: 2,
            ExpectedPeriodVersion: fixture.Period.Version,
            RetainedEvidence: fixture.Request.RetainedEvidence);

    private static AppendAssetAccountingLifecycleStageRequestDto BuildLifecycleRequest(
        Fixture fixture,
        AssetAccountingLifecycleStageDto stage)
    {
        var request = fixture.Request;
        var attestedAt = request.ProjectedAtUtc.AddHours(1);
        var subjectId = AssetAccountingEvidenceSubjects.LifecycleSubjectId(
            request.EconomicEvent.EventId,
            request.Scope.FundProfileId,
            request.Scope.LedgerBookId,
            request.Scope.TenantId,
            request.Scope.CompanyId,
            "reconciliation-case-1");
        var evidence = request.RetainedEvidence[0] with
        {
            EvidenceId = "reconciliation-evidence-1",
            EvidenceUri = "evidence://asset/reconciliation-evidence-1",
            SubjectType = stage == AssetAccountingLifecycleStageDto.Reported
                ? AssetAccountingEvidenceSubjects.Report
                : AssetAccountingEvidenceSubjects.Reconciliation,
            SubjectId = subjectId
        };
        return new AppendAssetAccountingLifecycleStageRequestDto(
            request.EconomicEvent.EventId,
            request.EconomicEvent.EventVersion,
            stage,
            ExpectedSpineVersion: 2,
            ExpectedBookPositionVersion: request.Scope.ExpectedBookPositionVersion,
            request.Scope.FundProfileId,
            "reconciler",
            attestedAt,
            "reconciliation-case-1",
            [evidence]);
    }

    private static AccountingPolicyDto BuildPolicy()
        => new(
            "gaap-v1",
            AccountingBasisKindDto.Gaap,
            "v1",
            "Fund Alpha GAAP",
            new DateOnly(2026, 1, 1),
            EffectiveTo: null,
            IsDefault: true,
            RulesJson: "{}",
            CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            RulePack: new AccountingPolicyRulePackDto(
                "pack-asset",
                "v7",
                [new AccountingPolicyRuleDto("rule-valuation", AccountingTreatmentKindDto.General, "v2")]));

    private static RuleDryRunResultDto BuildMatchingDryRun(Fixture fixture)
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
            GeneratedPostingLines:
            [
                new GeneratedPostingLineDto(
                    "L1", projected.Lines[0].AccountId, AccountingTemplateLineSideDto.Debit, "amount",
                    projected.Lines[0].Debit, projected.Lines[0].Currency, projected.Lines[0].Dimensions),
                new GeneratedPostingLineDto(
                    "L2", projected.Lines[1].AccountId, AccountingTemplateLineSideDto.Credit, "amount",
                    projected.Lines[1].Credit, projected.Lines[1].Currency, projected.Lines[1].Dimensions)
            ]);
    }

    private static AccountingPostingCandidateWriteResult BuildPreApprovedCandidateWrite(
        PostingRuleJournalCandidateRequestDto request)
    {
        var journalEntryId = Guid.NewGuid();
        var lineDimensions = new LedgerLineDimensionSet(
            FundId: request.FundProfileId,
            BookId: request.LedgerBookId?.ToString("D"));
        var entry = new JournalEntry(
            journalEntryId,
            request.AccountingTimestamp,
            request.Description,
            [
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, request.AccountingTimestamp,
                    new LedgerAccount("Assets:Investment", LedgerAccountType.Asset),
                    request.EventAmount, 0m, request.Description, lineDimensions),
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, request.AccountingTimestamp,
                    new LedgerAccount("Income:Unrealized", LedgerAccountType.Revenue),
                    0m, request.EventAmount, request.Description, lineDimensions)
            ],
            new JournalEntryMetadata(
                ActivityType: request.SourceEventType,
                LedgerBook: request.LedgerBookId?.ToString("D"),
                EffectiveDate: request.EffectiveDate));
        var command = new AccountingPostingCommandDto(
            Guid.NewGuid(),
            request.AggregateId,
            request.PeriodId,
            request.EffectiveDate,
            request.AccountingTimestamp,
            $"candidate:{request.SourceEventId:N}",
            SourceEventId: request.SourceEventId,
            CorrelationId: request.CorrelationId,
            SourceEventType: request.SourceEventType,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "pre-approved-1",
            LedgerBookId: request.LedgerBookId);
        var write = new LedgerJournalEntryWrite(
            entry,
            request.AggregateId,
            request.PeriodId,
            Guid.NewGuid(),
            request.CorrelationId,
            request.AccountingBasis,
            request.PolicyId ?? "gaap-v1",
            "v1",
            "rule-valuation",
            "v2",
            request.SourceEventId,
            request.SourceJournalEntryId,
            request.PostingKind,
            request.AdjustmentApproval,
            command,
            request.LedgerBookId);
        var dryRun = new RuleDryRunResultDto(
            request.FundProfileId,
            request.LedgerBookId,
            request.SourceEventType,
            request.EffectiveDate,
            request.EventAmount,
            request.Currency,
            IsPostingBalanced: true,
            SelectedRuleId: "rule-valuation",
            RuleMatches:
            [
                new AccountingRuleDryRunMatchDto(
                    "rule-valuation", "Valuation revaluation", "v2", 100,
                    IsMatched: true, Explanations: ["matched"], ValidationIssues: [])
            ],
            GeneratedLines: [],
            ValidationIssues: []);
        var candidate = new PostingRuleJournalCandidateResultDto(
            dryRun,
            "rule-valuation",
            "v2",
            GeneratedPostingLines: [],
            command,
            JournalEntryId: journalEntryId,
            TotalDebits: request.EventAmount,
            TotalCredits: request.EventAmount,
            Imbalance: 0m,
            IsBalanced: true,
            HasBlockingIssues: false,
            CanSubmitForApproval: true,
            CanPostWithoutAdditionalApproval: false,
            request.EvidenceLinks,
            Issues: []);
        return new AccountingPostingCandidateWriteResult(candidate, write);
    }

    private static Fixture BuildFixture(
        AssetAccountingEventKindDto eventKind = AssetAccountingEventKindDto.Valuation)
    {
        var eventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var securityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var positionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var roleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var bookId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var periodId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var ownerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var effectiveDate = new DateOnly(2026, 6, 30);
        var occurredAt = DateTimeOffset.Parse("2026-06-30T18:00:00Z");
        var generatedAt = occurredAt.AddMinutes(2);
        var projectedAt = generatedAt.AddMinutes(3);
        var hash = Convert.ToHexString(SHA256.HashData("valuation-source"u8.ToArray())).ToLowerInvariant();
        var evidence = new RetainedEvidenceIdentityDto(
            "valuation-source", "evidence://asset/valuation-source", hash,
            "Custodian", "valuation-source-entity", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "controller", occurredAt.AddSeconds(10), effectiveDate, 1, occurredAt.AddSeconds(20),
            "retention-service", AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var dimensions = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            InstrumentId: securityId,
            BookId: bookId.ToString("D"))
        {
            PositionId = positionId
        };
        var bookContext = new AccountingBookContextDto(
            bookId, "fund-alpha", ownerId, FundStructureNodeKindDto.Fund, "Fund Alpha GAAP",
            "USD", AccountingBasisKindDto.Gaap, "gaap-v1", "v1", periodId, dimensions);
        var position = new BookPositionDto(
            positionId, securityId, roleId, bookContext, BookPositionSides.Long, "Active",
            new DateOnly(2026, 1, 1), Version: 3)
        {
            RetainedEvidence = [evidence]
        };
        var security = new SecurityDetailDto(
            securityId, "Equity", SecurityStatusDto.Active, "Asset", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
            [], [], 2, occurredAt.AddDays(-1), null);
        var book = new LedgerBookDto(
            bookId, "fund-alpha", ownerId, FundStructureNodeKindDto.Fund, "Fund Alpha GAAP", "USD",
            occurredAt.AddDays(-30), occurredAt.AddDays(-30),
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-v1", AccountingPolicyVersion: "v1");
        var period = new LedgerPeriodDto(
            periodId, bookId, 2026, 6, "June 2026", new DateOnly(2026, 6, 1), effectiveDate,
            LedgerPeriodStatusDto.Open, occurredAt.AddDays(-30), null, 4,
            AccountingBasisKindDto.Gaap, "gaap-v1", "v1");
        var economicEvent = new EconomicEventReferenceDto(
            eventId, AssetAccountingEventTypeNames.For(eventKind), 1,
            effectiveDate, occurredAt, "AssetOperations", "valuation-source-entity", SourceContentHash: hash)
        {
            SecurityId = securityId,
            BookPositionId = positionId,
            RetainedEvidence = [evidence]
        };
        var lineage = new ProjectionLineageDto(
            Guid.NewGuid(), null, eventKind.ToString(), "v1", "engine-v1", "base", effectiveDate,
            generatedAt, "AssetOperations", "valuation-source-entity", economicEvent)
        {
            BookPositionId = positionId,
            RetainedEvidence = [evidence]
        };
        position = position with
        {
            OriginEvent = economicEvent,
            ProjectionLineage = lineage
        };
        var projected = new ProjectedAccountingEffectDto(
            lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, effectiveDate,
            100m, 100m, "USD",
            [
                new ProjectedAccountingEffectLineDto("Assets:Investment", 100m, 0m, "USD", Dimensions: dimensions),
                new ProjectedAccountingEffectLineDto("Income:Unrealized", 0m, 100m, "USD", Dimensions: dimensions)
            ]);
        var request = new ProjectAssetAccountingEventRequestDto(
            eventKind,
            new AssetAccountingEventScopeDto(
                securityId, 2, positionId, 3, bookId, periodId, AccountingBasisKindDto.Gaap,
                "fund-alpha", Dimensions: dimensions),
            economicEvent, lineage, projected, 100m, "USD", 4, "projector", projectedAt, [evidence]);
        return new Fixture(request, security, position, book, period);
    }

    private sealed record Fixture(
        ProjectAssetAccountingEventRequestDto Request,
        SecurityDetailDto Security,
        BookPositionDto Position,
        LedgerBookDto Book,
        LedgerPeriodDto Period);
}
