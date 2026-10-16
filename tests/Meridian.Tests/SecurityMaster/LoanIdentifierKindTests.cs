using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class LoanIdentifierKindTests
{
    [Theory]
    [InlineData("LX123456", true)]
    [InlineData(" lx-123456 ", true)]
    [InlineData("LX12345", false)]
    [InlineData("LXA23456", false)]
    [InlineData("AB123456", false)]
    public void LoanX_RequiresLxAndSixDigits(string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.LoanX, value, out var message).Should().Be(valid, message);
    }

    [Theory]
    [InlineData("12345ABC9", true)]
    [InlineData("12345-abc9", true)]
    [InlineData("12345ABC", false)]
    public void Clip_RequiresNineAlphanumericCharacters(string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Clip, value, out var message).Should().Be(valid, message);
    }

    [Fact]
    public void LoanIdentifiers_NormalizeToTheirCompactUpperCaseForm()
    {
        SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.LoanX, " lx-123456 ").Should().Be("LX123456");
        SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.Clip, "12345-abc9").Should().Be("12345ABC9");
    }

    [Fact]
    public void LoanClasses_PreferTheLoanIdentifiers()
    {
        SecurityAssetClassCatalog.GetPreferredIdentifierKinds("DirectLoan")
            .Should().Contain([SecurityIdentifierKind.LoanX, SecurityIdentifierKind.Clip]);
        SecurityAssetClassCatalog.GetPreferredIdentifierKinds("StructuredCredit")
            .Should().Contain(SecurityIdentifierKind.Clip);
    }
}
