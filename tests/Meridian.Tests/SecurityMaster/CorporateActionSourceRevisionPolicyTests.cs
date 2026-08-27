using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionSourceRevisionPolicyTests
{
    private static readonly Guid SecurityId = Guid.Parse("aa2b196d-5cad-4e02-81e8-941b245630b4");
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SourceEventLockScope_IsSharedAcrossVersionsOfOneProviderEvent()
    {
        var first = Proposal("v1", ObservedAt).ProviderIdentity;
        var second = first with { SourceEventVersion = "v2" };

        PostgresCorporateActionOperationsStore.SourceEventLockScope(first)
            .Should().Be(PostgresCorporateActionOperationsStore.SourceEventLockScope(second));
    }

    [Fact]
    public void BindNewSourceRevision_ImplicitParentFormsAmendmentAndCarriesAcceptedAncestor()
    {
        var canonicalId = Guid.NewGuid();
        var tip = Proposal("v1", ObservedAt, acceptedCorporateActionId: canonicalId);
        var candidate = Proposal("v2", ObservedAt.AddMinutes(1));

        var bound = PostgresCorporateActionOperationsStore.BindNewSourceRevision(tip, candidate);

        bound.SupersedesProposalId.Should().Be(tip.ProposalId);
        bound.ProposedAction.SupersedesCorpActId.Should().Be(canonicalId);
    }

    [Fact]
    public void BindNewSourceRevision_UnacceptedIntermediateCarriesNearestAcceptedAncestor()
    {
        var canonicalId = Guid.NewGuid();
        var intermediate = Proposal(
            "v2",
            ObservedAt,
            supersedesProposalId: Guid.NewGuid(),
            canonicalAncestorId: canonicalId);

        var bound = PostgresCorporateActionOperationsStore.BindNewSourceRevision(
            intermediate,
            Proposal("v3", ObservedAt.AddMinutes(1)));

        bound.SupersedesProposalId.Should().Be(intermediate.ProposalId);
        bound.ProposedAction.SupersedesCorpActId.Should().Be(canonicalId);
    }

    [Fact]
    public void BindNewSourceRevision_WithNoAcceptedAncestorKeepsCanonicalLineageRooted()
    {
        var unacceptedTip = Proposal("v1", ObservedAt);

        var bound = PostgresCorporateActionOperationsStore.BindNewSourceRevision(
            unacceptedTip,
            Proposal("v2", ObservedAt.AddMinutes(1)));

        bound.SupersedesProposalId.Should().Be(unacceptedTip.ProposalId);
        bound.ProposedAction.SupersedesCorpActId.Should().BeNull();
    }

    [Fact]
    public void BindNewSourceRevision_StaleExplicitParentFailsClosed()
    {
        var tip = Proposal("v2", ObservedAt);
        var candidate = Proposal(
            "v3",
            ObservedAt.AddMinutes(1),
            supersedesProposalId: Guid.NewGuid());

        var act = () => PostgresCorporateActionOperationsStore.BindNewSourceRevision(tip, candidate);

        act.Should().Throw<CorporateActionStateConflictException>();
    }

    [Fact]
    public void BindNewSourceRevision_OlderObservationFailsClosed()
    {
        var tip = Proposal("v2", ObservedAt);
        var candidate = Proposal("v3", ObservedAt.AddMinutes(-1));

        var act = () => PostgresCorporateActionOperationsStore.BindNewSourceRevision(tip, candidate);

        act.Should().Throw<CorporateActionStateConflictException>();
    }

    [Fact]
    public void BindExactSourceReplay_RestoresStoreOwnedLineageForIdempotentRepeat()
    {
        var parentId = Guid.NewGuid();
        var canonicalId = Guid.NewGuid();
        var existing = Proposal(
            "v2",
            ObservedAt,
            supersedesProposalId: parentId,
            canonicalAncestorId: canonicalId);
        var repeat = existing with
        {
            ProposalId = Guid.NewGuid(),
            SupersedesProposalId = null,
            ProposedAction = existing.ProposedAction with { SupersedesCorpActId = null },
        };

        var bound = PostgresCorporateActionOperationsStore.BindExactSourceReplay(existing, repeat);

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, bound)
            .Should().BeTrue();
    }

    [Fact]
    public void SourceReplayEquality_RejectsReleaseStatusElevationUnderSameVersion()
    {
        var existing = Proposal("v1", ObservedAt) with
        {
            ProviderIdentity = Proposal("v1", ObservedAt).ProviderIdentity with
            {
                ReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly,
            },
        };
        var elevated = existing with
        {
            ProposalId = Guid.NewGuid(),
            ProviderIdentity = existing.ProviderIdentity with
            {
                ReleaseStatus = CorporateActionProviderReleaseStatusDto.AcceptanceEligible,
            },
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, elevated)
            .Should().BeFalse();
    }

    private static CorporateActionSourceProposalDto Proposal(
        string sourceVersion,
        DateTimeOffset observedAt,
        Guid? supersedesProposalId = null,
        Guid? acceptedCorporateActionId = null,
        Guid? canonicalAncestorId = null)
    {
        var action = new CorporateActionDto(
            Guid.NewGuid(),
            SecurityId,
            CorporateActionEventTypes.Dividend,
            new DateOnly(2026, 8, 14),
            new DateOnly(2026, 8, 28),
            0.24m,
            "USD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            SupersedesCorpActId: canonicalAncestorId,
            RecordDate: new DateOnly(2026, 8, 15));
        return new CorporateActionSourceProposalDto(
            Guid.NewGuid(),
            SecurityId,
            new CorporateActionProviderEventIdentityDto(
                "provider-a",
                "event-100",
                sourceVersion,
                observedAt,
                new string('a', 64),
                "provider://event-100/raw",
                CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
            action,
            action.PayloadSchemaVersion,
            CorporateActionEconomicFingerprint.Compute(action),
            CorporateActionSourceProposalStates.Observed,
            1,
            supersedesProposalId,
            acceptedCorporateActionId,
            null,
            "ingest",
            observedAt,
            observedAt);
    }
}
