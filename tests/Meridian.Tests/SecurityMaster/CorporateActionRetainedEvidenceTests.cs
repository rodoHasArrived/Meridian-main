using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionRetainedEvidenceTests
{
    private const string Schema = "alpaca-market-data-corporate-actions-v1/meridian-v2";
    private const string Raw = """{ "id": "native-event", "rate": 0.05, "extra": { "vendor_note": "Retain me" } }""";

    [Fact]
    public void SourceContentHash_PreservesHighPrecisionAmendmentsAndIgnoresEquivalentNumericSpelling()
    {
        using var original = JsonDocument.Parse(
            """{"id":"native-event","rate":0.05,"unmapped":1.00000000000000000000000000001}""");
        using var changed = JsonDocument.Parse(
            """{"id":"native-event","rate":0.05,"unmapped":1.00000000000000000000000000002}""");
        using var equivalent = JsonDocument.Parse(
            """{ "unmapped":1.000000000000000000000000000010, "rate":5e-2, "id":"native-event" }""");

        var originalHash = CorporateActionSourceProposalReplayComparer.ComputeSourceContentHash(original.RootElement);

        originalHash.Should().NotBe(CorporateActionSourceProposalReplayComparer.ComputeSourceContentHash(changed.RootElement),
            "a source amendment beyond decimal precision must receive a distinct revision");
        originalHash.Should().Be(CorporateActionSourceProposalReplayComparer.ComputeSourceContentHash(equivalent.RootElement),
            "property order, whitespace and exact equivalent numeric representations are replay-stable");
    }

    [Fact]
    public async Task RecordSourceProposal_RetainsVerifiedRawEvidenceAndStillBlocksAcceptance()
    {
        var store = Substitute.For<ICorporateActionOperationsStore>();
        store.RecordSourceProposalAsync(Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CorporateActionSourceProposalDto>());
        var service = new CorporateActionOperationsService(store);
        var request = Request();

        var proposal = await service.RecordSourceProposalAsync(request);

        proposal.ProposedAction.SourceEvidence.Should().Be(request.ProposedAction.SourceEvidence);
        proposal.State.Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        proposal.ActionAvailability!.CanAccept.Should().BeFalse();
        proposal.AcceptedCorporateActionId.Should().BeNull();
        proposal.InitialCaseId.Should().BeNull();
        proposal.EconomicFingerprint.Should().Be(CorporateActionEconomicFingerprint.Compute(
            proposal.ProposedAction with { SourceEvidence = null }));
    }

    [Theory]
    [InlineData("changed raw bytes", null)]
    [InlineData("[]", "hash")]
    [InlineData("not json", "hash")]
    public async Task RecordSourceProposal_InvalidRetainedRawEvidenceFailsBeforeStorage(string raw, string? computeHash)
    {
        var store = Substitute.For<ICorporateActionOperationsStore>();
        var service = new CorporateActionOperationsService(store);
        var request = Request();
        request = request with
        {
            ProposedAction = request.ProposedAction with
            {
                SourceEvidence = request.ProposedAction.SourceEvidence! with
                {
                    RawPayloadJson = raw,
                    RawPayloadSha256 = computeHash is null ? new string('0', 64) : Sha256Digest.ComputeUtf8(raw),
                },
            },
        };

        var act = () => service.RecordSourceProposalAsync(request);

        await act.Should().ThrowAsync<CorporateActionValidationException>();
        await store.DidNotReceive().RecordSourceProposalAsync(Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replay_FormattingEquivalentRawRetainsOriginalButChangedUnknownFieldIsConflict()
    {
        var store = Substitute.For<ICorporateActionOperationsStore>();
        store.RecordSourceProposalAsync(Arg.Any<CorporateActionSourceProposalDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CorporateActionSourceProposalDto>());
        var service = new CorporateActionOperationsService(store);
        var original = await service.RecordSourceProposalAsync(Request());
        const string reordered = """{"extra":{"vendor_note":"Retain me"},"rate":5e-2,"id":"native-event"}""";
        var equivalent = original with
        {
            ProposalId = Guid.NewGuid(),
            ProposedAction = original.ProposedAction with { SourceEvidence = Evidence(reordered) },
        };
        var changed = equivalent with
        {
            ProposedAction = equivalent.ProposedAction with { SourceEvidence = Evidence(reordered.Replace("Retain me", "Changed")) },
        };

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(original, equivalent).Should().BeTrue();
        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(original, changed).Should().BeFalse();
        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(original,
            equivalent with { ProposedAction = equivalent.ProposedAction with { SourceEvidence = null } }).Should().BeFalse();
        original.ProposedAction.SourceEvidence!.RawPayloadJson.Should().Be(Raw);
    }

    [Fact]
    public void SourceEvidence_IsExcludedFromEconomicsButTypedTermsAffectFingerprint()
    {
        var original = Request().ProposedAction;
        var changedEvidence = original with { SourceEvidence = Evidence("""{"id":"native-event","rate":0.05,"metadata":"amended"}""") };
        var changedTerms = original with { DistributionRatio = 0.1m };

        CorporateActionEconomicFingerprint.Compute(original).Should().Be(CorporateActionEconomicFingerprint.Compute(changedEvidence));
        CorporateActionEconomicFingerprint.Compute(original).Should().NotBe(CorporateActionEconomicFingerprint.Compute(changedTerms));
        var json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<CorporateActionDto>(json)!.SourceEvidence.Should().Be(original.SourceEvidence);
        JsonSerializer.Serialize(original with { SourceEvidence = null }).Should().NotContain("SourceEvidence");
    }

    internal static RecordCorporateActionSourceProposalRequestDto Request(Guid? securityId = null, string? nativeId = null)
        => new(new CorporateActionDto(
                Guid.NewGuid(), securityId ?? Guid.NewGuid(), CorporateActionEventTypes.SpinOff,
                new DateOnly(2026, 8, 14), null, null, null, null, null, 0.05m,
                null, null, null, null, Payload: JsonSerializer.SerializeToElement(new { newSymbol = "CHILD" }))
        {
            SourceEvidence = Evidence(Raw),
        },
            new CorporateActionProviderEventIdentityDto("alpaca", nativeId ?? "native-event", "v1:alpaca-rest-v2",
                new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), new string('a', 64),
                "alpaca://corporate-actions/v1/spin_off/native-event/versions/alpaca-rest-v2/fixture",
                CorporateActionProviderReleaseStatusDto.ReviewOnly),
            "synthetic-ingest");

    internal static CorporateActionRetainedSourceEvidenceDto Evidence(string raw)
        => new("spin_off", Schema, raw, Sha256Digest.ComputeUtf8(raw));
}
