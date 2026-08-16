using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;

namespace Meridian.Tests.Contracts.Integrity;

public sealed class Sha256DigestTests
{
    private const string LowercaseDigest =
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static string UppercaseDigest => LowercaseDigest.ToUpperInvariant();

    [Fact]
    public void Compute_EmitsCanonicalLowercaseHex()
    {
        var digest = Sha256Digest.ComputeUtf8("test");

        digest.Should().Be(LowercaseDigest);
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public void Compute_Stream_EmitsCanonicalLowercaseHexFromCurrentPosition()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("skiptest"));
        stream.Position = 4;

        var digest = Sha256Digest.Compute(stream);

        digest.Should().Be(LowercaseDigest, "only the bytes after the current position are hashed");
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAsync_EmitsCanonicalLowercaseHexMatchingCompute()
    {
        var payload = Encoding.UTF8.GetBytes("test");
        using var stream = new MemoryStream(payload);

        var digest = await Sha256Digest.ComputeAsync(stream);

        digest.Should().Be(LowercaseDigest);
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAsync_HashesFromCurrentPosition()
    {
        var payload = Encoding.UTF8.GetBytes("skiptest");
        using var stream = new MemoryStream(payload);
        stream.Position = 4;

        var digest = await Sha256Digest.ComputeAsync(stream);

        digest.Should().Be(LowercaseDigest, "only the bytes after the current position are hashed");
    }

    [Fact]
    public void Compute_MatchesFrameworkHash()
    {
        var payload = Encoding.UTF8.GetBytes("meridian");

        Sha256Digest.Compute(payload)
            .Should()
            .Be(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // non-hex
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")] // 63 chars
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a088")] // 65 chars
    public void MalformedValues_AreNeitherCanonicalNorWellFormed(string? value)
    {
        Sha256Digest.IsCanonical(value).Should().BeFalse();
        Sha256Digest.IsWellFormed(value).Should().BeFalse();
    }

    [Fact]
    public void UppercaseDigest_IsWellFormedButNotCanonical()
    {
        Sha256Digest.IsWellFormed(UppercaseDigest).Should().BeTrue();
        Sha256Digest.IsCanonical(UppercaseDigest).Should().BeFalse();
    }

    [Fact]
    public void MixedCaseDigest_IsWellFormedButNotCanonical()
    {
        var mixed = string.Concat(LowercaseDigest[..32], UppercaseDigest[32..]);

        Sha256Digest.IsWellFormed(mixed).Should().BeTrue();
        Sha256Digest.IsCanonical(mixed).Should().BeFalse();
    }

    [Fact]
    public void Normalize_RepairsNonCanonicalCasing()
    {
        Sha256Digest.Normalize(UppercaseDigest).Should().Be(LowercaseDigest);
        Sha256Digest.Normalize(LowercaseDigest).Should().Be(LowercaseDigest);
    }

    [Fact]
    public void Normalize_ReturnsNullForMalformedInput() =>
        Sha256Digest.Normalize("not-a-digest").Should().BeNull();

    // The regression this whole primitive exists for: before consolidation, a retained uppercase
    // digest verified under the permissive reporting-side check but was reported as a hash mismatch
    // by the lowercase-only store-side check — a phantom tamper alert on intact bytes.
    [Fact]
    public void Compare_TreatsCasingDifferenceAsMatch_NotTamper()
    {
        Sha256Digest.Compare(UppercaseDigest, LowercaseDigest)
            .Should()
            .Be(Sha256DigestComparison.Match);

        Sha256Digest.FixedEquals(UppercaseDigest, LowercaseDigest).Should().BeTrue();
        Sha256Digest.FixedEquals(LowercaseDigest, UppercaseDigest).Should().BeTrue();
    }

    [Fact]
    public void Compare_ReportsGenuineMismatch()
    {
        var other = Sha256Digest.ComputeUtf8("different");

        Sha256Digest.Compare(LowercaseDigest, other).Should().Be(Sha256DigestComparison.Mismatch);
        Sha256Digest.FixedEquals(LowercaseDigest, other).Should().BeFalse();
    }

    [Theory]
    [InlineData("bad", LowercaseDigest, Sha256DigestComparison.MalformedLeft)]
    [InlineData(LowercaseDigest, "bad", Sha256DigestComparison.MalformedRight)]
    [InlineData("bad", "worse", Sha256DigestComparison.MalformedBoth)]
    [InlineData(null, LowercaseDigest, Sha256DigestComparison.MalformedLeft)]
    [InlineData(LowercaseDigest, null, Sha256DigestComparison.MalformedRight)]
    public void Compare_SeparatesMalformedInputFromMismatch(
        string? left,
        string? right,
        Sha256DigestComparison expected)
    {
        Sha256Digest.Compare(left, right).Should().Be(expected);

        // FixedEquals still collapses to false, but callers can now tell the two apart.
        Sha256Digest.FixedEquals(left, right).Should().BeFalse();
    }

    [Fact]
    public void Compare_IsReflexiveForCanonicalDigest() =>
        Sha256Digest.Compare(LowercaseDigest, LowercaseDigest)
            .Should()
            .Be(Sha256DigestComparison.Match);

    [Fact]
    public void ComputeUtf8_RejectsNull()
    {
        var act = () => Sha256Digest.ComputeUtf8(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
