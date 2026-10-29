using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

public sealed class CredentialPlaceholderDetectorTests
{
    [Theory]
    [InlineData("__SET_ME__")]
    [InlineData("YOUR_API_KEY")]
    [InlineData("your-key-here")]
    [InlineData("REPLACE_secret")]
    [InlineData("change-me")]
    [InlineData("<API_KEY>")]
    public void ContainsPlaceholderMarker_ReturnsTrueForKnownPlaceholders(string value)
    {
        CredentialPlaceholderDetector.ContainsPlaceholderMarker(value).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContainsPlaceholderMarker_ReturnsFalseForMissingValues(string? value)
    {
        CredentialPlaceholderDetector.ContainsPlaceholderMarker(value).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TODO")]
    public void IsPlaceholderValue_TreatsMissingOrPlaceholderAsPlaceholder(string? value)
    {
        CredentialPlaceholderDetector.IsPlaceholderValue(value).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeRealCredential_ReturnsTrueOnlyForNonPlaceholderValues()
    {
        CredentialPlaceholderDetector.LooksLikeRealCredential("ak_live_1234567890").Should().BeTrue();
        CredentialPlaceholderDetector.LooksLikeRealCredential("YOUR_SECRET").Should().BeFalse();
        CredentialPlaceholderDetector.LooksLikeRealCredential("").Should().BeFalse();
    }
}
