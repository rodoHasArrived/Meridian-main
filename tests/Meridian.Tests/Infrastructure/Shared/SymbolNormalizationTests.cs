using FluentAssertions;
using Meridian.Infrastructure.Utilities;
using Xunit;

namespace Meridian.Tests.Infrastructure;

/// <summary>
/// Unit tests for the SymbolNormalization utility class.
/// Ensures consistent symbol handling across all data providers.
/// </summary>
public sealed class SymbolNormalizationTests
{
    #region Normalize Tests

    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("AAPL", "AAPL")]
    [InlineData("Aapl", "AAPL")]
    [InlineData("  aapl  ", "AAPL")]
    [InlineData("msft", "MSFT")]
    [InlineData("brk.a", "BRK.A")]
    [InlineData("BRK.B", "BRK.B")]
    public void Normalize_ShouldUppercaseAndTrim(string input, string expected)
    {
        // Act
        var result = SymbolNormalization.Normalize(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ShouldThrowOnInvalidInput(string? input)
    {
        // Act & Assert
        var action = () => SymbolNormalization.Normalize(input!);
        action.Should().Throw<ArgumentException>();
    }

    #endregion

    #region NormalizeForTiingo Tests

    [Theory]
    [InlineData("brk.a", "BRK-A")]
    [InlineData("BRK.B", "BRK-B")]
    [InlineData("aapl", "AAPL")]
    [InlineData("  brk.a  ", "BRK-A")]
    public void NormalizeForTiingo_ShouldReplaceDots(string input, string expected)
    {
        // Act
        var result = SymbolNormalization.NormalizeForTiingo(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeForYahoo Tests

    [Fact]
    public void NormalizeForYahoo_WithoutSuffix_ShouldJustNormalize()
    {
        // Act
        var result = SymbolNormalization.NormalizeForYahoo("aapl");

        // Assert
        result.Should().Be("AAPL");
    }

    [Fact]
    public void NormalizeForYahoo_WithSuffix_ShouldAddSuffix()
    {
        // Act
        var result = SymbolNormalization.NormalizeForYahoo("vod", "L");

        // Assert
        result.Should().Be("VOD.L");
    }

    [Fact]
    public void NormalizeForYahoo_WithSuffixStartingWithDot_ShouldNotDoubleDot()
    {
        // Act
        var result = SymbolNormalization.NormalizeForYahoo("vod", ".L");

        // Assert
        result.Should().Be("VOD.L");
    }

    [Fact]
    public void NormalizeForYahoo_SymbolAlreadyHasSuffix_ShouldNotAddAnother()
    {
        // Act
        var result = SymbolNormalization.NormalizeForYahoo("vod.L", "T");

        // Assert
        result.Should().Be("VOD.L"); // Original suffix preserved
    }

    [Theory]
    [InlineData("7203", "T", "7203.T")]
    [InlineData("bp", "L", "BP.L")]
    public void NormalizeForYahoo_InternationalSymbols(string symbol, string suffix, string expected)
    {
        // Act
        var result = SymbolNormalization.NormalizeForYahoo(symbol, suffix);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeForStooq Tests

    [Theory]
    [InlineData("AAPL", "aapl")]
    [InlineData("aapl", "aapl")]
    [InlineData("BRK.A", "brk-a")]
    [InlineData("  MSFT  ", "msft")]
    public void NormalizeForStooq_ShouldLowercaseAndReplaceDots(string input, string expected)
    {
        // Act
        var result = SymbolNormalization.NormalizeForStooq(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeForNasdaqDataLink Tests

    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("brk.a", "BRK_A")]
    [InlineData("BRK.B", "BRK_B")]
    [InlineData("  msft  ", "MSFT")]
    public void NormalizeForNasdaqDataLink_ShouldUppercaseAndReplaceDotsWithUnderscores(string input, string expected)
    {
        // Act
        var result = SymbolNormalization.NormalizeForNasdaqDataLink(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeForOpenFigi Tests

    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("brk.a", "BRK")]
    [InlineData("MSFT.US", "MSFT")]
    [InlineData("  googl  ", "GOOGL")]
    public void NormalizeForOpenFigi_ShouldRemoveSuffixes(string input, string expected)
    {
        // Act
        var result = SymbolNormalization.NormalizeForOpenFigi(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region IsValidSymbol Tests

    [Theory]
    [InlineData("AAPL", true)]
    [InlineData("BRK.A", true)]
    [InlineData("BRK-B", true)]
    [InlineData("SPY_US", true)]
    [InlineData("GOOGL", true)]
    [InlineData("A", true)]
    [InlineData("1234", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void IsValidSymbol_ShouldValidateCorrectly(string? input, bool expected)
    {
        // Act
        var result = SymbolNormalization.IsValidSymbol(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("AAP$L")]
    [InlineData("AAPL@")]
    [InlineData("AAP#L")]
    [InlineData("AAPL!")]
    public void IsValidSymbol_WithSpecialCharacters_ShouldReturnFalse(string input)
    {
        // Act
        var result = SymbolNormalization.IsValidSymbol(input);

        // Assert
        result.Should().BeFalse($"symbol '{input}' contains invalid special characters");
    }

    #endregion

    #region NormalizeMany Tests

    [Fact]
    public void NormalizeMany_ShouldNormalizeAllSymbols()
    {
        // Arrange
        var symbols = new[] { "aapl", "MSFT", "googl" };

        // Act
        var result = SymbolNormalization.NormalizeMany(symbols);

        // Assert
        result.Should().BeEquivalentTo(new[] { "AAPL", "MSFT", "GOOGL" });
    }

    [Fact]
    public void NormalizeMany_ShouldRemoveDuplicates()
    {
        // Arrange
        var symbols = new[] { "aapl", "AAPL", "Aapl", "msft" };

        // Act
        var result = SymbolNormalization.NormalizeMany(symbols);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("AAPL");
        result.Should().Contain("MSFT");
    }

    [Fact]
    public void NormalizeMany_ShouldSkipEmptyStrings()
    {
        // Arrange
        var symbols = new[] { "aapl", "", "msft", "   ", "googl" };

        // Act
        var result = SymbolNormalization.NormalizeMany(symbols);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(new[] { "AAPL", "MSFT", "GOOGL" });
    }

    [Fact]
    public void NormalizeMany_WithEmptyCollection_ShouldReturnEmpty()
    {
        // Arrange
        var symbols = Array.Empty<string>();

        // Act
        var result = SymbolNormalization.NormalizeMany(symbols);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeMany_WithNull_ShouldThrow()
    {
        // Act & Assert
        var action = () => SymbolNormalization.NormalizeMany(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("A")]
    [InlineData("Z")]
    [InlineData("AA")]
    [InlineData("AAAAAAAAAAAAAAA")] // Very long symbol
    public void Normalize_EdgeCaseLengths_ShouldWork(string input)
    {
        // Act
        var result = SymbolNormalization.Normalize(input);

        // Assert
        result.Should().Be(input.ToUpperInvariant());
    }

    [Fact]
    public void AllNormalizationMethods_ShouldHandleMixedCase()
    {
        // Arrange
        const string mixedCase = "BrK.a";

        // Act & Assert
        SymbolNormalization.Normalize(mixedCase).Should().Be("BRK.A");
        SymbolNormalization.NormalizeForTiingo(mixedCase).Should().Be("BRK-A");
        SymbolNormalization.NormalizeForStooq(mixedCase).Should().Be("brk-a");
        SymbolNormalization.NormalizeForNasdaqDataLink(mixedCase).Should().Be("BRK_A");
        SymbolNormalization.NormalizeForOpenFigi(mixedCase).Should().Be("BRK");
    }

    #endregion
}
