using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Format-validation and normalization coverage for the identifier kinds that previously had no
/// (or check-digit-free) validation: LEI (ISO 17442 mod 97-10), FIGI (structure + mod-10 check
/// digit), and RIC (lightweight structural guard).
/// </summary>
public sealed class SecurityIdentifierNormalizerTests
{
    [Theory]
    [InlineData("HWUPKR0MPOU8FGXBT394")] // Apple Inc. LEI (valid ISO 7064 mod 97-10 checksum).
    [InlineData("hwupkr0mpou8fgxbt394")] // Case-insensitive: normalized to upper before validation.
    public void TryValidateFormat_ValidLei_Succeeds(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Lei, value, out var message);

        isValid.Should().BeTrue();
        message.Should().BeNull();
    }

    [Theory]
    [InlineData("HWUPKR0MPOU8FGXBT395")] // Wrong check digits (mod-97 != 1).
    [InlineData("HWUPKR0MPOU8FGXBT39")]  // Too short.
    [InlineData("HWUPKR0MPOU8FGXBT3940")] // Too long.
    public void TryValidateFormat_InvalidLei_FailsWithCheckDigitMessage(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Lei, value, out var message);

        isValid.Should().BeFalse();
        message.Should().Contain("LEI");
    }

    [Theory]
    [InlineData("BBG000B9XRY4")] // Apple Inc. FIGI.
    [InlineData("BBG000B9Y5X2")] // Apple composite FIGI.
    [InlineData("BBG001S5N8V8")] // Apple share-class FIGI.
    public void TryValidateFormat_ValidFigi_Succeeds(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Figi, value, out var message);

        isValid.Should().BeTrue();
        message.Should().BeNull();
    }

    [Theory]
    [InlineData("BBG000B9XRY5")] // Wrong check digit.
    [InlineData("BBB000B9XRY4")] // No 'G' in position 3.
    [InlineData("BBG0A0B9XRY4")] // Contains a vowel.
    [InlineData("BBG000B9XRY")]  // Too short.
    public void TryValidateFormat_InvalidFigi_FailsWithFigiMessage(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Figi, value, out var message);

        isValid.Should().BeFalse();
        message.Should().Contain("FIGI");
    }

    [Theory]
    [InlineData("AAPL.O")]
    [InlineData("EUR=")]
    [InlineData("GBP=D2")]
    [InlineData("0#.FTSE")]
    public void TryValidateFormat_ValidRic_Succeeds(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Ric, value, out var message);

        isValid.Should().BeTrue();
        message.Should().BeNull();
    }

    [Theory]
    [InlineData("AAPL O")]   // Embedded whitespace.
    [InlineData("AAPL$O")]   // Illegal character.
    [InlineData("....")]     // No alphanumeric content.
    public void TryValidateFormat_InvalidRic_Fails(string value)
    {
        var isValid = SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Ric, value, out var message);

        isValid.Should().BeFalse();
        message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NormalizeValue_Figi_StripsPunctuationAndUppercases()
        => SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.Figi, " bbg-000-b9xry4 ")
            .Should().Be("BBG000B9XRY4");

    [Fact]
    public void NormalizeValue_Ric_PreservesPunctuationAndUppercases()
        => SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.Ric, " aapl.o ")
            .Should().Be("AAPL.O");
}
