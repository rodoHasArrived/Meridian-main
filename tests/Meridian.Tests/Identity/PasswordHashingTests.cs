using FluentAssertions;
using Meridian.Identity;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: local Meridian accounts are hash-backed, so the password hashing helper
/// must round-trip correct credentials, reject wrong ones in constant time, and refuse malformed
/// or unsupported hash material rather than silently accepting it.
/// </summary>
public sealed class PasswordHashingTests
{
    [Fact]
    public void HashPassword_ProducesSupportedPbkdf2Format()
    {
        var hash = PasswordHashing.HashPassword("correct horse battery staple");

        hash.Should().StartWith($"{PasswordHashing.Algorithm}$");
        PasswordHashing.IsSupportedHash(hash).Should().BeTrue();
        hash.Split('$').Should().HaveCount(4);
    }

    [Fact]
    public void HashPassword_UsesRandomSaltSoTwoHashesDiffer()
    {
        var first = PasswordHashing.HashPassword("same-password");
        var second = PasswordHashing.HashPassword("same-password");

        first.Should().NotBe(second, "each hash must use a fresh random salt");
        PasswordHashing.VerifyPassword("same-password", first).Should().BeTrue();
        PasswordHashing.VerifyPassword("same-password", second).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHashing.HashPassword("s3cret-value");

        PasswordHashing.VerifyPassword("s3cret-value", hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        var hash = PasswordHashing.HashPassword("s3cret-value");

        PasswordHashing.VerifyPassword("s3cret-valuE", hash).Should().BeFalse();
        PasswordHashing.VerifyPassword("totally-different", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]      // non-positive iterations
    [InlineData("bcrypt$210000$c2FsdA==$aGFzaA==")]         // unsupported algorithm
    [InlineData("pbkdf2-sha256$210000$not-base64$aGFzaA==")] // malformed salt
    public void VerifyPassword_WithMalformedHash_ReturnsFalse(string malformedHash)
    {
        PasswordHashing.VerifyPassword("any-password", malformedHash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithEmptyPassword_ReturnsFalse()
    {
        var hash = PasswordHashing.HashPassword("s3cret-value");

        PasswordHashing.VerifyPassword(string.Empty, hash).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bcrypt$210000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$abc$c2FsdA==$aGFzaA==")] // non-numeric iterations
    public void IsSupportedHash_RejectsUnsupportedOrMalformedMaterial(string? candidate)
    {
        PasswordHashing.IsSupportedHash(candidate).Should().BeFalse();
    }
}
