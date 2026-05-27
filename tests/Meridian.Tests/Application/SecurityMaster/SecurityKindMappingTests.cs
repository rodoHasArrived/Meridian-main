using FluentAssertions;
using Meridian.Application.SecurityMaster;

namespace Meridian.Tests.Application.SecurityMaster;

public sealed class SecurityKindMappingTests
{
    [Fact]
    public void TryNormalizeAssetClass_ReturnsCanonicalAssetClass_WhenInputMatchesIgnoringCase()
    {
        var normalized = SecurityKindMapping.TryNormalizeAssetClass("cryptocurrency", out var canonicalAssetClass);

        normalized.Should().BeTrue();
        canonicalAssetClass.Should().Be("CryptoCurrency");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("UnknownAssetClass")]
    public void TryNormalizeAssetClass_ReturnsFalse_ForUnknownOrMissingAssetClass(string assetClass)
    {
        var normalized = SecurityKindMapping.TryNormalizeAssetClass(assetClass, out var canonicalAssetClass);

        normalized.Should().BeFalse();
        canonicalAssetClass.Should().BeEmpty();
    }
}
