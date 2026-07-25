using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class AssetAccountingEventSpineServiceTests
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

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
