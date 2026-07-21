using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The registered <see cref="SecurityAssetSpecificTermsUpcasterPipeline"/> is the single choke point the
/// projection store promotes <c>schema_version</c> through. It must compose the full migrate-on-read chain
/// (v0 stamping plus cross-family economic-terms v2 -> v1 flattening) so the store never promotes a version
/// the mapping guard would reject, while preserving unknown future versions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetSpecificTermsUpcasterPipelineTests
{
    private readonly SecurityAssetSpecificTermsUpcasterPipeline _pipeline = new();

    [Fact]
    public void Upcast_UnstampedLegacyPayload_ResolvesToLegacyVersion()
    {
        var result = _pipeline.Upcast("""{"shareClass":"Common"}""");

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
    }

    [Fact]
    public void Upcast_EconomicTermsV2Document_FlattensToAcceptedLegacyVersion()
    {
        // A cross-family v2 economic-terms document reaching the asset-specific-terms slot: the pipeline
        // flattens it to the accepted legacy shape so the promoted version matches what the guard accepts.
        var result = _pipeline.Upcast(
            $$"""
            {"schemaVersion":{{EconomicTermsSchema.Current}},"maturity":{"maturityDate":"2030-06-30"},"coupon":{"couponType":"Fixed","couponRate":5.25}}
            """);

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
        result.Payload.TryGetProperty("maturityDate", out _).Should().BeTrue("the v2 maturity block is flattened");
        result.Payload.TryGetProperty("couponType", out _).Should().BeTrue("the v2 coupon block is flattened");
    }

    [Fact]
    public void Upcast_CustomAssetProfilePayload_PreservesItsVersion()
    {
        var result = _pipeline.Upcast(
            $$"""
            {"schemaVersion":{{AssetSpecificTermsSchema.CustomAssetProfile}},"customProfileId":"co-invest-spv","profileVersion":1}
            """);

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(AssetSpecificTermsSchema.CustomAssetProfile);
    }

    [Fact]
    public void Upcast_InvalidJson_ReturnsNull()
    {
        _pipeline.Upcast("not json").Should().BeNull();
    }

    [Fact]
    public void SchemaBounds_MatchTheComposedChain()
    {
        _pipeline.FromSchemaVersion.Should().Be(0);
        _pipeline.ToSchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
    }
}
