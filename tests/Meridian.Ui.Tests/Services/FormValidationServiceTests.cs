using FluentAssertions;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="FormValidationRules"/> shared validation logic.
/// </summary>
public sealed class FormValidationServiceTests
{
    [Theory]
    [InlineData("SPY", true)]
    [InlineData("AAPL", true)]
    [InlineData("MSFT", true)]
    [InlineData("TSLA", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("SP Y", false)]
    [InlineData("123", false)]
    [InlineData("A", true)] // Single letter symbols are valid (e.g., X, F)
    public void ValidateSymbol_ValidatesSymbolFormat(string? symbol, bool expectedValid)
    {
        // Act
        var result = FormValidationRules.ValidateSymbol(symbol);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Message.Should().NotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData("2024-01-01", true)]
    [InlineData("2024-12-31", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("2024/01/01", false)] // Wrong format
    public void ValidateDateRange_ValidatesDateFormat(string? dateStr, bool expectedValid)
    {
        // Act
        var result = FormValidationRules.ValidateDateRange(dateStr);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Message.Should().NotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData("config.json", true)]
    [InlineData("C:\\data\\config.json", true)]
    [InlineData("/var/data/config.json", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateFilePath_ValidatesPathFormat(string? path, bool expectedValid)
    {
        // Act
        var result = FormValidationRules.ValidateFilePath(path);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Message.Should().NotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData(8080, true)]
    [InlineData(80, true)]
    [InlineData(443, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)] // Port 0 is reserved
    [InlineData(65536, false)] // Above max port
    [InlineData(-1, false)]
    [InlineData(null, false)]
    public void ValidatePort_ValidatesPortNumber(int? port, bool expectedValid)
    {
        // Act
        var result = FormValidationRules.ValidatePort(port);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Message.Should().NotBeNullOrEmpty();
        }
    }
}
