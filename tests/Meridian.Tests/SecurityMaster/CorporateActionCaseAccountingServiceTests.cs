using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The governed corporate-action accounting lane: attaching the exact-version projection binding,
/// maker-checker approval, and durable posting through the Asset Accounting Event Spine. Posting
/// must be refused with typed problem codes without exact scope, approved policy coverage, an open
/// period, balanced journals, valid lot mutations, retained evidence, and the required
/// maker-checker approval (exit criterion four of W9-CORPACT-011).
/// </summary>
public sealed class CorporateActionCaseAccountingServiceTests
{
    private static readonly Guid CaseId = Guid.Parse("3f2c4b7a-1e5d-4c88-9d21-73f2ab9de101");
    private static readonly Guid SecurityId = Guid.Parse("15c4a1de-7c40-4f83-9a5e-6dbb0182a202");
    private static readonly Guid EventId = Guid.Parse("9c81a5b2-4f7e-49f1-8d43-2ab6cc71e303");
    private static readonly Guid ProjectionId = Guid.Parse("64b3e7e5-9be6-4890-9df1-53a7f6f3fd22");
    private static readonly Guid ApprovalId = Guid.Parse("b1a6ff36-6a25-4b5f-8e51-b41d4d1f4c33");
    private static readonly Guid LedgerBookId = Guid.Parse("0e6f9d5c-58fb-4c76-95a3-3bb3b7a11e44");
    private static readonly Guid PeriodId = Guid.Parse("d1c2ba9f-19a4-4a58-8f1d-5d3a2f9be055");

    [Fact]
    public async Task AttachProjection_WithoutPrepareAuthority_IsRefusedBeforeAnyStoreWork()
    {
        var fixture = Fixture.Create();

        var act = () => fixture.Service.AttachProjectionAsync(
            AttachRequest() with { Authority = null });

        await act.Should().ThrowAsync<CorporateActionPermissionDeniedException>();
        await fixture.Store.DidNotReceiveWithAnyArgs().AttachAccountingProjectionAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task AttachProjection_WithoutTheSpineStore_FailsClosed()
    {
        var fixture = Fixture.Create(withSpineStore: false);

        var act = () => fixture.Service.AttachProjectionAsync(AttachRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
    }

    [Fact]
    public async Task AttachProjection_OutsideAccountingReview_IsRefused()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Detected);

        var act = () => fixture.Service.AttachProjectionAsync(AttachRequest());

        await act.Should().ThrowAsync<CorporateActionStateConflictException>();
    }

    [Fact]
    public async Task AttachProjection_WhenTheSpineMovedPastTheExactVersion_RefusesProjectionStale()
    {
        var fixture = Fixture.Create();

        var act = () => fixture.Service.AttachProjectionAsync(
            AttachRequest() with { ExpectedSpineVersion = 2 });

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ProjectionStale);
    }

    [Fact]
    public async Task AttachProjection_BindsTheRetainedDraftedSnapshotAndForwardsToTheStore()
    {
        var fixture = Fixture.Create();
        CorporateActionCaseAccountingProjectionDto? persisted = null;
        fixture.Store.AttachAccountingProjectionAsync(
                Arg.Any<AttachCorporateActionAccountingProjectionRequestDto>(),
                Arg.Do<CorporateActionCaseAccountingProjectionDto>(value => persisted = value),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new CorporateActionAccountingProjectionMutationResultDto(
                fixture.Case, callInfo.Arg<CorporateActionCaseAccountingProjectionDto>(), Replayed: false));

        await fixture.Service.AttachProjectionAsync(AttachRequest());

        persisted.Should().NotBeNull();
        persisted!.AccountingEventId.Should().Be(EventId);
        persisted.SpineVersion.Should().Be(3);
        persisted.DraftedCandidateFingerprint.Should().Be(fixture.DraftedCandidateFingerprint);
        persisted.LedgerBookId.Should().Be(LedgerBookId);
        persisted.PeriodId.Should().Be(PeriodId);
        persisted.ExpectedPeriodVersion.Should().Be(4);
        persisted.TotalDebits.Should().Be(120.50m);
        persisted.TotalCredits.Should().Be(120.50m);
        persisted.PreparedBy.Should().Be("fund-accountant");
        persisted.RulePackId.Should().Be("corp-act-pack");
        persisted.SelectedRuleId.Should().Be("cash-dividend");
    }

    [Fact]
    public async Task Approve_WithoutApproveAuthority_IsRefused()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval);

        var act = () => fixture.Service.ApproveAsync(ApproveRequest() with { Authority = null });

        await act.Should().ThrowAsync<CorporateActionPermissionDeniedException>();
    }

    [Fact]
    public async Task Approve_ByThePreparer_IsRefusedAsMakerChecker()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval);
        fixture.WithCurrentProjection();

        var act = () => fixture.Service.ApproveAsync(
            ApproveRequest() with { Actor = "Fund-Accountant" });

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
        await fixture.Store.DidNotReceiveWithAnyArgs().ApproveAccountingAsync(
            default!, default!, default, default!, default);
    }

    [Fact]
    public async Task Approve_TargetingASupersededProjection_RefusesProjectionStale()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval);
        fixture.WithCurrentProjection();

        var act = () => fixture.Service.ApproveAsync(
            ApproveRequest() with { ProjectionId = Guid.NewGuid() });

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ProjectionStale);
    }

    [Fact]
    public async Task Approve_ForwardsTheVersionedIdempotentCommandToTheStore()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval);
        fixture.WithCurrentProjection();
        fixture.Store.ApproveAccountingAsync(
                Arg.Any<ApproveCorporateActionCaseAccountingRequestDto>(),
                Arg.Any<CorporateActionCaseAccountingApprovalDto>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new CorporateActionAccountingApprovalResultDto(
                fixture.Case,
                callInfo.Arg<CorporateActionCaseAccountingApprovalDto>(),
                Transition(CorporateActionCaseStates.ReadyForApproval, CorporateActionCaseStates.Approved),
                Replayed: false));

        var result = await fixture.Service.ApproveAsync(ApproveRequest());

        result.Approval.ApprovedBy.Should().Be("controller");
        result.Approval.ProjectionId.Should().Be(ProjectionId);
        await fixture.Store.Received(1).ApproveAccountingAsync(
            Arg.Is<ApproveCorporateActionCaseAccountingRequestDto>(static value =>
                value.Actor == "controller" && value.ExpectedVersion == 6),
            Arg.Any<CorporateActionCaseAccountingApprovalDto>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_WithoutPostAuthority_IsRefused()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);

        var act = () => fixture.Service.PostAsync(PostRequest() with { Authority = null });

        await act.Should().ThrowAsync<CorporateActionPermissionDeniedException>();
    }

    [Fact]
    public async Task Post_WithoutComposedSpineAuthorities_FailsClosed()
    {
        var fixture = Fixture.Create(
            caseState: CorporateActionCaseStates.Approved, withPostingService: false);

        var act = () => fixture.Service.PostAsync(PostRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
    }

    [Fact]
    public async Task Post_WithoutAnActiveMakerCheckerApproval_IsRefused()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection();

        var act = () => fixture.Service.PostAsync(PostRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
        await fixture.PostingService.DidNotReceiveWithAnyArgs().PostCandidateAsync(default!, default);
    }

    [Theory]
    [InlineData(LedgerPeriodStatusDto.SoftClosed)]
    [InlineData(LedgerPeriodStatusDto.HardClosed)]
    public async Task Post_WhenThePeriodIsNotOpen_RefusesPeriodLocked(LedgerPeriodStatusDto status)
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection().WithActiveApproval().WithPeriod(status, version: 4);

        var act = () => fixture.Service.PostAsync(PostRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.PeriodLocked);
        await fixture.PostingService.DidNotReceiveWithAnyArgs().PostCandidateAsync(default!, default);
    }

    [Fact]
    public async Task Post_WhenThePeriodVersionIsStale_RefusesPeriodLocked()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection().WithActiveApproval().WithPeriod(LedgerPeriodStatusDto.Open, version: 9);

        var act = () => fixture.Service.PostAsync(PostRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.PeriodLocked);
    }

    [Fact]
    public async Task Post_ExecutesTheSpinePostingWithExactApprovalEvidenceAndRecordsTheImmutableJournal()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection().WithActiveApproval().WithPeriod(LedgerPeriodStatusDto.Open, version: 4);
        PostPostingRuleJournalCandidateRequestDto? spineCommand = null;
        fixture.PostingService.PostCandidateAsync(
                Arg.Do<PostPostingRuleJournalCandidateRequestDto>(value => spineCommand = value),
                Arg.Any<CancellationToken>())
            .Returns(fixture.PostedResult());
        CorporateActionCaseAccountingPostingDto? recorded = null;
        fixture.Store.RecordAccountingPostingAsync(
                Arg.Any<PostCorporateActionCaseAccountingRequestDto>(),
                Arg.Do<CorporateActionCaseAccountingPostingDto>(value => recorded = value),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new CorporateActionAccountingPostingResultDto(
                fixture.Case with { State = CorporateActionCaseStates.Posted, Version = 7 },
                callInfo.Arg<CorporateActionCaseAccountingPostingDto>(),
                Transition(CorporateActionCaseStates.Approved, CorporateActionCaseStates.Posted),
                Replayed: false));

        var result = await fixture.Service.PostAsync(PostRequest());

        spineCommand.Should().NotBeNull();
        spineCommand!.Candidate.Should().BeSameAs(fixture.Spine.DraftedCandidate);
        spineCommand.Actor.Should().Be("controller");
        spineCommand.ApprovalId.Should().Be(ApprovalId.ToString("D"));
        var evidence = spineCommand.ApprovalEvidence.Should().ContainSingle().Subject;
        evidence.ReviewedBy.Should().Be("controller");
        evidence.SubjectType.Should().Be(AssetAccountingEvidenceSubjects.PostingApproval);
        evidence.SubjectId.Should().Be(AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
            EventId,
            1,
            "fund-alpha",
            LedgerBookId,
            PeriodId,
            AccountingBasisKindDto.Gaap,
            ApprovalId.ToString("D"),
            fixture.DraftedCandidateFingerprint,
            "tenant-a",
            "company-a"));

        recorded.Should().NotBeNull();
        recorded!.JournalEntryId.Should().NotBeEmpty();
        recorded.PostingStatus.Should().Be("Posted");
        recorded.TotalDebits.Should().Be(recorded.TotalCredits);
        recorded.LedgerBookId.Should().Be(LedgerBookId);
        recorded.PeriodId.Should().Be(PeriodId);
        result.Case.State.Should().Be(CorporateActionCaseStates.Posted);
    }

    [Fact]
    public async Task Post_ReplaysTheCommittedReceiptWithoutReRunningTheSpine()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        var replayed = new CorporateActionAccountingPostingResultDto(
            fixture.Case with { State = CorporateActionCaseStates.Posted },
            Posting(),
            Transition(CorporateActionCaseStates.Approved, CorporateActionCaseStates.Posted),
            Replayed: true);
        fixture.Store.GetAccountingPostingReceiptAsync(
                CaseId, "post:v6", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(replayed);

        var result = await fixture.Service.PostAsync(PostRequest());

        result.Replayed.Should().BeTrue();
        await fixture.PostingService.DidNotReceiveWithAnyArgs().PostCandidateAsync(default!, default);
        await fixture.Store.DidNotReceiveWithAnyArgs().RecordAccountingPostingAsync(
            default!, default!, default, default!, default);
    }

    [Fact]
    public async Task Post_WhenTheSpineWasPostedUnderAForeignApproval_RefusesInsteadOfAdoptingTheJournal()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection().WithActiveApproval().WithPeriod(LedgerPeriodStatusDto.Open, version: 4);
        fixture.WithPostedSpine(approvalReference: Guid.NewGuid().ToString("D"));

        var act = () => fixture.Service.PostAsync(PostRequest());

        await act.Should().ThrowAsync<CorporateActionStateConflictException>();
        await fixture.PostingService.DidNotReceiveWithAnyArgs().PostCandidateAsync(default!, default);
    }

    [Fact]
    public async Task Post_WhenTheSpineAlreadyPostedThisApproval_RecoversTheDurableRecordWithoutReposting()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.Approved);
        fixture.WithCurrentProjection().WithActiveApproval().WithPeriod(LedgerPeriodStatusDto.Open, version: 4);
        fixture.WithPostedSpine(approvalReference: ApprovalId.ToString("D"));
        fixture.Store.RecordAccountingPostingAsync(
                Arg.Any<PostCorporateActionCaseAccountingRequestDto>(),
                Arg.Any<CorporateActionCaseAccountingPostingDto>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new CorporateActionAccountingPostingResultDto(
                fixture.Case with { State = CorporateActionCaseStates.Posted },
                callInfo.Arg<CorporateActionCaseAccountingPostingDto>(),
                Transition(CorporateActionCaseStates.Approved, CorporateActionCaseStates.Posted),
                Replayed: false));

        var result = await fixture.Service.PostAsync(PostRequest());

        result.Posting.PostingStatus.Should().Be("Posted");
        await fixture.PostingService.DidNotReceiveWithAnyArgs().PostCandidateAsync(default!, default);
        await fixture.Store.Received(1).RecordAccountingPostingAsync(
            Arg.Any<PostCorporateActionCaseAccountingRequestDto>(),
            Arg.Any<CorporateActionCaseAccountingPostingDto>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AttachCorporateActionAccountingProjectionRequestDto AttachRequest() =>
        new(
            CaseId,
            ExpectedVersion: 5,
            IdempotencyKey: "attach:v5",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            AccountingEventId: EventId,
            AccountingEventVersion: 1,
            ExpectedSpineVersion: 3,
            ProjectionInputHash: new string('a', 64),
            PostingIntentHash: new string('b', 64),
            PostingIdempotencyKey: $"corporate-action-posting/v1:{new string('c', 64)}",
            PolicyDecisionId: Guid.NewGuid(),
            PolicyDecisionVersion: 2,
            LotSnapshotId: Guid.NewGuid(),
            LotSnapshotVersion: 9,
            Actor: "fund-accountant",
            ScopeAssertion: Scope(),
            ProjectionId: ProjectionId,
            Authority: new CorporateActionCaseTransitionAuthorityDto(
                CanResolveTerms: false,
                CanRecordElection: false,
                CanPrepareAccounting: true,
                CanOverridePolicy: false,
                CanReopenCase: false));

    private static ApproveCorporateActionCaseAccountingRequestDto ApproveRequest() =>
        new(
            CaseId,
            ExpectedVersion: 6,
            IdempotencyKey: "approve:v6",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            ProjectionId: ProjectionId,
            Reason: "Reviewed the exact-version projection and evidence.",
            EvidenceReference: "document://approvals/corp-act-1",
            EvidenceHash: new string('e', 64),
            Actor: "controller",
            ScopeAssertion: Scope(),
            ApprovalId: ApprovalId,
            Authority: new CorporateActionAccountingDecisionAuthorityDto(
                CanApproveAccounting: true, CanPostAccounting: false));

    private static PostCorporateActionCaseAccountingRequestDto PostRequest() =>
        new(
            CaseId,
            ExpectedVersion: 6,
            IdempotencyKey: "post:v6",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            ProjectionId: ProjectionId,
            ApprovalId: ApprovalId,
            Reason: "Posting the approved corporate-action journals.",
            Actor: "controller",
            ScopeAssertion: Scope(),
            Authority: new CorporateActionAccountingDecisionAuthorityDto(
                CanApproveAccounting: false, CanPostAccounting: true));

    private static CorporateActionCaseScopeDto Scope() =>
        new(
            "tenant-a",
            "company-a",
            FundProfileId: "fund-alpha",
            LedgerBookId: LedgerBookId.ToString("D"),
            PeriodId: PeriodId.ToString("D"),
            AccountingBasis: "GAAP",
            FunctionalCurrency: "USD");

    private static CorporateActionCaseTransitionDto Transition(string fromState, string toState) =>
        new(Guid.NewGuid(), CaseId, fromState, toState, 6, 7, "controller", "reason", "key", DateTimeOffset.UtcNow);

    private static CorporateActionCaseAccountingPostingDto Posting() =>
        new(
            Guid.NewGuid(),
            CaseId,
            ProjectionId,
            ApprovalId,
            JournalEntryId: Guid.NewGuid(),
            LedgerBookId,
            PeriodId,
            "Gaap",
            "USD",
            120.50m,
            120.50m,
            "Posted",
            TaxLotMutationBatchId: null,
            "controller",
            DateTimeOffset.UtcNow);

    private sealed class Fixture
    {
        public required ICorporateActionOperationsStore Store { get; init; }
        public required IAssetAccountingEventProjectionStore SpineStore { get; init; }
        public required IAccountingPostingCandidatePostService PostingService { get; init; }
        public required ILedgerBookService LedgerBookService { get; init; }
        public required CorporateActionCaseAccountingService Service { get; init; }
        public required CorporateActionProcessingCaseDto Case { get; init; }
        public required AssetAccountingEventSpineDto Spine { get; set; }
        public required string DraftedCandidateFingerprint { get; init; }

        public static Fixture Create(
            string caseState = CorporateActionCaseStates.AccountingReview,
            bool withSpineStore = true,
            bool withPostingService = true)
        {
            var store = Substitute.For<ICorporateActionOperationsStore>();
            var spineStore = Substitute.For<IAssetAccountingEventProjectionStore>();
            var postingService = Substitute.For<IAccountingPostingCandidatePostService>();
            var ledgerBookService = Substitute.For<ILedgerBookService>();
            var service = new CorporateActionCaseAccountingService(
                store,
                withSpineStore ? spineStore : null,
                withPostingService ? postingService : null,
                ledgerBookService);

            var processingCase = new CorporateActionProcessingCaseDto(
                CaseId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                SecurityId,
                Scope(),
                caseState,
                Version: caseState == CorporateActionCaseStates.AccountingReview ? 5 : 6,
                MethodologyProfileId: null,
                AssignedTo: null,
                BlockedReason: null,
                "acceptor",
                DateTimeOffset.UtcNow.AddDays(-1),
                "acceptor",
                DateTimeOffset.UtcNow);
            store.GetCaseAsync(CaseId, "tenant-a", "company-a", Arg.Any<CancellationToken>())
                .Returns(processingCase);

            var candidate = BuildCandidate();
            var fingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate);
            var spine = BuildSpine(candidate, fingerprint);
            spineStore.GetLatestAsync(EventId, 1, Arg.Any<CancellationToken>())
                .Returns(new AssetAccountingEventProjectionRecord(spine, new string('f', 64)));

            return new Fixture
            {
                Store = store,
                SpineStore = spineStore,
                PostingService = postingService,
                LedgerBookService = ledgerBookService,
                Service = service,
                Case = processingCase,
                Spine = spine,
                DraftedCandidateFingerprint = fingerprint,
            };
        }

        public Fixture WithCurrentProjection()
        {
            Store.GetAccountingProjectionAsync(CaseId, "tenant-a", "company-a", Arg.Any<CancellationToken>())
                .Returns(new CorporateActionCaseAccountingProjectionDto(
                    ProjectionId,
                    CaseId,
                    BoundCaseVersion: 6,
                    EventId,
                    AccountingEventVersion: 1,
                    SpineVersion: 3,
                    new string('a', 64),
                    new string('b', 64),
                    $"corporate-action-posting/v1:{new string('c', 64)}",
                    DraftedCandidateFingerprint,
                    PolicyDecisionId: Guid.NewGuid(),
                    PolicyDecisionVersion: 2,
                    "corp-act-pack",
                    "7",
                    "cash-dividend",
                    "3",
                    LedgerBookId,
                    PeriodId,
                    ExpectedPeriodVersion: 4,
                    "Gaap",
                    "fund-alpha",
                    "USD",
                    new DateOnly(2026, 8, 14),
                    120.50m,
                    120.50m,
                    LotSnapshotId: Guid.NewGuid(),
                    LotSnapshotVersion: 9,
                    HasAuthoritativeLotResolution: true,
                    "fund-accountant",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    IsCurrent: true));
            return this;
        }

        public Fixture WithActiveApproval()
        {
            Store.GetAccountingApprovalAsync(CaseId, "tenant-a", "company-a", Arg.Any<CancellationToken>())
                .Returns(new CorporateActionCaseAccountingApprovalDto(
                    ApprovalId,
                    CaseId,
                    ProjectionId,
                    BoundCaseVersion: 6,
                    "controller",
                    DateTimeOffset.UtcNow.AddMinutes(-10),
                    "Reviewed the exact-version projection and evidence.",
                    "document://approvals/corp-act-1",
                    new string('e', 64)));
            return this;
        }

        public Fixture WithPeriod(LedgerPeriodStatusDto status, long version)
        {
            LedgerBookService.ListPeriodsAsync(
                    Arg.Is<LedgerPeriodQuery>(static query => query.LedgerBookId != null),
                    Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new LedgerPeriodDto(
                        PeriodId,
                        LedgerBookId,
                        2026,
                        8,
                        "2026-08",
                        new DateOnly(2026, 8, 1),
                        new DateOnly(2026, 8, 31),
                        status,
                        DateTimeOffset.UtcNow.AddMonths(-1),
                        ClosedAt: null,
                        version,
                        AccountingBasisKindDto.Gaap),
                });
            return this;
        }

        public Fixture WithPostedSpine(string approvalReference)
        {
            var stages = Spine.Stages!.ToList();
            stages.Add(new AssetAccountingStageEvidenceDto(
                AssetAccountingLifecycleStageDto.Approved,
                DateTimeOffset.UtcNow.AddMinutes(-4),
                "controller",
                ReferenceId: approvalReference));
            stages.Add(new AssetAccountingStageEvidenceDto(
                AssetAccountingLifecycleStageDto.Posted,
                DateTimeOffset.UtcNow.AddMinutes(-3),
                "controller",
                ReferenceId: EventId.ToString("D")));
            Spine = Spine with
            {
                SpineVersion = 5,
                Stages = stages,
                PostedJournalImpact = new PostedJournalImpactDto(
                    Guid.NewGuid(),
                    LedgerBookId,
                    PeriodId,
                    AccountingBasisKindDto.Gaap,
                    DateTimeOffset.UtcNow.AddMinutes(-3),
                    JournalPostingStatusDto.Posted,
                    "USD",
                    120.50m,
                    120.50m),
            };
            SpineStore.GetLatestAsync(EventId, 1, Arg.Any<CancellationToken>())
                .Returns(new AssetAccountingEventProjectionRecord(Spine, new string('f', 64)));
            return this;
        }

        public PostedPostingRuleJournalCandidateResultDto PostedResult() =>
            new(
                Spine.DraftedCandidateResult!,
                new PostedLedgerJournalEntryResultDto(
                    Guid.NewGuid(),
                    LedgerBookId,
                    AccountingBasisKindDto.Gaap,
                    PeriodId,
                    LedgerBookId,
                    CommandId: null,
                    SourceEventId: EventId,
                    CorrelationId: null))
            {
                JournalImpact = new PostedJournalImpactDto(
                    Guid.NewGuid(),
                    LedgerBookId,
                    PeriodId,
                    AccountingBasisKindDto.Gaap,
                    DateTimeOffset.UtcNow,
                    JournalPostingStatusDto.Posted,
                    "USD",
                    120.50m,
                    120.50m),
            };

        private static PostingRuleJournalCandidateRequestDto BuildCandidate()
        {
            var economicEvent = new EconomicEventReferenceDto(
                EventId,
                "AssetAccounting.CorporateAction",
                EventVersion: 1,
                new DateOnly(2026, 8, 14),
                new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
                "security-master");
            return new PostingRuleJournalCandidateRequestDto(
                "fund-alpha",
                "AssetAccounting.CorporateAction",
                120.50m,
                "USD",
                new DateOnly(2026, 8, 14),
                "fund-accountant",
                AggregateId: LedgerBookId,
                PeriodId,
                new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero),
                "Cash dividend corporate action",
                AccountingBasisKindDto.Gaap,
                LedgerBookId,
                SourceEventId: EventId,
                TenantId: "tenant-a",
                CompanyId: "company-a")
            {
                RetainedEvidence =
                [
                    new RetainedEvidenceIdentityDto(
                        "source-event-evidence",
                        "provider://event-100/v1",
                        new string('9', 64),
                        "alpaca",
                        "event-100",
                        RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                        "fund-accountant",
                        new DateTimeOffset(2026, 8, 14, 13, 0, 0, TimeSpan.Zero),
                        new DateOnly(2026, 8, 14),
                        EvidenceVersion: 1,
                        new DateTimeOffset(2026, 8, 14, 13, 5, 0, TimeSpan.Zero),
                        "fund-accountant",
                        AssetAccountingEvidenceSubjects.Event,
                        "event-subject"),
                ],
                EconomicEvent = economicEvent,
                RulePackReference = new AccountingRulePackReferenceDto(
                    "corp-act-pack", "7", "cash-dividend", "3"),
                ExpectedPeriodVersion = 4,
            };
        }

        private static AssetAccountingEventSpineDto BuildSpine(
            PostingRuleJournalCandidateRequestDto candidate,
            string fingerprint)
        {
            var scope = new AssetAccountingEventScopeDto(
                SecurityId,
                ExpectedSecurityVersion: 3,
                BookPositionId: Guid.NewGuid(),
                ExpectedBookPositionVersion: 11,
                LedgerBookId,
                PeriodId,
                AccountingBasisKindDto.Gaap,
                "fund-alpha",
                TenantId: "tenant-a",
                CompanyId: "company-a");
            var lineage = new ProjectionLineageDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "clearwater-corporate-action",
                "v1",
                "corporate-action-projection-v1",
                "base",
                new DateOnly(2026, 8, 14),
                new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero),
                "security-master",
                "event-100",
                candidate.EconomicEvent!);
            var candidateResult = new PostingRuleJournalCandidateResultDto(
                new RuleDryRunResultDto(
                    "fund-alpha",
                    LedgerBookId,
                    "AssetAccounting.CorporateAction",
                    new DateOnly(2026, 8, 14),
                    120.50m,
                    "USD",
                    IsPostingBalanced: true,
                    "cash-dividend",
                    RuleMatches: [],
                    GeneratedLines: [],
                    ValidationIssues: []),
                "cash-dividend",
                "3",
                GeneratedPostingLines:
                [
                    new GeneratedPostingLineDto("l1", "assets:cash", AccountingTemplateLineSideDto.Debit, "amount", 120.50m),
                    new GeneratedPostingLineDto("l2", "income:dividends", AccountingTemplateLineSideDto.Credit, "amount", 120.50m),
                ],
                PostingCommand: null,
                JournalEntryId: null,
                TotalDebits: 120.50m,
                TotalCredits: 120.50m,
                Imbalance: 0m,
                IsBalanced: true,
                HasBlockingIssues: false,
                CanSubmitForApproval: true,
                CanPostWithoutAdditionalApproval: false,
                EvidenceLinks: [],
                Issues: []);
            var stageTime = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
            return new AssetAccountingEventSpineDto(
                EventId,
                AssetAccountingEventKindDto.CorporateAction,
                EventVersion: 1,
                SpineVersion: 3,
                new DateOnly(2026, 8, 14),
                120.50m,
                "USD",
                scope,
                candidate.EconomicEvent!,
                lineage,
                RetainedEvidence: candidate.RetainedEvidence,
                Stages:
                [
                    new AssetAccountingStageEvidenceDto(
                        AssetAccountingLifecycleStageDto.Expected, stageTime.AddMinutes(-2), "fund-accountant"),
                    new AssetAccountingStageEvidenceDto(
                        AssetAccountingLifecycleStageDto.Projected, stageTime.AddMinutes(-1), "fund-accountant"),
                    new AssetAccountingStageEvidenceDto(
                        AssetAccountingLifecycleStageDto.Drafted, stageTime, "fund-accountant"),
                ],
                DraftedCandidate: candidate,
                DraftedCandidateResult: candidateResult,
                DraftedCandidateFingerprint: fingerprint,
                DraftedCandidateResultFingerprint:
                    AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateResult));
        }
    }
}
