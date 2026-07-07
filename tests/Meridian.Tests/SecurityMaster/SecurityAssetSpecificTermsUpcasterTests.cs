using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migrate-on-read upcaster for Security Master asset-specific-terms payloads: legacy payloads with no
/// <c>schemaVersion</c> are stamped as the legacy version so downstream always sees an explicitly
/// versioned payload, while already-versioned payloads are preserved unchanged.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetSpecificTermsUpcasterTests
{
    private readonly SecurityAssetSpecificTermsV0ToCurrentUpcaster _upcaster = new();

    [Fact]
    public void Upcast_LegacyPayloadWithoutVersion_StampsLegacyVersion()
    {
        var result = _upcaster.Upcast("""{"shareClass":"Common"}""");

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
        result.Payload.TryGetProperty("schemaVersion", out var version).Should().BeTrue();
        version.GetInt32().Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
        // Existing properties are preserved.
        result.Payload.GetProperty("shareClass").GetString().Should().Be("Common");
    }

    [Fact]
    public void Upcast_PayloadWithExplicitVersion_PreservesVersionAndContent()
    {
        var result = _upcaster.Upcast($$"""{"schemaVersion":{{SecurityMasterSchemaVersions.CustomAssetProfileTerms}},"customProfileId":"p-1"}""");

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(SecurityMasterSchemaVersions.CustomAssetProfileTerms);
        result.Payload.GetProperty("schemaVersion").GetInt32()
            .Should().Be(SecurityMasterSchemaVersions.CustomAssetProfileTerms);
        result.Payload.GetProperty("customProfileId").GetString().Should().Be("p-1");
    }

    [Fact]
    public void Upcast_EmptyObject_StampsLegacyVersion()
    {
        var result = _upcaster.Upcast("{}");

        result.Should().NotBeNull();
        result!.SchemaVersion.Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
        result.Payload.GetProperty("schemaVersion").GetInt32()
            .Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
    }

    [Fact]
    public void Upcast_UnparseableJson_ReturnsNull()
    {
        _upcaster.Upcast("not json").Should().BeNull();
        _upcaster.Upcast("").Should().BeNull();
    }

    [Fact]
    public void ResolveSchemaVersion_MissingVersion_ReturnsLegacyDefault()
    {
        using var document = JsonDocument.Parse("""{"shareClass":"Common"}""");

        SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(document.RootElement)
            .Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
    }

    [Fact]
    public void ResolveSchemaVersion_ExplicitVersion_ReturnsThatVersion()
    {
        using var document = JsonDocument.Parse("""{"schemaVersion":2}""");

        SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(document.RootElement)
            .Should().Be(2);
    }

    [Fact]
    public void FromAndToVersions_DescribeTheLegacyStampingTransition()
    {
        _upcaster.FromSchemaVersion.Should().Be(0);
        _upcaster.ToSchemaVersion.Should().Be(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
    }
}
