using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterServiceSnapshotTests
{
    [Theory]
    [InlineData("scheduleConventions", "{\"stubPolicy\":\"Short\"}")]
    [InlineData("callSchedule", "[{\"callDate\":\"2028-01-01\",\"exerciseStyle\":\"European\"}]")]
    public async Task AmendWithoutTermsPatch_DoesNotPromoteAStoredStrayContainer(string key, string json)
    {
        var harness = new ServiceHarness();
        var id = Guid.NewGuid();
        harness.Seed(id, "Bond", Stamped(BondAtVersion1, 1, terms => terms[key] = JsonNode.Parse(json)));

        var action = () => RunAsync(harness, id, AmendWithoutTermsPatch);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be promoted by an unrelated amendment*");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task FullReplacement_ExplicitlyClearsStoredStrayBeforeAnAmendment()
    {
        var harness = new ServiceHarness();
        var id = Guid.NewGuid();
        harness.Seed(id, "Bond", Stamped(BondAtVersion1, 1,
            terms => terms[SecurityVersionedTerms.ScheduleConventionsKey] = JsonNode.Parse("{\"stubPolicy\":\"Short\"}")));

        var detail = await harness.Service.AmendTermsAsync(AmendRequest(id, Parse(BondAtVersion1)));

        detail.AssetSpecificTerms.TryGetProperty(SecurityVersionedTerms.ScheduleConventionsKey, out _).Should().BeFalse();
        detail.AssetSpecificTerms.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(1);
        harness.Appended.Should().ContainSingle();
        harness.Upserted.Should().ContainSingle();
    }

    [Fact]
    public async Task FullReplacement_ExplicitHigherStampIntentionallyDeclaresAValidatedBlock()
    {
        var harness = new ServiceHarness();
        var id = Guid.NewGuid();
        var stray = Stamped(BondAtVersion1, 1,
            terms => terms[SecurityVersionedTerms.ScheduleConventionsKey] = JsonNode.Parse("{\"stubPolicy\":\"Short\"}"));
        harness.Seed(id, "Bond", stray);
        var replacement = JsonNode.Parse(stray)!.AsObject();
        replacement[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;

        var detail = await harness.Service.AmendTermsAsync(AmendRequest(id, Parse(replacement.ToJsonString())));

        detail.AssetSpecificTerms.GetProperty(SecurityVersionedTerms.ScheduleConventionsKey).GetProperty("stubPolicy").GetString().Should().Be("Short");
        detail.AssetSpecificTerms.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(2);
        harness.Appended.Should().ContainSingle();
        harness.Upserted.Should().ContainSingle();
    }
}
