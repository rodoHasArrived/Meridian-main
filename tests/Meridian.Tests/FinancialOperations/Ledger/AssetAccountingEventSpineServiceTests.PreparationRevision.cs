using FluentAssertions;
using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Fact]
    public async Task FactorPaydown_ReprepareResolvesTheReferenceRecordedAtActualPreparationWithoutChangingItsObservation()
    {
        var recordedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var rawStore = Substitute.For<ISecurityMasterStore>();
        var harness = BuildFactorPaydownSpineHarness(securityMasterStore: rawStore,
            recordedSecurityAt: (security, cutoff) => cutoff < recordedAt ? security : security with { Version = 2 });
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var previous = harness.Retained[2];
        var detail = harness.Fixture.Security;
        var empty = JsonSerializer.SerializeToElement(new { });
        var current = new SecurityProjectionRecord(detail.SecurityId, detail.AssetClass, detail.Status,
            detail.DisplayName, detail.Currency, "ISIN", "REVISED", empty, empty, empty, 2,
            detail.EffectiveFrom, detail.EffectiveTo, [], []);
        rawStore.GetProjectionAsync(detail.SecurityId, Arg.Any<CancellationToken>()).Returns(current);
        var instruction = harness.Instruction with
        {
            Scope = harness.Instruction.Scope with { ExpectedSecurityVersion = 2 },
            ExpectedSecurityHash = OpenLotAmortization.SecurityHash(current)
        };
        var request = harness.Fixture.Request with
        {
            Scope = instruction.Scope,
            FactorPaydown = instruction,
            ProjectedAtUtc = DateTimeOffset.UtcNow
        };
        request.ProjectionLineage.GeneratedAtUtc.Should().BeBefore(recordedAt,
            "the economic observation and its retained original lineage precede the reference revision");

        var result = await harness.Service.ReprepareUnpostedFactorPaydownAsync(request,
            FactorPaydownLotMutation(instruction), 2, previous.CanonicalFingerprint);

        result.Spine.Scope.ExpectedSecurityVersion.Should().Be(2);
        result.Spine.FactorPaydown!.ExpectedSecurityHash.Should().Be(OpenLotAmortization.SecurityHash(current));
        result.Spine.EconomicEvent.Should().BeEquivalentTo(previous.Projection.EconomicEvent);
        result.Spine.ProjectionLineage.Should().BeEquivalentTo(previous.Projection.ProjectionLineage);
        harness.Retained[2].Should().BeEquivalentTo(previous);
        await harness.Atomic.DidNotReceiveWithAnyArgs().AppendAssetPostingAsync(default!, default);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("maker")]
    [InlineData("maker-casing")]
    [InlineData("future")]
    public async Task FactorPaydown_ReprepareRefusesAnUnreviewedPredecessorOrPreparingActor(string scenario)
    {
        var harness = BuildFactorPaydownSpineHarness();
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var previous = harness.Retained[2];
        var request = harness.Fixture.Request with
        {
            Actor = scenario switch
            {
                "maker" => "another-preparer",
                "maker-casing" => harness.Fixture.Request.Actor.ToUpperInvariant(),
                _ => harness.Fixture.Request.Actor
            },
            ProjectedAtUtc = scenario == "future" ? DateTimeOffset.UtcNow.AddMinutes(1) : DateTimeOffset.UtcNow
        };
        var recovery = () => harness.Service.ReprepareUnpostedFactorPaydownAsync(request,
            FactorPaydownLotMutation(harness.Instruction), 2,
            scenario == "fingerprint" ? new string('a', 64) : previous.CanonicalFingerprint);

        await recovery.Should().ThrowAsync<InvalidOperationException>();

        harness.Retained.Should().HaveCount(2);
        harness.Retained[2].Should().BeEquivalentTo(previous);
        await harness.Atomic.DidNotReceiveWithAnyArgs().AppendAssetPostingAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactorPaydown_ReprepareCannotReplaceRetainedDraftedOrPostedAuthority(bool posted)
    {
        var harness = BuildFactorPaydownSpineHarness();
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var drafted = await harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(harness.Instruction)
        });
        if (posted)
            await harness.PostService.PostCandidateAsync(harness.ApprovedPostRequest(
                drafted.Spine.DraftedCandidate!, drafted.Spine.DraftedCandidateFingerprint!));
        var previous = harness.Retained.Values.MaxBy(static record => record.Projection.SpineVersion)!;
        var beforeCount = harness.Retained.Count;
        var beforeFingerprint = previous.CanonicalFingerprint;

        var recovery = () => harness.Service.ReprepareUnpostedFactorPaydownAsync(
            harness.Fixture.Request with { ProjectedAtUtc = DateTimeOffset.UtcNow },
            FactorPaydownLotMutation(harness.Instruction), previous.Projection.SpineVersion, beforeFingerprint);

        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only an Expected or Projected*");
        harness.Retained.Should().HaveCount(beforeCount);
        harness.Retained[previous.Projection.SpineVersion].CanonicalFingerprint.Should().Be(beforeFingerprint);
    }
}
