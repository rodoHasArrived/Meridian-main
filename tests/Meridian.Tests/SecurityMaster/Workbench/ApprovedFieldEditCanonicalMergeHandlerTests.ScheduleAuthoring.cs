using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed partial class ApprovedFieldEditCanonicalMergeHandlerTests
{
    [Theory]
    [InlineData("scheduleConventions")]
    [InlineData("callSchedule")]
    [InlineData("putSchedule")]
    public async Task ExplicitJsonNullContainerReplacement_MergesAsUnsetRatherThanText(string key)
    {
        var terms = JsonNode.Parse(AheadOfWriteFixtures.BondAtWriteVersion)!.AsObject();
        // A retained stray value is undeclared at stamp one. Explicit JSON null repairs it;
        // a null revision FieldValue would instead withdraw the overlay without a merge.
        terms[key] = JsonNode.Parse("{\"unknownMember\":1}");
        SetProjection("Bond", JsonSerializer.SerializeToElement(terms));
        var path = $"assetSpecificTerms.{key}";
        var revisionId = await SeedApprovedFieldRevisionAsync(path, "null");
        var canonical = new CanonicalAmendHarness(_projectionStore);

        await CreateCanonicalMergeHandler(canonical.Service)
            .HandleAsync(Event(revisionId, path) with { EffectiveFrom = DateTimeOffset.UtcNow });

        var persisted = canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms;
        persisted.TryGetProperty(key, out var value).Should().BeFalse("the repaired row holds no declared container");
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(persisted).Should().Be(1);
    }

    [Fact]
    public async Task ApprovedWholeScheduleReplacement_ExplicitlyPromotesAndRepairsRetainedStrayContent()
    {
        SetProjection("Bond", AheadOfWriteFixtures.BondTerms(1, withBlock: false,
            putSchedule: """[{"putDate":"2028-01-01","putPrice":101}]"""));
        const string path = "assetSpecificTerms.putSchedule";
        const string replacement = """[{"putDate":"2029-01-01","putPrice":100,"priceBasis":"PercentOfPar","exerciseStyle":"European"}]""";
        var revisionId = await SeedApprovedFieldRevisionAsync(path, replacement);
        var canonical = new CanonicalAmendHarness(_projectionStore);

        await CreateCanonicalMergeHandler(canonical.Service)
            .HandleAsync(Event(revisionId, path) with { EffectiveFrom = DateTimeOffset.UtcNow });

        var persisted = canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms;
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(persisted).Should().Be(3);
        persisted.GetProperty("putSchedule")[0].GetProperty("putDate").GetString().Should().Be("2029-01-01");
        persisted.GetProperty("putSchedule")[0].GetProperty("exerciseStyle").GetString().Should().Be("European");
    }
}
