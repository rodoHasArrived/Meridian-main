using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionOperationsServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("fe3036fe-b787-48d4-a91a-5e6ca97c2525");
    private static readonly Guid ProposalId = Guid.Parse("09f91987-20b0-448a-aa5d-7a04393cb57b");
    private static readonly DateOnly ExDate = new(2026, 8, 14);
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordSourceProposal_SynthesizedIdentity_IsQuarantinedAsNonActionableReview()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.RecordSourceProposalAsync(
                Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CorporateActionSourceProposalDto>(0));
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(),
            new CorporateActionProviderEventIdentityDto(
                "provider-a",
                "synthetic-0123456789abcdef0123456789abcdef",
                "unverified-content-0123456789abcdef01234567",
                Now,
                EvidenceHash: "0123456789abcdef",
                EvidenceReference: "non-authoritative-synthetic://corporate-actions/provider-a/observation"),
            Actor: "ingest");

        var result = await fixture.Service.RecordSourceProposalAsync(request);

        result.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        result.ActionAvailability.Should().NotBeNull();
        result.ActionAvailability!.CanAccept.Should().BeFalse();
        result.ActionAvailability.Blockers.Should().Contain(blocker =>
            blocker.Contains(CorporateActionProblemCodes.SpecialistReviewRequired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordSourceProposal_NativeIdentityWithoutCanonicalEvidence_IsNonActionableReview()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.RecordSourceProposalAsync(
                Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CorporateActionSourceProposalDto>(0));
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(),
            new CorporateActionProviderEventIdentityDto(
                "provider-a",
                "native-event-100",
                "native-version-1",
                Now,
                EvidenceHash: "sha256:not-a-canonical-digest",
                EvidenceReference: "untyped-reference"),
            Actor: "ingest");

        var result = await fixture.Service.RecordSourceProposalAsync(request);

        result.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        result.ActionAvailability!.CanAccept.Should().BeFalse();
        result.ActionAvailability.Blockers.Should().Contain(blocker =>
            blocker.Contains(CorporateActionProblemCodes.SpecialistReviewRequired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordSourceProposal_ReviewOnlyReleaseWithCanonicalEvidence_RemainsNonActionable()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.RecordSourceProposalAsync(
                Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CorporateActionSourceProposalDto>(0));
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(),
            new CorporateActionProviderEventIdentityDto(
                "tiingo",
                "native-event-100",
                "native-version-1",
                Now,
                EvidenceHash: new string('a', 64),
                EvidenceReference: "provider://event-100/v1",
                ReleaseStatus: CorporateActionProviderReleaseStatusDto.ReviewOnly),
            Actor: "ingest");

        var result = await fixture.Service.RecordSourceProposalAsync(request);

        result.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        result.ActionAvailability!.CanAccept.Should().BeFalse();
    }

    [Fact]
    public async Task RecordSourceProposal_EvidenceDerivedReplayKeyDoesNotReplaceNativeEventIdentity()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.RecordSourceProposalAsync(
                Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CorporateActionSourceProposalDto>(0));
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(),
            new CorporateActionProviderEventIdentityDto(
                "alpaca",
                "evidence-0123456789abcdef0123456789abcdef",
                $"evidence-{new string('a', 64)}",
                Now,
                EvidenceHash: new string('a', 64),
                EvidenceReference: "alpaca://corporate-actions/announcements/missing-id/versions/v1",
                ReleaseStatus: CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
            Actor: "ingest");

        var result = await fixture.Service.RecordSourceProposalAsync(request);

        result.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        result.ActionAvailability!.CanAccept.Should().BeFalse();
    }

    [Fact]
    public async Task RecordSourceProposal_ExplicitParentFromDifferentProviderEventFailsClosed()
    {
        var parent = SourceProposal(Dividend());
        var fixture = CreateFixture(parent);
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(0.25m),
            parent.ProviderIdentity with
            {
                SourceEventId = "different-event-200",
                SourceEventVersion = "v2",
            },
            Actor: "ingest",
            SupersedesProposalId: parent.ProposalId);

        var act = () => fixture.Service.RecordSourceProposalAsync(request);

        await act.Should().ThrowAsync<CorporateActionSourceConflictException>();
        await fixture.Store.DidNotReceive().RecordSourceProposalAsync(
            Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSourceProposal_ProviderIdentityAtUtf8ByteLimit_ReachesStore()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.RecordSourceProposalAsync(
                Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CorporateActionSourceProposalDto>(0));
        var request = new RecordCorporateActionSourceProposalRequestDto(
            Dividend(),
            new CorporateActionProviderEventIdentityDto(
                new string('p', CorporateActionOperationsService.MaximumIndexedIdentityUtf8Bytes),
                new string('e', CorporateActionOperationsService.MaximumIndexedIdentityUtf8Bytes),
                new string('v', CorporateActionOperationsService.MaximumIndexedIdentityUtf8Bytes),
                Now),
            Actor: "ingest");

        var result = await fixture.Service.RecordSourceProposalAsync(request);

        result.ProviderIdentity.ProviderId.Should().HaveLength(
            CorporateActionOperationsService.MaximumIndexedIdentityUtf8Bytes);
        await fixture.Store.Received(1).RecordSourceProposalAsync(
            Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ProviderId")]
    [InlineData("SourceEventId")]
    [InlineData("SourceEventVersion")]
    public async Task RecordSourceProposal_ProviderIdentityOverUtf8ByteLimit_FailsBeforeStore(string field)
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var oversized = new string('\u00e9', 129);
        var identity = new CorporateActionProviderEventIdentityDto(
            field == "ProviderId" ? oversized : "provider-a",
            field == "SourceEventId" ? oversized : "event-100",
            field == "SourceEventVersion" ? oversized : "v1",
            Now);

        var act = () => fixture.Service.RecordSourceProposalAsync(
            new RecordCorporateActionSourceProposalRequestDto(Dividend(), identity, Actor: "ingest"));

        await act.Should().ThrowAsync<CorporateActionValidationException>()
            .WithMessage("*256 UTF-8 bytes*");
        await fixture.Store.DidNotReceive().RecordSourceProposalAsync(
            Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectSourceProposal_IdempotencyKeyUsesPostTrimUtf8ByteLimit()
    {
        var proposal = SourceProposal(Dividend());
        var fixture = CreateFixture(proposal);
        fixture.Store.RejectSourceProposalAsync(
                Arg.Any<RejectCorporateActionSourceProposalRequestDto>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new CorporateActionSourceProposalDecisionResultDto(
                proposal with
                {
                    State = CorporateActionSourceProposalStates.Rejected,
                    Version = proposal.Version + 1,
                },
                Replayed: false));
        var maximumKey = new string('\u00e9', 128);
        var valid = new RejectCorporateActionSourceProposalRequestDto(
            ProposalId, 4, $" {maximumKey} ", "operations-user", "Duplicate provider notice.");

        await fixture.Service.RejectSourceProposalAsync(valid);

        await fixture.Store.Received(1).RejectSourceProposalAsync(
            Arg.Is<RejectCorporateActionSourceProposalRequestDto>(request => request.IdempotencyKey == maximumKey),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var act = () => fixture.Service.RejectSourceProposalAsync(
            valid with { IdempotencyKey = new string('\u00e9', 129) });

        await act.Should().ThrowAsync<CorporateActionValidationException>()
            .WithMessage("*256 UTF-8 bytes*");
        await fixture.Store.Received(1).RejectSourceProposalAsync(
            Arg.Any<RejectCorporateActionSourceProposalRequestDto>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInbox_ScopeIdentityEnforcesIndividualAndCompositeUtf8ByteLimits()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.Store.ListActionableSourceProposalsAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionSourceProposalDto>());
        fixture.Store.ListCasesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionProcessingCaseDto>());
        var segment = new string('s', CorporateActionOperationsService.MaximumScopeIdentityUtf8Bytes);
        var maximumScope = new CorporateActionCaseScopeDto(
            segment,
            segment,
            StructureNodeId: segment,
            FundProfileId: segment,
            FinancialAccountId: segment,
            PortfolioId: segment,
            CustodyAccountId: segment,
            LedgerBookId: segment);

        await fixture.Service.GetInboxAsync(maximumScope, take: 10);

        var aggregateAct = () => fixture.Service.GetInboxAsync(
            maximumScope with { PeriodId = "p" },
            take: 10);
        await aggregateAct.Should().ThrowAsync<CorporateActionScopeMismatchException>()
            .WithMessage("*2048 UTF-8 bytes in total*");

        var individualAct = () => fixture.Service.GetInboxAsync(
            maximumScope with
            {
                TenantId = new string('t', CorporateActionOperationsService.MaximumScopeIdentityUtf8Bytes + 1),
                StructureNodeId = null,
            },
            take: 10);
        await individualAct.Should().ThrowAsync<CorporateActionValidationException>()
            .WithMessage("*256 UTF-8 bytes*");
    }

    [Fact]
    public async Task UpsertOption_IndexedOptionCodeOverUtf8ByteLimit_FailsBeforeStore()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var request = new UpsertCorporateActionProcessingOptionRequestDto(
            CaseId: Guid.NewGuid(),
            ExpectedVersion: 1,
            IdempotencyKey: "option:v1",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            OptionCode: new string('\u00e9', 129),
            Label: "Direct exchange",
            Description: "Conserve book value and holding period.",
            State: CorporateActionProcessingOptionStates.Proposed,
            Actor: "operations-user");

        var act = () => fixture.Service.UpsertOptionAsync(request);

        await act.Should().ThrowAsync<CorporateActionValidationException>()
            .WithMessage("*256 UTF-8 bytes*");
        await fixture.Store.DidNotReceive().UpsertOptionAsync(
            Arg.Any<UpsertCorporateActionProcessingOptionRequestDto>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_SameIdempotencyKeyAndPayloadWithNewTrace_ReplaysCommittedOutcome()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        CorporateActionSourceProposalAcceptanceResultDto? committed = null;
        string? committedFingerprint = null;
        fixture.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0);
                var fingerprint = call.ArgAt<string>(5);
                if (committed is null)
                {
                    committedFingerprint = fingerprint;
                    committed = AcceptanceResult(
                        request,
                        call.ArgAt<Guid>(1),
                        call.ArgAt<Guid>(2),
                        call.ArgAt<Guid>(3),
                        call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4),
                        replayed: false);
                    return committed;
                }

                if (!string.Equals(committedFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new CorporateActionIdempotencyConflictException(request.ProposalId, request.IdempotencyKey);
                }

                return committed with { Replayed = true };
            });
        var request = AcceptRequest();

        var first = await fixture.Service.AcceptSourceProposalAsync(request);
        var replay = await fixture.Service.AcceptSourceProposalAsync(request with
        {
            CorrelationId = "trace-ca-retry",
        });

        first.Replayed.Should().BeFalse();
        replay.Replayed.Should().BeTrue();
        replay.CorporateAction.CorpActId.Should().Be(first.CorporateAction.CorpActId);
        replay.Case.CaseId.Should().Be(first.Case.CaseId);
        replay.Audit.AuditId.Should().Be(first.Audit.AuditId);
        await fixture.Store.Received(2).AcceptSourceProposalAsync(
            Arg.Is<AcceptCorporateActionSourceProposalRequestDto>(accepted =>
                accepted.ProposalId == ProposalId &&
                accepted.ExpectedVersion == 4 &&
                accepted.IdempotencyKey == "accept:proposal:v4" &&
                accepted.Scope.TenantId == "tenant-a" &&
                accepted.Scope.CompanyId == "company-a"),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            null,
            Arg.Is<string>(fingerprint => fingerprint == committedFingerprint),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_SameIdempotencyKeyWithChangedCommand_IsCollision()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        string? committedFingerprint = null;
        fixture.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0);
                var fingerprint = call.ArgAt<string>(5);
                if (committedFingerprint is not null && committedFingerprint != fingerprint)
                {
                    throw new CorporateActionIdempotencyConflictException(request.ProposalId, request.IdempotencyKey);
                }

                committedFingerprint = fingerprint;
                return AcceptanceResult(
                    request,
                    call.ArgAt<Guid>(1),
                    call.ArgAt<Guid>(2),
                    call.ArgAt<Guid>(3),
                    call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4),
                    replayed: false);
            });
        var request = AcceptRequest();
        await fixture.Service.AcceptSourceProposalAsync(request);

        var act = () => fixture.Service.AcceptSourceProposalAsync(request with { Reason = "changed command data" });

        var exception = await act.Should().ThrowAsync<CorporateActionIdempotencyConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.IdempotencyCollision);
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("version")]
    [InlineData("idempotency")]
    [InlineData("actor")]
    public async Task AcceptSourceProposal_RejectsWeakMutationIdentityBeforeReadingProposal(string invalidField)
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var valid = AcceptRequest();
        var request = invalidField switch
        {
            "proposal" => valid with { ProposalId = Guid.Empty },
            "version" => valid with { ExpectedVersion = 0 },
            "idempotency" => valid with { IdempotencyKey = "  " },
            "actor" => valid with { Actor = "  " },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        var act = () => fixture.Service.AcceptSourceProposalAsync(request);

        var exception = await act.Should().ThrowAsync<CorporateActionValidationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ValidationFailed);
        await fixture.Store.DidNotReceive().GetSourceProposalAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_RejectsProviderNameOnlyDissentWithoutFieldEvidence()
    {
        var proposal = SourceProposal(Dividend()) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            DisplayMetadata = new CorporateActionSourceDisplayMetadataDto(
                "ACME", "provider-a", ["provider-a"], ["provider-b"]),
        };
        var fixture = CreateFixture(proposal);

        var act = () => fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionSourceConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.SourceConflict);
        await fixture.Store.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "company-a")]
    [InlineData("tenant-a", "")]
    public async Task AcceptSourceProposal_RequiresExactTenantAndCompanyScope(string tenantId, string companyId)
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var request = AcceptRequest() with { Scope = new CorporateActionCaseScopeDto(tenantId, companyId) };

        var act = () => fixture.Service.AcceptSourceProposalAsync(request);

        var exception = await act.Should().ThrowAsync<CorporateActionScopeMismatchException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ScopeMismatch);
        await fixture.Store.DidNotReceive().GetSourceProposalAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptCorrectedSourceProposal_RetainsSupersedeAndRestatementHandoff()
    {
        var original = Dividend() with { CorpActId = Guid.Parse("e65d3a4c-dfb2-4d95-970e-cd08e49945ce") };
        var correction = Dividend(amount: 0.26m) with
        {
            CorpActId = Guid.Parse("96c5e926-398e-499a-b6ac-e79ed395ba0f"),
            SupersedesCorpActId = original.CorpActId,
        };
        var fixture = CreateFixture(SourceProposal(correction), [original]);
        fixture.RestatementTrigger.OnSupersededAsync(
                Arg.Any<CorporateActionDto>(),
                Arg.Any<CorporateActionDto>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new SecurityMasterRestatementDecision(RestatementRequired: true, Candidates: []));
        fixture.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => AcceptanceResult(
                call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0),
                call.ArgAt<Guid>(1),
                call.ArgAt<Guid>(2),
                call.ArgAt<Guid>(3),
                call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4),
                replayed: false,
                proposedAction: correction));
        var request = AcceptRequest() with
        {
            Scope = AcceptRequest().Scope with { FundProfileId = "fund-closed-period" },
        };

        var result = await fixture.Service.AcceptSourceProposalAsync(request);

        result.CorporateAction.CorpActId.Should().NotBe(original.CorpActId);
        result.CorporateAction.SupersedesCorpActId.Should().Be(original.CorpActId);
        result.Restatement.Should().NotBeNull();
        result.Restatement!.RestatementRequired.Should().BeTrue();
        result.Restatement.EvaluationStatus.Should().Be(
            CorporateActionRestatementEvaluationStates.PendingPeriodValidation);
        await fixture.RestatementTrigger.Received(1).OnSupersededAsync(
            Arg.Is<CorporateActionDto>(action =>
                action.SupersedesCorpActId == original.CorpActId &&
                action.DividendPerShare == 0.26m),
            Arg.Is<CorporateActionDto>(action => action == original),
            "fund-closed-period",
            "operations-user",
            "trace-ca",
            Arg.Any<CancellationToken>());
        await fixture.Store.Received(1).AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Is<Guid>(id => id == result.CorporateAction.CorpActId),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Is<SecurityMasterCorporateActionRestatementDto>(decision => decision.RestatementRequired),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceRevision_SkipsUnacceptedIntermediateToNearestAcceptedAncestor()
    {
        var acceptedAction = Dividend() with { CorpActId = Guid.NewGuid() };
        var root = SourceProposal(acceptedAction) with
        {
            ProposalId = Guid.NewGuid(),
            State = CorporateActionSourceProposalStates.Superseded,
            AcceptedCorporateActionId = acceptedAction.CorpActId,
            ProviderIdentity = SourceProposal(acceptedAction).ProviderIdentity with
            {
                SourceEventVersion = "v1",
            },
        };
        var intermediateAction = Dividend(0.25m) with
        {
            SupersedesCorpActId = acceptedAction.CorpActId,
        };
        var intermediate = SourceProposal(intermediateAction) with
        {
            ProposalId = Guid.NewGuid(),
            State = CorporateActionSourceProposalStates.Superseded,
            SupersedesProposalId = root.ProposalId,
            ProviderIdentity = root.ProviderIdentity with { SourceEventVersion = "v2" },
        };
        var tipAction = Dividend(0.26m) with { SupersedesCorpActId = acceptedAction.CorpActId };
        var tip = SourceProposal(tipAction) with
        {
            SupersedesProposalId = intermediate.ProposalId,
            ProviderIdentity = root.ProviderIdentity with { SourceEventVersion = "v3" },
        };
        var fixture = CreateFixture(tip, [acceptedAction]);
        fixture.Store.GetSourceProposalAsync(intermediate.ProposalId, Arg.Any<CancellationToken>())
            .Returns(intermediate);
        fixture.Store.GetSourceProposalAsync(root.ProposalId, Arg.Any<CancellationToken>())
            .Returns(root);
        fixture.RestatementTrigger.OnSupersededAsync(
                Arg.Any<CorporateActionDto>(),
                Arg.Any<CorporateActionDto>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new SecurityMasterRestatementDecision(RestatementRequired: true, Candidates: []));
        fixture.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => AcceptanceResult(
                call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0),
                call.ArgAt<Guid>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3),
                call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4),
                replayed: false,
                proposedAction: tipAction));

        var result = await fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        result.CorporateAction.SupersedesCorpActId.Should().Be(acceptedAction.CorpActId);
        await fixture.Store.Received(1).GetSourceProposalAsync(
            root.ProposalId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptEquivalentProviderProposals_UsesOneCanonicalActionAndScopedCaseIdentity()
    {
        var firstProposal = SourceProposal(Dividend());
        var secondProposalId = Guid.Parse("df188ee7-754c-44df-a9f3-a54120e7e5e8");
        var secondProposal = firstProposal with
        {
            ProposalId = secondProposalId,
            ProviderIdentity = firstProposal.ProviderIdentity with
            {
                ProviderId = "provider-b",
                SourceEventId = "provider-b-event-900",
            },
        };
        var first = CreateFixture(firstProposal);
        var second = CreateFixture(secondProposal);
        first.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => AcceptanceResult(
                call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0),
                call.ArgAt<Guid>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3),
                call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4), replayed: false));
        second.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => AcceptanceResult(
                call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0),
                call.ArgAt<Guid>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3),
                call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4), replayed: false));

        var firstResult = await first.Service.AcceptSourceProposalAsync(AcceptRequest());
        var secondResult = await second.Service.AcceptSourceProposalAsync(AcceptRequest() with
        {
            ProposalId = secondProposalId,
            IdempotencyKey = "accept:provider-b:v4",
        });

        secondResult.CorporateAction.CorpActId.Should().Be(firstResult.CorporateAction.CorpActId,
            "provider-neutral economics and lineage own canonical identity");
        secondResult.Case.CaseId.Should().Be(firstResult.Case.CaseId,
            "one canonical action and full scope own one processing case");
    }

    [Fact]
    public async Task AcceptSourceProposal_WithSynthesizedProviderIdentity_FailsClosed()
    {
        var proposal = SourceProposal(Dividend()) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            ProviderIdentity = new CorporateActionProviderEventIdentityDto(
                "provider-a",
                "synthetic-0123456789abcdef0123456789abcdef",
                "unverified-content-0123456789abcdef01234567",
                Now,
                EvidenceHash: "0123456789abcdef",
                EvidenceReference: "non-authoritative-synthetic://corporate-actions/provider-a/observation"),
        };
        var fixture = CreateFixture(proposal);

        var act = () => fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.SpecialistReviewRequired);
        await fixture.Store.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_WithReviewOnlyProviderRelease_FailsClosed()
    {
        var proposal = SourceProposal(Dividend()) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            ProviderIdentity = SourceProposal(Dividend()).ProviderIdentity with
            {
                ReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly,
            },
        };
        var fixture = CreateFixture(proposal);

        var act = () => fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.SpecialistReviewRequired);
        await fixture.Store.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInbox_ProjectsOnlyActionableProposalsWithTrustedAcceptanceScope()
    {
        var open = SourceProposal(Dividend()) with
        {
            DisplayMetadata = new CorporateActionSourceDisplayMetadataDto(
                "ACME", "provider-a", ["provider-a", "provider-b"], []),
        };
        var fixture = CreateFixture(open);
        fixture.Store.ListActionableSourceProposalsAsync(
                null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { open });
        fixture.Store.ListCasesAsync(
                "tenant-a", "company-a", null, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionProcessingCaseDto>());
        var scope = new CorporateActionCaseScopeDto(
            " tenant-a ", " company-a ", FundProfileId: " fund-a ", AccountingBasis: " gaap ",
            FunctionalCurrency: " usd ", Jurisdiction: " us ");

        var inbox = await fixture.Service.GetInboxAsync(scope, take: 25);

        var entry = inbox.Staged.Should().ContainSingle().Subject;
        entry.ProposalId.Should().Be(open.ProposalId);
        entry.Version.Should().Be(open.Version);
        entry.ProposalState.Should().Be(CorporateActionSourceProposalStates.Observed);
        entry.Ticker.Should().Be("ACME");
        entry.AcceptanceScope.Should().BeEquivalentTo(new CorporateActionCaseScopeDto(
            "tenant-a", "company-a", FundProfileId: "fund-a", AccountingBasis: "GAAP",
            FunctionalCurrency: "USD", Jurisdiction: "US"));
        entry.ActionAvailability.CanAccept.Should().BeTrue();
        await fixture.Store.Received(1).ListActionableSourceProposalsAsync(
            null, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInbox_AcceptedProposalLeavesStagingButItsFullyScopedCaseRemainsConsumable()
    {
        var accepted = SourceProposal(Dividend()) with
        {
            State = CorporateActionSourceProposalStates.Accepted,
            Version = 5,
            AcceptedCorporateActionId = Guid.Parse("1883d4f9-1b9d-4cb9-ae34-1e3b24ea00f9"),
            InitialCaseId = Guid.Parse("42dbb08f-c9a8-4de2-a863-865f7a55f0eb"),
        };
        var scope = new CorporateActionCaseScopeDto(
            "tenant-a",
            "company-a",
            StructureNodeId: "structure-a",
            FundProfileId: "fund-a",
            FinancialAccountId: "financial-account-a",
            PortfolioId: "portfolio-a",
            CustodyAccountId: "custody-a",
            LedgerBookId: "book-a",
            PeriodId: "period-2026-08",
            AccountingBasis: "GAAP",
            FunctionalCurrency: "USD",
            Jurisdiction: "US");
        var processingCase = new CorporateActionProcessingCaseDto(
            accepted.InitialCaseId!.Value,
            accepted.ProposalId,
            accepted.AcceptedCorporateActionId!.Value,
            SecurityId,
            scope,
            CorporateActionCaseStates.AccountingReview,
            Version: 6,
            CorporateActionOperationsService.ClearwaterMethodologyProfileId,
            AssignedTo: "accountant-a",
            BlockedReason: null,
            CreatedBy: "operations-user",
            CreatedAtUtc: Now.AddHours(-1),
            UpdatedBy: "accountant-a",
            UpdatedAtUtc: Now,
            SourceSnapshot: new CorporateActionCaseSourceSnapshotDto(
                accepted.ProposedAction,
                accepted.ProviderIdentity,
                accepted.DisplayMetadata));
        var fixture = CreateFixture(accepted);
        fixture.Store.ListActionableSourceProposalsAsync(
                null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionSourceProposalDto>());
        fixture.Store.ListCasesAsync(
                "tenant-a", "company-a", null, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { processingCase });

        var inbox = await fixture.Service.GetInboxAsync(scope, take: 25);
        var roundTripped = JsonSerializer.Deserialize<CorporateActionDurableInboxDto>(
            JsonSerializer.Serialize(inbox, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        inbox.Staged.Should().BeEmpty("accepted source proposals leave the decision queue");
        roundTripped.Should().NotBeNull();
        var retainedCase = roundTripped!.Cases.Should().ContainSingle().Subject;
        retainedCase.CaseId.Should().Be(processingCase.CaseId);
        retainedCase.ProposalId.Should().Be(accepted.ProposalId);
        retainedCase.Version.Should().Be(6);
        retainedCase.Scope.Should().BeEquivalentTo(scope);
        retainedCase.SourceSnapshot.Should().NotBeNull();
        retainedCase.SourceSnapshot!.ProposedAction.Should().BeEquivalentTo(accepted.ProposedAction);
        retainedCase.SourceSnapshot.ProviderIdentity.Should().BeEquivalentTo(accepted.ProviderIdentity);
        retainedCase.SourceSnapshot.DisplayMetadata.Should().BeEquivalentTo(accepted.DisplayMetadata);
        retainedCase.ActionAvailability.Should().NotBeNull();
        retainedCase.ActionAvailability!.AllowedTransitionTargets
            .Should().Contain(CorporateActionCaseStates.RestatementRequired);
        retainedCase.ActionAvailability.AllowedTransitionTargets
            .Should().NotContain(CorporateActionCaseStates.ReadyForApproval,
            "approval readiness is unavailable without a durable exact-version projection");
    }

    [Fact]
    public async Task CaseConflicts_AreReadThroughNormalizedTenantCompanyScope()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var caseId = Guid.Parse("e9487d84-fafa-4e06-9a20-d95d3b318bb1");
        var conflict = Conflict(caseId);
        fixture.Store.GetConflictAsync(
                caseId,
                conflict.ConflictId,
                "tenant-a",
                "company-a",
                Arg.Any<CancellationToken>())
            .Returns(conflict);
        fixture.Store.ListConflictsAsync(
                caseId,
                "tenant-a",
                "company-a",
                CorporateActionConflictStates.Open,
                25,
                Arg.Any<CancellationToken>())
            .Returns(new[] { conflict });

        var loaded = await fixture.Service.GetConflictAsync(
            caseId, conflict.ConflictId, " tenant-a ", " company-a ");
        var listed = await fixture.Service.ListConflictsAsync(
            caseId, " tenant-a ", " company-a ", " Open ", 25);

        loaded.Should().Be(conflict);
        listed.Should().ContainSingle().Which.Should().Be(conflict);
        await fixture.Store.Received(1).GetConflictAsync(
            caseId,
            conflict.ConflictId,
            "tenant-a",
            "company-a",
            Arg.Any<CancellationToken>());
        await fixture.Store.Received(1).ListConflictsAsync(
            caseId,
            "tenant-a",
            "company-a",
            CorporateActionConflictStates.Open,
            25,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListCaseConflicts_RejectsUnknownStateBeforeStoreRead()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var caseId = Guid.Parse("e9487d84-fafa-4e06-9a20-d95d3b318bb1");

        var act = () => fixture.Service.ListConflictsAsync(
            caseId, "tenant-a", "company-a", "ResolvedElsewhere", 25);

        var exception = await act.Should().ThrowAsync<CorporateActionValidationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ValidationFailed);
        await fixture.Store.DidNotReceive().ListConflictsAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Blocked)]
    [InlineData(CorporateActionCaseStates.RestatementRequired)]
    public async Task CaseAvailability_FailsClosedOnTermsDependentReturnFromCorrectionLane(string state)
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var processingCase = AcceptanceResult(
            AcceptRequest(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false).Case with
        {
            State = state,
            Version = 4,
        };
        fixture.Store.ListCasesAsync(
                "tenant-a",
                "company-a",
                null,
                null,
                25,
                Arg.Any<CancellationToken>())
            .Returns(new[] { processingCase });

        var cases = await fixture.Service.ListCasesAsync(
            "tenant-a", "company-a", securityId: null, state: null, take: 25);

        var availability = cases.Should().ContainSingle().Subject.ActionAvailability;
        availability.Should().NotBeNull();
        availability!.AllowedTransitionTargets.Should().NotContain(
            CorporateActionCaseStates.AccountingReview,
            "the compact case read does not prove retained evidence and zero open conflicts");
        availability.Blockers.Should().Contain(blocker =>
            blocker.Contains(CorporateActionProblemCodes.TermsIncomplete, StringComparison.Ordinal)
            && blocker.Contains(CorporateActionCaseStates.AccountingReview, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.ElectionPending)]
    [InlineData(CorporateActionCaseStates.AccountingReview)]
    public async Task CaseAvailability_DoesNotAdvertiseConflictRecordingAfterTermsConfirmation(string state)
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var processingCase = AcceptanceResult(
            AcceptRequest(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false).Case with
        {
            State = state,
            Version = 4,
        };
        fixture.Store.ListCasesAsync(
                "tenant-a",
                "company-a",
                null,
                null,
                25,
                Arg.Any<CancellationToken>())
            .Returns(new[] { processingCase });

        var cases = await fixture.Service.ListCasesAsync(
            "tenant-a", "company-a", securityId: null, state: null, take: 25);

        cases.Should().ContainSingle().Subject.ActionAvailability!
            .CanRecordConflict.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveConflict_RequiresDurableEvidenceAndForwardsVersionedIdempotentCommand()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var caseId = Guid.Parse("e9487d84-fafa-4e06-9a20-d95d3b318bb1");
        var conflictId = Guid.Parse("de53c04b-041f-461c-8aa0-1710d330e941");
        var request = new ResolveCorporateActionConflictRequestDto(
            caseId,
            conflictId,
            ExpectedVersion: 3,
            IdempotencyKey: "resolve-conflict:v3",
            TenantId: " tenant-a ",
            CompanyId: " company-a ",
            Disposition: CorporateActionConflictStates.Waived,
            Resolution: " Issuer notice governs. ",
            EvidenceReference: "document://issuer-notices/notice-1",
            EvidenceHash: new string('b', 64),
            Actor: " controller ",
            CorrelationId: "trace-a");
        fixture.Store.ResolveConflictAsync(
                Arg.Any<ResolveCorporateActionConflictRequestDto>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var trusted = call.ArgAt<ResolveCorporateActionConflictRequestDto>(0);
                var processingCase = AcceptanceResult(
                    AcceptRequest(), Guid.NewGuid(), caseId, Guid.NewGuid(), null, false).Case with
                {
                    Version = 4,
                };
                var conflict = new CorporateActionConflictDto(
                    conflictId, caseId, "CashAmount", "Provider values differ", [],
                    trusted.Disposition, trusted.Resolution, 4, "ingest", Now,
                    trusted.Actor, Now, trusted.EvidenceReference, trusted.EvidenceHash);
                return new CorporateActionConflictResolutionResultDto(processingCase, conflict, false);
            });

        var result = await fixture.Service.ResolveConflictAsync(request);

        result.Conflict.State.Should().Be(CorporateActionConflictStates.Waived);
        result.Conflict.ResolvedBy.Should().Be("controller");
        await fixture.Store.Received(1).ResolveConflictAsync(
            Arg.Is<ResolveCorporateActionConflictRequestDto>(value =>
                value.ExpectedVersion == 3
                && value.IdempotencyKey == "resolve-conflict:v3"
                && value.TenantId == "tenant-a"
                && value.CompanyId == "company-a"
                && value.Resolution == "Issuer notice governs."),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveConflict_RejectsUntrustedEvidenceReference()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var request = new ResolveCorporateActionConflictRequestDto(
            Guid.NewGuid(), Guid.NewGuid(), 1, "resolve:v1", "tenant-a", "company-a",
            CorporateActionConflictStates.Resolved, "Compared notices.",
            "arbitrary://unretained", new string('a', 64), "resolver");

        var act = () => fixture.Service.ResolveConflictAsync(request);

        await act.Should().ThrowAsync<CorporateActionValidationException>();
        await fixture.Store.DidNotReceive().ResolveConflictAsync(
            Arg.Any<ResolveCorporateActionConflictRequestDto>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransitionCase_ReadyForApprovalWithoutDurableProjection_FailsClosed()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var request = new TransitionCorporateActionCaseRequestDto(
            CaseId: Guid.Parse("e9487d84-fafa-4e06-9a20-d95d3b318bb1"),
            ExpectedVersion: 3,
            IdempotencyKey: "ready:v3",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            ToState: CorporateActionCaseStates.ReadyForApproval,
            Actor: "fund-accountant",
            Reason: "Accounting projection reviewed.",
            Authority: new CorporateActionCaseTransitionAuthorityDto(
                CanResolveTerms: false,
                CanRecordElection: false,
                CanPrepareAccounting: true,
                CanOverridePolicy: false,
                CanReopenCase: false));

        var act = () => fixture.Service.TransitionCaseAsync(request);

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ProjectionStale);
        await fixture.Store.DidNotReceive().TransitionCaseAsync(
            Arg.Any<TransitionCorporateActionCaseRequestDto>(),
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_WithoutAComposedScopeAuthority_FailsClosedBeforeTheStore()
    {
        // The decision boundary is closed by the absence of the authority, not by a setting. A
        // deployment that cannot enumerate affected scopes must not open a case in one of them.
        var store = Substitute.For<ICorporateActionOperationsStore>();
        store.GetSourceProposalAsync(ProposalId, Arg.Any<CancellationToken>())
            .Returns(SourceProposal(Dividend()));
        var service = new CorporateActionOperationsService(
            store,
            Substitute.For<ISecurityMasterEventStore>(),
            Substitute.For<ISecurityMasterStore>(),
            Substitute.For<ICorporateActionRestatementTrigger>(),
            scopeFanOut: null);

        var act = () => service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
        await store.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_WhenTheFactReachesSeveralScopes_RefusesRatherThanCasingOne()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.ScopeFanOut.ResolveDecisionScopeAsync(
                Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.MultiScope,
                CorporateActionScopeFanOutGate.MultiScopeBlocker));

        var act = () => fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.DownstreamAuthorityRequired);
        await fixture.Store.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_WhenTheFactReachesAnotherTenant_FailsClosedAsScopeMismatch()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.ScopeFanOut.ResolveDecisionScopeAsync(
                Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.ForeignScope,
                CorporateActionScopeFanOutGate.ForeignScopeBlocker));

        var act = () => fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        var exception = await act.Should().ThrowAsync<CorporateActionOperationException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.ScopeMismatch);
    }

    [Fact]
    public async Task AcceptSourceProposal_StampsTheServerResolvedNarrowScopeOnTheStoreCommand()
    {
        var fixture = CreateFixture(SourceProposal(Dividend()));
        fixture.ScopeFanOut.ResolveDecisionScopeAsync(
                Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new CorporateActionScopeFanOutDecision(
                true,
                new CorporateActionCaseScopeDto(
                    "tenant-a",
                    "company-a",
                    StructureNodeId: null,
                    FundProfileId: "fund-1",
                    FinancialAccountId: "account-1",
                    PortfolioId: "portfolio-1",
                    CustodyAccountId: null,
                    LedgerBookId: "book-1",
                    PeriodId: null,
                    AccountingBasis: null,
                    FunctionalCurrency: "USD"),
                CorporateActionScopeFanOutRefusal.None,
                []));
        fixture.Store.AcceptSourceProposalAsync(
                Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => AcceptanceResult(
                call.ArgAt<AcceptCorporateActionSourceProposalRequestDto>(0),
                call.ArgAt<Guid>(1),
                call.ArgAt<Guid>(2),
                call.ArgAt<Guid>(3),
                call.ArgAt<SecurityMasterCorporateActionRestatementDto?>(4),
                replayed: false));

        // The caller supplied tenant/company only; every narrower field is the authority's.
        await fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        await fixture.Store.Received(1).AcceptSourceProposalAsync(
            Arg.Is<AcceptCorporateActionSourceProposalRequestDto>(accepted =>
                accepted.Scope.FundProfileId == "fund-1" &&
                accepted.Scope.FinancialAccountId == "account-1" &&
                accepted.Scope.PortfolioId == "portfolio-1" &&
                accepted.Scope.LedgerBookId == "book-1" &&
                accepted.Scope.FunctionalCurrency == "USD"),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<SecurityMasterCorporateActionRestatementDto?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptSourceProposal_CommittedRetryReplaysWithoutReAskingTheScopeAuthority()
    {
        // Holdings move. A committed acceptance must still return its original receipt, so the
        // replay path is deliberately ahead of the authority rather than behind it.
        var fixture = CreateFixture(SourceProposal(Dividend()));
        var committed = AcceptanceResult(
            AcceptRequest(),
            Guid.Parse("6f2c1c58-9c0e-4a41-8f2f-2b4b2a4a0f11"),
            Guid.Parse("6f2c1c58-9c0e-4a41-8f2f-2b4b2a4a0f22"),
            Guid.Parse("6f2c1c58-9c0e-4a41-8f2f-2b4b2a4a0f33"),
            restatement: null,
            replayed: true);
        fixture.Store.GetAcceptanceReceiptAsync(
                ProposalId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(committed);
        fixture.ScopeFanOut.ResolveDecisionScopeAsync(
                Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.NoAffectedScope,
                CorporateActionScopeFanOutGate.NoAffectedScopeBlocker));

        var replay = await fixture.Service.AcceptSourceProposalAsync(AcceptRequest());

        replay.Replayed.Should().BeTrue();
        await fixture.ScopeFanOut.DidNotReceive().ResolveDecisionScopeAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture(
        CorporateActionSourceProposalDto proposal,
        IReadOnlyList<CorporateActionDto>? existingActions = null)
    {
        var store = Substitute.For<ICorporateActionOperationsStore>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var securityMasterStore = Substitute.For<ISecurityMasterStore>();
        var restatementTrigger = Substitute.For<ICorporateActionRestatementTrigger>();
        store.GetSourceProposalAsync(proposal.ProposalId, Arg.Any<CancellationToken>()).Returns(proposal);
        eventStore.LoadCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(existingActions ?? Array.Empty<CorporateActionDto>());
        securityMasterStore.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(SecurityProjection());
        // Acceptance now resolves its scope from the fan-out authority. These cases are about the
        // acceptance command itself, so the authority is composed and permissive; the refusal
        // paths have their own cases below and in CorporateActionScopeFanOutGateTests.
        var scopeFanOut = Substitute.For<ICorporateActionScopeFanOutGate>();
        scopeFanOut.ResolveDecisionScopeAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateOnly>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new CorporateActionScopeFanOutDecision(
                true,
                new CorporateActionCaseScopeDto(call.ArgAt<string>(2), call.ArgAt<string>(3)),
                CorporateActionScopeFanOutRefusal.None,
                []));
        var service = new CorporateActionOperationsService(
            store, eventStore, securityMasterStore, restatementTrigger, scopeFanOut);
        return new Fixture(service, store, eventStore, securityMasterStore, restatementTrigger, scopeFanOut);
    }

    private static AcceptCorporateActionSourceProposalRequestDto AcceptRequest() =>
        new(
            ProposalId,
            ExpectedVersion: 4,
            IdempotencyKey: "accept:proposal:v4",
            Scope: new CorporateActionCaseScopeDto("tenant-a", "company-a"),
            Actor: "operations-user",
            Reason: "Terms confirmed from retained source evidence.",
            CorrelationId: "trace-ca");

    private static CorporateActionSourceProposalDto SourceProposal(CorporateActionDto action) =>
        new(
            ProposalId,
            SecurityId,
            new CorporateActionProviderEventIdentityDto(
                "provider-a", "event-100", "v1", Now.AddMinutes(-5),
                EvidenceHash: new string('a', 64),
                EvidenceReference: "provider://event-100/v1",
                ReleaseStatus: CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
            action,
            action.PayloadSchemaVersion,
            CorporateActionEconomicFingerprint.Compute(action),
            CorporateActionSourceProposalStates.Observed,
            Version: 4,
            SupersedesProposalId: null,
            AcceptedCorporateActionId: null,
            InitialCaseId: null,
            RecordedBy: "ingest",
            RecordedAtUtc: Now.AddMinutes(-5),
            UpdatedAtUtc: Now.AddMinutes(-5));

    private static CorporateActionConflictDto Conflict(Guid caseId) =>
        new(
            Guid.Parse("de53c04b-041f-461c-8aa0-1710d330e941"),
            caseId,
            CorporateActionPayloads.CashAmount,
            "Provider values differ.",
            [
                new CorporateActionConflictCandidateDto(
                    "provider-a",
                    JsonSerializer.SerializeToElement(0.24m),
                    "provider-event://provider-a/event-100/v1"),
                new CorporateActionConflictCandidateDto(
                    "provider-b",
                    JsonSerializer.SerializeToElement(0.26m),
                    "provider-event://provider-b/event-200/v1"),
            ],
            CorporateActionConflictStates.Open,
            Resolution: null,
            CaseVersion: 3,
            RecordedBy: "ingest",
            RecordedAtUtc: Now);

    private static CorporateActionSourceProposalAcceptanceResultDto AcceptanceResult(
        AcceptCorporateActionSourceProposalRequestDto request,
        Guid corporateActionId,
        Guid caseId,
        Guid transitionId,
        SecurityMasterCorporateActionRestatementDto? restatement,
        bool replayed,
        CorporateActionDto? proposedAction = null)
    {
        var action = (proposedAction ?? Dividend()) with { CorpActId = corporateActionId };
        var proposal = new CorporateActionSourceProposalDto(
            ProposalId,
            SecurityId,
            new CorporateActionProviderEventIdentityDto("provider-a", "event-100", "v1", Now),
            action,
            action.PayloadSchemaVersion,
            CorporateActionEconomicFingerprint.Compute(action),
            CorporateActionSourceProposalStates.Accepted,
            Version: request.ExpectedVersion + 1,
            SupersedesProposalId: null,
            AcceptedCorporateActionId: corporateActionId,
            InitialCaseId: caseId,
            RecordedBy: "ingest",
            RecordedAtUtc: Now.AddMinutes(-5),
            UpdatedAtUtc: Now,
            DecisionBy: request.Actor,
            DecisionAtUtc: Now);
        var processingCase = new CorporateActionProcessingCaseDto(
            caseId,
            ProposalId,
            corporateActionId,
            SecurityId,
            request.Scope,
            CorporateActionCaseStates.Detected,
            Version: 1,
            request.MethodologyProfileId,
            AssignedTo: null,
            BlockedReason: null,
            CreatedBy: request.Actor,
            CreatedAtUtc: Now,
            UpdatedBy: request.Actor,
            UpdatedAtUtc: Now);
        var transition = new CorporateActionCaseTransitionDto(
            transitionId,
            caseId,
            FromState: null,
            CorporateActionCaseStates.Detected,
            ExpectedVersion: 0,
            ResultingVersion: 1,
            request.Actor,
            "Accepted canonical source fact.",
            request.IdempotencyKey,
            Now,
            request.CorrelationId);
        var audit = new SecurityMasterCorporateActionAuditDto(
            $"audit:{corporateActionId:D}",
            SecurityId,
            corporateActionId,
            action.EventType,
            "provider-a",
            request.Actor,
            Now,
            "event-100:v1",
            request.Reason,
            request.CorrelationId);
        return new CorporateActionSourceProposalAcceptanceResultDto(
            proposal, action, processingCase, transition, audit, restatement, replayed);
    }

    private static CorporateActionDto Dividend(decimal amount = 0.24m) =>
        new(
            Guid.Parse("4ccfa18d-d2af-417a-8351-c2a07e0fae16"),
            SecurityId,
            CorporateActionEventTypes.Dividend,
            ExDate,
            ExDate.AddDays(14),
            amount,
            "USD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            RecordDate: ExDate.AddDays(1));

    private static SecurityProjectionRecord SecurityProjection() =>
        new(
            SecurityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Acme Corp",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "ACME",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Acme Corp", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "operations-tests" }),
            Version: 3,
            EffectiveFrom: Now.AddYears(-1),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker, "ACME", true, Now.AddYears(-1), null, null),
            ],
            Aliases: []);

    private sealed record Fixture(
        CorporateActionOperationsService Service,
        ICorporateActionOperationsStore Store,
        ISecurityMasterEventStore EventStore,
        ISecurityMasterStore SecurityMasterStore,
        ICorporateActionRestatementTrigger RestatementTrigger,
        ICorporateActionScopeFanOutGate ScopeFanOut);
}
