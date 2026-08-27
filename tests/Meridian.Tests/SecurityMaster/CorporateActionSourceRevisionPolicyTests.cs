using System.Text.Json;
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

    [Fact]
    public void SourceReplayEquality_AcceptsReplayAfterStoreOwnedStateTransition()
    {
        var existing = Proposal("v1", ObservedAt) with
        {
            State = CorporateActionSourceProposalStates.Accepted,
            AcceptedCorporateActionId = Guid.NewGuid(),
            InitialCaseId = Guid.NewGuid(),
        };
        var replay = existing with
        {
            ProposalId = Guid.NewGuid(),
            State = CorporateActionSourceProposalStates.Observed,
            AcceptedCorporateActionId = null,
            InitialCaseId = null,
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, replay)
            .Should().BeTrue();
    }

    [Fact]
    public void SourceReplayEquality_RejectsChangedDissentEvidenceUnderSameVersion()
    {
        var existing = Proposal("v1", ObservedAt) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            DisplayMetadata = DissentMetadata(0.26m),
        };
        var changed = existing with
        {
            ProposalId = Guid.NewGuid(),
            DisplayMetadata = DissentMetadata(0.27m),
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, changed)
            .Should().BeFalse();
    }

    [Fact]
    public void SourceReplayEquality_AcceptsEquivalentReorderedConsensusMetadata()
    {
        var existing = Proposal("v1", ObservedAt) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            DisplayMetadata = DissentMetadata(0.26m),
        };
        var reordered = existing with
        {
            ProposalId = Guid.NewGuid(),
            DisplayMetadata = existing.DisplayMetadata! with
            {
                AgreeingSources = ["provider-b", "provider-a"],
                DissentingSources = ["provider-c", "provider-b"],
                DissentingFields = existing.DisplayMetadata.DissentingFields!
                    .Select(static field => field with
                    {
                        Candidates = field.Candidates.Reverse().ToArray(),
                    })
                    .Reverse()
                    .ToArray(),
            },
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, reordered)
            .Should().BeTrue();
    }

    [Fact]
    public void SourceReplayEquality_AcceptsEquivalentCandidateJsonFormatting()
    {
        var existing = Proposal("v1", ObservedAt) with
        {
            State = CorporateActionSourceProposalStates.ReviewRequired,
            DisplayMetadata = StructuredDissentMetadata(
                """{"amount":0.2600,"currency":"USD"}"""),
        };
        var reformatted = existing with
        {
            ProposalId = Guid.NewGuid(),
            DisplayMetadata = StructuredDissentMetadata(
                """{"currency":"USD","amount":2.6e-1}"""),
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(existing, reformatted)
            .Should().BeTrue();
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

    private static CorporateActionSourceDisplayMetadataDto DissentMetadata(decimal cashAmount) =>
        new(
            "ACME",
            "provider-a",
            ["provider-a", "provider-b"],
            ["provider-b", "provider-c"],
            [
                new CorporateActionDissentFieldDto(
                    CorporateActionPayloads.CashAmount,
                    [
                        new CorporateActionConflictCandidateDto(
                            "provider-a",
                            JsonSerializer.SerializeToElement(0.24m),
                            "provider-event://provider-a/event-100/v1"),
                        new CorporateActionConflictCandidateDto(
                            "provider-b",
                            JsonSerializer.SerializeToElement(cashAmount),
                            "provider-event://provider-b/event-200/v1"),
                    ]),
                new CorporateActionDissentFieldDto(
                    "currency",
                    [
                        new CorporateActionConflictCandidateDto(
                            "provider-a",
                            JsonSerializer.SerializeToElement("USD"),
                            "provider-event://provider-a/event-100/v1"),
                        new CorporateActionConflictCandidateDto(
                            "provider-c",
                            JsonSerializer.SerializeToElement("CAD"),
                            "provider-event://provider-c/event-300/v1"),
                    ]),
            ]);

    private static CorporateActionSourceDisplayMetadataDto StructuredDissentMetadata(string candidateJson) =>
        new(
            "ACME",
            "provider-a",
            ["provider-a"],
            ["provider-b"],
            [
                new CorporateActionDissentFieldDto(
                    "structuredTerms",
                    [
                        new CorporateActionConflictCandidateDto(
                            "provider-a",
                            ParseJson("""{"amount":0.24,"currency":"USD"}"""),
                            "provider-event://provider-a/event-100/v1"),
                        new CorporateActionConflictCandidateDto(
                            "provider-b",
                            ParseJson(candidateJson),
                            "provider-event://provider-b/event-200/v1"),
                    ]),
            ]);

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
