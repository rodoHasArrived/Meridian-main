using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The single, centralized asset-specific-terms version-acceptance set. The validation surface and the
/// mapping/deserialization guard both consult these helpers, so the accepted set can never drift
/// between the two paths.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterSchemaVersionsTests
{
    [Fact]
    public void LegacyVersion_IsAcceptedForAnyRecordShape()
    {
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(
            SecurityMasterSchemaVersions.LegacyAssetSpecificTerms, isProfileBacked: false).Should().BeTrue();
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(
            SecurityMasterSchemaVersions.LegacyAssetSpecificTerms, isProfileBacked: true).Should().BeTrue();
    }

    [Fact]
    public void CustomAssetProfileVersion_IsAcceptedOnlyForProfileBackedRecords()
    {
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(
            SecurityMasterSchemaVersions.CustomAssetProfileTerms, isProfileBacked: true).Should().BeTrue();
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(
            SecurityMasterSchemaVersions.CustomAssetProfileTerms, isProfileBacked: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(0)]
    public void UnsupportedVersions_AreRejectedRegardlessOfShape(int schemaVersion)
    {
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(schemaVersion, isProfileBacked: false)
            .Should().BeFalse();
        SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(schemaVersion, isProfileBacked: true)
            .Should().BeFalse();
    }

    [Fact]
    public void AcceptedVersions_ReflectRecordShape()
    {
        SecurityMasterSchemaVersions.AcceptedAssetSpecificTermsVersions(isProfileBacked: false)
            .Should().Equal(SecurityMasterSchemaVersions.LegacyAssetSpecificTerms);
        SecurityMasterSchemaVersions.AcceptedAssetSpecificTermsVersions(isProfileBacked: true)
            .Should().Equal(
                SecurityMasterSchemaVersions.LegacyAssetSpecificTerms,
                SecurityMasterSchemaVersions.CustomAssetProfileTerms);
    }

    [Fact]
    public void DescribeAcceptedVersions_RendersTheSetForMessages()
    {
        SecurityMasterSchemaVersions.DescribeAcceptedAssetSpecificTermsVersions(isProfileBacked: false)
            .Should().Be("1");
        SecurityMasterSchemaVersions.DescribeAcceptedAssetSpecificTermsVersions(isProfileBacked: true)
            .Should().Be("1 or 3");
    }
}
