using FluentAssertions;
using Meridian.Contracts.Text;
using Xunit;

namespace Meridian.Tests.Contracts.Text;

/// <summary>
/// Pins the semantics the scattered private copies already shared, so a future edit to the shared
/// helper cannot quietly change what several hundred call sites mean.
/// </summary>
public sealed class TextPrimitivesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \t\r\n ")]
    public void NormalizeOptional_BlankInput_ReturnsNull(string? value)
        => TextPrimitives.NormalizeOptional(value).Should().BeNull();

    [Theory]
    [InlineData("value", "value")]
    [InlineData("  value  ", "value")]
    [InlineData("\tvalue\n", "value")]
    [InlineData("two words", "two words")]
    [InlineData("  two  words  ", "two  words")]
    public void NormalizeOptional_TrimsOuterWhitespaceOnly(string value, string expected)
        => TextPrimitives.NormalizeOptional(value).Should().Be(expected);

    /// <summary>
    /// The regression that motivated consolidating this helper: two former copies shared this exact
    /// name and signature but also case-folded, so the same call meant different things depending
    /// on which file it appeared in. The shared helper preserves case, and callers that need
    /// folding now say so.
    /// </summary>
    [Theory]
    [InlineData("MixedCase")]
    [InlineData("UPPER")]
    [InlineData("lower")]
    [InlineData("  MixedCase  ")]
    public void NormalizeOptional_PreservesCase(string value)
        => TextPrimitives.NormalizeOptional(value).Should().Be(value.Trim());

    [Theory]
    [InlineData("value", "value")]
    [InlineData("  value  ", "value")]
    public void RequireText_PresentInput_ReturnsTrimmed(string value, string expected)
        => TextPrimitives.RequireText(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireText_BlankInput_Throws(string? value)
    {
        var act = () => TextPrimitives.RequireText(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Value must be non-empty text.*");
    }

    [Fact]
    public void RequireText_BlankInput_NamesTheCallersExpression()
    {
        string? candidateIdentifier = "   ";

        var act = () => TextPrimitives.RequireText(candidateIdentifier);

        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be(nameof(candidateIdentifier));
    }

    [Fact]
    public void FirstNonBlank_ReturnsFirstPresentValueTrimmed()
        => TextPrimitives.FirstNonBlank(null, "", "   ", "  chosen  ", "later").Should().Be("chosen");

    [Fact]
    public void FirstNonBlank_AllBlank_ReturnsNull()
        => TextPrimitives.FirstNonBlank(null, "", "   ").Should().BeNull();

    [Fact]
    public void FirstNonBlank_NoValues_ReturnsNull()
        => TextPrimitives.FirstNonBlank().Should().BeNull();
}
