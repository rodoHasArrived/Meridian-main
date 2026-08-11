using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts;

public sealed class LedgerDimensionSetNormalizerTests
{
    [Fact]
    public void Canonicalize_WhitespaceOnlyDimensions_ReturnsNull()
    {
        var dimensions = new LedgerDimensionSetDto(
            FundId: " ",
            EntityId: "\t",
            ExternalGlDimensions: new Dictionary<string, string>
            {
                ["Department"] = " "
            });

        var canonical = LedgerDimensionSetNormalizer.Canonicalize(dimensions);

        canonical.Should().BeNull();
        LedgerDimensionSetNormalizer.HasAny(dimensions).Should().BeFalse();
    }

    [Fact]
    public void FirstTag_FirstPresentValueIsBlank_DoesNotFallThroughToAlias()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fundId"] = " ",
            ["fundProfileId"] = " fallback-fund "
        };

        var value = LedgerDimensionSetNormalizer.FirstTag(tags, "fundId", "fundProfileId");

        value.Should().BeNull();
    }

    [Theory]
    [InlineData("externalGl.Department", null)]
    [InlineData("externalGl:Department", null)]
    [InlineData("gl.Department", null)]
    [InlineData("gl:Department", null)]
    [InlineData("lineDimensions.scope.externalGl.Department", "lineDimensions.scope.")]
    public void ExtractExternalGlDimensions_SupportedAliases_PreserveCanonicalCompatibility(
        string tagKey,
        string? prefix)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [tagKey] = " Investment Operations ",
            ["unrelated"] = "ignored",
            [prefix is null ? "gl.Blank" : $"{prefix}gl.Blank"] = " "
        };

        var dimensions = LedgerDimensionSetNormalizer.ExtractExternalGlDimensions(tags, prefix);

        dimensions.Should().HaveCount(1);
        dimensions["Department"].Should().Be("Investment Operations");
    }
}
